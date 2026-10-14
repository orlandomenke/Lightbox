using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests.Inbetween;

/// <summary>
/// An inbetween drawn from a stated correspondence (Q270): which strokes of key
/// A are the same part as which strokes of key B. The engine draws; the
/// answer only says what goes with what, so a line drawn as one stroke in one
/// key and as two (one of them the other way round) in the other is tweened as
/// the one line it is.
/// </summary>
public class MatchedInbetweenTests(ITestOutputHelper output)
{
    private static Stroke Path(params (double X, double Y)[] points) => new()
    {
        Points = [.. points.Select(p => new StrokePoint(p.X, p.Y, 0.8))],
        Brush = new BrushSettings { Size = 6 },
    };

    /// <summary>An L: down 200, then right 100.</summary>
    private static Stroke L(double dx) => Path((100 + dx, 100), (100 + dx, 300), (200 + dx, 300));

    /// <summary>
    /// The same L moved right by <paramref name="dx"/>, drawn as two strokes in
    /// the other order: the foot first, right to left, then the upright upward —
    /// with a small gap at the corner, the way a hand leaves one.
    /// </summary>
    private static List<Stroke> LRedrawn(double dx) =>
    [
        Path((200 + dx, 300), (106 + dx, 300)),
        Path((100 + dx, 294), (100 + dx, 100)),
    ];

    private static double DistanceTo(StrokePoint p, Stroke s)
    {
        var best = double.MaxValue;
        for (var i = 1; i < s.Points.Count; i++)
            best = Math.Min(best, GeometryOps.DistToSegment(p, s.Points[i - 1], s.Points[i]));
        return best;
    }

    /// <summary>The furthest any point of <paramref name="drawn"/> lies from <paramref name="expected"/>.</summary>
    private static double Worst(IEnumerable<Stroke> drawn, Stroke expected) =>
        drawn.SelectMany(s => s.Points).Max(p => DistanceTo(p, expected));

    [Fact]
    public void AOneStrokeLineMatchedToTheSameLineInTwoReversedPiecesTweensAsOneLine()
    {
        var frame = MatchedInbetween.Inbetween([L(0)], LRedrawn(40), [new PartMatch([0], [0, 1])], 0.5, Easing.Linear);

        var worst = Worst(frame, L(20));
        output.WriteLine($"{frame.Count} strokes, worst {worst:0.0}px from the L halfway");
        Assert.True(worst < 4, $"worst {worst:0.0}px");
    }

    [Fact]
    public void WithoutTheMatchTheEngineGetsItWrong()
    {
        // What the stated correspondence is for: left to guess, the matcher
        // pairs the one stroke with one of the two and fades the other.
        var frame = Inbetweener.Inbetween([L(0)], LRedrawn(40), 0.5, Easing.Linear);

        var worst = Worst(frame, L(20));
        output.WriteLine($"guessed: worst {worst:0.0}px from the L halfway");
        Assert.True(worst > 20, $"worst {worst:0.0}px");
    }

    [Fact]
    public void ItComesBackInTheNearerKeysPieces()
    {
        var early = MatchedInbetween.Inbetween([L(0)], LRedrawn(40), [new PartMatch([0], [0, 1])], 0.25, Easing.Linear);
        var late = MatchedInbetween.Inbetween([L(0)], LRedrawn(40), [new PartMatch([0], [0, 1])], 0.75, Easing.Linear);

        Assert.Single(early);
        Assert.Equal(2, late.Count);
    }

    [Fact]
    public void TheGapBetweenTwoPiecesIsNotDrawnAcross()
    {
        // Two separate lines that are one part: a broken outline.
        var a = new List<Stroke> { Path((100, 100), (200, 100)), Path((260, 100), (360, 100)) };
        var b = new List<Stroke> { Path((100, 140), (200, 140)), Path((260, 140), (360, 140)) };

        var frame = MatchedInbetween.Inbetween(a, b, [new PartMatch([0, 1], [0, 1])], 0.5, Easing.Linear);

        Assert.Equal(2, frame.Count);
        var inGap = frame.SelectMany(s => s.Points).Count(p => p.X is > 210 and < 250);
        Assert.Equal(0, inGap);
    }

