using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Projects;
using Xunit.Abstractions;

namespace Lightbox.Ai.Tests;

/// <summary>
/// The measurement the reading is gated on: <b>does the taxonomy alone
/// measurably improve an inbetween?</b>
/// </summary>
/// <remarks>
/// <para>
/// Stated in <c>docs/DESIGN-subject-reading.md</c> before anything was built,
/// because a gate written afterwards is a rationalisation. Same two keys, same
/// provider, run with and against without, judged on the failures the
/// inbetweener actually has — a limb that swings through the torso, a part that
/// vanishes because it was hidden in one key, a stroke that loses its label.
/// </para>
/// <para>
/// <b>This needs a real provider and is skipped without one.</b> Set
/// <c>LIGHTBOX_MEASURE_KEY</c> (and optionally <c>LIGHTBOX_MEASURE_MODEL</c>)
/// to run it. It is deliberately not a pass/fail assertion on quality: it
/// prints both answers so a person can judge them, because "does this read at
/// 12 fps" is <c>art-director</c>'s call and not a number. What it does assert
/// is the part that <em>is</em> mechanical — that both runs happened, produced
/// usable frames, and cost what the budget says.
/// </para>
/// <para>
/// Three outcomes and each says what to do next, per the design: taxonomy alone
/// closes most of the gap (placement becomes a refinement), taxonomy helps only
/// with placement present (they ship together), or neither moves the needle
/// (the blindness was not the problem, and that finding is worth as much as the
/// feature).
/// </para>
/// </remarks>
public class TaxonomyMeasurementTests(ITestOutputHelper output)
{
    private const int W = 512;
    private const int H = 512;

    private static string? Key => Environment.GetEnvironmentVariable("LIGHTBOX_MEASURE_KEY");
    private static string Model =>
        Environment.GetEnvironmentVariable("LIGHTBOX_MEASURE_MODEL") ?? AnthropicArtist.Model;

    /// <summary>
    /// A swing where the near arm crosses the torso — chosen because it is the
    /// failure the taxonomy is supposed to fix. A blind inbetweener has no way
    /// to know the arm passes in front, and no way to know the torso continues
    /// behind it rather than ending where the arm starts.
    /// </summary>
    private static InbetweenRequest Swing(SubjectTaxonomy? taxonomy) => new(
        new SceneInfo(W, H, 12),
        [
            Stroke("torso", (256, 150), (256, 340)),
            Stroke("near-arm", (256, 190), (150, 260)),
            Stroke("head", (256, 150), (256, 100)),
        ],
        [
            Stroke("torso", (256, 150), (256, 340)),
            Stroke("near-arm", (256, 190), (360, 260)),
            Stroke("head", (256, 150), (256, 100)),
        ],
        [0.5],
        Easing.EaseInOut,
        Taxonomy: taxonomy);

    private static Stroke Stroke(string label, params (double X, double Y)[] points) => new()
    {
        Label = label,
        Color = "#101010",
        Brush = new BrushSettings { Size = 6, Hardness = 0.8 },
        Points = points.Select(p => new StrokePoint(p.X, p.Y, 0.7)).ToList(),
    };

    private static SubjectTaxonomy Biped() => new()
    {
        Kind = "biped",
        Parts =
        [
            new SubjectPart { Name = "torso", Depth = 0 },
            new SubjectPart { Name = "head", Parent = "torso", Depth = 1 },
            new SubjectPart { Name = "near-arm", Parent = "torso", Depth = 2 },
        ],
    };

    /// <summary>
    /// The half of the measurement that needs no provider: the two runs really
    /// do differ, and they differ in exactly one thing.
    /// </summary>
    /// <remarks>
    /// Worth its own test because it is the assumption the live run rests on.
    /// A comparison where the "with" and "without" arms turned out to send the
    /// same bytes would produce two similar answers and a confident wrong
    /// conclusion — the failure mode is not a red test, it is a decision.
    /// </remarks>
    [Fact]
    public void TheTwoArmsOfTheComparisonDifferInExactlyTheTaxonomy()
    {
        var blind = Prompts.InbetweenUser(Swing(null));
        var read = Prompts.InbetweenUser(Swing(Biped()));

        Assert.NotEqual(blind, read);
        Assert.EndsWith(blind, read);           // the only difference is a prefix
        Assert.Contains("near-arm", read[..^blind.Length]);
        Assert.Contains("biped", read[..^blind.Length]);

        output.WriteLine($"blind {blind.Length} B, read {read.Length} B, "
                       + $"prefix {read.Length - blind.Length} B");
    }

    [Fact]
    public async Task DoesTheTaxonomyAloneImproveAnInbetween()
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            // Not silently green: say what did not run and how to run it. The
            // judgement half of this measurement cannot be faked locally, and
            // a test that pretended otherwise would be worse than an absent one.
            output.WriteLine("SKIPPED — no provider. The live measurement did not run.");
            output.WriteLine("Set LIGHTBOX_MEASURE_KEY (and optionally LIGHTBOX_MEASURE_MODEL)");
            output.WriteLine("and re-run: dotnet test tests/Lightbox.Ai.Tests "
                           + "--filter DoesTheTaxonomyAloneImproveAnInbetween");
            return;
        }

        var artist = new AnthropicArtist(Key!, Model);

        var blind = await artist.GenerateInbetweensAsync(Swing(null), CancellationToken.None);
        var read = await artist.GenerateInbetweensAsync(Swing(Biped()), CancellationToken.None);

        output.WriteLine($"model: {Model}");
        Report("without a taxonomy", blind);
        Report("with a taxonomy", read);

        // The mechanical half is assertable; the judgement half is not, and
        // pretending otherwise would turn art-director's veto into a threshold
        // somebody tuned until it passed.
        Assert.Equal(AiOutcome.Success, blind.Outcome);
        Assert.Equal(AiOutcome.Success, read.Outcome);
        Assert.NotEmpty(read.Value!);

        output.WriteLine("");
        output.WriteLine("Judge these side by side against the three outcomes in");
        output.WriteLine("docs/DESIGN-subject-reading.md before building the placement half.");
    }

    private void Report(string label, AiResult<List<InbetweenFrameResult>> result)
    {
        output.WriteLine("");
        output.WriteLine($"--- {label} ---");
        if (result.Outcome != AiOutcome.Success)
        {
            output.WriteLine($"{result.Outcome}: {result.Message}");
            return;
        }

        foreach (var frame in result.Value!)
        {
            output.WriteLine($"t={frame.T:0.##}, {frame.Strokes.Count} strokes");
            foreach (var stroke in frame.Strokes)
            {
                var xs = stroke.Points.Select(p => p.X).ToList();
                var ys = stroke.Points.Select(p => p.Y).ToList();
                output.WriteLine(
                    $"  {stroke.Label ?? "(unlabelled)",-14} {stroke.Points.Count,2} pts  "
                    + $"x {xs.Min():0}–{xs.Max():0}  y {ys.Min():0}–{ys.Max():0}");
            }

            // The three failures worth naming, because they are what a person
            // would otherwise have to spot by eye in a wall of coordinates.
            var labels = frame.Strokes.Select(s => s.Label).ToList();
            foreach (var wanted in new[] { "torso", "near-arm", "head" })
            {
                if (!labels.Contains(wanted)) output.WriteLine($"  MISSING: {wanted}");
            }
            if (labels.Any(string.IsNullOrEmpty)) output.WriteLine("  LOST A LABEL");
        }
    }
}
