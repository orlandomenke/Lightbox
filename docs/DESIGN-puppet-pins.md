# Puppet pins — design and plan

*Status: designed 2026-10-10; **the geometry is built** (2026-10-11) —
`PinMesh`, `PinSolve` and `PinSkinning` in Core — **and so is the record**:
`Armature.Pins`, read for the solve by `PinOps`. All tests green. Nothing an
artist can reach yet: no render path, no tool. Decisions are in
Q239. The bone rig's baseline and the lab's `pose-drag` scenario landed
separately (B442, and the lab branch); what they found is under* Performance.*

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
2. Mark every cell a stroke's path passes through — the path, not the
   brush's width, so a wide brush cannot join two limbs drawn apart. A
   fill's contours are closed and mark what they enclose.
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

The paper's two steps, as built:

1. **Similarity.** Every triangle tries to stay similar to itself — free to
   turn and scale, not to shear. Quadratic in the vertices, so with the pins
   held it is one linear solve, and it reproduces any turn of the pins
   exactly, a half turn included. Because the energy has no force of its own,
   the answer is linear in the pins' displacement: `Prepare` works out that
   matrix once and a drag only multiplies by it.
2. **Rigid fit.** Each rest triangle is fitted rigidly onto its step-one
   self, and the mesh is solved once more so that every edge is as near its
   fitted one as the pins allow — one banded back-substitution per axis.

- **`Prepare(mesh, pins)`** — once, when pins are placed or the mesh changes:
  two banded Cholesky factorisations in a fixed order.
- **`Solve(targets, deformed)`** — per pointer event and per rendered frame,
  into the caller's buffer, allocating nothing. Exactly two steps; no
  iteration and no tolerance.
- **Pins are exact.** A pin anywhere in a triangle is a linear constraint on
  its corners, enforced through the constraints' own small system rather
  than a stiff spring, so a pin lands on its target to rounding.
- A starch pin makes the triangles within its extent up to sixty times
  stiffer in both steps and holds no position.
- A piece of the mesh with no position pin stays at rest; a piece with one is
  carried by it without bending.
- No pin moved → rest positions copied, bit-exact, no solve.

**What the solve showed about pins, and it is After Effects' behaviour too:**
a pin is a point, a point is a pivot, and a body held at two points flexes
between them like a beam. On the test fork — two prongs on a base — dragging
one prong's tip 80 units moved the *other* prong's tip 28 with two pins on the
base, which is no better than a blend that knows nothing about shape (also
28). With a third base pin it moved 7.0 against the blend's 21.7; with a pin
at the far prong's root 3.5; with starch over the base 11.2. So the mesh's
knowledge of the silhouette pays off once the body is held, and the tool
should make holding it easy — starch as a one-click "this part is solid" is
not an extra.

### The record

| Key | Shape | Absent when |
| --- | --- | --- |
| `Armature.Pins?` | a list of `Pin`: id, name, rest x/y | the rig has no pins |
| `Pin.Kind?`, `Pin.Extent?` | `starch` and its reach | an ordinary position pin |
| `Pin.ParentId?` | the bone the pin rides | it rides none |
| `Armature.PinCell?` | mesh cell size | the default cell |
| `PoseKey.Bones[pinId]` | the existing `BonePose` — x/y offset from rest, in its bone's frame if it has one | the pin is at rest on that key |

**As built (2026-10-11).** An ordinary pin writes four keys — id, name, x, y —
and a rig without pins writes none. `PinOps.HandlesOf` and `PinOps.TargetsAt`
read the record for the solve; an unposed puppet's targets are its rest
positions to the last bit. Pins are copied with the rig (and so with every
undo snapshot), rescaled with the image on every rig in the document, and
scaled and moved when a rig set is fitted to another drawing. Keys a newer
build writes on a pin survive a save here (Q230).

**What the record does not do yet, on purpose:**

- A rig of pins and no bones round-trips, but most of the application asks
  "is there a rig?" as "are there bones?" (`Doc.HasArmature`, the render
  funnel, some twenty guards in the view model). Those change with the render
  path and the tool, where there is something to see when they do.
- Rig export (own JSON, Godot, DragonBones) ignores pins. A mesh deformer has
  no place in a bone format; the baked frames carry what pins did.
- Fitting a rig set measures the bones' bounds; a set of pins alone is placed
  as it stands.

**Debts the record leaves for the branches that make pins reachable** (from
the sensitivity review of the record, 2026-10-11 — none can happen while
nothing can author a pin, and each must land with the branch named):

