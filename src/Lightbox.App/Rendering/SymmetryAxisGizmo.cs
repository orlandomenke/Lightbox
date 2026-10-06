using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>Which part of the symmetry gizmo a press landed on.</summary>
public enum SymmetryHandle
{
    None,

    /// <summary>The square at the axis centre: dragging it moves the whole arrangement.</summary>
    Centre,

    /// <summary>The circle pushed out along the axis: dragging it turns the arrangement.</summary>
    Rotate,
}

/// <summary>
/// The geometry of the on-canvas symmetry gizmo — where its lines run, where
/// its two handles sit, and what a press hits — with no canvas in sight.
/// </summary>
/// <remarks>
/// <para>
/// Pure functions of a <see cref="SymmetryAxis"/> and a view scale, so the
/// arrangement an artist sees can be tested without a window, the way
/// <c>CameraGizmoGeometryTests</c> tests the camera frame. <c>CanvasControl</c>
/// draws what this returns and asks it what a press hit; it decides nothing
/// of its own.
/// </para>
/// <para>
/// <b>View-only chrome</b> (invariant 5): the gizmo shows where the copies of
/// the next mark will land and never reaches a pixel. What it draws follows
/// <see cref="SymmetryAxis.Placements"/> exactly — a mirror line per
/// reflection, a spoke per rotation — because a gizmo that disagrees with the
/// engine about where the copies go is worse than no gizmo.
/// </para>
/// <para>
/// Sizes are in <em>screen</em> pixels and divided by the view scale at the
/// call site, so a handle stays the same size under the pointer however far the
/// document is zoomed; the camera gizmo makes the same choice for the same
/// reason.
/// </para>
/// </remarks>
public static class SymmetryAxisGizmo
{
    /// <summary>How far the rotate handle sits from the centre, on screen.</summary>
    public const float RotateOffsetPx = 44f;

    /// <summary>Half the side of the centre square, on screen.</summary>
    public const float CentreHalfPx = 5f;

    /// <summary>Radius of the rotate handle, on screen.</summary>
    public const float RotateRadiusPx = 5f;

    /// <summary>
    /// How close a press has to land to take a handle, on screen. Deliberately
    /// small, like the camera's: everywhere else on the canvas still paints.
    /// </summary>
    public const float HitRadiusPx = 9f;

    /// <summary>One line of the gizmo, in document coordinates.</summary>
    /// <param name="From">One end.</param>
    /// <param name="To">The other.</param>
    /// <param name="IsMirror">
    /// True for a reflection line, drawn solid; false for a rotation spoke,
    /// drawn dashed, which shows where a turned copy goes without claiming it
    /// is reflected across anything.
    /// </param>
    public readonly record struct GizmoLine(SKPoint From, SKPoint To, bool IsMirror);

    /// <summary>The axis centre as a point.</summary>
    public static SKPoint Centre(SymmetryAxis axis) => new((float)axis.CenterX, (float)axis.CenterY);

    /// <summary>Unit vector along a line at <paramref name="angleDeg"/>.</summary>
    /// <remarks>
    /// <c>(cos θ, sin θ)</c> in document space, y down — the same convention
    /// <c>SKMatrix.CreateRotationDegrees</c> uses, so a line drawn here at angle
    /// β is the line <c>reflect(β) = rotate(2β) ∘ flipY</c> reflects across in
    /// <see cref="SymmetryAxis.Placements"/>. 90 is vertical.
    /// </remarks>
    public static SKPoint Direction(double angleDeg)
    {
        var rad = angleDeg * Math.PI / 180.0;
        return new SKPoint((float)Math.Cos(rad), (float)Math.Sin(rad));
    }

    /// <summary>Where the rotate handle sits: <paramref name="offsetDoc"/> along the axis from the centre.</summary>
    public static SKPoint RotateHandle(SymmetryAxis axis, float offsetDoc)
    {
        var c = Centre(axis);
        var d = Direction(axis.AngleDeg);
        return new SKPoint(c.X + d.X * offsetDoc, c.Y + d.Y * offsetDoc);
    }

