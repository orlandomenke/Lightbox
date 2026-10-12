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
    private const int KindIsometric = 2;
    private const int KindVanishingPoint = 3;
    private const int KindHeightScale = 4;

    /// <summary>
    /// How prominently one guide is drawn.
    /// </summary>
    /// <remarks>
    /// Three states above plain, and they answer three different questions the
    /// artist is asking at three different moments: <i>can I grab anything
    /// here</i>, <i>would this one come up</i>, and <i>which one am I
    /// changing</i>. Ambient exists because most guides are grabbed at their
    /// anchor — a grid's origin and a vanishing point are single invisible
    /// points on a canvas full of lines — so without it, picking one up is
    /// hunting. It is on only while the Move tool is in hand, so a rig you are
    /// drawing against never lights up at you.
    /// </remarks>
    public enum Emphasis
    {
        /// <summary>Chrome to draw against: the faint rig, unchanged.</summary>
        None,

        /// <summary>The Move tool is in hand, so every guide here can be picked up.</summary>
        Grabbable,

        /// <summary>The pointer is on this one; a press would take it.</summary>
        Hover,

        /// <summary>The one the options are pointed at.</summary>
        Selected,
    }

    /// <summary>A guide flattened for the render thread. See <c>CanvasControl.GuideLine</c>.</summary>
    /// <remarks>
    /// <paramref name="Label"/> is the guide's name when it has one — an
    /// eye-line that does not say "eye line" is just a line, and a shared rig
    /// has to read at a glance. <paramref name="Divisions"/> is a height
    /// scale's head count and zero on everything else.
    /// </remarks>
    public readonly record struct Line(
        int Kind, float X, float Y, float Spacing, IReadOnlyList<double> Angles,
        string? Label = null, int Divisions = 0, Emphasis Emphasis = Emphasis.None);

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
        SKColorFilter? artworkFilter = null,
        bool tiled = false)
    {
        checkerboard?.Invoke(canvas);
        if (artwork is not null)
        {
            // The filter applies to the artwork alone — soloing a channel must
            // not grey out the guides an artist is still aiming with.
            using var paint = new SKPaint { IsAntialias = true, ColorFilter = artworkFilter };

            // When compositing is viewport-culled, the image is smaller than the document.
            // Draw it at the viewport position with its actual size.
            var dest = docViewport is { } vp && vp.Width > 0 && vp.Height > 0
                ? new SKRect(vp.Left, vp.Top, vp.Left + vp.Width, vp.Top + vp.Height)
                : new SKRect(0, 0, docW, docH);
            var sampling = new SKSamplingOptions(SKFilterMode.Linear);

            // Seamless tiles (Q192): the page's eight neighbours, the same
            // image moved by a page width or height, so the seam is judged
            // where it will be seen. Dimmed so the page still reads as the
            // page, and drawn first so the page sits on top. View-only — it
            // never reaches a pixel of the document.
            if (tiled)
            {
                using var dim = new SKPaint { IsAntialias = true, ColorFilter = artworkFilter, Color = SKColors.White.WithAlpha(170) };
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var at = dest;
                        at.Offset(dx * docW, dy * docH);
                        canvas.DrawImage(artwork, at, sampling, dim);
                    }
                }
            }

            canvas.DrawImage(artwork, dest, sampling, paint);
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
        // Before anything is made: this is called for every frame the canvas
        // draws, and a document with no rig must pay nothing for one (the
        // leak-hunter — the pens below were eleven paints an idle frame).
        if (guides is not { Count: > 0 } && draft is null) return;

        scale = Math.Max(0.01f, scale);
        var reach = (docW + docH) * 2f;

        // Q252. A guide at rest is deliberately not on the canvas's overlay
        // style: it is something to draw against, and a rig of thirty styled
        // lines would read as thirty selections. One faint pass — and still
        // one with the Move tool merely in hand, a touch stronger. The style
        // arrives for the one guide the pointer is on and the one that is
        // selected: its cased accent line, and an anchor that is its grip.
        using var pens = new OverlayStyle.Pens(scale);
        using var thin = Stroke();
        using var mark = Stroke();
        using var casing = Stroke();
        casing.Color = OverlayStyle.Casing;

        if (guides is { Count: > 0 } lines)
        {
            foreach (var guide in lines)
            {
                var emphasis = guide.Emphasis;
                // The lattice itself brightens only when it is the one under
                // the pointer or under the options. A whole-canvas grid lit up
                // merely because the Move tool is in hand is a wash over the
                // drawing, which is the opposite of what the ambient hint is
                // for — its anchor is enough.
                var forLines = guide.Kind == KindGrid && emphasis == Emphasis.Grabbable
                    ? Emphasis.None
                    : emphasis;
                var styled = Tint(thin, mark, casing, forLines, scale);

                // Casing first and for the whole guide, so no line's casing is
                // drawn over another line's accent.
                if (styled) Lines(canvas, guide, casing, casing, scale, reach, docW, docH);
                Lines(canvas, guide, thin, mark, scale, reach, docW, docH);

                if (guide.Kind is not (KindGrid or KindHeightScale) && guide.Label is { Length: > 0 } name)
                {
                    // Just above the anchor, so "Horizon" sits on its own line
                    // rather than being cut by it.
                    Label(canvas, name, guide.X + 6f / scale, guide.Y - 6f / scale, scale, mark.Color);
                }

                // The grab point, shown only while something is reaching for
                // it. A grid and an isometric rig are picked up at their
                // anchor — never on their lines, or a grid would cover the
                // canvas in grab targets and nothing else could be picked up —
                // and that anchor is an invisible point, on a grid usually the
                // canvas corner. Without a mark, "you can drag this" is a
                // sentence in the manual and a hunt on the screen. Last, so the
                // grip sits over the lines that cross it.
                if (emphasis != Emphasis.None && guide.Kind is KindGrid or KindIsometric)
                {
                    pens.Grip(
                        canvas, guide.X, guide.Y, round: false,
                        selected: emphasis == Emphasis.Selected, hover: emphasis == Emphasis.Hover,
                        role: OverlayStyle.Role.Reference);
                }
            }
        }

        if (draft is not { } pulled) return;
        // The styled line, because it is the thing you are looking at: the
        // whole gesture is aiming a line, and a draft drawn as faintly as the
        // rig behind it cannot be aimed.
        Tint(thin, mark, casing, Emphasis.Selected, scale);
        foreach (var angle in pulled.Angles) Ray(canvas, pulled.X, pulled.Y, angle, reach, casing);
        foreach (var angle in pulled.Angles) Ray(canvas, pulled.X, pulled.Y, angle, reach, thin);
    }

    private static SKPaint Stroke() => new() { Style = SKPaintStyle.Stroke, IsAntialias = true };

    /// <summary>One guide's lines and its own marks, in the paints given.</summary>
    private static void Lines(
        SKCanvas canvas, Line guide, SKPaint thin, SKPaint mark, float scale,
        float reach, float docW, float docH)
    {
        if (guide.Kind == KindGrid)
        {
            Grid(canvas, guide, thin, scale, docW, docH);
            return;
        }
        if (guide.Kind == KindHeightScale)
        {
            HeightScale(canvas, guide, thin, mark, scale, label: !ReferenceEquals(thin, mark));
            return;
        }
        foreach (var angle in guide.Angles) Ray(canvas, guide.X, guide.Y, angle, reach, thin);
        if (guide.Kind != KindVanishingPoint) return;
        // A vanishing point is a place as well as a set of directions, and
        // without a mark on it you cannot tell which of the rays meet where.
        var arm = 7f / scale;
        canvas.DrawLine(guide.X - arm, guide.Y, guide.X + arm, guide.Y, mark);
        canvas.DrawLine(guide.X, guide.Y - arm, guide.X, guide.Y + arm, mark);
    }

    /// <summary>
    /// Set the paints for one emphasis. Returns whether it is one the overlay
    /// style draws — cased — rather than a quiet pass.
    /// </summary>
    private static bool Tint(SKPaint thin, SKPaint mark, SKPaint casing, Emphasis emphasis, float scale)
    {
        var styled = emphasis is Emphasis.Hover or Emphasis.Selected;
        var (line, point, width) = emphasis switch
        {
            // A touch stronger than at rest: "anything here can be picked up".
            Emphasis.Grabbable => (OverlayStyle.QuietLine.WithAlpha(170), OverlayStyle.Reference, 1f),
            Emphasis.Hover => (
                OverlayStyle.Accent, OverlayStyle.Reference, OverlayStyle.LineWidth + OverlayStyle.HoverLineGain),
            Emphasis.Selected => (OverlayStyle.Accent, OverlayStyle.Reference, OverlayStyle.LineWidth),
            _ => (OverlayStyle.QuietLine, OverlayStyle.QuietMark, 1f),
        };
        thin.Color = line;
        thin.StrokeWidth = width / scale;
        mark.Color = point;
        mark.StrokeWidth = (width + 0.5f) / scale;
        casing.StrokeWidth = (width + (OverlayStyle.CasingWidth - OverlayStyle.LineWidth)) / scale;
        return styled;
    }

    private static void HeightScale(
        SKCanvas canvas, Line guide, SKPaint thin, SKPaint mark, float scale, bool label)
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

        // Not on the casing pass, which would write the words twice.
        if (!label) return;
        var text = guide.Label is { Length: > 0 } name ? name : $"{divisions} heads";
        Label(canvas, text, guide.X + arm * 2.2f, top + 4f / scale, scale, mark.Color);
    }

    /// <summary>
    /// A guide's name, drawn at a fixed size on screen like every other line
    /// of the rig — chrome, so it never scales with the zoom.
    /// </summary>
    /// <param name="colour">
    /// The guide's own mark colour, so a label is as quiet as the guide it
    /// names and comes up with it.
    /// </param>
    private static void Label(SKCanvas canvas, string text, float x, float y, float scale, SKColor colour)
    {
        using var font = new SKFont(SKTypeface.Default, 12f / scale);
        using var paint = new SKPaint
        {
            Color = colour,
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
