using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Xunit;

namespace Lightbox.Core.Tests.Geometry;

/// <summary>
/// The perspective checker (Q135). The load-bearing distinction is the two
/// bands: a line INSIDE the convergence band is right, a line OUTSIDE the
/// alignment band is about something else, and only the ring between them —
/// nearly aiming, and missing — is a finding. And the artist's own vanishing
/// points always win: inference only runs where none are declared, and the
/// report says when it did.
/// </summary>
public class PerspectiveCheckerTests
{
    private static Stroke LineFrom(double ax, double ay, double bx, double by) => new()
    {
        Points =
        [
            new StrokePoint(ax, ay, 1),
            new StrokePoint((ax + bx) / 2, (ay + by) / 2, 1),
            new StrokePoint(bx, by, 1),
        ],
    };

    private static Scene WithVp(double x, double y, string? name = null)
    {
        var scene = new Scene();
        scene.Guides = [new Guide { Kind = GuideKind.VanishingPoint, X = x, Y = y, Name = name }];
        return scene;
    }

    [Fact]
    public void ALineAimedNearButNotAtThePointIsTheFinding()
    {
        var frame = new Frame
        {
            Strokes =
            [
                LineFrom(0, 100, 100, 100),    // dead on the point: converges
                LineFrom(0, 140, 100, 133),    // ~4° off aiming at it: the miss
                LineFrom(50, 0, 50, 100),      // vertical, about something else
            ],
        };

        var report = PerspectiveChecker.Check(WithVp(300, 100, "east"), frame);

        Assert.Equal(3, report.StraightLines);
        Assert.False(report.Inferred);
        var finding = Assert.Single(report.Findings);
        Assert.Equal(frame.Strokes[1].Id, finding.StrokeId);
        Assert.Equal("east", finding.VpName);
        // Both numbers, the measuring rule: the miss is a few degrees, inside
        // the alignment band and outside the convergence band.
        Assert.InRange(finding.OffByDeg, PerspectiveChecker.ConvergeBandDeg, PerspectiveChecker.AlignBandDeg);
    }

    [Fact]
    public void ACurveIsNotALine()
    {
        var bowed = LineFrom(0, 100, 100, 100);
        bowed.Points[1] = new StrokePoint(50, 80, 1);
        var frame = new Frame { Strokes = [bowed] };

        var report = PerspectiveChecker.Check(WithVp(300, 100), frame);

        Assert.Equal(0, report.StraightLines);
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void AShortTickAimsAtNothing()
    {
        var frame = new Frame { Strokes = [LineFrom(0, 100, 10, 99)] };

        Assert.Equal(0, PerspectiveChecker.Check(WithVp(300, 100), frame).StraightLines);
    }

    [Fact]
    public void WithNoDeclaredPointTheCheckerInfersOneAndSaysSo()
    {
        // Three lines agree on (200, 100); the fourth nearly aims and misses.
        var frame = new Frame
        {
            Strokes =
            [
                LineFrom(0, 0, 100, 50),
                LineFrom(0, 200, 100, 150),
                LineFrom(0, 100, 100, 100),
                LineFrom(0, 60, 100, 73),
            ],
        };

        var report = PerspectiveChecker.Check(new Scene(), frame);

        Assert.True(report.Inferred);
        Assert.Equal(1, report.VanishingPoints);
        var finding = Assert.Single(report.Findings);
        Assert.Equal(frame.Strokes[3].Id, finding.StrokeId);
        Assert.True(finding.Inferred);
        Assert.Contains("inferred", finding.VpName);
    }

    [Fact]
    public void TheArtistsOwnPointSilencesInference()
    {
        // The same converging sketch, but a declared point far from where the
        // ink agrees: the ink is judged against the artist's statement alone.
        var frame = new Frame
        {
            Strokes =
            [
                LineFrom(0, 0, 100, 50),
                LineFrom(0, 200, 100, 150),
                LineFrom(0, 100, 100, 100),
            ],
        };

        var report = PerspectiveChecker.Check(WithVp(0, -500), frame);

        Assert.False(report.Inferred);
        Assert.Equal(1, report.VanishingPoints);
    }

    [Fact]
    public void TooFewAgreeingLinesInferNothing()
    {
        // Two lines cross somewhere by construction — a candidate needs three,
        // or every X in a sketch becomes a vanishing point.
        var frame = new Frame
        {
            Strokes =
            [
                LineFrom(0, 0, 100, 50),
                LineFrom(0, 200, 100, 150),
            ],
        };

        var report = PerspectiveChecker.Check(new Scene(), frame);

        Assert.Equal(0, report.VanishingPoints);
        Assert.False(report.Inferred);
        Assert.Empty(report.Findings);
    }
}
