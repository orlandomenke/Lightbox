# Q132 · Why a flame cannot shed its tip, and what to do about it — **answered 2026-08-20: add a combustion term**

Raised by: the owner, watching a render — "flames rise upwards, near the top
sometimes partial flames separate from the main body. I seem to have noticed
that this isn't happening in our effect."

What it blocks: whether `SimParams` grows a combustion term, or whether shedding
stays a tuning problem for the artist.

## The observation was right, and the first reading of it was wrong

"It isn't happening" reads as *the fluid never breaks up*. Measured, it does —
the field separates into pieces on 22 of 40 frames, and the tracer draws every
one of them (21 of 22; there is no minimum-area filter in `FieldTracer`, only a
three-point minimum for a shape). Counting only pieces of five cells or more,
the window's own fire shed six over forty frames and **every one lasted exactly
one frame**. The median detached piece was a single cell.

So the missing thing is not separation. It is **survival**. A piece that exists
for one frame at 12 fps is sparkle.

## Why it dies

Heat is stamped at an emitter and from then on only decays. A piece still inside
the column is refuelled from below every frame; a piece that has left has
nothing, and at `Cooling = 0.06` per step over eight substeps it loses 39% of its
heat per frame — under the outermost band level before the next drawing. Real
flame tips detach *and go on burning*, because they carry their fuel with them.

Slowing the cooling proves it and, in proving it, finds the actual problem:

```
                         flame height   sheds
  Cooling 0.06 (tuned)    21 cells      6, every one 1 frame
  Cooling 0.01            43 cells      2, lasting 12 and 26 frames
  Cooling 0               84 cells      everything merges into one mass
```

The setting that gives the behaviour is the setting that doubles the flame.
**One number sets both a flame's length and its tip's survival**, and the
defaults were tuned for length — so no setting of it could have given a short
flame that sheds. (Zero is worse than 0.01, incidentally: with nothing cooling
the whole grid stays lit and there is no *separate* piece to see. It is a window
rather than a direction.)

## The decision

**Recommended, and what the owner chose: add a combustion term.** Density
becomes fuel; where fuel sits above an ignition point a fraction burns per step
and becomes heat. A detached parcel carries its own fuel and burns for several
frames; the column is unchanged, because its fuel burns at the same rate.

Measured on the flame that prompted the question: the longest-lived piece goes
from **2 frames to 8**, and on the grid a new element actually gets, from
**1 frame to 4** with nothing else changed.

What it costs, stated because it is not free:

- **A re-tune was expected and mostly did not happen.** Burning makes a fire
  hotter and a hotter fire climbs, so the expectation was that `Vorticity` would
  have to come up with it to spend the extra rise on curl. That is true on a tall
  grid and false on the 44-cell grid a new element starts with, where the flame
  has no room to use the heat — 47% of the grid's height against 50% before. The
  default therefore changes one thing.
- **`SimParams` grows a nullable block**, three keys, absent on everything that
  does not burn.
- **It is a mechanism, so it has to be right rather than merely convincing.** It
  is self-limiting by construction — burning spends the fuel, heat production
  falls away, cooling has it again, and what is left is cool density. That is
  also why `Emitter.Burst` gets *fireball becomes smoke* out of it for free.

### What was rejected, and what each would have cost

- **Cool the core more slowly** — scale `Cooling` by local density, roughly five
  lines and no fuel field. It would have got most of the effect for a fraction of
  the work, and it is a fudge rather than a mechanism: no fireball that burns
  out, and it risks the `Cooling = 0` failure where everything stays hot and
  merges.
- **Tuning and a preset only** — ship a "shedding flame" preset at
  `Cooling ≈ 0.01`, write the trade into the manual, no code. Zero risk, and it
  concedes the thing the question is about: a shedding flame would then
  necessarily be a *tall lazy* flame, and a character effect usually wants a
  short one.

## The part that was not a preference

Whichever had won, **the outermost drawn contour sits at 10% of the element's
peak** (`LevelOf`, three bands over `BandLow`..`BandHigh`), so a fading parcel
leaves the drawing well before it leaves the field. Raising the band count or
lowering `BandLow` puts the silhouette nearer the faint edge and is worth
knowing about independently of any of the above — six bands roughly doubled the
size of the largest drawn piece in the measurements here.
