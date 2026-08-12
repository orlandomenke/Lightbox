using Lightbox.Core.Documents;

namespace Lightbox.Core.Inbetween;

/// <summary>
/// Deterministic inbetweening over stroke geometry.
///
/// Given two keyframes' stroke lists, produces the drawing at t ∈ (0,1):
/// match strokes (label first, then greedy nearest-centroid weighted by
/// length ratio), resample matched pairs, interpolate all channels with the
/// chosen easing; unmatched strokes fade out over the first half / fade in
/// over the second half so nothing pops.
///
/// Output is a stroke list — the raster pipeline re-renders it with the same
/// brush that painted the keys, which is what makes inbetweens
/// indistinguishable from hand-painted frames. The AI produces the same
/// shape of output, so both producers share one insertion path.
/// </summary>
public static class Inbetweener
{
    /// <summary>Strokes below this opacity are dropped from fades.</summary>
    public const double FadeThreshold = 0.02;

    public static List<Stroke> Inbetween(
        IReadOnlyList<Stroke> a,
        IReadOnlyList<Stroke> b,
        double t,
        Easing easing = Easing.EaseInOut)
    {
        // Interpolate the *effective* drawings: erased strokes must not come
        // back to life in the inbetweens, and eraser marks aren't artwork.
        var ea = StrokeRecordCleaner.EffectiveStrokes(a);
        var eb = StrokeRecordCleaner.EffectiveStrokes(b);

        var et = EasingOps.Ease(Math.Clamp(t, 0, 1), easing);

        // Stable stamping order: keep A's paint order for everything anchored
        // in A; B-only strokes go after, in B's paint order.
        var indexInA = new Dictionary<string, int>();
        for (var i = 0; i < ea.Count; i++) indexInA[ea[i].Id] = i;
        var indexInB = new Dictionary<string, int>();
        for (var i = 0; i < eb.Count; i++) indexInB[eb[i].Id] = i;

        var ordered = new List<(int Key, Stroke Stroke)>();
        foreach (var pair in StrokeMatcher.Match(ea, eb))
        {
            if (pair.A is not null && pair.B is not null)
            {
                ordered.Add((indexInA[pair.A.Id], StrokeInterpolator.Interpolate(pair.A, pair.B, et)));
            }
            else if (pair.A is not null)
            {
                // Fades out over the first half so it's gone before B arrives.
                var op = pair.A.Brush.Opacity * Math.Max(0, 1 - et * 2);
                if (op > FadeThreshold)
                {
                    var s = pair.A.Clone();
                    s.Brush.Opacity = op;
                    ordered.Add((indexInA[pair.A.Id], s));
                }
            }
            else if (pair.B is not null)
            {
                // Fades in over the second half.
                var op = pair.B.Brush.Opacity * Math.Max(0, et * 2 - 1);
                if (op > FadeThreshold)
                {
                    var s = pair.B.Clone();
                    s.Brush.Opacity = op;
                    ordered.Add((ea.Count + indexInB[pair.B.Id], s));
                }
            }
        }
        return ordered.OrderBy(x => x.Key).Select(x => x.Stroke).ToList();
    }

    /// <summary>
    /// Evenly spaced inbetweens for a gap of <paramref name="count"/> frames:
    /// t = 1/(count+1) .. count/(count+1).
    /// </summary>
    public static List<List<Stroke>> InbetweenSeries(
        IReadOnlyList<Stroke> a,
        IReadOnlyList<Stroke> b,
        int count,
        Easing easing = Easing.EaseInOut)
    {
        var frames = new List<List<Stroke>>(count);
        for (var k = 1; k <= count; k++)
        {
            frames.Add(Inbetween(a, b, (double)k / (count + 1), easing));
        }
        return frames;
    }

    /// <summary>
    /// Inbetweens placed by a timing chart (Q58): one drawing per rung, at
    /// the rung's fraction of the travel. Linear on purpose — the chart IS
    /// the shaping, and easing the rungs again would move them off where the
    /// artist put them.
    /// </summary>
    public static List<List<Stroke>> InbetweenSeries(
        IReadOnlyList<Stroke> a,
        IReadOnlyList<Stroke> b,
        IReadOnlyList<double> chart)
    {
        var frames = new List<List<Stroke>>(chart.Count);
        foreach (var rung in chart)
        {
            frames.Add(Inbetween(a, b, rung, Easing.Linear));
        }
        return frames;
    }
}
