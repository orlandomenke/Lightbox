using Xunit.Abstractions;
using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Projects;

namespace Lightbox.Ai.Tests;

/// <summary>
/// Phase 3 of <c>docs/DESIGN-ai-correctness.md</c>: the repair loop. A refused
/// frame is asked again with the fault named and its own rejected drawing
/// handed back, bounded at <see cref="InbetweenRepair.MaxReasks"/> re-asks
/// (Q85), and refused for good if it still cannot be defended.
/// </summary>
/// <remarks>
/// Every test here is offline. What needs proving is not that a model improves
/// — no test can make one do that — but that the <em>harness</em> behaves:
/// that the re-ask carries the information it claims to, that the budget is
/// actually bounded, that a failed re-ask does not throw away a good first
/// call, and above all that repairing one frame can never take away another.
/// </remarks>
public class InbetweenRepairTests(ITestOutputHelper output)
{
    /// <summary>
    /// An artist that reads scripted answers off a queue and keeps every
    /// request it was handed, so the re-ask can be inspected.
    /// </summary>
    private sealed class ScriptedArtist(params AiResult<List<InbetweenFrameResult>>[] answers) : IAiArtist
    {
        private int _next;

        public List<InbetweenRequest> Requests { get; } = [];

        public Task<AiResult<List<InbetweenFrameResult>>> GenerateInbetweensAsync(
            InbetweenRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            // The last scripted answer repeats, so "this model never improves"
            // is one entry rather than one per permitted attempt.
            var answer = answers[Math.Min(_next, answers.Length - 1)];
            _next++;
            return Task.FromResult(answer);
        }

        public Task<AiResult<SubjectTaxonomy>> ReadSubjectAsync(SubjectRequest request, CancellationToken ct) =>
            Task.FromResult(AiResult<SubjectTaxonomy>.Refused("not under test"));
    }

    // An arm swinging 40 → 216 down the canvas: travel 176, so betweenness
    // allows about 81px of deviation and coherence about 29px. Every number
    // below is chosen against those two bands on purpose — see
    // ARepairThatWouldCostAnAlreadyAcceptedFrameIsNotAdopted.
    private const double Travel = 176;

    private static Stroke Arm(double y) => new()
    {
        Label = "arm",
        Points = [new(40, y, 0.6), new(216, y, 0.6)],
        Brush = new BrushSettings { Size = 4 },
    };

    private static InbetweenRequest Swing(params double[] ts) =>
        new(new SceneInfo(256, 256, 12), [Arm(40)], [Arm(216)], ts, Easing.Linear);

    /// <summary>Where linear interpolation puts the arm at this t.</summary>
    private static double Straight(double t) => 40 + Travel * t;

    private static AiResult<List<InbetweenFrameResult>> Answer(params (double T, double Y)[] frames) =>
        AiResult<List<InbetweenFrameResult>>.Success(
            frames.Select(f => new InbetweenFrameResult(f.T, [Arm(f.Y)])).ToList());

    private static Task<AiResult<RepairedRun>> Run(IAiArtist artist, InbetweenRequest request) =>
        InbetweenRepair.RunAsync(artist, request, CancellationToken.None);

    [Fact]
    public async Task AnAnswerThatIsRightFirstTimeCostsOneCallAndRecordsNoRepair()
    {
        var request = Swing(0.25, 0.5, 0.75);
        var artist = new ScriptedArtist(Answer(
            (0.25, Straight(0.25)), (0.5, Straight(0.5)), (0.75, Straight(0.75))));

        var run = (await Run(artist, request)).Value!;

        Assert.Equal(1, run.Calls);
        Assert.Single(artist.Requests);
        Assert.Equal(3, run.AcceptedCount);
        Assert.All(run.Frames, f => Assert.Equal(1, f.Attempts));
        Assert.False(run.WasRepaired);
    }

