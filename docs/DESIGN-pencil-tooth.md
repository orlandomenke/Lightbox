# A pencil is graphite on the paper's tooth

*Design note, 2026-10-09, implemented the same day. The measurements are
`tests/Lightbox.Raster.Tests/PencilToothTests.cs` and run in the suite.*

## The finding

The owner, comparing with Clip Studio Paint: *"Our pencil nothing like a real
pencil. Just some noise texture."* The shipped pencil was a hard round (size 3,
hardness 0.9) with granulation 0.15 — a flat grain multiplied into the mark,
the same at any pressure. A real pencil is graphite caught on the paper's
tooth. Held lightly it marks only the peaks, so the line is a speckle of the
paper's own grain; pressed, it fills the valleys and goes solid; and its edge,
where the tip bears least, shows more paper than its middle. None of that is
noise. All of it is pressure.

## What Clip Studio Paint and Photoshop do

Both have the mechanism: a paper texture whose depth is driven by pen pressure
(Photoshop's *Texture → Depth control: pen pressure*; CSP's paper texture with
density on pressure). Neither simulates anything. The mark is a tip under a
texture gate, and the gate is what the hand controls.

## The mechanism here

Lightbox already had the texture pass (`ApplyTexture`): a paper height field
at document resolution, multiplied into the stroke once at pen-lift as
`keep = 1 − depth·(1 − height)`. It is applied to the whole stroke rather than
per dab, because the grain belongs to the paper and must not move when a dab
lands on it, and once is far cheaper than hundreds of times. That stays.

What was missing was *where the pen pressed and how hard*, per pixel. The dabs
now record it beside their footprint: `StampPress` draws each dab's shape
scaled by its pressure into an opaque surface under `Lighten`, which on an
opaque surface is a per-channel maximum — `StampFootprint`'s trick, in its
own surface because the footprint's three channels are taken. The same walk as
the dabs, same geometry, same call; not a second implementation of the
dynamics chain.

The gate then turns the bite into a threshold that pressure lowers:

    t(p)    = depth · (1 − gate · press(p)^½)
    keep(p) = smoothstep(t − s, t + s, height(p)),  s = 0.25

At a light touch `t` sits high and only the paper's peaks keep graphite; at a
hard press it falls toward zero and the valleys fill. The soft edge of a dab
presses less than its core, so the border of a mark shows more paper than its
middle — a pencil held at an angle. `gate` is `BrushSettings.TexturePressure`,
null until used and then written; with it null the pass is byte for byte what
it was, pinned by `AnUngatedTextureRendersExactlyAsItDidBeforeTheGateExisted`
against a fingerprint recorded on the engine before the gate existed.

**The two constants were set by looking, and the first guess was wrong in a
way worth keeping.** With a linear gate and a ramp of ±0.12, every candidate
preset drew a *dotted line* at medium pressure: whole stretches of the mark
on or off, at any grain size from 1 px to 4 px. The centreline alpha said why
— the procedural papers pack their heights between about 0.3 and 0.7, so a
narrow ramp flips entire sections at once and a linear gate spends the first
half of the pen's travel above every peak and the second half below every
valley. Pressure now enters as its square root (a light touch lands on the
peaks, a medium press is already in the valleys) and the ramp is ±0.25, wide
enough that a valley reads as lighter graphite inside a continuous line
rather than as a gap. Grain size turned out not to be the lever at all.

The shipped Pencil is a cold-press grain at 2 px, depth 0.8, gate 0.8, size 4,
hardness 0.7, flow following pressure at gamma 0.8. Measured as mean graphite
over the core of a line (PencilToothTests): **light touch 57 %, hard press
91 %**, where the same brush ungated reads 61 % at both; along a stroke whose
pressure ramps 0.15 → 1, **69 %, 73 %, 90 %** by thirds. The sheet the suite
writes under `LIGHTBOX_VISUALS` (`pencil-tooth.png`) is the picture of it.

## Cost

The press pass walks the dabs a second time, as the footprint pass does for a
soft brush, and a pencil's dabs are small. On the commit path that is one
extra walk per stroke and per symmetry copy. On the live path the map is
**carried across the stroke** exactly as `LivePaintSession` carries the
footprint (B293, B299): a document-sized buffer begun with the stroke, the
settled dabs accumulated once, the tail backed up and taken back when the
next event moves it, a backward cut rebuilt from black, and the post-process
handed a crop of it. A pointer event therefore pays for the dabs that are new
and the band that changed, never for the mark. The first draft rebuilt the map
from the whole stroke record per pass; the leak-hunter costed that at
10–50 ms per event on a 2000 px line and it did not land. The rebuild remains
as the fallback for a caller with nothing to carry, as the footprint's does.

Two things the gate does not do exactly. The press surface on the commit path
is device-sized while the texture's mask is at document resolution, so at an
output scale above 1 the gate is point-sampled a quarter of a pixel off where
the 1× gate sits — the mask is magnified, not re-gated, which is the paper's
rule (a fixed physical scale). And a brush whose **Pen pressure** master is off
records every dab as fully pressed, the rule every other dynamic follows.

A brush store or document carrying `texturePressure` opened in a build older
than Q193's version field drops the key silently and renders the tooth as a
fixed-bite texture; that is the standing gap Q193 accepted for new per-stroke
keys, and this key goes on every stroke of the most-used brush, so the
exposure is larger than it was for symmetry or wrap.

## What it is not

Not a tip texture — the grain is the paper's, anchored to the document, so
two pencil lines crossing the same patch sit on the same tooth. Not graphite
sheen, not smudging with a finger, not a chisel or a flat tip; a tip image
still works under the gate (its bounding disc records the press; the tip's
colour never reaches the map, which a review caught). And not a
new `BrushDynamic`: an unknown dynamic key makes a build older than Q193's
version field throw the whole brush store away, where an unknown property is
skipped.
