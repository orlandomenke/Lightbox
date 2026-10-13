# Oil has hairs, and the hairs are the stroke's

*Design note, 2026-10-10. Measurements are `tests/Lightbox.Raster.Tests/HairCeilingTests.cs`
and run in the suite; the sheet the suite writes under `LIGHTBOX_VISUALS`
(`oil-hairs.png`) is the picture of it.*

## The finding

The owner, comparing with Clip Studio Paint: *"Oil paint has volume, strokes
influenced by the hairs."* Rendered as shipped, the Oil was a smooth,
soft-edged ribbon with a faint pillow shade. The bristle tip it uses, turned to
the stroke, left no hair in the mark at all — **even with the medium switched
off**. Not the lattice, then. The dabs: at spacing 0.06 and flow 0.9 about
sixteen dabs land on every pixel of the path, and sixteen source-over
composites of anything above 0.2 alpha saturate to full. The tip shapes the
outline and nothing inside it. (The colour-mixing note measured the same thing
from the other side: a 60 % bristle scratch reads 0.83 against 0.95 solid.)

What a real loaded brush leaves is not a texture on the mark. It is **the same
hair at the same place across the stroke**, from the first dab to the last —
hair seven is a ridge all the way along, hair three is a scratch — and that is
exactly what overlap destroys when the hairs belong to the dab and survives
when they belong to the stroke.

## What the engine already had

The footprint ceiling (Q157, B349): a running maximum of dab shapes, kept in an
opaque surface under `Lighten`, that caps the mark's alpha so a soft brush
keeps a soft edge instead of saturating. It is already carried live across the
stroke (B293, B299), cropped into the post-process, and applied on commit. Its
one rule for media was that they stay outside it — a watercolour wash should
saturate where it pools.

The bristle tip's grooves are radial (`TipGenerator.Bristle`: a cosine comb in
the angle, deeper toward the rim). Put through a running maximum they vanish:
as dabs slide past a pixel, every angle sweeps through it, and the maximum over
all angles is the plain body. So the tip cannot be the ceiling. The hairs have
to be stamped **as parallel stripes along the heading**, and then the maximum
over a straight run of dabs lays the same stripes on the same pixels and keeps
them exactly.

## The mechanism

A medium brush with `BristleDrag` above zero is a **hair brush**
(`BrushEngine.NeedsHairCeiling`). It takes the footprint path — `NeedsFootprintCap`
says yes for it — and each dab stamps, instead of its own shape, the stroke's
hairs: `StampHairs` draws the dab's disc under the stroke's heading with a
linear gradient across it, one stripe per hair, each stripe at the hair's
weight in the red channel, under `Lighten`. The weights are hashed from **the
stroke's first point and the hair's index** (invariant 2: nothing random,
and a replay lays the same hairs), skewed toward full so a brush is mostly
paint with a few scratches:

    weight_i = 0.3 + 0.7 · hash(seed, i)^0.6
    level_i  = 1 − drag · (1 − weight_i)

Hairs are about three document pixels wide (`HairWidthPx`), four to
forty-eight of them across the brush, so a 34 px oil has eleven. The swept
ceiling (B349) is switched off for a hair brush — `CeilingReachPx` is zero —
because it rebuilds a soft dab's falloff from the support's edge and would fill
the hairs back in; the hair ceiling is read as laid. The live coverage buffer
stays at document scale for a hair brush, where the ordinary cap may follow
the compose scale: a hair is three pixels and a coarser buffer would gate the
wrong ones.

What the cap then does is two things at once. **Coverage per hair**: a hair at
weight 0.4 never exceeds 0.4 of the dab however many dabs cross it, so the
mark carries streaks, and where the brush runs dry (`PaintLoad`) the thin hairs
drop below the paper first — scratches, the dry-brush break. **Height per
hair**: `Impasto.Shade` reads the mark's own alpha as its height, so the capped
streaks are ridges and grooves, lit from the upper left as the body already is.
No new state, no new map: the footprint the engine already carries.

## What it is not

Not the advection loop (`DESIGN-fluid-media.md` piece 3): paint is combed
where it lands, not pushed along the stroke, and `Pickup` still waits on that
loop. Not a per-layer height field: two strokes crossing still do not build a
ridge where they meet, the limit the impasto pass has always had and the next
piece if the look wants it. Not a tip texture: the hairs are the stroke's,
and the tip keeps shaping the outline under them.

## Cost

A hair brush pays what a capped soft brush pays — one footprint stamp per dab
(a linear gradient instead of a radial one) and the cap pass over the band —
which the engine already budgets and carries. Measured on the real app by the
perf lab before this merged; the numbers are in the pull request.
