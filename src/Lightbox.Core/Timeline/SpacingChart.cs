using Lightbox.Core.Documents;

namespace Lightbox.Core.Timeline;

/// <summary>
/// The measured spacing of the animation: how far the artwork actually moves
/// between consecutive drawings on a layer.
/// </summary>
/// <remarks>
/// <para>
/// This is the differentiator the stroke record buys (Q58). Every graph
/// editor in the field plots <em>transform</em> curves — camera moves, peg
/// positions. None of them can plot the drawings, because in a raster tool a
/// drawing is pixels. Here a drawing is replayable geometry, so the spacing
/// chart animators used to draw in the margin of the paper can be measured
/// off the art itself: even spacing reads as constant speed, widening reads
/// as acceleration, and a spike names the drawing that pops.
/// </para>
/// <para>
/// The measure is the displacement of the ink's centroid between drawings.
/// Deliberately simple: a centroid is stable under line-weight noise and
/// costs one pass over the points, and what the chart is FOR is seeing the
/// shape of the spacing, not simulating physics. Empty drawings (no ink) are
/// skipped rather than measured as zero.
/// </para>
/// </remarks>
public static class SpacingChart
{
    /// <summary>One measured interval: the later drawing's frame, and how far the ink moved to reach it.</summary>
    public readonly record struct Span(int Frame, double Distance);

    /// <summary>Spacing between each consecutive pair of keyed drawings on the layer.</summary>
    public static IReadOnlyList<Span> Measure(Layer layer)
    {
        var keyed = KeyedDrawings(layer);
        var spans = new List<Span>(Math.Max(0, keyed.Count - 1));
        for (var k = 1; k < keyed.Count; k++)
        {
            spans.Add(new Span(keyed[k].Index, Distance(keyed[k - 1], keyed[k])));
        }
        return spans;
    }

    /// <summary>
    /// What the spacing SHOULD be under the given easing: each run between
    /// extremes (drawings whose role is Key) keeps its measured total travel,
    /// redistributed along the easing curve. Laid over the measured chart,
    /// the gap between the two is the drawing that misses the ease — which is
    /// what makes the chart actionable rather than merely legible.
    /// </summary>
    /// <remarks>
    /// A run with one interval, or a sheet where every drawing is an extreme
    /// (the default role), redistributes into itself — intended equals
    /// measured, honestly, because with no inbetweens there is nothing the
    /// easing could re-space.
    /// </remarks>
    public static IReadOnlyList<Span> Intended(Layer layer, Inbetween.Easing easing)
    {
        var keyed = KeyedDrawings(layer);
        var spans = new List<Span>(Math.Max(0, keyed.Count - 1));
        var runStart = 0;
        for (var k = 1; k < keyed.Count; k++)
        {
            // A run closes at the next extreme, or at the end of the sheet.
            if (keyed[k].Role != FrameRole.Key && k != keyed.Count - 1) continue;

            var m = k - runStart;
            double total = 0;
            for (var j = runStart + 1; j <= k; j++) total += Distance(keyed[j - 1], keyed[j]);
            // The opening extreme's timing chart wins over the global easing
            // (Q58) — it IS this run's intended spacing, provided it still
            // describes this run: a chart authored for three inbetweens says
            // nothing about a run that now holds five.
            var chart = keyed[runStart].Role == FrameRole.Key
                        && keyed[runStart].Chart is { } rungs && rungs.Count == m - 1
                ? rungs : null;
            for (var j = 1; j <= m; j++)
            {
                var share = chart is null
                    ? Inbetween.EasingOps.Ease((double)j / m, easing)
                      - Inbetween.EasingOps.Ease((double)(j - 1) / m, easing)
                    : (j == m ? 1 : chart[j - 1]) - (j == 1 ? 0 : chart[j - 2]);
                spans.Add(new Span(keyed[runStart + j].Index, total * share));
            }
            runStart = k;
        }
        return spans;
    }

    private static double Distance(
        (int Index, double X, double Y, FrameRole Role, List<double>? Chart) a,
        (int Index, double X, double Y, FrameRole Role, List<double>? Chart) b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static List<(int Index, double X, double Y, FrameRole Role, List<double>? Chart)> KeyedDrawings(Layer layer)
    {
        var keyed = new List<(int, double, double, FrameRole, List<double>?)>();
        for (var i = 0; i < layer.Cels.Count; i++)
        {
            if (ExposureSheet.FrameAtExactIndex(layer, i) is not { } frame) continue;
            if (Centroid(frame) is not { } c) continue;
            keyed.Add((i, c.X, c.Y, frame.Role, frame.Chart));
        }
        return keyed;
    }

    /// <summary>The mean of every stroke point in the drawing, or null when there is no ink.</summary>
    internal static (double X, double Y)? Centroid(Frame frame)
    {
        double x = 0, y = 0;
        var n = 0;
        foreach (var stroke in frame.Strokes)
        {
            foreach (var point in stroke.Points)
            {
                x += point.X;
                y += point.Y;
                n++;
            }
        }
        return n == 0 ? null : (x / n, y / n);
    }
}
