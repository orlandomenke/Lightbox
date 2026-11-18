using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// The two keys of a gap drawn side by side with every piece numbered — A1, A2…
/// on key A and B1, B2… on key B — so a model can say which line is which by
/// naming numbers instead of producing coordinates.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why numbers.</b> Q270 measured the model drawing an inbetween: it read
/// which line was which from pictures, and failed at writing exact points for a
/// complex pose. This asks it only the half it was good at. Pointing at marks
/// drawn on a picture is the way a vision model is most reliably grounded; a
/// box or a coordinate list is not (Q180's listing arm).
/// </para>
/// <para>
/// <b>A diagram, not the drawing.</b> Pieces are drawn as plain polylines in
/// distinct colours, not through <c>BrushEngine</c>: the reader needs to tell
/// lines apart and find their numbers, and the artist's brush would make
/// neighbouring lines run together. Nothing is stamped, so no dab is seeded and
/// invariant 7 is not in play; the record is never touched.
/// </para>
/// <para>
/// <b>Same frame for both keys.</b> Both panels crop to the union of the two
/// keys' bounds, so a line that did not move sits in the same place in each
/// panel. The other key is drawn faintly beneath, for where things are
/// relative to each other.
/// </para>
/// </remarks>
internal static class MatchPiecesPicture
{
    /// <summary>The long edge of one panel, in pixels.</summary>
    internal const int PanelLongEdge = 640;

    /// <summary>The most a caller may ask for: two panels and the gutter stay within about 1568px.</summary>
    internal const int MaxPanelLongEdge = 778;

    private const int Pad = 24;
    private const int Gutter = 12;
    private const int Header = 24;

    /// <summary>One numbered piece: its name in the picture, what it is in the record, and where it is in the scene.</summary>
    internal sealed record Numbered(string Name, PieceRef Ref, LinePiece Piece);

    /// <summary>Name the pieces of a key: <paramref name="side"/> and their place in the cut, from 1.</summary>
    internal static List<Numbered> Number(IReadOnlyList<LinePiece> pieces, char side) =>
        [.. pieces.Select((p, i) => new Numbered($"{side}{i + 1}", new PieceRef(p.Source, p.Ordinal), p))];

    /// <summary>
    /// Render the two keys' numbered pieces side by side as a PNG. Pieces in a
    /// match share a colour across the panels.
    /// </summary>
    internal static byte[] Render(
        IReadOnlyList<Numbered> a, IReadOnlyList<Numbered> b,
        IReadOnlyList<LineMatch>? matches, string captionA, string captionB,
        int panelLongEdge = PanelLongEdge)
    {
        var all = a.Concat(b).SelectMany(n => n.Piece.Stroke.Points).ToList();
        double minX = 0, minY = 0, maxX = 1, maxY = 1;
        if (all.Count > 0)
        {
            minX = all.Min(p => p.X) - Pad; minY = all.Min(p => p.Y) - Pad;
            maxX = all.Max(p => p.X) + Pad; maxY = all.Max(p => p.Y) + Pad;
        }
        var (w, h) = (maxX - minX, maxY - minY);
        var scale = (float)(Math.Max(1, panelLongEdge) / Math.Max(w, h));
        var panelW = Math.Max(1, (int)Math.Ceiling(w * scale));
        var panelH = Math.Max(1, (int)Math.Ceiling(h * scale));

        var info = new SKImageInfo(panelW * 2 + Gutter, panelH + Header, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Could not make a surface for the match picture.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        var colourOf = ColoursOf(a, b, matches);
        Panel(canvas, 0, a, b, colourOf, captionA, minX, minY, scale, panelW, panelH);
        Panel(canvas, panelW + Gutter, b, a, colourOf, captionB, minX, minY, scale, panelW, panelH);

        using var line = new SKPaint { Color = new SKColor(0xC8, 0xC8, 0xC8), StrokeWidth = 1 };
        canvas.DrawLine(panelW + Gutter / 2f, 0, panelW + Gutter / 2f, panelH + Header, line);
        canvas.Flush();
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG encode failed.");
        return data.ToArray();
    }

    /// <summary>
    /// A colour for every piece: one per match, shared by both its sides, and
    /// one each for unmatched pieces after those — so a reader can see at a
    /// glance what is already matched.
    /// </summary>
    private static Dictionary<string, SKColor> ColoursOf(
        IReadOnlyList<Numbered> a, IReadOnlyList<Numbered> b, IReadOnlyList<LineMatch>? matches)
    {
        var colours = new Dictionary<string, SKColor>();
        var next = 0;
        foreach (var m in matches ?? [])
        {
            var c = OverlayStyle.Match(next++);
            foreach (var n in a.Where(n => m.A.Contains(n.Ref))) colours[n.Name] = c;
            foreach (var n in b.Where(n => m.B.Contains(n.Ref))) colours[n.Name] = c;
        }
        foreach (var n in a.Concat(b))
        {
            if (!colours.ContainsKey(n.Name)) colours[n.Name] = OverlayStyle.Match(next++);
        }
        return colours;
    }

    private static void Panel(
        SKCanvas canvas, float left, IReadOnlyList<Numbered> own, IReadOnlyList<Numbered> other,
        Dictionary<string, SKColor> colourOf, string caption,
        double minX, double minY, float scale, int panelW, int panelH)
    {
        SKPoint At(StrokePoint p) => new(left + (float)((p.X - minX) * scale), Header + (float)((p.Y - minY) * scale));

        using var font = new SKFont(SKTypeface.FromFamilyName(null, SKFontStyle.Bold), 13);
        using var ink = new SKPaint { Color = new SKColor(0x30, 0x30, 0x30), IsAntialias = true };
        canvas.DrawText(caption, left + 6, 16, font, ink);

        using var faint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 38), StrokeWidth = 2, IsAntialias = true,
            Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
        };
        foreach (var n in other) Polyline(canvas, n.Piece.Stroke.Points, At, faint);

