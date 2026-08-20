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
deposition, none of which a plume needs — so a dedicated solver should come in
well under half. And **fluid is low-frequency**: you simulate at 192×108 and trace
contours into full-resolution document coordinates, because the contour is the
deliverable and the pixels never are. Simulating at document resolution would
multiply the work by ~100 for information the edge interpolation already
supplies.

> **Measured 2026-08-18, and the halving was wrong.** `FluidSolver` costs
> **3.74 ms/step** at 192×108 — more than *twice* `FluidLattice`, not half of
> it. The prediction ignored the one thing an incompressible solver pays for
> that a shallow-water one does not: a **pressure projection**, sixteen Jacobi
> sweeps over every cell, which is the bulk of the step. Dropping the pigment
> channels saved much less than the projection cost.
>
> The conclusion survives the arithmetic being wrong, which is the only reason
> this is a correction rather than a redesign: a 48-frame bake at 8 substeps
> measures **1437 ms**, inside the two-second target with room. It is worth
> keeping the wrong number visible, because the same reasoning would say a
> 512×288 element is affordable and it is not.

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

What is needed is a Stam-style incompressible solver in
`src/Lightbox.Raster/Media/FluidSolver.cs`: `u`/`v` face velocities, a `density`
field and a `temperature` field; per step advect velocity, add forces (buoyancy
from temperature, vorticity confinement to put back the curl advection eats,
seeded curl noise for turbulence), hold the field inside its CFL bound, project
to divergence-free, transport the scalars, then dissipate and cool.

**Momentum is resampled and matter is moved, and that split was forced by
measurement** (Q117). Velocity advects semi-Lagrangian, which is
unconditionally stable and where conservation buys nothing. Density and
temperature move as *face fluxes* — donor-cell upwinding, a flux subtracted from
one cell and added to precisely one other — because the textbook choice of
advecting them the same way is not conservative, and it does not merely drift:
measured on a closed swirl over a hundred steps the density came out at
**107% / 141% / 219% / 293%** of what went in as the peak speed rose through
0.17 / 0.41 / 0.77 / 1.02 cells per step. Every cell was a plausible number and
no assertion could have caught it after the fact. What flux transport costs is
sharpness, and that is cheaper here than in a renderer that shows the field as
pixels: the deliverable is a traced iso-contour, so a smoother field gives a
smoother line — which, given Q116 chose per-frame tracing, is a benefit rather
than a consolation.

What transfers from `FluidLattice` is the expensive part — the parts that took
the time rather than the parts in the textbook:

- **The MAC staggering, and its argument verbatim.** A single vector per cell
  cannot represent flow leaving a local peak in all four directions at once, so
  a lone hot cell would sit there instead of blooming. Faces get that right and
  make transport exactly conservative.
- **A fixed-sweep pressure solve**, iteration count a compile-time constant
  rather than a convergence test, for the reason that file already gives: a
  solver that runs to tolerance is a solver whose output depends on
  floating-point luck. The iteration is **Jacobi rather than Gauss-Seidel**,
  which is the one place this parts company with `FluidLattice`: Gauss-Seidel
  reads its own partial results, so a row-major sweep biases the answer along
  the sweep and a left–right symmetric setup comes out slightly asymmetric.
  Jacobi is exactly symmetric, which turns "is there an index slip" from a
  judgement about a picture into an assertion — and it caught one immediately,
  in the test fixture rather than the solver. Sixteen sweeps, chosen off a
  measured table of residual divergence against cost that lives beside the
  constant.
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

## One pipeline, three sources of field

The stage that turns physics into drawing is **`field → iso-contour → strokes`**,
and it does not care where the field came from. That is worth stating plainly,
because it is what decides how far this system reaches and it answers the
question the effect list keeps raising — *is water just smoke with different
numbers?*

| Source | What it serves | State |
| --- | --- | --- |
| **Solved grid** — density and temperature on the incompressible solver | smoke, fire, steam, dust, ash, ink in water | built (step 1) |
| **Splatted particles** — a metaball field summed from particle positions | goo, slime, thick splashes, blobs that merge and separate | not built; cheapest of the three |
| **Free surface** — a tracked interface rather than a threshold | water | not built; the hard one |

**The gaseous family really is one engine with different numbers.** Smoke, fire,
steam, dust and ink differ by buoyancy, weight, dissipation, cooling, vorticity
and turbulence — plus which field the bands read, since fire bands from
*temperature* and smoke from density. Those are presets, and the landing
checklist's "presets as project files" is where they live.

**Water is not, and no parameter reaches it.** In smoke the contour is a
threshold you chose through a soft field, and choosing a different one gives a
different but still plausible plume. In water the contour *is* the simulation:
pick a different threshold and you do not get a different-looking splash, you get
a wrong one. Surface tension, droplets separating and merging, and a flat resting
level do not exist in this model at any setting.

**Goo sits between them, and is the case where the particle half becomes the
source rather than the decoration.** Blobs that merge and separate are natural as
particles and miserable on a grid, and summing particles into a scalar field —
metaballs — hands the existing tracer exactly what it wants. Everything
downstream is unchanged: same iso-contour, same bands, same strokes, same line
treatment. Only the field's origin differs, which is why it is the cheap one of
the two unbuilt sources and why it is worth naming now, before step 2 fixes the
tracer's interface around a single producer.

**The consequence for step 2**: the tracer takes *a field*, not *a solver*. Given
`(float[] field, int w, int h)` and a band spec it must produce strokes, with no
reference to how the numbers were made. That costs nothing today and is what
keeps sources 2 and 3 from being rewrites.

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

