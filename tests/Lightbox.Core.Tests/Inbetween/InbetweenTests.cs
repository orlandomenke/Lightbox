using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;

namespace Lightbox.Core.Tests.Inbetween;

public class MatchTests
{
    private static Stroke At(double x, double y, string? label = null, double length = 10) => new()
    {
        Label = label,
        Points = [new(x, y, 0.5), new(x + length, y, 0.5)],
    };

    [Fact]
    public void LabelMatch_BeatsProximity()
    {
        var a = new[] { At(0, 0, "arm") };
        // A nearer unlabeled stroke and a far stroke labeled "arm".
        var near = At(1, 1);
        var far = At(500, 500, "arm");
        var pairs = StrokeMatcher.Match(a, [near, far]);

        var armPair = pairs.First(p => p.A?.Label == "arm");
        Assert.Same(far, armPair.B);
    }

    [Fact]
    public void GreedyMatching_PicksNearestCentroids()
    {
        var a1 = At(0, 0);
        var a2 = At(100, 100);
        var b1 = At(2, 2);
        var b2 = At(101, 99);
        var pairs = StrokeMatcher.Match([a1, a2], [b2, b1]);

        Assert.Same(b1, pairs.First(p => p.A == a1).B);
        Assert.Same(b2, pairs.First(p => p.A == a2).B);
    }

    [Fact]
    public void LengthMismatch_InflatesCost()
    {
        var a = At(0, 0, length: 100);
        var bShortNear = At(6, 0, length: 4);
        var bLongFar = At(12, 0, length: 100);
        var pairs = StrokeMatcher.Match([a], [bShortNear, bLongFar]);
        Assert.Same(bLongFar, pairs.First(p => p.A == a).B);
    }

    [Fact]
    public void Unmatched_PairWithNull()
    {
        var pairs = StrokeMatcher.Match([At(0, 0), At(50, 50)], [At(1, 1)]);
        Assert.Contains(pairs, p => p.A is not null && p.B is null);
        Assert.Equal(2, pairs.Count);
    }

    [Fact]
    public void EmptyFrames_ProduceOnlyOneSidedPairs()
    {
        Assert.Empty(StrokeMatcher.Match([], []));
        var pairs = StrokeMatcher.Match([], [At(0, 0)]);
        var p = Assert.Single(pairs);
        Assert.Null(p.A);
        Assert.NotNull(p.B);
    }
}

public class InterpolateTests
{
    private static Stroke Line(double x0, double y0, double x1, double y1, double size = 6, string color = "#000000") => new()
    {
        Color = color,
        Brush = new BrushSettings { Size = size },
        Points = [new(x0, y0, 0.5), new(x1, y1, 0.5)],
    };

    [Fact]
    public void T0_MatchesA_T1_MatchesB()
    {
        var a = Line(0, 0, 100, 0);
        var b = Line(0, 50, 100, 50);
        var at0 = StrokeInterpolator.Interpolate(a, b, 0);
        var at1 = StrokeInterpolator.Interpolate(a, b, 1);

        Assert.All(at0.Points, p => Assert.Equal(0, p.Y, 6));
        Assert.All(at1.Points, p => Assert.Equal(50, p.Y, 6));
        Assert.Equal(StrokeInterpolator.Samples, at0.Points.Count);
    }

    [Fact]
    public void Midpoint_IsHalfway()
    {
        var a = Line(0, 0, 100, 0, size: 4, color: "#000000");
        var b = Line(0, 100, 100, 100, size: 12, color: "#ffffff");
        var mid = StrokeInterpolator.Interpolate(a, b, 0.5);

        Assert.All(mid.Points, p => Assert.Equal(50, p.Y, 6));
        Assert.Equal(8, mid.Brush.Size, 6);
        Assert.Equal("#808080", mid.Color);
    }

