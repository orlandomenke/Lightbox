using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests.Inbetween;

/// <summary>
/// Arcs (Q272): a line between two keys turns about its joint — the end that
/// moves least — instead of every point sliding in a straight line, so a limb
/// keeps its length through a swing. At the keys it is exactly the drawing.
/// </summary>
public class ArcTests(ITestOutputHelper output)
{
    private static Stroke Line(params (double X, double Y)[] points) => new()
    {
        Points = [.. points.Select(p => new StrokePoint(p.X, p.Y, 0.8))],
        Brush = new BrushSettings { Size = 6 },
    };

    /// <summary>A thigh 200 long, hanging from a hip at (100, 100), swung from straight down to straight forward.</summary>
    private static (Stroke Down, Stroke Forward) Swing() =>
        (Line((100, 100), (100, 200), (100, 300)), Line((100, 100), (200, 100), (300, 100)));

    [Fact]
    public void ALimbSwingingAboutItsJointKeepsItsLength()
    {
        var (down, forward) = Swing();

        var half = StrokeInterpolator.Interpolate(down, forward, 0.5);

        var length = GeometryOps.PathLength(half.Points);
        output.WriteLine($"halfway length {length:0.0} (the keys: 200; a straight-line tween: {200 * Math.Sqrt(0.5):0.0})");
        Assert.InRange(length, 196, 204);
    }

    [Fact]
    public void ItTurnsAboutTheEndThatMovesLeast()
    {
        var (down, forward) = Swing();

        var half = StrokeInterpolator.Interpolate(down, forward, 0.5);

        // The hip stays put; the foot is on the circle, halfway round.
        var hip = half.Points.MinBy(p => GeometryOps.Dist(p, new StrokePoint(100, 100, 0)))!;
        Assert.True(GeometryOps.Dist(hip, new StrokePoint(100, 100, 0)) < 1);
        var foot = half.Points.MaxBy(p => GeometryOps.Dist(p, new StrokePoint(100, 100, 0)))!;
        var expected = new StrokePoint(100 + 200 * Math.Sin(Math.PI / 4), 100 + 200 * Math.Cos(Math.PI / 4), 0);
        output.WriteLine($"foot at ({foot.X:0.0}, {foot.Y:0.0}), on the arc at ({expected.X:0.0}, {expected.Y:0.0})");
        Assert.True(GeometryOps.Dist(foot, expected) < 3);
    }

    [Fact]
    public void AtTheKeysItIsExactlyTheDrawing()
    {
        var (down, forward) = Swing();

        var start = StrokeInterpolator.Interpolate(down, forward, 0);
        var end = StrokeInterpolator.Interpolate(down, forward, 1);

        var a = GeometryOps.Resample(down.Points, StrokeInterpolator.Samples);
        var b = GeometryOps.Resample(forward.Points, StrokeInterpolator.Samples);
        for (var i = 0; i < a.Count; i++)
        {
            Assert.True(GeometryOps.Dist(start.Points[i], a[i]) < 1e-6);
            Assert.True(GeometryOps.Dist(end.Points[i], b[i]) < 1e-6);
        }
    }

    [Fact]
    public void ALineThatOnlyMovesMovesAsItAlwaysDid()
    {
        var a = Line((100, 100), (200, 120), (300, 100));
        var b = Line((140, 160), (240, 180), (340, 160));

        var half = StrokeInterpolator.Interpolate(a, b, 0.5);

        var expected = Line((120, 130), (220, 150), (320, 130));
        var ra = GeometryOps.Resample(expected.Points, StrokeInterpolator.Samples);
        for (var i = 0; i < ra.Count; i++) Assert.True(GeometryOps.Dist(half.Points[i], ra[i]) < 1e-6);
    }

    [Fact]
    public void AShapeThatChangesStillArrivesAtTheOtherKeysShape()
    {
        // A bend that straightens while it turns: the turn is the arc, the
        // straightening is blended on top, and the last frame is key B.
        var a = Line((100, 100), (130, 200), (100, 300));
        var b = Line((100, 100), (200, 100), (300, 100));

        var almost = StrokeInterpolator.Interpolate(a, b, 0.999);

        Assert.True(almost.Points.Max(p => Math.Abs(p.Y - 100)) < 1);
    }

    [Fact]
    public void AMatchedLineInPiecesSwingsToo()
    {
        // The same swing, key B's line drawn as two pieces: Match lines' path.
        var (down, _) = Swing();
        List<Stroke> forward = [Line((100, 100), (200, 100)), Line((200, 100), (300, 100))];

        var half = MatchedInbetween.Inbetween([down], forward, [new PartMatch([0], [0, 1], Solid: false)], 0.5, Easing.Linear);

        var length = half.Sum(s => GeometryOps.PathLength(s.Points));
        output.WriteLine($"halfway length {length:0.0}");
        Assert.InRange(length, 192, 204);
    }