## Line treatment — following an art style

An effect has to read as *this show's* effect. Q118 settles how, and the useful
half of the answer is that most of it already exists.

**The mark is free.** The outline is a `ToolKind.Brush` stroke carrying a full
`BrushSettings`, so size, hardness, tip, roundness, angle-follows-direction,
texture and grain all apply, including from a brush imported out of Photoshop or
Krita. Nothing to build, and nothing to invent: **a brush preset says what the
mark looks like, and a treatment says how a contour becomes a stroke for that
brush to draw.** Anything a brush already expresses must not be duplicated here.

**The weight along the line is the part that reads as drawn**, and the tracer is
uniquely placed to supply it, because at every contour point it knows the
physics. It writes a *pressure* per point, and pressure is already what the brush
turns into size, flow and hardness — so this needs no new rendering at all, and
`PressureProfile` already carries pressure by arc length so the weight survives
curve-fitting and reshaping.

| Driver | Heavier where | Reads as |
| --- | --- | --- |
| curvature | the contour bends | classic animation line |
| flow speed | the fluid is slow | motion, a whipped tongue |
| field gradient | the falloff is sharp | graphic edge vs wispy one |
| light direction | the contour faces away | inked solidity |
| band depth | the outermost band | depth without shading |
| arc taper | away from the ends | a brush-drawn stroke |

Drivers blend rather than compete: each contributes an amount, and every weight
defaulting to zero except one keeps a simple treatment simple.

**The outline may leave the contour** (Q118). It can be offset in or out, cover
only part of the silhouette, and break into several strokes with gaps — anime
fire commonly outlines only its upper edge, and a broken line reads as drawn
where a closed one reads as traced. The consequence for step 2 is concrete: **one
band may emit several strokes**, so nothing in the tracer may assume
one-contour-one-stroke.

```csharp
LineTreatment {                  // every field nullable — see the cascade below
  string? BrushPresetId;         // what the mark looks like; nothing here repeats it

  double? Offset;                // stroke-widths, + outward
  Coverage? Covers;              // Full | Facing | Leading
  double? CoverageAngleDeg, CoverageSpreadDeg;
  double? LightAngleDeg;         // for the LightDirection driver; not the same as coverage
  double? BreakLength, BreakGap; // stroke-widths; absent = continuous

  double? BaseWeight;            // the pressure floor
  List<WeightDriver>? Weights;   // (source, amount), blended

  double? Simplify;              // stroke-widths
  double? Smoothing;             // 0 raw polyline … 1 fitted cubics
  double? CornerAngleDeg;        // sharper than this stays a corner

  int? Bands;  BandSpacing? Spacing;  Outlined? OutlinedBands;
}
```

**Units are ratios, angles and stroke-widths — never pixels, never
coefficients.** Two reasons, and they agree. Invariant 7's argument: a treatment
in pixels means something different at another element or output scale, while
stroke-widths is scale-free. And Q118 chose to design for style inference now,
which requires every field to be **observable in a drawing** — nobody can see
`PressureSizeGamma = 1.4`, and anybody can see that a line is three times heavier
on bends than on straights. That constraint makes the record better for the
artist and better for the model at the same time, which is the only reason it is
worth taking on before a look has been tuned by hand.

**The cascade costs one type, not two.** An element names a treatment by id and
may override fields on top of it. Because every field above is nullable and the
defaults live in exactly one place, a shared treatment and an override are the
*same record*, and resolution is `override.X ?? shared.X ?? default.X`. The
obvious shape — a parallel mirror record of nullable fields — is the one thing
that would have to be kept in step forever, and it is not needed. It also lands
*optional means absent* for free: an untouched treatment writes no keys.

The part of the cascade that is not free has to be paid deliberately: **an
overridden field is marked as overridden and has a per-field revert to the shared
value.** Without both, "two places to look" is a support burden and reads to an
artist as the app being inconsistent rather than as a cascade.

**Restyling re-traces, it does not re-solve.** Every field above applies at trace
time, so changing a look costs ~30 ms across 48 frames against ~1437 ms to
re-simulate. The baked fields are therefore held in memory for the life of the
session (Q118) — nothing new in the file, style tuning live, one re-solve after
reopening before the first restyle.

### Style inference, and why the vocabulary is fixed for it now

Q118 chose to design for "point at a drawing and match it" from the start,
without building it yet — inference needs a look to compare its answer against,
so it lands after step 4. What is fixed now is the vocabulary, plus three
consequences:

- The record is schema-constrained in the manner of `StrokeSchemas`, small enough
  that a model reasons about named properties rather than emitting opaque floats.
- The payload is the cheap direction for once. `docs/DESIGN-ai-payload.md`
  measures images at ~87% of a request's bytes and ~5% of its tokens; this sends
  one image and receives a small object, which is the inverse of the
  inbetweener's problem and needs none of its stroke-count levers.
- **Gate G12 now applies** to the treatment record and its schema: the
  ai-engineer and art-director pair review them, with art-director holding the
  veto on whether an inferred style actually reads.

