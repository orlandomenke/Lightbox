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
        var et = EasingOps.Ease(Math.Clamp(t, 0, 1), easing);
        var strokes = new List<Stroke>();
        foreach (var pair in StrokeMatcher.Match(a, b))
        {
            if (pair.A is not null && pair.B is not null)
            {
                strokes.Add(StrokeInterpolator.Interpolate(pair.A, pair.B, et));
            }
            else if (pair.A is not null)
            {
                // Fades out over the first half so it's gone before B arrives.
                var op = pair.A.Brush.Opacity * Math.Max(0, 1 - et * 2);
                if (op > FadeThreshold)
                {
                    var s = pair.A.Clone();
                    s.Brush.Opacity = op;
                    strokes.Add(s);
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
                    strokes.Add(s);
                }
            }
        }
        return strokes;
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
}
