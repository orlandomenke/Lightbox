using Lightbox.Core.Timeline;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Draws the motion trail: a polyline through the subject's position on each
/// drawing around the playhead, with a tick per drawing. The distances between
/// ticks are the spacing — the trail is the chart, not a decoration on one.
/// </summary>
/// <remarks>
/// <para>
/// A separate class for <see cref="RigOverlayPainter"/>'s reason: the draw op
/// only runs inside a Skia lease, which a headless run never grants, so
/// everything here takes an <see cref="SKCanvas"/> and returns nothing — a test
/// paints into a bitmap and reads pixels.
/// </para>
/// <para>
/// Before/after take the onion skin's tints (red behind, blue ahead), because
/// the artist already reads those two colours as "earlier" and "later" and a
/// third convention on the same canvas would have to be learned against the
/// first two. The current drawing's tick is white, the colour that wins over
/// both — <see cref="RigOverlayPainter"/>'s selection rule.
/// </para>
/// </remarks>
public static class MotionTrailPainter
{
    private static readonly SKColor BeforeColor = new(0xd0, 0x40, 0x40);
    private static readonly SKColor AfterColor = new(0x30, 0x60, 0xc0);
    private static readonly SKColor CurrentColor = new(0xff, 0xff, 0xff);

    /// <summary>Tick radius in screen pixels — sized to read, not to cover the drawing.</summary>
    public const float TickScreenRadius = 3.5f;

    /// <summary>
    /// Paint the trail. Expects the canvas already in document space; a null
    /// or single-point list draws nothing — one position is not a motion.
    /// </summary>
    /// <param name="scale">
    /// View scale, so ticks and line widths stay screen-sized at any zoom.
    /// </param>
    public static void Paint(SKCanvas canvas, IReadOnlyList<TrailPoint>? points, float scale)
    {
        if (points is not { Count: > 1 }) return;

        var px = 1f / Math.Max(0.01f, scale);
        var tick = TickScreenRadius * px;

        using var line = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f * px,
        };
        using var dot = new SKPaint { IsAntialias = true };

        // The split is the record's, not re-derived here: each tick carries
        // which side of the playhead it is on, so a playhead standing on a
        // drawing with no tick of its own cannot skew the tints. A segment
        // takes its later endpoint's side — the walk *into* the current
        // drawing is part of the past, the walk out of it is the future.
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            line.Color = (b.Before || b.Current ? BeforeColor : AfterColor).WithAlpha(170);
            canvas.DrawLine((float)a.X, (float)a.Y, (float)b.X, (float)b.Y, line);
        }

        foreach (var p in points)
        {
            var color = p.Current ? CurrentColor
                : p.Before ? BeforeColor
                : AfterColor;
            var x = (float)p.X;
            var y = (float)p.Y;
            // An authored anchor is a statement and draws filled; a derived
            // centre is a guess and draws hollow, so the artist can see which
            // ticks to trust before adjusting a drawing to fix an arc.
            if (p.Anchored)
            {
                dot.Style = SKPaintStyle.Fill;
                dot.Color = color;
                canvas.DrawCircle(x, y, tick, dot);
            }
            else
            {
                dot.Style = SKPaintStyle.Stroke;
                dot.StrokeWidth = 1.5f * px;
                dot.Color = color;
                canvas.DrawCircle(x, y, tick * 0.85f, dot);
            }
            // A ring around the current tick, whichever fill it has, so the
            // playhead's drawing is findable at a glance mid-flip.
            if (p.Current)
            {
                dot.Style = SKPaintStyle.Stroke;
                dot.StrokeWidth = 1.5f * px;
                dot.Color = CurrentColor;
                canvas.DrawCircle(x, y, tick * 1.8f, dot);
            }
        }
    }
}
