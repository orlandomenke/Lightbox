using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;

namespace Lightbox.Ai.Golden;

/// <summary>How one pair went.</summary>
/// <param name="Answered">Something parseable came back. Separate from
/// <paramref name="Accepted"/> for the connection tester's reason: "returned
/// nonsense" and "returned nothing" need different fixes.</param>
/// <param name="Note">The refusal, or the transport failure, in a sentence.</param>
/// <param name="DepartureFromFree">
/// How far the answer sits from the deterministic one, in pixels of centroid
/// distance, or null when nothing came back.
/// </param>
/// <remarks>
/// <para>
/// <b><paramref name="DepartureFromFree"/> is a diagnostic and never a score,
/// and Q33 is why.</b> Distance from the deterministic answer is evidence about
/// *cost*, never about correctness — far from it on a dog means very little,
/// and close to it on a dog might mean both answers are wrong the same way. So
/// it is reported and never thresholded.
/// </para>
/// <para>
/// It is still the most useful number in the Arc row, because it is the only
/// one that separates a model that arced from a model that interpolated along
/// the chord. The verifier cannot: it scores betweenness, and a chord
/// interpolation is *exactly* between the keys. Without this the Arc category
/// would pass everything and measure nothing — the "check that cannot fire"
/// failure, which is worse than not having the check.
/// </para>
/// <para>
/// It reads two ways, which is the point. Near zero means the model added
/// nothing and was not worth paying for. Large means it interpreted, which may
/// be the arc that was wanted or may be invention — the verifier decides which,
/// and <paramref name="Accepted"/> already carries that.
/// </para>
/// </remarks>
public sealed record GoldenOutcome(
    string PairId,
    GoldenCategory Category,
    bool Answered,
    bool Accepted,
    double LabelRetention,
    string? Note,
    double? DepartureFromFree = null);

/// <summary>One category's row in the profile.</summary>
/// <param name="Headline">
/// The row as a person reads it. "not measured — no pairs" for a category that
/// ships empty, which is the whole reason it appears at all.
/// </param>
public sealed record CapabilityScore(
    GoldenCategory Category,
    int Pairs,
    int Answered,
    int Accepted,
    string Headline)
{
    public bool Measured => Pairs > 0;
}

/// <summary>
/// What a model can and cannot do, measured rather than assumed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Numbers and a headline per category; no overall pass or fail.</b> The
/// useful output is a plan for shaping the request — the adaptation table in
/// <c>docs/DESIGN-ai-correctness.md</c> — and a boolean cannot carry it. A
/// model weak on arcs is still worth using where the free engine is weak, and a
/// model that degrades past twelve strokes is fine if it is sent ten. Collapsing
/// that to "unusable" would throw away the one number the design calls the most
/// valuable and nobody measures.
/// </para>
/// <para>
/// This phase <b>measures only</b>. Nothing here changes what is sent to a
/// model; adapting the request to the profile is phase 4, and keeping the two
/// apart is what lets the measurement be trusted when it arrives.
/// </para>
/// </remarks>
/// <param name="DegradesPastStrokes">
/// The largest stroke count the model still handled, or null when it cleared
/// every rung it was given. Read it with <paramref name="LadderCeiling"/>: a
/// short run that clears 24 has not proved anything about 60.
/// </param>
/// <param name="NotMeasured">
/// Categories with no pairs. Never empty in a shipped build —
/// <see cref="GoldenCategory.Organic"/> lives here until somebody draws its
/// pairs — and printing it is what stops an unasked question reading as a
/// passed one.
/// </param>
public sealed record CapabilityProfile(
    string Subject,
    bool FullRun,
    IReadOnlyList<CapabilityScore> Categories,
    IReadOnlyList<GoldenOutcome> Outcomes,
    double SchemaAdherence,
    double LabelRetention,
    int? DegradesPastStrokes,
    int LadderCeiling,
    IReadOnlyList<GoldenCategory> NotMeasured)
{
    /// <summary>The profile as an artist reads it, one line per fact.</summary>
    public IReadOnlyList<string> Lines()
    {
        var lines = new List<string>
        {
            $"{Subject} — {(FullRun ? "full" : "short")} run, {Outcomes.Count} pair(s)",
            $"Schema adherence: {SchemaAdherence:P0}",
            $"Label retention: {LabelRetention:P0}",
            DegradesPastStrokes is { } n
                ? $"Degrades past {n} strokes — send it fewer than that."
                : $"Cleared every rung up to {LadderCeiling} strokes; nothing above that was asked.",
        };
        lines.AddRange(Categories.Select(c => $"{c.Category}: {c.Headline}"));
        return lines;
    }
}