> **Built 2026-08-18 (step 2), and four things are worth carrying forward.**
>
> - **The tracer takes `(field, width, height)` and a treatment.** No solver
>   anywhere in `FieldTraceRequest`, so the particle and free-surface sources
>   plug in without touching it.
> - **Levels sit *interior* to the range**, at `(k+1)/(bands+1)` of it. A band at
>   `High` encloses almost nothing and one at `Low` encloses everything; neither
>   is worth spending one of three bands on.
> - **A level at or below the field's `outside` value encloses the whole plane**,
>   because the padding is then inside too. That is what it means rather than a
>   defect — and it caught four test fixtures on their first run, since tracing a
>   signed-distance field at zero against a default `outside` of zero is the
>   obvious thing to write.
> - **Winding is the classifier.** Marching keeps the inside on the left, so in
>   the document's y-down space an outer contour signs negative and a hole signs
>   positive. `FieldTracer` groups holes into regions on that, and
>   `MarchingSquaresTests.An_Annulus_…` is what holds the convention still.
>
> Measured: **0.92 ms a frame**, so re-tracing a whole 48-frame element is
> **44 ms** against ~1437 ms to re-simulate. The ~30 ms the section above
> estimated was close enough to keep the conclusion — restyling is live, baking
> is not.

## The record

`Doc.Sims`, a `Dictionary<string, SimElement>?` **absent until an element is
authored** — the camera's rule, and the "optional has two halves" lesson the
medium block paid for. `Assert.DoesNotContain("\"sims\"", json)` on a default
document ships in the same commit as the record, not after it.

```csharp
SimElement {
  string Id;  string Kind;                  // "fire" | "smoke" | "steam", registry-resolved
  string? Name;                             // absent unless an artist named it
  int FirstFrame, FrameCount, ExposeOn;     // ExposeOn 2 is animating on 2s
  int GridWidth, GridHeight;
  double OriginX, OriginY, Scale;
  int Substeps;
  SimParams Params;                         // buoyancy, vorticity, turbulence, dissipation, cooling
  List<Emitter> Emitters;                   // shape, strength, temperature — cell units, element-local
  bool BandsFromHeat;                       // fire bands from temperature, smoke from density
  double BandLow, BandHigh;                 // the field range the bands span
  List<string> BandColors;                  // a colour per band; how MANY is style, on the treatment
  string OutlineColor;
  string? TreatmentId;                      // the shared line treatment, if any
  LineTreatment? Treatment;                 // overrides on top of it — the same record
  ParticleSpec? Particles;                  // absent unless used
}
```

**`SimParams` lives in Core and the solver reads it directly**, which is the one
place the built record departs from this sketch. `FluidSolver` briefly owned its
own parameter struct, and keeping it would have meant two copies of the same
eight numbers and a translation between them — with the document's copy free to
drift out of step with the solver's. `BrushEngine` already settled this shape:
Core says what a mark is, Raster carries it out. The consequence worth stating is
that **a solver tuning change is now visibly a file-format change**, which is
the honest position rather than a cost.

**`Doc.LineTreatments` holds the shared looks, and `Doc.TreatmentFor(element)` is
the only place the cascade is resolved** — so nothing else has to remember the
order, and a treatment deleted out from under an element resolves to the
defaults rather than throwing. A document that never authors an effect writes
neither key.

**The deep-copy is guarded by machinery that already existed.** `DocCloneTests`
walks the whole `Doc` type graph by reflection and fails on any field a `Clone`
forgets, so the new records were covered the moment they were reachable from
`Doc` — no new test needed, and no way to add a field later and quietly share it.

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

## What step 4 found by looking

The build order put fire fourth so that the sweep count, the simplify default
and the band vocabulary would finally be judged against a picture rather than a
measurement. They were, and **the picture disagreed with every green test**.
Momentum was conserved, the fluid was incompressible, the smoke was neither
created nor destroyed, and it did not look like fire. Four separate faults, none
of which any assertion could have raised:

- **The turbulence was acting as wind.** A noise scale of twelve cells on a
  seventy-cell element is a push the width of the plume, so the flame was blown
  sideways rather than made wispy. Six reads as turbulence.
- **The flame stalled.** Buoyancy is proportional to heat, so a plume that cools
  slowly stops climbing while it is still visible. Hotter, cooling faster, gives
  the short bright tongue a flame has.
- **Nothing damped the flow.** A sustained plume in a box with four solid walls
  accumulates circulation until the flame lies over — a lava lamp. `SimParams.Drag`
  is the fix, and it was measured rather than assumed: worst sideways drift of
  7.0 cells at drag 0, 3.6 at 0.05, 0.9 at 0.12. The default is 0.05, because a
  flame that never wavers reads as fake.
- **Almost nothing was drawn.** A plume's field is steeply peaked — only 4% of an
  element's cells hold more than a hundredth of its peak — so bands spread over
  the whole range all landed inside the brightest core. Band levels are now
  fractions of the element's own measured peak, over a window from 2% to a third
  of it.

Two of those are now tests rather than tuning: `A_Flame_Stands_Over_Its_Emitter`
and `The_Outer_Band_Reaches_The_Edge_Of_The_Plume` assert the properties the
render revealed, so the next person to change a default finds out mechanically.

**One defect the look exposed that nothing else would have.** `PeakBand` was
measured over the frames an element *keeps*, so an element exposed on 2s sampled
a different peak from the same element on 1s and every band level shifted with
it. Holding a drawing must say nothing whatever about what is drawn. It is taken
over every frame simulated now, and `Exposing_On_Twos_Halves_The_Drawings_And_Not_The_Motion`
checks the peaks agree as well as the drawings.

### Movement and wind — wind built, attachment next (Q122)

The one thing an artist asked for that the record cannot yet express: an effect
that belongs to a character who is running, turning or being rained on. Three
different things, and conflating them is the trap — *ambient wind* moves smoke
already in the air, *emitter motion* lays a trail behind a travelling source, and
*attachment* moves the element's box so the effect stays with a character.

