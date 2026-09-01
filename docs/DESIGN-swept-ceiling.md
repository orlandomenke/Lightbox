# The ceiling of a swept soft mark (B349)

*Design note, 2026-09-02. Measured before it was believed; the measurements are
`tests/Lightbox.Raster.Tests/SweptCeilingTests.cs` and run in the suite.*

## The defect, restated as geometry

Q157 gave a soft brush a **ceiling**: no pixel of a mark may be more opaque
than the brush's own footprint allows at that point, so that a hardness-0.35
brush keeps a hardness-0.35 edge instead of saturating into a hard one. The
ceiling is accumulated as a running **maximum** of dab shapes
(`StampFootprint`, `Lighten`), and B349 is what that maximum looks like where
the artist has swept back and forth: between two passes no dab centre is near,
the maximum dips, and because the mark has saturated to the ceiling there, the
dip is printed at full contrast — lines along the sweep, and a hard rim where
the core meets the halo.

B349's afternoon proved something stronger than "the repairs tried did not
work": **no operator on that buffer can work.** Flattening a ripple means
lowering peaks or raising troughs; raising troughs while keeping peaks leaves
the difference, and lowering anything clips a lone dab against its own
profile, which is the one thing the ceiling exists to keep exact. The blur that
came closest costs a lone dab 4%.

So the defect is in what the ceiling *is*.

## The definition

Today's ceiling at a pixel is *the best any single dab does there*:

    ceiling_today(p) = max_i  F( |p − c_i| )

where `F` is the dab's radial shape — and the engine's `F` is a plain ramp,
`1` out to `hardness·R` and then linear to `0` at `R`.

The candidate is *the dab's shape, applied to how far inside the stroke's
reach the pixel sits*:

    ceiling(p) = F( R − d(p) ),   d(p) = distance from p to the edge of  ⋃_i disc(c_i, R)

With the ramp written out, that is one line:

    ceiling(p) = min( 1,  d(p) / ((1 − hardness) · R) )

— the distance to the edge of everything the stroke reached, over the width of
the brush's own falloff.

**Why it cannot clip.** For any pixel, take the dab centre nearest to it. That
dab's whole disc lies inside the union, so the distance to the union's edge is
at least the distance to that disc's edge, and a falling `F` of a larger
argument is a larger value. Hence `ceiling ≥ ceiling_today` everywhere — the
new ceiling is a *relaxation* of the old one, never a cut. That is the
requirement B349 showed no post-hoc operator could meet, and it is met here by
definition rather than by operator.

**Where the two coincide.** For a lone dab the union is that dab's disc and
`R − d` is the radius: identical. Across a straight stroke, through a dab's
centre, the union's edge is `R − |y|` away: identical — which is exactly the
column Q157's tests measure. Along the stroke they differ: today reads `F(0)`
on a dab centre and `F(pitch/2)` between two, the candidate reads the stadium's
edge and is flat. That difference is the dab-pitch ripple B349 found that finer
sampling could not remove, on a single pass.

## Measured

Size 70, flow 1, 900×460, eight passes at the pitch that ridges worst under
today's ceiling. Ripple is B349's own metric — the detrended peak-to-trough of a
cut through the interior, out of 255 — with the cut kept clear of the outermost
passes' own falloff.

| | today | candidate | uncapped mark |
| --- | ---: | ---: | ---: |
| Soft round (hardness 0.35), pitch 40 | **82.4** | **0.0** | 0.0 |
| Airbrush (hardness 0.05), pitch 40 | **138.3** | 2.1 | 2.1 |

The Airbrush's 2.1 is the uncapped mark's own texture — a fully soft brush at
flow 1 does not quite saturate between 40 px passes — and the candidate adds
nothing to it. Today's ceiling adds 136 levels.

