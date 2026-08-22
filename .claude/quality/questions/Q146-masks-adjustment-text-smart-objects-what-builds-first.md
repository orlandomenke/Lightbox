# Q146 · Masks, adjustment, text, smart objects — what builds first? — **answered 2026-08-22: masks + clipping now, adjustment on the effects record next, text and smart objects roadmapped**

Raised by the request *"layer masks, adjustment layers, smart objects, text
layers, clipping masks"* — five features, which is several branches of work,
three of them already `[?]` on the roadmap and two not on it at all.

| | What it costs |
| --- | --- |
| **Masks + clipping masks first** (recommended, **chosen**) | They are one compositing story with no unbuilt dependency. Adjustment layers wait one branch — deliberately, because building them *now* would mean either building the `DESIGN-effects.md` record core in the same diff or inventing a parallel adjustment record that the effects work would then have to subsume. |
| All three compositing features at once | Adjustment layers land sooner, but the branch carries the effects-record core too — two designs' worth of surface in one review. |
| All five this session | Text layers and smart objects both need design decisions (font shaping; what a smart object even *is* here) that deserve their own pass — building them from a guess is the failure the questions process exists to prevent. |

The consequence for adjustment layers, recorded so the next branch does not
re-decide it: **an adjustment layer is an effect-carrying layer, not a new
mechanism.** `DESIGN-effects.md` already gives the record (`EffectStack`,
keyable params, reach declarations, the registry); an adjustment layer is that
stack applied to the composite below the layer, scoped by the same mask and
clipping machinery this branch builds. Q149 carries the text/smart-object half.
