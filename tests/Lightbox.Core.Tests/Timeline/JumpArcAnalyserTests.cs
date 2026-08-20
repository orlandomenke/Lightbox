using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using Xunit;

namespace Lightbox.Core.Tests.Timeline;

/// <summary>
/// The gravity fit (Q133): points on a parabola fit with no offenders, the
/// drawing off the arc is named with both positions, an arc that never comes
/// down is called not ballistic, and the fit is scoped to the playhead's run.
/// </summary>
public class JumpArcAnalyserTests
{
    private static Frame DrawingAt(double x, double y) => new()
    {
        Strokes =
        [
            new Stroke { Points = [new StrokePoint(x - 5, y - 5, 1), new StrokePoint(x + 5, y + 5, 1)] },
        ],
    };

    /// <summary>A single run through the given subject positions.</summary>
    private static Layer Run(params (double X, double Y)[] at)
    {
        var layer = new Layer { Name = "L", Kind = LayerKind.Painted };
        foreach (var (x, y) in at) layer.Cels.Add(new Cel { Frame = DrawingAt(x, y) });
        for (var i = 1; i < layer.Cels.Count - 1; i++)
            layer.Cels[i].Frame!.Role = FrameRole.Inbetween;
        return layer;
    }

    /// <summary>y(t) for a jump: up fast, apex, down — gravity of 8 px/frame².</summary>
    private static double Ballistic(double t) => 200 - 40 * t + 4 * t * t;

    [Fact]
    public void PointsOnAParabolaFitWithNoOffenders()
    {
        var layer = Run(Enumerable.Range(0, 6)
            .Select(t => (20.0 * t, Ballistic(t))).ToArray());

        var fit = JumpArcAnalyser.FitRun(new Scene(), layer, 0);

        Assert.NotNull(fit);
        Assert.True(fit.Ballistic);
        Assert.All(fit.Deviations, d => Assert.False(d.OffArc, $"frame {d.Index}: {d.Distance}"));
        Assert.All(fit.Deviations, d => Assert.True(d.Distance < 1e-6));
    }

    [Fact]
    public void ADrawingOffTheArcIsNamed()
    {
        var points = Enumerable.Range(0, 6)
            .Select(t => (20.0 * t, Ballistic(t))).ToArray();
        points[3] = (points[3].Item1, points[3].Item2 - 40);
        var layer = Run(points);

        var fit = JumpArcAnalyser.FitRun(new Scene(), layer, 0);

        Assert.NotNull(fit);
        var offender = Assert.Single(fit.Deviations, d => d.OffArc);
        Assert.Equal(3, offender.Index);
        // Both positions carried, so the overlay can draw actual and fitted
        // and the readout can print the gap rather than a bare flag.
        Assert.True(offender.Distance > 10);
        Assert.True(offender.Y < offender.FitY);
    }

    [Fact]
    public void AnArcThatNeverComesDownIsNotBallistic()
    {
        // Y shrinking quadratically — the subject accelerates upward, which
        // no thrown thing does. The fit succeeds; the verdict says what it is.
        var layer = Run(Enumerable.Range(0, 6)
            .Select(t => (20.0 * t, 400.0 - 4 * t * t)).ToArray());

        var fit = JumpArcAnalyser.FitRun(new Scene(), layer, 0);

        Assert.NotNull(fit);
        Assert.False(fit.Ballistic);
    }

    [Fact]
    public void AShortRunRefusesToJudge()
    {
        // Three points fit any parabola exactly — a verdict needs a fourth.
        var layer = Run((0, 100), (20, 60), (40, 100));

        Assert.Null(JumpArcAnalyser.FitRun(new Scene(), layer, 0));
    }

    [Fact]
    public void TheFitIsTheRunAtThePlayheadNotTheWholeLayer()
    {
        // A jump then a slide, split by an extreme: fitting both as one
        // parabola is meaningless, which is why the run rule exists.
        var jump = Enumerable.Range(0, 5).Select(t => (20.0 * t, Ballistic(t)));
        var slide = Enumerable.Range(0, 5).Select(t => (100.0 + 10 * t, 300.0));
        var layer = Run(jump.Concat(slide).ToArray());
        layer.Cels[4].Frame!.Role = FrameRole.Key;

        var fit = JumpArcAnalyser.FitRun(new Scene(), layer, 1);

        Assert.NotNull(fit);
        Assert.Equal(5, fit.Deviations.Count);
        Assert.Equal(4, fit.Deviations[^1].Index);
        Assert.All(fit.Deviations, d => Assert.False(d.OffArc));
    }

    [Fact]
    public void TheCurveRunsTheWholeSpanForThePainter()
    {
        var layer = Run(Enumerable.Range(0, 5)
            .Select(t => (20.0 * t, Ballistic(t))).ToArray());

        var fit = JumpArcAnalyser.FitRun(new Scene(), layer, 0);

        Assert.NotNull(fit);
        Assert.Equal(0, fit.Curve[0].X, 6);
        Assert.Equal(80, fit.Curve[^1].X, 6);
        // Sampled finer than the drawings, or the "curve" is the trail again.
        Assert.True(fit.Curve.Count > fit.Deviations.Count);
    }
}
