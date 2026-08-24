using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster.Text;

/// <summary>
/// Turns a text element into strokes — the moment type becomes drawing.
/// </summary>
/// <remarks>
/// <para>
/// <b>One stroke per glyph, and the alternative is worth recording.</b> The
/// whole block could have been a single stroke carrying every contour, which
/// would have avoided one thing: at a stroke opacity below 1, glyphs that
/// overlap — script faces, tight negative tracking — show where they cross,
/// because each is composited separately. Against that, a glyph per stroke
/// costs nothing new anywhere else in the application: each one is an ordinary
/// even-odd contour fill, which is a shape every part of this codebase already
/// understands, and its counters come out right without a fill rule anybody has
/// to remember. A whole-block stroke would have needed a contour-grouping shape
/// in the record, a second fill branch in the engine, and its own answer to
/// picking, transforming and merging.
/// </para>
/// <para>
/// <b>Re-baking is deletion plus this</b> — see <see cref="Stroke.TextId"/>.
/// Nothing here mutates; a caller that wants to change a word drops the strokes
/// carrying the element's id and calls this again.
/// </para>
/// </remarks>
public static class TextBaker
{
    /// <summary>
    /// The glyph strokes for an element, painted like <paramref name="paint"/>.
    /// </summary>
    /// <param name="paint">
    /// The colour, brush and clip every glyph is painted with — a prototype
    /// stroke, cloned per glyph. A prototype rather than a list of parameters so
    /// that a stroke field added later (a swatch, an alpha lock, a treatment)
    /// reaches type without this signature having to learn about it.
    /// </param>
    /// <remarks>
    /// Glyphs with no outline — spaces, and anything the font draws as nothing —
    /// produce no stroke rather than an empty one. A record full of empty marks
    /// is a record every consumer has to filter.
    /// </remarks>
    public static List<Stroke> Bake(TextElement text, SKTypeface typeface, Stroke paint)
    {
        var layout = TextLayout.Of(text, typeface);
        using var font = new SKFont(typeface, (float)text.Size);
        var strokes = new List<Stroke>();

        foreach (var line in layout.Lines)
        {
            foreach (var glyph in line.Glyphs)
            {
                using var path = font.GetGlyphPath(glyph.Glyph);
                if (path is null || path.IsEmpty) continue;

                var contours = GlyphOutline.Contours(path, glyph.X, glyph.Y);
                if (contours.Count == 0) continue;

                var stroke = paint.Clone();
                stroke.Tool = ToolKind.Text;
                stroke.TextId = text.Id;
                stroke.Points = contours[0];
                // Everything after the first contour is a hole in the even-odd
                // sense — which covers a counter (the middle of an "o") and a
                // disjoint piece (the dot on an "i") without having to tell them
                // apart, because even-odd already does.
                stroke.Holes = contours.Count > 1 ? contours.GetRange(1, contours.Count - 1) : null;
                stroke.Path = null;
                strokes.Add(stroke);
            }
        }

        return strokes;
    }
}
