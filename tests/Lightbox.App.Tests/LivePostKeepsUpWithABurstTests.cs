using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// The live post-process is not outranked by the pen that is feeding it (B313).
/// </summary>
/// <remarks>
/// <para>
/// <b>The complaint was specific and the mean did not show it:</b> <i>"especially
/// speedy lines trail behind the pen; short quick strokes not so much"</i>. The
/// render report put the wait from queued to started at a mean of 7.5 ms — and
/// a <b>worst of 3,126 ms</b>. Averages hide a tail, and nobody experiences an
/// average.
/// </para>
/// <para>
/// <b>The mechanism.</b> The pass was posted at <c>Background</c>, which
/// Avalonia runs below <c>Input</c>, so for as long as the hand keeps moving it
/// is outranked by every pointer event — which is the whole of a long fast
/// stroke and none of a short one. That is the difference the artist was
/// describing. Worse, the cost of being late compounds: the pending region
/// keeps growing while the pass waits, so the same capture shows a 360 ms pass
/// over a region averaging 1.3% of the mark. And a brush with an effect
/// displays the mark as of the last completed pass, so the paint stops there
/// until the next one lands.
/// </para>
/// <para>
/// <b>Testable without a clock, which is the point.</b> "Trails behind" is a
/// statement about <em>order</em>, not duration —
/// <c>StrokeLatencyTests</c> makes the same reframe for B73. Draining the
/// dispatcher down to <c>Input</c> and no further leaves anything posted at
/// <c>Background</c> exactly where it was, so the two priorities give different
/// answers on a run with no timing in it at all.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class LivePostKeepsUpWithABurstTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>Gouache (flat), near enough: soft, granulated, with a wet edge.</summary>
    private static MainViewModel Ready()
    {
        var vm = new MainViewModel(null)
        {
            SmoothStrokes = false,
            ColorHex = "#203040",
            BrushSize = 18,
            BrushHardness = 0.75,
            BrushOpacity = 0.95,
            BrushFlow = 0.9,
            BrushSpacing = 0.12,
            BrushWetEdge = 0.15,
            BrushGranulation = 0.25,
            LivePostRunner = work => { work(); return Task.CompletedTask; },
        };
        vm.NewDocument(new NewDocumentSettings("flat", 1920, 1080, 12, 72, "#ffffff", false));
        Dispatcher.UIThread.RunJobs();
        return vm;
    }

    /// <summary>
    /// A pass queued during a burst runs alongside the burst, not after it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The events are posted rather than called in a loop for the reason
    /// <c>StrokeLatencyTests.Burst</c> gives: a synchronous loop never produces
    /// the competition the defect lives in, and would pass on the broken code.
    /// </para>
    /// <para>
    /// <c>RunJobs(Input)</c> is the whole test. It drains everything at Input
    /// priority and above and leaves Background alone — which is what a hand
    /// that keeps moving does to a real dispatcher, compressed into one call
    /// and with no clock involved.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void APassQueuedDuringABurstIsNotLeftBehindByIt()
    {
        var vm = Ready();
        Assert.Equal(DispatcherPriority.Input, MainViewModel.LivePostPriority);

        vm.BeginStroke(200, 400, 1);
        Dispatcher.UIThread.RunJobs();

        for (var i = 1; i <= 40; i++)
        {
            var x = 200 + i * 14.0;
            var y = 400 + Math.Sin(i * 0.3) * 60;
            Dispatcher.UIThread.Post(() => vm.MoveStroke(x, y, 0.9), DispatcherPriority.Input);
        }

        // Everything the pen queued, and nothing that was pushed below it.
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);

        var passes = vm.LivePostPasses;
        output.WriteLine($"{passes} passes ran while the burst drained");
        vm.EndStroke();

        Assert.True(
            passes > 0,
            "the post-process never ran while the pen was moving — it is queued below the events "
            + "feeding it, so the mark stops at the last completed pass for as long as the hand does not");
    }

    /// <summary>
    /// The sensitivity half: the drain really does stop above Background.
    /// </summary>
    /// <remarks>
    /// Without this, the assertion above would pass just as well on a build
    /// where <c>RunJobs(Input)</c> quietly ran everything — and then it would be
    /// asserting nothing about priority at all. A job posted at Background must
    /// still be waiting when that call returns, or this file's whole method is
    /// unsound.
    /// </remarks>
    [AvaloniaFact]
    public void DrainingToInputLeavesBackgroundWorkAlone()
    {
        var ran = false;
        Dispatcher.UIThread.Post(() => ran = true, DispatcherPriority.Background);

        Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
        Assert.False(ran, "a Background job ran during a drain that should have stopped above it");

        Dispatcher.UIThread.RunJobs();
        Assert.True(ran, "the Background job never ran at all, so the probe proves nothing");
    }
}
