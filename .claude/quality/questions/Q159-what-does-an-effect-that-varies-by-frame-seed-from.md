# Q159 · What does an effect that varies by frame seed from? — **answered 2026-08-24: our own hash, over document position and a per-use seed**

Asked when the effects catalogue turned to the ones that *move*: film grain,
wiggle and flicker. Every effect shipped so far is a pure function of its
parameters, so its output is the same on every frame; these three are the
first whose output is meant to differ frame to frame, which is the exact
place invariant 2 is easiest to break and hardest to notice.

**It also fills a shelf that has been empty since the record landed.** The
design's central claim is one effect model with two presentation lanes —
filters and animation effects — and until now every kind sat in the filter
lane (`grade`, `blur`, `style`). A claim about two lanes with nothing in one
of them is untested; `anim.wiggle` and `anim.flicker` are what test it.

## Where the noise comes from

| | What it costs |
| --- | --- |
| **`DeterministicHash.Unit`, ours** (recommended, **chosen**) | Grain becomes a CPU pixel pass, so — like Hue/Saturation — it is backdrop-only: the scene stack or an adjustment layer, not a layer's own stack. |
| **Skia's Perlin noise shader** | Native and fast, and it would work on a layer's own stack too. But the algorithm belongs to Skia: a library upgrade could change what a saved document renders, which is the precise failure `RuntimeDeterminismTests` exists to catch. |

The same primitive the brush engine seeds every dab dynamic from, for the
same reason: two renders of one document must agree, and a document rendered
next year must agree with one rendered today. A film's grain is not something
that can be allowed to change under a dependency bump.

Wiggle and flicker stay **native** — an offset and an alpha multiply, both
cheap — and are therefore reachable on a layer's own stack *and* on the
backdrop, where a wiggle over the whole composite is a camera shake.

## The seed is per use, and authored only when dialled

Two layers wiggling in lockstep read as one rigid object, which is the
opposite of the effect's purpose. So the seed's default is **derived from the
use's id** — different by construction, stable forever, and absent from the
file until an artist dials it to re-roll a motion they did not like. That is
`EffectParamSpec.PerUse`: the docker does not write the parameter on add, and
every reader falls back to the derived value.

## Frequency is a hold, not a frequency

The obvious control is a rate in cycles per second. The animation-native one
is a **hold**: how many frames the value stays put before it jumps. An
animator working on 2s wants the boil on 2s, and says so in the units the
exposure sheet already uses. Both wiggle and flicker step on holds, grain
defaults to a hold of 1 because film grain moves every frame.

## Two traps the design named in advance, both real

- **The cache would have served frame 0 forever.** The filter cache
  fingerprints a stack on its parameters *evaluated at the frame*, and a
  frame-seeded effect's parameters do not change with the frame — its output
  does. A definition now declares itself `TimeSeeded`, and the frame joins
  the fingerprint when any use is.
- **A bounded repaint and a 2× export would each re-roll the grain.** The CPU
  pass runs on a clip-bounded readback of the backdrop in *device* pixels, so
  it was seeded from wherever the dirty rectangle happened to start, at
  whatever zoom happened to be on. It now receives that rectangle's origin and
  the device scale and seeds from **document** position, which is invariant 7
  stated as arithmetic: `ATiledRepaintGrainsExactlyAsAWholeOneDoes` and
  `GrainDoesNotReRollWhenTheSurfaceScales` are the two pins.