    /// <summary>
    /// The whole point of the phase: the re-ask names the fault <b>and</b>
    /// hands back the drawing that earned it, so the model edits rather than
    /// redrawing from the keys with a hint.
    /// </summary>
    [Fact]
    public async Task TheReAskCarriesTheFaultAndTheDrawingThatEarnedIt()
    {
        var request = Swing(0.5);
        // Far outside the keys — 400 against a band of about 81.
        var artist = new ScriptedArtist(Answer((0.5, 400)));

        await Run(artist, request);

        Assert.Equal(3, artist.Requests.Count);
        var repair = artist.Requests[1];
        Assert.NotNull(repair.Repair);
        var refused = Assert.Single(repair.Repair!);

        Assert.Equal(0.5, refused.T);
        Assert.Contains("did not stay between the keys", refused.Fault);
        // Its own answer, not the keys: the y it was refused for.
        Assert.Equal(400, Assert.Single(refused.Strokes).Points[0].Y);
        output.WriteLine(refused.Fault);

        // And the prompt actually says so, rather than the record merely
        // carrying it — a repair block nobody renders is a blind retry.
        var prompt = Prompts.InbetweenUser(repair);
        Assert.Contains("did not stay between the keys", prompt);
        Assert.Contains("yourStrokes", prompt);
    }

    /// <summary>
    /// A repair asks only for what was refused, and changes nothing else about
    /// the request — the keys, the scene and the easing are the same question.
    /// </summary>
    [Fact]
    public async Task ARepairNarrowsTheTsAndLeavesEverythingElseAlone()
    {
        var request = Swing(0.25, 0.5, 0.75);
        var artist = new ScriptedArtist(Answer(
            (0.25, Straight(0.25)), (0.5, 400), (0.75, Straight(0.75))));

        await Run(artist, request);

        var repair = artist.Requests[1];
        Assert.Equal([0.5], repair.Ts);
        Assert.Same(request.KeyframeA, repair.KeyframeA);
        Assert.Same(request.KeyframeB, repair.KeyframeB);
        Assert.Equal(request.Easing, repair.Easing);
    }

    [Fact]
    public async Task AModelThatCorrectsItselfGetsItsFrameInAndTheFrameSaysItTookTwo()
    {
        var request = Swing(0.5);
        var artist = new ScriptedArtist(
            Answer((0.5, 400)),
            Answer((0.5, Straight(0.5))));

        var run = (await Run(artist, request)).Value!;

        var frame = Assert.Single(run.Frames);
        Assert.True(frame.Accepted);
        Assert.Equal(2, frame.Attempts);
        Assert.Equal(2, run.Calls);
        Assert.True(run.WasRepaired);
    }

    /// <summary>
    /// Bounded, and the bound is the reason repair is safe to have on by
    /// default. Three calls total — the ask and two re-asks (Q85) — then the
    /// frame is refused with the fault it kept failing.
    /// </summary>
    [Fact]
    public async Task AModelThatNeverImprovesIsRefusedAfterThreeCalls()
    {
        var request = Swing(0.5);
        var artist = new ScriptedArtist(Answer((0.5, 400)));

        var run = (await Run(artist, request)).Value!;

        Assert.Equal(3, artist.Requests.Count);
        Assert.Equal(3, run.Calls);
        var frame = Assert.Single(run.Frames);
        Assert.False(frame.Accepted);
        Assert.Null(frame.Strokes);
        Assert.Contains("did not stay between the keys", frame.Refusal);
        Assert.Equal(3, frame.Attempts);
    }

