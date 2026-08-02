using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// The perspective rig — guides, vanishing points and grids — and the order it
/// goes on in relative to the artwork.
/// </summary>
/// <remarks>
/// <para>
/// Pulled out of <c>CanvasControl.DrawOp</c> so it can be tested. The draw op
/// only runs inside a Skia lease from the compositor, which a headless run
/// never grants, so everything in it was unreachable — and B17 lived there:
/// guides were drawn <i>under</i> the artwork on the reasoning that "a ruler
/// on paper is something you draw over", which does not survive a document
/// that opens with an opaque white background layer. The whole rig vanished
/// the moment it crossed the canvas.
/// </para>
/// <para>
/// The order is the fix and so the order is what
/// <see cref="PaintDocument"/> owns: checkerboard, artwork, then guides over
/// the top, translucent. Splitting those three apart is what let the bug
/// happen, so they are one call now.
/// </para>
/// </remarks>
public static class GuidePainter
{
    private const int KindGrid = 1;
    private const int KindVanishingPoint = 3;

    /// <summary>A guide flattened for the render thread. See <c>CanvasControl.GuideLine</c>.</summary>
    public readonly record struct Line(
        int Kind, float X, float Y, float Spacing, IReadOnlyList<double> Angles);

    /// <summary>
    /// Checkerboard, artwork, guides — in the one order that works.
    /// </summary>
    /// <remarks>
    /// The canvas is expected to be in document space already. Guides last and
    /// translucent: what the old under-the-art order was protecting — not
    /// hiding the drawing — is paid for with alpha instead, so the rig reads
    /// as a rig and the art still reads through it.
    /// </remarks>
    public static void PaintDocument(
        SKCanvas canvas,
        SKImage? artwork,
        float docW,
        float docH,
        float scale,
        Action<SKCanvas>? checkerboard,
        IReadOnlyList<Line>? guides,
        Line? draft)
    {
        checkerboard?.Invoke(canvas);
        if (artwork is not null)
        {
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawImage(
                artwork, new SKRect(0, 0, docW, docH), new SKSamplingOptions(SKFilterMode.Linear), paint);
        }
        Paint(canvas, guides, draft, docW, docH, scale);
    }

    /// <summary>
    /// The rig alone, over whatever is already on the canvas.
    /// </summary>
    /// <remarks>
    /// Every line is divided by the zoom so it stays a hairline on screen. A
    /// guide that scaled with the view would be invisible at 25% and a stripe
    /// at 800%, which is the opposite of what chrome is for.
    /// </remarks>
    public static void Paint(
        SKCanvas canvas, IReadOnlyList<Line>? guides, Line? draft, float docW, float docH, float scale)
    {
        scale = Math.Max(0.01f, scale);
        var reach = (docW + docH) * 2f;

        if (guides is { Count: > 0 } lines)
        {
            using var thin = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1f / scale,
                Color = new SKColor(80, 150, 240, 110),
                IsAntialias = true,
            };
            using var mark = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f / scale,
                Color = new SKColor(240, 120, 80, 200),
                IsAntialias = true,
            };

            foreach (var guide in lines)
            {
                if (guide.Kind == KindGrid)
                {
                    Grid(canvas, guide, thin, scale, docW, docH);
                    continue;
                }
                foreach (var angle in guide.Angles) Ray(canvas, guide.X, guide.Y, angle, reach, thin);
                if (guide.Kind != KindVanishingPoint) continue;
                // A vanishing point is a place as well as a set of directions,
                // and without a mark on it you cannot tell which of the rays
                // meet where.
                var arm = 7f / scale;
                canvas.DrawLine(guide.X - arm, guide.Y, guide.X + arm, guide.Y, mark);
                canvas.DrawLine(guide.X, guide.Y - arm, guide.X, guide.Y + arm, mark);
            }
        }

        if (draft is not { } pulled) return;
        // Brighter than a placed guide, because it is the thing you are
        // looking at: the whole gesture is aiming a line, and a draft drawn in
        // the same faint blue as the rig behind it cannot be aimed.
        using var drafting = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f / scale,
            Color = new SKColor(240, 160, 80, 220),
            IsAntialias = true,
        };
        foreach (var angle in pulled.Angles) Ray(canvas, pulled.X, pulled.Y, angle, reach, drafting);
    }

    private static void Grid(
        SKCanvas canvas, Line guide, SKPaint paint, float scale, float docW, float docH)
    {
        var pitch = Math.Max(1f, guide.Spacing);
        // Below a few pixels on screen a grid is a grey wash. Drawing it anyway
        // costs thousands of lines to produce something nobody can use, which
        // is invariant 6's spirit applied to chrome.
        if (pitch * scale < 4) return;

        var reach = Math.Max(docW, docH) * 2f;
        var steps = (int)Math.Ceiling(reach / pitch);
        var angle = guide.Angles.Count > 0 ? guide.Angles[0] : 0;
        var radians = angle * Math.PI / 180;
        var (cos, sin) = ((float)Math.Cos(radians), (float)Math.Sin(radians));

        for (var i = -steps; i <= steps; i++)
        {
            var offset = i * pitch;
            // One family of lines, then the other: the grid's own axes,
            // rotated with it.
            Ray(canvas, guide.X - sin * offset, guide.Y + cos * offset, angle, reach, paint);
            Ray(canvas, guide.X + cos * offset, guide.Y + sin * offset, angle + 90, reach, paint);
        }
    }

    private static void Ray(
        SKCanvas canvas, float x, float y, double degrees, float reach, SKPaint paint)
    {
        var radians = degrees * Math.PI / 180;
        var dx = (float)Math.Cos(radians) * reach;
        var dy = (float)Math.Sin(radians) * reach;
        canvas.DrawLine(x - dx, y - dy, x + dx, y + dy, paint);
    }
}
