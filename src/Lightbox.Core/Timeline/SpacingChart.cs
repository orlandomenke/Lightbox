using Lightbox.Core.Documents;

namespace Lightbox.Core.Timeline;

/// <summary>
/// The measured spacing of the animation: how far the artwork actually moves
/// between consecutive drawings on a layer.
/// </summary>
/// <remarks>
/// <para>
/// This is the differentiator the stroke record buys (Q54). Every graph
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
        var keyed = new List<(int Index, double X, double Y)>();
        for (var i = 0; i < layer.Cels.Count; i++)
        {
            if (ExposureSheet.FrameAtExactIndex(layer, i) is not { } frame) continue;
            if (Centroid(frame) is not { } c) continue;
            keyed.Add((i, c.X, c.Y));
        }

        var spans = new List<Span>(Math.Max(0, keyed.Count - 1));
        for (var k = 1; k < keyed.Count; k++)
        {
            var dx = keyed[k].X - keyed[k - 1].X;
            var dy = keyed[k].Y - keyed[k - 1].Y;
            spans.Add(new Span(keyed[k].Index, Math.Sqrt(dx * dx + dy * dy)));
        }
        return spans;
    }

    /// <summary>The mean of every stroke point in the drawing, or null when there is no ink.</summary>
    internal static (double X, double Y)? Centroid(Frame frame)
    {
        var strokes = frame switch
        {
            VectorFrame v => v.Strokes,
            PaintedFrame p => p.Strokes,
            _ => null,
        };
        if (strokes is null) return null;

        double x = 0, y = 0;
        var n = 0;
        foreach (var stroke in strokes)
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
