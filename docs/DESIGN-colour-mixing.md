# Colour mixing — paint that picks up what it is laid on

**Status: being built, 2026-10-09.** Q232 holds the four decisions; this is the
design they produce, and the investigation that led to them, written for the
next session rather than for this one.

## Why this and not a better simulation

The owner's complaint, 2026-10-09: the simulated media *"look and perform like
ass"* next to Clip Studio Paint, and they would *"give up the simulation
aspects if we can visually create more realistic paint."* Measured against the
suite's own contact sheets (`scripts/visuals.sh media`) the complaint held: the
simulated watercolour is a pale tube with a seam, oil and gouache are flat blobs
with no bristle or chisel in them.

The cause is not the physics but **where the simulation sits**. `MediumSimulator`
reads only the stamped mark's *alpha*, seeds one uniform stroke colour, runs the
lattice and overwrites the scratch — so every per-dab colour the engine computed
(`JitterColor`: hue, saturation, value, a second colour) is thrown away, and a
bristle or chisel tip survives only as coverage, which the opacity map
`1 − e^(−3·mass)` then saturates: a 60 % bristle scratch reads α 0.83 against
0.95 for the solid. With a medium on, granulation, the stamped wet edge and the
paper-texture pass are skipped as well. And at the time of writing the shipped
Watercolor's `EdgePull` was 0.06 (B35), so the lattice's one unique effect was
nearly off — B427 found why it had to be, fixed the term, and put it back to
0.45.

**Clip Studio Paint has no fluid simulation.** Its wet media are textured
material tips, a paper texture multiplied in per plot, a whole-stroke
*watercolor edge* (what `ApplyWetEdge` already is), and — the thing Lightbox
lacked entirely — **colour mixing** in the Ink settings: each dab picks up the
colour already on the layer and mixes it with the brush's paint, with an
*amount of paint*, a *colour stretch* for how long the pickup persists, and a
*perceptual* mixing mode. Its oil brushes imitate impasto with the same mixing;
there is no height field. All of it is per dab, forward, single pass.

So the pass is: give the paint path per-dab colour mixing; keep tips, texture,
jitter and the stamped wet edge on under a medium; and make the lattice take
the colours it is given rather than discarding them. The lattice stays, as an
opt-in that composes with the dabs instead of replacing them.

## The record

`BrushSettings.Mixing`, a nullable `ColourMixing` block. Null is off, and null
writes nothing — the camera's rule, and the `optional-settings` skill's two
traps avoided: the block is nullable (not inert at a default), and there is no
convenience getter beside it to reintroduce the key.

| Field | Meaning | CSP's name |
| --- | --- | --- |
| `Amount` 0..1 | how much of a dab is the brush's own paint; the rest is what it picked up | Amount of paint |
| `Length` 0..1 | how much of the carried colour survives each dab — the smudge option's word for the same idea | Color stretch |
| `Reach` 0..1 | how far around the dab the ground is sampled, as a fraction of its radius | — |

Pressure does **not** drive the amount yet, deliberately. A pressure curve is
stored under a `BrushDynamic` key, and a key an older build does not know makes
that build's brush store unreadable — which it then saves over, empty. The
owner runs older builds daily, so the one mixing control an artist would reach
for by pressing waits for Q193's version field (the sensitivity guardian's
finding). Invariant 4: the block is on the stroke, so a preference change never
alters existing art.

## The engine

**Per dab, in `StampDabRange`, before the dab is drawn:**

1. **Sample the ground** under the dab: the scratch so far (the stroke's own
   earlier dabs) composited over the layer beneath, a five-point average at
   `radius × Reach` — the same sampler the dulling smudge uses.
2. **Carry.** The first dab's carried colour is the ground. Each later dab
   mixes the ground into what it carries by `Length`: at 0 the sample is
   replaced every dab and colour barely travels, at 1 it is dragged the length
   of the stroke.
3. **Deposit** the brush's colour (after its own jitter) mixed toward the
   carried colour by `(1 − Amount) × carriedAlpha`. Nothing under the brush
   means nothing to mix with, so on a blank canvas a mixing brush is exactly
   the same brush without mixing — a test pins that identity.
4. Mix in **Kubelka–Munk** (`Pigment.Mix` on K and S, mass tone back out), so
   yellow into blue goes green. Alpha is lerped; only the colour goes through
   the pigment model.

The deposit then takes the ordinary dab path — tip, roundness, flow, the
footprint ceiling — so everything a paint brush has stays. A mixing brush is
excluded from `DrawsAsOneSilhouette` and `SubdividesForFidelity` for the same
reason colour jitter is: its dabs are not interchangeable.

**The live path** stamps settled dabs once and re-stamps a tail every event
after restoring a backup, so dab *k* always sees dabs 0..*k−1* in the scratch —
the same ground the commit sees. The carried colour is checkpointed at the
settled boundary, as `SmudgeCarry` is, and the tail continues from a copy.
The layer beneath is the frame's cached bitmap, as the medium pass already
borrows it.

**Symmetry copies** reuse the identity pass's mixed colours rather than
sampling at their own position. The copy is the mark, moved — `Hash01` seeds
every other dynamic from authored coordinates for the same reason — and a copy
sampling its own ground would make two mirrored halves of one stroke disagree.
One known gap, filed as **B426**: the live path stamps the copies
into the same scratch the drawn mark samples, while the commit gives each copy
its own, so a mixing stroke crossing its axis picks up its reflection live and
not on commit.

**Where a mixing stroke is refused**, the way a smudge is: per-tile
rasterisation (`CanTile`), a region repaint on undo (`RepaintRegion`) and a
layer merge all replay strokes over pixels a full render would not have shown
them, so each takes the whole-frame route for a mixing stroke.

**Bounded (invariant 6):** one five-point sample of two bitmaps and one mix per
dab, nothing proportional to the canvas.

## The medium takes the colours it is given

`MediumSimulator` now seeds each cell's pigment colour from the downsampled
scratch — the colour the dabs actually laid there, unpremultiplied — rather
than from one stroke colour. `WriteBack` already recovers colour from the
deposit's mass, so nothing else changes. Existing media documents whose brushes
carried colour jitter re-render with that jitter restored. Two honest
qualifications from review: no render fingerprint in the suite covers a medium
stroke, so nothing was re-recorded and nothing would catch a further change;
and a plain single-colour stroke's *rim* cells — alpha of a few levels, above
the seed threshold — read back a colour quantised by premultiplied storage, so
those bytes move by a few levels too. Invisible at that alpha, and said here
rather than claimed away.

## Cost

Mixing reads the canvas back per dab, so `BrushCostOf` puts a mixing brush in
the **Expressive** tier with the smudge and the media, and the picker badge
says why. The four media presets already sat there.

## What this is not

Not a height field, not pickup of a *patch* (that is the smearing smudge, and
it is what a palette-knife brush would be built on later), not a change to the
fluid lattice's physics. The lattice's edge pull, bleed and glazing remain the
things only it can do; whether the shipped presets should lean on them again
is a later decision, made by looking at the contact sheets once the dabs carry
real colour.

## Evidence

`ColourMixingTests` (Raster): yellow over blue goes green not grey; a blank
canvas leaves a mixing brush identical to itself without mixing; length
carries a picked-up colour further; deterministic; not a silhouette.
`ColourMixingRecordTests` (Core): absent from the file until used; round-trips;
clones deep. `LiveMatchesCommittedTests.AMixingBrushLooksTheSameLiveAndCommitted`
(App). `MediumSeedColourTests` (Raster): the lattice keeps per-dab colour.
