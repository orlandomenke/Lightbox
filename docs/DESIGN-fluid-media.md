# Fluid and viscous media — notes for the next brush-engine pass

**Status: notes, not a plan.** Nothing here is built. This is the brief for a
future pass on `BrushEngine`, written down so it survives the gap. Recorded
2026-08-02 from a working note; the reasoning has been checked against this
engine rather than copied wholesale, and where it collides with an invariant
that is said plainly below.

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
speckle on it. Related to bug **B/M16b** (stamping arcs), which is the same
absence seen from a different angle.

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

Cheapest of the five, and visible on its own. Probably where to start.

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
  differently, which is the definition of a broken record.
- Every part of the loop must be exactly reproducible in floating point,
  including iteration order. A parallel diffusion step that reduces in
  non-deterministic order would break re-renders and make AI inbetweens
  diverge. This is the one that will bite.

**Invariant 6 — painting is bounded work — constrains (3) and (4).** Sampling
behind each dab and running a Sobel per frame are both per-dab or per-region
costs, which is fine; a diffusion pass over the whole canvas per event is not.
Any cellular-automata or bleed step has to be confined to the stroke's dirty
region.

**Open question, and it is the real one:** whether wet state crosses strokes.
Paint that dries between strokes is much easier — the state is per stroke,
bounded, and disposable. Paint that stays wet so the next stroke can pick it up
is what artists actually mean by wet media, and it puts simulation state in the
document. That decision comes before any of the five, and it belongs in
`QUESTIONS.md` rather than being settled by whoever implements first.

## Not decided here

Order of work, whether this is a new medium alongside the existing ones or a
replacement for them, the buffer format, and whether (4) belongs in the brush
engine or the compositor. All of it wants a measured spike against the charter's
budgets before anything is promised.

## Source

A working note from the user, 2026-08-02, on moving from noise to physics for
paint and ink. Read it as a brief that has been checked, not as a specification:
the five pieces and their reasoning are theirs, the invariant analysis and the
open question above are this repository's.