    [Fact]
    public void AClosedLoopIsTweenedFromWhereverEachKeyStartedIt()
    {
        // A square outline started at a different corner, and running the
        // other way, in key B.
        var a = Path((100, 100), (200, 100), (200, 200), (100, 200), (100, 100));
        var b = Path((240, 240), (240, 140), (140, 140), (140, 240), (240, 240));
        var halfway = Path((120, 120), (220, 120), (220, 220), (120, 220), (120, 120));

        var frame = MatchedInbetween.Inbetween([a], [b], [new PartMatch([0], [0])], 0.5, Easing.Linear);

        var worst = Worst(frame, halfway);
        output.WriteLine($"worst {worst:0.0}px from the square halfway");
        Assert.True(worst < 4, $"worst {worst:0.0}px");
    }

    [Fact]
    public void APartWithNoPartnerFadesAsItAlwaysHas()
    {
        var a = new List<Stroke> { L(0), Path((400, 100), (450, 100)) };
        var b = new List<Stroke> { L(40) };

        var early = MatchedInbetween.Inbetween(a, b, [new PartMatch([0], [0]), new PartMatch([1], [])], 0.25, Easing.Linear);
        var late = MatchedInbetween.Inbetween(a, b, [new PartMatch([0], [0]), new PartMatch([1], [])], 0.75, Easing.Linear);

        var fading = early.Single(s => s.Points[0].X >= 400);
        Assert.True(fading.Brush.Opacity is > 0 and < 1);
        Assert.DoesNotContain(late, s => s.Points[0].X >= 400);
    }

    [Fact]
    public void StrokesTheAnswerDoesNotMentionAreMatchedTheUsualWay()
    {
        var a = new List<Stroke> { L(0), Path((400, 100), (450, 100)) };
        var b = new List<Stroke> { L(40), Path((400, 140), (450, 140)) };

        var frame = MatchedInbetween.Inbetween(a, b, [new PartMatch([0], [0])], 0.5, Easing.Linear);

        Assert.Equal(2, frame.Count);
        Assert.Contains(frame, s => Math.Abs(s.Points[0].Y - 120) < 1);
    }

    [Fact]
    public void NonsenseInTheAnswerIsIgnoredRatherThanThrown()
    {
        // Indices out of range, and a stroke claimed by two parts: the first
        // claim stands.
        var frame = MatchedInbetween.Inbetween([L(0)], LRedrawn(40),
            [new PartMatch([0, 7], [0, 1, -1]), new PartMatch([0], [1])], 0.5, Easing.Linear);

        Assert.True(Worst(frame, L(20)) < 4);
    }

    [Fact]
    public void EachPieceKeepsTheBrushOfTheKeyItWasCutFrom()
    {
        var a = L(0);
        a.Brush = new BrushSettings { Size = 6, TipId = "pencil" };
        var b = LRedrawn(40);
        foreach (var s in b) s.Brush = new BrushSettings { Size = 10, TipId = "ink" };

        var early = MatchedInbetween.Inbetween([a], b, [new PartMatch([0], [0, 1])], 0.25, Easing.Linear);
        var late = MatchedInbetween.Inbetween([a], b, [new PartMatch([0], [0, 1])], 0.75, Easing.Linear);

        Assert.NotEmpty(early);
        Assert.NotEmpty(late);
        Assert.All(early, s => Assert.Equal("pencil", s.Brush.TipId));
        Assert.All(late, s => Assert.Equal("ink", s.Brush.TipId));
    }

    /// <summary>A box drawn as four edges, the way a cuff or a shoe is drawn: several small lines that move together.</summary>
    private static List<Stroke> Box(double dx, double dy, bool redrawn = false) => redrawn
        ?
        [
            Path((200 + dx, 160 + dy), (100 + dx, 160 + dy)),
            Path((100 + dx, 100 + dy), (100 + dx, 160 + dy)),
            Path((200 + dx, 100 + dy), (200 + dx, 160 + dy)),
            Path((200 + dx, 100 + dy), (100 + dx, 100 + dy)),
        ]
        :
        [
            Path((100 + dx, 100 + dy), (200 + dx, 100 + dy)),
            Path((200 + dx, 100 + dy), (200 + dx, 160 + dy)),
            Path((200 + dx, 160 + dy), (100 + dx, 160 + dy)),
            Path((100 + dx, 160 + dy), (100 + dx, 100 + dy)),
        ];

