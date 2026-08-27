using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// What the dam's average hold is averaged over (B328).
/// </summary>
/// <remarks>
/// <para>
/// <b>The report printed a mean above its own worst for three captures running,
/// and it took an impossibility to notice.</b> <c>DamHeldTotalMs</c> accumulates
/// on every release, but its denominator was <c>ByPresent + ByTimer</c> — every
/// way a deferral could end until B321 added a third, a pointer event asking.
/// On the owner's machine that third way was 66%, 73% and 76% of releases, so
/// the mean came out 3-4x high: <b>67.3 ms beside a worst of 47.16</b>.
/// </para>
/// <para>
/// Nothing was wrong with the dam. The number describing it was wrong, it read
/// as the largest single cost in the pen-to-screen chain, and it was quoted as
/// one in the session that found it. **A diagnostic that lies is worse than no
/// diagnostic**, because it is acted on.
/// </para>
/// <para>
/// So the guard is the impossibility rather than the arithmetic: a mean may
/// never exceed the worst it sits beside. That holds whatever denominator
/// anyone chooses later, and it is the check that would have caught this on
/// the first capture.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class DamHoldAccountingTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel Ready()
    {
        var vm = new MainViewModel(null)
        {
            InFlightDepth = 1,
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
    /// Draw a stroke that actually defers, and whose deferrals are released by
    /// the next pointer event rather than by an announcement.
    /// </summary>
    /// <remarks>
    /// <b>The canvas has to stay one frame behind, and getting that wrong makes
    /// the test pass for no reason.</b> Marking every publish drawn as soon as it
    /// happens means the dam is never behind, nothing is ever held, and the
    /// counters this file is about all read zero — which asserts nothing while
    /// looking like a stroke. So the probe catches up only on alternate events:
    /// one event finds the last frame undrawn and is deferred, the next finds it
    /// drawn and lets the deferral through by asking. That is B321's fix and the
    /// mid-stroke case the old denominator could not see.
    /// </remarks>
    private static List<long> DriveAStrokeThatDefers(MainViewModel vm)
    {
        var seqs = new List<long>();
        vm.SnapshotChanged += s => seqs.Add(s.Seq);

        // Nothing is ever announced: the probe is the only way the dam can learn
        // of a draw, so a pointer event is the only thing that can release one.
        long drawn = 0;
        vm.SetRenderedSeqProbe(() => drawn);
        vm.SetRenderedAtProbe(() => System.Diagnostics.Stopwatch.GetTimestamp());

        vm.BeginStroke(50, 50, 1);
        Dispatcher.UIThread.RunJobs();

        for (var i = 0; i < 16; i++)
        {
            Move(vm, 60 + (i * 10), 50);
            if (i % 2 == 1 && seqs.Count > 0) drawn = seqs[^1];
        }

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
        return seqs;
    }

    /// <summary>
    /// A stroke whose deferrals are released by the artist's own events — the
    /// common case mid-stroke, and the one that was missing from the count.
    /// </summary>
    [AvaloniaFact]
    public void EveryTimedHoldIsCounted_IncludingTheOnesAPointerEventReleased()
    {
        var vm = Ready();
        DriveAStrokeThatDefers(vm);

        output.WriteLine(
            $"deferrals {vm.DamDeferrals}, timed {vm.DamHoldsTimed}, "
            + $"by present {vm.DamReleasedByPresent}, by timer {vm.DamReleasedByTimer}, "
            + $"by event {vm.DamReleasedByEvent}");

        Assert.True(vm.DamReleasedByEvent > 0,
            "no deferral was released by a pointer event, so this test is not "
            + "exercising the case the count was blind to");
        // The old denominator, written out so its failure is legible.
        var wasCountedBefore = vm.DamReleasedByPresent + vm.DamReleasedByTimer;
        Assert.True(vm.DamHoldsTimed > wasCountedBefore,
            $"holds timed {vm.DamHoldsTimed} against the old denominator "
            + $"{wasCountedBefore} — the event releases are still missing");
    }

    /// <summary>
    /// <b>The impossibility, guarded directly.</b> Whatever anybody divides by
    /// later, an average hold above the longest single hold is a broken number,
    /// and this is the assertion that survives a refactor of the counters.
    /// </summary>
    [AvaloniaFact]
    public void TheMeanHoldCanNeverExceedTheWorstHold()
    {
        var vm = Ready();
        DriveAStrokeThatDefers(vm);

        Assert.True(vm.DamHoldsTimed > 0, "nothing was held, so nothing is being checked");
        var mean = vm.DamHeldTotalMs / vm.DamHoldsTimed;
        output.WriteLine($"mean {mean:F3} ms over {vm.DamHoldsTimed} holds, worst {vm.DamHeldWorstMs:F3} ms");

        Assert.True(mean <= vm.DamHeldWorstMs + 1e-9,
            $"mean hold {mean:F3} ms exceeds the worst single hold "
            + $"{vm.DamHeldWorstMs:F3} ms — the total and its denominator have drifted");
    }

    /// <summary>
    /// And the report carries it through. The owner's capture of 2026-08-27
    /// 09:16 at its real numbers: 292 deferrals of which 221 were released by an
    /// event, a total averaging 16.36 ms, a worst of 47.16.
    /// </summary>
    [Fact]
    public void TheReportAveragesOverEveryTimedHold()
    {
        const int deferrals = 292;
        var line = DamLine((deferrals, 57, 14, deferrals, 16.36 * deferrals, 47.16, 0, 0, 221));
        output.WriteLine(line);

        Assert.Contains("mean 16.36 ms", line);
        Assert.Contains("worst 47.16 ms", line);
    }

    /// <summary>
    /// <b>And when the pair is impossible the report says so, rather than
    /// leaving a reader to notice.</b> This is the old denominator reproduced:
    /// the same total over only the present and timer releases, which printed
    /// 67.3 ms beside a worst of 47.16 and was read as a finding.
    /// </summary>
    [Fact]
    public void AMeanAboveTheWorstIsCalledOutAsImpossible()
    {
        const int deferrals = 292;
        var text = DamSection((deferrals, 57, 14, 57 + 14, 16.36 * deferrals, 47.16, 0, 0, 221));
        output.WriteLine(text);

        // 16.36 x 292 over the 71 present-and-timer releases: the capture's own
        // arithmetic, and 43% above the worst hold it sits beside.
        Assert.Contains("mean 67.28 ms", text);
        Assert.Contains("worst 47.16 ms", text);
        Assert.Contains("ABOVE the worst, which is impossible", text);
        Assert.Contains("B328", text);
    }

    /// <summary>A possible pair must not carry the warning, or it means nothing.</summary>
    [Fact]
    public void APossiblePairIsNotFlagged()
    {
        var text = DamSection((292, 57, 14, 292, 16.36 * 292, 47.16, 0, 0, 221));
        Assert.DoesNotContain("impossible", text);
    }

    private static string DamLine(
        (int, int, int, int, double, double, double, double, int) dam) =>
        DamSection(dam)
            .Split('\n')
            .First(l => l.Contains("publish held back", StringComparison.Ordinal))
            .Trim();

    private static string DamSection(
        (int Deferrals, int ByPresent, int ByTimer, int HoldsTimed,
         double HeldTotalMs, double HeldWorstMs,
         double LateTotalMs, double LateWorstMs, int ByEvent) dam)
    {
        var dir = Path.Combine(Path.GetTempPath(), "lightbox-dam-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            DiagnosticLog.DirectoryOverride = dir;
            RenderReport.ResetForTests();

            // The section prints only once a stroke has been measured, so it
            // needs events on the record to reach the dam at all.
            var strokes = new Lightbox.App.Rendering.StrokeToScreen.Stats(
                Events: 2402, Publishes: 435, Drawn: 415, Superseded: 19,
                Stamp: default, WaitToPublish: default, WaitToDraw: default,
                PenToScreen: default);

            var path = RenderReport.WriteStartup(
                RenderReportTests.Facts(strokeWait: strokes, dam: dam))!;
            return File.ReadAllText(path);
        }
        finally
        {
            DiagnosticLog.DirectoryOverride = null;
            RenderReport.ResetForTests();
            try { Directory.Delete(dir, recursive: true); } catch { /* a temp dir is not worth failing over */ }
        }
    }
}
