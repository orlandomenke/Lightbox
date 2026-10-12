using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// The transform box (Ctrl+T): the transformed quad, a grip at each corner
/// and, where an edge can be dragged, one in the middle of each edge, and the
/// pivot the drawing turns about.
/// </summary>
/// <remarks>
/// <para>
/// Out of <c>CanvasControl.DrawOp</c> for <see cref="GuidePainter"/>'s reason —
/// nothing in a draw operation can be tested — and onto
/// <see cref="OverlayStyle"/> (Q249, Q252). It had a cyan of its own, a dashed
/// uncased outline that vanished on cyan artwork, and a pivot in an amber of
/// its own.
/// </para>
/// <para>
/// <b>Nothing on a transform box is filled.</b> Filled means selected, and a
/// box has no selected handle — every grip is equally there to be taken. A
/// corner is square; an edge's grip is the same grip, round, because it
/// slides along one line where a corner moves freely — the style's rule for
/// every grip.
/// </para>
/// <para>
/// <b>The pivot is the one reference mark</b>: it is where the drawing turns,
/// not part of the drawing, so it is the style's amber and nothing else here
/// is.
/// </para>
/// </remarks>
public static class TransformGizmoPainter
{
    /// <summary>The pivot's ring, in screen pixels.</summary>
    public const float PivotRadius = 7f;

    /// <summary>How far the pivot's crosshair reaches past its ring, as a multiple of the radius.</summary>
    public const float PivotReach = 1.6f;

    /// <param name="pivot">
    /// Null when something else stands in the pivot's place — the warp bands
    /// and the cage draw their own handles there.
    /// </param>
    /// <param name="perspective">
    /// Corners only: a free or perspective transform has no edge to drag.
    /// </param>
    public static void Paint(
        SKCanvas canvas, SKPoint c0, SKPoint c1, SKPoint c2, SKPoint c3,
        SKPoint? pivot, bool perspective, float scale)
    {
        using var pens = new OverlayStyle.Pens(scale);
        Paint(canvas, pens, c0, c1, c2, c3, pivot, perspective, scale);
    }

    public static void Paint(
        SKCanvas canvas, OverlayStyle.Pens pens, SKPoint c0, SKPoint c1, SKPoint c2, SKPoint c3,
        SKPoint? pivot, bool perspective, float scale)
    {
        scale = Math.Max(0.01f, scale);
        using (var quad = new SKPath())
        {
            quad.MoveTo(c0);
            quad.LineTo(c1);
            quad.LineTo(c2);
            quad.LineTo(c3);
            quad.Close();
            pens.Line(canvas, quad);
        }

        ReadOnlySpan<SKPoint> corners = [c0, c1, c2, c3];
        if (!perspective)
        {
            for (var i = 0; i < 4; i++)
            {
                var a = corners[i];
                var b = corners[(i + 1) % 4];
                pens.Grip(canvas, (a.X + b.X) / 2, (a.Y + b.Y) / 2, round: true, selected: false);
            }
        }
        foreach (var c in corners) pens.Grip(canvas, c.X, c.Y, round: false, selected: false);

        if (pivot is { } p) PaintPivot(canvas, pens, p, scale);
    }

    /// <summary>The pivot alone: a ring and a crosshair, in the reference colour.</summary>
    public static void PaintPivot(SKCanvas canvas, OverlayStyle.Pens pens, SKPoint p, float scale)
    {
        pens.Cross(
            canvas, p.X, p.Y, OverlayStyle.Role.Reference,
            reach: PivotRadius * PivotReach / OverlayStyle.CrossArm);
        pens.Ring(canvas, p.X, p.Y, PivotRadius, OverlayStyle.Role.Reference);
    }
}
