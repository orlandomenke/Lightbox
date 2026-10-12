using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// The path overlay: the pen's line in progress, the white arrow's retrace,
/// and the points and handles of whichever of them is live.
/// </summary>
/// <remarks>
/// <para>
/// Pulled out of <c>CanvasControl.DrawOp</c> for <see cref="GuidePainter"/>'s
/// reason — the draw operation only runs inside a compositor lease a headless
/// run never grants, so nothing in it could be tested — and drawn from
/// <see cref="OverlayStyle"/>, the first overlay to be.
/// </para>
/// <para>
/// <b>Chrome, not paint.</b> The line here is a trace of the shape, never the
/// brush: stamping the real mark on every pointer move for as long as a path
/// takes to place is what invariant 6 refuses.
/// </para>
/// <para>
/// <b>Handles are drawn only for selected points</b>, matching the hit test
/// exactly. An overlay that showed a handle nothing could grab, or hid one
/// that could be, would make the tool feel unreliable in a way that is very
/// hard to report.
/// </para>
/// </remarks>
public static class PathOverlayPainter
{
    /// <param name="scale">
    /// The view scale the canvas is under, so sizes come out in screen pixels:
    /// a point is as easy to grab at 25% as at 800%.
    /// </param>
    public static void Paint(
        SKCanvas canvas,
        IReadOnlyList<CanvasControl.PathNodeGlyph>? nodes,
        IReadOnlyList<StrokePoint>? penPreview,
        IReadOnlyList<StrokePoint>? pathTrace,
        float scale)
    {
        var anyLine = penPreview is { Count: > 1 } || pathTrace is { Count: > 1 };
        if (!anyLine && nodes is not { Count: > 0 }) return;

        using var pens = new OverlayStyle.Pens(scale);

        // Two traces, one look. Separate channels because both can be alive at
        // once: a parked pen path survives a tool switch into isolation.
        Trace(canvas, pens, penPreview);
        Trace(canvas, pens, pathTrace);
        if (nodes is not { Count: > 0 }) return;

        // Stems and dots first, so a stem never draws over the point it
        // belongs to.
        foreach (var n in nodes)
        {
            if (!n.Selected) continue;
            Handle(canvas, pens, n, n.InX, n.InY);
            Handle(canvas, pens, n, n.OutX, n.OutY);
        }

        foreach (var n in nodes)
        {
            var x = (float)n.X;
            var y = (float)n.Y;
            pens.Grip(canvas, x, y, round: !n.Corner, n.Selected);
            // The pen's closing indicator is a ring, because the point under it
            // must stay legible: the ring says the next click joins up here,
            // the point keeps saying what it is.
            if (n.CloseHint) pens.Ring(canvas, x, y, OverlayStyle.GripHalf * OverlayStyle.RingReach);
        }
    }

    private static void Handle(
        SKCanvas canvas, OverlayStyle.Pens pens, CanvasControl.PathNodeGlyph n, double hx, double hy)
    {
        if (hx == 0 && hy == 0) return;
        var ax = (float)(n.X + hx);
        var ay = (float)(n.Y + hy);
        pens.Stem(canvas, (float)n.X, (float)n.Y, ax, ay);
        pens.Dot(canvas, ax, ay);
    }

    private static void Trace(SKCanvas canvas, OverlayStyle.Pens pens, IReadOnlyList<StrokePoint>? points)
    {
        if (points is not { Count: > 1 }) return;
        using var path = new SKPath();
        path.MoveTo((float)points[0].X, (float)points[0].Y);
        for (var i = 1; i < points.Count; i++) path.LineTo((float)points[i].X, (float)points[i].Y);
        pens.Line(canvas, path);
    }
}
