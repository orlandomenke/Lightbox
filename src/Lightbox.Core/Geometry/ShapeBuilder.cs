using Lightbox.Core.Documents;

namespace Lightbox.Core.Geometry;

public enum ShapeKind
{
    Line,
    Rectangle,
    Ellipse,
    Polygon,
}

/// <summary>
/// Shapes, as strokes.
/// </summary>
/// <remarks>
/// <para>
/// A rectangle here is not a rectangle primitive — it is a polyline round a
/// rectangle, handed to <c>BrushEngine.StampStroke</c> like anything else.
/// That is invariant 1 doing its job: there is one pixel path, so a rectangle
/// drawn with a watercolour brush is a watercolour rectangle, it erases and
/// re-renders like every other mark, and the inbetweener can interpolate it
/// without knowing shapes exist.
/// </para>
/// <para>
/// The cost is that a shape cannot be re-edited as a shape after it is drawn.
/// That is the right trade for a frame-by-frame tool: two hundred drawings of
/// re-editable vector rectangles is a different application, and the one thing
/// this app cannot afford is a second kind of mark that behaves differently
/// under the brush.
/// </para>
/// <para>
/// Deterministic — the same corners always give the same points, to the bit.
/// A shape that re-tessellated differently on reload would be a different
/// mark, which is invariant 2 seen from the geometry side.
/// </para>
/// </remarks>
public static class ShapeBuilder
{
    /// <summary>Straight edges are two points; nothing is gained by more.</summary>
    private const int MinEllipseSegments = 24;

    private const int MaxEllipseSegments = 512;

    /// <summary>
    /// The outline of a shape spanning two corners.
    /// </summary>
    /// <param name="fromCentre">Treat the first corner as the middle instead.</param>
    /// <param name="regular">Square it up — a circle rather than an ellipse.</param>
    /// <param name="sides">Corners, for <see cref="ShapeKind.Polygon"/>.</param>
    /// <remarks>
    /// Closed shapes repeat their first point at the end. A stroke is stamped
    /// along its points, so without the repeat the last corner has a notch in
    /// it — visible on anything but the hardest round tip, and exactly the
    /// kind of detail that only shows up on the drawing rather than in a test
    /// of the point list.
    /// </remarks>
    public static List<StrokePoint> Outline(
        ShapeKind kind,
        double x0, double y0, double x1, double y1,
        bool fromCentre = false, bool regular = false, int sides = 5)
    {
        // A line is the one shape the centre modifier means nothing for: it has
        // two ends and you dragged between them — and the one where "regular"
        // cannot mean a square. It means the angle snaps, which is what Shift
        // does to a line everywhere else.
        if (kind == ShapeKind.Line)
        {
            return regular
                ? [At(x0, y0), SnappedEnd(x0, y0, x1, y1)]
                : [At(x0, y0), At(x1, y1)];
        }

        if (regular)
        {
            var reach = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
            x1 = x0 + Math.Sign(x1 - x0 == 0 ? 1 : x1 - x0) * reach;
            y1 = y0 + Math.Sign(y1 - y0 == 0 ? 1 : y1 - y0) * reach;
        }

        var (left, top, right, bottom) = fromCentre
            ? (x0 - (x1 - x0), y0 - (y1 - y0), x1, y1)
            : (Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));
        if (fromCentre)
        {
            (left, right) = (Math.Min(left, right), Math.Max(left, right));
            (top, bottom) = (Math.Min(top, bottom), Math.Max(top, bottom));
        }

        return kind switch
        {
            ShapeKind.Rectangle => Closed(
            [
                (left, top), (right, top), (right, bottom), (left, bottom),
            ]),
            ShapeKind.Ellipse => Ellipse(left, top, right, bottom),
            _ => Polygon(left, top, right, bottom, sides),
        };
    }

    /// <summary>How far apart a constrained line's angles are, in degrees.</summary>
    /// <remarks>
    /// The eight compass directions. Finer would make Shift a suggestion
    /// rather than a constraint, and the two things Shift is actually for on a
    /// line — level, and true diagonal — are both in this set.
    /// </remarks>
    public const double LineSnapDegrees = 45;

    /// <summary>
    /// The far end of a line, rotated onto the nearest snapped angle.
    /// </summary>
    /// <remarks>
    /// The arithmetic is <see cref="Snapper.OnNearestAngle"/>'s (B218); the
    /// forty-five is this tool's, and the constant above says why.
    /// </remarks>
    private static StrokePoint SnappedEnd(double x0, double y0, double x1, double y1)
    {
        var (x, y) = Snapper.OnNearestAngle(x0, y0, x1, y1, LineSnapDegrees);
        return At(x, y);
    }

    private static List<StrokePoint> Ellipse(double left, double top, double right, double bottom)
    {
        var rx = (right - left) / 2;
        var ry = (bottom - top) / 2;
        var cx = left + rx;
        var cy = top + ry;

        // Segments from the size, so a small circle is not over-tessellated and
        // a large one is not a polygon. Ramanujan's perimeter is overkill for
        // choosing a count; the cheap approximation is well inside the error
        // that would change the count.
        var perimeter = Math.PI * (3 * (rx + ry) - Math.Sqrt((3 * rx + ry) * (rx + 3 * ry)));
        var segments = Math.Clamp(
            (int)Math.Ceiling(perimeter / 3), MinEllipseSegments, MaxEllipseSegments);
        // Rounded up to a multiple of four so the four extremes — 0, 90, 180,
        // 270 degrees — are actually sampled. Otherwise the drawn ellipse sits
        // a fraction inside its own box, which is invisible until somebody
        // fits a circle into a grid cell and finds it does not touch.
        segments = (segments + 3) / 4 * 4;

        var points = new List<StrokePoint>(segments + 1);
        for (var i = 0; i <= segments; i++)
        {
            // The last point is computed from i == segments rather than copied
            // from the first, so it closes on the same value by construction.
            var t = i % segments * (2 * Math.PI / segments);
            points.Add(At(cx + rx * Math.Cos(t), cy + ry * Math.Sin(t)));
        }
        return points;
    }

    private static List<StrokePoint> Polygon(
        double left, double top, double right, double bottom, int sides)
    {
        var n = Math.Clamp(sides, 3, 64);
        var rx = (right - left) / 2;
        var ry = (bottom - top) / 2;
        var cx = left + rx;
        var cy = top + ry;

        var corners = new List<(double, double)>(n);
        for (var i = 0; i < n; i++)
        {
            // Starting at the top, which is where every polygon tool puts the
            // first corner and what makes a pentagon look like a pentagon
            // rather than a rotated one.
            var t = -Math.PI / 2 + i * (2 * Math.PI / n);
            corners.Add((cx + rx * Math.Cos(t), cy + ry * Math.Sin(t)));
        }
        return Closed(corners);
    }

    private static List<StrokePoint> Closed(IReadOnlyList<(double X, double Y)> corners)
    {
        var points = new List<StrokePoint>(corners.Count + 1);
        foreach (var (x, y) in corners) points.Add(At(x, y));
        points.Add(At(corners[0].X, corners[0].Y));
        return points;
    }

    /// <summary>
    /// Full pressure at every point.
    /// </summary>
    /// <remarks>
    /// A shape is not a gesture — there is no hand travelling along the edge to
    /// have a pressure. Tapering it from the drag would put the thin part
    /// wherever the artist happened to release, which is nowhere meaningful.
    /// </remarks>
    private static StrokePoint At(double x, double y) => new(x, y, 1);
}
