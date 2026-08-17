using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// The rectangles of reference on the canvas: a sheet's grid cells while it is
/// being sliced, and the box round a projected reference the Arrow has hold of.
/// </summary>
/// <remarks>
/// <para>
/// <b>One painter for both</b> (Q108), because they are one thing to look at: a
/// rectangle of reference, amber when it is the one you have, with white grips
/// at the corners that say it can be resized from there. An artist who has
/// learned the grid boxes has already learned the selection box.
/// </para>
/// <para>
/// <b>A collaborator rather than a method on the renderer</b>, extracted out of
/// <c>CanvasControl</c> for the reason its ratchet gives: new work goes beside
/// that file, not into it. It follows <c>GuidePainter</c> and
/// <c>ArmatureOverlayPainter</c>, which are the same shape for the same reason.
/// </para>
/// </remarks>
public static class ReferenceBoxPainter
{
    /// <summary>
    /// The reference grid, while it is being edited.
    /// </summary>
    /// <remarks>
    /// <para>
    /// View-only chrome, exactly like the camera frame and the transform
    /// gizmo: it never reaches a pixel of the document. Absent unless the
    /// mode is on, so an artist who never touches a reference sees none of
    /// it.
    /// </para>
    /// <para>
    /// Every line is divided by the zoom so the boxes stay one pixel wide
    /// on screen. A gizmo that scaled with the view would be a hairline at
    /// 25% and a slab at 800%, which is the opposite of what chrome is for.
    /// </para>
    /// </remarks>
    public static void Paint(
        SKCanvas canvas,
        IReadOnlyList<CanvasControl.ReferenceBox>? referenceBoxes,
        SKRect? newBox,
        CanvasControl.ReferenceBox? sheetBox,
        float viewScale)
    {
        if (referenceBoxes is not { Count: > 0 } && newBox is null && sheetBox is null) return;
        var scale = Math.Max(0.01f, viewScale);
        var thin = 1f / scale;
        var handle = 4f / scale;

        using var edge = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = thin,
            Color = new SKColor(90, 170, 255, 200),
            IsAntialias = true,
        };
        using var chosen = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = thin * 2,
            Color = new SKColor(255, 190, 60, 235),
            IsAntialias = true,
        };
        using var pivot = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = thin * 1.5f,
            Color = new SKColor(255, 90, 90, 235),
            IsAntialias = true,
        };
        using var grip = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = new SKColor(255, 255, 255, 220),
            IsAntialias = true,
        };

        foreach (var box in Boxes(referenceBoxes, sheetBox))
        {
            var rect = SKRect.Create(box.X, box.Y, box.W, box.H);
            canvas.DrawRect(rect, box.Selected ? chosen : edge);
            if (box.Selected)
            {
                // Corners only. Eight handles on a box this small is a row
                // of overlapping targets, and the corners are the two
                // gestures — origin and extent — that actually differ.
                foreach (var corner in (SKPoint[])
                    [
                        new(rect.Left, rect.Top), new(rect.Right, rect.Top),
                        new(rect.Left, rect.Bottom), new(rect.Right, rect.Bottom),
                    ])
                {
                    canvas.DrawRect(
                        SKRect.Create(corner.X - handle, corner.Y - handle, handle * 2, handle * 2),
                        grip);
                }
            }
            // The registration mark, as a cross rather than a dot: a dot
            // has no centre you can see once it is over a drawing. A whole
            // sheet has no mark of its own — registration belongs to the cell
            // — and says so with a pivot that is not a number.
            if (float.IsNaN(box.PivotX) || float.IsNaN(box.PivotY)) continue;
            var arm = 6f / scale;
            canvas.DrawLine(box.PivotX - arm, box.PivotY, box.PivotX + arm, box.PivotY, pivot);
            canvas.DrawLine(box.PivotX, box.PivotY - arm, box.PivotX, box.PivotY + arm, pivot);
        }

        if (newBox is { } drawing) canvas.DrawRect(drawing, chosen);
    }
    /// <summary>
    /// The grid's boxes and the sheet selection, as one sequence to draw.
    /// </summary>
    /// <remarks>
    /// The selection comes last so it draws over the grid in the one case where
    /// both are up. They cannot both be interactive — grid mode owns the press
    /// while it is on — but a box hidden under another box would still be a lie
    /// about what is selected.
    /// </remarks>
    private static IEnumerable<CanvasControl.ReferenceBox> Boxes(
        IReadOnlyList<CanvasControl.ReferenceBox>? grid, CanvasControl.ReferenceBox? sheet)
    {
        foreach (var box in grid ?? []) yield return box;
        if (sheet is { } selection) yield return selection;
    }
}
