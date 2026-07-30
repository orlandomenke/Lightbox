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
