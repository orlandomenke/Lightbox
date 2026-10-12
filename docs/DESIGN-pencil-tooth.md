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

## The mechanism here, second version (B437)

Lightbox already had the texture pass (`ApplyTexture`): a paper height field
at document resolution, multiplied into the stroke once at pen-lift as
`keep = 1 − depth·(1 − height)`. The first version of the pencil kept that
pass and gated it: the dabs recorded a press map, the pass read it, and the
mark reached the screen when a pass had finished. The perf lab measured what
that meant at 300 px and a fast pen: **2.9 s from pen to screen** where the old
pencil took 45 ms, because a paper-textured brush is one of the two kinds the
live path keeps off the raw tip (Q168) and waits for. The owner's rule
settled the second version: *what you draw is what you see; no drawing
program changes the stroke after the pen lifts.*

So the tooth is a **ceiling**, not a pass. The engine already caps a soft
brush's mark to a per-pixel maximum it accumulates beside the dabs (Q157,
B349) and, for a brush with nothing else to do at pen-lift, applies that cap
**in place, per pointer event, on the band that changed** — no worker pass,
nothing at pen-lift. A pencil-like brush (`NeedsToothCeiling`) takes that
route. Each dab stamps, instead of its own shape, the paper's keep value at
that dab's pressure:

    t(p)    = depth · (1 − gate · p^½)
    keep(h) = smoothstep(t − s, t + s, h),  s = 0.25

as one Skia draw — the paper tile as a repeating image shader in document
space (the grain belongs to the paper and must not move with the dab), through
a 256-entry colour table that is the gate for this pressure (cached per
pressure byte), modulated by the dab's own radial falloff, under `Lighten` on
the opaque footprint. The cap then holds the mark to it. A light touch keeps
only the paper's peaks; a hard press reaches the valleys; the soft edge of a
dab presses less than its core, so the border of a mark shows more paper than
its middle. The swept ceiling (B349) is off for a tooth brush, because it
rebuilds a soft dab's falloff from the support's edge and would fill the
tooth back in; the live coverage buffer stays at document scale for it,
because a tooth is two pixels of grain.

A ceiling rather than a multiplier is also what keeps the tooth through the
dozen dabs that overlap every pixel: where the paper keeps 0.3, the mark
reaches 0.3 of full however many dabs cross it. In the saturated core of a
stroke the two read identically, which is why the measurements below did not
move between the versions.

`gate` is `BrushSettings.TexturePressure`, null until used and then written;
with it null the paper is the ungated pass it always was, byte for byte,
pinned by `AnUngatedTextureRendersExactlyAsItDidBeforeTheGateExisted` against
a fingerprint recorded on the engine before the gate existed. The ungated
paper keeps Q168's route; whether it should also become a ceiling is the next
question, and the owner's rule says yes.

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
Live against committed on the shipped Pencil: **mean 0.00/255, worst 0** —
the live mark is the mark.

## Cost

A tooth brush pays what a capped soft brush pays — one footprint stamp per
dab (an image shader through a colour table instead of a radial gradient) and
the band cap per event — and nothing at pen-lift: the texture pass is gone
for it, and with it the press map and its carry. Headless at 300 px and 25 px
per event, the pointer thread spends 15.9 ms per event against the old
pencil's 9.3 (the tooth stamp is twice a radial footprint on a 70 000 px dab)
and the commit 284 ms against 185; the real app's pen-to-screen rows are the
lab's and are in the pull request. The first version's live route is the
reason the lab runs before every media merge now.

## What it is not

Not a tip texture — the grain is the paper's, anchored to the document, so
two pencil lines crossing the same patch sit on the same tooth. Not graphite
sheen, not smudging with a finger, not a chisel or a flat tip; a tip image
still works under the gate (its bounding disc records the press; the tip's
colour never reaches the map, which a review caught). And not a
new `BrushDynamic`: an unknown dynamic key makes a build older than Q193's
version field throw the whole brush store away, where an unknown property is
skipped.
