using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Timeline;
using Xunit;

namespace Lightbox.Core.Tests.Timeline;

/// <summary>
/// The acting half of the spacing chart (Q134): targets sit on the measured
/// path at the intended fractions, extremes are never moved, and the flag
/// threshold separates a drawing that pops from arithmetic noise.
/// </summary>
public class SpacingAssistantTests
{
    /// <summary>A drawing whose ink centres at (x, 50).</summary>
    private static Frame DrawingAt(double x) => new()
    {
        Strokes =
        [
            new Stroke { Points = [new StrokePoint(x - 10, 40, 1), new StrokePoint(x + 10, 60, 1)] },
        ],
    };

    /// <summary>
    /// One run: extremes at both ends, inbetweens between — the default role
    /// is Key, and a sheet of nothing but extremes has no inbetween to target.
    /// </summary>
    private static Layer RowAt(params double[] xs)
    {
        var layer = new Layer { Name = "L", Kind = LayerKind.Painted };
        foreach (var x in xs) layer.Cels.Add(new Cel { Frame = DrawingAt(x) });
        for (var i = 1; i < layer.Cels.Count - 1; i++)
            layer.Cels[i].Frame!.Role = FrameRole.Inbetween;
        return layer;
    }

    [Fact]
    public void AnEvenRunUnderALinearEaseMissesNothing()
    {
        var layer = RowAt(0, 25, 50, 75, 100);

        var targets = SpacingAssistant.TargetsForRun(new Scene(), layer, 0, Easing.Linear);

        Assert.Equal(3, targets.Count);
        Assert.All(targets, t => Assert.False(t.Misses));
        Assert.All(targets, t => Assert.True(t.Deviation < 1e-9, $"deviation {t.Deviation}"));
    }

    [Fact]
    public void ABunchedDrawingIsFlaggedAndItsTargetSitsOnTheMeasuredPath()
    {
        // The middle drawing crowds its neighbour: 0, 40, 50, 100 under a
        // linear intent wants 0, 33.3, 66.7, 100.
        var layer = RowAt(0, 40, 90, 100);

        var targets = SpacingAssistant.TargetsForRun(new Scene(), layer, 0, Easing.Linear);

        Assert.Equal(2, targets.Count);
        var second = targets[1];
        Assert.True(second.Misses);
        // The path is a straight line along y = 50, so the target stays on it.
        Assert.Equal(50, second.TargetY, 6);
        Assert.Equal(100.0 * 2 / 3, second.TargetX, 6);
        // Both numbers printed, the measuring rule: the miss is the gap
        // between them, not a bare boolean.
        Assert.Equal(90, second.X);
        Assert.True(second.Deviation > 20);
    }

    [Fact]
    public void TheExtremesOwnChartBeatsTheGlobalEasing()
    {
        var layer = RowAt(0, 50, 100);
        // A chart asking for the single inbetween at 80% of the travel: the
        // drawing at 50 is 30 px short of where the chart wants it.
        layer.Cels[0].Frame!.Chart = [0.8];

        var target = Assert.Single(
            SpacingAssistant.TargetsForRun(new Scene(), layer, 0, Easing.Linear));

        Assert.Equal(80, target.TargetX, 6);
        Assert.True(target.Misses);
    }

    [Fact]
    public void AChartForTheWrongCountFallsBackToTheEasing()
    {
        var layer = RowAt(0, 50, 100);
        // Authored for three inbetweens, describing a run that has one.
        layer.Cels[0].Frame!.Chart = [0.2, 0.5, 0.8];

        var target = Assert.Single(
            SpacingAssistant.TargetsForRun(new Scene(), layer, 0, Easing.Linear));

        Assert.Equal(50, target.TargetX, 6);
        Assert.False(target.Misses);
    }

    [Fact]
    public void TheEndpointsAreNeverTargets()
    {
        var layer = RowAt(0, 10, 100);

        var targets = SpacingAssistant.TargetsForRun(new Scene(), layer, 0, Easing.Linear);

        Assert.DoesNotContain(targets, t => t.Index == 0);
        Assert.DoesNotContain(targets, t => t.Index == 2);
    }

    [Fact]
    public void AMotionlessRunSaysNothing()
    {
        // Three drawings in the same place have no travel to redistribute —
        // an assistant that flagged them would be asking for motion nobody drew.
        var layer = RowAt(50, 50, 50);

        Assert.Empty(SpacingAssistant.TargetsForRun(new Scene(), layer, 0, Easing.Linear));
    }

    [Fact]
    public void TheRunIsThePlayheadsNotTheWholeLayer()
    {
        // Two runs split by an extreme in the middle; asking inside the second
        // run must target its inbetween, not the first run's.
        var layer = RowAt(0, 10, 40, 90, 100);
        foreach (var cel in layer.Cels) cel.Frame!.Role = FrameRole.Inbetween;
        layer.Cels[0].Frame!.Role = FrameRole.Key;
        layer.Cels[2].Frame!.Role = FrameRole.Key;
        layer.Cels[4].Frame!.Role = FrameRole.Key;

        var second = SpacingAssistant.TargetsForRun(new Scene(), layer, 3, Easing.Linear);

        var target = Assert.Single(second);
        Assert.Equal(3, target.Index);
        Assert.Equal(70, target.TargetX, 6);
    }
}
