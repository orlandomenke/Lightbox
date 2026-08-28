using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// That the cycle attribution names the gate that actually turned an event
/// away, and that neither gate can be blamed for the other's refusals (B178).
/// </summary>
/// <remarks>
/// <para>
/// <b>These prove the instrument, not the fix</b> — the split
/// <c>StrokeToScreenTests</c> and <c>PublishTallyTests</c> already hold for
/// B189 and B178's per-caller tally. The evidence for a cycle being fixed is a
/// capture on the owner's machine; what a test can hold is that the counter
/// cannot lie about which gate said no.
/// </para>
/// <para>
/// <b>Why the three-way split is the thing under test.</b> The report's verdict
/// is "the dispatcher is setting the rate" precisely when
/// <c>RequestsRefusedByPost</c> dominates, and that bucket claims something
/// stronger than "a publish was posted": it claims <em>the dam would have let
/// this one through</em>. If the two conditions were ever conflated, a pipeline
/// that is correctly paced would read as a dispatcher fault, and the fix would
/// go to the wrong place — which is the mistake B178's own entry records
/// itself making once already, reasoning from prose rather than a counter.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class WhichGateAteTheCycleTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel Ready(int depth, bool inline = false)
    {
        var vm = new MainViewModel(null)
        {
            InFlightDepth = depth,
            PublishesInline = inline,
            SmoothStrokes = false,
            ColorHex = "#000000",
            BrushSize = 12,
            BrushHardness = 1,
            BrushOpacity = 1,
            BrushFlow = 1,
        };
        Dispatcher.UIThread.RunJobs();
        return vm;
    }

    private static void Move(MainViewModel vm, double x, double y)
    {
        Dispatcher.UIThread.Post(() => vm.MoveStroke(x, y, 1), DispatcherPriority.Input);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// A canvas that never draws puts every refusal on the dam, and none on the
    /// post.
    /// </summary>
    [AvaloniaFact]
    public void ACanvasThatNeverDrawsIsTheDamsRefusal()
    {
        var vm = Ready(depth: 1);
        var seqs = new List<long>();
        vm.SnapshotChanged += s => seqs.Add(s.Seq);

        // Drawn once, and then never again: the first publish is presented so
        // the pacing has a consumer to pace to (a canvas that has never
        // presented is deliberately never paced), and everything after it piles
        // up against the depth.
        long drawn = 0;
        vm.SetRenderedSeqProbe(() => drawn);
        vm.SetRenderedAtProbe(() => System.Diagnostics.Stopwatch.GetTimestamp());

        vm.BeginStroke(50, 50, 1);
        Dispatcher.UIThread.RunJobs();
        Move(vm, 60, 50);
        Assert.NotEmpty(seqs);
        drawn = seqs[^1];

        for (var i = 0; i < 12; i++) Move(vm, 70 + (i * 8), 50);

        output.WriteLine(
            $"dam {vm.RequestsRefusedByDam}   post {vm.RequestsRefusedByPost}"
            + $"   both {vm.RequestsRefusedByBoth}   through {vm.RequestsLetThrough}");

        Assert.True(
            vm.RequestsRefusedByDam > 0,
            "a canvas stuck one frame behind refused nothing, so this test exercised "
            + "no gate at all and the counters below prove nothing");
        Assert.Equal(0, vm.RequestsRefusedByPost);

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// And a canvas that keeps up puts the refusals on the post instead — the
    /// bucket the report's dispatcher verdict rests on.
    /// </summary>
    /// <remarks>
    /// The pending post is simulated the only way it can be without a real
    /// windowing loop: the publish job is posted and the dispatcher is not
    /// pumped, so further events arrive while it is still queued. That is
    /// exactly the mid-stroke state on Windows, where pointer messages are
    /// handled by the platform loop and a job posted at Input priority behind
    /// them waits.
    /// </remarks>
    [AvaloniaFact]
    public void APendingPostIsNotBlamedOnTheDam()
    {
        var vm = Ready(depth: 4);
        var seqs = new List<long>();
        vm.SnapshotChanged += s => seqs.Add(s.Seq);

        // Ahead of everything, so the depth can never bind and any refusal that
        // happens must be the post's.
        vm.SetRenderedSeqProbe(() => long.MaxValue / 2);
        vm.SetRenderedAtProbe(() => System.Diagnostics.Stopwatch.GetTimestamp());

        vm.BeginStroke(50, 50, 1);
        Dispatcher.UIThread.RunJobs();

        // No RunJobs between these: the first leaves a publish posted, and the
        // rest arrive while it is still sitting there.
        for (var i = 0; i < 8; i++) vm.MoveStroke(60 + (i * 8), 50, 1);

        output.WriteLine(
            $"dam {vm.RequestsRefusedByDam}   post {vm.RequestsRefusedByPost}"
            + $"   both {vm.RequestsRefusedByBoth}   through {vm.RequestsLetThrough}");

        Assert.True(
            vm.RequestsRefusedByPost > 0,
            "no event was refused by a pending post, so the arm the report's "
            + "dispatcher verdict rests on was never exercised");
        Assert.Equal(0, vm.RequestsRefusedByDam);
        Assert.Equal(0, vm.RequestsRefusedByBoth);

        Dispatcher.UIThread.RunJobs();
        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// The two gates never refuse the same request, whatever the canvas does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This test was written to prove the opposite and measured its own
    /// premise wrong, which is the useful half.</b> It set out to check that an
    /// overlap is attributed to both gates rather than to whichever check is
    /// written first — and the overlap never happens. The sequence number only
    /// advances inside <c>PublishSnapshot</c>, and the posted flag is already
    /// down by the time it runs, so a request refused by a pending post always
    /// sees exactly the gap the dam saw when it let that post through. There is
    /// no state in which both conditions hold.
    /// </para>
    /// <para>
    /// <b>So the report's dispatcher verdict rests on a structural property
    /// rather than on a lucky ordering</b>, and this is where that property is
    /// written down. It would break if the in-flight depth could shrink while a
    /// publish was posted; the counter and the report's tripwire line exist for
    /// that, and this holds the reasoning behind them.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void TheTwoGatesNeverRefuseTheSameRequest()
    {
        int totalDam = 0, totalPost = 0;
        foreach (var depth in new[] { 1, 2, 4 })
        {
            var vm = Ready(depth);
            var seqs = new List<long>();
            vm.SnapshotChanged += s => seqs.Add(s.Seq);

            long drawn = 0;
            vm.SetRenderedSeqProbe(() => drawn);
            vm.SetRenderedAtProbe(() => System.Diagnostics.Stopwatch.GetTimestamp());

            vm.BeginStroke(50, 50, 1);
            Dispatcher.UIThread.RunJobs();

            // Every mixture of pumped and unpumped events, against a canvas
            // that sometimes keeps up and sometimes does not — the whole space
            // the two gates can see between them.
            for (var i = 0; i < 24; i++)
            {
                vm.MoveStroke(60 + (i * 6), 50, 1);
                if (i % 3 == 0) Dispatcher.UIThread.RunJobs();
                if (i % 5 == 0 && seqs.Count > 0) drawn = seqs[^1];
            }

            Dispatcher.UIThread.RunJobs();

            output.WriteLine(
                $"depth {depth}: dam {vm.RequestsRefusedByDam}   post {vm.RequestsRefusedByPost}"
                + $"   both {vm.RequestsRefusedByBoth}   through {vm.RequestsLetThrough}");

            // The claim is per depth; the inertness check is over all of them,
            // because a deep pipeline against a canvas that keeps up is
            // ALLOWED to have an idle dam — that is what the depth is for, and
            // demanding a refusal there would be asserting a fault.
            totalDam += vm.RequestsRefusedByDam;
            totalPost += vm.RequestsRefusedByPost;
            Assert.Equal(0, vm.RequestsRefusedByBoth);

            vm.EndStroke();
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(
            totalDam > 0 && totalPost > 0,
            $"across every depth the dam refused {totalDam} and the post refused "
            + $"{totalPost}: one of the gates never said no at all, so the "
            + "exclusivity asserted above holds vacuously");
    }

    /// <summary>
    /// The inline arm publishes on the event that asked, without a dispatcher
    /// turn — and publishes the same thing the posted arm does.
    /// </summary>
    /// <remarks>
    /// <b>Both halves matter.</b> That the arm is faster to publish is the
    /// point of it; that it publishes the same snapshots is what makes it an
    /// experiment rather than a second behaviour. An arm that drew differently
    /// could not be A/B'd against the default by a person looking at a screen,
    /// which is the only test that has ever found one of these faults.
    /// </remarks>
    [AvaloniaFact]
    public void TheInlineArmPublishesWithoutADispatcherTurn()
    {
        var inline = Ready(depth: 4, inline: true);
        var inlineSeqs = new List<long>();
        inline.SnapshotChanged += _ => inlineSeqs.Add(1);
        inline.SetRenderedSeqProbe(() => long.MaxValue / 2);
        inline.SetRenderedAtProbe(() => System.Diagnostics.Stopwatch.GetTimestamp());

        inline.BeginStroke(50, 50, 1);
        Dispatcher.UIThread.RunJobs();
        var before = inlineSeqs.Count;
        for (var i = 0; i < 8; i++) inline.MoveStroke(60 + (i * 8), 50, 1);
        var withoutPumping = inlineSeqs.Count - before;

        var posted = Ready(depth: 4);
        var postedSeqs = new List<long>();
        posted.SnapshotChanged += _ => postedSeqs.Add(1);
        posted.SetRenderedSeqProbe(() => long.MaxValue / 2);
        posted.SetRenderedAtProbe(() => System.Diagnostics.Stopwatch.GetTimestamp());

        posted.BeginStroke(50, 50, 1);
        Dispatcher.UIThread.RunJobs();
        var postedBefore = postedSeqs.Count;
        for (var i = 0; i < 8; i++) posted.MoveStroke(60 + (i * 8), 50, 1);
        var postedWithoutPumping = postedSeqs.Count - postedBefore;

        output.WriteLine(
            $"eight events, no dispatcher turn: inline published {withoutPumping}, "
            + $"posted published {postedWithoutPumping}");

        Assert.True(
            withoutPumping > postedWithoutPumping,
            $"the inline arm published {withoutPumping} against the posted arm's "
            + $"{postedWithoutPumping}, so the two arms did the same thing and this "
            + "capture would A/B a build against itself");
        Assert.Equal(0, inline.RequestsRefusedByPost);

        Dispatcher.UIThread.RunJobs();
        inline.EndStroke();
        posted.EndStroke();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// What the inline arm gives up, and the reason it is not the default
    /// (B335).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This test exists because the default was switched and had to be
    /// switched back.</b> The A/B was decisive on the thing it measured — the
    /// cycle 28.84 to 22.12 ms, ink arriving every 2.0 pen events instead of
    /// 5.4, the worst pen-to-screen 479 ms to 79, and the owner reporting the
    /// stutter simply gone. Then the suite failed eight tests, and two of them
    /// were guarantees this work had no business overruling.
    /// </para>
    /// <para>
    /// <b>This is the first of the two: B73's coalescing.</b> A burst of
    /// already-queued pointer events becomes one full compose per event.
    /// <c>StrokeLatencyTests.APenBurstIsOneFrameNotOnePerEvent</c> is where that
    /// guarantee lives and it is the test that fails; this one states the same
    /// fact from the route's side, so that anyone flipping the default meets the
    /// reason here rather than discovering it in an unrelated file.
    /// </para>
    /// <para>
    /// <b>The other blocker is not visible from here at all</b>, which is worth
    /// knowing before trying to fix this one: <c>builtin-smudge</c> and
    /// <c>builtin-blender</c> stop matching their own commits, because a brush
    /// that samples the layer beneath depends on <em>when</em> the publish
    /// happens. Rate-limiting this route to the refresh interval would satisfy
    /// the burst below and would not touch that.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void TheInlineArmGivesUpTheBurstCoalescing()
    {
        const int Events = 15;

        int PublishesInABurst(MainViewModel vm)
        {
            var count = 0;
            vm.SnapshotChanged += _ => count++;
            vm.BeginStroke(50, 50, 1);
            Dispatcher.UIThread.RunJobs();

            var before = count;
            // No dispatcher turn between them: a burst delivered in one go, the
            // case B73 is about.
            for (var i = 0; i < Events; i++) vm.MoveStroke(60 + (i * 8), 50, 1);
            var published = count - before;

            Dispatcher.UIThread.RunJobs();
            vm.EndStroke();
            Dispatcher.UIThread.RunJobs();
            return published;
        }

        var shipped = PublishesInABurst(Ready(depth: 2));
        var inline = PublishesInABurst(Ready(depth: 2, inline: true));

        output.WriteLine($"a burst of {Events} events, no dispatcher turn:");
        output.WriteLine($"  the route this build ships   {shipped} publishes");
        output.WriteLine($"  LIGHTBOX_PUBLISH=inline      {inline} publishes");

        // B73's own bar, from StrokeLatencyTests: a couple for a burst is
        // coalescing working, one per event is the defect.
        Assert.True(
            shipped <= 3,
            $"the shipped route published {shipped} of {Events} queued events, so B73's "
            + "coalescing has been given up by default — which is exactly what this "
            + "test exists to stop happening quietly. See B335.");

        Assert.True(
            inline > shipped,
            $"the inline arm published {inline} against the shipped route's {shipped}, "
            + "so the two arms behave identically here and this test is measuring "
            + "nothing");
    }
}