    /// <summary>
    /// <b>The guarantee that makes re-asking safe.</b> An accepted frame is
    /// carried into the next round untouched, so the only way it can newly fail
    /// is coherence — a repaired neighbour that makes it jitter. Here the frame
    /// at t=0.3 sits 40px off the straight path: fine on its own (the
    /// betweenness band is ~81px) and fine while it is the run's first frame,
    /// but once t=0.15 arrives and puts a smooth neighbour either side of it,
    /// that 40px becomes a zigzag against a ~29px coherence band.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The round is deliberately a <b>net</b> improvement — two frames gained
    /// against one lost — because that is the case a count comparison gets
    /// wrong and the only case worth a test. Adopting it would trade a frame
    /// the artist already had for two different ones, which reads as a bug
    /// however the totals come out.
    /// </para>
    /// <para>
    /// So the round is dropped whole: 0.15 and 0.45 stay refused and 0.3 keeps
    /// the drawing it was given. Verified against the weaker rule — swapping
    /// the check for "more accepted than before" makes this the one test in
    /// the file that fails.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ARepairThatWouldCostAnAlreadyAcceptedFrameIsNotAdoptedEvenWhenItGainsTwo()
    {
        var request = Swing(0.15, 0.3, 0.45, 0.6, 0.75);
        var artist = new ScriptedArtist(
            // Nothing for 0.15 or 0.45, and 0.3 bowed off the straight path.
            Answer((0.3, Straight(0.3) + 40), (0.6, Straight(0.6)), (0.75, Straight(0.75))),
            // Two perfectly good frames — which is exactly what turns 0.3 into a jitter.
            Answer((0.15, Straight(0.15)), (0.45, Straight(0.45))));

        var run = (await Run(artist, request)).Value!;

        var at = run.Frames.ToDictionary(f => f.T);
        output.WriteLine(string.Join("\n", run.Frames.Select(f =>
            $"t={f.T:0.00} accepted={f.Accepted} attempts={f.Attempts} {f.Refusal}")));

        Assert.True(at[0.3].Accepted);
        Assert.Equal(Straight(0.3) + 40, at[0.3].Strokes![0].Points[0].Y);
        Assert.Equal(1, at[0.3].Attempts);
        Assert.False(at[0.15].Accepted);
        Assert.False(at[0.45].Accepted);
        Assert.True(at[0.6].Accepted);
        Assert.True(at[0.75].Accepted);
    }

    /// <summary>
    /// A rate limit on the second call is not a failed run. The frames the
    /// first call earned are still defensible and still worth inserting.
    /// </summary>
    [Fact]
    public async Task AFailedReAskKeepsTheFramesTheFirstCallEarned()
    {
        var request = Swing(0.25, 0.5);
        var artist = new ScriptedArtist(
            Answer((0.25, Straight(0.25)), (0.5, 400)),
            AiResult<List<InbetweenFrameResult>>.Error("rate limited", retryable: true));

        var result = await Run(artist, request);

        Assert.Equal(AiOutcome.Success, result.Outcome);
        var run = result.Value!;
        Assert.Equal(1, run.AcceptedCount);
        Assert.True(run.Frames[0].Accepted);
        Assert.False(run.Frames[1].Accepted);
        // It stopped rather than spending the second re-ask on a broken pipe.
        Assert.Equal(2, run.Calls);
    }

    /// <summary>
    /// A first call that fails is still a failed run, and it fails as itself —
    /// a truncated answer must not reach the artist as a generic error, because
    /// the two have different fixes.
    /// </summary>
    [Fact]
    public async Task AFailedFirstCallComesBackAsTheFailureItWas()
    {
        var artist = new ScriptedArtist(
            AiResult<List<InbetweenFrameResult>>.Truncated("the answer was cut off"));

        var result = await Run(artist, Swing(0.5));

        Assert.Equal(AiOutcome.Truncated, result.Outcome);
        Assert.Null(result.Value);
        Assert.Equal("the answer was cut off", result.Message);
        Assert.Single(artist.Requests);
    }

    /// <summary>
    /// A t nothing came back for is refused by name rather than silently
    /// handed the next frame's drawing. Matching answers to requests by
    /// position would mistime the whole sequence, which is invisible in a
    /// still and obvious in motion.
    /// </summary>
    [Fact]
    public async Task AnAnswerMissingATIsRefusedRatherThanShiftingTheOthersAlong()
    {
        var request = Swing(0.25, 0.5, 0.75);
        // Only the two outer ts, deliberately returned out of order.
        var artist = new ScriptedArtist(Answer((0.75, Straight(0.75)), (0.25, Straight(0.25))));

        var run = (await Run(artist, request)).Value!;

        Assert.True(run.Frames[0].Accepted);
        Assert.Equal(Straight(0.25), run.Frames[0].Strokes![0].Points[0].Y);
        Assert.False(run.Frames[1].Accepted);
        Assert.True(run.Frames[2].Accepted);
        Assert.Equal(Straight(0.75), run.Frames[2].Strokes![0].Points[0].Y);
    }

