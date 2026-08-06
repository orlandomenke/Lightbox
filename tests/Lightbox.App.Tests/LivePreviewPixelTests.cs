using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Drives the real live-preview pipeline (BeginStroke → MoveStrokeBatch →
/// published RenderSnapshot) and inspects the pixels the canvas would show —
/// the headless equivalent of watching the screen while drawing.
/// </summary>
/// <remarks>
/// In the <c>BrushState</c> collection because it sets brush parameters, and
/// those live in a process-wide store: running beside a test that assumes
/// defaults hands it this one’s brush. It opted out until a CI run caught
/// it — the live-preview pixel check went red on a loaded machine and was
/// green on every local run, which is what this collection exists to stop.
/// </remarks>
[Collection("BrushState")]
public class LivePreviewPixelTests : BrushStateIsolated
{
    private static MainViewModel PinnedVm(double opacity = 1) => new(null)
    {
        SmoothStrokes = false,
        ColorHex = "#000000",
        BrushSize = 24,
        BrushHardness = 1,
        BrushOpacity = opacity,
        BrushFlow = 1,
        BrushWetEdge = 0,
        BrushGranulation = 0,
        BrushScatter = 0,
    };

    private static SKBitmap LatestPixels(RenderSnapshot snapshot)
    {
        var bmp = SKBitmap.FromImage(snapshot.Image);
        Assert.NotNull(bmp);
        return bmp!;
    }

    [AvaloniaFact]
    public void MidStroke_ThePublishedSnapshot_ShowsTheLine()
    {
        var vm = PinnedVm();
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(300, 100, 1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // flush the coalesced publish

        Assert.NotNull(latest);
        using var bmp = LatestPixels(latest!);
        Assert.True(bmp.GetPixel(200, 100).Red < 100,
            "the live preview did not ink the stroke midpoint while drawing");

        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using var committed = LatestPixels(latest!);
        Assert.True(committed.GetPixel(200, 100).Red < 100,
            "the committed stroke vanished from the snapshot after pen lift");
    }

    [AvaloniaFact]
    public void SelfCrossing_LooksTheSame_LiveAndCommitted()
    {
        var vm = PinnedVm(opacity: 0.5);
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        // An X drawn as one stroke: down-right, then jump up, then down-left,
        // crossing at (200,150).
        vm.BeginStroke(100, 50, 1);
        vm.MoveStroke(300, 250, 1);
        vm.MoveStroke(300, 50, 1);
        vm.MoveStroke(100, 250, 1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(latest);
        byte liveAtCrossing;
        using (var bmp = LatestPixels(latest!))
        {
            liveAtCrossing = bmp.GetPixel(200, 150).Red;
        }

        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        byte committedAtCrossing;
        using (var bmp = LatestPixels(latest!))
        {
            committedAtCrossing = bmp.GetPixel(200, 150).Red;
        }

        // 50% black over white paper is mid-grey in BOTH: the preview must not
        // double-darken the crossing and then "fade" on pen lift.
        Assert.True(committedAtCrossing is > 100 and < 160,
            $"committed crossing should be ~50% grey, was {committedAtCrossing}");
        Assert.True(Math.Abs(liveAtCrossing - committedAtCrossing) <= 10,
            $"preview ({liveAtCrossing}) and commit ({committedAtCrossing}) diverge at the crossing");
    }

    /// <summary>
    /// B69/B89 through the real pipeline, which is the half an engine test cannot reach.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>EffectPreviewMatchesCommitTests</c> proves the engine's resumable range reproduces a
    /// single pass exactly. It cannot prove the <em>application</em> feeds it that way — and
    /// feeding was the entire bug, since the engine was always correct when handed a whole
    /// stroke. This drives BeginStroke → several MoveStroke events → EndStroke and compares the
    /// published frame either side of the pen lift.
    /// </para>
    /// <para>
    /// Several separate <c>MoveStroke</c> calls on purpose: one call would be a single segment
    /// and would have passed against the broken code.
    /// </para>
    /// </remarks>
    [AvaloniaTheory]
    [InlineData("builtin-smudge")]
    [InlineData("builtin-blender")]
    public void AnEffectStroke_LooksTheSame_LiveAndCommitted(string presetId)
    {
        var vm = PinnedVm();
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        // Something to push around. Smudging bare paper is a no-op by construction.
        vm.ColorHex = "#101010";
        vm.BrushSize = 60;
        vm.BeginStroke(80, 90, 1);
        vm.MoveStroke(320, 90, 1);
        vm.MoveStroke(320, 170, 1);
        vm.MoveStroke(80, 170, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.ApplyPreset(vm.BrushPresetChoices.First(p => p.Id == presetId));
        vm.BrushSize = 40;

        // A drag across the block's edge, one event per move — the feed that used to
        // restart the walk phase and the carried colour on every one of them.
        vm.BeginStroke(120, 110, 1);
        vm.MoveStroke(160, 118, 1);
        vm.MoveStroke(205, 126, 1);
        vm.MoveStroke(250, 134, 1);
        vm.MoveStroke(292, 142, 1);
        vm.MoveStroke(330, 150, 1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(latest);
        using var live = LatestPixels(latest!);
        var liveCopy = live.Copy();

        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using var committed = LatestPixels(latest!);

        int differing = 0, worst = 0;
        var region = new SKRectI(60, 60, Math.Min(380, committed.Width), Math.Min(200, committed.Height));
        for (var y = region.Top; y < region.Bottom; y++)
        for (var x = region.Left; x < region.Right; x++)
        {
            var a = liveCopy.GetPixel(x, y);
            var b = committed.GetPixel(x, y);
            var d = Math.Max(Math.Abs(a.Red - b.Red), Math.Abs(a.Alpha - b.Alpha));
            if (d == 0) continue;
            differing++;
            worst = Math.Max(worst, d);
        }

        // Not vacuous: the smudge has to have marked the block for "identical" to mean
        // anything. A brush that drew nothing would otherwise match perfectly.
        var moved = 0;
        for (var x = region.Left; x < region.Right; x++)
        {
            if (committed.GetPixel(x, 130).Red != committed.GetPixel(x, 62).Red) moved++;
        }
        Assert.True(moved > 40, $"the smudge barely marked the block ({moved} px) — nothing is being measured");

        Assert.True(
            differing == 0,
            $"{presetId}: preview and commit differ over {differing} px, worst {worst}/255 — "
            + "the stroke settled when the pen lifted");

        liveCopy.Dispose();
    }
}