    [Fact]
    public void ReversedStrokeB_GetsFlipped()
    {
        var a = Line(0, 0, 100, 0);
        // Same line drawn right-to-left.
        var b = new Stroke { Points = [new(100, 10, 0.5), new(0, 10, 0.5)] };
        var mid = StrokeInterpolator.Interpolate(a, b, 0.5);

        // Without orientation fixing the stroke would collapse to its center;
        // with it, the first point stays near x=0 and the last near x=100.
        Assert.InRange(mid.Points[0].X, -1, 5);
        Assert.InRange(mid.Points[^1].X, 95, 101);
    }

    [Fact]
    public void Interpolation_IsStructurallyDeterministic()
    {
        var a = Line(0, 0, 100, 40);
        var b = Line(10, 5, 90, 80);
        var r1 = StrokeInterpolator.Interpolate(a, b, 0.3);
        var r2 = StrokeInterpolator.Interpolate(a, b, 0.3);
        Assert.Equal(r1.Points, r2.Points);
        Assert.Equal(r1.Color, r2.Color);
        Assert.Equal(r1.Brush.Size, r2.Brush.Size);
    }
}

public class InbetweenerTests
{
    private static Stroke Labeled(string label, double x, double opacity = 1) => new()
    {
        Label = label,
        Brush = new BrushSettings { Opacity = opacity },
        Points = [new(x, 0, 0.5), new(x + 10, 0, 0.5)],
    };

    [Fact]
    public void MatchedStrokes_Interpolate()
    {
        var frame = Inbetweener.Inbetween(
            [Labeled("a", 0)],
            [Labeled("a", 100)],
            0.5,
            Easing.Linear);
        var s = Assert.Single(frame);
        Assert.Equal(50, s.Points[0].X, 6);
    }

    [Fact]
    public void UnmatchedA_FadesOutInFirstHalf()
    {
        var a = new[] { Labeled("gone", 0) };
        var b = Array.Empty<Stroke>();

        var early = Inbetweener.Inbetween(a, b, 0.25, Easing.Linear);
        var s = Assert.Single(early);
        Assert.Equal(0.5, s.Brush.Opacity, 6);

        var late = Inbetweener.Inbetween(a, b, 0.6, Easing.Linear);
        Assert.Empty(late);
    }

    [Fact]
    public void UnmatchedB_FadesInSecondHalf()
    {
        var a = Array.Empty<Stroke>();
        var b = new[] { Labeled("new", 0) };

        Assert.Empty(Inbetweener.Inbetween(a, b, 0.4, Easing.Linear));

        var late = Inbetweener.Inbetween(a, b, 0.75, Easing.Linear);
        var s = Assert.Single(late);
        Assert.Equal(0.5, s.Brush.Opacity, 6);
    }

    [Fact]
    public void Easing_ShiftsParameter()
    {
        var a = new[] { Labeled("a", 0) };
        var b = new[] { Labeled("a", 100) };
        var linear = Inbetweener.Inbetween(a, b, 0.25, Easing.Linear)[0].Points[0].X;
        var easeIn = Inbetweener.Inbetween(a, b, 0.25, Easing.EaseIn)[0].Points[0].X;
        Assert.True(easeIn < linear); // easeIn starts slower
        Assert.Equal(6.25, easeIn, 6); // 0.25^2 * 100
    }

    [Fact]
    public void InbetweenSeries_ProducesEvenlySpacedTs()
    {
        var a = new[] { Labeled("a", 0) };
        var b = new[] { Labeled("a", 100) };
        var series = Inbetweener.InbetweenSeries(a, b, 3, Easing.Linear);
        Assert.Equal(3, series.Count);
        Assert.Equal(25, series[0][0].Points[0].X, 6);
        Assert.Equal(50, series[1][0].Points[0].X, 6);
        Assert.Equal(75, series[2][0].Points[0].X, 6);
    }

    [Theory]
    [InlineData(Easing.Linear)]
    [InlineData(Easing.EaseIn)]
    [InlineData(Easing.EaseOut)]
    [InlineData(Easing.EaseInOut)]
    public void EasingFunctions_PinEndpoints(Easing kind)
    {
        Assert.Equal(0, EasingOps.Ease(0, kind), 9);
        Assert.Equal(1, EasingOps.Ease(1, kind), 9);
    }
}
