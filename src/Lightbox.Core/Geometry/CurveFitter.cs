using Lightbox.Core.Documents;

namespace Lightbox.Core.Geometry;

/// <summary>
/// <see cref="StrokePoint"/>s → <see cref="StrokePath"/>: Schneider's curve fit,
/// which is what makes a line somebody already drew editable.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is Q50's answer and the reason the vector work does not need a vector
/// layer.</b> A Lightbox stroke is already a centreline with a width at every
/// point — Harmony's *pencil line*, Disney's Meander — so there is no conversion
/// to do, only a description to add. Every drawing already in every document
/// becomes reshapeable the moment something asks for a path, and nothing about
/// the drawing changes when it does.
/// </para>
/// <para>
/// <b>The algorithm is Schneider's, from Graphics Gems (1990)</b>, and it is
/// chosen for being the one every tool uses rather than for being clever: fit one
/// cubic by least squares against a chord-length parameterisation, measure the
/// worst error, improve the parameterisation with a few Newton steps if it is
/// close, and split at the worst point and recurse if it is not. Illustrator,
/// Inkscape and potrace all descend from it, which means its failure cases are
/// the ones artists already have intuitions about.
/// </para>
/// <para>
/// <b>Deterministic, and it has to be.</b> The fit produces nodes an artist then
/// edits, and editing re-flattens into the points that render — so a fit that
/// varied between runs would make reopening a document a different drawing. There
/// is no randomness here and no dependence on anything outside the arguments; the
/// Newton iteration is capped rather than run to convergence for the same reason
/// (<see cref="NewtonIterations"/>).
/// </para>
/// <para>
/// <b>What it is not.</b> Not a simplifier — that is phase 4's <em>Simplify</em>,
/// which is this with the tolerance turned up and a node count shown to the
/// artist. Not lossy by intent: the caller keeps the original points, and one
/// undo restores them exactly, because fitting adds a field rather than replacing
/// one.
/// </para>
/// </remarks>
public static class CurveFitter
{
    /// <summary>
    /// Default worst-case deviation, in document pixels, between the fitted
    /// curve and the points it was fitted to.
    /// </summary>
    /// <remarks>
    /// Deliberately looser than <see cref="PathFlattener.Tolerance"/>. Flattening
    /// is a rendering step and wants to be invisible; fitting is an
    /// <em>authoring</em> step, and its job is to produce a handful of nodes a
    /// hand can work with. A fit tight enough to be invisible produces a node per
    /// wobble, which is a path nobody can edit — the tool would appear to work
    /// and be useless.
    /// </remarks>
    public const double Tolerance = 1.5;

    /// <summary>
    /// How many Newton-Raphson reparameterisation passes to attempt before
    /// splitting.
    /// </summary>
    /// <remarks>
    /// Schneider's own figure. Capped rather than iterated to convergence so the
    /// result is a function of the input alone: "until it stops improving" is a
    /// stopping rule that depends on floating-point noise, and this fit has to
    /// produce the same nodes every time it is asked.
    /// </remarks>
    public const int NewtonIterations = 4;

    /// <summary>
    /// Fit a path through these points. Returns null when there is nothing to
    /// fit.
    /// </summary>
    /// <param name="closed">
    /// Whether the line joins back to its start — a fill's contour does, a drawn
    /// line does not. Passed in rather than guessed from the endpoints, because a
    /// stroke that happens to end where it began is not the same thing as a
    /// closed one.
    /// </param>
    public static StrokePath? Fit(
        IReadOnlyList<StrokePoint> points, double tolerance = Tolerance, bool closed = false)
    {
        var clean = Dedupe(points);
        if (clean.Count < 2) return null;

        // Two points cannot bend, so there is nothing for a fit to discover and
        // the honest answer is the straight line they describe.
        if (clean.Count == 2)
        {
            return new StrokePath
            {
                Closed = closed,
                Nodes =
                [
                    PathNode.At(clean[0].X, clean[0].Y, clean[0].Pressure),
                    PathNode.At(clean[1].X, clean[1].Y, clean[1].Pressure),
                ],
            };
        }

        // Both tangents point INTO the curve from their own end — the left one
        // forwards, the right one backwards. Getting the right one's sign wrong
        // is not a subtle error and does not look like one: the least-squares
        // solve returns a negative handle length, the fallback plants the handle
        // outside the curve, the error blows past tolerance and the fit splits.
        // A dead-straight line came back as eight nodes.
        var left = UnitTangent(clean[0], clean[1]);
        var right = UnitTangent(clean[^1], clean[^2]);

        var beziers = new List<Cubic>();
        FitCubic(clean, 0, clean.Count - 1, left, right, tolerance, beziers);

        return ToPath(clean, beziers, closed);
    }

