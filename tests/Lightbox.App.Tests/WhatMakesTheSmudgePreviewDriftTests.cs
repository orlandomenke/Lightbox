using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Why a brush that samples the layer beneath previews differently depending on
/// how often the frame is published, and — the half that decides how much it
/// matters — which side of the comparison moves (B337).
/// </summary>
/// <remarks>
/// <para>
/// <b>Found by changing something else, and the first three explanations were
/// all wrong.</b> Switching the publish route (B335) made
/// <c>AnEffectStroke_LooksTheSame_LiveAndCommitted</c> fail for
/// <c>builtin-smudge</c> and <c>builtin-blender</c> at 20 px and 13 px, worst
/// 1/255. The entry was filed blaming the live post-process pass. It is not
/// that, and it is not the route either.
/// </para>
/// <para>
/// <b>What the eliminations below establish, in order:</b> the divergence
/// reproduces on the shipped route with no route change at all, purely by
/// publishing once per pointer event instead of once for the burst — so it is a
/// defect that exists today and the inline arm merely makes it certain. Draining
/// the dispatcher <em>above</em> Input priority, which leaves the publish queued
/// and runs everything else, produces no divergence at all, so it is the publish
/// and nothing else on the queue. No post-process pass is ever queued for these
/// brushes and running the passes to completion changes nothing. The live tip
/// never engages. And publishing leaves the effect surface byte-for-byte as it
/// found it.
/// </para>
/// <para>
/// <b>So the drift is in how the displayed frame is composed, not in the mark.</b>
/// The one thing every failing arm has in common is that a publish happened
/// earlier in the same stroke — which points at compositing state seeded
/// mid-stroke, B332's family. That last hop is not pinned here, and the entry
/// says so rather than guessing a fourth time.
/// </para>
/// <para>
/// <b>The reassuring half, and it is asserted rather than assumed:</b> the
/// committed pixels are identical in every arm. The record is never wrong, only
/// the preview of it — which is why this is P2 and not P1, and why no artist has
/// ever lost anything to it.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class WhatMakesTheSmudgePreviewDriftTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>
    /// A view model with its OWN brush store.
    /// </summary>
    /// <remarks>
    /// <b>Learned the hard way inside this very class.</b> BrushStateIsolated
    /// gives each TEST a fresh store file; every view model built inside one
    /// test still shares it. So the first arm's ApplyPreset was handing its
    /// brush to the second arm, and the tell was two different presets
    /// returning a byte-identical commit. Any test that builds more than one
    /// view model and changes a brush needs this, not just the collection.
    /// </remarks>
    private static MainViewModel Ready()
    {
        MainViewModel.BrushStorePath = Path.Combine(
            Path.GetTempPath(), $"lightbox-b337-{Guid.NewGuid():N}.json");
        return NewVm();
    }

    private static MainViewModel NewVm() => new(null)
    {
        SmoothStrokes = false,
        ColorHex = "#000000",
        BrushSize = 24,
        BrushHardness = 1,
        BrushOpacity = 1,
        BrushFlow = 1,
        BrushWetEdge = 0,
        BrushGranulation = 0,
        BrushScatter = 0,
    };

    private static readonly SKRectI Region = new(60, 60, 380, 200);

    private static long Sum(SKBitmap b)
    {
        long h = 17;
        for (var y = Region.Top; y < Math.Min(Region.Bottom, b.Height); y++)
        for (var x = Region.Left; x < Math.Min(Region.Right, b.Width); x++)
        {
            var p = b.GetPixel(x, y);
            h = (h * 31) + p.Red + (p.Alpha * 7);
        }

        return h;
    }

    /// <summary>
    /// One smudge drag over a painted block, with the dispatcher pumped in one
    /// of three ways between the pen events.
    /// </summary>
    /// <param name="drain">
    /// <c>null</c> — nothing between events, so one publish covers the burst.
    /// <c>Background</c> — a full drain, so the Input-priority publish runs each
    /// time. <c>Send</c> — a drain that deliberately leaves the publish queued
    /// and runs everything above it, which is the arm that separates "the
    /// publish" from "anything else the turn happened to run".
    /// </param>
    private (int Differing, int Worst, long LiveSum, long CommitSum, int Publishes, int Passes)
        Drag(string presetId, DispatcherPriority? drain, bool wholeCanvas = false)
    {
        var vm = Ready();
        var work = new Queue<Action>();
        vm.LivePostRunner = w => { work.Enqueue(w); return System.Threading.Tasks.Task.CompletedTask; };

        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        vm.ColorHex = "#101010";
        vm.BrushSize = 60;
        vm.BeginStroke(80, 90, 1);
        vm.MoveStroke(320, 90, 1);
        vm.MoveStroke(320, 170, 1);
        vm.MoveStroke(80, 170, 1);
        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
        work.Clear();

        vm.ApplyPreset(vm.BrushPresetChoices.First(p => p.Id == presetId));
        vm.BrushSize = 40;

        var publishes = 0;
        vm.SnapshotChanged += _ => publishes++;

        vm.BeginStroke(120, 110, 1);
        foreach (var (mx, my) in new (double, double)[]
                 { (160, 118), (205, 126), (250, 134), (292, 142), (330, 150) })
        {
            vm.MoveStroke(mx, my, 1);
            // The hypothesis, tested by removing the variable: if the drift is
            // the dirty rectangle missing pixels the smudge rewrote behind the
            // pen, then repainting everything makes it disappear.
            if (wholeCanvas) vm.MarkWholeCanvasDirtyForTests();
            if (drain is { } p) Dispatcher.UIThread.RunJobs(p);
        }

        Dispatcher.UIThread.RunJobs();
        var passes = work.Count;

        Assert.NotNull(latest);
        using var live = SKBitmap.FromImage(latest!.Image)!;
        var liveCopy = live.Copy();

        vm.EndStroke();
        while (work.Count > 0)
        {
            work.Dequeue()();
            Dispatcher.UIThread.RunJobs();
        }

        Dispatcher.UIThread.RunJobs();
        using var committed = SKBitmap.FromImage(latest!.Image)!;

        int differing = 0, worst = 0;
        for (var y = Region.Top; y < Math.Min(Region.Bottom, committed.Height); y++)
        for (var x = Region.Left; x < Math.Min(Region.Right, committed.Width); x++)
        {
            var a = liveCopy.GetPixel(x, y);
            var b = committed.GetPixel(x, y);
            var d = Math.Max(Math.Abs(a.Red - b.Red), Math.Abs(a.Alpha - b.Alpha));
            if (d == 0) continue;
            differing++;
            worst = Math.Max(worst, d);
        }

        var result = (differing, worst, Sum(liveCopy), Sum(committed), publishes, passes);
        liveCopy.Dispose();
        return result;
    }

    /// <summary>
    /// **The invariant that holds.** However often the frame is published, the
    /// mark that gets saved is the same one.
    /// </summary>
    /// <remarks>
    /// This is the assertion that decides the severity of the whole entry, and
    /// it is the one worth defending hardest: invariant 1 says the record is the
    /// document and the pixels are derived from it. A commit that moved with the
    /// publish rate would mean the schedule was an input to the picture, which
    /// would be a data-loss bug rather than a cosmetic one.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData("builtin-smudge")]
    [InlineData("builtin-blender")]
    public void TheSavedMarkIsTheSameHoweverOftenWePublish(string presetId)
    {
        var coalesced = Drag(presetId, null);
        var perEvent = Drag(presetId, DispatcherPriority.Background);

        output.WriteLine(
            $"{presetId}: {coalesced.Publishes} publishes -> commit {coalesced.CommitSum}");
        output.WriteLine(
            $"{presetId}: {perEvent.Publishes} publishes -> commit {perEvent.CommitSum}");

        // Inertness: if both arms published the same number of times, the
        // question was never put.
        Assert.True(
            perEvent.Publishes > coalesced.Publishes,
            $"both arms published {perEvent.Publishes} times, so the publish rate was "
            + "never actually varied and the equality below is trivial");

        Assert.Equal(coalesced.CommitSum, perEvent.CommitSum);
    }

    /// <summary>
    /// And the eliminations: it is the publish, and it is not the pass, the tip
    /// or the effect surface.
    /// </summary>
    /// <remarks>
    /// Reported rather than bounded. B337 is open and the numbers here are what
    /// it is open about — asserting the drift away would lock the defect in, and
    /// asserting it to zero would be a red suite on a known bug. What IS asserted
    /// is the part that must not change while the fix is found: which arm is
    /// clean, and that the diagnostic is not inert.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData("builtin-smudge")]
    [InlineData("builtin-blender")]
    public void ThePreviewMatchesTheCommitHoweverOftenWePublish(string presetId)
    {
        var coalesced = Drag(presetId, null);
        var perEvent = Drag(presetId, DispatcherPriority.Background);
        var aboveInput = Drag(presetId, DispatcherPriority.Send);

        output.WriteLine($"{presetId}");
        output.WriteLine(
            $"  nothing between events      {coalesced.Publishes} publishes"
            + $"   {coalesced.Differing} px differ (worst {coalesced.Worst}/255)");
        output.WriteLine(
            $"  full drain between events   {perEvent.Publishes} publishes"
            + $"   {perEvent.Differing} px differ (worst {perEvent.Worst}/255)");
        output.WriteLine(
            $"  drain ABOVE Input only      {aboveInput.Publishes} publishes"
            + $"   {aboveInput.Differing} px differ (worst {aboveInput.Worst}/255)");
        output.WriteLine(
            $"  post-process passes queued: {coalesced.Passes} / {perEvent.Passes} / {aboveInput.Passes}");

        // **This is the regression test, and it is the inversion its own earlier
        // version asked for.** While B337 was open this asserted that the
        // per-event arm still diverged, so the diagnostic could not pass while
        // inert. The fix landed and the assertion turned over.
        Assert.Equal(0, coalesced.Differing);
        Assert.Equal(0, aboveInput.Differing);
        Assert.Equal(0, perEvent.Differing);

        // Inertness, which the "must still diverge" line used to provide for
        // free and now has to be stated: if the arms published the same number
        // of times, the variable was never moved and three zeroes prove nothing.
        Assert.True(
            perEvent.Publishes > coalesced.Publishes,
            $"every arm published {perEvent.Publishes} times, so the publish rate was "
            + "never varied and the equalities above are trivial");

        // These brushes take no worker pass at all, which is what removed the
        // first explanation the entry was filed with.
        Assert.Equal(0, perEvent.Passes);
    }

    /// <summary>
    /// Publishing leaves the effect brushes' working surface exactly as it found
    /// it — so the drift is in how the frame is composed, not in the mark.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("builtin-smudge")]
    [InlineData("builtin-blender")]
    public void TheNarrowRepaintNowAgreesWithTheBlanketOne(string presetId)
    {
        var narrow = Drag(presetId, DispatcherPriority.Background);
        var blanket = Drag(presetId, DispatcherPriority.Background, wholeCanvas: true);

        output.WriteLine($"{presetId}: narrow repaint   {narrow.Differing} px differ");
        output.WriteLine($"{presetId}: whole canvas     {blanket.Differing} px differ");
        output.WriteLine($"{presetId}: live sums        {narrow.LiveSum} / {blanket.LiveSum}");

        // **How the fault was located, kept as the test that it stays fixed.**
        // Repainting everything was the experiment that identified the dirty
        // rectangle as the culprit: it made the drift vanish while nothing else
        // changed. The fix marks the pixels the smudge rewrites behind the pen
        // instead, so the narrow repaint must now produce the SAME image as the
        // blanket one — not merely a matching pixel count.
        Assert.Equal(0, narrow.Differing);
        Assert.Equal(0, blanket.Differing);
        Assert.Equal(blanket.LiveSum, narrow.LiveSum);
    }

    [AvaloniaTheory]
    [InlineData("builtin-smudge")]
    [InlineData("builtin-blender")]
    public void PublishingDoesNotTouchTheEffectSurface(string presetId)
    {
        var vm = Ready();
        vm.ColorHex = "#101010";
        vm.BrushSize = 60;
        vm.BeginStroke(80, 90, 1);
        vm.MoveStroke(320, 90, 1);
        vm.MoveStroke(320, 170, 1);
        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();

        vm.ApplyPreset(vm.BrushPresetChoices.First(p => p.Id == presetId));
        vm.BrushSize = 40;
        vm.BeginStroke(120, 110, 1);
        vm.MoveStroke(160, 118, 1);
        vm.MoveStroke(205, 126, 1);

        var surface = vm.LiveCompositeForTests;
        Assert.NotNull(surface);
        var before = Sum(surface!);
        Dispatcher.UIThread.RunJobs();
        var after = Sum(vm.LiveCompositeForTests!);

        output.WriteLine($"{presetId}: effect surface {before} -> {after}");
        Assert.Equal(before, after);

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
    }
}
