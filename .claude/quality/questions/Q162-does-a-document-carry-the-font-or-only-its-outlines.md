# Q162 · Does a saved document carry the font, or only the outlines it used? — **answered 2026-08-24: outlines always, and the font too where the licence plainly allows it**

| | What it costs |
| --- | --- |
| Outlines only (recommended) | Self-contained by construction, no redistribution question at all. Re-typing on a machine without the font warns instead of silently re-flowing. |
| **Outlines, plus the font where the licence allows** (**chosen**) | Also stashes the font bytes beside `BrushTips` when the source publishes a licence permitting redistribution — Google's OFL, Apache-2.0 and UFL families. Text stays editable anywhere for those; it costs a licence model, a per-source policy, and font bytes in a public-by-default file format. |
| Embed everything | Always editable anywhere, and it makes redistribution the artist's problem for fonts whose terms this application cannot read. |

**Chosen against the recommendation, and the extra cost is what was accepted:**
a licence field on every carried font, a source field so the claim is checkable,
and a rule with a hard edge — *not knowing the licence is a "no"*. An installed
font is named and never copied; a Google font is carried. The whole of that
policy is `FontLibrary.Reference`, in one method, so there is a single place to
read to know what a Lightbox document can contain.

The property that makes the choice safe either way, and the reason it was never
a rendering question: **the picture never depends on it.** Glyph contours are in
the record, so a document with no fonts carried and none installed renders
identically. What embedding buys is retyping, and nothing else.

`FontSettings.EmbedOpenFonts` turns it off for an artist who would rather have
the smaller file; turning it off never changes how anything looks.
