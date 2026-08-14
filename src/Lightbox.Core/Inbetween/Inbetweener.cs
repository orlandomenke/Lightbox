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

    /// <summary>One drawing already in a run: where it sits in 0..1, and its strokes.</summary>
    public readonly record struct RunStop(double Time, IReadOnlyList<Stroke> Strokes);

    /// <summary>
    /// Fill a whole run — two extremes with any number of already-drawn
    /// waypoints between them — easing across the run rather than within each
    /// gap.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is what "the breakdown is a hard constraint on the arc" actually
    /// asks for</b>, and it is not what it sounds like. A breakdown was already
    /// honoured exactly, because the old command stopped at the next
    /// <em>drawing</em> and so the breakdown was an endpoint rather than
    /// something to pass through. What it was not was part of one arc: the
    /// easing restarted at it, so a single slow-out/slow-in the animator drew
    /// across a run came out as two, with a stutter at the breakdown. The
    /// constraint is on the <em>curve</em>, not on the pose.
    /// </para>
    /// <para>
    /// So the easing is evaluated once over the whole run, and each gap is then
    /// interpolated <b>linearly</b> between its own two drawings at the local
    /// fraction the global curve landed on:
    /// <c>(E(τ) − E(τₖ)) / (E(τₖ₊₁) − E(τₖ))</c>. Easing again inside the gap
    /// would apply the curve twice, which is the bug restated.
    /// </para>
    /// <para>
    /// <b>Nothing is moved.</b> Each waypoint keeps its own time and its own
    /// pose, and every new drawing stays inside the gap it belongs to — a
    /// renormalized local fraction cannot leave its bracket, so no inbetween
    /// between the key and the breakdown can show a pose from beyond it. That
    /// the artist's placement may disagree with the easing is not an error to
    /// correct here: <see cref="Timeline.SpacingChart"/> exists to *show* that
    /// disagreement, and silently re-spacing a drawing the artist placed would
    /// be the application arguing with them.
    /// </para>
    /// <para>
    /// With two stops and nothing between, this reduces to
    /// <see cref="Inbetween"/> exactly — <c>E(0)=0</c>, <c>E(1)=1</c>, so the
    /// local fraction is <c>E(τ)</c> — which is why a document with no
    /// breakdowns renders identically before and after.
    /// </para>
    /// </remarks>
    /// <param name="run">The drawings already in the run, in time order, extremes at each end.</param>
    /// <param name="times">Where the new drawings go, in 0..1 across the run.</param>
    public static List<List<Stroke>> InbetweenRun(
        IReadOnlyList<RunStop> run,
        IReadOnlyList<double> times,
        Easing easing = Easing.EaseInOut)
    {
        if (run.Count < 2) return [];
        var frames = new List<List<Stroke>>(times.Count);
        foreach (var time in times)
        {
            var t = Math.Clamp(time, 0, 1);
            var k = 0;
            while (k < run.Count - 2 && t > run[k + 1].Time) k++;

            var lo = EasingOps.Ease(Math.Clamp(run[k].Time, 0, 1), easing);
            var hi = EasingOps.Ease(Math.Clamp(run[k + 1].Time, 0, 1), easing);
            var local = hi > lo ? (EasingOps.Ease(t, easing) - lo) / (hi - lo) : 0;

            // Linear: the run's easing is already in `local`.
            frames.Add(Inbetween(run[k].Strokes, run[k + 1].Strokes, local, Easing.Linear));
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
