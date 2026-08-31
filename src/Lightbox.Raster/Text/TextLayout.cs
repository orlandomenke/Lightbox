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

    /// <summary>
    /// The highlight behind a selected range: one rectangle per line it covers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per line rather than one shape, because a selection spanning three lines
    /// is three bars and not a polygon — which is what every text field draws
    /// and the only thing that reads correctly when the lines are different
    /// widths.
    /// </para>
    /// <para>
    /// <b>A line fully inside the range is highlighted past its last glyph</b>,
    /// by a token width, so a selected line break is visible. Without it,
    /// selecting three empty lines highlights nothing and the artist cannot see
    /// that Backspace is about to take them.
    /// </para>
    /// </remarks>
    public IReadOnlyList<SKRect> SelectionRects(int start, int end)
    {
        var lo = Math.Min(start, end);
        var hi = Math.Max(start, end);
        if (hi <= lo) return [];

        var rects = new List<SKRect>();
        foreach (var line in Lines)
        {
            var lineEnd = line.Start + line.Length;
            var from = Math.Max(lo, line.Start);
            var to = Math.Min(hi, lineEnd);
            if (to < from) continue;
            // The break at the end of a line is inside the range: show it.
            var breakSelected = hi > lineEnd;
            if (to == from && !breakSelected) continue;

            var left = EdgeOn(line, from);
            var right = breakSelected ? line.Left + line.Width + BreakWidth : EdgeOn(line, to);
            if (right <= left && !breakSelected) continue;

            rects.Add(new SKRect(
                (float)left,
                (float)(line.BaselineY + Ascent),
                (float)Math.Max(right, left + BreakWidth),
                (float)(line.BaselineY + Descent)));
        }
        return rects;
    }

    /// <summary>How wide a selected line break shows as.</summary>
    private const double BreakWidth = 4;

    /// <summary>The x of a caret position, known to be on this line.</summary>
    private static double EdgeOn(TextLine line, int index)
    {
        foreach (var glyph in line.Glyphs)
        {
            if (glyph.Cluster >= index) return glyph.X;
        }
        return line.Left + line.Width;
    }

    /// <summary>
    /// The character position a point lands on — <see cref="Caret"/> run
    /// backwards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The half the tool was missing.</b> Type could say where the caret goes
    /// for an index; nothing could say which index a click meant, so picking up
    /// existing type put the caret at the end of it and there was nowhere for a
    /// selection to start.
    /// </para>
    /// <para>
    /// <b>Nearest edge, not the glyph you hit.</b> A caret sits between
    /// characters, so a click in the left half of a letter means before it and
    /// the right half means after — which is what makes clicking at the end of a
    /// word put the caret at the end of the word rather than inside it. Measured
    /// against each glyph's advance rather than its ink, because a comma and an
    /// "m" both own the space they were allotted.
    /// </para>
    /// <para>
    /// Vertically it is the nearest baseline rather than a band, so a click
    /// above the first line or below the last still lands somewhere: there is no
    /// "outside" for a caret to be, and refusing would mean a click just past
    /// the descender doing nothing.
    /// </para>
    /// </remarks>
    public int IndexAt(double x, double y)
    {
        if (Lines.Count == 0) return 0;

        var line = Lines[0];
        var best = double.MaxValue;
        foreach (var candidate in Lines)
        {
            var distance = Math.Abs(y - candidate.BaselineY);
            if (distance >= best) continue;
            best = distance;
            line = candidate;
        }

        var end = line.Start + line.Length;
        if (line.Glyphs.Count == 0) return line.Start;

        for (var i = 0; i < line.Glyphs.Count; i++)
        {
            var glyph = line.Glyphs[i];
            var right = i + 1 < line.Glyphs.Count ? line.Glyphs[i + 1].X : line.Left + line.Width;
            if (x >= right) continue;
            // Past the glyph's midpoint means the caret belongs after it, which
            // is the next glyph's cluster — or the end of the line for the last.
            if (x <= (glyph.X + right) / 2) return glyph.Cluster;
            return i + 1 < line.Glyphs.Count ? line.Glyphs[i + 1].Cluster : end;
        }
        return end;
    }

    /// <summary>
    /// The word around a position, as the half-open range a double-click takes.
    /// </summary>
    /// <remarks>
    /// Runs of word characters, runs of whitespace and runs of anything else are
    /// each a unit, which is the rule every text field uses: double-clicking a
    /// word takes the word, double-clicking the space between two takes the
    /// space rather than nothing. A newline is never joined to the run beside
    /// it — selecting across a line break by double-click is not something
    /// anybody means.
    /// </remarks>
    public static (int Start, int End) WordAt(string text, int index)
    {
        if (text.Length == 0) return (0, 0);
        var at = Math.Clamp(index, 0, text.Length);
        // A caret at the very end has no character under it; take the one behind.
        if (at == text.Length) at--;
        if (text[at] == '\n') return (at, at + 1);

        var kind = KindOf(text[at]);
        var start = at;
        while (start > 0 && text[start - 1] != '\n' && KindOf(text[start - 1]) == kind) start--;
        var end = at + 1;
        while (end < text.Length && text[end] != '\n' && KindOf(text[end]) == kind) end++;
        return (start, end);
    }

    private static int KindOf(char c) =>
        char.IsLetterOrDigit(c) || c == '\'' ? 0 : char.IsWhiteSpace(c) ? 1 : 2;

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
