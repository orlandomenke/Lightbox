using Lightbox.Core.Documents;

namespace Lightbox.Core.Geometry;

public static class GeometryOps
{
    public static double Dist(StrokePoint a, StrokePoint b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    public static StrokePoint LerpPoint(StrokePoint a, StrokePoint b, double t) => new(
        Lerp(a.X, b.X, t),
        Lerp(a.Y, b.Y, t),
        Lerp(a.Pressure, b.Pressure, t));

    public static double PathLength(IReadOnlyList<StrokePoint> points)
    {
        double len = 0;
        for (var i = 1; i < points.Count; i++) len += Dist(points[i - 1], points[i]);
        return len;
    }

    public static StrokePoint Centroid(IReadOnlyList<StrokePoint> points)
    {
        if (points.Count == 0) return new StrokePoint(0, 0, 0.5);
        double x = 0, y = 0;
        foreach (var p in points)
        {
            x += p.X;
            y += p.Y;
        }
        return new StrokePoint(x / points.Count, y / points.Count, 0.5);
    }

    /// <summary>
    /// Resample a polyline to exactly <paramref name="n"/> points, evenly
    /// spaced by arc length. Normalizes two strokes so they can be
    /// interpolated point-by-point.
    /// </summary>
    public static List<StrokePoint> Resample(IReadOnlyList<StrokePoint> points, int n)
    {
        if (points.Count == 0) return [];
        if (points.Count == 1 || n == 1)
            return [.. Enumerable.Repeat(points[0], n)];

        var total = PathLength(points);
        if (total == 0)
            return [.. Enumerable.Repeat(points[0], n)];

        var output = new List<StrokePoint>(n) { points[0] };
        var step = total / (n - 1);
        double acc = 0;
        var i = 1;
        var prev = points[0];
        while (output.Count < n - 1 && i < points.Count)
        {
            var cur = points[i];
            var d = Dist(prev, cur);
            if (acc + d >= step && d > 0)
            {
                var t = (step - acc) / d;
                var np = LerpPoint(prev, cur, t);
                output.Add(np);
                prev = np;
                acc = 0;
            }
            else
            {
                acc += d;
                prev = cur;
                i++;
            }
        }
        while (output.Count < n) output.Add(points[^1]);
        return output;
    }

    /// <summary>Moving-average smoothing that preserves endpoints.</summary>
    public static List<StrokePoint> Smooth(IReadOnlyList<StrokePoint> points, int iterations = 1)
    {
        var pts = points.ToList();
        for (var it = 0; it < iterations; it++)
        {
            if (pts.Count < 3) return pts;
            var output = new List<StrokePoint>(pts.Count) { pts[0] };
            for (var i = 1; i < pts.Count - 1; i++)
            {
                var a = pts[i - 1];
                var b = pts[i];
                var c = pts[i + 1];
                output.Add(new StrokePoint(
                    (a.X + 2 * b.X + c.X) / 4,
                    (a.Y + 2 * b.Y + c.Y) / 4,
                    (a.Pressure + 2 * b.Pressure + c.Pressure) / 4));
            }
            output.Add(pts[^1]);
            pts = output;
        }
        return pts;
    }

    /// <summary>
    /// Above this turn, a vertex is a corner the artist meant rather than a
    /// bend the pen under-sampled, and the path is broken there so it stays
    /// sharp. 60° keeps a drawn rectangle square and a deliberate flick
    /// pointed, while leaving a fast-drawn curve — rarely past 30° between
    /// samples even on a flick — to be rounded.
    /// </summary>
    private const double CornerTurnDeg = 60;

    /// <summary>
    /// Fill in the curve between recorded points, so a path is walked as the
    /// arc it was drawn as rather than as the chords the pen happened to
    /// sample.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pen reports at a fixed rate, so a fast stroke arrives as a handful of
    /// widely spaced points and a straight-line walk between them cuts every
    /// corner. On a thin brush that is invisible; on a thick one the flat
    /// facet is wider than the line and reads as the tops of the individual
    /// stamps showing through the outside of the bend. Measured on a 180 px
    /// arc: samples 20° apart put the path 2.7 px inside the curve, 30° apart
    /// 6.1 px — both far past the point where a 60 px brush shows it.
    /// </para>
    /// <para>
    /// Centripetal Catmull–Rom, so the curve <b>passes exactly through every
    /// recorded point</b> — the record still says where the pen was, and this
    /// only decides what happens between two of them. Centripetal rather than
    /// uniform because the uniform form loops and self-intersects when samples
    /// are unevenly spaced, which is precisely the fast-stroke case this is
    /// for.
    /// </para>
    /// <para>
    /// Deterministic and a pure function of the points, so invariant 2 holds:
    /// the dabs land in the same places on every re-render, and the position
    /// hashes that seed scatter and jitter stay put with them.
    /// </para>
    /// </remarks>
    /// <param name="maxChord">
    /// Longest straight piece the result may contain, in document units. The
    /// residual sagitta falls with its square, so ~2 px is already far below
    /// what any brush can show.
    /// </param>
    public static IReadOnlyList<StrokePoint> Densify(IReadOnlyList<StrokePoint> points, double maxChord = 2.0)
    {
        if (points.Count < 3) return points;

        // Nothing to add: hand the caller its own list back rather than a copy
        // of it. This runs from the dab walk on every pointer event, and a
        // stroke drawn at any ordinary speed already arrives with its points
        // closer together than the chord — so the common case allocates
        // nothing, and pays only for one pass that stops at the first span
        // long enough to matter.
        var needed = false;
        for (var i = 0; i + 1 < points.Count && !needed; i++)
        {
            needed = Dist(points[i], points[i + 1]) > maxChord;
        }
        if (!needed) return points;

        var output = new List<StrokePoint> { points[0] };
        for (var i = 0; i < points.Count - 1; i++)
        {
            var p1 = points[i];
            var p2 = points[i + 1];
            var span = Dist(p1, p2);
            // Already short enough to be its own chord, so nothing to add.
            // Also the zero-length case, where the tangents are undefined.
            if (span <= maxChord)
            {
                output.Add(p2);
                continue;
            }

            // Outside control points. Where there is no real neighbour — the
            // ends of the stroke, and either side of a corner that must stay
            // sharp — the missing one is the span's own endpoint, which pins
            // the tangent to the chord and lets that span run straighter than
            // its neighbours.
            //
            // Reflecting instead (p0 = 2·p1 − p2, the textbook end condition)
            // was tried and measured worse: on a 180 px arc sampled every 30°
            // it took the end spans from 2.9 px off the curve to 3.8 px,
            // because a reflected control puts the tangent on the chord of the
            // WRONG side of the vertex.
            var p0 = i > 0 && !IsCorner(points[i - 1], p1, p2) ? points[i - 1] : p1;
            var p3 = i + 2 < points.Count && !IsCorner(p1, p2, points[i + 2]) ? points[i + 2] : p2;

            var steps = (int)Math.Min(64, Math.Ceiling(span / maxChord));
            for (var s = 1; s <= steps; s++)
            {
                output.Add(CatmullRom(p0, p1, p2, p3, (double)s / steps));
            }
        }
        return output;
    }

    /// <summary>Whether the turn at <paramref name="b"/> is sharp enough to be intent.</summary>
    private static bool IsCorner(StrokePoint a, StrokePoint b, StrokePoint c)
    {
        double ux = b.X - a.X, uy = b.Y - a.Y, vx = c.X - b.X, vy = c.Y - b.Y;
        var lu = Math.Sqrt(ux * ux + uy * uy);
        var lv = Math.Sqrt(vx * vx + vy * vy);
        // A doubled point has no direction, so it cannot be shown to be a
        // corner. Treating it as one would break the curve at every stall.
        if (lu == 0 || lv == 0) return false;
        var cos = Math.Clamp((ux * vx + uy * vy) / (lu * lv), -1, 1);
        return Math.Acos(cos) * 180 / Math.PI > CornerTurnDeg;
    }

    /// <summary>
    /// Centripetal Catmull–Rom at t in [0,1] on the p1→p2 span. Pressure rides
    /// the same curve, so a taper interpolates as smoothly as the path does.
    /// </summary>
    private static StrokePoint CatmullRom(
        StrokePoint p0, StrokePoint p1, StrokePoint p2, StrokePoint p3, double t)
    {
        // Centripetal parameterisation: knot spacing is the square root of the
        // chord length (alpha = 0.5).
        static double Knot(double prev, StrokePoint a, StrokePoint b)
        {
            var d = Math.Sqrt(Dist(a, b));
            // Coincident controls would divide by zero below; nudging the knot
            // makes the span degenerate to a straight line instead.
            return prev + (d > 1e-9 ? d : 1e-9);
        }

        var t0 = 0.0;
        var t1 = Knot(t0, p0, p1);
        var t2 = Knot(t1, p1, p2);
        var t3 = Knot(t2, p2, p3);
        var tt = Lerp(t1, t2, t);

        static StrokePoint Blend(StrokePoint a, StrokePoint b, double ta, double tb, double tt)
        {
            var w = (tb - tt) / (tb - ta);
            return new StrokePoint(
                a.X * w + b.X * (1 - w),
                a.Y * w + b.Y * (1 - w),
                a.Pressure * w + b.Pressure * (1 - w));
        }

        var a1 = Blend(p0, p1, t0, t1, tt);
        var a2 = Blend(p1, p2, t1, t2, tt);
        var a3 = Blend(p2, p3, t2, t3, tt);
        var b1 = Blend(a1, a2, t0, t2, tt);
        var b2 = Blend(a2, a3, t1, t3, tt);
        return Blend(b1, b2, t1, t2, tt);
    }

    /// <summary>Distance from point p to the segment a–b.</summary>
    public static double DistToSegment(StrokePoint p, StrokePoint a, StrokePoint b)
    {
        var abx = b.X - a.X;
        var aby = b.Y - a.Y;
        var lenSq = abx * abx + aby * aby;
        if (lenSq == 0) return Dist(p, a);
        var t = Math.Clamp(((p.X - a.X) * abx + (p.Y - a.Y) * aby) / lenSq, 0, 1);
        var proj = new StrokePoint(a.X + t * abx, a.Y + t * aby, 0);
        return Dist(p, proj);
    }

    public readonly record struct BBox(double MinX, double MinY, double MaxX, double MaxY);

    public static BBox BoundsOf(IReadOnlyList<StrokePoint> points)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var p in points)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }
        return new BBox(minX, minY, maxX, maxY);
    }
}
