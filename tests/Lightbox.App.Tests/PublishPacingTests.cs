using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// Publishes are paced to what the canvas has actually drawn (B189).
/// </summary>
/// <remarks>
/// <para>
/// <b>The mechanism, measured before it was built:</b> B189's first capture
/// showed publishing outrunning drawing about 2:1 at 4K — 935 of 1921 ink
/// publishes replaced before anything drew them, ~27 ms of UI-thread compose
/// each, while drawn frames queued a mean of 93 ms behind the surplus. The fix
/// keeps at most one undrawn frame in flight: a coalesced publish request
/// arriving while the newest publish is still unpresented is deferred, and the
/// canvas's <see cref="CanvasControl.SnapshotPresented"/> signal releases it.
/// </para>
/// <para>
/// <b>These tests drive the view model and play the canvas by hand</b> —
/// <c>NoteFramePresented</c> is the seam the real signal arrives through — for
/// the same reason the B73 suite counts events rather than milliseconds: the
/// headless harness has no real compositor, so what can be pinned exactly is
/// the ordering. B73's own tests double as the control group: they never
/// present a frame, so pacing must stay out of their way entirely.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class PublishPacingTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel Ready()
    {
        var vm = new MainViewModel(null)
        {
            SmoothStrokes = false,
            ColorHex = "#101010",
            BrushSize = 12,
            BrushHardness = 1,
            BrushOpacity = 1,
            BrushFlow = 1,
        };
        Dispatcher.UIThread.RunJobs();
        return vm;
    }

    /// <summary>One stroke event through the coalescing path, jobs drained.</summary>
    private static void Move(MainViewModel vm, double x, double y)
    {
        Dispatcher.UIThread.Post(() => vm.MoveStroke(x, y, 1), DispatcherPriority.Input);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// The heart of the pacing: while the newest publish is unpresented, more
    /// stroke events compose nothing — and the moment the canvas reports the
    /// frame drawn, exactly one publish covers everything that waited.
    /// </summary>
    [AvaloniaFact]
    public void EventsWhileTheCanvasIsBehindComposeNothingUntilItCatchesUp()
    {
        var vm = Ready();
        var seqs = new List<long>();
        vm.SnapshotChanged += s => seqs.Add(s.Seq);

        vm.BeginStroke(50, 50, 1);
        Dispatcher.UIThread.RunJobs();

        // Present everything so far: the canvas is caught up and pacing is armed.
        Move(vm, 60, 50);
        Assert.NotEmpty(seqs);
        vm.NoteFramePresented(seqs[^1]);

        // A caught-up canvas publishes immediately, as before.
        var before = seqs.Count;
        Move(vm, 70, 50);
        Assert.Equal(before + 1, seqs.Count);
        var inFlight = seqs[^1];

        // That frame is now in flight and NOT presented: further events defer.
        Move(vm, 80, 50);
        Move(vm, 90, 50);
        Move(vm, 100, 50);
        output.WriteLine($"3 events against an unpresented frame → {seqs.Count - before - 1} publishes");
        Assert.Equal(before + 1, seqs.Count);

        // The canvas catches up; the deferred publish fires once, covering all
        // three waiting events.
        vm.NoteFramePresented(inFlight);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before + 2, seqs.Count);

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// A canvas that has never presented anything must not pace: headless
    /// hosts, IPC-only documents and the first frames of a session have no
    /// consumer to pace to, and B73's whole test suite runs in that state.
    /// </summary>
    [AvaloniaFact]
    public void ACanvasThatNeverPresentsNeverDefers()
    {
        var vm = Ready();
        var publishes = 0;
        vm.SnapshotChanged += _ => publishes++;

        vm.BeginStroke(50, 50, 1);
        Dispatcher.UIThread.RunJobs();
        var atPenDown = publishes;

        Move(vm, 60, 50);
        Move(vm, 70, 50);
        Move(vm, 80, 50);
        output.WriteLine($"3 events, nothing ever presented → {publishes - atPenDown} publishes");
        Assert.Equal(atPenDown + 3, publishes);

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// The liveness bound: a canvas that stops drawing — hidden, minimised —
    /// holds publishes back for at most <see cref="MainViewModel.PublishDamMs"/>,
    /// not forever.
    /// </summary>
    [AvaloniaFact]
    public void AStalledCanvasOnlyDamsPublishesForTheBoundedTime()
    {
        var vm = Ready();
        var seqs = new List<long>();
        vm.SnapshotChanged += s => seqs.Add(s.Seq);

        vm.BeginStroke(50, 50, 1);
        Dispatcher.UIThread.RunJobs();
        Move(vm, 60, 50);
        vm.NoteFramePresented(seqs[^1]);

        Move(vm, 70, 50);           // in flight, never presented — the stall
        var before = seqs.Count;
        Move(vm, 80, 50);           // deferred: inside the dam
        Assert.Equal(before, seqs.Count);

        Thread.Sleep((int)MainViewModel.PublishDamMs + 60);
        Move(vm, 90, 50);           // past the dam: publish anyway
        output.WriteLine($"event after {MainViewModel.PublishDamMs} ms stall → {seqs.Count - before} publish(es)");
        Assert.Equal(before + 1, seqs.Count);

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// The dam flushes itself: a deferral whose interaction produces no
    /// further event still publishes.
    /// </summary>
    /// <remarks>
    /// The adversarial pass's falsifying case, kept as the regression test. The
    /// first draft's dam was only checked inside <c>RequestSnapshot</c>, so
    /// "released after 250 ms at most" was really "released by the next event,
    /// whenever that is" — a stall with no follow-up event held the deferred
    /// frame forever. The release runs through its own seam because whether a
    /// <c>DispatcherTimer</c> ticks under a headless pump is a fact about the
    /// harness (the B61 debounce records the same decision); the one-line
    /// arming is wiring.
    /// </remarks>
    [AvaloniaFact]
    public void AStallWithNoFurtherEventsStillFlushesAfterTheDam()
    {
        var vm = Ready();
        var seqs = new List<long>();
        vm.SnapshotChanged += s => seqs.Add(s.Seq);

        vm.BeginStroke(50, 50, 1);
        Dispatcher.UIThread.RunJobs();
        Move(vm, 60, 50);
        vm.NoteFramePresented(seqs[^1]);

        Move(vm, 70, 50);           // in flight, never presented
        var before = seqs.Count;
        Move(vm, 80, 50);           // deferred — and this is the LAST event

        Thread.Sleep((int)MainViewModel.PublishDamMs + 60);
        vm.ReleaseOverduePublish(); // what the one-shot timer calls
        Dispatcher.UIThread.RunJobs();

        output.WriteLine($"stall, no further events → {seqs.Count - before} publish(es) after the dam");
        Assert.Equal(before + 1, seqs.Count);

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// The timer's release re-defers when the canvas is merely slow rather
    /// than stalled — a publish landed inside the dam window, so going ahead
    /// would stack a second undrawn frame, which is the state pacing exists
    /// to prevent.
    /// </summary>
    [AvaloniaFact]
    public void AReleaseInsideTheDamReDefersRatherThanStackingASecondFrame()
    {
        var vm = Ready();
        var seqs = new List<long>();
        vm.SnapshotChanged += s => seqs.Add(s.Seq);

        vm.BeginStroke(50, 50, 1);
        Dispatcher.UIThread.RunJobs();
        Move(vm, 60, 50);
        vm.NoteFramePresented(seqs[^1]);

        Move(vm, 70, 50);           // in flight — publish just left, dam fresh
        var before = seqs.Count;
        Move(vm, 80, 50);           // deferred

        vm.ReleaseOverduePublish(); // timer fires early: no sleep, dam not expired
        Dispatcher.UIThread.RunJobs();

        output.WriteLine($"early release → {seqs.Count - before} publish(es)");
        Assert.Equal(before, seqs.Count);

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// A direct publish — pen-up's commit, undo, a frame change — satisfies the
    /// deferred one rather than stacking on it: the snapshot it hands over is
    /// built from current state, which includes everything the deferral was
    /// waiting to show. Without this, the release would publish a duplicate.
    /// </summary>
    [AvaloniaFact]
    public void ADirectPublishSatisfiesTheDeferredOne()
    {
        var vm = Ready();
        var seqs = new List<long>();
        vm.SnapshotChanged += s => seqs.Add(s.Seq);

        vm.BeginStroke(50, 50, 1);
        Dispatcher.UIThread.RunJobs();
        Move(vm, 60, 50);
        vm.NoteFramePresented(seqs[^1]);

        Move(vm, 70, 50);           // in flight
        var inFlight = seqs[^1];
        Move(vm, 80, 50);           // deferred behind it

        // Pen up: EndStroke commits and publishes directly.
        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
        var afterCommit = seqs.Count;

        // The canvas catches up on everything; the old deferral must not fire
        // a stale extra publish.
        vm.NoteFramePresented(inFlight);
        vm.NoteFramePresented(seqs[^1]);
        Dispatcher.UIThread.RunJobs();
        output.WriteLine($"publishes after commit: {seqs.Count - afterCommit}");
        Assert.Equal(afterCommit, seqs.Count);
    }

    /// <summary>
    /// The released publish still lands behind pointer events already queued —
    /// B73's ordering, preserved under pacing. A release that jumped the queue
    /// would publish a frame that is already stale, which is the exact defect
    /// B73 fixed.
    /// </summary>
    [AvaloniaFact]
    public void AReleasedPublishStillCoversTheEventsQueuedBeforeIt()
    {
        var vm = Ready();
        var seqs = new List<long>();
        vm.SnapshotChanged += s => seqs.Add(s.Seq);

        vm.BeginStroke(50, 50, 1);
        Dispatcher.UIThread.RunJobs();
        Move(vm, 60, 50);
        vm.NoteFramePresented(seqs[^1]);

        Move(vm, 70, 50);           // in flight
        var inFlight = seqs[^1];
        Move(vm, 80, 50);           // deferred

        // The release and a burst of new events arrive together; drain once.
        var before = seqs.Count;
        Dispatcher.UIThread.Post(() => vm.MoveStroke(90, 50, 1), DispatcherPriority.Input);
        Dispatcher.UIThread.Post(() => vm.MoveStroke(100, 50, 1), DispatcherPriority.Input);
        vm.NoteFramePresented(inFlight);
        Dispatcher.UIThread.RunJobs();

        // One publish, not three: the released request coalesced with the
        // burst the way an ordinary one does.
        output.WriteLine($"release + 2 queued events → {seqs.Count - before} publish(es)");
        Assert.Equal(before + 1, seqs.Count);

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
    }
}
