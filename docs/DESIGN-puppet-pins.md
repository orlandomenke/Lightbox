# Puppet pins — design and plan

*Status: designed 2026-10-10, **not built**. This branch carries the plan, the
decision record (Q239), the API surface as stubs that throw, and the tests —
red on purpose. Nothing here has been compiled or run yet: the machine was
busy with a performance-lab run when it was written, so the first build and
the first red run are step 0 below.*

The request (owner, 2026-10-10): After Effects' Puppet tool — a mesh that
deforms the drawing and can be shown as simple lines, pins that act as joints,
and options that shape the motion: arc, fluidity, squash and stretch. Wanted
performant, with the performance lab part of the tests.

## What the investigation found

**Most of a puppet system is already here, built on bones.** `docs/DESIGN-bones.md`
landed in full: hierarchy, forward and inverse kinematics, constraints, spline
chains, per-point skinning with weight painting, angle-driven correctives,
jiggle, stepped timing, bake, and export to Godot and DragonBones. The cage
warp (Q199) is a one-shot lattice deformer on the Transform tool.

| After Effects Puppet | Lightbox today | Gap |
| --- | --- | --- |
| Drop pins, drag | Build a skeleton, bind, correct weights | **Immediacy** |
| Mesh follows the silhouette | Auto-bind and the cage measure straight-line distance | **Shape awareness** — an arm handle pulls a nearby leg |
| Mesh shown, density adjustable | No mesh; a cage lattice that is gone on apply | **A visible mesh** |
| Starch pins | Weight painting | A one-click equivalent |
| Keyframed pins | Pose track, easing, stepping on 2s | None — reuse it |
| *(not in After Effects)* squash and stretch | Bone pose is rotation + translation only | **No scale anywhere in the rig** |
| *(not in After Effects)* arc | Bones arc because they rotate; `MotionArc` overlay | Pins would move in straight lines between keys |
| *(not in After Effects)* fluidity | `BoneJiggle` spring | Exists per bone; not a named motion control |

Two things the investigation did **not** establish, and which are therefore
steps rather than assumptions:

- **What posing costs today.** The lab had no rig scenario and the app writes
  no pose span, so there was no baseline. `BoneRigPerformanceTests` and
  `perf/scenarios/pose-drag.json` are that baseline, written here.
- **Whether a silhouette mesh is stable across neighbouring drawings** of a
  frame-by-frame layer. See *Open risks*.

## Decisions (Q239)

1. **Pins live inside the rig** — a second kind of handle in the same
   `Armature` and the same pose track.
2. **Pins and mesh first; motion options second.**
3. **Motion options reach bones and pins alike**, by one mechanism.

## The design

### The idea that carries over from bones

A pin never warps a picture. The mesh moves **stroke points**; `BrushEngine`
re-stamps the mark along the moved path, and every dab seeds from the rest
path (`Stroke.RestPoints`), so line weight survives and a pinned drawing does
not boil. Imported pixels are the one thing drawn *through* the mesh, by the
`PassMesh` the cage already uses. The mesh is construction, like the armature.

### The mesh — `PinMesh` (Core)

A regular triangle grid clipped to the drawing's silhouette:

1. Lay a grid of `Cell` units over the drawing's bounds.
2. Mark every cell a stroke's ink reaches (control points and their brush
   radius; a fill's contours mark what they enclose).
3. Flood the unmarked cells from the border. What the flood cannot reach is
   *inside*.
4. Keep marked and inside cells; two triangles each.

Why this and not a triangulated outline (what After Effects does): line art
has no filled silhouette to trace; a gap narrower than a cell closes itself,
so there is no Expansion slider to tune; it needs no constrained Delaunay
triangulation, which is the largest piece of new geometry the alternative
would add; and regularity is what makes the solve banded and a point's
triangle a division instead of a search. What it gives up is a smooth mesh
boundary, which does not matter when the mesh carries points rather than
texture.

- **Derived, never stored**: a pure function of strokes and cell size. The
  record holds the cell size and the pins.
- **Capped**: `MaxVertices` (4,000). A drawing too large for its cell gets a
  coarser grid, never a larger solve.
- **Shown as lines**: `Outline` is the silhouette edges; the overlay draws
  either that or the full mesh.

### The solve — `PinSolve` (Core)

As-rigid-as-possible shape manipulation (Igarashi, Moscovich, Hughes 2005 —
published, and already cleared by the licensing section of the bones design).

- **`Prepare(mesh, pins)`** — once, when pins are placed or the mesh changes:
  assemble and factorise (banded Cholesky, fixed elimination order).