    /// <summary>
    /// <b>The coherence fault is the one a re-ask cannot state on its own.</b>
    /// Every other refusal names a stroke and a distance the model can act on
    /// with the keys and its own rejected drawing; <i>"it jitters against the
    /// frames beside it"</i> is defined entirely against frames the repair
    /// would otherwise not carry. So the accepted neighbours ride along — and
    /// the prompt only claims consistency with them when it is actually
    /// sending them.
    /// </summary>
    [Fact]
    public async Task AJitterRepairCarriesTheAcceptedNeighboursTheFaultIsMeasuredAgainst()
    {
        // 0.5 sits 40px off the straight path: inside the ~81px betweenness
        // band, outside the ~29px coherence band once it has neighbours.
        var request = Swing(0.25, 0.5, 0.75);
        var artist = new ScriptedArtist(Answer(
            (0.25, Straight(0.25)), (0.5, Straight(0.5) + 40), (0.75, Straight(0.75))));

        await Run(artist, request);

        var repair = artist.Requests[1];
        Assert.Contains("jitters against the frames beside it", Assert.Single(repair.Repair!).Fault);

        var settled = repair.Settled;
        Assert.NotNull(settled);
        Assert.Equal([0.25, 0.75], settled!.Select(s => s.T));
        Assert.All(settled, s => Assert.NotEmpty(s.Strokes));

        var prompt = Prompts.InbetweenUser(repair);
        Assert.Contains("accepted and are now fixed", prompt);
    }

    /// <summary>
    /// …and the other faults do not pay for context they cannot use. Neighbour
    /// strokes are roughly another whole frame on the wire, so sending them
    /// always would be paying a token cost for nothing on every fault but one.
    /// </summary>
    [Fact]
    public async Task AnOrdinaryFaultSendsNoNeighboursAndClaimsNoConsistency()
    {
        var request = Swing(0.25, 0.5, 0.75);
        // Far outside the keys: a betweenness fault, fully actionable alone.
        var artist = new ScriptedArtist(Answer(
            (0.25, Straight(0.25)), (0.5, 400), (0.75, Straight(0.75))));

        await Run(artist, request);

        var repair = artist.Requests[1];
        Assert.Null(repair.Settled);

        var prompt = Prompts.InbetweenUser(repair);
        Assert.DoesNotContain("accepted and are now fixed", prompt);
        // The instruction and the data arrive together or neither does.
        Assert.DoesNotContain("consistent with", prompt);
    }

    /// <summary>
    /// <b>A discarded round must change the next question.</b> When a round is
    /// thrown away the loop's state is unchanged, so a naive rebuild sends the
    /// same prompt again — which makes the third of the three calls Q85 pays
    /// for a verbatim resend, and the blind retry this whole stage exists to
    /// replace.
    /// </summary>
    [Fact]
    public async Task AThrownAwayRoundTellsTheModelWhyRatherThanAskingAgainVerbatim()
    {
        // The gain-two-lose-one shape: the round is dropped whole.
        var request = Swing(0.15, 0.3, 0.45, 0.6, 0.75);
        var artist = new ScriptedArtist(
            Answer((0.3, Straight(0.3) + 40), (0.6, Straight(0.6)), (0.75, Straight(0.75))),
            Answer((0.15, Straight(0.15)), (0.45, Straight(0.45))));

        await Run(artist, request);

        Assert.Equal(3, artist.Requests.Count);
        var second = Prompts.InbetweenUser(artist.Requests[1]);
        var third = Prompts.InbetweenUser(artist.Requests[2]);
        Assert.NotEqual(second, third);
        Assert.DoesNotContain("was discarded", second);
        Assert.Contains("discarded because it broke the frame(s) at t=0.3", third);
        output.WriteLine(artist.Requests[2].Repair![0].Fault);
    }

    /// <summary>
    /// The same, for the duller shape: a correction that fails the same way
    /// gets told so rather than being asked identically a third time.
    /// </summary>
    [Fact]
    public async Task ACorrectionThatFailsTheSameWayIsToldSoBeforeTheLastAsk()
    {
        var artist = new ScriptedArtist(Answer((0.5, 400)));

        await Run(artist, Swing(0.5));

        var second = Prompts.InbetweenUser(artist.Requests[1]);
        var third = Prompts.InbetweenUser(artist.Requests[2]);
        Assert.NotEqual(second, third);
        Assert.Contains("failed the same way", third);
    }

