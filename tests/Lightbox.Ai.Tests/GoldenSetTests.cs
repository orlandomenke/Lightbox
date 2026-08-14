using Xunit.Abstractions;
using Lightbox.Ai.Golden;
using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Projects;

namespace Lightbox.Ai.Tests;

/// <summary>
/// Phase 2 of <c>docs/DESIGN-ai-correctness.md</c>: the golden set and the
/// capability profile it produces (Q34 — it ships, so an artist can grade their
/// own model).
/// </summary>
/// <remarks>
/// Every test here runs for free and offline. Profiling a real provider costs
/// tokens and needs a key, so what CI can hold is the harness — the scoring,
/// the summarising, the honesty of the coverage report — plus the set itself
/// measured against the one subject that needs no model.
/// </remarks>
public class GoldenSetTests(ITestOutputHelper output)
{
    private static Task<CapabilityProfile> Profile(IAiArtist artist, IReadOnlyList<GoldenPair> pairs, bool full = false) =>
        CapabilityProfiler.ProfileAsync(artist, "test", pairs, full, null, CancellationToken.None);

    /// <summary>
    /// <b>The set is checked against a known quantity, not merely published.</b>
    /// The deterministic engine's behaviour on constructed geometry is not in
    /// question, so a constructed pair it cannot clear is a bug in the pair.
    /// Without this, the set is a claim about other people's models that nobody
    /// ever tested — and Q34's obligation is precisely that it stays honest.
    /// </summary>
    [Fact]
    public async Task TheFreeEngineClearsEveryConstructedPair()
    {
        var profile = await Profile(new FreeEngineArtist(), GoldenSet.Full(), full: true);
        foreach (var line in profile.Lines()) output.WriteLine(line);

        foreach (var outcome in profile.Outcomes)
        {
            Assert.True(outcome.Accepted,
                $"{outcome.PairId} ({outcome.Category}) was refused: {outcome.Note}");
        }
        Assert.Equal(1, profile.SchemaAdherence);
    }

    /// <summary>
    /// The category that ships empty must appear in the output saying so. A row
    /// reading "not measured" is a known gap; a row silently absent is an
    /// unknown one, and the whole point of enumerating categories is to make
    /// the difference visible in every profile.
    /// </summary>
    [Fact]
    public async Task TheOrganicCategoryReportsItselfUnmeasuredRatherThanVanishing()
    {
        var profile = await Profile(new FreeEngineArtist(), GoldenSet.Short());

        Assert.Contains(GoldenCategory.Organic, profile.NotMeasured);
        var organic = Assert.Single(profile.Categories, c => c.Category == GoldenCategory.Organic);
        Assert.False(organic.Measured);
        Assert.Contains("not measured", organic.Headline);
        Assert.Contains("hand-drawn", organic.Headline);

        // And it reaches the artist-facing text, not only the record.
        Assert.Contains(profile.Lines(), l => l.Contains("Organic") && l.Contains("not measured"));
    }

    /// <summary>
    /// The number the design calls the most valuable and says nobody measures:
    /// where a model stops coping with stroke count. Reported as the largest
    /// count still cleared, because what a caller needs is what is safe to send.
    /// </summary>
    [Fact]
    public async Task TheLadderFindsWhereAModelStopsCoping()
    {
        // Fails any pair past eight strokes, cleanly — the shape of a small
        // local model running out of context rather than misbehaving.
        var profile = await Profile(new CapAtArtist(8), GoldenSet.Full(), full: true);
        foreach (var line in profile.Lines()) output.WriteLine(line);

        Assert.Equal(8, profile.DegradesPastStrokes);
        Assert.Contains(profile.Lines(), l => l.Contains("Degrades past 8 strokes"));
    }

    /// <summary>
    /// Clearing every rung is not the same as having no limit, and the profile
    /// has to say which it found. A short run that clears 24 has proved nothing
    /// about 60, and a null that printed as "no limit" would be the profile
    /// overclaiming in exactly the way the Organic row exists to prevent.
    /// </summary>
    [Fact]
    public async Task ClearingEveryRungIsReportedAsACeilingNotAsNoLimit()
    {
        var profile = await Profile(new FreeEngineArtist(), GoldenSet.Short());

        Assert.Null(profile.DegradesPastStrokes);
        Assert.Contains(profile.Lines(), l => l.Contains("Cleared every rung up to 24"));
        Assert.DoesNotContain(profile.Lines(), l => l.Contains("Degrades past"));
    }

