# Q161 · What carries a text object in the record? — **answered 2026-08-24: an authored element plus one baked contour stroke per glyph**

Q149 left this open in as many words: *"it needs its own design pass before a
line of it is built (what carries the text: a stroke kind, a placement, or a
vector-layer object)"*. This is that pass.

| | What it costs |
| --- | --- |
| **An element plus baked glyphs** (recommended, **chosen**) | `Doc.Texts` holds what was typed, in what and where; committing shapes it and records one `ToolKind.Text` contour stroke per glyph carrying `Stroke.TextId`. Reuses `StampFill` unchanged and the sim's drop-and-re-bake pattern, and the document renders on a machine with no fonts. The cost: overlapping glyphs at a stroke opacity below 1 can seam, because each is composited separately. |
| One stroke carrying every glyph | No seam at any opacity — and it adds a contour-grouping shape the record does not have, a second fill branch in the engine, and its own answers for picking, transforming and merging. |
| A vector-layer object outside the stroke record | Closest to Illustrator, and it introduces a second kind of mark. That is precisely what `ShapeBuilder` refuses in its own remarks: *"the one thing this app cannot afford is a second kind of mark that behaves differently under the brush."* |

**What recording it turned up.** The chosen shape is not a compromise between
the other two — it is `StrokePath` again. The pen already stores authored nodes
beside flattened points, with the points staying the truth and the path being
what allows re-editing; text is the same arrangement one level up, with the
element authored and the contours true. Neither the renderer, the picker, the
transform, the exporter nor the inbetweener learned that type exists.

The seam is real and has not been observed: type is normally set at full stroke
opacity and faded, when it is faded, by the layer. If it ever bites, the fix is
the second option above and it does not change the record's shape — only how
many strokes a block bakes to.