| Debt | Where | Lands with |
| --- | --- | --- |
| Deleting a bone, an IK target or a spline handle leaves a pin's `ParentId` naming it. The pin then reads as unparented and its keys, authored in the bone's frame, are read in document axes: it jumps on every keyed frame. Clear or re-parent in the same undo step and rebase the keys, as child bones are. | `MainViewModel.Armature.cs` bone deletion, `MainViewModel.Ik.cs`, `MainViewModel.Spline.cs` | the tool |
| Pasting a single row's pose key skips any id that is not a bone of the first rig, so a pin's key would be dropped on paste. Whole-key paste refuses a rig of pins alone. | `MainViewModel.TimelineClipboard.cs` | the tool |
| `ArmatureIsBound` knows layer bindings and stroke weights only. Once a layer can follow pins, pulling or rescaling a rig a drawing is pinned to passes the guard that exists to refuse exactly that. | `MainViewModel.RigSets.cs` | the render path |
| Image resize rescales the *bones* of the first rig only (B-to-be-filed; found here, predates pins). Pins are rescaled on every rig, so on a second rig a pin would be carried about a bone standing at the old scale. | `ImageResize.cs` | its own branch, before the render path |

**What an older build does with a file that has pins.** A build from
2026-10-09 on (Q230) carries `pins` as opaque JSON and writes it back, so the
pins are not lost — but it cannot rescale or re-parent what it cannot read:
resize the image or delete a bone there, and the pins come back here at the
old scale or with a dead parent. That is the standing cost of "keep it and
write it back", and pins are the first carried key that holds coordinates. A
build from before that date drops them silently on save, and nothing in the
file can refuse it. Both want checking against the owner's deployed build
before the tool ships. A pin kind added later will not load on this build,
like every enum in the record.

Pin poses reuse the pose track's keys, which is what makes easing, `Hold`,
stepping on 2s, the timeline rows and key editing work for pins without new
code. A pin with a parent bone rides it, so pins and bones mix on one figure, and
its own key is in that bone's frame the way a child bone's is.
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

