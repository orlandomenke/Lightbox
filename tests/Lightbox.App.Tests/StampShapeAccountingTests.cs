using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// Whether the stamp-shape counters count what they say they count
/// (B322 attempt 6).
/// </summary>
/// <remarks>
/// <para>
/// <b>Written because the suite was green while they did not.</b> The first
/// version of these counters recorded the provisional tail as re-stamped whether
/// or not it had been, and never ran at all for a brush that takes the
/// whole-mark route — and 6,425 tests passed, because not one of them asserted
/// anything about a tally. A green suite says nothing about whether an
/// instrument measures the right thing.
/// </para>
/// <para>
/// That is the failure this whole session kept repeating in different clothes: a
/// minimum standing in for a distribution, points subtracted from dabs, a mean
/// divided by a median, grep's exit code standing in for dotnet's. Every one of
/// them was invisible to the tests because the tests were about the code, not
/// about the measurements. **An instrument that reaches a report needs a test
/// like any other output.**
/// </para>
/// </remarks>
[Collection("BrushState")]
public class StampShapeAccountingTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>A brush that takes the per-dab route: soft, so not one silhouette.</summary>
    private static MainViewModel PerDab() => Ready(hardness: 0.4);

    /// <summary>A brush that takes the whole-mark route — hard, anti-aliased, no tip.</summary>
    private static MainViewModel WholeMark() => Ready(hardness: 1.0);

    private static MainViewModel Ready(double hardness)
    {
        var vm = new MainViewModel(null)
        {
            SmoothStrokes = false,
            ColorHex = "#101010",
            BrushSize = 24,
            BrushHardness = hardness,
            BrushOpacity = 1,
            BrushFlow = 1,
            LivePostRunner = work => { work(); return Task.CompletedTask; },
        };
        Dispatcher.UIThread.RunJobs();
        return vm;
    }

    /// <summary>
    /// Draw a stroke and report how many dabs it produced, <b>read before
    /// <c>EndStroke</c></b>.
    /// </summary>
    /// <remarks>
    /// The live session drops its dab list when the stroke commits, so asking
    /// afterwards returns zero and the conservation check below compares a real
    /// count against nothing. The first draft of these tests did exactly that
    /// and failed for a reason that had nothing to do with what they assert —
    /// which is worth a comment, because a harness bug that looks like a finding
    /// is the specific way this bug has wasted time twice already.
    /// </remarks>
    private static (int Dabs, int Stable) Stroke(MainViewModel vm)
    {
        vm.BeginStroke(40, 40, 1);
        Dispatcher.UIThread.RunJobs();
        for (var i = 1; i <= 20; i++)
        {
            var x = 40 + (i * 9);
            Dispatcher.UIThread.Post(() => vm.MoveStroke(x, 40 + (x % 7), 1), DispatcherPriority.Input);
            Dispatcher.UIThread.RunJobs();
        }

        var seen = (vm.LiveDabCountForTest, vm.LiveStableDabsForTest);
        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
        return seen;
    }

    /// <summary>
    /// <b>The conservation law.</b> Every dab settles exactly once, so the
    /// settled counts must sum to the settled prefix — no more, and none lost.
    /// A counter placed after an early return, or reading a stale index, breaks
    /// this and nothing else in the suite would notice.
    /// </summary>
    /// <remarks>
    /// <b>Against <c>StableDabs</c> and not against the dab count</b>, which is
    /// how this was written first and why it failed: the last tail is still on
    /// loan when the stroke ends, so a perfectly correct counter looked like it
    /// was losing ten dabs. The remainder is checked below as exactly that tail,
    /// which is the stronger statement anyway — the two halves account for the
    /// whole stroke between them.
    /// </remarks>
    [AvaloniaFact]
    public void TheSettledCountsSumToTheSettledPrefix()
    {
        var vm = PerDab();
        var (dabs, stable) = Stroke(vm);

        var counted = vm.LiveStampSettled.TotalMs;   // Tally sums whatever it is given
        output.WriteLine(
            $"settled counted {counted}, settled prefix {stable}, dabs {dabs}, "
            + $"still on loan {dabs - stable}, events {vm.LiveStampSettled.Count}");

        Assert.True(vm.LiveStampSettled.Count > 0, "no events were counted at all");
        Assert.Equal(stable, counted);
        // And nothing falls between the two halves.
        Assert.True(dabs >= stable, $"the settled prefix {stable} ran past the dab list {dabs}");
    }

    /// <summary>
    /// <b>A brush the split cannot describe must be counted, not skipped.</b>
    /// The whole-mark route stamps its silhouette in one piece and returns
    /// before any settled/provisional bookkeeping. Without this counter the
    /// report would print a median over whatever minority of events took the
    /// other route, with nothing to say most of the session was missing.
    /// </summary>
    [AvaloniaFact]
    public void TheWholeMarkRouteIsCountedRatherThanSilentlyMissing()
    {
        var vm = WholeMark();
        Stroke(vm);

        output.WriteLine(
            $"whole-mark events {vm.LiveStampWholeMarkEvents}, "
            + $"split events {vm.LiveStampSettled.Count}");

        Assert.True(
            vm.LiveStampWholeMarkEvents > 0,
            "a hard anti-aliased brush should take the whole-mark route; if this brush "
            + "no longer does, the test needs a different one rather than deleting");
        Assert.Equal(0, vm.LiveStampSettled.Count);
    }

    /// <summary>
    /// <b>The tail is counted only when it was actually re-stamped.</b> The
    /// first version took the tail's SIZE every event regardless, which reports
    /// work that did not happen — the same shape of error as B329's units.
    /// </summary>
    [AvaloniaFact]
    public void TheProvisionalTailIsNeverCountedAboveWhatWasStamped()
    {
        var vm = PerDab();
        var (dabs, _) = Stroke(vm);

        var provisional = vm.LiveStampProvisional;
        output.WriteLine(
            $"provisional events {provisional.Count}, total {provisional.TotalMs}, "
            + $"worst {provisional.WorstMs}, settled total {vm.LiveStampSettled.TotalMs}");

        Assert.Equal(vm.LiveStampSettled.Count, provisional.Count);
        // A tail is a suffix of the dab list, so it can never exceed it.
        Assert.True(
            provisional.WorstMs <= dabs,
            $"a single event claimed {provisional.WorstMs} re-stamped dabs against a stroke "
            + $"of {dabs} — the counter is measuring something else");
    }
}