**This is where the simulation earns its cost over a library of pre-made
cycles.** When a character turns, the smoke in the air keeps going the old way
while new smoke goes the new way, and the whip and lag come free from the
solver having history. Baking "run right" and "run left" separately and cutting
between them cannot produce it — each bake starts from still air, so the cut
pops. Q122 settles the shape: a keyable wind vector on the element, elements
bound to a drawing's anchor so they follow the animation without keying, a
pre-roll so an element starts from an established plume, and the key vocabulary
`DESIGN-effects.md` already specifies rather than a second one.

**Wind landed 2026-08-18, and the measurement is the interesting part.** It is
applied as a *relaxation toward the wind's speed, weighted by how much fluid is
there* — not as a uniform push, which in a box with four solid walls is
divergence the projection removes on the same step, and not as a constant force,
which would accelerate smoke past the wind and keep going. Two consequences:
still air stays still and only the plume is blown, which is what an artist means
by wind and is cheaper besides.

The inertia Q122 claimed is now a number rather than an argument. Two frames
after a wind reversal, the risen smoke leans **−24.6 cells** — still travelling
the old way — while the smoke leaving the emitter already leans **+4.0**. A
28-cell bend, and no pair of separate bakes can produce it, because each would
start from still air. The first measurement of it looked for that lag in the
*whole field's* centre of mass and did not find it: the core at the emitter turns
within a frame, so the test was measuring the one part of the plume that has no
memory.

**Calibration, found by rendering it.** Wind is in the same units as the flow, and
a plume rises at roughly 0.15 cells per step — so a wind of that order bends a
flame about half a right angle, and 0.5 lays it flat. The useful range for a
figure in motion is far below what the number suggests, which belongs in the
manual before anybody meets it.

**Looping is a known gap, stated rather than hidden.** A pre-roll removes the
thin start; it does not make a run cycle seamless. Blending the end into the
beginning would mean blending two contour sets, and the strokes have no
correspondence between frames — which is exactly what Q116 chose when it took
per-frame tracing. If looping cycles become the priority, that is an argument for
revisiting Q116, not for a blend.

### Painted emission, and what the render said about it

An emitter can name a **mask layer**, and where that layer has ink is where it
emits. Three things about it are load-bearing:

- **The mask is the emission, whole.** It is not intersected with the costume at
  bake time. Q124 originally recorded the opposite and Q125 corrects it: alpha
  lock belongs to the *painter*, keeping a brush on the garment while a mask is
  made, and it is optional even there. An intersection at bake time would let a
  hem swinging away silently extinguish the fire on it, with nothing in the
  record to say why.
- **The origin is the only thing that moves it.** `Emitter.MotionX`/`MotionY` are
  keyed offsets from where the emitter was placed, so placing and animating stay
  separate acts. With nothing keyed the mask emits exactly where it was painted.
- **A mask is an ordinary layer**, so holds, the inbetweener, rig binding and
  onion skin all work on it and none of them was rebuilt. `OmitFromExport` keeps
  it out of renders. Where a rigid stamp slides — a billowing cloak deforms and a
  stamp does not — the answer is to redraw the mask on the frames that need it,
  which composes with the origin because neither mechanism knows about the other.

The mask is downsampled by rendering the same strokes onto a smaller surface at
an output scale of `1/Scale`, never by scaling their geometry: invariant 7, and
the reason a coarse grid gives a coarser mask of the same drawing rather than a
different drawing.

> **What the render said, and it is a finding rather than a defect.** A mask that
> emits over an *area*, every frame, produces a **glowing shape** — it refuels the
> whole region continuously, so heat never leaves it and no tongue can detach. On
> a painted hem that reads as a burning edge, which is useful and is not flames
> rising off cloth. Lowering the fuel and raising the cooling made it ragged and
> licking rather than a smooth wire; it did not make it flames.
>
> Flames need emission that is **sparse in space or in time**. In space that is
> the artist's job and already works — paint a broken mask, get separate flames.
> In time it would be a *flicker*: emission modulated per cell by `Hash01` over
> position and frame, so the burning points wander along the hem the way they
> really do. That is the sanctioned pattern for something that must vary by
> frame, and it is the first thing to try if a continuous mask reads as too even.
> It is deliberately not in this branch: it is a new parameter that reaches
> pixels and varies by frame, which is a decision rather than a tweak.

### Burning, and the one number that was doing two jobs

The observation came from watching a render: real flames shed their tips, and
ours did not. **The measurement refuted the obvious reading of that.** The
shedding was already there — the field breaks into separate pieces on 22 of 40
frames, and the tracer draws every one of them (21 of 22; there is no
minimum-area filter). What was missing was *survival*: counting only pieces of
five cells or more, the window's own fire shed six over forty frames and **every
one lasted exactly one frame.** The median detached piece was a single cell.
That is sparkle, and it is why nobody saw a flame tip.

The cause is structural rather than a mis-set number. **Heat is stamped at an
emitter and from then on only decays.** A piece inside the column is refuelled
from below every frame; a piece that has detached has nothing, so at
`Cooling = 0.06` per step over eight substeps — 39% of its heat per frame — it
falls under the outermost band level inside one frame. Real flame tips detach
*and keep burning*, because they carry fuel with them.

Slowing the cooling does work, and measuring it is what found the real problem:

```
                          flame height   sheds
  Cooling 0.06 (tuned)     21 cells      6, every one 1 frame
  Cooling 0.03             19 cells      10, 1-2 frames
  Cooling 0.01             43 cells      2, lasting 12 and 26 frames
  Cooling 0               84 cells       everything merges into one mass
```

