using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Serialization;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests.Inbetween;

/// <summary>
/// The artist's line matches (Q271): which lines of key A are which lines of
/// key B, kept on key A for the gap to the next key. Absent from the file until
/// one is made; named by stroke id and piece, so a match outlives edits
/// elsewhere in the drawing and is dropped, not guessed at, when its line goes.
/// </summary>
public class LineMatchTests(ITestOutputHelper output)
{
    private static Stroke Path(string id, params (double X, double Y)[] points) => new()
    {
        Id = id,
        Points = [.. points.Select(p => new StrokePoint(p.X, p.Y, 0.8))],
        Brush = new BrushSettings { Size = 6 },
    };

    [Fact]
    public void ADocumentWithoutMatchesWritesNoKey()
    {
        var doc = DocumentFactory.CreateDoc(64, 48, 12);

        Assert.DoesNotContain("\"matches\"", DocJson.Serialize(doc));
    }

    [Fact]
    public void MatchesRoundTripAndACloneIsItsOwn()
    {
        var doc = DocumentFactory.CreateDoc(64, 48, 12);
        var frame = doc.Scene.Layers[^1].Cels[0].Frame!;
        frame.Matches = [new LineMatch { A = [new PieceRef("s1", 0)], B = [new PieceRef("t1", 0), new PieceRef("t2", 1)] }];

        var back = DocJson.Deserialize(DocJson.Serialize(doc)).Scene.Layers[^1].Cels[0].Frame!;
        var m = Assert.Single(back.Matches!);
        Assert.Equal([new PieceRef("s1", 0)], m.A);
        Assert.Equal([new PieceRef("t1", 0), new PieceRef("t2", 1)], m.B);

        var copy = frame.Clone();
        copy.Matches![0].A.Add(new PieceRef("s9", 0));
        Assert.Single(frame.Matches[0].A);
    }

    [Fact]
    public void AStrokeIsCutWhereAnotherLineEndsAgainstItAndNothingIsLost()
    {
        // A long line with a short one ending against its middle: a T.
        var strokes = new List<Stroke>
        {
            Path("bar", (0, 100), (200, 100)),
            Path("stem", (100, 200), (100, 102)),
        };

        var pieces = LinePieces.Cut(strokes);

        var bar = pieces.Where(p => p.Source == "bar").ToList();
        output.WriteLine(string.Join(", ", pieces.Select(p => $"{p.Source}#{p.Ordinal} {GeometryOps.PathLength(p.Stroke.Points):0}px")));
        Assert.Equal(2, bar.Count);
        Assert.Equal([0, 1], bar.Select(p => p.Ordinal));
        Assert.Single(pieces, p => p.Source == "stem");
        // The pieces share their cut point, so laid end to end they are the line.
        Assert.Equal(200, bar.Sum(p => GeometryOps.PathLength(p.Stroke.Points)), 0);
    }

    [Fact]
    public void AStrokeIsCutAtASharpCorner()
    {
        var pieces = LinePieces.Cut([Path("l", (100, 100), (100, 300), (200, 300))]);

        Assert.Equal(2, pieces.Count);
    }

    [Fact]
    public void CuttingIsTheSameEveryTime()
    {
        var strokes = new List<Stroke>
        {
            Path("bar", (0, 100), (200, 100)),
            Path("stem", (100, 200), (100, 102)),
            Path("l", (300, 100), (300, 300), (400, 300)),
        };

        var once = LinePieces.Cut(strokes).Select(p => (p.Source, p.Ordinal, p.Stroke.Points.Count));
        var twice = LinePieces.Cut(strokes).Select(p => (p.Source, p.Ordinal, p.Stroke.Points.Count));

        Assert.Equal(once, twice);
    }

    /// <summary>An L, as key A draws it.</summary>
    private static List<Stroke> KeyA() => [Path("a-l", (100, 100), (100, 300), (200, 300))];

    /// <summary>The same L moved right by 40, drawn as two strokes the other way round.</summary>
    private static List<Stroke> KeyB() =>
    [
        Path("b-foot", (240, 300), (146, 300)),
        Path("b-up", (140, 294), (140, 100)),
    ];

    private static double Worst(IEnumerable<Stroke> drawn, Stroke expected) =>
        drawn.SelectMany(s => s.Points).Max(p =>
            Enumerable.Range(1, expected.Points.Count - 1)
                .Min(i => GeometryOps.DistToSegment(p, expected.Points[i - 1], expected.Points[i])));

