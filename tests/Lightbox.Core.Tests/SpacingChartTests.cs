using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// The measured spacing chart: displacement of the ink between consecutive
/// drawings, off the stroke record (Q31).
/// </summary>
public class SpacingChartTests
{
    private static VectorFrame DrawingAt(double x, double y) => new()
    {
        Strokes =
        [
            new Stroke { Points = [new StrokePoint(x - 1, y, 1), new StrokePoint(x + 1, y, 1)] },
        ],
    };

    private static Layer LayerWith(params (int Index, VectorFrame? Frame)[] cels)
    {
        var layer = new Layer { Kind = LayerKind.Vector };
        var count = cels.Max(c => c.Index) + 1;
        for (var i = 0; i < count; i++) layer.Cels.Add(new Cel());
        foreach (var (index, frame) in cels) layer.Cels[index].Frame = frame;
        return layer;
    }

    [Fact]
    public void EvenSpacingMeasuresEven()
    {
        // Drawings marching 10 units per step: the chart reads flat.
        var layer = LayerWith((0, DrawingAt(0, 0)), (2, DrawingAt(10, 0)), (4, DrawingAt(20, 0)));

        var spans = SpacingChart.Measure(layer);

        Assert.Equal(2, spans.Count);
        Assert.Equal(2, spans[0].Frame);
        Assert.Equal(10, spans[0].Distance, 3);
        Assert.Equal(4, spans[1].Frame);
        Assert.Equal(10, spans[1].Distance, 3);
    }

    [Fact]
    public void AnEaseReadsAsWideningSpacing()
    {
        // 2, then 6, then 18 units — accelerating out of a pose. The numbers
        // ARE the chart; this is what an animator reads off it.
        var layer = LayerWith(
            (0, DrawingAt(0, 0)), (1, DrawingAt(2, 0)), (2, DrawingAt(8, 0)), (3, DrawingAt(26, 0)));

        var spans = SpacingChart.Measure(layer);

        Assert.Equal(3, spans.Count);
        Assert.True(spans[0].Distance < spans[1].Distance && spans[1].Distance < spans[2].Distance,
            $"spacing {spans[0].Distance:F1}, {spans[1].Distance:F1}, {spans[2].Distance:F1} does not widen");
    }

    [Fact]
    public void HoldsDoNotMeasureAsMovement()
    {
        // Only keyed drawings are measured — a hold is the same drawing, and
        // "the drawing did not move" is not a data point, it is a hold.
        var layer = LayerWith((0, DrawingAt(0, 0)), (5, DrawingAt(12, 0)));

        var spans = SpacingChart.Measure(layer);

        var span = Assert.Single(spans);
        Assert.Equal(5, span.Frame);
        Assert.Equal(12, span.Distance, 3);
    }

    [Fact]
    public void AnEmptyDrawingIsSkippedRatherThanMeasuredAsZero()
    {
        var empty = new VectorFrame();
        var layer = LayerWith((0, DrawingAt(0, 0)), (1, empty), (2, DrawingAt(10, 0)));

        var spans = SpacingChart.Measure(layer);

        // One span, straight across the inkless drawing.
        var span = Assert.Single(spans);
        Assert.Equal(2, span.Frame);
        Assert.Equal(10, span.Distance, 3);
    }

    [Fact]
    public void NoDrawingsMeansNoChart()
    {
        Assert.Empty(SpacingChart.Measure(LayerWith((3, null))));
    }
}