The row that gives the behaviour is the row that doubles the flame. **`Cooling`
sets a flame's length and its tip's survival with one number**, and the defaults
were tuned for length — so the flame we shipped could not have shed a tip at any
setting without becoming a different flame. Note also that zero is worse than
0.01: with nothing cooling, the whole grid stays lit and there is no *separate*
piece to see. It is a window, not a direction.

**`Combustion` is that second job moved somewhere it can be set on its own.**
Density is the fuel — an emitter already stamps it beside heat and the flow
already carries it, so a detached parcel is already carrying a supply and only
needed permission to spend it. Where fuel sits above an ignition point, a
fraction of it burns per step and becomes heat.

Three things about the shape are load-bearing:

- **It is self-limiting**, and that is what makes it a flame rather than a
  runaway. Burning consumes the fuel, so heat production falls away while
  `Cooling` keeps taking a constant fraction; what is left is cool density,
  which is smoke. `Emitter.Burst` gets *fireball becomes smoke* out of this for
  free rather than needing a second mechanism.
- **Ignition is absolute, not a fraction of the element's peak.** `BandLow` is a
  fraction because it reads a field that has already been computed. An ignition
  point *decides what the field becomes*, so scaling it by the peak would make
  the threshold depend on the burning it is gating. Bands may follow the result;
  this has to stand outside it.
- **It is a value type.** `SimParams` is a record and `SimElement.Clone` copies
  it with `with { }`, which copies a reference rather than what it points at — a
  class here would leave a duplicated effect editing the original.
  `EmitterScatter` can be a class because its owner clones it by hand; this
  cannot, and `Editing_A_Copys_Combustion_Leaves_The_Original_Alone` is what
  turns that from a comment into a test.

**Burning makes a fire hotter and a hotter fire climbs**, so on a grid with room
overhead this lengthens the flame and `Vorticity` is what takes the rise back by
spending it on curl. That interaction is grid-dependent, which is the part that
was nearly assumed rather than measured — on the 44-cell grid a new element gets,
the flame has no room to use the extra heat:

```
                                  height of grid   longest piece
  burning off (before)               50%             1 frame
  burning on, vorticity .35 (ships)  47%             4 frames
  burning on, vorticity .7           43%             7 frames
```

So the default changes one thing. New **fire** elements arrive burning; smoke and
steam do not and write no keys about it.

## Reach and configuration

Absent by default, reachable everywhere. A document with no element writes no
`sims` key and shows no effects UI; a project type decides whether the docker is
in front of you — a film project would default it on — never whether the
capability exists.

## Landing checklist

Resolved in advance, per the *land the places it shows up* table:

- `ShortcutMap`: **done** — `effects.window` on `Ctrl+Shift+E`, filed under an
  `Effects` category and named word for word as the menu names it, because the
  editor is searched and an entry under a second name for the same command is
  the same failure as no entry, one step later. Generating and re-baking an
  element are buttons in the window and are not yet bindable; they belong here
  the moment either becomes something an artist repeats.
- **A menu item, at `Effects ▸ Fluid effects…`.** A top level of its own rather
  than a line under View: View is where you say what you want to *look* at, and
  everything that will land here changes what is *in* the document. It is also
  the shelf the rest of this note needs — goo, water and style inference each
  arrive as a window, and without it each one is another orphan among the
  dockers. A shortcut is not a way in on its own: nobody discovers
  `Ctrl+Shift+E`, they open menus.
- **Own view model and window.** **Done.** `FluidEffectsViewModel` in its own
  files; `MainViewModel` gains one mutation seam (`MainViewModel.Effects.cs`,
  which exists so `InvalidateFrameRender` can stay private) and `MainWindow`
  gains a menu item, a shortcut case and one field. `HOTSPOTS.md` is the reason,
  and it is the same structural constraint `DESIGN-effects.md` took.
- Presets as project files, beside effect presets — a fire is tuned once.
  **The global half is done** (step 6g): `EffectPresetStore` is the artist's own
  shelf, `effects.json` beside `brushes.json`. The *project* shelf is
  outstanding, and is what "as project files" was asking for.
- ~~The docker registers in workspace defaults.~~ Not applicable — it is a
  window, and a window is not in the workspace layout.
- MCP `sim.create` / `sim.bake` / `sim.params`: an agent that can paint should
  be able to author a flame. **Outstanding.**
- A manual section, marked *Planned* until step 4 lands. **Done** —
  `docs/manual/15-effects.md`.

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
5. **The effects window and its view model** (Q123) — **landed 2026-08-19**. A
   window rather than the docker this originally said, because thirty-odd fields
   do not fit a column and tuning needs a preview and a scrubber — while
   *placement* stays on the canvas, since typing coordinates for a flame is not
   authoring. Includes the cascade's two obligations: an overridden treatment
   field is marked as such and reverts to the shared value in one action.

   Three things it settled that the plan had not:

   - **Simulate and Bake are separate buttons**, because the two costs differ by
     a factor of forty. A style edit redraws from the solve already in hand and
     previews as the slider moves; a fluid edit marks the picture stale and waits
     to be asked. Hiding that would make every edit feel like the slow one — and
     worse, would make an artist afraid to touch the cheap half. `SolveFingerprint`
     is the mechanical half of the same line, and it is asserted from *both*
     sides: eleven changes that must force a re-solve, seven that must not.
   - **The outline pen belongs to the element** (`SimElement.OutlineBrush`,
     nullable so an element on the default pen writes no key). The obvious thing
     was to hand the tracer whatever the toolbar was holding, which makes a bake
     unreproducible: the same element re-baked after picking up a marker comes
     back inked with a marker, and an artist has no way to say what an element's
     line *is*. That is invariant 4's reasoning one level up.
   - **Rows hold ids, not elements.** Undo swaps a whole `Doc` back in rather
     than editing in place, so a row holding the object would go on editing a
     document nobody is looking at, with every slider still appearing to work.

   The fields are *data* (`FluidEffectsViewModel.Fields.cs`), not controls, so
   adding a solver parameter is a line there and a line in `SolveFingerprint` and
   nothing in XAML — and `Every_Solver_Parameter_Has_A_Row` walks `SimParams` by
   reflection so forgetting the first line fails rather than shipping invisible.