    /// <summary>
    /// A leg with its foot planted: the thigh swings forward from the hip, the
    /// shin turns about the ankle. Each line on its own would put the knee on
    /// its own arc, and they would part (the art-director, 2026-10-14).
    /// </summary>
    [Fact]
    public void TwoLinesThatMeetAtBothKeysStillMeetBetweenThem()
    {
        var thighA = Line((100, 100), (100, 150), (100, 200));
        var shinA = Line((100, 200), (100, 250), (100, 300));
        var knee = (X: 100 + 100 * Math.Sin(Math.PI / 4), Y: 100 + 100 * Math.Cos(Math.PI / 4));
        var thighB = Line((100, 100), ((100 + knee.X) / 2, (100 + knee.Y) / 2), (knee.X, knee.Y));
        var shinB = Line((knee.X, knee.Y), ((knee.X + 100) / 2, (knee.Y + 300) / 2), (100, 300));

        var half = Inbetweener.Inbetween([thighA, shinA], [thighB, shinB], 0.5, Easing.Linear);

        var thigh = half.Single(s => GeometryOps.Dist(s.Points[0], new StrokePoint(100, 100, 0)) < 2);
        var shin = half.Single(s => s != thigh);
        var gap = new[] { shin.Points[0], shin.Points[^1] }.Min(p => GeometryOps.Dist(p, thigh.Points[^1]));
        output.WriteLine($"knee gap {gap:0.00}px; thigh {GeometryOps.PathLength(thigh.Points):0.0}, shin {GeometryOps.PathLength(shin.Points):0.0}");
        Assert.True(gap < 1, $"the knee came apart by {gap:0.0}px");
        // The hip and the planted ankle stay where they are.
        Assert.True(GeometryOps.Dist(thigh.Points[0], new StrokePoint(100, 100, 0)) < 1);
        Assert.Contains(new[] { shin.Points[0], shin.Points[^1] }, p => GeometryOps.Dist(p, new StrokePoint(100, 300, 0)) < 1);
    }

    [Fact]
    public void AThrownRodKeepsItsLengthAndItsMiddleNearlyOnAStraightPath()
    {
        // A club thrown across, turning a quarter turn on the way.
        var a = Line((100, 200), (200, 200));
        var b = Line((350, 150), (350, 250));

        var half = StrokeInterpolator.Interpolate(a, b, 0.5);

        var middle = half.Points[half.Points.Count / 2];
        output.WriteLine($"middle at ({middle.X:0.0}, {middle.Y:0.0}); length {GeometryOps.PathLength(half.Points):0.0}");
        // Its ends do not move alike (255 and 158px for a quarter turn in flight),
        // so the joint sits between the nearer end and the middle: within a few
        // pixels of the straight path, measured 4.2.
        Assert.True(GeometryOps.Dist(middle, new StrokePoint(250, 200, 0)) < 6);
        Assert.InRange(GeometryOps.PathLength(half.Points), 97, 103);
    }

    [Fact]
    public void AJointDoesNotJumpWhenTheTwoEndsMoveNearlyAlike()
    {
        // A bar turning 10 degrees about its middle while its ends move the same
        // 8.8px; the second take nudges one end of key B by a pixel. Picking
        // "the end that moves least" flipped the joint on that pixel; a blend
        // gives nearly the same drawing.
        var a = Line((100, 200), (200, 200));
        var b1 = Line((101.63, 191.39), (200.11, 208.76));
        var b2 = Line((102.63, 191.39), (200.11, 208.76));

        var h1 = StrokeInterpolator.Interpolate(a, b1, 0.5);
        var h2 = StrokeInterpolator.Interpolate(a, b2, 0.5);

        var worst = h1.Points.Zip(h2.Points).Max(z => GeometryOps.Dist(z.First, z.Second));
        output.WriteLine($"two takes differ by at most {worst:0.0}px");
        Assert.True(worst < 3, $"{worst:0.0}px");
    }

    [Fact]
    public void ALineTurningAboutItsMiddleKeepsItsMiddleStill()
    {
        // The golden set's cross: a bar turning a quarter turn about its
        // centre (the ai-engineer, 2026-10-14). Hung from an end, its middle
        // drifted 20px and a model's correct answer was refused.
        var a = Line((60, 128), (196, 128));
        var b = Line((128, 60), (128, 196));

        var half = StrokeInterpolator.Interpolate(a, b, 0.5);

        var middle = GeometryOps.Centroid(half.Points);
        output.WriteLine($"middle at ({middle.X:0.0}, {middle.Y:0.0}), length {GeometryOps.PathLength(half.Points):0.0}");
        Assert.True(GeometryOps.Dist(middle, new StrokePoint(128, 128, 0)) < 2);
        Assert.InRange(GeometryOps.PathLength(half.Points), 130, 142);
    }
}
