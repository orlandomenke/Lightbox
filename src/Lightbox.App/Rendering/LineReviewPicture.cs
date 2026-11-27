using Lightbox.Core.Cleanup;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// The picture a line review sends (Q296): key A, the inbetween and key B side
/// by side over the same bounds, every line labelled with the number the
/// listing gives it — A1…, L1…, B1… — so a model can say "L7" and mean a line.
/// </summary>
/// <remarks>
/// <para>
/// <b>Same bounds in all three</b>, cropped to the drawings: what moved
/// between the keys reads straight across, and the paper around the subject
/// is not paid for.
/// </para>
/// <para>
/// <b>Rendered by the brush engine, scaled as a surface</b> (invariant 7), so
/// the lines are the artist's marks; the labels are drawn over them, each at a
/// third of the way along its line so the two passes of a doubled line, which
/// start together, are not labelled on top of each other.
/// </para>
/// </remarks>
public static class LineReviewPicture
{
    /// <summary>The longest edge the whole picture is sent at, in pixels.</summary>
    public const int LongEdge = 1536;

    /// <summary>The most pixels the picture is sent at: what providers take without scaling it down.</summary>
    public const double MaxPixels = 1_150_000;

    private const int Margin = 24, Gap = 16, Head = 22;

    /// <summary>
    /// Paper, ink, the keys' label colour and the inbetween's: a picture for a
    /// model, not canvas chrome, so its colours are its own — named once here.
    /// </summary>
    private static readonly (SKColor Paper, SKColor Ink, SKColor Key, SKColor Inbetween) Colours = (SKColors.White, SKColors.Black, new SKColor(30, 90, 200), new SKColor(200, 40, 20));

    /// <summary>The picture, as base64 PNG.</summary>
    public static string Png(ReviewSheet sheet)
    {
        var all = sheet.KeyA.Concat(sheet.Lines).Concat(sheet.KeyB).SelectMany(s => s.Points)
            .Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y)).ToList();
        if (all.Count == 0) all = [new StrokePoint(0, 0, 0), new StrokePoint(1, 1, 0)];
        var box = GeometryOps.BoundsOf(all);
        var left = (int)Math.Floor(box.MinX) - Margin;
        var top = (int)Math.Floor(box.MinY) - Margin;
        var w = Math.Max(1, (int)Math.Ceiling(box.MaxX - box.MinX) + 2 * Margin);
        var h = Math.Max(1, (int)Math.Ceiling(box.MaxY - box.MinY) + 2 * Margin);
        var scale = Math.Min(1.0, (LongEdge - 2 * Gap) / (3.0 * w));
        scale = Math.Min(scale, (LongEdge - Head) / (double)h);
        // The area as well as the edge: a tall subject at 1536 wide is over
        // what a provider takes unscaled, and its downscale shrinks the labels
        // past reading (ai-engineer).
        var area = (3.0 * w * scale + 2 * Gap) * (h * scale + Head);
        if (area > MaxPixels) scale *= Math.Sqrt(MaxPixels / area);
        var pw = (int)Math.Floor(w * scale);
        var ph = (int)Math.Floor(h * scale);

        using var sheetBitmap = new SKBitmap(3 * pw + 2 * Gap, ph + Head);
        using var canvas = new SKCanvas(sheetBitmap);
        canvas.Clear(Colours.Paper);
        using var font = new SKFont(SKTypeface.Default, 13);
        using var title = new SKPaint { Color = Colours.Ink, IsAntialias = true };
        (string Title, string Prefix, IReadOnlyList<Stroke> Lines, SKColor Ink)[] panels =
        [
            ("Key A (artist)", "A", sheet.KeyA, Colours.Key),
            ("Inbetween under review", "L", sheet.Lines, Colours.Inbetween),
            ("Key B (artist)", "B", sheet.KeyB, Colours.Key),
        ];
        for (var i = 0; i < panels.Length; i++)
        {
            var x = i * (pw + Gap);
            canvas.DrawText(panels[i].Title, x + 4, 16, font, title);
            using var bitmap = FrameRasterizer.Rasterize(panels[i].Lines, w, h, scale, new SKPointI(left, top));
            canvas.DrawBitmap(bitmap, x, Head);
            using var label = new SKPaint { Color = panels[i].Ink, IsAntialias = true };
            using var halo = new SKPaint { Color = Colours.Paper.WithAlpha(220), IsAntialias = true };
            var placed = new List<SKRect>();
            for (var n = 0; n < panels[i].Lines.Count; n++)
            {
                var line = panels[i].Lines[n].Points;
                // A dot at each line's start, because every fix names an end.
                var start = line[0];
                canvas.DrawCircle(x + (float)((start.X - left) * scale), Head + (float)((start.Y - top) * scale), 2.5f, label);
                var at = AlongThird(line);
                var ax = x + (float)((at.X - left) * scale);
                var ay = Head + (float)((at.Y - top) * scale);
                var lx = ax + 3;
                var ly = ay - 3;
                var text = panels[i].Prefix + (n + 1);
                var width = font.MeasureText(text);
                // Two passes of a doubled line put their labels on top of each
                // other; the later one steps aside until it is clear.
                var tag = new SKRect(lx - 1, ly - 11, lx + width + 1, ly + 2);
                for (var step = 0; step < 8 && placed.Any(r => r.IntersectsWith(tag)); step++)
                {
                    tag.Offset(width + 4, 0);
                }
                placed.Add(tag);
                // A label that stepped aside says which line it is with a leader.
                if (tag.Left > lx + 1) canvas.DrawLine(ax, ay, tag.Left, tag.MidY, label);
                canvas.DrawRect(tag, halo);
                canvas.DrawText(text, tag.Left + 1, tag.Bottom - 2, font, label);
            }
        }
        canvas.Flush();
        using var data = sheetBitmap.Encode(SKEncodedImageFormat.Png, 100);
        return Convert.ToBase64String(data.ToArray());
    }

    private static StrokePoint AlongThird(IReadOnlyList<StrokePoint> points)
    {
        var length = GeometryOps.PathLength(points);
        var target = length / 3;
        var walked = 0.0;
        for (var i = 1; i < points.Count; i++)
        {
            var seg = GeometryOps.Dist(points[i - 1], points[i]);
            if (walked + seg >= target && seg > 0) return GeometryOps.LerpPoint(points[i - 1], points[i], (target - walked) / seg);
            walked += seg;
        }
        return points[0];
    }
}
