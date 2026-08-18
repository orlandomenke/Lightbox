# DESIGN: Fluid effects — fire, smoke and water as drawn line and fill

*Written 2026-08-18, answering "would a performant 2D fluid simulation be
possible, taking line style for the outlines and filling the strokes with
colour". The four pivotal choices were prompted and answered; `Q116` records
them, including the three that went against the recommendation and what each
costs.*

An **effects element** is a deterministic field simulation that **authors
drawings**. It is not a filter and not a brush: it runs a solver over a frame
range and writes ordinary strokes into those frames — `ToolKind.Fill` for the
colour separations, `ToolKind.Brush` along the outermost contour for the line,
and short tapered brush strokes for particles.

That distinction from `DESIGN-effects.md` is the whole architecture. An effect
there is `(image, frame, params) → image`: it transforms pixels that already
exist and lives in the render path. This produces the pixels' *source*. Two
consequences, and everything below falls out of them:

- **Invariant 1 holds trivially.** The output is strokes, so the record already
  describes the drawing and nothing new has to be re-derived at render time.
- **It is not in the render path at all.** Playback, export and the per-pointer
  budget pay nothing for an element that has been baked. The cost is an
  authoring bake, in the same class as an AI inbetween run.

## Feasibility, measured rather than assumed

Half the machinery exists. `src/Lightbox.Raster/Media/FluidLattice.cs` is a real
MAC-grid solver — face velocities, fixed-sweep Gauss-Seidel divergence
relaxation, conservative face transport — built for watercolour with
determinism, conservation and bounded work as stated load-bearing properties.
`FloodFill.TraceAllContours` already walks a mask to a boundary and runs
Douglas-Peucker over it.

The existing lattice, measured at effects-sim grid sizes (Release, one core, a
seeded disc, `FluidParams(0.3, 0.4, 0.5, 0.4, 0.3)`):

| Grid | per step | 8-step frame | 24 frames at 8 steps |
| --- | --- | --- | --- |
| 128×72 | 0.83 ms | 6.6 ms | 160 ms |
| 192×108 | 1.70 ms | 13.6 ms | 327 ms |
| 256×144 | 3.49 ms | 28.0 ms | 671 ms |
| 320×180 | 6.21 ms | 49.7 ms | 1192 ms |
| 512×288 | 13.97 ms | 111.7 ms | 2682 ms |

Contour trace and simplify at 192×108: 0.64 ms, yielding **37 points** for an
annulus — a drawable line, not a 600-point staircase.

Two things to read off that table. It is the *watercolour* solver, carrying four
premultiplied pigment channels, a chamfer distance field, capillary pull and
deposition, none of which a plume needs — a dedicated solver should come in well
under half. And **fluid is low-frequency**: you simulate at 192×108 and trace
contours into full-resolution document coordinates, because the contour is the
deliverable and the pixels never are. Simulating at document resolution would
multiply the work by ~100 for information the edge interpolation already
supplies.

**The budget is therefore a bake, not a frame.** The charter's numbers guard
interactive paths — 20 ms per pointer event, 400 ms per commit — and none of
them applies. The number that matters is press-Generate-to-48-drawings, and the
target is **≤ 2 s for 48 frames of fire at 192×108 on 8 substeps**, cancellable,
with progress.

## The solver

Not `FluidLattice`, and the reason is a modelling one rather than a performance
one. That is a *shallow-water* model: water flows down its own surface slope
over a paper height field, held by a capillary entry pressure, exchanging
pigment with the paper. It is right for a wash sitting on paper and wrong for a
plume rising through air.

What is needed is a standard Stam-style semi-Lagrangian incompressible solver in
`src/Lightbox.Raster/Media/FluidSolver.cs`: `u`/`v` face velocities, a `density`
field and a `temperature` field; per step add forces (buoyancy from temperature,
vorticity confinement to put the curl back that advection eats), advect
velocity, project to divergence-free, advect the scalars, then dissipate and
cool.

What transfers from `FluidLattice` is the expensive part — the parts that took
the time rather than the parts in the textbook:

- **The MAC staggering, and its argument verbatim.** A single vector per cell
  cannot represent flow leaving a local peak in all four directions at once, so
  a lone hot cell would sit there instead of blooming. Faces get that right and
  make transport exactly conservative.
- **Fixed-sweep Gauss-Seidel projection**, iteration count a compile-time
  constant rather than a convergence test, for the reason that file already
  gives: a solver that runs to tolerance is a solver whose output depends on
  floating-point luck.
