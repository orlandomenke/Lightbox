using Lightbox.Core.Documents;

namespace Lightbox.Core.Geometry;

/// <summary>
/// <see cref="StrokePath"/> → <see cref="StrokePoint"/>s: the one direction in
/// which authored geometry becomes something the renderer can draw.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no path-stroking code path, and there must never be one.</b> The
/// whole reason a path is an optional extra field rather than a second kind of
/// stroke is that <c>BrushEngine</c> keeps exactly one input. Editing a node
/// re-flattens here and the engine stamps the new polyline exactly as it stamps
/// a drawn one — same dabs, same seeding, same everything.
/// </para>
/// <para>
/// <b>Deterministic, because it feeds pixels.</b> Invariant 2 forbids randomness
/// in rendering, and a flatten that produced a different point count for the same
/// nodes would re-roll every dab dynamic downstream of it — the marks would
/// change without the drawing changing. So the sample count is a pure function of
/// the control points, computed from a curvature bound rather than from anything
/// adaptive that could depend on evaluation order.
/// </para>
/// <para>
/// <b>Sparse on purpose.</b> This does not need to emit a point per pixel:
/// <c>IncrementalDensify</c> already lays the brush along the curve through the
/// points it is given, so over-sampling here would cost the record size and buy
/// nothing. The tolerance is a geometric error bound, not a spacing.
/// </para>
/// </remarks>
public static class PathFlattener
{
    /// <summary>
    /// How far, in document pixels, the emitted polyline may deviate from the
    /// true curve.
    /// </summary>
    /// <remarks>
    /// A quarter of a pixel is the usual choice and is below what any brush can
    /// show — the thinnest mark the engine makes is a pixel wide, and it is
    /// anti-aliased. Tightening it costs points in the record for a difference
    /// nothing renders.
    /// </remarks>
    public const double Tolerance = 0.25;

    /// <summary>
    /// A ceiling on samples per segment, so one absurd handle cannot produce a
    /// record nothing can open.
    /// </summary>
    /// <remarks>
    /// Handles are unconstrained — a node's control point can be dragged a
    /// thousand pixels away — and the sample count grows with the square root of
    /// that distance, so this bites rarely and bounds the damage when it does.
    /// The visible cost of hitting it is a very slightly faceted curve at a
    /// zoom nobody works at.
    /// </remarks>
    public const int MaxSamplesPerSegment = 96;

    /// <summary>Flatten a path into the points that render it.</summary>
    public static List<StrokePoint> Flatten(StrokePath path, double tolerance = Tolerance)
    {
        var nodes = path.Nodes;
        if (nodes.Count == 0) return [];
        if (nodes.Count == 1)
        {
            var only = nodes[0];
            return [new StrokePoint(only.X, only.Y, only.Pressure)];
        }

        var points = new List<StrokePoint>(nodes.Count * 4);
        var segments = path.Closed ? nodes.Count : nodes.Count - 1;

        for (var i = 0; i < segments; i++)
        {
            var a = nodes[i];
            var b = nodes[(i + 1) % nodes.Count];

            // The start of every segment, emitted once. The end is left to the
            // next segment's start, which is what keeps joins from doubling —
            // and a doubled point is not cosmetic downstream: it is a zero-length
            // step for the densifier and a repeated dab for the engine.
            points.Add(new StrokePoint(a.X, a.Y, a.Pressure));
            AppendInterior(points, a, b, tolerance);
        }

        // Every path needs its final point. An open one ends on its last node; a
        // closed one ends back on its first — and that repeat is not optional.
        // The renderer stamps dabs along this polyline and knows nothing about
        // Closed, so without the return point the closing segment is a fact in
        // the record that no pixel ever shows: a "closed" triangle rendered as
        // an open corner. (The straight-segment early-out above makes it worse,
        // not merely approximate — a straight closing segment emitted nothing at
        // all.)
        var final = path.Closed ? nodes[0] : nodes[^1];
        points.Add(new StrokePoint(final.X, final.Y, final.Pressure));

        return points;
    }

    /// <summary>
    /// Where the curve actually is at <paramref name="t"/> along one segment —
    /// the same evaluation the flatten uses, exposed for hit tests and overlays
    /// so nothing has to re-derive it and get it subtly different.
    /// </summary>
    public static (double X, double Y) PointOn(PathNode a, PathNode b, double t)
    {
        var (x1, y1) = (a.X + a.OutX, a.Y + a.OutY);
        var (x2, y2) = (b.X + b.InX, b.Y + b.InY);
        return (
            Cubic(a.X, x1, x2, b.X, t),
            Cubic(a.Y, y1, y2, b.Y, t));
    }

    private static void AppendInterior(
        List<StrokePoint> into, PathNode a, PathNode b, double tolerance)
    {
        var (x0, y0) = (a.X, a.Y);
        var (x1, y1) = (a.X + a.OutX, a.Y + a.OutY);
        var (x2, y2) = (b.X + b.InX, b.Y + b.InY);
        var (x3, y3) = (b.X, b.Y);

        // Two straight handles mean a straight line, and a straight line needs no
        // interior points at all. Worth the branch: a pen used as a polygon tool
        // places nothing but these, and sampling them would quadruple the record
        // for a shape whose corners are the whole content.
        if (!a.HasHandles && !b.HasHandles) return;

        var steps = StepsFor(x0, y0, x1, y1, x2, y2, x3, y3, tolerance);
        for (var s = 1; s < steps; s++)
        {
            var t = (double)s / steps;
            into.Add(new StrokePoint(
                Cubic(x0, x1, x2, x3, t),
                Cubic(y0, y1, y2, y3, t),
                a.Pressure + (b.Pressure - a.Pressure) * t));
        }
    }

    /// <summary>
    /// How many uniform steps hold the polyline within tolerance of the curve.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The standard bound: a cubic sampled at <c>n</c> uniform steps deviates by
    /// at most <c>3·d / (4·n²)</c>, where <c>d</c> is the larger of the two
    /// second differences of the control polygon. Inverting it gives
    /// <c>n = ceil(sqrt(3d / 4·tol))</c>.
    /// </para>
    /// <para>
    /// <b>Uniform rather than recursive-adaptive, and the reason is pressure.</b>
    /// De Casteljau subdivision is the textbook flatten and it loses the curve
    /// parameter as it goes, so interpolating pressure across it means either
    /// carrying <c>t</c> through the recursion or interpolating along the wrong
    /// variable. Uniform steps keep <c>t</c> in hand at every sample for a few
    /// extra points on an S-curve, which is the cheaper side of that trade in a
    /// record that already holds hundreds of drawn points per stroke.
    /// </para>
    /// </remarks>
    private static int StepsFor(
        double x0, double y0, double x1, double y1,
        double x2, double y2, double x3, double y3, double tolerance)
    {
        var ax = x0 - 2 * x1 + x2;
        var ay = y0 - 2 * y1 + y2;
        var bx = x1 - 2 * x2 + x3;
        var by = y1 - 2 * y2 + y3;
        var d = Math.Sqrt(Math.Max(ax * ax + ay * ay, bx * bx + by * by));
        if (d <= 0 || !double.IsFinite(d)) return 1;

        var n = (int)Math.Ceiling(Math.Sqrt(3 * d / (4 * Math.Max(1e-6, tolerance))));
        return Math.Clamp(n, 1, MaxSamplesPerSegment);
    }

    private static double Cubic(double p0, double p1, double p2, double p3, double t)
    {
        var u = 1 - t;
        return u * u * u * p0
            + 3 * u * u * t * p1
            + 3 * u * t * t * p2
            + t * t * t * p3;
    }
}