- **`Solve(targets, deformed)`** — per pointer event and per rendered frame:
  back-substitution, a fixed number of local/global rounds, into the caller's
  buffer, allocating nothing.
- Position pins are hard constraints; a starch pin raises rigidity within its
  extent and holds no position.
- A piece of the mesh with no position pin is left out and stays at rest.
- No pin moved → rest positions copied, bit-exact, no solve.

### The record

| Key | Shape | Absent when |
| --- | --- | --- |
| `Armature.Pins?` | id, name, rest x/y, kind, starch extent, optional parent bone | the rig has no pins |
| `Armature.PinCell?` | mesh cell size | default |
| `PoseKey.Bones[pinId]` | the existing `BonePose` — x/y offset from rest | the pin is at rest on that key |

Pin poses reuse the pose track's keys, which is what makes easing, `Hold`,
stepping on 2s, the timeline rows and key editing work for pins without new
code. A pin with a parent bone rides it, so pins and bones mix on one figure.
A layer follows pins the way it follows the whole skeleton today (`Layer.BoneId`).

### Where it shows up

| Surface | What |
| --- | --- |
| Bone tool (**K**) | a fourth mode, **Pins**, beside Bind / Pose / Weights; click drops a pin, drag moves it and keys at the playhead |
| `ShortcutMap` | `armature.pinMode`, Ctrl+Alt+K (free today) |
| Tool options | pin list, kind (Position / Starch), starch extent, mesh density, show mesh: off / outline / full |
| Bake, onion skin, export | through `PoseFrameForRender` — pins are solved to a posed frame like bones |
| MCP | list / add / move pins, beside the existing rig surface (G12 pair on that diff) |
| Manual | `docs/manual/14-bones-and-rigging.md`, a *Pins* section; the opening paragraph's contrast with "puppet tools" is rewritten |

### Phase 2 — motion options (after phase 1 lands; its own branches)

- **Squash and stretch**: scale on a handle, volume-preserving by default
  (stretch along × squash across = 1). For bones this adds scale to
  `BonePose`, the solver and both exporters. For pins it is the blend between
  the solve's similarity step (free to scale) and its rigid step.
- **Arc**: pin keys interpolate along a curve through the keys rather than a
  straight chord; `MotionArc` already draws the path.
- **Fluidity**: to be defined with the owner (Q239, left open) — the jiggle
  spring generalised to pins is the candidate.

## Performance

**Where the time goes.** A posed frame is: evaluate the pose → solve → move
every bound stroke's points → re-stamp every bound stroke. The first three are
geometry and are budgeted in Core tests; the fourth is the brush engine and is
what the lab measures. The expectation, to be confirmed by the baseline, is
that re-stamping dominates and that pins and bones therefore cost about the
same on screen.

**Budgets written as tests** (`Category=Performance`, minimum of several runs,
absolutes printed, nothing rendered):

| Budget | Rules out |
| --- | --- |
| Largest mesh solves inside one refresh (16.7 ms) | a dense solve |
| Default mesh solves in a tenth of a refresh | — |
| A drag costs under a quarter of `Prepare` | refactorising per pointer event |
| `Solve` into the caller's buffer allocates 0 bytes | garbage on the UI thread |
| 20,000 points mapped inside one refresh | a linear triangle search |
| Re-meshing a drawing under 100 ms, vertex cap held at 4K | an unbounded mesh |
| Posing strokes through pins ≤ 3× posing through a bone | pins being the slow way to rig |

**The bone baseline** (`BoneRigPerformanceTests`) guards what exists: pose
evaluation and solve for 30 bones, posing a 100-stroke drawing's geometry,
scaling with stroke count, zero cost on an unbound frame, the jiggle
overhead, and auto-bind. Its ceilings were written before a first run and are
labelled so; the first run decides which are guards and which are findings.

**The lab.** Two scenarios that make the *same* wrist gesture, so they are one
comparison:

- `pose-drag` — existing bones. Needs a `rigged` fixture and three spans
  (`pose.drag`, `pose.frame`, `pose.commit`).
- `pin-drag` — pins. Needs a `pinned` fixture and `pin.drag`, `pin.solve`,
  `pin.frame`, `pin.commit`, `pin.prepare`.

`RigLabScenarioTests` fails until those fixtures and spans exist, so a
scenario cannot quietly measure nothing.

**Acceptance for phase 1:** `lab.py ab`-style comparison of `pin-drag` against
`pose-drag` on the same build — no metric more than 15% above its bone
counterpart, and no stall the bone scenario does not have. Judged on medians
of at least three runs each, machine quiet, per `perf/README.md`.