- **Allocate once, `Rent` the lattice.** Roughly two dozen floats per cell, all
  taken in the constructor.
- **The determinism stance in full**: fixed iteration counts, fixed row-major
  traversal, no RNG, no clock, no parallelism.

**Determinism gains a second requirement when a sim spans frames.** `FluidLattice`
guarantees same-inputs-same-output within one run. Here frame *N*'s state must
be a pure function of the parameters and *N*, never of when it was computed — so
a bake runs from the first frame forward in one pass and never resumes from a
cached partial state. Resuming is precisely where a same-seed-different-picture
bug would live, and it would surface as an element that changed when you
re-baked a sub-range.

Turbulence and emitter variation seed from `Hash01` over cell position and step
index, the way brushes seed from dab position. Never an RNG. This is the "visual
variation is wanted, logical randomness is forbidden" rule at solver scale.

## From field to drawing

### 1. Bands

Sample the field on the sim grid, take *N* iso-levels, produce *N* masks, trace
each. **For fire the field is temperature, not density**, so band 0 is the
white-hot core and band *N* the dark tips: the ramp is the drawing, and band
count is an artistic control — one band is a silhouette, three or four is a
classic flame.

One piece cannot be reused as-is. `ContourTracer` walks pixel centres, so a
192×108 field traced into a 1920×1080 document puts every vertex on a 10 px
lattice and the staircase is 10 px wide. The band tracer needs **marching
squares with linear interpolation along the cell edge at the crossing**, which
puts the vertex where the field actually crosses the level and is smooth at any
output scale. `FloodFill.Simplify` (Douglas-Peucker, ε 0.75) then runs unchanged
— it is already the right second stage.

Bands are emitted back-to-front, coolest first, so painter's-algorithm stroke
order gives the stacking with no blend trickery.

### 2. Strokes

Per band, per frame: one `ToolKind.Fill` stroke, outer contour in `Points` and
inner contours in `Holes` — the even-odd convention the record already has and
invariant 3 already covers.

The outermost band additionally gets a `ToolKind.Brush` stroke along the same
contour. **This is the line-style half of the request, and it is the reason to
bake strokes rather than pixels**: the outline is not a stroked path, it is a
Lightbox stroke, so the artist's brush, its pressure profile, taper, texture and
every dynamic apply to it exactly as they would to a line they drew.

Points are document coordinates — `doc = origin + cell × scale` — so invariant 7
needs nothing special: export scale is a canvas transform on ordinary strokes.
Grid resolution is a property of the element, and changing it is re-authoring
rather than rescaling.

### 3. Particles

Massless particles advected through the same velocity field, each emitting a
short tapered brush stroke from its previous position to its current one.
Spawn position seeds from `Hash01` over emitter arc-length and step index, so a
re-bake puts the same ember in the same place.

Q116 took these in the first slice rather than later, and fire is what makes
that pay: a flame without embers reads as a flat shape. Bands are still built
and judged first — the ordering inside the slice is the mitigation for having
two things to tune at once.

## The record

`Doc.Sims`, a `Dictionary<string, SimElement>?` **absent until an element is
authored** — the camera's rule, and the "optional has two halves" lesson the
medium block paid for. `Assert.DoesNotContain("\"sims\"", json)` on a default
document ships in the same commit as the record, not after it.

```csharp
SimElement {
  string Id;  string Kind;               // "fire" | "smoke" | "water", registry-resolved
  int FirstFrame;  int FrameCount;  int ExposeOn;
  int GridW, GridH;  double OriginX, OriginY, Scale;
  int Substeps;
  List<Emitter> Emitters;                // shape, strength, temperature, keyable
  SimParams Params;                      // buoyancy, vorticity, dissipation, cooling
  BandSpec Bands;                        // levels, swatches, which band carries the outline
  string? OutlineBrushPresetId;
  ParticleSpec? Particles;               // absent unless used
}
```

`Kind` is a string id resolved through a registry, not an enum, for the reason
the brush tip registry already took: an unknown kind from a newer build is
**preserved on save and skipped on bake**, never dropped.

On `Stroke`, one nullable addition: `string? SimId`. Absent on every hand-drawn
stroke, and it changes no pixel — it is provenance plus the handle for re-baking
(drop every stroke carrying this id in the range, run again). Deleting it leaves
ordinary strokes, exactly as `Frame.AiProvenance` does.

**Why baking wins**, per Q116: every existing consumer works unchanged, and the
artist can draw over, erase into, recolour and rig the result. The accepted cost
is that re-simulating discards hand edits inside the element, which the UI must
say plainly before it does it.