    /// <summary>One fitted segment, plus which input point each end sits on.</summary>
    private readonly record struct Cubic(
        int FirstPoint, int LastPoint,
        double P1X, double P1Y, double P2X, double P2Y);

    /// <summary>
    /// Consecutive identical points break the chord-length parameterisation
    /// (a zero-length chord divides by zero), and a pen at rest produces them.
    /// </summary>
    private static List<StrokePoint> Dedupe(IReadOnlyList<StrokePoint> points)
    {
        var clean = new List<StrokePoint>(points.Count);
        foreach (var p in points)
        {
            if (clean.Count > 0)
            {
                var last = clean[^1];
                if (Math.Abs(p.X - last.X) < 1e-9 && Math.Abs(p.Y - last.Y) < 1e-9) continue;
            }
            clean.Add(p);
        }
        return clean;
    }

    private static void FitCubic(
        List<StrokePoint> pts, int first, int last,
        (double X, double Y) tHat1, (double X, double Y) tHat2,
        double tolerance, List<Cubic> into)
    {
        var count = last - first + 1;

        if (count == 2)
        {
            // Schneider's heuristic for the degenerate case: put both handles a
            // third of the way along the chord, in the tangent directions.
            var dist = Distance(pts[first], pts[last]) / 3.0;
            into.Add(new Cubic(
                first, last,
                pts[first].X + tHat1.X * dist, pts[first].Y + tHat1.Y * dist,
                pts[last].X + tHat2.X * dist, pts[last].Y + tHat2.Y * dist));
            return;
        }

        var u = ChordLengthParameterise(pts, first, last);
        var curve = GenerateBezier(pts, first, last, u, tHat1, tHat2);
        var (error, split) = MaxError(pts, first, last, curve, u);

        if (error < tolerance)
        {
            into.Add(curve);
            return;
        }

        // Close enough that a better parameterisation may well close the gap.
        // Beyond this the shape is wrong rather than the spacing, and iterating
        // only burns time before the split that was always coming.
        if (error < tolerance * tolerance)
        {
            for (var i = 0; i < NewtonIterations; i++)
            {
                u = Reparameterise(pts, first, last, u, curve);
                curve = GenerateBezier(pts, first, last, u, tHat1, tHat2);
                (error, split) = MaxError(pts, first, last, curve, u);
                if (error < tolerance)
                {
                    into.Add(curve);
                    return;
                }
            }
        }

        // Split at the worst point, with a tangent that keeps the two halves
        // smooth across the join. A split point is where a corner would go if the
        // line has one, which is why the node it produces is marked as smooth
        // here and only becomes a corner when the geometry says so (ToPath).
        var centre = CentreTangent(pts, split);
        FitCubic(pts, first, split, tHat1, centre, tolerance, into);
        FitCubic(pts, split, last, (-centre.X, -centre.Y), tHat2, tolerance, into);
    }

    /// <summary>
    /// Least-squares fit of the two interior control points, with the endpoints
    /// and both tangent directions fixed.
    /// </summary>
    private static Cubic GenerateBezier(
        List<StrokePoint> pts, int first, int last, double[] u,
        (double X, double Y) tHat1, (double X, double Y) tHat2)
    {
        var count = last - first + 1;
        var c00 = 0.0; var c01 = 0.0; var c11 = 0.0;
        var x0 = 0.0; var x1v = 0.0;

        for (var i = 0; i < count; i++)
        {
            var t = u[i];
            var b1 = B1(t);
            var b2 = B2(t);
            var a1X = tHat1.X * b1;
            var a1Y = tHat1.Y * b1;
            var a2X = tHat2.X * b2;
            var a2Y = tHat2.Y * b2;

            c00 += a1X * a1X + a1Y * a1Y;
            c01 += a1X * a2X + a1Y * a2Y;
            c11 += a2X * a2X + a2Y * a2Y;

            // The residual: the point, less where the fixed endpoints already
            // put the curve at this parameter.
            var b0 = B0(t);
            var b3 = B3(t);
            var tmpX = pts[first + i].X - (pts[first].X * (b0 + b1) + pts[last].X * (b2 + b3));
            var tmpY = pts[first + i].Y - (pts[first].Y * (b0 + b1) + pts[last].Y * (b2 + b3));

            x0 += a1X * tmpX + a1Y * tmpY;
            x1v += a2X * tmpX + a2Y * tmpY;
        }

        var det = c00 * c11 - c01 * c01;
        double alphaL, alphaR;
        if (Math.Abs(det) < 1e-12)
        {
            alphaL = alphaR = 0;
        }
        else
        {
            alphaL = (c11 * x0 - c01 * x1v) / det;
            alphaR = (c00 * x1v - c01 * x0) / det;
        }

        // A negative or vanishing handle means the least-squares answer folded
        // the curve back on itself. Schneider's fallback — the same third-of-the-
        // chord heuristic as the two-point case — is used rather than clamping,
        // because a clamped fold is still a fold.
        var segLength = Distance(pts[first], pts[last]);
        var epsilon = 1e-6 * segLength;
        if (alphaL < epsilon || alphaR < epsilon)
        {
            alphaL = alphaR = segLength / 3.0;
        }

        return new Cubic(
            first, last,
            pts[first].X + tHat1.X * alphaL, pts[first].Y + tHat1.Y * alphaL,
            pts[last].X + tHat2.X * alphaR, pts[last].Y + tHat2.Y * alphaR);
    }

