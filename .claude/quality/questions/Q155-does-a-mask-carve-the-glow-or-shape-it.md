# Q155 · Does a mask carve the glow, or shape it? — **answered 2026-08-23: styles read the carved silhouette**

Asked alongside Q153, because the existing pipeline had already decided the
opposite for filters. Since the effects branch, a self effect filters the
layer's content *first*, in a group of its own, and the mask carves the
filtered result — deliberately, so a mask cuts a crisp edge through a blurred
layer rather than blurring the cut (`SceneRenderer.DrawPass`'s comment records
it). Applied unchanged to styles, that rule would hard-stop an outer glow at
the mask edge: the glow of the *unmasked* content, trimmed.

| | What it costs |
| --- | --- |
| **Styles read the carved silhouette** (recommended, **chosen**) | One pipeline with two documented behaviours: filters inside the carve, styles outside it. |
| **Mask trims styles too** | Uniform and simpler to state — but a glow on a masked layer stops dead at the mask edge, which is Photoshop's opt-in "Layer Mask Hides Effects" made mandatory and reads as a bug on the canvas. |

The pipeline is now: **content → filter effects → mask carve → style
effects**, one nesting of save-layers per pass. Mask away half a character and
the glow hugs the half that is left — the silhouette the artist actually sees
is the silhouette the style decorates, which is Photoshop's default and the
answer an animator expects without reading anything.

Blur keeps its crisp cut: nothing about the filter shelf moved. The two
behaviours are not an inconsistency but the same principle applied twice — an
effect decorates *what the layer shows*. A blur is part of what the layer
shows, so the mask trims it; a glow is a decoration *of* what the layer shows,
so it follows the trim.

`MaskAndClipCompositingTests` pins both directions: a masked layer's outer glow
present beyond the content edge on the kept side, absent on the carved side's
far reaches where the unmasked content would have glowed.
