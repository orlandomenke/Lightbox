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
    private static Frame DrawingAt(double x, double y) => new()
    {
        Strokes =
        [
            new Stroke { Points = [new StrokePoint(x - 1, y, 1), new StrokePoint(x + 1, y, 1)] },
        ],
    };

    private static Layer LayerWith(params (int Index, Frame? Frame)[] cels)
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
        var empty = new Frame();
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

/// <summary>
/// The intended chart: the run's measured travel redistributed by the easing,
/// so measured-vs-intended overlays name the drawing that misses the ease.
/// </summary>
public class IntendedSpacingTests
{
    private static Frame DrawingAt(double x, FrameRole role = FrameRole.Key) => new()
    {
        Role = role,
        Strokes = [new Stroke { Points = [new StrokePoint(x, 0, 1), new StrokePoint(x, 2, 1)] }],
    };

    private static Layer LayerWith(params Frame[] frames)
    {
        var layer = new Layer { Kind = LayerKind.Vector };
        foreach (var f in frames) layer.Cels.Add(new Cel { Frame = f });
        return layer;
    }

    [Fact]
    public void LinearIntendedSpacingIsEven()
    {
        // Extreme, two inbetweens, extreme — travelled 30 in total, drawn
        // unevenly. Linear intent: 10 per step.
        var layer = LayerWith(
            DrawingAt(0),
            DrawingAt(3, FrameRole.Inbetween),
            DrawingAt(24, FrameRole.Inbetween),
            DrawingAt(30));

        var intended = Lightbox.Core.Timeline.SpacingChart.Intended(layer, Lightbox.Core.Inbetween.Easing.Linear);

        Assert.Equal(3, intended.Count);
        Assert.All(intended, s => Assert.Equal(10, s.Distance, 3));
    }

    [Fact]
    public void EaseInIntendedSpacingWidens()
    {
        var layer = LayerWith(
            DrawingAt(0),
            DrawingAt(10, FrameRole.Inbetween),
            DrawingAt(20, FrameRole.Inbetween),
            DrawingAt(30));

        var intended = Lightbox.Core.Timeline.SpacingChart.Intended(layer, Lightbox.Core.Inbetween.Easing.EaseIn);

        Assert.True(intended[0].Distance < intended[1].Distance
                    && intended[1].Distance < intended[2].Distance,
            $"ease-in must widen: {intended[0].Distance:F1}, {intended[1].Distance:F1}, {intended[2].Distance:F1}");
        // And the travel is conserved — the intent re-spaces, never re-draws.
        Assert.Equal(30, intended.Sum(s => s.Distance), 3);
    }

    [Fact]
    public void AllExtremesMeansIntendedEqualsMeasured()
    {
        // Every drawing an extreme (the default role): each run is a single
        // interval, and there is nothing for the easing to re-space.
        var layer = LayerWith(DrawingAt(0), DrawingAt(7), DrawingAt(30));

        var measured = Lightbox.Core.Timeline.SpacingChart.Measure(layer);
        var intended = Lightbox.Core.Timeline.SpacingChart.Intended(layer, Lightbox.Core.Inbetween.Easing.EaseInOut);

        Assert.Equal(measured.Select(s => (s.Frame, s.Distance)), intended.Select(s => (s.Frame, s.Distance)));
    }
}
