# Fluid and viscous media — notes for the next brush-engine pass

**Status: notes, not a plan.** Nothing here is built. This is the brief for a
future pass on `BrushEngine`, written down so it survives the gap. Recorded
2026-08-02 from a working note; the reasoning has been checked against this
engine rather than copied wholesale, and where it collides with an invariant
that is said plainly below.

**Updated 2026-08-02** with the answer to Q10 — wet paint survives for a
bounded window of strokes — which turns the document's central open question
into a constraint the rest has to satisfy, and which turns out to cost the
record nothing. One prediction the original note made has since been tested and
was wrong; it is kept below rather than quietly removed.

## What we are actually aiming at

Stated before the five pieces, because it is the thing that decides how far
each one goes.

**This is a drawing application, not a physics paper.** We are not recreating
watercolour. We are giving an artist the part of watercolour that makes a mark
say something — the pooling at an edge, the drag of a loaded brush, the tear
when it runs out — and stopping there. Where a cheap approximation and an
accurate simulation look the same to a person, the cheap one is correct and the
accurate one is a defect that costs frames.

**Performance is a constraint on this work, not a consideration after it.** The
budgets in `CHARTER.md` are the ceiling; a piece that cannot meet them is not
shipped in a slower form, it is redesigned or dropped. Realism is worth nothing
if the canvas stutters, because the thing being defended is the artist's
attention.

**Visual variation, never logical randomness.** Marks should differ the way
real media differ — because of where they are, what they are on, how fast the
hand moved — and never because of a die roll. That is invariant 2 read from the
artist's side rather than a rule fighting them, and it is also what makes the
variety reproducible.

**Where the edge is.** Krita's brush engine pushes further than most and still
leaves this on the table. The advantage is in expression, not fidelity — so
spending the frame budget chasing accuracy spends the advantage rather than
earning it.

The consequence for everything below: these are **opt-in options on presets**.
Most brushes do not use them and must not start to. The picker badges the ones
that do, derived from the settings rather than declared, and every simulated
medium ships a fast counterpart — a medium nobody can afford is a trap rather
than a feature.

## The problem it names

Our wet media vary the mark with noise: opacity jitter, scatter, granulation,
all seeded from dab position via `Hash01`. That gets texture and misses
*fluidity*, and the reason is structural rather than a matter of tuning. Noise
treats every pixel independently. Real water and viscous paint link a pixel to
its neighbours through surface tension, capillary flow and the momentum of the
stroke, so what a dab does depends on what is already on the paper and which
way the brush was travelling. No amount of better noise produces that, because
the missing thing is the coupling, not the variety.

Symptom, in our own terms: a soft-round wash reads as a tube of circles with
speckle on it.

> **A prediction this document made, and got wrong.** The original note tied
> this to bug **M16b** (stamping arcs) as "the same absence seen from a
> different angle". It was not. M16b was measured and fixed on 2026-08-02 and
> the cause was chord error — the dab walk followed the straight lines between
> recorded pen samples, putting the path up to 6 px inside a curve the artist
> drew, with a facet wider than the brush on the outside of every bend. It had
> nothing to do with fluid coupling and needed no simulation to fix; the path
> is a centripetal Catmull–Rom through the recorded points now.
>
> Worth keeping rather than deleting, because it narrows what this pass is
> for. "The marks look like separate stamps" had two causes, and the geometric
> one was the cheaper and the larger. What is left for fluid coupling is the
> part that survived the fix: a wash still reads as flat, because the pigment
> does not know what it landed on. Measure before assuming the remainder is
> what this document says it is.

## The shape of the change

We are a **stamp engine**, and the note is explicit that a stamp engine can get
there without becoming a grid fluid simulation. Five pieces, roughly in the
order they would pay off:

### 1. Separate the channels

Stop rendering straight to one RGBA buffer. Per stroke, carry:

| Buffer | What it holds |
| --- | --- |
| Pigment (RGB) | The colour particles |
| Moisture (1 channel) | Water or solvent present |
| Height (1 channel) | Physical volume of paint |

Everything below reads at least two of these. This is the piece the other four
depend on, and it is the one with real memory cost — a per-stroke bounded
region, not a full-canvas set of buffers, or invariant 6 goes.

### 2. Wet edge by capillary action

Fluid migrates to the perimeter of the footprint as the tip compresses it.
Instead of a flat radial falloff, the stamp's alpha curve **peaks just before
the edge** and then drops. That deposits more pigment and moisture at the rim:
the watercolour wet edge, rather than a soft digital glow.

Cheapest of the five, and visible on its own — see *Where to start*, which
argues for beginning here rather than with (1).

### 3. Directional advection — the smudge loop

Each stamp pulls from the canvas along the stroke vector rather than sitting
down independently:

1. Vector between this dab and the previous one.
2. Sample the canvas slightly *behind* the dab along that vector.
3. Blend a share of that sample into the dab's pigment before drawing.

This is what makes paint drag and fold rather than composite, and it is the
single change that most removes the isolated look. Note the overlap with our
existing smudge tool: that samples and re-deposits already, so the machinery is
partly there and the work is making it a property of a *brush* rather than a
tool of its own.