**Budgets, written as tests before the solve existed, and what it measured**
(2026-10-11, owner's machine, Release, minimum of runs, alone):

| Budget | Measured | Rules out |
| --- | --- | --- |
| Largest mesh (3,888 vertices, 7 pins) solves inside one refresh | 1.3 ms | a dense solve |
| An ordinary mesh (858 vertices) solves well under a refresh | 0.33 ms | — |
| A drag costs under a quarter of `Prepare` | 1.3–1.6 ms against 66–112 ms | refactorising per pointer event |
| `Solve` into the caller's buffer allocates nothing | 0 bytes | garbage on the UI thread |
| 20,000 points mapped through 7,526 triangles | 2.4 ms | a linear triangle search |
| Meshing a 4K drawing, vertex cap held | 8.6 ms | an unbounded mesh |
| Posing 200 strokes through pins against through a bone | 4.2 ms against 8.5 ms | pins being the slow way to rig |

Placing a pin on the largest mesh costs about a tenth of a second (66 to 112 ms across runs) — a hitch on the click that
drops it, never on a drag. Ceilings in the tests sit well above the measured
figures, because the suite runs beside others (B442 measured a tenfold swing).

### The bone baseline, measured — and corrected

The first run (2026-10-11) reported 45.9 ms to pose 100 strokes and 62.7 ms to
auto-bind them. **Both figures were wrong**: the timing helper warmed with one
call and measured code the runtime had not finished optimising. Measured
properly the same day (Release, owner's machine, minimum of runs once three
batches in a row stopped getting faster):

| Measurement | Before B442 | After |
| --- | --- | --- |
| Pose 100 auto-bound strokes (7,900 points), 30 bones | about 5 ms | about 5 ms |
| Pose 200 one-bone strokes: 30-bone rig / 120-bone rig | 7.5 / 36.6 ms | 4.9 / 4.6 ms |
| Auto-bind 60 strokes: 30 bones / 120 bones | 11 / 502 ms | 5.4 / 21.6 ms |
| Auto-bind 100 strokes, 30 bones | 18.5 ms | 4.4 ms |
| `PoseAt` + `Solve` + `Deltas`, 30 bones | about 0.03 ms | unchanged |

So the posing geometry was never three refreshes slow. What the baseline did
find is two costs that grew with the size of the rig instead of the drawing —
the skeleton re-solved for every stroke, and a bones-cubed lookup in
auto-bind — fixed as **B442** on `perf/rig/posed-frame-geometry`, where
`BoneRigPerformanceTests` now lives. The pin budgets on this branch
take the corrected warm-up too (`PinSolvePerformanceTests.MinMs`).

**The lab.** `pose-drag` (bones) exists and has been run once cleanly, before
B442 reached main (3 runs, `rigged` fixture — 60 lines on a two-bone arm):
posing the geometry (`pose.frame`) 4.2 ms, **re-stamping the posed arm
(`raster.miss`) 380 ms**, release 891 ms, undo 897 ms, 15 s of stalls a run.
So the geometry is about 1% of a posed frame and the re-stamp is the rest: a
pose drag updates two or three times a second today, and pins re-stamp
through the same mechanism. An unposed drawing of similar size missed in
5.4 ms in the same run — per stroke about twenty times cheaper — which is one
data point and has not been investigated. **That investigation waits for the
owner's painting refactor to land** (owner, 2026-10-11): the cost is in the
stamping path the refactor is changing.

`pin-drag` is the same wrist gesture through pins, so the two are one
comparison. Its scenario file and wiring tests were written with the plan and
are parked until the render step, when the `pinned` fixture, the `pin.*`
spans and the `armature.pinMode` shortcut exist for them to be wired to (the
file as written is in this branch's history at 67c64229).

**Acceptance for phase 1:** `lab.py ab`-style comparison of `pin-drag` against
`pose-drag` on the same build — no metric more than 15% above its bone
counterpart, and no stall the bone scenario does not have. Judged on medians
of at least three runs each, machine quiet, per `perf/README.md`.

**Degrade, if the baseline shows re-stamping does not fit a drag:** the route
bones already have — expressive brushes ghost as centrelines during the drag
and land exactly on release (Q81 decision 5). Pins inherit it by going through
`PoseFrameForRender`.

## Tests

On this branch, green:

| File | Covers |
| --- | --- |
| `PinMeshTests` (15) | inside of an outline; small gaps; separate drawings; fork prongs; winding; cell clamp; vertex cap; outline; embedding; reach past the silhouette; bit-identical rebuild; empty; record untouched |
| `PinSolveTests` (16) | identity to the bit; pins land exactly; one pin translates; rigid motions reproduced; bend keeps area and folds nothing; one prong does not pull the other; unpinned piece stays; starch; starch target ignored; NaN targets; no pins; wrong target count; determinism |
| `PinSkinningTests` (3) | record untouched and rest path carried; unmoved mesh hands the stroke back; a two-point line bends |
| `PinSolvePerformanceTests` (7) | the budgets above |

Elsewhere: `BoneRigPerformanceTests` (8) on main with B442;
`PoseDragPerfLogTests` and `RigLabScenarioTests` on the lab branch.

`PinRecordTests` (19) covers the record: absent unless used, the round trip,
a pin-only rig, newer keys kept, cloning, pin keys as pose keys, a pin riding
a bone, non-finite keys, the hand-off to the solve, image resize on one rig
and on two, rig fitting, and a rigged document written by a build without
pins opening and saving back byte for byte (the fixture the sensitivity
review asked for).

**Not written yet:** the App tests (tool mode, overlay, undo, bake, MCP),
which arrive with the things they test.

**Where the limits sit.** Two of the geometric tests carry their wrong answer
with them: a blend of the pins' displacements by straight-line distance, with
no mesh and no rigidity, computed in the same test. A bent forearm keeps a
rigid residual of 3.0 where the blend leaves 26.1; the far prong moves 7.0
where the blend moves it 21.7. Rigid motions of the pins come back exact to
1e-11, and starch takes a bent patch's residual from 2.23 to 0.32. The adversary
broke the solve four ways. An identity rigid fit, soft pins and no starch were
each caught by the test meant for them. Skipping the second step was caught
only by a starch test, by accident, so `PullingALimbLongerDoesNotMakeItFatter`
was added for it. It also found that a point or a pin at infinity threw, and
that two pins a millionth of a unit apart threw the mesh 7e8 units; both are
fixed and tested.

## Steps

| # | Step | Branch | Cost |
| --- | --- | --- | --- |
| 0 | Build this branch; confirm the pin tests are red for the right reason and record what `BoneRigPerformanceTests` measures — **done 2026-10-11** | this one | S |
| 1a | The two rig costs the baseline found (B442) — **done 2026-10-11**; `BoneRigPerformanceTests` lives there | `perf/rig/posed-frame-geometry` | S |
| 1b | Bone baseline in the lab: `rigged` fixture in `Lightbox.Bench`, `pose.*` spans, run `pose-drag`. **Spike:** mesh a real frame-by-frame layer drawing by drawing and measure how much the mesh changes | `perf/lab/rig-baseline` | S–M |
| 2a | `PinMesh` + `PinSolve` + `PinSkinning` in Core to green — **done 2026-10-11** | `feat/puppet-pins` | L |
| 2b | The record: `Armature.Pins`, optional-absent, tests first, carried through clone, resize and rig fitting — **done 2026-10-11** | `feat/puppet-pins-record` | M |
| 3 | Render path: a layer follows pins; a rig of pins alone counts as a rig; pins through `PoseFrameForRender`, imported pixels through `PassMesh`, bake, `pin.*` spans, `pinned` fixture, run `pin-drag` against the acceptance line | `feat/puppet-pins-render` | M |
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
   Related, and decided by the build: the mesh follows the pen's path and not
   the brush's width, so a figure painted with a broad brush and no outline
   is meshed as its centrelines — thin. Whether a painted figure needs the
   brush's reach marked too is a question for real paintings.
3. **Determinism of the factorisation across runtimes.** Plain double
   arithmetic in a fixed order should be bit-stable, as the brush engine's is
   (`RuntimeDeterminismTests`); a fingerprint test belongs in step 2.
4. **Bones gaining scale** (phase 2) touches the pose record, IK, both
   exporters and the no-boil rule's seeding space. Larger than its one line in
   the decision table suggests.
5. **Re-stamp cost is measured and is the problem**: 380 ms for a 60-line
   posed arm. Nothing built here makes a pin drag feel fast until that is
   understood, and it waits on the painting refactor.
6. **A body held by two pins flexes.** The solve's own finding, above. The
   tool has to make holding a body easy or pins will read as rubbery.
