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
            new ReviewFix(ReviewFixKind.Join, 3, true, 1, true, ["A1"], "too far"),
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
    public void SeveralTrimsOnOneLineAreBoundedTogetherByTheLineAsDrawn()
    {
        // Adversary: three crossings, three trims, each under half alone and
        // together 85% of the line.
        var line = Line(0, 50, 100, 50);
        Stroke[] crossings = [Line(55, 0, 55, 100), Line(30, 0, 30, 100), Line(15, 0, 15, 100)];
        Stroke[] keys = [Line(0, 150, 100, 150)];
        Stroke[] drawing = [line, .. crossings];

        var outcome = LineReview.Apply(drawing, ReviewSheet.For(drawing, keys, keys),
            [Trim(1, false, 2, "A1"), Trim(1, false, 3, "A1"), Trim(1, false, 4, "A1")]);

        Assert.Single(outcome.Made);
        Assert.Equal(2, outcome.Refused.Count);
        Assert.Contains(outcome.Refused, r => r.Why.Contains("in all"));
        Assert.True(GeometryOps.PathLength(outcome.Result.Strokes[0].Points) >= 50 - 1e-6);
    }

    [Fact]
    public void ATrimThenARemoveCannotTakeAWholeLine()
    {
        var line = Line(0, 50, 70, 50, size: 2);
        var post = Line(36, 0, 36, 100);
        Stroke[] keys = [Line(0, 150, 100, 150)];

        var outcome = LineReview.Apply([line, post], ReviewSheet.For([line, post], keys, keys),
        [
            Trim(1, false, 2, "A1"),
            new ReviewFix(ReviewFixKind.Remove, 1, false, null, false, ["A1"], "now short"),
        ]);

        Assert.Contains(outcome.Result.Strokes, s => s.Id == line.Id);
        Assert.Contains("a line, not a mark", Assert.Single(outcome.Refused).Why);
    }

    [Fact]
    public void AJoinThatWouldFoldBackIntoAHairpinIsRefused()
    {
        // Two ends that already ran past each other side by side: joining
        // them turns both lines back past the joint.
        var down = Line(100, 0, 100, 130);
        var up = Line(104, 300, 104, 100);
        Stroke[] keys = [Line(90, 0, 90, 300)];

        var outcome = LineReview.Apply([down, up], ReviewSheet.For([down, up], keys, keys),
            [new ReviewFix(ReviewFixKind.Join, 1, false, 2, false, ["A1"], "meet")]);

        Assert.Contains("fold a line back", Assert.Single(outcome.Refused).Why);
        Assert.False(outcome.Result.Changed);
    }

    [Fact]
    public void AnEndJoinsOnce()
    {
        var a = Line(100, 0, 100, 100);
        var b = Line(100, 110, 100, 300);
        var c = Line(100, 112, 160, 112);
        Stroke[] keys = [Line(90, 0, 90, 300)];

        var outcome = LineReview.Apply([a, b, c], ReviewSheet.For([a, b, c], keys, keys),
        [
            new ReviewFix(ReviewFixKind.Join, 1, false, 2, true, ["A1"], "meet"),
            new ReviewFix(ReviewFixKind.Join, 1, false, 3, true, ["A1"], "and meet again"),
        ]);

        Assert.Single(outcome.Made);
        Assert.Contains("joins once", Assert.Single(outcome.Refused).Why);
    }

    [Fact]
    public void ATrimmedLineKeepsTheLengthOfItsDrawnTaperNotTheLengthCut()
    {
        // The knee, its end tapering over the last 10 px; 30 px are cut.
        var knee = Line(100, 0, 100, 130);
        knee.Points = [.. knee.Points.Select(p => p with { Pressure = p.Y >= 120 ? 0.2 + 0.6 * (130 - p.Y) / 10 : 0.8 })];
        var (_, a, b) = Knee();
        Stroke[] drawing = [knee, Line(104, 100, 104, 300)];

        var trimmed = LineReview.Apply(drawing, ReviewSheet.For(drawing, a, b), [Trim(1, false, 2, "A1")]).Result.Strokes[0];

        Assert.Equal(0.2, trimmed.Points[^1].Pressure, 6);
        var full = trimmed.Points.Where(p => p.Y < 100 - 15).ToList();
        Assert.All(full, p => Assert.Equal(0.8, p.Pressure, 6));   // 15 px back, the weight is untouched
    }

    [Fact]
    public void AMarkLikeOneAKeyHasNearbyIsNeverRemoved()
    {
        // The heel hook: the inbetween's version differs in shape, and a key
        // has its own a few pixels away — the keys' mark, moving.
        var line = Line(0, 0, 0, 300);
        var hook = Line(40, 280, 52, 290);
        Stroke[] keyA = [Line(0, 0, 0, 300), Line(30, 285, 40, 295)];
        Stroke[] keyB = [Line(10, 0, 10, 300)];

        var outcome = LineReview.Apply([line, hook], ReviewSheet.For([line, hook], keyA, keyB),
            [new ReviewFix(ReviewFixKind.Remove, 2, false, null, false, ["B1"], "leftover")]);

        Assert.Contains(outcome.Result.Strokes, s => s.Id == hook.Id);
        Assert.Contains("A2", Assert.Single(outcome.Refused).Why);
    }

    [Fact]
    public void WithAPictureTheKeysAreListedWithoutTheirNeighbours()
    {
        var (inbetween, a, b) = Knee();
        var sheet = ReviewSheet.For(inbetween, a, b);

        var withPicture = sheet.Listing(keyNeighbours: false);
        var textOnly = sheet.Listing(keyNeighbours: true);

        Assert.True(withPicture.Length < textOnly.Length);
        Assert.DoesNotContain("A2 0 px away", withPicture);
        Assert.Contains("L2", withPicture);
    }

    [Fact]
    public void DrawingsPastTheCleanersSizeAreTooLargeToReview()
    {
        var many = Enumerable.Range(0, LineCleaner.MaxLines).Select(i => Line(0, i, 10, i)).ToArray();

        Assert.True(ReviewSheet.For(many, [Line(0, 0, 1, 1)], [Line(0, 0, 1, 1)]).TooLarge);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void TheListingOfADenseSheetIsBoundedWork()
    {
        // Perf review: 300 lines x 100 points crowded into one figure took
        // 7.6 s when every nearby pair was measured in full.
        Stroke[] Dense(int seed) => [.. Enumerable.Range(0, 300).Select(i =>
            new Stroke
            {
                Brush = new BrushSettings { Size = 3 },
                Points = [.. Enumerable.Range(0, 100).Select(k =>
                    new StrokePoint(100 + (i * 7 % 500) + k * 1.2, 100 + (i * 11 % 500) + 3 * Math.Sin((k + seed + i) / 4.0), 0.7))],
            })];
        var sheet = ReviewSheet.For(Dense(0), Dense(5), Dense(9));
        sheet.Listing();

        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var listing = sheet.Listing(keyNeighbours: true);
        var ms = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        // A Release budget — CI runs Release — just under the 7.6 s the
        // unbounded listing took there; a Debug run gets four times as long.
        Assert.True(ms < 6000 * BuildFactor, $"the listing took {ms:F0} ms");
        Assert.True(listing.Length < 400_000, $"the listing is {listing.Length} characters");
    }

    [Fact]
    public void TheJointsSayWhetherAnOverlapReadsAsOneLineOrTwo()
    {
        // The owner's key A overlaps its knee on top of itself; the inbetween
        // overlaps it side by side. The first live grade missed the knee until
        // this was written out (2026-11-19).
        Stroke[] keyA = [Line(90, 0, 90, 130), Line(91, 100, 91, 300)];      // on top of each other
        Stroke[] keyB = [Line(110, 0, 110, 100), Line(110, 100, 110, 300)];  // meeting
        Stroke[] inbetween = [Line(100, 0, 100, 130), Line(108, 100, 108, 300)];

        var listing = ReviewSheet.For(inbetween, keyA, keyB).Listing(keyNeighbours: false);

        Assert.Matches(@"A1 end / A2 start: overlap: .*reads as one line", listing);
        Assert.Matches(@"B1 end / B2 start: meet", listing);
        Assert.Matches(@"L1 end / L2 start: overlap: .*reads as two lines", listing);
    }

    [Fact]
    public void ATrimAlongALineThatStopsShortIsRefusedRatherThanLeaveTheFormUndrawn()
    {
        // The first live grade's harm: a knee cut back to the tapering end of
        // a second thigh pass, leaving 20 px drawn by nothing.
        var knee = Line(100, 0, 100, 200);
        var pass = Line(96, 20, 104, 45);   // crosses shallowly, then stops
        Stroke[] keys = [Line(90, 0, 90, 300)];

        var outcome = LineReview.Apply([knee, pass], ReviewSheet.For([knee, pass], keys, keys), [Trim(1, atStart: true, other: 2, "A1")]);

        Assert.Contains("stops short", Assert.Single(outcome.Refused).Why);
        Assert.False(outcome.Result.Changed);
    }

#if DEBUG
    private const double BuildFactor = 4;
#else
    private const double BuildFactor = 1;
#endif

    [Fact]
    [Trait("Category", "Performance")]
    public void TwiceTheLinesListsInAboutTwiceTheTimeNotFour()
    {
        // The shape of the cost, which no machine's load changes: twice the
        // lines over twice the paper, at the same density. (Crowding more
        // lines into one place costs more per end by nature — more neighbours
        // to weigh — and the absolute budget above covers that.)
        static Stroke[] Spread(int n, int seed) => [.. Enumerable.Range(0, n).Select(i =>
            new Stroke
            {
                Brush = new BrushSettings { Size = 3 },
                Points = [.. Enumerable.Range(0, 100).Select(k =>
                    new StrokePoint(50 + (i * 37 % (6 * n)) + k * 1.2, 50 + (i * 53 % 1000) + 3 * Math.Sin((k + seed + i) / 4.0), 0.7))],
            })];
        double Time(int n)
        {
            var sheet = ReviewSheet.For(Spread(n, 0), Spread(n, 5), Spread(n, 9));
            sheet.Listing();
            var best = double.MaxValue;
            for (var r = 0; r < 3; r++)
            {
                var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                sheet.Listing(keyNeighbours: true);
                best = Math.Min(best, System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
            }
            return best;
        }

        var small = Time(150);
        var large = Time(300);

        Assert.True(large < 3 * small + 20, $"150 lines {small:F0} ms, 300 lines {large:F0} ms");
    }

    [Fact]
    public void APerpendicularOvershootIsStillTrimmedOnASketchyLine()
    {
        // The crossing is judged over a chord: a 90° post past a short bar,
        // drawn with a wobble, is an overshoot, not two lines running together.
        var bar = Line(0, 100, 60, 100);
        var post = Line(30, 40, 30, 110);
        post.Points = [.. post.Points.Select((p, i) => p with { X = p.X + 0.8 * Math.Sin(i * 1.7) })];
        Stroke[] keys = [Line(0, 200, 60, 200)];

        var outcome = LineReview.Apply([bar, post], ReviewSheet.For([bar, post], keys, keys), [Trim(2, atStart: false, other: 1, "A1")]);

        Assert.Empty(outcome.Refused);
        Assert.InRange(outcome.Result.Strokes[1].Points[^1].Y, 99, 101);
    }

    [Fact]
    public void OneOfTwoPassesDrawnTogetherIsNeverTrimmed()
    {
        // The third live grade: both passes of a doubled thigh line cut back
        // where they run into the knee, a joint a key draws side by side.
        var pass1 = Line(100, 0, 100, 130);
        var pass2 = Line(103, 0, 103, 128);
        var knee = Line(101, 100, 101, 300);
        Stroke[] keys = [Line(90, 0, 90, 300)];
        Stroke[] drawing = [pass1, pass2, knee];

        var outcome = LineReview.Apply(drawing, ReviewSheet.For(drawing, keys, keys),
            [Trim(1, false, 3, "A1"), Trim(2, false, 3, "A1")]);

        Assert.Equal(2, outcome.Refused.Count);
        Assert.All(outcome.Refused, r => Assert.Contains("two passes drawn together", r.Why));
        Assert.False(outcome.Result.Changed);
    }

    [Fact]
    public void ANumberNotOnTheSheetIsRefusedByName()
    {
        var (inbetween, a, b) = Knee();

        var outcome = LineReview.Apply(inbetween, ReviewSheet.For(inbetween, a, b), [Trim(7, false, 2, "A1")]);

        Assert.Contains("no line L7", Assert.Single(outcome.Refused).Why);
    }
}
