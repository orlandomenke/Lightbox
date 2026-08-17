using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Draws the cursor's +/− badge (<see cref="CursorBadge"/>) beside a pointer
/// gizmo — today the brush ring, whose platform cursor is hidden and so
/// cannot carry the badge itself.
/// </summary>
/// <remarks>
/// A separate class for <see cref="ArmatureOverlayPainter"/>'s reason (B58's
/// lesson): a draw op only runs inside a compositor lease a headless test
/// never gets, so anything painted there is unreachable by test. Everything
/// here takes an <see cref="SKCanvas"/>, so a test paints into a bitmap and
/// reads pixels. Each mark is drawn twice — a pale wide pass under the dark
/// one — for the marching-ants reason every cursor here follows.
/// </remarks>
public static class CursorBadgePainter
{
    /// <summary>Half-extent of the glyph's strokes, in the pixels of the given canvas.</summary>
    public const float Reach = 4f;

    /// <summary>
    /// Draw the badge centred at (<paramref name="x"/>, <paramref name="y"/>),
    /// in screen pixels. <see cref="CursorBadge.None"/> draws nothing.
    /// </summary>
    public static void Draw(SKCanvas canvas, float x, float y, CursorBadge badge)
    {
        if (badge is CursorBadge.None) return;

        using var halo = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3.6f,
            StrokeCap = SKStrokeCap.Round,
            Color = new SKColor(255, 255, 255, 220),
        };
        using var line = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.6f,
            StrokeCap = SKStrokeCap.Round,
            Color = new SKColor(0, 0, 0, 220),
        };

        foreach (var paint in new[] { halo, line })
        {
            canvas.DrawLine(x - Reach, y, x + Reach, y, paint);
            if (badge is CursorBadge.Add)
                canvas.DrawLine(x, y - Reach, x, y + Reach, paint);
        }
    }
}