    /// <summary>
    /// <b>The Arc row has to separate a model that arced from one that did not,
    /// and the verifier cannot do it.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// A chord interpolation is <em>exactly</em> between the keys, so
    /// betweenness accepts it and the row would read "clean" for a model that
    /// added nothing. That is the check-that-cannot-fire failure, and it is why
    /// the row carries departure from the free engine's answer instead of a
    /// pass mark. Caught by running the set against the free engine and reading
    /// the row it produced about itself.
    /// </para>
    /// <para>
    /// Departure is reported and never thresholded — Q33: distance from the
    /// deterministic answer is evidence about cost, never about correctness. So
    /// this asserts the two models are <em>told apart</em>, not that one of them
    /// is wrong.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheArcRowTellsAnArcApartFromAChord()
    {
        var chord = await Profile(new FreeEngineArtist(), GoldenSet.Short());
        var arcing = await Profile(new ArcingArtist(28), GoldenSet.Short());

        string Row(CapabilityProfile p) => p.Categories.Single(c => c.Category == GoldenCategory.Arc).Headline;
        output.WriteLine($"chord : {Row(chord)}");
        output.WriteLine($"arcing: {Row(arcing)}");

        // Both are accepted — that is the point, and why acceptance cannot be
        // the signal here.
        Assert.All(chord.Outcomes.Where(o => o.Category == GoldenCategory.Arc), o => Assert.True(o.Accepted));
        Assert.All(arcing.Outcomes.Where(o => o.Category == GoldenCategory.Arc), o => Assert.True(o.Accepted));

        Assert.Contains("no arc", Row(chord));
        Assert.Contains("departed", Row(arcing));
        Assert.NotEqual(Row(chord), Row(arcing));
    }

    /// <summary>
    /// Q18 left the label-adherence claim unmeasured and said it belonged in
    /// the golden set's scores. It matters past tidiness: labels are
    /// <c>StrokeMatcher</c>'s fast path, so a model that drops them makes every
    /// later inbetween of its own output harder to match.
    /// </summary>
    [Fact]
    public async Task LabelRetentionIsMeasuredRatherThanAssumed()
    {
        var kept = await Profile(new FreeEngineArtist(), GoldenSet.Short());
        var dropped = await Profile(new LabelDroppingArtist(), GoldenSet.Short());
        output.WriteLine($"kept {kept.LabelRetention:P0}, dropped {dropped.LabelRetention:P0}");

        Assert.Equal(1, kept.LabelRetention);
        Assert.Equal(0, dropped.LabelRetention);
    }

    /// <summary>
    /// "Returned nothing" and "returned nonsense" are different problems with
    /// different fixes — the connection tester's distinction, kept here because
    /// a profile that scored them the same would tell an artist to change the
    /// wrong thing.
    /// </summary>
    [Fact]
    public async Task NotAnsweringAndAnsweringBadlyAreScoredApart()
    {
        var silent = await Profile(new FailingArtist(), GoldenSet.Short());
        var nonsense = await Profile(new NonsenseArtist(), GoldenSet.Short());
        output.WriteLine($"silent: schema {silent.SchemaAdherence:P0}; nonsense: schema {nonsense.SchemaAdherence:P0}");

        Assert.Equal(0, silent.SchemaAdherence);
        Assert.Equal(1, nonsense.SchemaAdherence);
        Assert.All(nonsense.Outcomes, o => Assert.False(o.Accepted));
        Assert.All(nonsense.Outcomes, o => Assert.NotNull(o.Note));
    }

    /// <summary>
    /// An artist is not supposed to throw, and one that does is a finding about
    /// that artist. Losing the rest of the run to it would throw away every
    /// rung already paid for.
    /// </summary>
    [Fact]
    public async Task AnArtistThatThrowsBecomesAResultRatherThanEndingTheRun()
    {
        var profile = await Profile(new ThrowingArtist(), GoldenSet.Short());

        Assert.Equal(GoldenSet.Short().Count, profile.Outcomes.Count);
        Assert.All(profile.Outcomes, o => Assert.False(o.Answered));
        Assert.All(profile.Outcomes, o => Assert.Contains("threw", o.Note!));
    }

    /// <summary>
    /// The short run is the default and has to be materially cheaper than the
    /// full one, or the choice is decoration. Following the brush rule:
    /// expensive is available, deliberate, never the default.
    /// </summary>
    [Fact]
    public void TheShortRunCostsAFractionOfTheFullOneAndSaysSoBeforeSpending()
    {
        var shortChars = CapabilityProfiler.EstimatedPayloadChars(GoldenSet.Short());
        var fullChars = CapabilityProfiler.EstimatedPayloadChars(GoldenSet.Full());
        output.WriteLine($"short {shortChars:N0} chars, full {fullChars:N0} chars, ratio {(double)fullChars / shortChars:F1}×");

        Assert.True(shortChars > 0);
        Assert.True(fullChars > shortChars * 2,
            $"the full run is meant to be the expensive one: {shortChars:N0} vs {fullChars:N0}");
    }

