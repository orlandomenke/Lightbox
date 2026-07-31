using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// The stamp-based brush. Walks a stroke's path by arc length and stamps
/// round dabs whose radius and position follow pressure. Softness comes from
/// a radial-gradient dab (solid to <c>Hardness</c>, falling off to the rim).
///
/// A stroke is stamped at full alpha onto a scratch surface, then composited
/// once at the stroke's opacity — so overlapping dabs inside one stroke never
/// darken each other, matching how painting apps behave.
///
/// This is deliberately the ONLY place pixels are produced from strokes:
/// live painting, inbetween re-render, AI strokes, and undo re-render all
/// call into it, which is what makes generated frames indistinguishable from
/// hand-painted ones.
/// </summary>
public static class BrushEngine
{
    private const double MinPressure = 0.05;
    private const double MinStepPx = 0.5;

    /// <summary>
    /// Stamp a stroke onto <paramref name="target"/>. Brush strokes composite
    /// SrcOver; eraser strokes composite DstOut (they remove layer content).
    /// </summary>
    public static void StampStroke(SKCanvas target, Stroke stroke, SKImageInfo info)
    {
        if (stroke.Points.Count == 0) return;

        using var scratch = SKSurface.Create(info);
        if (scratch is null) throw new InvalidOperationException("Could not create scratch surface.");
        var canvas = scratch.Canvas;
        canvas.Clear(SKColors.Transparent);

        var color = ParseColor(stroke.Color);
        foreach (var (pos, pressure) in DabPositions(stroke))
        {
            StampDab(canvas, pos, pressure, stroke.Brush, color);
        }

        using var snapshot = scratch.Snapshot();
        using var paint = new SKPaint
        {
            Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(stroke.Brush.Opacity, 0, 1) * 255)),
            BlendMode = stroke.Tool == ToolKind.Eraser ? SKBlendMode.DstOut : SKBlendMode.SrcOver,
        };
        target.DrawImage(snapshot, 0, 0, paint);
    }

    /// <summary>
    /// Dab centers and pressures along the stroke, spaced by
    /// <c>Brush.Spacing × dab size</c> of arc length (floor 0.5 px).
    /// </summary>
    public static IEnumerable<(SKPoint Pos, double Pressure)> DabPositions(Stroke stroke)
    {
        var pts = stroke.Points;
        var first = pts[0];
        yield return (new SKPoint((float)first.X, (float)first.Y), Math.Max(first.Pressure, MinPressure));
        if (pts.Count == 1) yield break;

        var step = Math.Max(stroke.Brush.Size * stroke.Brush.Spacing, MinStepPx);
        double acc = 0;
        var prev = first;
        for (var i = 1; i < pts.Count; i++)
        {
            var cur = pts[i];
            var d = GeometryOps.Dist(prev, cur);
            while (d > 0 && acc + d >= step)
            {
                var t = (step - acc) / d;
                var np = GeometryOps.LerpPoint(prev, cur, t);
                yield return (new SKPoint((float)np.X, (float)np.Y), Math.Max(np.Pressure, MinPressure));
                d -= step - acc;
                acc = 0;
                prev = np;
            }
            acc += d;
            prev = cur;
        }
    }

    private static void StampDab(SKCanvas canvas, SKPoint pos, double pressure, BrushSettings brush, SKColor color)
    {
        var radius = (float)(brush.Size * pressure / 2);
        if (radius <= 0) return;

        using var paint = new SKPaint { IsAntialias = true };
        var hardness = (float)Math.Clamp(brush.Hardness, 0, 1);
        if (hardness >= 0.999f)
        {
            paint.Color = color;
        }
        else
        {
            paint.Shader = SKShader.CreateRadialGradient(
                pos,
                radius,
                [color, color.WithAlpha(0)],
                [hardness, 1f],
                SKShaderTileMode.Clamp);
        }
        canvas.DrawCircle(pos, radius, paint);
    }

    public static SKColor ParseColor(string hex)
    {
        var (r, g, b) = ColorOps.HexToRgb(hex);
        return new SKColor(r, g, b);
    }
}
