using Lightbox.Core.Documents;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace Lightbox.Raster.Text;

/// <summary>One glyph, placed: which glyph, where, and which character it came from.</summary>
/// <param name="Cluster">
/// The index into the source string this glyph belongs to. Not one-to-one with
/// glyphs in either direction — a ligature is one glyph over two characters, an
/// accent can be two glyphs over one — which is exactly why the caret is placed
/// from clusters rather than by counting glyphs.
/// </param>
public readonly record struct PlacedGlyph(ushort Glyph, double X, double Y, int Cluster);

/// <summary>One laid-out line of a text element.</summary>
public sealed class TextLine
{
    /// <summary>Where this line starts in the source string.</summary>
    public int Start { get; init; }

    /// <summary>Its length in the source string, not counting the newline that ended it.</summary>
    public int Length { get; init; }

    /// <summary>Advance width, tracking included.</summary>
    public double Width { get; init; }

    public double BaselineY { get; init; }

    /// <summary>Where the line's left edge sits, after alignment.</summary>
    public double Left { get; init; }

    public IReadOnlyList<PlacedGlyph> Glyphs { get; init; } = [];
}

/// <summary>
/// A text element, shaped and placed: what the baker turns into contours and
/// what the canvas asks where to draw the caret.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shaped by HarfBuzz, not by adding up advances.</b> Kerning, ligatures,
/// marks that sit over the letter before them, and any script that is not
/// left-to-right Latin are all decided here, and none of them can be recovered
/// afterwards from a list of widths. It costs a package; the alternative is type
/// that is subtly wrong in the places typographers look first.
/// </para>
/// <para>
/// <b>Shaping's version is not a determinism hazard, and it is worth saying why
/// rather than leaving it to be rediscovered.</b> A different HarfBuzz might one
/// day place a glyph a hundredth of a pixel differently — but shaping happens
/// when the artist types, and what the document stores is the contours that came
/// out of it. Existing art is never re-shaped, so it can never move. This is
/// invariant 4's reasoning — settings that reach pixels are captured at the
/// moment of the mark — applied to a library version instead of a preference.
/// </para>
/// </remarks>
public sealed class TextLayout
{
    public required IReadOnlyList<TextLine> Lines { get; init; }

    /// <summary>Distance between baselines.</summary>
    public required double LineHeight { get; init; }

    /// <summary>Font ascent, negative — the top of the em box above the baseline.</summary>
    public required double Ascent { get; init; }

    /// <summary>Font descent, positive.</summary>
    public required double Descent { get; init; }

    /// <summary>
    /// The block's em box: alignment-wide, ascent to descent, in document
    /// coordinates.
    /// </summary>
    /// <remarks>
    /// The em box rather than the ink bounds, because this is what an artist is
    /// aiming and what a caret has to live inside. A line of "www" and a line of
    /// "..." occupy the same height here, which is the answer somebody
    /// positioning type expects.
    /// </remarks>
    public SKRect Box { get; init; }

    /// <summary>Where the caret sits for a position in the source string.</summary>
    /// <remarks>
    /// <para>
    /// Clamped into the string, and biased to the end of the line rather than
    /// the start of the next one when an index falls on a line break — which is
    /// what typing at the end of a line looks like and is the position an
    /// artist has just left the caret in.
    /// </para>
    /// <para>
    /// A glyph's cluster is the first character it covers, so the caret before
    /// character <c>i</c> is the leading edge of the first glyph whose cluster
    /// is at least <c>i</c>. Past every glyph on the line, it is the line's
    /// trailing edge.
    /// </para>
    /// </remarks>
    public (double X, double Y) Caret(int index)
    {
        if (Lines.Count == 0) return (0, 0);

        var line = Lines[0];
        foreach (var candidate in Lines)
        {
            if (index >= candidate.Start) line = candidate;
        }

        var local = Math.Clamp(index, line.Start, line.Start + line.Length);
        foreach (var glyph in line.Glyphs)
        {
            if (glyph.Cluster >= local) return (glyph.X, line.BaselineY);
        }
        return (line.Left + line.Width, line.BaselineY);
    }

    /// <summary>Shape and place an element's text with a typeface.</summary>
    public static TextLayout Of(TextElement text, SKTypeface typeface)
    {
        using var font = new SKFont(typeface, (float)text.Size);
        var metrics = font.Metrics;
        var ascent = metrics.Ascent;
        var descent = metrics.Descent;

        // The font's own idea of line spacing when nobody has overridden it:
        // the em box plus whatever gap the designer asked for between lines.
        var lineHeight = text.LineHeight ?? (descent - ascent + metrics.Leading);

        // Tracking is thousandths of an em everywhere type is set, which makes a
        // value copied off a specimen mean the same thing here.
        var tracking = text.Tracking / 1000.0 * text.Size;

        using var shaper = new SKShaper(typeface);
        var lines = new List<TextLine>();
        var source = text.Text;
        var start = 0;
        var lineIndex = 0;
        var widest = 0.0;

        while (true)
        {
            var brk = source.IndexOf('\n', start);
            var length = (brk < 0 ? source.Length : brk) - start;
            var content = source.Substring(start, length);
            var baseline = text.Y + lineIndex * lineHeight;

            var glyphs = new List<PlacedGlyph>();
            var width = 0.0;

            if (content.Length > 0)
            {
                var shaped = shaper.Shape(content, font);
                for (var i = 0; i < shaped.Codepoints.Length; i++)
                {
                    glyphs.Add(new PlacedGlyph(
                        (ushort)shaped.Codepoints[i],
                        shaped.Points[i].X + i * tracking,
                        shaped.Points[i].Y + baseline,
                        start + (int)shaped.Clusters[i]));
                }
                // Tracking is applied after every glyph including the last, the
                // way letter-spacing works everywhere else: a tracked-out word
                // centred on a point sits a touch left of centre, and that is
                // what every other application does with it.
                width = shaped.Width + shaped.Codepoints.Length * tracking;
            }

            widest = Math.Max(widest, width);
            lines.Add(new TextLine
            {
                Start = start,
                Length = length,
                Width = width,
                BaselineY = baseline,
                Glyphs = glyphs,
            });

            if (brk < 0) break;
            start = brk + 1;
            lineIndex++;
        }

        // Alignment shifts each line by its own width, so it has to happen after
        // every line is measured — which is why the glyphs are placed relative
        // to the line and moved here rather than positioned outright.
        var placed = new List<TextLine>(lines.Count);
        foreach (var line in lines)
        {
            var left = text.X + text.Align switch
            {
                TextAlign.Centre => -line.Width / 2,
                TextAlign.Right => -line.Width,
                _ => 0,
            };
            placed.Add(new TextLine
            {
                Start = line.Start,
                Length = line.Length,
                Width = line.Width,
                BaselineY = line.BaselineY,
                Left = left,
                Glyphs = [.. line.Glyphs.Select(g => g with { X = g.X + left })],
            });
        }

        var boxLeft = text.X + text.Align switch
        {
            TextAlign.Centre => -widest / 2,
            TextAlign.Right => -widest,
            _ => 0,
        };
        var top = text.Y + ascent;
        var bottom = text.Y + (placed.Count - 1) * lineHeight + descent;

        return new TextLayout
        {
            Lines = placed,
            LineHeight = lineHeight,
            Ascent = ascent,
            Descent = descent,
            Box = new SKRect((float)boxLeft, (float)top, (float)(boxLeft + widest), (float)bottom),
        };
    }
}
