using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using Xunit.Abstractions;

namespace Lightbox.Ai.Tests;

/// <summary>
/// What an AI request costs, kept honest.
/// </summary>
/// <remarks>
/// These are budgets in the charter's sense — deliberately loose, catching an
/// order-of-magnitude regression rather than drift. They exist because the
/// payload is the one cost in this app that is invisible locally: a change
/// that doubles it shows up on somebody's bill a month later, and nothing in
/// the test suite would have said a word.
///
/// Every one of them prints both numbers. The lesson `CLAUDE.md` records about
/// dab saturation applies here in the same shape: an assertion that passes
/// tells you nothing about how close it came.
///
/// The figures behind `docs/DESIGN-ai-payload.md` come from here.
/// </remarks>
[Trait("Category", "Performance")]
public class AiPayloadBudgetTests(ITestOutputHelper output)
{
    /// <summary>A stroke with realistic point counts and full-precision coordinates.</summary>
    private static Stroke Part(int seed, int points)
    {
        var list = new List<StrokePoint>();
        for (var i = 0; i < points; i++)
        {
            var t = i / (double)(points - 1);
            list.Add(new StrokePoint(
                100 + seed * 7 + 300 * t + 12 * Math.Sin(t * 6 + seed),
                200 + seed * 11 + 80 * Math.Cos(t * 3 + seed),
                0.3 + 0.6 * Math.Sin(t * Math.PI)));
        }
        return new Stroke
        {
            Label = $"part-{seed}",
            Color = "#1a1a1a",
            Points = list,
            Brush = new BrushSettings { Size = 6.25, Hardness = 0.83, Opacity = 0.97 },
        };
    }

    private static List<Stroke> Frame(int strokes, int points, int offset) =>
        Enumerable.Range(0, strokes).Select(i => Part(i + offset, points)).ToList();

    private static InbetweenRequest Request(int strokes, int points) => new(
        new SceneInfo(1920, 1080, 24),
        Frame(strokes, points, 0),
        Frame(strokes, points, 3),
        [0.25, 0.5, 0.75],
        Easing.EaseInOut);

    /// <summary>
    /// Characters ÷ 4, the usual rule of thumb. It under-counts numeric JSON,
    /// because tokenizers split digit runs — so this is a floor, and saying so
    /// is the difference between a budget and a false comfort.
    /// </summary>
    private static int TokenFloor(string s) => s.Length / 4;

    [Theory]
    [InlineData(12, 40, 40)]
    [InlineData(40, 60, 130)]
    [InlineData(120, 90, 380)]
    public void AnInbetweenRequestStaysWithinItsBudget(int strokes, int points, int budgetKb)
    {
        var user = Prompts.InbetweenUser(Request(strokes, points));
        var kb = user.Length / 1024.0;

        output.WriteLine(
            $"{strokes} strokes x {points} points: {kb:F1} KB of {budgetKb} KB " +
            $"(≥{TokenFloor(user)} tokens)");

        Assert.True(kb < budgetKb, $"payload grew to {kb:F1} KB, budget {budgetKb} KB");
    }

    [Fact]
    public void ResamplingIsWhatKeepsALongStrokeAffordable()
    {
        // MaxWirePoints is the single most load-bearing constant in the
        // payload. Deleting it would not fail any other test.
        var short_ = Prompts.InbetweenUser(Request(20, StrokeWire.MaxWirePoints));
        var long_ = Prompts.InbetweenUser(Request(20, StrokeWire.MaxWirePoints * 8));

        var ratio = (double)long_.Length / short_.Length;
        output.WriteLine(
            $"32 points {short_.Length / 1024.0:F1} KB, 256 points {long_.Length / 1024.0:F1} KB, ratio {ratio:F3}");

        // The same size, not the same string: eight times the points come back
        // resampled to 32, and the resampled coordinates round to a different
        // number of digits. Without the cap this would be 8×.
        Assert.InRange(ratio, 0.98, 1.02);
    }

    [Fact]
    public void TheFixedOverheadIsNotWorthOptimising()
    {
        // Recorded so nobody spends an afternoon shortening the system prompt:
        // it and the schema together are a rounding error beside the strokes.
        var user = Prompts.InbetweenUser(Request(40, 60));
        var fixedPart = Prompts.InbetweenSystem.Length + StrokeSchemas.InbetweenResult.Length;
        var share = 100.0 * fixedPart / (fixedPart + user.Length);

        output.WriteLine(
            $"system {Prompts.InbetweenSystem.Length} B + schema {StrokeSchemas.InbetweenResult.Length} B " +
            $"= {share:F1}% of a 40-stroke request");

        Assert.True(share < 10, $"fixed overhead is now {share:F1}% — that changes the advice in DESIGN-ai-payload.md");
    }

    [Fact]
    public void CostScalesWithStrokeCount_WhichIsWhySendingFewerIsTheRealLever()
    {
        // The claim DESIGN-ai-payload.md makes: halving the strokes halves the
        // cost, exactly, and beats any encoding trick available. Asserting the
        // ratio rather than the sizes keeps it true as the format changes.
        var few = Prompts.InbetweenUser(Request(20, 60)).Length;
        var many = Prompts.InbetweenUser(Request(40, 60)).Length;
        var ratio = (double)many / few;

        output.WriteLine($"20 strokes {few / 1024.0:F1} KB, 40 strokes {many / 1024.0:F1} KB, ratio {ratio:F2}");

        Assert.InRange(ratio, 1.8, 2.2);
    }

    [Fact]
    public void ADrawRequestWithAnEmptyCanvasIsTiny()
    {
        // The common case, and it must not carry the inbetweener's baggage.
        var user = Prompts.DrawUser(new DrawRequest(new SceneInfo(1920, 1080, 24), "a house", []));

        output.WriteLine($"empty-canvas draw request: {user.Length} B");

        Assert.True(user.Length < 512, $"grew to {user.Length} B");
    }
}
