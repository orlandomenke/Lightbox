# Q153 · What is a layer style in the record? — **answered 2026-08-23: an effect kind on the layer's own stack**

Asked when layer styles (outer glow, inner glow, stroke, bevel) were requested,
one branch after the effects record landed (Q151). Photoshop keeps styles in a
dedicated panel beside its filter machinery; the question is whether the record
should mirror that split or absorb styles into the `EffectStack` the layer
already carries.

| | What it costs |
| --- | --- |
| **Effect kinds on the stack** (recommended, **chosen**) | No separate "fx" section in the docker — styles list among effects, distinguished by their shelf. |
| **A separate `Layer.Styles` block** | Duplicates the entire seam — serialization, docker, undo, dirty inflation, bake refusal, MCP surface — for machinery that is functionally identical to an effect. |

A style *is* an effect: a non-destructive, parameterised, keyable pass over the
layer's own output. Recording it as `style.*` kinds on `Layer.Effects` buys the
whole existing seam for free — absent-by-default serialization, the docker's
rows and undo steps, `ReachOf`-driven dirty inflation, the bake and tile
refusals, and keyed parameters the moment the timeline's curve editor arrives.
What distinguishes a style from a filter is *where it reads and writes*, and
that lives in the registry (`Shelf: "style"`, self-only) rather than in a
second record shape.

## The v1 set, and why drop shadow joined it

The request named outer glow, inner glow, stroke and bevel. **Drop shadow was
added**: it is the most-used style anywhere, and it is nearly free once outer
glow exists — the same blur-behind-the-silhouette machinery plus an offset.
Leaving it out would make the set read as incomplete for no saving.

So v1 is five kinds: `style.dropShadow`, `style.outerGlow`, `style.innerGlow`,
`style.stroke`, `style.bevel` — all native Skia filter graphs (no CPU pass, so
the one-filtered-redraw fast path holds), all reading the layer's carved
silhouette (Q155), all self-only: a style of the scene composite or of an
adjustment backdrop has no silhouette to read, so the docker offers styles only
where they mean something — the mirror of `BackdropOnly` gating Hue/Saturation
off self stacks.

## Colour is the one thing the params could not say

`EffectParam` is a keyed scalar (Q122's shared vocabulary), and a glow without
a colour is not a glow. Rather than pack ARGB into a double or fake it with
three sliders, `EffectUse` gains an optional `Colors` dictionary — key to hex
string, the same colour vocabulary strokes already use — nullable and absent
until a colour is authored, per the optional-means-absent rule. Colour keys are
*not* animatable in v1; the scalar params are, which matches what an animator
actually keys (a pulsing glow keys its size and opacity, not its hue).
