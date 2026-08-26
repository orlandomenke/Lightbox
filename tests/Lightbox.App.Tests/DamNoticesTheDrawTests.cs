using Avalonia.Headless.XUnit;
using Avalonia.Threading;
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
    private static MainViewModel Ready()
    {
        var vm = new MainViewModel(null)
        {
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
}