    /// <summary>
    /// A frame that came back as the free engine's own answer is flagged, not
    /// merely noted in prose. Repair is what gives a model a reason to flatten
    /// onto the deterministic chord — it passes every geometric check by
    /// construction — so "corrected" and "gave up and copied the cheap engine"
    /// must not look the same from outside (Q33: report, never veto).
    /// </summary>
    [Fact]
    public async Task AFrameThatMatchesTheFreeEngineSaysSoAsAValue()
    {
        var request = Swing(0.5);
        var free = Inbetweener.Inbetween(request.KeyframeA, request.KeyframeB, 0.5, Easing.Linear);
        var artist = new ScriptedArtist(
            Answer((0.5, 400)),
            AiResult<List<InbetweenFrameResult>>.Success([new InbetweenFrameResult(0.5, free)]));

        var run = (await Run(artist, request)).Value!;

        var frame = Assert.Single(run.Frames);
        Assert.True(frame.Accepted);
        Assert.Equal(2, frame.Attempts);
        Assert.True(frame.MatchesDeterministic);
        Assert.Equal(1, run.MatchedFreeEngineCount);
    }

    /// <summary>
    /// An ordinary request is byte-identical to what Lightbox sent before
    /// repair existed. Optional means absent: a run that needs no repair must
    /// not pay a single character for the machinery that would have.
    /// </summary>
    [Fact]
    public void ARequestWithNothingToRepairRendersNoRepairBlock()
    {
        var prompt = Prompts.InbetweenUser(Swing(0.5));

        Assert.DoesNotContain("yourStrokes", prompt);
        Assert.DoesNotContain("rejected", prompt);
        Assert.DoesNotContain("previous answer", prompt);
    }

    /// <summary>
    /// The free engine clears its own answers, so repair costs it nothing —
    /// the loop must not turn a working model into three calls' worth of one.
    /// </summary>
    [Fact]
    public async Task TheFreeEngineNeedsNoRepair()
    {
        var request = Swing(0.25, 0.5, 0.75);
        var free = new Golden.FreeEngineArtist();

        var run = (await Run(free, request)).Value!;

        Assert.Equal(1, run.Calls);
        Assert.Equal(3, run.AcceptedCount);
    }

    /// <summary>
    /// <b>Grading a model must not go through the repair loop.</b> Phase 2
    /// measures what a model does in one shot — schema adherence, and the
    /// stroke count where it stops coping. Fold repair in and the degradation
    /// rung moves because Lightbox tried harder, not because the model coped,
    /// and the number is published under the model's name.
    /// </summary>
    /// <remarks>
    /// Pinned as behaviour rather than left to a comment, because the change
    /// that breaks it is a plausible-looking improvement — "the profiler should
    /// use the same path the artist does" — and nothing else in the suite would
    /// notice.
    /// </remarks>
    [Fact]
    public async Task ProfilingAModelMeasuresItsFirstAnswerAndNotItsThird()
    {
        var artist = new ScriptedArtist(
            Answer((0.5, 400)),
            Answer((0.5, Straight(0.5))));

        await Golden.CapabilityProfiler.ProfileAsync(
            artist, "test", [Golden.GoldenSet.Short()[0]], false, null, CancellationToken.None);

        Assert.Single(artist.Requests);
        Assert.Null(artist.Requests[0].Repair);
    }

    /// <summary>
    /// The same, for the connection test. Certifying a model that only works
    /// on its third go tells an artist the wrong thing about what they are
    /// about to depend on.
    /// </summary>
    [Fact]
    public async Task TheConnectionTestJudgesOneAnswerPerCallItMakes()
    {
        var artist = new ScriptedArtist(Answer((0.5, 400)));
        var connection = new AiConnection { ProviderId = AiProviders.Compatible };
        connection.Set("baseUrl", "https://scripted.test/v1");
        connection.Set("model", "scripted");

        var check = await AiConnectionTester.TestAsync(
            connection, AiTestDepth.Thorough, artist, null, CancellationToken.None);

        Assert.False(check.Ok);
        // Two calls, both single-shot: the quick probe and the real inbetween.
        // What must not appear is a repair on either.
        Assert.All(artist.Requests, r => Assert.Null(r.Repair));
    }
}