    [Fact]
    public void AnInbetweenFollowsTheMatchesItIsGiven()
    {
        // Key A's L is one stroke cut at its corner into two pieces; key B's
        // two strokes are one piece each.
        List<LineMatch> matches =
        [
            new() { A = [new PieceRef("a-l", 0), new PieceRef("a-l", 1)], B = [new PieceRef("b-foot", 0), new PieceRef("b-up", 0)] },
        ];

        var frame = MatchedInbetween.Inbetween(KeyA(), KeyB(), matches, 0.5, Easing.Linear);

        var worst = Worst(frame, Path("half", (120, 100), (120, 300), (220, 300)));
        output.WriteLine($"{frame.Count} strokes, worst {worst:0.0}px");
        Assert.True(worst < 4, $"worst {worst:0.0}px");
    }

    [Fact]
    public void AMatchNamingALineThatIsGoneIsDroppedAndTheRestStillDrawn()
    {
        List<LineMatch> matches =
        [
            new() { A = [new PieceRef("a-l", 0), new PieceRef("a-l", 1)], B = [new PieceRef("b-foot", 0), new PieceRef("b-up", 0)] },
            new() { A = [new PieceRef("erased", 0)], B = [new PieceRef("b-up", 7)] },
        ];

        var frame = MatchedInbetween.Inbetween(KeyA(), KeyB(), matches, 0.5, Easing.Linear);

        Assert.True(Worst(frame, Path("half", (120, 100), (120, 300), (220, 300))) < 4);
    }

    [Fact]
    public void LinesNoMatchMentionsAreTweenedWholeTheUsualWay()
    {
        // A second line in both keys that the artist never touched: it is not
        // cut, so the ordinary matcher sees the stroke the artist drew.
        var a = KeyA();
        a.Add(Path("a-line", (400, 100), (500, 100)));
        var b = KeyB();
        b.Add(Path("b-line", (400, 140), (500, 140)));
        List<LineMatch> matches =
        [
            new() { A = [new PieceRef("a-l", 0), new PieceRef("a-l", 1)], B = [new PieceRef("b-foot", 0), new PieceRef("b-up", 0)] },
        ];

        var frame = MatchedInbetween.Inbetween(a, b, matches, 0.5, Easing.Linear);

        var line = Assert.Single(frame, s => s.Points[0].X >= 390);
        Assert.Equal(120, line.Points[0].Y, 0);
    }

    [Fact]
    public void AnAutomaticMatchWritesNoSolidKeyAndAnOverrideRoundTrips()
    {
        var doc = DocumentFactory.CreateDoc(64, 48, 12);
        var frame = doc.Scene.Layers[^1].Cels[0].Frame!;
        frame.Matches =
        [
            new LineMatch { A = [new PieceRef("s1", 0)], B = [new PieceRef("t1", 0)] },
            new LineMatch { A = [new PieceRef("s2", 0)], B = [new PieceRef("t2", 0)], Solid = false },
        ];

        var json = DocJson.Serialize(doc);
        var back = DocJson.Deserialize(json).Scene.Layers[^1].Cels[0].Frame!.Matches!;

        Assert.Equal(1, json.Split("\"solid\"").Length - 1);
        Assert.Null(back[0].Solid);
        Assert.False(back[1].Solid);
    }

    [Fact]
    public void ADamagedMatchListLoadsAsWhatItCanStillSay()
    {
        var doc = DocumentFactory.CreateDoc(64, 48, 12);
        var frame = doc.Scene.Layers[^1].Cels[0].Frame!;
        frame.Matches = [new LineMatch { A = [new PieceRef("s1", 0)], B = [new PieceRef("t1", 0)] }];
        var json = DocJson.Serialize(doc).Replace("\"matches\":[", "\"matches\":[null,{\"a\":null,\"b\":[null]},");

        var back = DocJson.Deserialize(json).Scene.Layers[^1].Cels[0].Frame!;

        var only = Assert.Single(back.Matches!);
        Assert.Equal([new PieceRef("s1", 0)], only.A);
        back.Clone();   // and the drawing can still be edited
    }

    [Fact]
    public void AStrokeOfAbsurdLengthIsCutQuickly()
    {
        var huge = Path("huge", (0, 0), (1e12, 0));

        var started = System.Diagnostics.Stopwatch.StartNew();
        var pieces = LinePieces.Cut([huge, Path("other", (5e11, 10), (5e11, 2))]);

        Assert.NotEmpty(pieces);
        Assert.True(started.ElapsedMilliseconds < 2000, $"{started.ElapsedMilliseconds} ms");
    }
}