    /// <summary>
    /// The lines the gizmo draws for this axis, long enough to cross
    /// <paramref name="document"/> wherever the centre is.
    /// </summary>
    /// <remarks>
    /// Reflections are across lines at <c>AngleDeg + k·180/N</c> — the dihedral
    /// arrangement <see cref="SymmetryAxis.Placements"/> produces, so with
    /// <c>Order 1</c> and <c>Mirror</c> this is the one line character design
    /// reaches for. Rotations are spokes at <c>k·360/N</c> from the axis angle,
    /// rays rather than lines because a turned copy lands on one side only.
    /// An identity axis still returns its one dashed line, so an artist who has
    /// turned mirror off at order one sees where the axis is rather than nothing.
    /// </remarks>
    public static IReadOnlyList<GizmoLine> Lines(SymmetryAxis axis, SKRect document)
    {
        var c = Centre(axis);
        var n = axis.EffectiveOrder;
        // Far enough to leave the page from anywhere the centre can be: the
        // page diagonal plus the centre's distance from the page centre.
        var dx = c.X - document.MidX;
        var dy = c.Y - document.MidY;
        var reach = MathF.Sqrt(document.Width * document.Width + document.Height * document.Height)
                    + MathF.Sqrt(dx * dx + dy * dy) + 1f;

        var lines = new List<GizmoLine>();
        if (axis.Mirror)
        {
            for (var k = 0; k < n; k++)
            {
                var d = Direction(axis.AngleDeg + k * 180.0 / n);
                lines.Add(new GizmoLine(
                    new SKPoint(c.X - d.X * reach, c.Y - d.Y * reach),
                    new SKPoint(c.X + d.X * reach, c.Y + d.Y * reach),
                    IsMirror: true));
            }
        }

        if (n > 1)
        {
            for (var k = 0; k < n; k++)
            {
                var d = Direction(axis.AngleDeg + k * 360.0 / n);
                lines.Add(new GizmoLine(c, new SKPoint(c.X + d.X * reach, c.Y + d.Y * reach), IsMirror: false));
            }
        }
        else if (!axis.Mirror)
        {
            var d = Direction(axis.AngleDeg);
            lines.Add(new GizmoLine(
                new SKPoint(c.X - d.X * reach, c.Y - d.Y * reach),
                new SKPoint(c.X + d.X * reach, c.Y + d.Y * reach),
                IsMirror: false));
        }

        return lines;
    }

    /// <summary>Which handle a press at document point (<paramref name="x"/>, <paramref name="y"/>) takes.</summary>
    /// <param name="scale">The view scale, so the hit zones stay screen-sized.</param>
    /// <remarks>
    /// The rotate handle is tested first: it is the smaller target and it sits
    /// near the centre square at low zoom, so the square would otherwise shadow
    /// it. The camera gizmo orders its handles the same way.
    /// </remarks>
    public static SymmetryHandle HandleAt(SymmetryAxis axis, double x, double y, float scale)
    {
        var s = Math.Max(0.01f, scale);
        var r = HitRadiusPx / s;
        bool Near(SKPoint p) => Math.Abs(p.X - x) <= r && Math.Abs(p.Y - y) <= r;

        if (Near(RotateHandle(axis, RotateOffsetPx / s))) return SymmetryHandle.Rotate;
        if (Near(Centre(axis))) return SymmetryHandle.Centre;
        return SymmetryHandle.None;
    }

    /// <summary>
    /// The axis angle that puts the rotate handle under document point
    /// (<paramref name="x"/>, <paramref name="y"/>) — what a rotate drag sets.
    /// </summary>
    /// <remarks>
    /// Absolute rather than incremental: the handle follows the pointer, so a
    /// drag cannot accumulate rounding and the handle never drifts away from
    /// the hand. Snaps to <paramref name="snapDeg"/> when it is positive, for a
    /// Shift-drag to land on 45s and 90s exactly.
    /// </remarks>
    public static double AngleTowards(SymmetryAxis axis, double x, double y, double snapDeg = 0)
    {
        var deg = Math.Atan2(y - axis.CenterY, x - axis.CenterX) * 180.0 / Math.PI;
        if (snapDeg > 0) deg = Math.Round(deg / snapDeg) * snapDeg;
        // One turn, so the record never grows a 720 that means the same thing.
        deg %= 360.0;
        if (deg < 0) deg += 360.0;
        return deg;
    }
}