    [Fact]
    public void APartOfManySmallPiecesMovesAsOneThingRatherThanBeingStrungIntoALine()
    {
        // Strung end to end, a box drawn in another order gets its edges
        // tied in knots; moved as one thing, each edge lands on its own.
        // Plus the double line of a sole and a lace across it: lines side by
        // side, which no single path runs along.
        List<Stroke> Shoe(double dx, double dy, bool redrawn)
        {
            var shoe = Box(dx, dy, redrawn);
            Stroke[] inner =
            [
                Path((105 + dx, 150 + dy), (195 + dx, 150 + dy)),
                Path((130 + dx, 120 + dy), (170 + dx, 120 + dy)),
            ];
            shoe.AddRange(redrawn ? inner.Reverse().Select(s => Path([.. s.Points.AsEnumerable().Reverse().Select(p => (p.X, p.Y))])) : inner);
            return shoe;
        }
        var halfway = Shoe(30, 10, false);
        var frame = MatchedInbetween.Inbetween(Shoe(0, 0, false), Shoe(60, 20, true), [new PartMatch([0, 1, 2, 3, 4, 5], [0, 1, 2, 3, 4, 5])], 0.5, Easing.Linear);

        var worst = frame.SelectMany(s => s.Points).Max(p => halfway.Min(e => DistanceTo(p, e)));
        output.WriteLine($"{frame.Count} strokes, worst {worst:0.0}px from the box halfway");
        Assert.Equal(6, frame.Count);
        Assert.True(worst < 3, $"worst {worst:0.0}px");
    }

    [Fact]
    public void APieceOfSuchAPartWithNoPartnerTravelsWithItWhileItFades()
    {
        // A lace across the middle of the shoe, in key A only and nowhere near
        // any line of key B's: it goes, but it goes with the shoe.
        var a = Box(0, 0);
        a.Add(Path((140, 130), (160, 130)));
        // Told to bend: a solid object shows its key's lace whole until halfway instead.
        var frame = MatchedInbetween.Inbetween(a, Box(60, 20), [new PartMatch([0, 1, 2, 3, 4], [0, 1, 2, 3], Solid: false)], 0.25, Easing.Linear);

        var lace = frame.Single(s => s.Brush.Opacity < 1);
        output.WriteLine($"lace at ({lace.Points[0].X:0}, {lace.Points[0].Y:0})");
        Assert.Equal(155, lace.Points[0].X, 1);
        Assert.Equal(135, lace.Points[0].Y, 1);
    }

    [Fact]
    public void ASmallPartSplitDifferentlyInEachKeyStillComesOutWhole()
    {
        // The shoe's outline as four edges in key A and as two corners in key
        // B: no piece of one is a piece of the other, so pairing pieces cannot
        // work, and each of A's lines bends towards B's ink instead.
        var b = new List<Stroke>
        {
            Path((160, 120), (260, 120), (260, 180)),
            Path((260, 180), (160, 180), (160, 120)),
        };
        b.Add(Path((165, 170), (255, 170)));
        b.Add(Path((165, 175), (255, 175)));   // a doubled sole line, so the part counts as many small lines
        var a = Box(0, 0);
        a.Add(Path((105, 150), (195, 150)));

        var frame = MatchedInbetween.Inbetween(a, b, [new PartMatch([0, 1, 2, 3, 4], [0, 1, 2, 3])], 0.5, Easing.Linear);

        var halfway = Box(30, 10);
        // Past halfway key B's pieces are drawn: its two corners are the
        // outline; its sole lines are inside it and are not measured here.
        var corners = frame.Where(s => s.Points.Max(p => p.Y) - s.Points.Min(p => p.Y) > 30).ToList();
        Assert.Equal(2, corners.Count);
        var outline = corners.SelectMany(s => s.Points).Max(p => halfway.Min(e => DistanceTo(p, e)));
        output.WriteLine($"{frame.Count} strokes, outline worst {outline:0.0}px from the box halfway");
        // Pairing pieces one to one, the version this replaced, was 13.0px.
        Assert.True(outline < 4, $"worst {outline:0.0}px");
    }

    /// <summary>A shoe-like object: an outline in four lines, a sole line and a lace, turned about its middle and moved.</summary>
    private static List<Stroke> Shoe(double angleDeg, double dx, double dy, bool redrawn)
    {
        var c = (X: 150.0, Y: 130.0);
        var a = angleDeg * Math.PI / 180;
        (double X, double Y) P(double x, double y) =>
            (c.X + dx + (x - c.X) * Math.Cos(a) - (y - c.Y) * Math.Sin(a),
             c.Y + dy + (x - c.X) * Math.Sin(a) + (y - c.Y) * Math.Cos(a));
        if (!redrawn)
        {
            return
            [
                Path(P(100, 100), P(200, 100)), Path(P(200, 100), P(200, 160)),
                Path(P(200, 160), P(100, 160)), Path(P(100, 160), P(100, 100)),
                Path(P(105, 150), P(195, 150)), Path(P(130, 115), P(170, 125)),
            ];
        }
        // The same shoe drawn another way: two corners, the sole backwards, the lace first.
        return
        [
            Path(P(170, 125), P(130, 115)),
            Path(P(100, 100), P(200, 100), P(200, 160)),
            Path(P(195, 150), P(105, 150)),
            Path(P(200, 160), P(100, 160), P(100, 100)),
        ];
    }

