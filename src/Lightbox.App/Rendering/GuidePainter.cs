using SkiaSharp;
using System;

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
    private const int KindHeightScale = 4;

    /// <summary>A guide flattened for the render thread. See <c>CanvasControl.GuideLine</c>.</summary>
    /// <remarks>
    /// <paramref name="Label"/> is the guide's name when it has one — an
    /// eye-line that does not say "eye line" is just a line, and a shared rig
    /// has to read at a glance. <paramref name="Divisions"/> is a height
    /// scale's head count and zero on everything else.
    /// </remarks>
    public readonly record struct Line(
        int Kind, float X, float Y, float Spacing, IReadOnlyList<double> Angles,
        string? Label = null, int Divisions = 0);

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
        Line? draft,
        SKRectI? docViewport = null,
        SKColorFilter? artworkFilter = null)
    {
        checkerboard?.Invoke(canvas);
        if (artwork is not null)
        {
            // The filter applies to the artwork alone — soloing a channel must
            // not grey out the guides an artist is still aiming with.
            using var paint = new SKPaint { IsAntialias = true, ColorFilter = artworkFilter };

            // When compositing is viewport-culled, the image is smaller than the document.
            // Draw it at the viewport position with its actual size.
            if (docViewport is { } vp && (vp.Width > 0 && vp.Height > 0))
            {
                canvas.DrawImage(
                    artwork,
                    new SKRect(vp.Left, vp.Top, vp.Left + vp.Width, vp.Top + vp.Height),
                    new SKSamplingOptions(SKFilterMode.Linear),
                    paint);
            }
            else
            {
                // Normal case: draw full-document image at (0,0) to (docW, docH)
                canvas.DrawImage(
                    artwork, new SKRect(0, 0, docW, docH), new SKSamplingOptions(SKFilterMode.Linear), paint);
            }
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
                if (guide.Kind == KindHeightScale)
                {
                    HeightScale(canvas, guide, thin, mark, scale);
                    continue;
                }
                foreach (var angle in guide.Angles) Ray(canvas, guide.X, guide.Y, angle, reach, thin);
                if (guide.Label is { Length: > 0 } name)
                {
                    // Just above the anchor, so "Horizon" sits on its own line
                    // rather than being cut by it.
                    Label(canvas, name, guide.X + 6f / scale, guide.Y - 6f / scale, scale);
                }
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

    /// <summary>
    /// A character height chart: a post standing on its anchor, a rung per
    /// head, and a count so it says what it is.
    /// </summary>
    /// <remarks>
    /// The rungs are warm and finite where guide rays are cool and infinite,
    /// because the whole point of a height scale is its <i>extent</i> — six
    /// heads is a statement about where the top is, and rays with no ends
    /// cannot make it. Snapping still reaches the full canvas width; what is
    /// drawn is the chart, not the pull.
    /// </remarks>
    private static void HeightScale(
        SKCanvas canvas, Line guide, SKPaint thin, SKPaint mark, float scale)
    {
        var divisions = Math.Max(1, guide.Divisions);
        var unit = Math.Max(1e-3f, guide.Spacing);
        var top = guide.Y - unit * divisions;

        canvas.DrawLine(guide.X, guide.Y, guide.X, top, mark);
        var arm = 10f / scale;
        for (var i = 0; i <= divisions; i++)
        {
            var y = guide.Y - unit * i;
            // The ground and the top reach further than the rungs between, so
            // the ends read as ends.
            var reach = i == 0 || i == divisions ? arm * 1.8f : arm;
            canvas.DrawLine(guide.X - reach, y, guide.X + reach, y, thin);
        }

        var text = guide.Label is { Length: > 0 } name ? name : $"{divisions} heads";
        Label(canvas, text, guide.X + arm * 2.2f, top + 4f / scale, scale);
    }

    /// <summary>
    /// A guide's name, drawn at a fixed size on screen like every other line
    /// of the rig — chrome, so it never scales with the zoom.
    /// </summary>
    private static void Label(SKCanvas canvas, string text, float x, float y, float scale)
    {
        using var font = new SKFont(SKTypeface.Default, 12f / scale);
        using var paint = new SKPaint
        {
            Color = new SKColor(240, 120, 80, 220),
            IsAntialias = true,
        };
        canvas.DrawText(text, x, y, font, paint);
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
