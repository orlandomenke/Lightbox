using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// How quickly a deferred publish notices that the frame it is waiting for has
/// been drawn.
/// </summary>
/// <remarks>
/// <para>
/// <b>The dam holds longer than the canvas takes, and the hold alone cannot say
/// why.</b> On the owner's machine, 2026-08-26: a deferral held <b>54.98 ms</b>
/// against a <c>publish -&gt; drawn</c> of <b>30.67 ms</b>. Twenty-four
/// milliseconds is neither drawing nor composing, and there are two candidates
/// with two different fixes — the release notification queueing behind the
/// artist's pointer events, or nothing asking again until the next one arrives.
/// </para>
/// <para>
/// These pin the half that can be tested without a clock: whether the dam
/// consults the canvas's own high-water mark when it is asked, rather than
/// waiting to be told. Ordering, not timing — the reframe
/// <c>StrokeLatencyTests</c> made for B73, and the reason nothing here sleeps.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class DamNoticesTheDrawTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>
    /// A view model with the dam at a KNOWN depth. These tests are about how a
    /// deferral is noticed and accounted for, not about how many frames the
    /// application ships in flight — and when that default moved from one to
    /// two, three of them failed for a reason that had nothing to do with what
    /// they assert. A test that depends on a default should say so.
    /// </summary>
    private static MainViewModel Ready(int depth = 1)
    {
        var vm = new MainViewModel(null)
        {
            InFlightDepth = depth,
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
    /// A draw the canvas has finished but not yet announced releases the dam on
    /// the next pointer event, without the announcement.
    /// </summary>
    /// <remarks>
    /// This is B321's fix stated as behaviour rather than as a number: the dam
    /// reads the interlocked high-water mark when it is asked, so the artist's
    /// next event is enough. If it ever regresses to waiting for the posted
    /// notification, every deferral gains a dispatcher hop that mid-stroke
    /// queues behind the events doing the asking.
    /// </remarks>
    [AvaloniaFact]
    public void ADrawnFrameReleasesTheDamWithoutBeingAnnounced()
    {
        var vm = Ready();
        var seqs = new List<long>();
        vm.SnapshotChanged += s => seqs.Add(s.Seq);

        // The canvas answers only through the probe — NoteFramePresented is
        // never called, so the posted announcement is simulated as never
        // arriving. That is the mid-stroke case: the post exists, and it is
        // behind the pointer events.
        long drawn = 0;
        vm.SetRenderedSeqProbe(() => drawn);
        vm.SetRenderedAtProbe(() => System.Diagnostics.Stopwatch.GetTimestamp());

        vm.BeginStroke(50, 50, 1);
        Dispatcher.UIThread.RunJobs();
        Move(vm, 60, 50);
        Assert.NotEmpty(seqs);
        drawn = seqs[^1];

        Move(vm, 70, 50);
        var inFlight = seqs[^1];
        var before = seqs.Count;

        // Unpresented and undrawn: this must defer, or the test that follows
        // proves nothing.
        Move(vm, 80, 50);
        Assert.Equal(before, seqs.Count);

        // The canvas finishes it. Nobody tells the view model.
        drawn = inFlight;

        Move(vm, 90, 50);
        output.WriteLine($"publishes after the silent draw: {seqs.Count - before}");
        Assert.True(
            seqs.Count > before,
            "the dam went on holding a frame the canvas had already drawn, because "
            + "nothing announced it — B321's fix has regressed");

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// And it still waits for a canvas that genuinely has not drawn. The check
    /// above would pass just as well on a dam that never held anything.
    /// </summary>
    [AvaloniaFact]
    public void AnUndrawnFrameIsStillWaitedFor()
    {
        var vm = Ready();
        var seqs = new List<long>();
        vm.SnapshotChanged += s => seqs.Add(s.Seq);

        long drawn = 0;
        vm.SetRenderedSeqProbe(() => drawn);

        vm.BeginStroke(50, 50, 1);
        Dispatcher.UIThread.RunJobs();
        Move(vm, 60, 50);
        drawn = seqs[^1];

        Move(vm, 70, 50);
        var before = seqs.Count;

        // The canvas draws nothing more, and says nothing more.
        Move(vm, 80, 50);
        Move(vm, 90, 50);
        Move(vm, 100, 50);
        output.WriteLine($"publishes against a canvas that never drew: {seqs.Count - before}");
        Assert.Equal(before, seqs.Count);

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// The lateness split only counts time inside the deferral it belongs to.
    /// </summary>
    /// <remarks>
    /// A frame drawn <em>before</em> a deferral began is the previous
    /// deferral's waste, and counting it here would report more overhead than
    /// there was — the report would then recommend chasing time nobody spent.
    /// </remarks>
    [AvaloniaFact]
    public void LatenessIsNeverMoreThanTheHold()
    {
        var vm = Ready();
        var seqs = new List<long>();
        vm.SnapshotChanged += s => seqs.Add(s.Seq);

        long drawn = 0;
        vm.SetRenderedSeqProbe(() => drawn);
        // A draw that happened long before anything was deferred.
        var ancient = System.Diagnostics.Stopwatch.GetTimestamp()
                      - System.Diagnostics.Stopwatch.Frequency * 5;
        vm.SetRenderedAtProbe(() => ancient);

        vm.BeginStroke(50, 50, 1);
        Dispatcher.UIThread.RunJobs();
        Move(vm, 60, 50);
        drawn = seqs[^1];
        Move(vm, 70, 50);
        var inFlight = seqs[^1];

        Move(vm, 80, 50);          // defers
        drawn = inFlight;
        Move(vm, 90, 50);          // releases

        output.WriteLine(
            $"deferrals {vm.DamDeferrals}, held {vm.DamHeldTotalMs:0.###} ms, "
            + $"late {vm.DamLateTotalMs:0.###} ms");
        Assert.True(vm.DamDeferrals > 0, "nothing was deferred, so there is nothing to measure");
        Assert.True(
            vm.DamLateTotalMs <= vm.DamHeldTotalMs + 0.001,
            $"lateness {vm.DamLateTotalMs:0.###} ms exceeds the hold {vm.DamHeldTotalMs:0.###} ms — "
            + "a draw from before the deferral is being counted against it");

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// The canvas's "I drew it" reaches the UI thread ahead of the pointer
    /// events queued behind it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the one that matters, and the two tests above are why it was
    /// nearly missed.</b> They prove a pointer event can release a deferral
    /// without the announcement — true, and irrelevant in a real stroke: the
    /// announcement is posted from the render thread the instant the draw
    /// lands, and the event that would overtake it arrives afterwards into the
    /// same FIFO queue. At Input the announcement therefore sat behind every
    /// queued pointer event, each paying its own stamping. Measured: 29.66 ms
    /// of a 53.88 ms hold, after the frame was already on screen.
    /// </para>
    /// <para>
    /// Tested by draining priorities rather than by a clock — B314's technique.
    /// Running the queue down to Input and no further leaves anything AT Input
    /// exactly where it sat, so a post above Input is delivered and a post at
    /// Input is not. Put the priority back and this fails, which is what makes
    /// it worth having.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void TheCanvasSaysItDrewBeforeQueuedPointerEventsRun()
    {
        var canvas = new CanvasControl();
        long? announced = null;
        canvas.SnapshotPresented += seq => announced = seq;

        // A pointer event's worth of work, queued at Input BEFORE the draw
        // lands — which is the ordering a real stroke produces.
        var pointerRan = false;
        Dispatcher.UIThread.Post(() => pointerRan = true, DispatcherPriority.Input);

        canvas.NoteRenderedForTest(7);

        // Drain everything above Input, and nothing at it.
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Default);

        output.WriteLine($"announced {announced?.ToString() ?? "nothing"}, pointer work ran: {pointerRan}");
        Assert.False(pointerRan, "the drain reached Input, so this proves nothing — tighten it");
        Assert.Equal(7, announced);
    }

    /// <summary>
    /// One frame in flight publishes once per round trip; two publishes twice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The mechanism, which is all a headless test can judge.</b> Whether a
    /// deeper pipeline feels better depends on a real vsync and a real hand,
    /// and this pump has neither. What it can say is that the depth is wired to
    /// something and does what it claims: at 1 a second event against an
    /// undrawn frame composes nothing, and at 2 it composes.
    /// </para>
    /// <para>
    /// The measurement that made this worth trying: a cycle of 35.44 ms against
    /// a <c>publish -&gt; drawn</c> of 31.19, a dam whose own overhead is
    /// 0.15 ms, and a pen delivering every 5.06 ms. Twenty-eight publishes a
    /// second from a tablet offering two hundred, and depth is the only thing
    /// left setting that.
    /// </para>
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    public void DepthDecidesHowManyPublishesAreAllowedPerRoundTrip(int depth, int expected)
    {
        var vm = Ready(depth);
        var seqs = new List<long>();
        vm.SnapshotChanged += s => seqs.Add(s.Seq);

        long drawn = 0;
        vm.SetRenderedSeqProbe(() => drawn);

        vm.BeginStroke(50, 50, 1);
        Dispatcher.UIThread.RunJobs();
        Move(vm, 60, 50);
        Assert.NotEmpty(seqs);
        drawn = seqs[^1];          // caught up

        Move(vm, 70, 50);          // publishes; that frame is now in flight
        var before = seqs.Count;

        // The canvas draws nothing more. How many further publishes get out is
        // exactly the depth minus the one already in flight.
        Move(vm, 80, 50);
        Move(vm, 90, 50);
        Move(vm, 100, 50);

        output.WriteLine($"depth {depth}: {seqs.Count - before} publishes against an undrawn frame");
        Assert.Equal(expected, seqs.Count - before);

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Every deferral is accounted for, however it came to be let through.
    /// </summary>
    /// <remarks>
    /// The pointer-event path — <c>CanvasIsBehind</c> answering false because
    /// <c>AdoptRenderedSeq</c> saw the draw — published and left the tally
    /// open, so <c>publish held back</c> was a mean over only the deferrals
    /// that went through the announcement or the timer. Measured on the owner's
    /// machine, 2026-08-27: <b>1,568 deferrals against 388 accounted
    /// releases</b>. The number was not wrong so much as computed over a
    /// quarter of its subject, which is worse — it looked trustworthy.
    /// </remarks>
    [AvaloniaFact]
    public void EveryDeferralIsAccountedForHoweverItWasReleased()
    {
        var vm = Ready();
        var seqs = new List<long>();
        vm.SnapshotChanged += s => seqs.Add(s.Seq);

        long drawn = 0;
        vm.SetRenderedSeqProbe(() => drawn);
        vm.SetRenderedAtProbe(() => System.Diagnostics.Stopwatch.GetTimestamp());

        vm.BeginStroke(50, 50, 1);
        Dispatcher.UIThread.RunJobs();
        Move(vm, 60, 50);
        drawn = seqs[^1];

        // Three round trips, each released by a pointer event asking rather
        // than by the announcement — NoteFramePresented is never called.
        for (var i = 0; i < 3; i++)
        {
            Move(vm, 70 + i * 10, 50);
            var inFlight = seqs[^1];
            Move(vm, 75 + i * 10, 50);   // defers
            drawn = inFlight;            // the canvas draws, silently
            Move(vm, 78 + i * 10, 50);   // an event asks, and it publishes
        }

        var accounted = vm.DamReleasedByPresent + vm.DamReleasedByTimer + vm.DamReleasedByEvent;
        output.WriteLine(
            $"deferrals {vm.DamDeferrals}, accounted {accounted} "
            + $"(screen {vm.DamReleasedByPresent}, timer {vm.DamReleasedByTimer}, event {vm.DamReleasedByEvent})");

        Assert.True(vm.DamDeferrals > 0, "nothing deferred, so nothing is being tested");
        Assert.True(
            vm.DamReleasedByEvent > 0,
            "no deferral was released by an event asking, so this proves nothing about that path");
        Assert.Equal(vm.DamDeferrals, accounted);

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
    }
}