/// <summary>
/// Runs the golden set against a model and reports what it can do.
/// </summary>
/// <remarks>
/// <para>
/// The generalisation of <see cref="AiConnectionTester"/> rather than a second
/// copy of it: that class asks two hard-coded pairs and returns a verdict, this
/// one asks a set and returns a shape. Both score with
/// <c>InbetweenVerifier</c>, so a model the tester certifies and a model this
/// profiles are being held to the same standard as the pipeline itself.
/// </para>
/// <para>
/// <b>It never throws and never partially fails.</b> A pair whose request errors
/// is an outcome with <c>Answered: false</c>, because "this model timed out on
/// the 64-stroke rung" is exactly the finding, not an exception to propagate.
/// </para>
/// </remarks>
public static class CapabilityProfiler
{
    /// <summary>
    /// What running this set will cost, in payload characters, before any of it
    /// is spent.
    /// </summary>
    /// <remarks>
    /// Characters rather than tokens, and deliberately not converted: the ratio
    /// is model-specific and a number dressed up as tokens would be a guess
    /// wearing a uniform. `docs/DESIGN-ai-payload.md` has the measured
    /// conversion for the providers that were measured; this is the honest raw
    /// figure a caller can convert or simply compare between the short and full
    /// runs.
    /// </remarks>
    public static long EstimatedPayloadChars(IReadOnlyList<GoldenPair> pairs) =>
        pairs.Sum(p => (long)Prompts.InbetweenUser(p.Request).Length);

