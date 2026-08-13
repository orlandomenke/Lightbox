using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// The wet-media live pass runs off the UI thread (B189), and these pin the
/// protocol around it.
/// </summary>
/// <remarks>
/// <para>
/// <b>What moved, and what must not have.</b> B189's second capture measured
/// the pass at 20.13 ms mean, 196.6 ms worst, 307 times in one session — six
/// seconds of blocked input, all of it in the event → publish segment the
/// pen-to-screen instrument showed growing. The simulation now runs on a
/// worker over copies; its <em>output</em> is pinned bit-identical to the old
/// synchronous path by <c>ACroppedRegionPassIsBitIdenticalToTheFullOne</c> in
/// the Raster suite, so what is left to test here is the choreography: one
/// pass in flight, stale results discarded, new points triggering the settle
/// pass, and the result actually reaching the preview.
/// </para>
/// <para>
/// <b>The runner seam makes this deterministic.</b> A queue stands in for the
/// thread pool, so the tests decide exactly when a "worker" finishes — the
/// same reason the pacing tests drive <c>NoteFramePresented</c> by hand, and
/// the same reason nothing here sleeps.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class LivePostAsyncTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static (MainViewModel Vm, Queue<Action> Work) Wet()
    {
        var vm = new MainViewModel(null)
        {
            SmoothStrokes = false,
            ColorHex = "#3060c0",
            BrushSize = 24,
            BrushHardness = 0.7,
            BrushOpacity = 1,
            BrushFlow = 0.9,
            BrushMedium = MediumKind.Watercolour,
        };
        var work = new Queue<Action>();
        vm.LivePostRunner = w => { work.Enqueue(w); return Task.CompletedTask; };
        Dispatcher.UIThread.RunJobs();
        return (vm, work);
    }

    private static void Stroke(MainViewModel vm, int events = 4)
    {
        vm.BeginStroke(100, 100, 0.9);
        for (var i = 1; i <= events; i++) vm.MoveStroke(100 + i * 40, 100, 0.9);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void AWorkerResultLandsOnThePreviewAndPublishes()
    {
        var (vm, work) = Wet();
        var publishes = 0;
        Stroke(vm);
        Assert.Single(work);

        vm.SnapshotChanged += _ => publishes++;
        work.Dequeue()();               // the "worker" finishes
        Dispatcher.UIThread.RunJobs();  // its posted finish lands

        output.WriteLine($"passes {vm.LivePostPasses}, publishes after finish {publishes}");
        Assert.Equal(1, vm.LivePostPasses);
        Assert.True(publishes >= 1, "the finished pass never published, so the wet look never reaches the screen");

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// One pass in flight, however many events arrive meanwhile — the flag
    /// spans the whole start → worker → finish arc, or a fast hand would fan
    /// out a worker per event and the copies would eat what the move saved.
    /// </summary>
    [AvaloniaFact]
    public void OnlyOnePassIsEverInFlight()
    {
        var (vm, work) = Wet();
        Stroke(vm);
        Assert.Single(work);

        for (var i = 0; i < 6; i++) vm.MoveStroke(400 + i * 30, 100, 0.9);
        Dispatcher.UIThread.RunJobs();
        output.WriteLine($"6 more events while a pass runs → {work.Count} worker item(s)");
        Assert.Single(work);

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Points that arrived while the worker ran start the settle pass, so the
    /// preview converges on the whole stroke instead of stopping wherever the
    /// pen was when the pass began — the old synchronous loop's behaviour,
    /// preserved across the thread hop.
    /// </summary>
    [AvaloniaFact]
    public void PointsArrivingDuringAPassTriggerTheSettlePass()
    {
        var (vm, work) = Wet();
        Stroke(vm);
        var first = work.Dequeue();

        vm.MoveStroke(500, 100, 0.9);   // lands after the copy was taken
        first();
        Dispatcher.UIThread.RunJobs();  // finish sees newer points, re-requests

        output.WriteLine($"worker items after a mid-pass event: {work.Count + 1}");
        Assert.Single(work);

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// A result that outlives its stroke is stale and must be dropped: the
    /// commit has already replaced the preview, and resurrecting it would put
    /// abandoned pixels over finished art until the next repaint.
    /// </summary>
    [AvaloniaFact]
    public void AResultArrivingAfterPenUpIsDiscarded()
    {
        var (vm, work) = Wet();
        Stroke(vm);
        var pass = work.Dequeue();

        vm.EndStroke();                 // commits and resets the live state
        Dispatcher.UIThread.RunJobs();
        var publishes = 0;
        vm.SnapshotChanged += _ => publishes++;

        pass();                         // the worker finishes too late
        Dispatcher.UIThread.RunJobs();

        output.WriteLine($"stale finish → passes {vm.LivePostPasses}, publishes {publishes}");
        Assert.Equal(0, vm.LivePostPasses);
        Assert.Equal(0, publishes);
    }

    /// <summary>
    /// A runner that cannot even schedule must not wedge the single-flight
    /// flag. The adversarial pass proved the first draft did exactly that: a
    /// synchronous throw skipped every path that clears the flag, and no wet
    /// pass ran again for the rest of the session. Asserted as recovery — the
    /// next event reaches a runner that works.
    /// </summary>
    [AvaloniaFact]
    public void ARunnerThatThrowsDoesNotWedgeThePassForever()
    {
        var (vm, work) = Wet();
        var thrown = false;
        var healthy = vm.LivePostRunner;
        vm.LivePostRunner = w =>
        {
            if (!thrown) { thrown = true; throw new InvalidOperationException("scheduling failed"); }
            return healthy(w);
        };

        vm.BeginStroke(100, 100, 0.9);
        vm.MoveStroke(140, 100, 0.9);
        Dispatcher.UIThread.RunJobs();  // the start swallows the throw
        Assert.True(thrown, "the throwing runner was never reached, so this tests nothing");

        vm.MoveStroke(180, 100, 0.9);
        Dispatcher.UIThread.RunJobs();

        output.WriteLine($"worker items after the throw and one more event: {work.Count}");
        Assert.Single(work);

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// The same, one stroke later: the stale guard must not swallow the NEXT
    /// stroke's passes — the generation moves forward, never wedges.
    /// </summary>
    [AvaloniaFact]
    public void TheStrokeAfterADiscardedResultStillGetsItsPasses()
    {
        var (vm, work) = Wet();
        Stroke(vm);
        var stale = work.Dequeue();
        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
        stale();
        Dispatcher.UIThread.RunJobs();

        Stroke(vm);
        Assert.Single(work);
        work.Dequeue()();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, vm.LivePostPasses);

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
    }
}
