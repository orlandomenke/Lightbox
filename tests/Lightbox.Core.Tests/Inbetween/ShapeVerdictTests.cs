using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests.Inbetween;

/// <summary>
/// The check, when the keys' strokes do not correspond (2026-10-14): an artist
/// does not draw each frame with the same strokes in the same order, and a
/// traced drawing certainly does not. Pairing stroke 5 with stroke 5 then pairs
/// the wrong lines, and refusing a frame for not following those pairs refuses
/// it for not copying a mistake. So the check judges what such a frame looks
/// like instead: does its ink lie where the keys' ink goes, and is there about
/// as much of it.
/// </summary>
/// <remarks>
/// Keys whose strokes do correspond — the ordinary case for a drawing made
/// stroke for stroke — keep the stroke-by-stroke check, which is stricter and
/// is pinned by <see cref="InbetweenVerifierTests"/>.
/// </remarks>
public class ShapeVerdictTests(ITestOutputHelper output)
{
    private static Stroke Path(params (double X, double Y)[] points) => new()
    {
        Points = [.. points.Select(p => new StrokePoint(p.X, p.Y, 0.8))],
        Brush = new BrushSettings { Size = 6 },
    };

    /// <summary>A figure as three long lines: an L of a leg and a foot, and a hip line.</summary>
    private static List<Stroke> Figure(double dx, double dy) =>
    [
        Path((100 + dx, 100 + dy), (140 + dx, 200 + dy), (180 + dx, 300 + dy)),
        Path((180 + dx, 300 + dy), (240 + dx, 300 + dy)),
        Path((60 + dx, 100 + dy), (160 + dx, 100 + dy)),
    ];

    /// <summary>
    /// The same figure drawn another way: lines split where the first drawing
    /// did not split them, in another order, and some the other way round —
    /// what tracing, or an artist, does.
    /// </summary>
    private static List<Stroke> FigureRedrawn(double dx, double dy) =>
    [
        Path((160 + dx, 100 + dy), (110 + dx, 100 + dy)),
        Path((240 + dx, 300 + dy), (180 + dx, 300 + dy), (160 + dx, 250 + dy)),
        Path((110 + dx, 100 + dy), (60 + dx, 100 + dy)),
        Path((100 + dx, 100 + dy), (140 + dx, 200 + dy), (160 + dx, 250 + dy)),
    ];

    private InbetweenJudgement Judge(List<Stroke> a, List<Stroke> b, List<Stroke> candidate)
    {
        var judged = InbetweenVerifier.Verify(a, b, [new CandidateInbetween(0.5, candidate)], Easing.Linear);
        var verdict = judged.Frames[0];
        output.WriteLine($"{verdict.Fault}: {verdict.Refusal ?? "accepted"}");
        return verdict;
    }

    [Fact]
    public void KeysDrawnStrokeForStrokeCorrespond()
    {
        Assert.True(InbetweenVerifier.KeysCorrespond(Figure(0, 0), Figure(40, 0)));
    }

    [Fact]
    public void KeysDrawnWithDifferentStrokesDoNot()
    {
        Assert.False(InbetweenVerifier.KeysCorrespond(Figure(0, 0), FigureRedrawn(40, 0)));
    }

    [Fact]
    public void AFreeDrawingHalfwayBetweenIndependentKeysIsAccepted()
    {
        // Halfway, and drawn with strokes of its own — neither key's split.
        var halfway = new List<Stroke>
        {
            Path((80, 100), (180, 100)),
            Path((120, 100), (160, 200), (200, 300), (260, 300)),
        };

        var verdict = Judge(Figure(0, 0), FigureRedrawn(40, 0), halfway);

        Assert.Null(verdict.Refusal);
    }

    [Fact]
    public void ADrawingThatStraysFromWhereTheKeysGoIsRefused()
    {
        var astray = Figure(20, 0);
        astray.Add(Path((600, 500), (700, 520), (760, 600)));

        var verdict = Judge(Figure(0, 0), FigureRedrawn(40, 0), astray);

        Assert.Equal(InbetweenFault.NotBetween, verdict.Fault);
    }

    [Fact]
    public void ADrawingWithFarLessLineThanTheKeysIsRefused()
    {
        var stub = new List<Stroke> { Path((80, 100), (130, 100)) };

        var verdict = Judge(Figure(0, 0), FigureRedrawn(40, 0), stub);

        Assert.Equal(InbetweenFault.Incomplete, verdict.Fault);
    }

    [Fact]
    public void ADrawingWithFarMoreLineThanTheKeysIsRefused()
    {
        var scribble = Figure(20, 0);
        for (var i = 0; i < 12; i++) scribble.Add(Path((100 + i * 10, 120), (130 + i * 10, 280)));

        var verdict = Judge(Figure(0, 0), FigureRedrawn(40, 0), scribble);

        Assert.Equal(InbetweenFault.Incomplete, verdict.Fault);
    }

    [Fact]
    public void ADrawingThatSitsOnOneKeyIsNotBetween()
    {
        // All of it on key A: it went nowhere. Between means the middle of
        // the drawing moved part of the way the keys' middles did.
        var stayed = Figure(-30, 0);

        var verdict = Judge(Figure(0, 0), FigureRedrawn(120, 0), stayed);

        Assert.Equal(InbetweenFault.NotBetween, verdict.Fault);
    }
}