### 4. Heightmap for shading

Every overlapping dab adds to the height buffer. Run a Sobel over it for
normals, then a Blinn-Phong pass in screen space. A little directional light on
those normals is what makes oil and heavy gouache read as thick rather than
merely opaque. Impasto for free once the buffer from (1) exists.

### 5. Velocity-dependent deposition

Tie transfer to stroke speed rather than to a random draw:

- **Slow** — tighter spacing, more fluid deposited: pooling, heavier build-up.
- **Fast** — wider spacing, less fluid: the medium starves and tears, which is
  a real dry-brush break instead of artificial grain.

We already vary spacing; what is missing is the fluid volume moving with it.

## Where this collides with the invariants, and what wins

This is the part the source note could not know, and it is the part that
decides whether any of it can ship.

**Invariant 2 — no randomness in rendering — is not threatened.** Everything
above is deterministic: advection is a function of the stroke's own geometry,
capillary flow of the footprint, deposition of speed computed from recorded
timestamps. That is *better* than what we have, not worse: it replaces
hash-driven variety with variety that has a cause. Nothing here needs an RNG,
and nothing here may acquire one.

**Invariant 1 — the stroke record is the document — is the hard one.** A
simulation whose result depends on the order dabs were laid and on what was
already wet is still reproducible, but only if the *whole* stroke sequence is
replayed identically from a known start state. Two consequences:

- The state must live in the stroke record or be derivable from it. A moisture
  buffer that persists between strokes and is not saved makes a reload render
  differently, which is the definition of a broken record. **Q10's answer takes
  the second branch** — see below; the window is bounded, so the state is
  recomputed from strokes already in the record and nothing new is stored.
- Every part of the loop must be exactly reproducible in floating point,
  including iteration order. A parallel diffusion step that reduces in
  non-deterministic order would break re-renders and make AI inbetweens
  diverge. This is the one that will bite.

**Invariant 6 — painting is bounded work — constrains (3) and (4).** Sampling
behind each dab and running a Sobel per frame are both per-dab or per-region
costs, which is fine; a diffusion pass over the whole canvas per event is not.
Any cellular-automata or bleed step has to be confined to the stroke's dirty
region.

## Does wet state cross strokes — answered

**Q10, answered 2026-08-02: yes, for a bounded window, and the size of the
window is a brush setting.** `BrushSettings.WetStrokes`: `0` means the paint is
dry the moment the pen lifts, `N` means the mark stays wet for the next `N`
strokes and anything laid within that window can pick it up.

The paint declares its own life, not the brush that arrives later. A stroke
made with `WetStrokes = 3` stays available for three strokes whatever is
painted next; a stroke made dry is dry immediately even if the following brush
is soaking. That is the physical reading and it is what "0 is directly dry"
means. Strokes with different windows simply expire at different times — there
is nothing to reconcile.

### Why bounded changes everything

The thing that made this hard was the assumption that persistent wetness means
persistent *state*, and therefore a moisture buffer in the file. A bounded
window means it does not:

> **The wet state at stroke *k* is a pure function of strokes *k−N* … *k−1*,
> and those are already in the record.**

So nothing new is saved, and invariant 1 is untouched. `Doc` gains no buffer,
the file gains no pixels, and a reload reproduces the painting by doing what it
already does — replaying strokes in order. Unbounded wetness (option (b)) would
have needed either the whole history or a stored buffer; that is the whole
reason (c) beat it, and it is worth restating whenever somebody proposes
"just keep the buffer".

Default `0` keeps every existing document byte-identical, and every existing
render bit-identical. Absent by default, the camera's rule again.

### What it does cost

Three things, all of them bounded, none of them free:

**A full frame render is free.** `Materialize` already replays every stroke in
order from an empty surface, so the moisture channel simply rides along. This
is the case that needs no new thinking at all.

**The incremental append is where the work is.** `FrameRasterizer.Append`
stamps one new stroke onto the cached RGBA bitmap — and RGBA does not carry
moisture, so a wet stroke cannot be appended from it. Two ways out, and the
choice wants a measured spike rather than an argument:

- Keep a moisture channel beside the cached frame, bounded to the region the
  last `N` strokes touched, and append against that.
- Keep nothing, and replay the last `N` strokes into a scratch buffer to
  reconstruct the moisture before appending.

The first costs memory that scales with the wet region, the second costs time
that scales with `N` and with those strokes' size. `N` is small in practice —
an artist working wet-into-wet means two or three strokes, not forty — which is
what makes either affordable and is also the argument for capping `N` low.

**An edit invalidates forward, by `N`.** Change or undo stroke *j* and the
renders of *j* … *j+N* are stale, not just *j*. Bounded and predictable, which
is exactly what (b) could not promise — there, an edit anywhere invalidated
everything after it. The frame cache and the scoped-edit path both have to know
this before any of the five pieces below ship.

### The two it did not settle, now settled

Two things the answer does not settle. Both are in `QUESTIONS.md`.

Both now answered.