    /// <summary>
    /// Every pair says what it is for, and no two share an id — the id is how a
    /// rung is cited from a profile row, and <c>Probes</c> is what makes a low
    /// score actionable rather than merely bad news.
    /// </summary>
    [Fact]
    public void EveryPairIsIdentifiedAndExplained()
    {
        var pairs = GoldenSet.Full();
        Assert.Equal(pairs.Count, pairs.Select(p => p.Id).Distinct().Count());
        Assert.All(pairs, p => Assert.False(string.IsNullOrWhiteSpace(p.Probes)));
        Assert.All(pairs, p => Assert.NotEmpty(p.Request.KeyframeA));
        Assert.All(pairs, p => Assert.NotEmpty(p.Request.KeyframeB));
    }

    // ---- stub artists --------------------------------------------------------

    private abstract class StubArtist : IAiArtist
    {
        public abstract Task<AiResult<List<InbetweenFrameResult>>> GenerateInbetweensAsync(
            InbetweenRequest request, CancellationToken ct);

        public Task<AiResult<SubjectTaxonomy>> ReadSubjectAsync(SubjectRequest request, CancellationToken ct) =>
            Task.FromResult(AiResult<SubjectTaxonomy>.Refused("stub"));
    }

    /// <summary>Answers correctly up to a stroke count, then fails outright.</summary>
    private sealed class CapAtArtist(int max) : StubArtist
    {
        private readonly FreeEngineArtist _free = new();

        public override Task<AiResult<List<InbetweenFrameResult>>> GenerateInbetweensAsync(
            InbetweenRequest request, CancellationToken ct) =>
            request.KeyframeA.Count > max
                ? Task.FromResult(AiResult<List<InbetweenFrameResult>>.Error("out of context", retryable: false))
                : _free.GenerateInbetweensAsync(request, ct);
    }

    /// <summary>
    /// The free engine's answer pushed sideways off the chord — a model that
    /// interpreted rather than interpolated. Deliberately still between the
    /// keys, so acceptance cannot be what tells it apart.
    /// </summary>
    private sealed class ArcingArtist(double bulge) : StubArtist
    {
        private readonly FreeEngineArtist _free = new();

        public override async Task<AiResult<List<InbetweenFrameResult>>> GenerateInbetweensAsync(
            InbetweenRequest request, CancellationToken ct)
        {
            var answer = await _free.GenerateInbetweensAsync(request, ct);
            foreach (var stroke in answer.Value!.SelectMany(f => f.Strokes))
            {
                stroke.Points = [.. stroke.Points.Select(p => new StrokePoint(p.X, p.Y + bulge, p.Pressure))];
            }
            return answer;
        }
    }

    /// <summary>Right geometry, no labels.</summary>
    private sealed class LabelDroppingArtist : StubArtist
    {
        private readonly FreeEngineArtist _free = new();

        public override async Task<AiResult<List<InbetweenFrameResult>>> GenerateInbetweensAsync(
            InbetweenRequest request, CancellationToken ct)
        {
            var answer = await _free.GenerateInbetweensAsync(request, ct);
            foreach (var stroke in answer.Value!.SelectMany(f => f.Strokes)) stroke.Label = null;
            return answer;
        }
    }

    private sealed class FailingArtist : StubArtist
    {
        public override Task<AiResult<List<InbetweenFrameResult>>> GenerateInbetweensAsync(
            InbetweenRequest request, CancellationToken ct) =>
            Task.FromResult(AiResult<List<InbetweenFrameResult>>.Error("no credit", retryable: false));
    }

    /// <summary>
    /// Well-formed strokes nowhere near the keys — the small-local-model failure
    /// the connection tester was built for, at profile scale.
    /// </summary>
    private sealed class NonsenseArtist : StubArtist
    {
        public override Task<AiResult<List<InbetweenFrameResult>>> GenerateInbetweensAsync(
            InbetweenRequest request, CancellationToken ct)
        {
            var frames = request.Ts
                .Select(t => new InbetweenFrameResult(t,
                [
                    new Stroke
                    {
                        Label = "scribble",
                        Points = [new(1, 1, 0.5), new(4, 4, 0.5)],
                        Brush = new BrushSettings { Size = 4 },
                    },
                ]))
                .ToList();
            return Task.FromResult(AiResult<List<InbetweenFrameResult>>.Success(frames));
        }
    }

    private sealed class ThrowingArtist : StubArtist
    {
        public override Task<AiResult<List<InbetweenFrameResult>>> GenerateInbetweensAsync(
            InbetweenRequest request, CancellationToken ct) =>
            throw new InvalidOperationException("socket closed");
    }
}