| constraint | Soft round | Airbrush |
| --- | ---: | ---: |
| lone dab, worst pixel against today (Q157's ±2) | 2 | 2 |
| straight stroke, cross-profile through a dab centre | 1 | 1 |
| straight stroke, ripple along the centreline (today → candidate) | 0 → 0 | 8 → 0 |
| pixels where the candidate is below today's ceiling by > 3 | 0 | 0 |

Two probe-side lessons, recorded because they cost two runs: Skia evaluates a
dab at pixel *centres*, so the column `r` pixels from a dab centre samples the
shape at `√((r+½)² + ¼)`, not `r`; and the pixel's centre has to be exactly a
supersample centre. Both read as a half-pixel shift, which in the Airbrush's
seven-levels-per-pixel falloff was seven levels of "residual" that was never in
the definition.

## Pitch matters, and the report's pitch did not ridge Soft round

At the report's 18.4 px pitch, Soft round at size 70 shows **no** ripple under
today's ceiling — its flat core (hardness 0.35 × radius 35 ≈ 12 px) covers that
pitch. The ridge appears from about 28 px and grows with the pitch: 14, 38, 61,
82 at 28, 32, 36, 40. The Airbrush ridges at every pitch (57 at 18.4). So the
reported picture was a large brush at a wider pitch than measured, or the
softer preset; either way the mechanism is the same and the candidate is flat
at every pitch tried.

## What it costs, and where

The candidate needs a **distance transform** of the union of dab supports.
Exact Euclidean, two separable passes (Felzenszwalb and Huttenlocher), `O(n)`
in pixels, deterministic — no random access, no clock, the same pixels for the
same dabs (invariant 2). The probe's implementation is naive (supersampled 5×,
allocating per line) and costs ~340 ms for the 0.41 Mpx image; an in-engine
one runs at document resolution with exact geometry and should sit near
5 ns/px.

Three places compute a ceiling today, and each has its own cost shape:

| path | today | with the candidate |
| --- | --- | --- |
| **commit** (`CapToFootprint`, whole mark) | one max-stamp per dab, one min per pixel | plus one EDT over the mark's bounds — same order as the min loop |
| **live, band-local** (`CapToFootprintBand`, B293/B313) | per event: max-stamp new dabs, min over the band | per event: EDT over band ⊕ halo, where the halo is `R` — a pixel's distance saturates at `R`, so nothing beyond `R` can change it |
| **preview scale** (`LiveFootprintScale`, B189) | the same at 0.375× | the same at 0.375×; distances scale with the buffer |

The live band is the one that touches feel. A 173×173 band with a 35 px halo is
243² ≈ 59 k pixels; at 5 ns/px that is ~0.3 ms per event against the 0.9 ms
the cap costs today and the ~4.8 ms the stamp costs (B189). **It has to be
measured under the existing per-event budgets before it ships, not estimated
here** — that is the first step of the implementation branch, and the promise
this note is written under is that nothing reaches the paint path that makes
the pen wait.

## Implementation plan

1. `StampFootprint` records the dab's **support** as well as its shape: the
   hard disc of radius `R` into a second channel of the same buffer (the
   footprint is opaque RGB and only red is read today). `Lighten` keeps it a
   union. A third channel carries the local falloff width `(1 − hardness)·R`
   as a maximum, so a pressure-varying stroke evaluates the ramp with the
   width of the widest dab covering the pixel.
2. `CapToFootprint` / `CapToFootprintBand` compute `d` over the band ⊕ `R`
   halo from the support channel, form `min(1, d / width)`, and take
   `max(red, that)` — the max is belt and braces: the theorem says it never
   binds, and keeping it means a pressure ramp can only ever relax the ceiling
   relative to today, never tighten it.
3. The live path's halo for the ceiling becomes `R` rather than
   `LivePassHalo`; the whole-mark support buffer already exists incrementally,
   so the EDT reads outside the band for free.
4. Fingerprints move, and that is the honest cost Q157 also paid:
   `RuntimeDeterminismTests`, `BrushPresetRenderFingerprintTests` and any
   document fingerprint drawn with an overlapping soft stroke re-record, with
   this note as the reason. Every existing soft-brush document renders with
   fewer ridges and no other difference.
5. `SweptCeilingTests` stops computing its own candidate and asserts the
   engine's ceiling directly: sweep ripple ≤ uncapped + 0.5, along-stroke
   ripple 0, and `FootprintCapTests` unchanged as Q157's guard.

## What is deliberately not in scope

- **Media stay excluded** from the ceiling (Q157 measured why: the fringe is
  the fluid's fuel). Nothing here changes `NeedsFootprintCap`.
- **A bitmap tip** keeps its own route; its support is its alpha, not a disc.
- **Q175 holds**: this is the soft-falloff paint engine's ceiling, not a stage
  every engine must survive.