- **Q13 — what counts as the same sheet of paper: (c).** The window is per
  frame and per layer, and **generated strokes never carry wetness** — the
  inbetweener and the MCP surface write `WetStrokes = 0` whatever the source
  said. The first half matches Q6; the second is a determinism guard, because
  an inbetween whose look depended on how many strokes the generator emitted
  would diverge between runs.
- **Q14 — what an eraser does: (a).** An eraser is a stroke like any other. It
  spends one of the window's `N` and removes pigment, and the moisture goes
  with the pigment it belonged to. An eraser that *smears* wet paint is the
  physical answer and is a brush somebody builds later on the advection loop —
  **if that turns out to matter, the fix is a new brush, not a change to the
  eraser.**

## Measured, 2026-08-02: what is actually wrong today

An artist's report — "ink wash and watercolour have no pooling spots, gouache
and oil have no height nor edges that catch light, it still looks like texture
made from noise, and nothing spreads or drags like a wet medium" — turned into
numbers. Every part of it was right, and two parts are worse than the report
suggested. Logged as **B23**, **B24** and **B25**.

| Claim | Measurement | Verdict |
| --- | --- | --- |
| No height, no light-catching edges | `Body`/`Relief`/`BristleDrag`/`Pickup` at 0 versus 1: **0 of 41 600 pixels differ** | Not implemented at all |
| No pooling | Rim/interior density by `EdgePull`: 0.0 → 1.22, 0.4 → **1.83**, 0.8 → 0.68, 1.0 → 0.65. Watercolor ships **0.7** | Implemented, but the response inverts above ~0.5 and the preset is past it |
| Looks like noise | Interior density 0.66 without a medium, **0.13–0.25** with one | The pigment is mostly gone, so texture is all that is left |
| Nothing spreads | Mark height 45 px flat, 50 px at 24 flow steps | ~10% — effectively no bleed |

Two things this changes about the plan below.

**The five pieces are not all unbuilt, and not all built.** `FluidLattice` is a
real Curtis-style shallow-water model and it does run. What is missing is
narrower and more fixable than "build a simulation": conserve the pigment
(B25), make the capillary term monotonic (B24), and implement the four settings
that already have controls (B23). Piece (1) — the channels — is what B23's
height half needs; the rest is repair.

**Impasto may not need the buffer.** Piece (4) assumes a height channel from
piece (1), but a first pass can take normals from the stroke's own alpha
coverage, which already exists. That gets a light-catching edge on gouache and
oil without the memory, and it is worth trying before committing to the buffer.

### On brush tip textures

Raised as a possible answer, and it is a partial one — worth being precise
about which part.

**What tip textures do reach:** the shape of a mark, its transparency
variation, and — with a directional tip and `AngleFollowsDirection`, both of
which exist — a convincing bristle streak. That is `BristleDrag` in appearance
if not in mechanism, and it is much cheaper than the advection loop. They are
also the honest answer to "it looks like noise": a scanned bristle or spatter
tip is structured where `Hash01` is not, and structure is what reads as a tool
rather than as grain.

**What they cannot reach**, and the reason they are not a substitute for the
pass: edge pooling, flow, surface tension and pickup are all *couplings* —
each depends on what is already on the paper — and a tip is a stamp that knows
nothing about the canvas. Neither can they produce relief, which needs
shading from a surface normal rather than a shape.

So: tip textures are a cheap, real improvement to two of the four complaints
and no help at all with the other two. Worth doing early for that reason,
provided the doc does not then claim the medium problem is solved.

## Where to start

Not with the channels, despite (1) being what the other four depend on.

**Start with (2), the wet edge**, because it is the only one of the five that
is visible on its own, needs no buffer, no window and no new state, and can be
measured against the existing budgets in an afternoon. If a peaked alpha curve
does not make a wash read better, that is worth knowing before building three
buffers to find out.

**Then (5), velocity-dependent deposition**, for the same reason: the stroke
already carries the timestamps, spacing already varies, and what is missing is
one coupling. Dry-brush tearing that comes from drawing fast is a real gain and
it does not touch the record.

Only then (1), and with it (3) and (4), which are the ones that need the
channels and the wet window and the invalidation rule. That is the expensive
half and it should be entered deliberately, with the two open questions
answered and a spike behind it.

## Not decided here

Whether this is a new medium alongside the existing ones or a replacement for
them; the buffer format; whether (4) belongs in the brush engine or the
compositor; and how to hold the moisture channel for the incremental append —
buffer beside the cache, or replay the last `N`. There is also a cap on `N` to
choose, and the argument for a low one is above rather than measured.

All of it wants a spike against the charter's budgets before anything is
promised. **G7 in particular**: three of the five pieces add per-dab or
per-region work to the paint path, and every serious stall this project has had
was in a path with no budget covering it. A budget for the wet path goes in
with the first piece that touches it, not after.

## Source

A working note from the user, 2026-08-02, on moving from noise to physics for
paint and ink. Read it as a brief that has been checked, not as a specification:
the five pieces and their reasoning are theirs; the invariant analysis, the Q10
consequences, the order of work and the two remaining questions are this
repository's.
