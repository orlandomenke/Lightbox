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
public class LivePreviewPixelTests
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
}
