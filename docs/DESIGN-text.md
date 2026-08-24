# Text, and where fonts sit

**The one sentence:** a font decides how the words are *shaped*, never how the
picture is *rendered* — because type is baked to contours the moment it is set,
and the contours are the drawing.

Everything below follows from that, including the parts that look like licensing
policy. Decisions are Q161–Q164; the roadmap item is `[x] Text` under *Guides
and shapes*.

## What the record holds

| | |
| --- | --- |
| `Doc.Texts[id]` | A `TextElement`: the string, the `FontRef`, size, tracking, leading, alignment, and the baseline point. **Editing state, not rendering state.** |
| `Stroke.TextId` | Provenance and a handle, on each glyph. |
| `ToolKind.Text` | One glyph. `Points` is its first contour, `Holes` the rest, read even-odd. |
| `Doc.Fonts[id]` | An `EmbeddedFont` — bytes, family, weight, slant, licence, source — present only where a licence permitted it. |

Every one of those keys is absent from a document nobody has typed in
(`ADocumentNobodyHasTypedInWritesNoTextKeys`).

**Delete `Doc.Texts` by hand and the document renders pixel for pixel the same.**
That is the honest test of which half is the truth, and it is worth performing
mentally before changing anything here: the element buys retyping and nothing
else.

## The shape of a commit

```
type on canvas ─► TextElement ─► HarfBuzz shaping ─► glyph outlines ─► flatten
                                                                        │
                       one ToolKind.Text stroke per glyph, carrying TextId
                                                                        │
                                              PerformDelta: strokes + element
                                                     + font, if it may travel
```

Retyping is the same picture with a deletion in front of it: drop every stroke
in the cel carrying the element's id, and bake again. That is
`Stroke.SimId`'s pattern, borrowed deliberately rather than reinvented.

**One stroke per glyph**, not one per block. A glyph is a filled contour, which
is a shape this codebase already understands everywhere; a block would have
needed a contour-grouping shape in the record and a second fill branch in the
engine. The price is a seam where glyphs overlap at a stroke opacity below 1 —
recorded in `TextBaker`, unobserved so far, and fixable without changing the
record.

## Why nothing downstream learned about type

`ToolKind.Text` renders through `StampFill`, unchanged. Adding it meant finding
six copies of `is Fill or ClearRegion` — the rasterizer, the picker twice, the
hover preview, the node editor, the inbetween verifier — each of which had to
learn about the new kind independently and any one of which would have failed
silently: type previewing as an open horseshoe, or refusing to be clicked. They
now ask `ToolKinds.FillsAContour`, so the next contour kind changes one line.

**A glyph's first contour is not necessarily its outer one.** For the "O" of the
face this was written against, `Points` is the counter and `Holes[0]` is the
ring. Even-odd does not care, which is exactly why the tool worked and the first
version of its test did not. Anything that reasons about a glyph's extent must
use every contour, not `Points`.

## Determinism

Flattening is a closed formula over the control points and the tolerance
(`GlyphOutline`), not adaptive recursion with a floating-point stopping test —
so the same font at the same size gives the same points, to the bit, on any
machine.

**Shaping's version is not a hazard, and it is worth being explicit about why.**
A future HarfBuzz might place a glyph a hundredth of a pixel differently. But
shaping happens when the artist types, and what the document stores is the
contours that came out of it: existing art is never re-shaped, so it cannot
move. This is invariant 4's argument — settings that reach pixels are captured
at the moment of the mark — applied to a library version instead of a
preference.

`GlyphOutline.Tolerance` is therefore **not** a quality setting that can be
turned up later. It is baked into drawings. A fifth of a pixel: under one pixel
even at the 4× renders invariant 7 makes cheap, and coarse enough that a page of
type is not the largest thing in a document.

## Fonts: two sources, one rule

```
                 ┌── SystemFontSource ── installed, instant, licence unknown
IFontSource ─────┤
                 └── GoogleFontSource ── catalogue + file, cached, licence known
```

`FontLibrary` merges them and resolves a `FontRef` in one order: **embedded,
then downloaded, then installed.** Embedded first because a document that
carried its font was made to travel, and an installed face of the same name can
be a different cut.

**The rule about what travels is one method** (`FontLibrary.Reference`), and it
has a hard edge: *not knowing the licence is a no.*

| Source | In the file | Can somebody else retype it? |
| --- | --- | --- |
| Installed | The name | Only where that font is installed |
| Google (OFL / Apache-2.0 / UFL) | The name **and the bytes** | Anywhere |

Neither row affects a single pixel. `FontSettings.EmbedOpenFonts` turns the
second one off for a smaller file, and changes nothing about how anything looks.

Choosing a font is **pure** — it returns a `FontChoice` and does not touch the
document — because committing text is one undoable edit and the revert has to
know exactly what was added. A font brought in by a caption goes back out when
that caption is undone, and a font another caption is still using does not.

### The keyless Google route, and its one trick

The developer API needs an API key, which would mean an artist signing up to a
cloud console before they can set a title. So:

- **Catalogue** — `https://fonts.google.com/metadata/fonts`, the list the Google
  Fonts website itself reads. Not a published contract, so `ParseCatalogue`
  treats every field as optional and a response it cannot read is *no fonts*
  rather than a crash.
- **Files** — the documented CSS endpoint, requested with a user agent old
  enough that the answer is TrueType rather than woff2, which Skia cannot open.
  If that ever stops working, `ParseCss` finds no `.ttf` and the download says
  it could not — rather than handing the font machinery bytes it cannot read.

**None of this was verifiable live.** The environment it was built in denies
`fonts.google.com`, so every test runs against captured responses and the code
is written so that being wrong about an endpoint degrades to "showing what is
cached", with a line of text saying so. First contact with the real endpoints is
the thing to check before trusting the Google half.

Nothing is requested until an artist opens the font list.
`FontSettings.UseGoogleFonts` off means the library is built without the source
at all — no network, rather than a result thrown away.

## A font that is named and cannot be used

A system font manager will happily name a Type 1 or bitmap-only family that
shapes to nothing. On the first machine this ran on, the alphabetically first
installed family is exactly that — so the tool opened in a font that silently
set nothing, and a typed title vanished on Escape with the status line saying
"Type removed."

Two changes came out of that, and both are the same idea: **fail where the
choice is made, not where the work is lost.**

- `TextBaker.CanSetType` probes by glyph id rather than by character — asking
  whether the face can draw an "A" would reject every CJK, Arabic and symbol
  font. Picking such a face says so and does not adopt it.
- The default face is the family the platform itself resolves
  (`SKTypeface.Default.FamilyName`), not the first one alphabetically.

## What is deliberately not here

Point text only: no wrapping box, no text on a path, one style per block, and
weight-and-slant rather than width. Each is a roadmap item under *Guides and
shapes* with its cost written down (Q164).

**The MCP surface is not here either**, and that is a scope call rather than an
oversight: placing type from an agent is a document capability that belongs
there, and a diff touching MCP needs the ai-engineer / art-director pair under
charter gate G12 — a second review of a different kind, on a branch already
carrying the tool, the shaping and two font sources (Q163).
