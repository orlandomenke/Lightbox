# Q156 · Does fixing the dab-saturation defect change existing art? — **answered 2026-08-23: yes, fix it everywhere**

Reported 2026-08-23 as *"anti aliasing which we have in our project is sub par …
the lines in my image are way more staggered"*, against an Ink-brush drawing
exported through the frame exporter. The reporter then established the thing that
located the defect: *"on display mode the visual artifacts do not differ that
much in app or export they look similar if not the same"* — so the laddering is
baked into the raster by the brush engine, not introduced by a blit filter or a
downscale on the way to the screen.

## The mechanism

**Overlapping antialiased dabs destroy antialiasing.** A dab's rim pixel carries
partial coverage; composited `SrcOver` against the previous dab's rim at the same
pixel it comes out at `1-(1-a)^n`. At the Ink brush's spacing — 0.1 × size 5, a
dab every half pixel — a stroke's *side* takes the same partial coverage from a
dozen dabs in a row and saturates to opaque.

Measured on a vertical size-5 Ink stroke, sweeping its sub-pixel position across
one pixel and integrating alpha across the mark. The exact answer is 5.000 at
every position:

| | worst width error | ink against geometry | whole-mark render |
| --- | --- | --- | --- |
| per-dab `SrcOver` (before) | **17.7%**, and biased — never thinner than asked, only fatter | +10.6% | 26.4 ms |
| one silhouette (after) | **0.08%** | +0.4% | **14.8 ms** |

The cost column is a 2000 px arc at size 5 rendered whole — the commit, the
reload, the export and every inbetween, all **1.8× faster**. The live preview
moves the other way, 1.50 → 3.19 ms per pointer event at size 5 and 0.60 → 2.02
at size 24, because it re-derives the outline from the whole dab list on every
event; that is **B290**, measured before this shipped rather than after.

Three sub-pixel positions an eighth of a pixel apart rendered *identical* pixels
before the fix. That is the staggering: a shallow diagonal — a hair strand —
holds a column for several rows and then jumps, and its apparent weight pulses
with a period of one pixel.

## The question

The fix computes coverage once for the union of the dabs instead of accumulating
it per dab, so it changes the pixels of **every stroke already drawn**.

| | What it costs |
| --- | --- |
| **Fix it everywhere** (recommended, **chosen**) | One render path. `RuntimeDeterminismTests`' fingerprint must be re-baselined, and a document saved yesterday is not pixel-identical tomorrow. |
| **Gate it per stroke** | A new nullable brush key. Every saved document stays bit-identical, but strokes already recorded keep their laddered edges permanently, and two render paths have to be tested forever. |

**Invariant 4 exists so that changing a *setting* never alters art — not to
preserve a rasterizer defect forever.** That is the distinction the answer turns
on. A per-stroke gate would be the right shape for a preference; here it would
freeze bad output into everything drawn so far and pay for the privilege with a
permanent second code path. The ledger's own rule is that a defect is fixed
rather than versioned.

## What the fix does *not* cover, and why that is deliberate

The silhouette is a union of plain circles filled with one colour at one alpha,
so `BrushEngine.DrawsAsOneSilhouette` refuses any brush whose dabs differ from
each other in colour, alpha or shape. Two exclusions are worth naming here
because they are not oversights:

- **Hardness below 1** gives each dab a radial gradient, which a union cannot
  represent — the falloff belongs to the dab, not to the silhouette. Soft brushes
  have the same saturation and need coverage accumulated with `max` instead.
  Asked at the same time and answered *separate branch*: it is a different
  mechanism, and the one-objective rule applies.
- **Size jitter, scatter, squash and bitmap tips** put geometry in the dab that
  `(position, radius)` does not carry. Excluded rather than reproduced, because
  the dynamics chain in `StampDab` mutates its own hash seed as it goes — scatter
  shifts the seed the later salts read — and a second copy of that ordering is
  exactly the kind of duplicate that comes to disagree. Widening this means
  extracting the chain so there is one copy, not writing a second one.

What survives is the hard round family — Ink, hard round, the eraser, and any
preset built from them — which is the line art the defect was reported against.
