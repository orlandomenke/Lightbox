using Lightbox.Core.Timeline;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Draws the arc overlay: the fitted arc under the trail, a pull-line and ring
/// on every off-arc tick, and the predicted positions dashed. Painted just
/// before <see cref="MotionTrailPainter"/> so the trail's own ticks stay on
/// top — the arc is the judgement, the ticks are the facts.
/// </summary>
/// <remarks>
/// <para>
/// A separate class for <see cref="RigOverlayPainter"/>'s reason: everything
/// takes an <see cref="SKCanvas"/> and returns nothing, so a headless test
/// paints into a bitmap and reads pixels.
/// </para>
/// <para>
/// One colour for everything here — gold — because the overlay is one added
/// concept: what the fitted arc has to say. The trail already spends red,
/// blue and white; a palette per judgement would have to be learned against
/// all three.
/// </para>
/// </remarks>
public static class MotionArcPainter
{
    private static readonly SKColor ArcColor = new(0xd8, 0xb0, 0x40);

    /// <summary>
    /// Paint the overlay. Expects the canvas already in document space; null
    /// draws nothing.
    /// </summary>
    /// <param name="scale">
    /// View scale, so line widths and markers stay screen-sized at any zoom.
    /// </param>
    public static void Paint(SKCanvas canvas, MotionArcOverlay? arc, float scale)
    {
        if (arc is null) return;

        var px = 1f / Math.Max(0.01f, scale);
        var tick = MotionTrailPainter.TickScreenRadius * px;

        using var line = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f * px,
            Color = ArcColor.WithAlpha(150),
        };
        DrawPolyline(canvas, arc.Path, line);

        // The pull-line runs from the tick to its foot on the arc — not just
        // that the drawing is off, but where it would sit if it were not.
        foreach (var off in arc.OffArc)
        {
            canvas.DrawLine((float)off.X, (float)off.Y, (float)off.FootX, (float)off.FootY, line);
            line.Color = ArcColor;
            canvas.DrawCircle((float)off.X, (float)off.Y, tick * 1.8f, line);
            line.Color = ArcColor.WithAlpha(150);
        }

        // Predictions are dashed: a suggestion, drawn so it cannot be misread
        // as a drawing that exists. The dash rides document space, so it is
        // scaled to stay screen-sized like everything else here.
        using var dash = SKPathEffect.CreateDash([4f * px, 3f * px], 0);
        line.PathEffect = dash;
        line.Color = ArcColor;
        DrawPolyline(canvas, arc.Extension, line);
        if (arc.Next is { } next) canvas.DrawCircle((float)next.X, (float)next.Y, tick, line);
        if (arc.Current is { } here)
        {
            canvas.DrawCircle((float)here.X, (float)here.Y, tick, line);
            // The ring the trail gives the current drawing, dashed: this is
            // where the playhead's missing drawing belongs.
            canvas.DrawCircle((float)here.X, (float)here.Y, tick * 1.8f, line);
        }
    }

    private static void DrawPolyline(SKCanvas canvas, IReadOnlyList<ArcSample> samples, SKPaint paint)
    {
        if (samples.Count < 2) return;
        using var path = new SKPath();
        path.MoveTo((float)samples[0].X, (float)samples[0].Y);
        for (var i = 1; i < samples.Count; i++)
            path.LineTo((float)samples[i].X, (float)samples[i].Y);
        canvas.DrawPath(path, paint);
    }
}