    private static double OffShoe(IEnumerable<Stroke> drawn, List<Stroke> shoe) =>
        drawn.SelectMany(s => s.Points).Max(p => shoe.Min(e => DistanceTo(p, e)));

    [Fact]
    public void ASolidObjectTurnsAndMovesAsOneDrawingTheNearerKeysLines()
    {
        // Turned 40 degrees and moved; halfway-ish it is the same shoe turned
        // part of the way and moved part of the way — whichever key it shows.
        var a = Shoe(0, 0, 0, redrawn: false);
        var b = Shoe(40, 80, 30, redrawn: true);
        List<PartMatch> solid = [new PartMatch([0, 1, 2, 3, 4, 5], [0, 1, 2, 3], Solid: true)];

        var early = MatchedInbetween.Inbetween(a, b, solid, 0.25, Easing.Linear);
        var late = MatchedInbetween.Inbetween(a, b, solid, 0.75, Easing.Linear);

        var offEarly = OffShoe(early, Shoe(10, 20, 7.5, redrawn: false));
        var offLate = OffShoe(late, Shoe(30, 60, 22.5, redrawn: false));
        output.WriteLine($"t 0.25: {early.Count} strokes, {offEarly:0.0}px off; t 0.75: {late.Count} strokes, {offLate:0.0}px off");
        Assert.Equal(6, early.Count);   // key A's lines
        Assert.Equal(4, late.Count);    // key B's lines
        Assert.True(offEarly < 2, $"{offEarly:0.0}px");
        Assert.True(offLate < 2, $"{offLate:0.0}px");
    }

    [Fact]
    public void ASolidObjectKeepsTheShapeOfEveryLine()
    {
        var a = Shoe(0, 0, 0, redrawn: false);
        var b = Shoe(40, 80, 30, redrawn: true);

        var frame = MatchedInbetween.Inbetween(a, b, [new PartMatch([0, 1, 2, 3, 4, 5], [0, 1, 2, 3], Solid: true)], 0.25, Easing.Linear);

        // Moved and turned, never bent: each line is as long as the one it came from.
        for (var i = 0; i < a.Count; i++)
        {
            Assert.Equal(GeometryOps.PathLength(a[i].Points), GeometryOps.PathLength(frame[i].Points), 0);
        }
    }

    [Fact]
    public void AMatchOfManySmallLinesIsSolidUnlessTheArtistSaysOtherwise()
    {
        var a = Shoe(0, 0, 0, redrawn: false);
        var b = Shoe(40, 80, 30, redrawn: true);

        var auto = MatchedInbetween.Inbetween(a, b, [new PartMatch([0, 1, 2, 3, 4, 5], [0, 1, 2, 3])], 0.25, Easing.Linear);
        var bent = MatchedInbetween.Inbetween(a, b, [new PartMatch([0, 1, 2, 3, 4, 5], [0, 1, 2, 3], Solid: false)], 0.25, Easing.Linear);

        var offAuto = OffShoe(auto, Shoe(10, 20, 7.5, redrawn: false));
        var offBent = OffShoe(bent, Shoe(10, 20, 7.5, redrawn: false));
        output.WriteLine($"automatic {offAuto:0.0}px off, told to bend {offBent:0.0}px off");
        Assert.True(offAuto < 2, $"{offAuto:0.0}px");
        Assert.True(offBent > offAuto + 1, "told not to be solid, it is drawn the bending way");
    }

    [Fact]
    public void ALongLineInPiecesIsNotSolidByDefault()
    {
        // The L case: one line drawn as two pieces bends, as it always has.
        var frame = MatchedInbetween.Inbetween([L(0)], LRedrawn(40), [new PartMatch([0], [0, 1])], 0.5, Easing.Linear);

        Assert.True(Worst(frame, L(20)) < 4);
    }
}
