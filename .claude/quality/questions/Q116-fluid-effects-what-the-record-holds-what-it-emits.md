# Q116 · Fluid effects — what the record holds, what the sim emits, and how the outline behaves — **answered 2026-08-18**

The owner asked whether a performant 2D fluid simulation for water, fire and
smoke was possible, rendered as drawn outlines with filled colour rather than as
pixels. Feasibility was measured before anything was asked (see
`docs/DESIGN-fluid-effects.md` for the numbers); what could not be measured were
four choices that between them decide the whole shape of the feature. All four
were prompted together, and three of the four went against the recommendation.

**1. Where a simulated effect lives in the record: bake to strokes, keep the
parameters** (recommended, accepted). The sim writes ordinary `ToolKind.Fill`
and `ToolKind.Brush` strokes into each frame; the generator's parameters live in
a `Doc.Sims` registry the strokes reference by id. This is the `StrokePath`
precedent exactly — an authored description alongside geometry, with `Points`
staying the truth — and it means the renderer, `StrokePicker`, transform, undo,
`SpriteSheetExporter`, the AI payload and the MCP surface need no change
whatever. The cost accepted: re-simulating discards hand edits inside the
element's stroke set, so the UI has to say so before it does it. The
alternatives had named costs — deriving strokes at render time makes every
consumer learn about a second kind of stroke and makes opening a document pay a
simulation; baking without parameters makes changing one number a 48-frame
redo.

**2. What the sim emits: contour bands *and* particles, both from the start**
(recommended: bands alone; **owner chose both**). Bands are iso-levels of the
field traced to closed contours — the outline-and-fill the request asked for.
Particles are advected through the same velocity field and become short tapered
strokes.

The cost of taking both at once is roughly double the first slice, and the
specific risk is that neither gets tuned properly before the other lands on top
of it: a band ramp that reads badly is hard to diagnose with embers drawn over
it. Two things make the choice more defensible than it looked when it was put,
and both come from decision 4 landing on fire — **fire without embers reads as
a flat shape**, so for this particular first effect the two halves are not
independent features but one look; and the particle path shares the solver,
the `Hash01` seeding and the stroke-authoring stage with the band path, so the
duplicated work is the tuning rather than the machinery. The mitigation is
ordering inside the slice rather than scope: bands are built and judged first,
particles are added to a band ramp already agreed.

**3. How the outline behaves frame to frame: trace each frame independently**
(recommended: advect the previous frame's contour and re-project; **owner chose
independent tracing**). Each frame's contours are traced from that frame's field
with no reference to the previous one.

This is the decision that sets the difficulty, and the cheap option was taken
deliberately. What it costs, written down because it will not be obvious from
the code:

- **The boil is a property of the tracer, not something authored**, so it cannot
  be dialled down. A flame should boil and will; a steam wisp or a water surface
  will boil exactly as hard, and there is no knob that says otherwise. Decision
  4 landing on fire is what makes this affordable now and it does not stay
  affordable when water arrives.
- **Stroke geometry is independent between frames**, so `StrokeMatcher` finds no
  correspondence and the inbetweener cannot work inside a baked element. Baking
  on 2s is the mitigation, and it is what an animator would do anyway.
- **File size is N independent polylines**, not one polyline and N deltas.

Two knobs reduce boil without building the advection machinery and both belong
in the first slice because they are nearly free: quantise the iso-level so the
contour does not chase sub-threshold noise, and bake every K frames and expose
on K. The upgrade path stays open — advect-and-re-project changes how `Points`
is computed and nothing else in the record.

**4. Which effect ships first: fire** (recommended: smoke; **owner chose
fire**). Fire needs a temperature field in addition to density, and the band
levels map to a heat ramp rather than to density, so a colour decision arrives
before the base chain has been proven end to end. Smoke was recommended for
being the most forgiving of every approximation in the pipeline — if a
smoke plume reads, the tracer, the band spec and the stroke authoring are all
known good before anything harder is attempted.

What the choice buys, against that: fire is the effect where **decision 3 is
most nearly correct** rather than merely cheap, because hand-drawn fire boils
hard and per-frame retracing is what an effects animator actually does. Taking
the forgiving effect first would have proved the pipeline against the case that
hides the tracer's worst property. The accepted cost is that the temperature
field, the heat ramp and the ember pass are all being judged at once, on the
first thing anybody sees — so a bad result will be ambiguous about which of the
three is wrong, and the build order in the design note separates them into
testable steps for exactly that reason.
