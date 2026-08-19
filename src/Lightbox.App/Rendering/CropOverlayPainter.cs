using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// The crop frame: the paper outside it dimmed, the frame itself, its eight
/// handles, and the thirds an artist frames against.
/// </summary>
/// <remarks>
/// <para>
/// <b>A collaborator rather than a method on the renderer</b>, following
/// <c>ReferenceBoxPainter</c>, <c>GuidePainter</c> and
/// <c>ArmatureOverlayPainter</c> for the reason <c>CanvasControl</c>'s ratchet
/// gives: new work goes beside that file, not into it.
/// </para>
/// <para>
/// <b>The dim is the feature.</b> A frame drawn as an outline alone asks the
/// artist to imagine the crop; darkening what falls outside it shows them. That
/// is the whole reason this is a tool rather than a third menu item — framing is
/// a judgement made by eye, and the eye needs to see the result to make it.
/// </para>
/// <para>
/// View-only chrome, like the camera frame and the transform gizmo: it never
/// reaches a pixel of the document, and applying it goes through
/// <c>CanvasResize.CropTo</c> like every other crop (Q126).
/// </para>
/// </remarks>
public static class CropOverlayPainter
{
    /// <summary>
    /// Draw the frame over the page.
    /// </summary>
    /// <param name="frame">The crop rectangle, in document coordinates.</param>
    /// <param name="paper">The whole page, so the dim knows what to cover.</param>
    /// <param name="scale">
    /// The view scale. Every width and radius below is divided by it so the
    /// chrome stays the same size on screen — a frame that scaled with the view
    /// would be a hairline at 25% and a slab at 800%, which is the opposite of
    /// what chrome is for.
    /// </param>
    public static void Paint(SKCanvas canvas, SKRect? frame, SKRect paper, float scale)
    {
        if (frame is not { } rect) return;
        var s = Math.Max(0.01f, scale);

        DrawDimOutside(canvas, rect, paper);

        var line = new SKColor(0xf0, 0xf0, 0xf0, 235);
        using var edge = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.4f / s,
            Color = line,
        };
        canvas.DrawRect(rect, edge);

        DrawThirds(canvas, rect, s);
        DrawHandles(canvas, rect, s, line);
    }

    /// <summary>
    /// Darken the paper the crop would throw away.
    /// </summary>
    /// <remarks>
    /// Four rectangles rather than an even-odd path, because the four are
    /// trivially correct and a path with a reversed inner contour is the kind
    /// of thing that renders inverted on one backend and right on another. They
    /// are drawn as a band each side of the frame, so nothing is painted twice
    /// — overlapping them would double the alpha at the corners and leave four
    /// darker squares.
    /// </remarks>
    private static void DrawDimOutside(SKCanvas canvas, SKRect rect, SKRect paper)
    {
        using var dim = new SKPaint { Color = new SKColor(0, 0, 0, 130), IsAntialias = false };
        // Full-width bands above and below, then only the remaining strips at
        // the sides — which is what keeps the corners single-covered.
        canvas.DrawRect(new SKRect(paper.Left, paper.Top, paper.Right, rect.Top), dim);
        canvas.DrawRect(new SKRect(paper.Left, rect.Bottom, paper.Right, paper.Bottom), dim);
        canvas.DrawRect(new SKRect(paper.Left, rect.Top, rect.Left, rect.Bottom), dim);
        canvas.DrawRect(new SKRect(rect.Right, rect.Top, paper.Right, rect.Bottom), dim);
    }

    /// <summary>
    /// The rule of thirds, faint, inside the frame.
    /// </summary>
    /// <remarks>
    /// Faint on purpose: it is a thing to glance at while judging a frame, not
    /// a grid to align to. Anything stronger competes with the artwork it is
    /// drawn over, which defeats the point of looking at the artwork.
    /// </remarks>
    private static void DrawThirds(SKCanvas canvas, SKRect rect, float scale)
    {
        using var thin = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 0.8f / scale,
            Color = new SKColor(0xff, 0xff, 0xff, 70),
        };
        for (var i = 1; i <= 2; i++)
        {
            var x = rect.Left + rect.Width * i / 3f;
            var y = rect.Top + rect.Height * i / 3f;
            canvas.DrawLine(x, rect.Top, x, rect.Bottom, thin);
            canvas.DrawLine(rect.Left, y, rect.Right, y, thin);
        }
    }

    /// <summary>
    /// Eight grips: four corners and four edge midpoints, matching what
    /// <c>CropSession.HandleAt</c> will actually pick up.
    /// </summary>
    /// <remarks>
    /// Drawing a handle the session does not answer for, or leaving one out
    /// that it does, is the version of this that looks fine and feels broken —
    /// so the two lists are deliberately the same eight places.
    /// </remarks>
    private static void DrawHandles(SKCanvas canvas, SKRect rect, float scale, SKColor line)
    {
        using var fill = new SKPaint { IsAntialias = true, Color = line };
        using var rim = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f / scale,
            Color = new SKColor(20, 20, 20, 235),
        };

        var half = 4.5f / scale;
        var midX = (rect.Left + rect.Right) / 2f;
        var midY = (rect.Top + rect.Bottom) / 2f;
        Span<SKPoint> grips =
        [
            new(rect.Left, rect.Top), new(midX, rect.Top), new(rect.Right, rect.Top),
            new(rect.Left, midY), new(rect.Right, midY),
            new(rect.Left, rect.Bottom), new(midX, rect.Bottom), new(rect.Right, rect.Bottom),
        ];
        foreach (var g in grips)
        {
            var box = new SKRect(g.X - half, g.Y - half, g.X + half, g.Y + half);
            canvas.DrawRect(box, fill);
            canvas.DrawRect(box, rim);
        }
    }
}
