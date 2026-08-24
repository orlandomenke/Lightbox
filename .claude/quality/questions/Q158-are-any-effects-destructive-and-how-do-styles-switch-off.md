# Q158 · Are any effects destructive, and how do styles switch off? — **answered 2026-08-24: none are, and the stack gets one master switch**

Asked by the owner when the layer-row entry points for styles were being
planned: *"How do we differentiate between non destructive layer effects and
destructive effects? I believe the layer styles should be able to be turned
on and off."*

The first half has a clean answer: **there is no such line to draw, because
Lightbox has no destructive effects.** Everything on the effects record —
filters and styles alike — is a setting over the untouched stroke record
(invariant 1), re-rendered live and stepped through by undo. The one
destructive act in the area is *Merge down*, which bakes what strokes cannot
carry and warns first (Q52); it is a layer operation, not an effect. So the
differentiation Photoshop needs (filters burn pixels, styles and smart
filters do not) simply does not arise here, and no UI should imply it does.

The second half was a real gap. Each use already had its own keep-the-settings
switch (`EffectUse.Disabled`), but "turn the layer's styles off" meant ticking
five boxes — and Photoshop's one-click eye on the Effects group is the reflex
an artist brings.

| | What it costs |
| --- | --- |
| **A stack-level `Disabled`** (recommended, **chosen**) | One more nullable key, absent while the stack runs — the same shape `EffectUse.Disabled` and the mask's `Disabled` already have. |
| **A "disable all uses" command** | No new record state, but it destroys the uses' own on/off pattern: re-enabling cannot know which uses were deliberately off before. |

`EffectStack.Disabled` mutes the whole stack without touching any use, so
five tuned styles come back exactly as they were. `AppliesAnything` gates on
it, which carries the switch through every compositor at once —
`HasLiveEffects`, the adjustment and scene passes, the tile gate, dirty
inflation and the merge bake all read that one derived property. The fx chip
on the layer row goes hollow while the stack is off, exactly as the mask
chip does, and the row menu and the docker header both toggle it.