    private static double[] ChordLengthParameterise(List<StrokePoint> pts, int first, int last)
    {
        var count = last - first + 1;
        var u = new double[count];
        u[0] = 0;
        for (var i = 1; i < count; i++)
        {
            u[i] = u[i - 1] + Distance(pts[first + i - 1], pts[first + i]);
        }
        var total = u[^1];
        if (total <= 0) total = 1;
        for (var i = 1; i < count; i++) u[i] /= total;
        return u;
    }

    /// <summary>
    /// One Newton-Raphson step per point: slide each parameter towards the
    /// nearest point on the current curve.
    /// </summary>
    private static double[] Reparameterise(
        List<StrokePoint> pts, int first, int last, double[] u, Cubic curve)
    {
        var count = last - first + 1;
        var next = new double[count];
        for (var i = 0; i < count; i++)
        {
            next[i] = NewtonStep(pts, curve, pts[first + i], u[i]);
        }
        return next;
    }

    private static double NewtonStep(List<StrokePoint> pts, Cubic c, StrokePoint p, double t)
    {
        var (qx, qy) = Evaluate(pts, c, t);
        var (q1x, q1y) = Derivative1(pts, c, t);
        var (q2x, q2y) = Derivative2(pts, c, t);

        var numerator = (qx - p.X) * q1x + (qy - p.Y) * q1y;
        var denominator = q1x * q1x + q1y * q1y + (qx - p.X) * q2x + (qy - p.Y) * q2y;
        if (Math.Abs(denominator) < 1e-12) return t;
        return t - numerator / denominator;
    }

    private static (double Error, int Split) MaxError(
        List<StrokePoint> pts, int first, int last, Cubic curve, double[] u)
    {
        var count = last - first + 1;
        var worst = 0.0;
        // Defaults to the middle: a curve that fits perfectly still has to name a
        // split point, and the midpoint is the one that halves the work rather
        // than shaving one point off an end and recursing forever.
        var split = (first + last) / 2;

        for (var i = 1; i < count - 1; i++)
        {
            var (x, y) = Evaluate(pts, curve, u[i]);
            var dx = x - pts[first + i].X;
            var dy = y - pts[first + i].Y;
            var dist = dx * dx + dy * dy;
            if (dist <= worst) continue;
            worst = dist;
            split = first + i;
        }
        return (Math.Sqrt(worst), split);
    }

    private static StrokePath ToPath(List<StrokePoint> pts, List<Cubic> beziers, bool closed)
    {
        var path = new StrokePath { Closed = closed };
        if (beziers.Count == 0) return path;

        for (var i = 0; i < beziers.Count; i++)
        {
            var seg = beziers[i];
            var anchor = pts[seg.FirstPoint];
            // The incoming handle belongs to the previous segment's second
            // control point; the first node has none.
            var (inX, inY) = i == 0
                ? (0.0, 0.0)
                : (beziers[i - 1].P2X - anchor.X, beziers[i - 1].P2Y - anchor.Y);
            var outX = seg.P1X - anchor.X;
            var outY = seg.P1Y - anchor.Y;

            path.Nodes.Add(new PathNode(
                anchor.X, anchor.Y, anchor.Pressure,
                inX, inY, outX, outY,
                Corner: IsCorner(inX, inY, outX, outY)));
        }

        var lastSeg = beziers[^1];
        var end = pts[lastSeg.LastPoint];
        path.Nodes.Add(new PathNode(
            end.X, end.Y, end.Pressure,
            lastSeg.P2X - end.X, lastSeg.P2Y - end.Y,
            0, 0,
            Corner: true));

        return path;
    }