**Degrade, if the baseline shows re-stamping does not fit a drag:** the route
bones already have — expressive brushes ghost as centrelines during the drag
and land exactly on release (Q81 decision 5). Pins inherit it by going through
`PoseFrameForRender`.

## Tests

Written on this branch, red until built:

| File | Covers |
| --- | --- |
| `PinMeshTests` (13) | inside of an outline; small gaps; separate drawings; fork prongs; winding; cell clamp; vertex cap; outline; embedding; reach past the silhouette; bit-identical rebuild; empty; record untouched |
| `PinSolveTests` (13) | identity to the bit; pins land exactly; one pin translates; rigid motions reproduced; bend keeps area and folds nothing; one prong does not pull the other; unpinned piece stays; starch; starch target ignored; NaN targets; no pins; wrong target count; determinism |
| `PinSkinningTests` (3) | record untouched and rest path carried; unmoved mesh hands the stroke back; a two-point line bends |
| `PinSolvePerformanceTests` (7) | the budgets above |
| `RigLabScenarioTests` (4) | fixtures exist; measured spans are written; both scenarios make the same gesture; the shortcut is registered |

Written on this branch against existing code (expected to run now):

| File | Covers |
| --- | --- |
| `BoneRigPerformanceTests` (6) | the bone baseline |

**Not written here, and why:** the record tests (`APinlessRigWritesNoPinsKey`,
`PinsRoundTrip`, `CloningARigCopiesItsPins`, `APinKeyIsAnOrdinaryPoseKey`) and
the App tests (tool mode, overlay, undo, bake, MCP). They need `Armature.Pins`
to exist to compile, and `Armature.cs` is a sensitive record file this branch
deliberately does not touch. They are the first commit of step 2 — red first,
there.

**Thresholds are provisional.** Each geometric limit (a tenth of the drag, a
residual of 3, half the starch residual) is a first statement of intent. Per
house practice they are re-set by breaking the solve on purpose — translation
only, similarity step only, straight-line weights — and placing each limit
between what the right answer scores and what the wrong ones do.

## Steps

| # | Step | Branch | Cost |
| --- | --- | --- | --- |
| 0 | Build this branch; confirm the pin tests are red for the right reason (`NotImplementedException`) and record what `BoneRigPerformanceTests` measures | this one | S |
| 1 | Bone baseline in the lab: `rigged` fixture in `Lightbox.Bench`, `pose.*` spans, run `pose-drag`. **Spike:** mesh a real frame-by-frame layer drawing by drawing and measure how much the mesh changes | `perf/lab/rig-baseline` | S–M |
| 2 | `PinMesh` + `PinSolve` + `PinSkinning` in Core to green; record (`Armature.Pins`), optional-absent, with its tests first; sensitivity review | `feat/puppet-pins-core` | L |
| 3 | Render path: pins through `PoseFrameForRender`, imported pixels through `PassMesh`, bake, `pin.*` spans, `pinned` fixture, run `pin-drag` against the acceptance line | `feat/puppet-pins-render` | M |
| 4 | The tool: Pins mode, overlay (outline / full mesh), options, shortcut, undo, manual, workspace defaults | `feat/ui/puppet-pins-tool` | M–L |
| 5 | MCP surface and rig export for pins | separate | S–M |
| 6 | Phase 2: squash and stretch, arc, fluidity — each its own question and branch | separate | L |

## Open risks

1. **Mesh stability across drawings.** Bones bind a *layer*, covering drawings
   not yet made. A silhouette mesh differs per drawing; a pin dropped at one
   place may be inside the arm on frame 3 and outside it on frame 4. The plan
   assumes a mesh derived per drawing with pins snapping to the nearest
   triangle. Step 1's spike measures it; if it reads badly this becomes a
   question for the owner (freeze the mesh at pin time, as After Effects does).
2. **Open line art.** A gap wider than a cell drains the shape. Fallback:
   coarsen until it closes, and say so in the overlay. Untested on real art.
3. **Determinism of the factorisation across runtimes.** Plain double
   arithmetic in a fixed order should be bit-stable, as the brush engine's is
   (`RuntimeDeterminismTests`); a fingerprint test belongs in step 2.
4. **Bones gaining scale** (phase 2) touches the pose record, IK, both
   exporters and the no-boil rule's seeding space. Larger than its one line in
   the decision table suggests.
5. **Re-stamp cost is unmeasured.** Everything above about on-screen speed is
   an expectation until step 1 produces numbers.
