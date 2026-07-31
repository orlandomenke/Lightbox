using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.Core.Inbetween;

/// <summary>
/// Computes the *effective* stroke record of a frame for interpolation.
///
/// A painted frame's record keeps every stroke, including brush strokes the
/// artist later erased — that's what makes re-rendering deterministic. But
/// interpolating the raw record resurrects erased artwork: erased strokes get
/// matched and drawn in the inbetweens. So before matching, we drop brush
/// strokes that later eraser strokes substantially covered, and drop the
/// eraser strokes themselves (their effect is baked in by the dropping).
///
/// Partially-erased strokes are kept whole — a visible-but-trimmed stroke
/// still carries the motion, and a faded "half eraser" in a tween looks far
/// worse than a briefly-untrimmed line.
/// </summary>
public static class StrokeRecordCleaner
{
    /// <summary>Fraction of a stroke's samples an eraser must cover to kill it.</summary>
    public const double CoverageThreshold = 0.85;

    private const int Samples = 32;
    private const double RadiusSlack = 1.05;

    public static List<Stroke> EffectiveStrokes(IReadOnlyList<Stroke> record)
    {
        var result = new List<Stroke>();
        for (var i = 0; i < record.Count; i++)
        {
            var stroke = record[i];
            if (stroke.Tool == ToolKind.Eraser) continue;

            var erasersAfter = new List<Stroke>();
            for (var k = i + 1; k < record.Count; k++)
            {
                if (record[k].Tool == ToolKind.Eraser) erasersAfter.Add(record[k]);
            }

            if (erasersAfter.Count == 0 || Coverage(stroke, erasersAfter) < CoverageThreshold)
            {
                result.Add(stroke);
            }
        }
        return result;
    }

    /// <summary>Fraction of the stroke's resampled points lying inside any eraser's swept area.</summary>
    private static double Coverage(Stroke stroke, IReadOnlyList<Stroke> erasers)
    {
        var points = stroke.Points.Count > Samples
            ? GeometryOps.Resample(stroke.Points, Samples)
            : (IReadOnlyList<StrokePoint>)stroke.Points;
        if (points.Count == 0) return 0;

        var covered = 0;
        foreach (var p in points)
        {
            if (erasers.Any(e => Covers(e, p))) covered++;
        }
        return (double)covered / points.Count;
    }

    private static bool Covers(Stroke eraser, StrokePoint p)
    {
        var pts = eraser.Points;
        if (pts.Count == 0) return false;
        if (pts.Count == 1)
        {
            var r = Radius(eraser, pts[0].Pressure);
            return GeometryOps.Dist(p, pts[0]) <= r;
        }
        for (var i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1];
            var b = pts[i];
            var r = Radius(eraser, Math.Max(a.Pressure, b.Pressure));
            if (GeometryOps.DistToSegment(p, a, b) <= r) return true;
        }
        return false;
    }

    private static double Radius(Stroke eraser, double pressure) =>
        eraser.Brush.Size * Math.Max(pressure, 0.05) / 2 * RadiusSlack;
}