    public static async Task<CapabilityProfile> ProfileAsync(
        IAiArtist artist,
        string subject,
        IReadOnlyList<GoldenPair> pairs,
        bool fullRun,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var outcomes = new List<GoldenOutcome>(pairs.Count);
        foreach (var pair in pairs)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"{pair.Id}: {pair.Probes}");
            outcomes.Add(await RunAsync(artist, pair, ct));
        }
        return Summarise(subject, fullRun, pairs, outcomes);
    }

    private static async Task<GoldenOutcome> RunAsync(IAiArtist artist, GoldenPair pair, CancellationToken ct)
    {
        AiResult<List<InbetweenFrameResult>> result;
        try
        {
            result = await artist.GenerateInbetweensAsync(pair.Request, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // An artist is not supposed to throw. One that does is a finding
            // about that artist, and losing the rest of the run to it would
            // throw away every rung already paid for.
            return new GoldenOutcome(pair.Id, pair.Category, false, false, 0, $"threw: {e.Message}");
        }

        if (result.Outcome != AiOutcome.Success || result.Value is not { Count: > 0 } frames)
            return new GoldenOutcome(pair.Id, pair.Category, false, false, 0, result.Message ?? "no frames");

        var candidates = frames
            .OrderBy(f => f.T)
            .Select(f => new CandidateInbetween(f.T, f.Strokes))
            .ToList();
        var judgement = InbetweenVerifier.Verify(
            pair.Request.KeyframeA, pair.Request.KeyframeB, candidates, pair.Request.Easing);

        var accepted = judgement.AllAccepted;
        var refusal = judgement.Frames.FirstOrDefault(f => !f.Accepted)?.Refusal;
        return new GoldenOutcome(
            pair.Id, pair.Category, true, accepted,
            LabelRetentionOf(pair.Request.KeyframeA, pair.Request.KeyframeB, frames.SelectMany(f => f.Strokes)),
            refusal,
            DepartureFrom(pair, candidates));
    }

    /// <summary>
    /// How far the model's answer sits from the deterministic one at the same
    /// <c>t</c>, measured along matched strokes.
    /// </summary>
    /// <remarks>
    /// <b>Per stroke and along its length — B360, and the old version failed at
    /// the one job this number has.</b> It used to flatten every stroke in the
    /// frame into a single point cloud and compare two centroids, which cannot
    /// tell an arc from the same arc drawn backwards: on the `arc` golden pair
    /// the correct answer and its mirror image both reported **21.6 px**, for
    /// drawings whose tips were 154 px apart. Since the whole reason this exists
    /// is to separate a model that arced from one that interpolated along the
    /// chord, a measure that pairs a good arc with a wrong one was reporting
    /// noise in the Arc row and calling it evidence.
    /// </remarks>
    private static double DepartureFrom(GoldenPair pair, IReadOnlyList<CandidateInbetween> candidates)
    {
        var distances = new List<double>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var free = Inbetweener.Inbetween(
                pair.Request.KeyframeA, pair.Request.KeyframeB, candidate.T, pair.Request.Easing);
            if (free.Count == 0 || candidate.Strokes.Count == 0) continue;
            // The same matcher the verifier uses, so "which stroke is which"
            // has one answer across the two halves of the correctness machinery.
            var perStroke = StrokeMatcher.Match(free, candidate.Strokes)
                .Where(m => m.A is not null && m.B is not null)
                .Select(m => GeometryOps.ShapeDeviation(m.A!.Points, m.B!.Points))
                .ToList();
            if (perStroke.Count > 0) distances.Add(perStroke.Average());
        }
        return distances.Count == 0 ? 0 : distances.Average();
    }

    /// <summary>
    /// The share of labels present in <em>both</em> keys that came back on the
    /// answer (Q18).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Q18 traded stroke labels' safety for 57% fewer payload bytes and left the
    /// adherence claim unmeasured, with a note that it belongs in the golden
    /// set's scores. This is that measurement. It matters past tidiness: labels
    /// are <c>StrokeMatcher</c>'s fast path, so a model that drops them makes
    /// every later inbetween of its own output harder to match.
    /// </para>
    /// <para>
    /// <b>Both keys, not key A — and the first version of this was wrong.</b>
    /// Scoring against A's labels marks a model down for a stroke that is
    /// *supposed* to disappear: the occlusion pair has a torso drawn in two
    /// pieces in A and whole in B, so <c>torso-lower</c> has no counterpart and
    /// correctly fades out by the midpoint. The free engine scored 95% on its
    /// own output, which is the metric being wrong rather than the engine.
    /// Labels common to both keys are the ones that name something present
    /// throughout, and those are the ones whose loss actually costs a later
    /// match.
    /// </para>
    /// </remarks>
    private static double LabelRetentionOf(
        IReadOnlyList<Stroke> keyA, IReadOnlyList<Stroke> keyB, IEnumerable<Stroke> answer)
    {
        var inB = keyB.Select(s => s.Label).OfType<string>().ToHashSet();
        var wanted = keyA.Select(s => s.Label).OfType<string>().Where(inB.Contains).Distinct().ToList();
        if (wanted.Count == 0) return 1;
        var got = answer.Select(s => s.Label).OfType<string>().ToHashSet();
        return wanted.Count(got.Contains) / (double)wanted.Count;
    }

    private static CapabilityProfile Summarise(
        string subject,
        bool fullRun,
        IReadOnlyList<GoldenPair> pairs,
        IReadOnlyList<GoldenOutcome> outcomes)
    {
        var scores = new List<CapabilityScore>();
        foreach (var category in Enum.GetValues<GoldenCategory>())
        {
            var rows = outcomes.Where(o => o.Category == category).ToList();
            var answered = rows.Count(o => o.Answered);
            var accepted = rows.Count(o => o.Accepted);
            scores.Add(new CapabilityScore(
                category, rows.Count, answered, accepted,
                Headline(category, rows)));
        }

        var ladder = outcomes.Where(o => o.Category == GoldenCategory.StrokeLadder).ToList();
        var rungs = pairs
            .Where(p => p.Category == GoldenCategory.StrokeLadder)
            .ToDictionary(p => p.Id, p => p.Request.KeyframeA.Count);

        // The largest rung it cleared, and the smallest it did not. Reported as
        // "degrades past the largest cleared" rather than "fails at the smallest
        // failed", because the actionable number is what is still safe to send.
        var cleared = ladder.Where(o => o.Accepted).Select(o => rungs[o.PairId]).DefaultIfEmpty(0).Max();
        var failed = ladder.Where(o => !o.Accepted).Select(o => rungs[o.PairId]).DefaultIfEmpty(int.MaxValue).Min();
        var ceiling = rungs.Values.DefaultIfEmpty(0).Max();

        return new CapabilityProfile(
            subject,
            fullRun,
            scores,
            outcomes,
            Share(outcomes, o => o.Answered),
            outcomes.Where(o => o.Answered).Select(o => o.LabelRetention).DefaultIfEmpty(0).Average(),
            failed == int.MaxValue ? null : cleared,
            ceiling,
            [.. scores.Where(s => !s.Measured).Select(s => s.Category)]);
    }

    private static double Share(IReadOnlyList<GoldenOutcome> rows, Func<GoldenOutcome, bool> predicate) =>
        rows.Count == 0 ? 0 : rows.Count(predicate) / (double)rows.Count;

    private static string Headline(GoldenCategory category, IReadOnlyList<GoldenOutcome> rows)
    {
        if (rows.Count == 0)
        {
            return category == GoldenCategory.Organic
                ? "not measured — ships with no pairs, and needs hand-drawn answers rather than computed ones"
                : "not measured — no pairs in this run";
        }
        var accepted = rows.Count(o => o.Accepted);
        var clean = accepted == rows.Count ? $"clean ({accepted}/{rows.Count})" : null;

        // The Arc row's real content. Betweenness cannot tell an arc from a
        // chord — both are between the keys — so without the departure number
        // this row would say "clean" whatever the model drew.
        if (category == GoldenCategory.Arc && rows.Any(o => o.Answered))
        {
            var departure = rows.Where(o => o.Answered).Select(o => o.DepartureFromFree ?? 0).Average();
            var arcing = departure < 1
                ? "interpolated along the chord — no arc, and nothing the free engine would not have drawn"
                : $"departed {departure:F1} px from the free engine's answer";
            return $"{clean ?? $"{accepted}/{rows.Count}"} — {arcing}";
        }

        if (clean is not null) return clean;
        var first = rows.First(o => !o.Accepted);
        return $"{accepted}/{rows.Count} — {first.PairId}: {first.Note ?? "refused"}";
    }
}
