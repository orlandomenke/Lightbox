using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Draws what the bucket or the wand would take at the pointer. The bucket's
/// answer is a tint of the colour in hand plus its outline — "this is the
/// mark the click makes"; the wand's is a faint, still dash — selection's
/// visual language (the dash means selection) at preview strength, unanimated
/// so it cannot be read as already selected.
/// </summary>
/// <remarks>
/// A static painter rather than more of <c>DrawOp</c>, for two reasons: the
/// ratcheted file gains nothing, and pure chrome is bitmap-testable here the
/// way <c>ArmatureOverlayPainter</c> is and a draw-op member is not.
/// </remarks>
public static class FillPreviewPainter
{
    public static void Draw(SKCanvas canvas, SKPath? region, bool wand, SKColor color, float viewScale)
    {
        if (region is null) return;
        var scale = Math.Max(0.01f, viewScale);
        if (wand)
        {
            var dash = 4f / scale;
            using var faintWhite = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.2f / scale,
                Color = new SKColor(255, 255, 255, 110),
                PathEffect = SKPathEffect.CreateDash([dash, dash], dash),
            };
            using var faintBlack = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.2f / scale,
                Color = new SKColor(0, 0, 0, 110),
                PathEffect = SKPathEffect.CreateDash([dash, dash], 0),
            };
            canvas.DrawPath(region, faintBlack);
            canvas.DrawPath(region, faintWhite);
            return;
        }
        using var tint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = color.WithAlpha(48),
        };
        using var outline = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f / scale,
            Color = color.WithAlpha(180),
        };
        canvas.DrawPath(region, tint);
        canvas.DrawPath(region, outline);
    }
}