5b. **Wind and pre-roll** (Q122) — **landed 2026-08-18**, and the first user of
   `DESIGN-effects.md`'s key vocabulary, which is built as `EffectParam` /
   `EffectKey` in `src/Lightbox.Core/Effects/`. Wind is two keyed scalars rather
   than an angle and a strength, because keying an angle wraps: a gust swinging
   from 10° to 350° would interpolate the long way round through every direction
   nobody asked for.
5c. **Emission painted onto a layer** (Q124, refined by Q125) — **landed
   2026-08-18**. An emitter names a mask layer; the mask *is* the emission, never
   intersected with anything at bake time, and the emitter's keyable origin is
   the only thing that moves it. Alpha lock turned out to belong to the
   *painter* rather than to the bake — see below.
5c-i. **Emission flicker** — **superseded as the fix for the burning edge; still
   wanted for shimmer.** Step 6f (scatter) is the answer to "a painted area reads
   as a burning edge", and a better one: its gaps are spatial and stable rather
   than temporal, so what rises off a flame actually leaves. What flicker still
   buys is shimmer within a flame, and it remains the first effect parameter
   whose seed varies by frame — Q80's ground for brushes — so it keeps its own
   re-render and hold tests. The original entry follows.
   The one thing the burning-cloak render asked for and
   did not get. A mask that emits over an area every frame refuels itself, so no
   tongue can detach and it reads as a burning *edge* rather than as flames.
   Emission modulated per cell by `Hash01` over position **and frame** makes the
   burning points wander along a hem the way they really do. Small, and a
   *decision* rather than a tweak: it is the first effect parameter whose seed
   varies by frame, which is the ground Q80 covers for brushes — the seeding
   story grows a frame dimension and needs its own re-render and hold tests.
   Sparse emission in *space* already works and needs nothing: paint a broken
   mask.
5d. **Drawn art as an obstacle** (Q125) — the other half, and its own branch,
   because the cost is not the rasterisation but putting solid boundaries
   *inside* the grid where `FluidSolver` has only walls at its edge: interior
   Neumann boundaries in the pressure solve, flux transport that will not carry
   mass into an obstacle cell, and conservation tests that learn mass may be held
   against an obstacle. **The window deliberately ships no Obstacle picker**
   until this lands: `SimElement.ObstacleLayerId` and `LayerMasks.Obstacle` both
   exist, so a control would bind and store perfectly and change nothing on
   screen — a lying control, and worse than a missing one because the stored
   value would look authored.
5e. **Anchor attachment** (Q122) — an element that follows a drawing's anchor.
5g. **Undo granularity while tuning** — *needs a decision, and the wart is
   written down rather than guessed at.* The window edits the element record
   directly and does not push an undo step per slider tick: one step per tick
   would bury the history an artist actually wants, and the bake — the moment the
   change reaches the drawing — already is one step. `NoteEffectEdited` keeps the
   document marked dirty so nothing is lost silently. The cost is real: undo
   taken *after* tuning and *before* baking restores the parameters along with
   the document, because undo swaps a whole `Doc`. The three ways out are a
   coalescing step per gesture (needs a gesture notion the field rows do not
   have), keeping the sims dictionary out of the undo snapshot entirely (cheap,
   and makes deleting an element unrecoverable), or leaving it as it is and
   saying so in the manual. Not decided.

5f. **The detach rule's call sites** — split out of step 5 rather than dropped.
   `SimBakeOps.Detach` exists and is tested; what does not exist is the wiring
   that calls it from every path that changes a baked stroke's geometry, colour
   or brush, plus a re-attach command for handing a stroke back. It is its own
   branch because it is an edit to `MainViewModel`'s stroke paths — the hottest
   file in `HOTSPOTS.md` — and has nothing in common with a window: one
   objective, one branch, and an "and" in the sentence means two.
6. **Smoke** (same solver, density instead of temperature, embers off by
   default) — **landed 2026-08-19**, and it took two things the plan had not
   named because neither is visible until it is rendered.

   - **A smoke emitter has to be warm.** Buoyancy reads temperature and `Weight`
     reads density, so an emitter at zero heat is pushed *down* by its own mass
     and spreads on the floor as a pancake — four identical frames of a flat
     blob, which is what the first smoke render was. Smoke rises because it is
     hot, and the preset now says so. Measured: 0 heat reaches 7.2 cells above
     the emitter, 0.4 reaches 26.6.
   - **Bands are concentric by construction, and a lit volume is not.** They are
     iso-contours, so every one is centred on the same core; unshaded smoke is
     an onion however it is coloured. `LineTreatment.ShadeOffset` slides band
     `b` by `b/(bands-1)` of itself toward `LightAngleDeg`, which puts the
     highlight on the lit side and crowds the rest into a crescent opposite.
     Band 0 never moves — it is the silhouette, and a silhouette that slid off
     its own volume would not be one.

   It is on the **treatment** rather than on the element because it is a style
   decision — how this production draws a lit volume — and it shares its angle
   with the line-weight driver so one light serves both halves of lighting.
   A translation of the contour rather than a second field: tracing a level from
   the field sampled at `p - L·s` gives exactly this contour, so the cheap
   version and the accurate one are the same picture and the cheap one costs a
   loop over points instead of a resample of every cell.

   **Clamped to the silhouette's box**, and that was found by rendering the
   slider at its end: past a certain offset the pale band pokes out beyond the
   dark outline and stops reading as a highlight, becoming a second paler shape
   sitting on top. An artist takes a slider to its end to find out what it does.
   Bounded by boxes rather than by contours because that is the containment
   somebody can actually see, and it costs a pass over points rather than a
   polygon clip. The cost, stated: past the clamp the slider does nothing.