        using var pen = new SKPaint
        {
            StrokeWidth = 3, IsAntialias = true,
            Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
        };
        foreach (var n in own)
        {
            pen.Color = colourOf[n.Name];
            Polyline(canvas, n.Piece.Stroke.Points, At, pen);
        }

        // Labels last, over every line, each where it is furthest from the
        // labels already placed — a crowded joint otherwise stacks them unreadably.
        using var halo = new SKPaint
        {
            Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = 4, StrokeJoin = SKStrokeJoin.Round,
        };
        using var text = new SKPaint { IsAntialias = true };
        var placed = new List<SKPoint>();
        foreach (var n in own)
        {
            var spot = LabelSpot(n.Piece.Stroke.Points.Select(At).ToList(), placed);
            placed.Add(spot);
            var width = font.MeasureText(n.Name);
            var (x, y) = (spot.X - width / 2, spot.Y + 5);
            canvas.DrawText(n.Name, x, y, font, halo);
            text.Color = Darker(colourOf[n.Name]);
            canvas.DrawText(n.Name, x, y, font, text);
        }
    }

    private static void Polyline(SKCanvas canvas, IReadOnlyList<StrokePoint> points, Func<StrokePoint, SKPoint> at, SKPaint paint)
    {
        if (points.Count == 0) return;
        if (points.Count == 1)
        {
            var p = at(points[0]);
            canvas.DrawPoint(p, paint);
            return;
        }
        using var path = new SKPath();
        path.MoveTo(at(points[0]));
        for (var i = 1; i < points.Count; i++) path.LineTo(at(points[i]));
        canvas.DrawPath(path, paint);
    }

    /// <summary>
    /// Where along a piece its label goes: of a few places along it, the one
    /// furthest from every label already placed, taking the middle when it is
    /// clear enough.
    /// </summary>
    internal static SKPoint LabelSpot(IReadOnlyList<SKPoint> points, IReadOnlyList<SKPoint> placed)
    {
        if (points.Count == 0) return SKPoint.Empty;
        const float Clear = 20;
        SKPoint best = points[points.Count / 2];
        var bestRoom = -1f;
        foreach (var f in new[] { 0.5, 0.35, 0.65, 0.2, 0.8, 0.08, 0.92 })
        {
            var p = points[(int)Math.Round(f * (points.Count - 1))];
            var room = placed.Count == 0 ? float.MaxValue : placed.Min(q => SKPoint.Distance(p, q));
            if (room > bestRoom) (best, bestRoom) = (p, room);
            if (bestRoom >= Clear) break;
        }
        return best;
    }

    /// <summary>A label's colour: its line's, darkened so it reads on white.</summary>
    private static SKColor Darker(SKColor c) =>
        new((byte)(c.Red * 0.7), (byte)(c.Green * 0.7), (byte)(c.Blue * 0.7));

    /// <summary>The scene box of a piece, [x, y, w, h], rounded to a pixel.</summary>
    internal static double[] Box(LinePiece piece)
    {
        var pts = piece.Stroke.Points;
        if (pts.Count == 0) return [0, 0, 0, 0];
        var (x0, y0) = (pts.Min(p => p.X), pts.Min(p => p.Y));
        var (x1, y1) = (pts.Max(p => p.X), pts.Max(p => p.Y));
        return [Math.Round(x0), Math.Round(y0), Math.Round(x1 - x0), Math.Round(y1 - y0)];
    }

    /// <summary>A piece's length along its points, rounded to a pixel.</summary>
    internal static double Length(LinePiece piece)
    {
        var pts = piece.Stroke.Points;
        var length = 0.0;
        for (var i = 1; i < pts.Count; i++) length += GeometryOps.Dist(pts[i - 1], pts[i]);
        return Math.Round(length);
    }
}
