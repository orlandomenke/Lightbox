using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Playback on a bounded document goes through the tile store (B144/Q62), so a
/// cel costs its ink rather than its paper while frames are flipping — and the
/// still picture stays exactly what it was before.
/// </summary>
/// <remarks>
/// The line these tests hold is the one the gate draws: <b>motion is tiled,
/// rest is canonical.</b> A paused publish must be byte-identical to what the
/// bounded compositor always produced; a playing publish may come from tiles,
/// and at 100% zoom those pixels are pinned bit-identical to the untiled render
/// by <c>ATiledRenderIsBitIdenticalToAnUntiledOne</c> in the Raster suite. What
/// the App suite owns is the seam between the two: the switch happens at all,
/// pixels agree across it, and a stroke committed while paused is never missing
/// from the next play.
/// </remarks>
[Collection("BrushState")]
public class PlaybackThroughTilesTests : BrushStateIsolated
{
    private static MainViewModel VmWithTwoInkedFrames()
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 12;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;

        // The tile path needs what the app always has and headless tests do
        // not: a viewport. Same arrangement lesson UnboundedCanvasPixelTests
        // records — without this the gate is never taken and every assertion
        // below passes against the wrong compositor.
        vm.SetViewport(SKRectI.Create(0, 0, 960, 540));

        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(300, 200, 1);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null);
        vm.BeginStroke(300, 100, 1);
        vm.MoveStroke(100, 200, 1);
        vm.EndStroke();
        vm.CurrentFrameIndex = 0;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return vm;
    }

    private static SKBitmap Pixels(Lightbox.App.Rendering.RenderSnapshot snapshot)
    {
        // B167 phase 3b: the tiled route hands over a description rather than
        // pixels, so the composite is performed here — on the CPU, since a test
        // has no lease to take a GRContext from.
        using var composed = snapshot.Materialise(null);
        var bmp = SKBitmap.FromImage(composed);
        Assert.NotNull(bmp);
        return bmp!;
    }

    [AvaloniaFact]
    public void PlaybackHoldsFramesAsTilesOnABoundedDocument()
    {
        var vm = VmWithTwoInkedFrames();
        Assert.Equal(0, vm.TileFrames.CachedFrames);

        vm.TogglePlaybackCommand.Execute(null);
        vm.StepPlayback();
        vm.PublishSnapshot();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(vm.IsPlaying);
        Assert.True(
            vm.TileFrames.CachedFrames > 0,
            "A playing publish on a bounded document should populate the tile store");
        vm.TogglePlaybackCommand.Execute(null);
    }

    [AvaloniaFact]
    public void ThePausedPictureIsUntouchedByThePlayingPath()
    {
        var vm = VmWithTwoInkedFrames();
        Lightbox.App.Rendering.RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        vm.PublishSnapshot();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using var before = Pixels(latest!);

        // A full play cycle, then pause back on the same frame.
        vm.TogglePlaybackCommand.Execute(null);
        vm.StepPlayback();
        vm.PublishSnapshot();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.TogglePlaybackCommand.Execute(null);
        vm.CurrentFrameIndex = 0;
        vm.PublishSnapshot();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using var after = Pixels(latest!);

        Assert.Equal(before.ByteCount, after.ByteCount);
        Assert.True(
            before.Bytes.AsSpan().SequenceEqual(after.Bytes),
            "Pausing must return the exact bounded render — the still picture is canonical");
    }

    [AvaloniaFact]
    public void PlayingAndPausedPixelsAgreeAtFullZoom()
    {
        var vm = VmWithTwoInkedFrames();
        Lightbox.App.Rendering.RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        vm.CurrentFrameIndex = 1;
        vm.PublishSnapshot();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using var paused = Pixels(latest!);

        vm.TogglePlaybackCommand.Execute(null);
        vm.CurrentFrameIndex = 1;
        vm.PublishSnapshot();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using var playing = Pixels(latest!);
        vm.TogglePlaybackCommand.Execute(null);

        // Sample where frame 1's ink is: the diagonal from (300,100) to
        // (100,200). Bit-identity of the tiled render is the Raster suite's
        // pin; here it is enough that the mark is present and dark on both,
        // because the failure this guards is a blank or misplaced tile pass.
        var pausedInk = paused.GetPixel(200, 150);
        var playingInk = playing.GetPixel(200, 150);
        Assert.True(pausedInk.Red < 100, $"paused ink {pausedInk} should be dark");
        Assert.True(playingInk.Red < 100, $"playing ink {playingInk} should be dark");
    }

    [AvaloniaFact]
    public void AStrokeCommittedWhilePausedReachesTheNextPlay()
    {
        var vm = VmWithTwoInkedFrames();
        Lightbox.App.Rendering.RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        // Warm the tile store by playing once.
        vm.TogglePlaybackCommand.Execute(null);
        vm.PublishSnapshot();
        vm.StepPlayback();
        vm.PublishSnapshot();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.TogglePlaybackCommand.Execute(null);

        // Draw on frame 0 while paused — the bitmap path is active, and the
        // warmed tiles must not be left one stroke behind.
        vm.CurrentFrameIndex = 0;
        vm.BeginStroke(500, 300, 1);
        vm.MoveStroke(700, 300, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.TogglePlaybackCommand.Execute(null);
        vm.CurrentFrameIndex = 0;
        vm.PublishSnapshot();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using var playing = Pixels(latest!);
        vm.TogglePlaybackCommand.Execute(null);

        var ink = playing.GetPixel(600, 300);
        Assert.True(
            ink.Red < 100,
            $"the paused-time stroke should be in the playing render, got {ink}");
    }
}