6b. **Explosions** — **landed 2026-08-19**. `Emitter.EmitFrom`/`EmitUntil` bound
   emission to a frame or two, and `Emitter.Burst` expands the front. Debris
   trails needed nothing new: a travelling emitter already lays a trail the
   fluid does not follow.

   **A burst is a volume source in the pressure solve, not an outward
   velocity** — `FluidSolver.AddExpansion`, consumed by one projection and
   cleared. The projection solves ∇²p = ∇·u and drives ∇·u to zero; give it
   ∇²p = ∇·u − s and it leaves exactly *s* worth of outward flow, which is how a
   combustion front is modelled and what an explosion briefly is.

   **The reasoning that motivated it was too strong, and the test caught it.**
   The claim written first was that a radial velocity achieves *nothing*,
   because the projection removes divergence. The test written to pin that
   failed: a push moves the front perfectly well. What the projection forbids is
   the fluid occupying more room, so a push is served by *displacement* instead
   — the fluid rolls outward and the middle is evacuated behind it, where an
   expansion keeps it filled. At matched reach the centre holds 0.45 against
   0.50: real, visible across a sequence as a fireball with a hole in it versus
   one that fills out, and far less than the argument promised.
   `A_Radial_Push_Hollows_The_Middle_Where_Expansion_Keeps_It` is the
   measurement, kept so the overclaim cannot come back.

   The first evidence for the overclaim was itself confounded, and that is the
   lesson worth more than the feature: a burst was measured against a plume
   whose buoyancy swamped it, so "it did nothing" was read off a test where
   nothing could have shown. `docs/DESIGN-performance.md`'s rule again — *the
   number was real and the attribution was not.*

   **A known consequence, not a defect.** `BandLow`/`BandHigh` are fractions of
   the peak the element reaches *anywhere*, and a blast peaks on frame one — so
   the dispersing smoke is measured against a value it will never approach again
   and leaves the bands early. Per-frame peaks would fix it and reintroduce
   flicker for every steady plume, which is the trade the whole-element peak was
   chosen for. The manual says to lower **Band low** for a blast.
6d. **Effects — several elements that are one thing** — **landed 2026-08-19**,
   answering "like Unity's particle system, could we combine and layer these?"
   Layering already worked, because elements have always baked to a layer each;
   what did not exist was any way to say three of them belong together.

   **A named set and batch operations, not a transform applied at bake time.**
   The obvious design is a group origin and frame offset added to each member
   when it renders, the way a scene graph works. It was rejected after counting
   the call sites: placement is read in the solve, in the trace, in the mask
   rasteriser, in the bake and in the preview's frame range, and an offset
   missed at any one of them is a bake that lands somewhere other than where the
   preview showed it. So `SimGroupOps.Move` and `Retime` write the members' own
   records, every element's origin stays honestly its origin, the bake path
   never learns groups exist, and ungrouping is lossless by construction.

   **The additive form is already provided one level up**, which is what settles
   it: `SymbolPlacement` has `X`, `Y` and `FrameOffset`, so once a group bakes
   to a symbol, placing it twice at different times is placement work. An offset
   on the group as well would be two mechanisms for one job, and the one on the
   placement can be used many times over.

   **Retiming shifts, never aligns.** The smoke starting four frames after the
   flash is what makes it read as one event, so every member moves by the same
   delta and the earliest one clamps at frame zero rather than a member being
   pushed negative and silently stopping being drawn.

   **Elements do not interact**, and that is the trade that buys the per-element
   grid: a small hot fireball at 4 document pixels per cell beside a slow smoke
   at 10, each paying only for its own resolution. One shared grid for the group
   would allow a blast to shove its own smoke and would cost every member the
   finest resolution any of them needed. Matching wind or burst is the answer
   for now, and it is in the manual.

