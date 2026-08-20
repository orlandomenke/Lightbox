using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;

namespace Lightbox.Core.Timeline;

/// <summary>
/// One drawing's spacing verdict: where its subject is, where the intended
/// spacing would put it, and whether the gap is worth pointing at.
/// </summary>
/// <param name="Index">The drawing's cel index, the trail's ordering.</param>
/// <param name="Deviation">Distance from the subject to its target, in document pixels.</param>
/// <param name="Misses">
/// True when the deviation clears the flag threshold — a fraction of the
/// run's travel with an absolute floor, so a hair off on a broad sweep and
/// sub-pixel noise on a tiny one are both left alone.
/// </param>
public readonly record struct SpacingTarget(
    int Index, string FrameId,
    double X, double Y,
    double TargetX, double TargetY,
    double Deviation, bool Misses);

/// <summary>
/// The acting half of the spacing chart (Q133): where each inbetween's subject
/// SHOULD sit for the run's intended spacing, so the miss can be shown as a
/// ghost tick on the motion trail and closed with one nudge.
/// </summary>
/// <remarks>
/// <para>
/// The run and the intent are the ones the rest of the timeline already
/// agrees on: extreme to extreme by <see cref="ExposureSheet.RunAt"/>'s
/// closing rule, the opening extreme's timing chart when it still describes
/// the run, else the global easing — exactly <see cref="SpacingChart"/>'s
/// reading. The path is the artist's own: targets sit ON the measured
/// polyline through the run's subjects, redistributed along its arclength,
/// so the assistant re-times the motion the artist drew rather than
/// inventing a straighter one.
/// </para>
/// <para>
/// The subject is <see cref="MotionTrail.Locate"/>'s — authored pivot, else
/// ink-bounds centre — not <see cref="SpacingChart"/>'s centroid, because the
/// ghost is painted beside the trail's real tick and the two must not
/// disagree about where a drawing is (Q133). Extremes are never targets: they
/// are the artist's statements the spacing is measured between.
/// </para>
/// </remarks>
public static class SpacingAssistant
{
    /// <summary>A miss is flagged past this share of the run's travel…</summary>
    public const double FlagShare = 0.05;

    /// <summary>…but never under this many document pixels, so noise on a short run stays quiet.</summary>
    public const double FlagFloorPx = 1.0;

    /// <summary>
    /// Targets for every inbetween of the run the index sits in. Empty when
    /// there is no run, the run has fewer than three located drawings, or the
    /// run does not move.
    /// </summary>
    public static IReadOnlyList<SpacingTarget> TargetsForRun(
        Scene scene, Layer layer, int index, Easing easing)
    {
        var run = ExposureSheet.RunAt(layer, index);
        if (run.Count < 3) return [];

        var located = new List<(int Index, string Id, double X, double Y)>(run.Count);
        foreach (var cel in run)
        {
            if (ExposureSheet.FrameAtExactIndex(layer, cel) is not { } frame) continue;
            if (MotionTrail.Locate(scene, frame) is not { } at) continue;
            located.Add((cel, frame.Id, at.X, at.Y));
        }
        if (located.Count < 3) return [];

        // Cumulative arclength along the measured polyline.
        var cum = new double[located.Count];
        for (var j = 1; j < located.Count; j++)
        {
            var dx = located[j].X - located[j - 1].X;
            var dy = located[j].Y - located[j - 1].Y;
            cum[j] = cum[j - 1] + Math.Sqrt(dx * dx + dy * dy);
        }
        var travel = cum[^1];
        if (travel <= 0) return [];

        // The opening extreme's chart wins over the global easing when it
        // still describes this run — SpacingChart's rule, including the guard:
        // a chart authored for three inbetweens says nothing about five, and
        // a run with unlocatable drawings has lost the correspondence between
        // rungs and the drawings that remain.
        var m = located.Count - 1;
        var chart = located.Count == run.Count
                    && ExposureSheet.FrameAtExactIndex(layer, run[0]) is { Role: FrameRole.Key } opening
                    && opening.Chart is { } rungs && rungs.Count == m - 1
            ? rungs : null;

        var flagAt = Math.Max(FlagShare * travel, FlagFloorPx);
        var targets = new List<SpacingTarget>(m - 1);
        for (var j = 1; j < m; j++)
        {
            var share = chart?[j - 1] ?? EasingOps.Ease((double)j / m, easing);
            var (tx, ty) = PointAtArclength(located, cum, share * travel);
            var dx = located[j].X - tx;
            var dy = located[j].Y - ty;
            var deviation = Math.Sqrt(dx * dx + dy * dy);
            targets.Add(new SpacingTarget(
                located[j].Index, located[j].Id,
                located[j].X, located[j].Y, tx, ty,
                deviation, deviation > flagAt));
        }
        return targets;
    }

    /// <summary>The point a given arclength along the measured polyline.</summary>
    private static (double X, double Y) PointAtArclength(
        List<(int Index, string Id, double X, double Y)> pts, double[] cum, double s)
    {
        if (s <= 0) return (pts[0].X, pts[0].Y);
        for (var j = 1; j < pts.Count; j++)
        {
            if (s > cum[j]) continue;
            var span = cum[j] - cum[j - 1];
            // A zero-length segment holds still; its start is as good as its end.
            var t = span <= 0 ? 0 : (s - cum[j - 1]) / span;
            return (pts[j - 1].X + (pts[j].X - pts[j - 1].X) * t,
                    pts[j - 1].Y + (pts[j].Y - pts[j - 1].Y) * t);
        }
        return (pts[^1].X, pts[^1].Y);
    }
}
