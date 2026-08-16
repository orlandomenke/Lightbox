using System.Globalization;
using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;

namespace Lightbox.Ai;

/// <summary>One requested t after the repair loop has finished with it.</summary>
/// <remarks>
/// <see cref="Strokes"/> is null exactly when <see cref="Refusal"/> is not —
/// the frame is either defensible or it is not, and there is no third state to
/// insert. <see cref="Attempts"/> is which ask produced the accepted drawing,
/// or how many were spent failing to.
/// </remarks>
public sealed record RepairedFrame(
    double T,
    IReadOnlyList<Stroke>? Strokes,
    string? Refusal,
    int Attempts,
    IReadOnlyList<string> Notes,
    bool MatchesDeterministic = false)
{
    public bool Accepted => Strokes is not null;
}

/// <summary>The whole run, and what it cost in model calls.</summary>
public sealed record RepairedRun(IReadOnlyList<RepairedFrame> Frames, int Calls)
{
    public int AcceptedCount => Frames.Count(f => f.Accepted);

    /// <summary>True when a frame needed more than one ask — the run was repaired, not merely generated.</summary>
    public bool WasRepaired => Calls > 1;

    /// <summary>
    /// Accepted frames that came back matching what the free engine would have
    /// drawn — the *added nothing* signal, counted rather than buried in notes.
    /// </summary>
    /// <remarks>
    /// It matters most on a repaired frame, which is why it is surfaced by a
    /// loop rather than by the verifier's caller. Flattening toward the
    /// deterministic chord is the one move that passes every geometric check
    /// by construction, so it is the path of least resistance for a model
    /// being pushed to correct itself. Q33 says that is a note and never a
    /// veto — but a note nobody can see is not a note, and repair is what
    /// gives a model a reason to take that path.
    /// </remarks>
    public int MatchedFreeEngineCount => Frames.Count(f => f.Accepted && f.MatchesDeterministic);
}

/// <summary>
/// Stage 4 of <c>docs/DESIGN-ai-correctness.md</c>: when the verifier refuses a
/// frame, ask again <b>naming the fault</b> instead of rolling the same dice.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is the harness that repairs, not the model.</b> Nothing here trusts
/// the second answer any more than the first: every round is verified again
/// from scratch, and the run is only replaced when the replacement is strictly
/// better. The guarantee an artist gets out of Q32 — the AI never inserts a
/// frame it cannot defend — is unchanged; repair only changes how many frames
/// clear that bar.
/// </para>
/// <para>
/// <b>A repair can never cost a frame that was already accepted.</b> That is
/// not a nicety, it is the only reason re-asking is safe at all: accepted
/// frames are carried into the next round unchanged, so the single way one of
/// them can newly fail is the coherence check — a repaired neighbour that
/// jitters against it. Adopting such a run would take a frame the artist had
/// already been given, which reads as a bug however the totals came out. So a
/// round is adopted only when every frame accepted before is still accepted
/// <em>and</em> at least one more is; anything else keeps the previous best and
/// spends the remaining budget trying again.
/// </para>
/// <para>
/// <b>Bounded at <see cref="MaxReasks"/> re-asks (Q85).</b> Two was chosen over
/// one deliberately and against the recommendation: it catches the model that
/// fixes the named fault and trips a different check, which is a real and
/// common shape. The cost is that a frame which is never going to work can
/// occupy three calls — 90 to 360 seconds of an artist waiting — before it is
/// refused. Cancellation is honoured between rounds, so the wait is
/// interruptible rather than merely long.
/// </para>
/// <para>
/// <b>On by default</b>, unlike best-of-N. The brush rule says an expensive
/// option is opt-in and never the default, and repair is the case that rule
/// does not cover: best-of-N buys a <em>better</em> frame, repair buys a frame
/// <em>at all</em>. The alternative to spending the call is an empty slot.
/// </para>
/// </remarks>
public static class InbetweenRepair
{
    /// <summary>How many times a refused frame may be re-asked before it is refused for good.</summary>
    public const int MaxReasks = 2;

