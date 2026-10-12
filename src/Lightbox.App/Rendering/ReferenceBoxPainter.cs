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
        // The canvas's one overlay style (Q252). A box that is not selected is
        // the thin outline — a sheet is a grid of these and they must not
        // crowd the drawing they frame; the selected one is the styled line
        // with a grip at each corner; the registration mark is a reference
        // mark, so it is amber. They were a blue, an amber, a red and a white.
        using var pens = new OverlayStyle.Pens(scale);
        using var plain = new SKPath();
        using var selected = new SKPath();
        using var marks = new SKPath();
        var arm = 6f / scale;

        foreach (var box in Boxes(referenceBoxes, sheetBox))
        {
            (box.Selected ? selected : plain).AddRect(SKRect.Create(box.X, box.Y, box.W, box.H));
            // The registration mark, as a cross rather than a dot: a dot
            // has no centre you can see once it is over a drawing. A whole
            // sheet has no mark of its own — registration belongs to the cell
            // — and says so with a pivot that is not a number.
            if (float.IsNaN(box.PivotX) || float.IsNaN(box.PivotY)) continue;
            marks.MoveTo(box.PivotX - arm, box.PivotY);
            marks.LineTo(box.PivotX + arm, box.PivotY);
            marks.MoveTo(box.PivotX, box.PivotY - arm);
            marks.LineTo(box.PivotX, box.PivotY + arm);
        }
        if (newBox is { } drawing) selected.AddRect(drawing);

        pens.Outline(canvas, plain);
        pens.Line(canvas, selected);
        pens.Line(canvas, marks, role: OverlayStyle.Role.Reference);

        foreach (var box in Boxes(referenceBoxes, sheetBox))
        {
            if (!box.Selected) continue;
            // Corners only. Eight handles on a box this small is a row of
            // overlapping targets, and the corners are the two gestures —
            // origin and extent — that actually differ.
            pens.Grip(canvas, box.X, box.Y, round: false, selected: false);
            pens.Grip(canvas, box.X + box.W, box.Y, round: false, selected: false);
            pens.Grip(canvas, box.X, box.Y + box.H, round: false, selected: false);
            pens.Grip(canvas, box.X + box.W, box.Y + box.H, round: false, selected: false);
        }
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