## Boil: what was chosen and what it costs

Contours are traced per frame independently (Q116 decision 3). Marching squares
over a moving field gives contours whose point count and parameterisation jump
every frame, so the line crawls even though the simulation is bit-reproducible.
**Determinism and temporal coherence are different properties**, and invariant 2
only buys the first — this is the charter's "fine on one image, boils at 12 fps"
failure arriving through a door the invariant does not cover.

For fire that is close to right: hand-drawn flame boils hard, and per-frame
retracing is what an effects animator does. The costs are in Q116 and the two
cheap mitigations belong in the first slice because they are nearly free —
**quantise the iso-level** so the contour does not chase sub-threshold noise,
and **expose on 2s** (`ExposeOn`), which halves the boil and is what an animator
would do anyway.

The upgrade path stays open and nothing in the record blocks it: advecting the
previous frame's contour and re-projecting it onto the new iso-level changes how
`Points` is computed and nothing else.

## Invariants, applied

| | |
| --- | --- |
| 1 · record is the document | output *is* strokes; `SimElement` is authoring parameters, as `StrokePath` is |
| 2 · no randomness | `Hash01` over cell position and step index; fixed sweeps, fixed traversal, no clock, no parallelism. `TwoBakesAreBitIdentical`, after `FluidLatticeTests.Two_Runs_Are_Bit_Identical` |
| 3 · fills are part of the record | bands are `ToolKind.Fill` strokes with contours and holes — the existing mechanism |
| 4 · pixel settings per stroke | band colours resolve to each stroke's `Color`/`SwatchId` at bake time |
| 5 · view transform is view-only | the sim is document-space; the camera never enters it |
| 6 · bounded work | bounded by grid × steps × frames, all authored — and outside the per-event path entirely |
| 7 · scale the surface, never the geometry | contour points are document coordinates; export scaling is unchanged |

## Reach and configuration

Absent by default, reachable everywhere. A document with no element writes no
`sims` key and shows no effects UI; a project type decides whether the docker is
in front of you — a film project would default it on — never whether the
capability exists.

## Landing checklist

Resolved in advance, per the *land the places it shows up* table:

- `ShortcutMap`: generate element, re-bake element.
- **Own view model and docker.** `FluidEffectsViewModel` in its own files;
  `MainViewModel` gains a registration line and nothing else. `HOTSPOTS.md` is
  the reason, and it is the same structural constraint `DESIGN-effects.md` took.
- Presets as project files, beside effect presets — a fire is tuned once.
- The docker registers in workspace defaults.
- MCP `sim.create` / `sim.bake` / `sim.params`: an agent that can paint should
  be able to author a flame.
- A manual section, marked *Planned* until step 4 lands.

## Build order

Each step is one branch with one objective.

1. **`FluidSolver` + determinism and budget tests.** No record, no UI. Two bakes
   bit-identical; the 48-frame target measured.
2. **Field → strokes.** Marching squares with edge interpolation, `BandSpec`,
   contour → `Fill` plus outline `Brush`. Tested by baking a *static* field, so
   the tracer is judged without the solver in the measurement.
3. **The record.** `Doc.Sims`, `Stroke.SimId`, serialization with the
   absent-until-used assertion.
4. **Fire, end to end** — emitter, temperature field, heat ramp, embers, re-bake.
   The first thing an artist can use, and where the roadmap item earns its
   evidence anchors.
5. **Docker, view model, landing checklist.**
6. Smoke (same solver, density instead of temperature, embers off by default),
   then water.

Steps 1–3 are deliberately separable so that a bad first result is diagnosable:
Q116 chose fire, which puts a new field, a colour ramp and a particle pass in
front of the artist at once, and splitting the build is how that stays
debuggable.

## Not decided here

- **Water.** In the roadmap item and it needs its own note: a free surface means
  the contour *is* the simulation rather than a threshold of it, plus splash and
  droplet separation — and it is the effect that most wants the coherent-contour
  upgrade Q116 declined.
- **GPU.** `DESIGN-gpu-compositing.md` records why compositing is on the CPU. A
  bake at these grid sizes does not need the lever, and reaching for it would
  tie an authoring feature to a rendering decision nobody has made.
- **Fluid that flows around drawn art.** Rasterise the frame's ink into the
  solver as a boundary condition — a natural step 7 and cheap once the solver
  exists, but it makes the bake depend on other layers, which is a record
  question rather than a solver one.