6f. **Scatter — flames on a surface rather than a surface on fire** —
   **landed 2026-08-19**. An emitter feeds every cell it covers, so nothing can
   detach from an area: whatever rises is replaced from below the same frame,
   and a painted hem reads as one continuous burning edge (Q125's finding).
   `Emitter.Scatter` picks discrete sites over the same area instead. Measured:
   a continuous hem is 99% alight in one unbroken run, the same hem scattered is
   46% alight in eight separate flames.

   **This supersedes 5c-i as the answer to that problem.** Flicker was the plan
   — emission modulated in *time* — and scatter is better because it is spatial
   and *stable*: the gaps are in the same place every frame, so what leaves a
   site actually leaves. Flicker is still wanted for shimmer and stays on the
   list as its own want.

   **Buckets, not a lattice**, and that was a bug fix rather than a preference.
   The first version walked lattice points and kept the ones that landed on ink,
   which fails exactly where the feature is aimed: a hem six cells thick against
   a spacing of eight means most lattice rows miss it, so the number of flames
   depended on where the garment sat relative to a grid anchored at the origin
   — moving it two cells could halve the fire. Now every bucket the shape
   touches contributes one site, picked by highest hash among the inked cells in
   it, ordered before use because a dictionary's order is not part of its
   contract.

   **A site is half a spacing across, not the emitter's radius.** For a disc or
   a segment the emitter's radius is the extent of the *shape* the sites are
   scattered over, so deriving a site from it made every site as big as the disc
   containing them — a scattered disc came out as one solid blob and the feature
   silently did nothing on two of the three shapes.

   **Two corrections the measurements forced, both worth more than the
   feature.** The design said `HeatVariation` would give tall flames beside
   short ones, reasoning that a flame is as tall as its heat survives `Cooling`.
   It does not: height is roughly logarithmic in heat, so ±60% barely shows —
   and the spread is *already there* with every variation at zero (heights of
   10, 16, 24, 30, 38, 42, 44), because the fluid makes it. A site with
   neighbours either side is fed by their rising column and runs tall; one on
   the end of a run does not. So scatter gives height variation free, and
   `HeatVariation` does something else: it varies *fierceness*, which for fire
   is which colour bands each flame reaches.

   And **both controls only read cleanly at the stamp.** Downstream the plumes
   lean into each other and blend: three separate metrics for size variation
   moved the wrong way or not at all before one — the narrowest run, which
   cannot be two sites that met — meant what it said. That is
   `docs/DESIGN-performance.md`'s rule for the fourth time in this feature: the
   number was real and the attribution was not.

6g. **Presets — an effect tuned once, used again** — **landed 2026-08-19**.
   `EffectPreset` keeps a group's *parameters*; `EffectPresets.Capture` and
   `Instantiate` are the two halves, and `EffectPresetStore` is the artist's own
   shelf beside their brushes. Verified by rendering: an effect made from a
   preset draws **exactly** what the original drew, translated — compared
   relative to each element's origin, because a preset is stored relative and an
   absolute comparison reports a difference that is the feature working.

   **Relative on purpose.** Capture subtracts the group's own corner and first
   frame, so what is kept is the shape of the effect rather than where it
   happened to be. The offsets *between* members are untouched, since those are
   the effect.

   **A library is a source to choose from, not a live dependency** — the
   decision `SymbolScopes` already took, and easier here: using a preset copies
   its parameters in, and nothing renders from the shelf at all. So a document
   keeps working with the library gone, and editing a preset does not reach back
   into effects already made from it.

   **Layer references travel by name, and that is the one real decision.**
   `Emitter.MaskLayerId` and `SimElement.ObstacleLayerId` name layers in *this*
   document; carried into another they name nothing, and an emitter pointed at a
   missing layer emits nothing at all — a preset that looks fine until somebody
   bakes. So capture keeps the layer's *name* and instantiate matches it. A
   production calling the cloak layer "cloak" in every scene gets it
   reconnected; anything else gets the reference dropped and is told which name
   was missing. `MissingLayers` answers that *before* instantiating, so a window
   can warn rather than report. Two alternatives were rejected: dropping the
   reference outright makes a burning-hem preset emit from a dot with no
   explanation, and prompting for a layer puts a dialog in the way of the common
   case where the name matches.

   **Global only, and that is a smaller claim than it looks.** Symbols have a
   project scope because a project must re-render on another machine with no
   library installed. A preset has no such duty, because nothing renders from
   it. A project shelf is worth having so a show can carry its own fire, and it
   is a separate change with nothing here to undo.

6e. **An effect as an `Fx` symbol** (Q129) — the next branch, and most of it
   exists already: `Symbol.Frames` is a `List<Frame>`, `SymbolKind.Fx` is in the
   enum with nothing producing it, and `SymbolPlacement` carries `FrameOffset`,
   scale, angle, opacity and a swatch override. A baked group placed as a symbol
   therefore gets instancing, per-placement timing, flip and recolour for free.
   Q129 decided the symbol **keeps the group record**, so *edit this effect*
   reopens the window and every placement updates — with the two costs that
   choice carries written down there: the group is serialized twice and can
   drift, and a re-bake that changes the frame count moves existing placements'
   timing under them.

6c. Goo through the metaball source, then water.
7. **Style inference** — a reference drawing in, a `LineTreatment` out, judged by
   baking it beside the reference. After step 4 because it needs a look to be
   judged against, and under gate G12 from the first line.

Steps 1–3 are deliberately separable so that a bad first result is diagnosable:
Q116 chose fire, which puts a new field, a colour ramp and a particle pass in
front of the artist at once, and splitting the build is how that stays
debuggable.

## Not decided here

- **Water, in detail.** Named as a field source above, but the mechanism needs
  its own note: a free surface means the contour *is* the simulation rather than
  a threshold of it, plus splash and droplet separation — and it is the effect
  that most wants the coherent-contour upgrade Q116 declined.
- **Goo, in detail.** Likewise named above and likewise unspecified: particle
  count and lifetime, how the metaball kernel is chosen so blobs merge at a
  believable distance, and whether the particles are the same ones the ember pass
  uses or a second system. The point of naming it now is the tracer's interface,
  not its own design.
- **GPU.** `DESIGN-gpu-compositing.md` records why compositing is on the CPU. A
  bake at these grid sizes does not need the lever, and reaching for it would
  tie an authoring feature to a rendering decision nobody has made.
- **Fluid that flows around drawn art.** Rasterise the frame's ink into the
  solver as a boundary condition — a natural step 7 and cheap once the solver
  exists, but it makes the bake depend on other layers, which is a record
  question rather than a solver one.