    /// <summary>
    /// Whether the two handles point in meaningfully different directions.
    /// </summary>
    /// <remarks>
    /// A node whose handles are opposite is smooth, and dragging one should swing
    /// the other. Marking every fitted node a corner would be safe and would make
    /// the fitted path miserable to edit — every drag would break the curve at
    /// that node. The threshold is generous (about 8°) because a fit's join is
    /// smooth by construction and only lands here as a corner when the drawn line
    /// genuinely turned.
    /// </remarks>
    private static bool IsCorner(double inX, double inY, double outX, double outY)
    {
        var inLen = Math.Sqrt(inX * inX + inY * inY);
        var outLen = Math.Sqrt(outX * outX + outY * outY);
        if (inLen < 1e-9 || outLen < 1e-9) return true;

        // Opposite handles have a dot product of -1 once normalised.
        var dot = (inX * outX + inY * outY) / (inLen * outLen);
        return dot > -0.99;
    }

    private static (double X, double Y) UnitTangent(StrokePoint from, StrokePoint to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        return len < 1e-12 ? (0, 0) : (dx / len, dy / len);
    }

    /// <summary>
    /// The tangent at a split, averaged across it so the two halves meet
    /// smoothly rather than kinking at every node the fit happened to place.
    /// </summary>
    private static (double X, double Y) CentreTangent(List<StrokePoint> pts, int centre)
    {
        var before = centre > 0 ? centre - 1 : centre;
        var after = centre < pts.Count - 1 ? centre + 1 : centre;
        var dx = pts[before].X - pts[after].X;
        var dy = pts[before].Y - pts[after].Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        return len < 1e-12 ? (0, 0) : (dx / len, dy / len);
    }

    private static double Distance(StrokePoint a, StrokePoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static (double X, double Y) Evaluate(List<StrokePoint> pts, Cubic c, double t)
    {
        var u = 1 - t;
        var b0 = u * u * u;
        var b1 = 3 * u * u * t;
        var b2 = 3 * u * t * t;
        var b3 = t * t * t;
        return (
            pts[c.FirstPoint].X * b0 + c.P1X * b1 + c.P2X * b2 + pts[c.LastPoint].X * b3,
            pts[c.FirstPoint].Y * b0 + c.P1Y * b1 + c.P2Y * b2 + pts[c.LastPoint].Y * b3);
    }

    private static (double X, double Y) Derivative1(List<StrokePoint> pts, Cubic c, double t)
    {
        var p0 = pts[c.FirstPoint];
        var p3 = pts[c.LastPoint];
        var d0X = 3 * (c.P1X - p0.X); var d0Y = 3 * (c.P1Y - p0.Y);
        var d1X = 3 * (c.P2X - c.P1X); var d1Y = 3 * (c.P2Y - c.P1Y);
        var d2X = 3 * (p3.X - c.P2X); var d2Y = 3 * (p3.Y - c.P2Y);
        var u = 1 - t;
        return (
            d0X * u * u + d1X * 2 * u * t + d2X * t * t,
            d0Y * u * u + d1Y * 2 * u * t + d2Y * t * t);
    }

    private static (double X, double Y) Derivative2(List<StrokePoint> pts, Cubic c, double t)
    {
        var p0 = pts[c.FirstPoint];
        var p3 = pts[c.LastPoint];
        var d0X = 3 * (c.P1X - p0.X); var d0Y = 3 * (c.P1Y - p0.Y);
        var d1X = 3 * (c.P2X - c.P1X); var d1Y = 3 * (c.P2Y - c.P1Y);
        var d2X = 3 * (p3.X - c.P2X); var d2Y = 3 * (p3.Y - c.P2Y);
        return (
            2 * (d1X - d0X) * (1 - t) + 2 * (d2X - d1X) * t,
            2 * (d1Y - d0Y) * (1 - t) + 2 * (d2Y - d1Y) * t);
    }

    private static double B0(double t) { var u = 1 - t; return u * u * u; }

    private static double B1(double t) { var u = 1 - t; return 3 * u * u * t; }

    private static double B2(double t) { var u = 1 - t; return 3 * u * t * t; }

    private static double B3(double t) => t * t * t;
}