    /// <summary>
    /// Ask, verify, and re-ask the refused frames with their faults named.
    /// </summary>
    /// <param name="onAttempt">
    /// Called before each re-ask with the attempt number and how many frames it
    /// is trying to repair, so a caller can say so while the artist waits. Not
    /// called for the first ask, which is an ordinary request rather than a
    /// repair.
    /// </param>
    public static async Task<AiResult<RepairedRun>> RunAsync(
        IAiArtist artist,
        InbetweenRequest request,
        CancellationToken ct,
        Action<int, int>? onAttempt = null,
        int maxReasks = MaxReasks)
    {
        var first = await artist.GenerateInbetweensAsync(request, ct);
        if (first.Outcome != AiOutcome.Success || first.Value is null) return Relay(first);

        var ts = request.Ts;
        var best = Judge(request, Align(ts, first.Value));
        var acceptedAt = new int[ts.Count];
        for (var i = 0; i < ts.Count; i++)
            if (best.Strokes[i] is not null && best.Refusals[i] is null) acceptedAt[i] = 1;

        var calls = 1;
        // Why the previous round was thrown away, forwarded into the next
        // re-ask. Without it the loop rebuilds an identical request from an
        // unchanged `best` and sends the same prompt twice — the second and
        // third calls become the blind retry this whole stage exists to
        // replace, and Q85's argument for two re-asks evaporates.
        string? discarded = null;

        for (var attempt = 2; attempt <= 1 + maxReasks; attempt++)
        {
            var refused = Enumerable.Range(0, ts.Count).Where(i => !best.IsAccepted(i)).ToList();
            if (refused.Count == 0) break;
            if (ct.IsCancellationRequested) break;

            onAttempt?.Invoke(attempt, refused.Count);
            var repairRequest = request with
            {
                Ts = refused.Select(i => ts[i]).ToList(),
                Repair = refused.Select(i => new RefusedFrame(
                    ts[i],
                    best.Strokes[i] ?? [],
                    Fault(best, i, discarded))).ToList(),
                Settled = NeighbourContext(best, refused, ts),
            };

            var answer = await artist.GenerateInbetweensAsync(repairRequest, ct);
            calls++;
            // A failed re-ask is not a failed run. The frames already accepted
            // are still defensible and still worth inserting, so a rate limit
            // on the second call must not throw away the first call's work.
            if (answer.Outcome != AiOutcome.Success || answer.Value is null) break;

            var replacements = Align(repairRequest.Ts, answer.Value);
            var trialStrokes = best.Strokes.ToArray();
            for (var r = 0; r < refused.Count; r++)
                if (replacements[r] is { } s) trialStrokes[refused[r]] = s;

            var trial = Judge(request, trialStrokes);
            var lost = Enumerable.Range(0, ts.Count)
                .Where(i => best.IsAccepted(i) && !trial.IsAccepted(i)).ToList();
            var gained = Enumerable.Range(0, ts.Count).Any(i => trial.IsAccepted(i) && !best.IsAccepted(i));
            if (lost.Count > 0 || !gained)
            {
                discarded = WhyDiscarded(lost, ts);
                continue;
            }

            discarded = null;
            for (var i = 0; i < ts.Count; i++)
                if (trial.IsAccepted(i) && !best.IsAccepted(i)) acceptedAt[i] = attempt;
            best = trial;
        }

        var frames = new List<RepairedFrame>(ts.Count);
        for (var i = 0; i < ts.Count; i++)
        {
            var accepted = best.IsAccepted(i);
            frames.Add(new RepairedFrame(
                ts[i],
                accepted ? best.Strokes[i] : null,
                accepted ? null : best.Refusals[i] ?? NothingReturned,
                accepted ? acceptedAt[i] : calls,
                best.Notes[i],
                accepted && best.MatchedFree[i]));
        }
        return AiResult<RepairedRun>.Success(new RepairedRun(frames, calls));
    }

    private const string NothingReturned = "no drawing came back for this frame.";

    /// <summary>The fault to state for this frame, plus what happened to the last correction.</summary>
    private static string Fault(Verdict best, int i, string? discarded) =>
        discarded is null
            ? best.Refusals[i] ?? NothingReturned
            : $"{best.Refusals[i] ?? NothingReturned} {discarded}";

    private static string WhyDiscarded(IReadOnlyList<int> lost, IReadOnlyList<double> ts) =>
        lost.Count == 0
            ? "Your last correction was checked and failed the same way, so it was discarded — "
              + "try a different reading of the motion rather than the same one again."
            : "Your last correction was discarded because it broke the frame(s) at t="
              + string.Join(", t=", lost.Select(i => ts[i].ToString("0.##", CultureInfo.InvariantCulture)))
              + ", which had already been accepted and cannot change. Fit your correction to those.";

    /// <summary>
    /// The accepted frames a coherence repair has to be able to see, or null.
    /// </summary>
    /// <remarks>
    /// Only the immediate accepted neighbours of a jittering frame, and only
    /// when a jitter is actually among the refusals — the other faults are
    /// fully actionable from the keys and the model's own drawing, and paying
    /// a frame's worth of strokes for them would be paying for context nobody
    /// asked for. Deduplicated, because two refused frames either side of one
    /// accepted frame need it once.
    /// </remarks>
    private static IReadOnlyList<AcceptedFrame>? NeighbourContext(
        Verdict best, IReadOnlyList<int> refused, IReadOnlyList<double> ts)
    {
        if (!refused.Any(i => best.Faults[i] == InbetweenFault.Incoherent)) return null;

        var wanted = new SortedSet<int>();
        foreach (var i in refused)
        {
            if (best.Faults[i] != InbetweenFault.Incoherent) continue;
            for (var j = i - 1; j >= 0; j--)
                if (best.IsAccepted(j)) { wanted.Add(j); break; }
            for (var j = i + 1; j < ts.Count; j++)
                if (best.IsAccepted(j)) { wanted.Add(j); break; }
        }
        if (wanted.Count == 0) return null;
        return wanted.Select(j => new AcceptedFrame(ts[j], best.Strokes[j]!)).ToList();
    }

