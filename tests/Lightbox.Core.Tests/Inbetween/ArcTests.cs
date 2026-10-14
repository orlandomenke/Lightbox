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
}
