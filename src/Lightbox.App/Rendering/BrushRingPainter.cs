using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Draws the brush ring: the outline that follows the pointer while paint hides
/// the platform cursor.
/// </summary>
/// <remarks>
/// <para>
/// <b>One line, in one of two inks.</b> This drew a dark ring with a light one
/// nested inside it — visible over anything, and 2.4 px of the drawing hidden all
/// the way round the brush. At the sizes an artist actually paints at that band
/// covers the edge you are aiming at, which is the one thing the ring exists not
/// to do. <see cref="CursorContrast"/> carries the rest of that argument and
/// decides which ink survives the artwork underneath.
/// </para>
/// <para>
/// <b>Out here rather than inside the canvas control, so a test can see it.</b>
/// The ring is drawn on the render thread inside the control's render op — not
/// in the published <c>RenderSnapshot</c>, and the suite's headless software
/// backend cannot capture a rendered frame at all, so for as long as this lived
/// in the control there was no way to assert anything about what it looks like.
/// A static that takes a canvas can be pointed at a bare <see cref="SKSurface"/>,
/// which is how <see cref="PickRing"/> and <see cref="CursorBadgePainter"/> are
/// already guarded.
/// </para>
/// </remarks>
public static class BrushRingPainter
{
    /// <summary>
    /// Stroke the ring at a view-space position, with a view-space radius.
    /// </summary>
    /// <param name="outline">
    /// The tip's silhouette in unit space (B74), or null for a brush with no tip
    /// — where <paramref name="roundness"/> and <paramref name="angleDeg"/>
    /// describe the ellipse the engine's round dab actually is.
    /// </param>
    public static void Draw(
        SKCanvas canvas,
        float x, float y, float radius,
        float roundness = 1f,
        float angleDeg = 0f,
        SKPath? outline = null,
        CursorInk ink = CursorInk.Dark,
        CursorBadge badge = CursorBadge.None)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = CursorContrast.StrokeWidth,
            Color = CursorContrast.ColorFor(ink),
        };

        if (outline is not null)
        {
            // Unit space to view space: the diameter is 2r, and the tip's own
            // aspect is already in the traced contour — so roundness flattens
            // it further rather than defining it, exactly as the engine
            // multiplies roundness onto whatever tip it is stamping.
            canvas.Save();
            canvas.Translate(x, y);
            if (angleDeg != 0) canvas.RotateDegrees(angleDeg);
            canvas.Scale(radius * 2, radius * 2 * roundness);
            // The stroke is scaled with the canvas, so undo it in the paint or
            // a big brush gets a fat ring and a small one gets none.
            paint.StrokeWidth = CursorContrast.StrokeWidth / (radius * 2);
            canvas.DrawPath(outline, paint);
            canvas.Restore();
            return;
        }

        if (roundness < 0.999f || angleDeg != 0)
        {
            canvas.Save();
            canvas.Translate(x, y);
            if (angleDeg != 0) canvas.RotateDegrees(angleDeg);
            canvas.DrawOval(new SKRect(-radius, -radius * roundness, radius, radius * roundness), paint);
            canvas.Restore();
            return;
        }

        canvas.DrawCircle(x, y, radius, paint);

        // The +/− beside the ring, lower-right in screen pixels — the
        // weight brush's mode visible where the artist is looking.
        if (badge is not CursorBadge.None)
        {
            var at = radius * 0.7071f + 8f;
            CursorBadgePainter.Draw(canvas, x + at, y + at, badge);
        }
    }
}