    /// <summary>A run's strokes per requested t, and the verifier's reading of them.</summary>
    private sealed record Verdict(
        IReadOnlyList<Stroke>?[] Strokes,
        string?[] Refusals,
        IReadOnlyList<string>[] Notes,
        InbetweenFault[] Faults,
        bool[] MatchedFree)
    {
        public bool IsAccepted(int i) => Strokes[i] is not null && Refusals[i] is null;
    }

    private static Verdict Judge(InbetweenRequest request, IReadOnlyList<Stroke>?[] strokes)
    {
        var refusals = new string?[strokes.Length];
        var notes = new IReadOnlyList<string>[strokes.Length];
        var faults = new InbetweenFault[strokes.Length];
        var matched = new bool[strokes.Length];
        for (var i = 0; i < strokes.Length; i++) notes[i] = [];

        var indices = new List<int>();
        var candidates = new List<CandidateInbetween>();
        for (var i = 0; i < strokes.Length; i++)
        {
            if (strokes[i] is not { } s)
            {
                refusals[i] = NothingReturned;
                faults[i] = InbetweenFault.Unusable;
                continue;
            }
            indices.Add(i);
            candidates.Add(new CandidateInbetween(request.Ts[i], s));
        }

        // The whole run is judged every round, never only the frames that
        // changed. Temporal coherence is a property of neighbours, so a
        // repaired frame can only be checked for boiling against the frames it
        // will actually sit between — which is the reason the verifier takes a
        // run rather than a frame in the first place.
        var judgement = InbetweenVerifier.Verify(
            request.KeyframeA, request.KeyframeB, candidates, request.Easing);
        for (var k = 0; k < indices.Count; k++)
        {
            refusals[indices[k]] = judgement.Frames[k].Refusal;
            notes[indices[k]] = judgement.Frames[k].Notes;
            faults[indices[k]] = judgement.Frames[k].Fault;
            matched[indices[k]] = judgement.Frames[k].MatchesDeterministic;
        }
        return new Verdict(strokes, refusals, notes, faults, matched);
    }

    /// <summary>
    /// Line the model's answer up with the ts that were asked for.
    /// </summary>
    /// <remarks>
    /// A model may echo the ts back in another order, round them, or return
    /// fewer frames than were asked for. Matching by position would then hand a
    /// t's slot a drawing meant for a different one — a silently mistimed
    /// sequence rather than a visible failure. Nearest-t within half the
    /// smallest requested gap keeps every answer with its own frame, and a t
    /// nothing came back for stays empty and is refused by name.
    /// </remarks>
    private static IReadOnlyList<Stroke>?[] Align(
        IReadOnlyList<double> ts, IReadOnlyList<InbetweenFrameResult> answer)
    {
        var slots = new IReadOnlyList<Stroke>?[ts.Count];
        var tolerance = ToleranceFor(ts);
        var taken = new bool[answer.Count];
        for (var i = 0; i < ts.Count; i++)
        {
            var pick = -1;
            var nearest = double.PositiveInfinity;
            for (var j = 0; j < answer.Count; j++)
            {
                if (taken[j]) continue;
                var d = Math.Abs(answer[j].T - ts[i]);
                if (d < nearest) { nearest = d; pick = j; }
            }
            if (pick >= 0 && nearest <= tolerance)
            {
                slots[i] = answer[pick].Strokes;
                taken[pick] = true;
            }
        }
        return slots;
    }

    private static double ToleranceFor(IReadOnlyList<double> ts)
    {
        var gap = double.PositiveInfinity;
        for (var i = 1; i < ts.Count; i++) gap = Math.Min(gap, Math.Abs(ts[i] - ts[i - 1]));
        return double.IsInfinity(gap) ? 0.01 : Math.Min(0.01, gap / 2);
    }

    private static AiResult<RepairedRun> Relay<T>(AiResult<T> failed) => failed.Outcome switch
    {
        AiOutcome.Refused => AiResult<RepairedRun>.Refused(failed.Message ?? "The model refused."),
        AiOutcome.Truncated => AiResult<RepairedRun>.Truncated(failed.Message ?? "The answer was cut off."),
        _ => AiResult<RepairedRun>.Error(failed.Message ?? "AI request failed.", failed.Retryable),
    };
}
