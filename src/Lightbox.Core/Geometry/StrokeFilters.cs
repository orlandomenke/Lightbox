using Lightbox.Core.Documents;

namespace Lightbox.Core.Geometry;

/// <summary>
/// Post-stroke input filters: applied once to a finished stroke's recorded
/// points (never to pixels), so the filtered stroke IS the record and
/// re-renders deterministically like any other.
/// </summary>
public static class StrokeFilters
{
    /// <summary>
    /// Centered moving average over <paramref name="window"/> points; the
    /// window shrinks at the ends so endpoints stay put. Filters hand tremor
    /// evenly; heavier windows round corners more.
    /// </summary>
    public static List<StrokePoint> MovingAverage(IReadOnlyList<StrokePoint> points, int window)
    {
        window = Math.Clamp(window | 1, 3, 25); // odd, sane bounds
        if (points.Count < 3) return points.ToList();
        var half = window / 2;
        var output = new List<StrokePoint>(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            // shrink symmetrically near the ends so the stroke doesn't creep
            var reach = Math.Min(half, Math.Min(i, points.Count - 1 - i));
            double x = 0, y = 0, p = 0;
            for (var k = -reach; k <= reach; k++)
            {
                var pt = points[i + k];
                x += pt.X;
                y += pt.Y;
                p += pt.Pressure;
            }
            var n = reach * 2 + 1;
            output.Add(new StrokePoint(x / n, y / n, p / n));
        }
        return output;
    }

    /// <summary>
    /// Savitzky–Golay smoothing (quadratic fit over a moving window):
    /// removes jitter while preserving peaks and corners better than a plain
    /// average. Supported windows: 5, 7, 9, 11 (nearest is used).
    /// </summary>
    public static List<StrokePoint> SavitzkyGolay(IReadOnlyList<StrokePoint> points, int window)
    {
        if (points.Count < 5) return points.ToList();
        var coefficients = NearestSavGolKernel(window);
        var half = coefficients.Length / 2;
        var norm = coefficients.Sum();
        var output = new List<StrokePoint>(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            double x = 0, y = 0, p = 0;
            for (var k = -half; k <= half; k++)
            {
                // replicate the endpoints beyond the stroke (standard edge handling)
                var pt = points[Math.Clamp(i + k, 0, points.Count - 1)];
                var w = coefficients[k + half];
                x += pt.X * w;
                y += pt.Y * w;
                p += pt.Pressure * w;
            }
            output.Add(new StrokePoint(x / norm, y / norm, Math.Clamp(p / norm, 0, 1)));
        }
        // endpoints anchored: the stroke must start and end where the pen did
        output[0] = points[0];
        output[^1] = points[^1];
        return output;
    }

    private static double[] NearestSavGolKernel(int window) => window switch
    {
        <= 5 => [-3, 12, 17, 12, -3],
        <= 7 => [-2, 3, 6, 7, 6, 3, -2],
        <= 9 => [-21, 14, 39, 54, 59, 54, 39, 14, -21],
        _ => [-36, 9, 44, 69, 84, 89, 84, 69, 44, 9, -36],
    };
}
