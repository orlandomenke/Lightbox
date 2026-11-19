using Lightbox.Core.Cleanup;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.Core.Tests.Cleanup;

/// <summary>
/// The engine side of the AI line review (Q296–Q298): fixes named on a numbered
/// sheet, cited against the keys, made inside the engine's own bounds.
/// </summary>
public class LineReviewTests
{
    private static Stroke Line(double x0, double y0, double x1, double y1, double size = 3)
    {
        var n = (int)Math.Ceiling(Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)));
        return new Stroke
        {
            Brush = new BrushSettings { Size = size },
            Points = [.. Enumerable.Range(0, n + 1).Select(i => new StrokePoint(x0 + (x1 - x0) * i / n, y0 + (y1 - y0) * i / n, 0.8))],
        };
    }

    /// <summary>
    /// The owner's knee, made small: the front of the knee (L1) runs down past
    /// the top of the shin (L2), the two side by side for 30 px. The keys draw
    /// the knee meeting the shin at one point.
    /// </summary>
    private static (Stroke[] Inbetween, Stroke[] KeyA, Stroke[] KeyB) Knee()
    {
        var knee = Line(100, 0, 100, 130);    // ends at y=130
        var shin = Line(104, 100, 104, 300);  // starts at y=100: 30 px of overlap
        Stroke[] keyA = [Line(90, 0, 90, 100), Line(90, 100, 90, 300)];
        Stroke[] keyB = [Line(110, 0, 110, 100), Line(110, 100, 110, 300)];
        return ([knee, shin], keyA, keyB);
    }

    private static ReviewFix Trim(int line, bool atStart, int other, params string[] cites) =>
        new(ReviewFixKind.Trim, line, atStart, other, false, cites, "test");

    [Fact]
    public void TheListingSaysTwoLinesRunAlongsideEachOtherPastTheJoint()
    {
        var (inbetween, a, b) = Knee();

        var listing = ReviewSheet.For(inbetween, a, b).Listing();

        Assert.Contains("L1:", listing);
        Assert.Contains("A2:", listing);
        Assert.Matches(@"L2 \d+ px away, runs alongside it for \d+ px", listing);
    }

    [Fact]
    public void ALineRunningPastTheJointIsTrimmedBackToWhereTheOtherBegins()
    {
        var (inbetween, a, b) = Knee();
        var sheet = ReviewSheet.For(inbetween, a, b);

        var outcome = LineReview.Apply(inbetween, sheet, [Trim(1, atStart: false, other: 2, "A1", "B1")]);

        Assert.Empty(outcome.Refused);
        var knee = outcome.Result.Strokes[0];
        Assert.InRange(knee.Points[^1].Y, 99, 101);                 // stops where the shin starts
        Assert.Equal(0.8, knee.Points[^1].Pressure, 6);
        Assert.Same(inbetween[1], outcome.Result.Strokes[1]);       // the shin is untouched
        Assert.Equal(CleanupKind.Trimmed, Assert.Single(outcome.Result.Changes).Kind);
    }

    [Fact]
    public void TheDrawingHandedInIsNeverChanged()
    {
        var (inbetween, a, b) = Knee();
        var before = inbetween[0].Points.ToList();

        LineReview.Apply(inbetween, ReviewSheet.For(inbetween, a, b), [Trim(1, false, 2, "A1")]);

        Assert.Equal(before, inbetween[0].Points);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A9")]
    [InlineData("C1")]
    [InlineData("it looks wrong")]
    public void AFixThatCitesNoKeyLineIsRefused(string cite)
    {
        var (inbetween, a, b) = Knee();
        string[] cites = cite.Length == 0 ? [] : [cite];

        var outcome = LineReview.Apply(inbetween, ReviewSheet.For(inbetween, a, b), [Trim(1, false, 2, cites)]);

        Assert.Empty(outcome.Made);
        Assert.Contains("Q298", Assert.Single(outcome.Refused).Why);
        Assert.False(outcome.Result.Changed);
    }

    [Fact]
    public void ATrimThatWouldTakeMoreThanHalfTheLineIsRefused()
    {
        var shortKnee = Line(100, 90, 100, 130);   // 40 px, 30 of them past the joint
        var shin = Line(104, 100, 104, 300);
        Stroke[] keys = [Line(90, 0, 90, 300)];

        var outcome = LineReview.Apply([shortKnee, shin], ReviewSheet.For([shortKnee, shin], keys, keys), [Trim(1, false, 2, "A1")]);

        Assert.Contains("more than half", Assert.Single(outcome.Refused).Why);
    }

    [Fact]
    public void AJoinClosesAGapUpToItsBoundAndNoFurther()
    {
        var upper = Line(100, 0, 100, 100);
        var near = Line(100, 120, 100, 300);   // 20 px gap
        var far = Line(300, 160, 300, 300);    // nowhere near
        Stroke[] keys = [Line(90, 0, 90, 300)];
        var sheet = ReviewSheet.For([upper, near, far], keys, keys);

        var outcome = LineReview.Apply([upper, near, far], sheet,
        [
            new ReviewFix(ReviewFixKind.Join, 1, false, 2, true, ["A1"], "gap"),
            new ReviewFix(ReviewFixKind.Join, 3, true, 1, false, ["A1"], "too far"),
        ]);

        Assert.Single(outcome.Made);
        Assert.Contains("at most", Assert.Single(outcome.Refused).Why);
        var a = outcome.Result.Strokes[0].Points[^1];
        var c = outcome.Result.Strokes[1].Points[0];
        Assert.True(GeometryOps.Dist(a, c) < 1e-9);
    }

    [Fact]
    public void OnlyAShortMarkIsRemoved()
    {
        var line = Line(0, 0, 0, 300);
        var tick = Line(40, 40, 52, 40);
        Stroke[] keys = [Line(90, 0, 90, 300)];
        var sheet = ReviewSheet.For([line, tick], keys, keys);

        var outcome = LineReview.Apply([line, tick], sheet,
        [
            new ReviewFix(ReviewFixKind.Remove, 2, false, null, false, ["A1"], "orphan"),
            new ReviewFix(ReviewFixKind.Remove, 1, false, null, false, ["A1"], "greedy"),
        ]);

        Assert.DoesNotContain(outcome.Result.Strokes, s => s.Id == tick.Id);
        Assert.Contains(outcome.Result.Strokes, s => s.Id == line.Id);
        Assert.Contains("a line, not a mark", Assert.Single(outcome.Refused).Why);
    }

    [Fact]
    public void ANumberNotOnTheSheetIsRefusedByName()
    {
        var (inbetween, a, b) = Knee();

        var outcome = LineReview.Apply(inbetween, ReviewSheet.For(inbetween, a, b), [Trim(7, false, 2, "A1")]);

        Assert.Contains("no line L7", Assert.Single(outcome.Refused).Why);
    }
}
