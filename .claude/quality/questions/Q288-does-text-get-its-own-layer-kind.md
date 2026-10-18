# Q288 · Does text get its own layer kind

Raised by: an unfinished note from a session on 2026-10-15 — *"Text gets its own
layer: kind, placement, merge and double-click"* — that was never written up and
whose id (Q275) was later taken on main. The owner asked on 2026-10-18 for it to
be written up properly and decided later.

What it blocks: nothing built today. It decides whether the next round of text
work is interaction on top of the record Q165 chose, or a change to that record.

## What exists

Q165 settled what carries text: an authored `TextElement` in `Doc.Texts` plus one
baked `ToolKind.Text` contour stroke per glyph, carrying `Stroke.TextId`, on
**any** layer. That choice is why the renderer, picker, transform, exporter and
inbetweener never learned that text exists, and why a document renders on a
machine without the font. Q164 kept the first tool to point text; Q172 made the
box an artist clicks into a *measured* one.

What the note asks for is the Photoshop arrangement: type lives on a layer of its
own, the layer says it is type, double-clicking it edits the words, and merging it
into a painted layer turns it into paint.

## The options

| | What it costs |
| --- | --- |
| **A text layer as a convention, not a kind** (recommended) | Typing on a layer that has nothing on it, or with no layer chosen, makes an ordinary layer named after the words and marks it with a **T** — derived from the fact that every stroke on it is text, never stored. Double-clicking that layer, or its thumbnail, enters the text. Merge needs nothing new: it already moves strokes, and a merged glyph stays editable text until the artist paints over it. No record change, no new key, nothing for the other systems to learn. The cost: nothing stops an artist painting on the "text layer", after which it is simply a layer with text and paint on it and loses its **T**. |
| A real `LayerKind.Text` | A third kind beside `Painted` and `Vector`, holding only text; the brush refuses it, the badge is authored. Gives Photoshop's guarantee that a type layer is only type. Costs an optional field to keep absent (the `optional-settings` rule), every switch on `LayerKind` to revisit, and an answer for merge — bake into paint, or refuse — that today's record does not need. Revisits Q165's "on any layer" for a guarantee artists can already get by not painting there. |
| Leave it as it is | Text on any layer, found by picking it on the canvas. Cheapest; the layer stack does not show where the type is, which is the part of the note that is clearly felt. |

## Recommendation: the convention

The note's four words — kind, placement, merge, double-click — are about *finding
and editing* type, and all four are served without a new kind: the **T** finds it,
the double-click edits it, placement is where the text was typed, and merge already
works. A real kind would buy only the refusal to paint on type, at the price of
reopening Q165's record. If that refusal turns out to be wanted, the convention
does not block it: a kind could be added later on top of the same `Doc.Texts`
record, with the derived **T** as its migration.
