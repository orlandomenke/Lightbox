namespace Lightbox.Core.Inbetween;

/// <summary>
/// Operations on a timing chart (Q58): the animator's ladder on an extreme,
/// saying where the inbetweens sit on the travel to the next key. The chart
/// itself is a list of fractions strictly between 0 and 1, ascending — rung
/// j is inbetween j's share of the travel. It lives on the extreme's
/// <see cref="Documents.Frame.Chart"/>, so it re-times, copies and holds
/// with the drawing it describes.
///
/// A chart is the classic pencil-era object: "1 3 5 7" on the corner of an
/// extreme is exactly this list, and it is the natural input to the
/// inbetweener — a chart with three rungs asks for three inbetweens, placed
/// where the rungs are rather than evenly.
/// </summary>
public static class TimingChart
{
    /// <summary>
    /// The closest a rung may sit to an extreme or to its neighbour. Two
    /// rungs on the same fraction would be two drawings in the same place —
    /// a stutter, not a spacing.
    /// </summary>
    public const double MinGap = 0.01;

    /// <summary>
    /// A chart fit to keep: ascending, deduplicated, everything outside
    /// (0,1) dropped. Null when nothing survives — no key is written for an
    /// empty chart, the camera's rule.
    /// </summary>
    public static List<double>? Normalise(IEnumerable<double>? rungs)
    {
        if (rungs is null) return null;
        var kept = new List<double>();
        foreach (var rung in rungs.OrderBy(r => r))
        {
            if (rung < MinGap || rung > 1 - MinGap) continue;
            if (kept.Count > 0 && rung - kept[^1] < MinGap) continue;
            kept.Add(rung);
        }
        return kept.Count > 0 ? kept : null;
    }

    /// <summary>Evenly spaced rungs — the chart the uniform default has always meant.</summary>
    public static List<double> Even(int count) => FromEasing(count, Easing.Linear);

    /// <summary>
    /// Rungs placed by an easing curve: the chart a preset button writes, so
    /// an artist starts from the shape they meant and drags from there.
    /// </summary>
    public static List<double> FromEasing(int count, Easing easing)
    {
        var rungs = new List<double>(Math.Max(0, count));
        for (var k = 1; k <= count; k++)
        {
            rungs.Add(EasingOps.Ease((double)k / (count + 1), easing));
        }
        return rungs;
    }
}
