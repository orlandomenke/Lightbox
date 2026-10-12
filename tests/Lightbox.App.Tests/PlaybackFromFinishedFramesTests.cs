using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// A playing frame whose finished composite is already in the cache is shown
/// from it, without building the frame at all (prepared playback frames, step 1).
/// </summary>
/// <remarks>
/// <para>
/// The composite cache (B167 phase 7) made a second lap's blend free, but the
/// tick still built the frame first — the pass list, the folds, and flattening
/// the tiles, all on the UI thread — only for the render thread to find the
/// answer in the cache. With playback paced by the screen's refresh (Q248) that
/// UI work holds the refresh itself. Now the tick asks the cache first, by the
/// same key, and a hit is published as it is.
/// </para>
/// <para>
/// The image is borrowed from the cache under its own hold (the B130 protocol):
/// the snapshot releases it rather than freeing it.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class PlaybackFromFinishedFramesTests : BrushStateIsolated
{
    private static MainViewModel Vm(ComposeCache cache)
    {
        var vm = VmLayers.PaperVm();
        vm.Onion.Enabled = false;
        vm.SmoothStrokes = false;
        vm.BrushSize = 30;
        // Part of the page in view: the routes the cache can serve.
        vm.SetViewport(SKRectI.Create(0, 0, 600, 340));
        vm.PlaybackComposites = cache;
        for (var i = 0; i < 3; i++)
        {
            if (i > 0) vm.AddFrameCommand.Execute(null);
            vm.BeginStroke(100 + i * 60, 100, 1);
            vm.MoveStroke(400 - i * 40, 200, 1);
            vm.EndStroke();
        }
        vm.CurrentFrameIndex = 0;
        return vm;
    }

    /// <summary>Play frames 0..2 once, handing each to <paramref name="cache"/> as the canvas would.</summary>
    private static List<RenderSnapshot> Lap(MainViewModel vm, ComposeCache cache, bool materialise = true)
    {
        var shown = new List<RenderSnapshot>();
        void Take(RenderSnapshot s) => shown.Add(s);
        vm.CurrentFrameIndex = 0; // paused: not part of the lap
        vm.SnapshotChanged += Take;
        try
        {
            vm.TogglePlaybackCommand.Execute(null);
            vm.CurrentFrameIndex = 1;
            vm.CurrentFrameIndex = 2;
        }
        finally
        {
            vm.SnapshotChanged -= Take;
        }
        if (materialise)
        {
            foreach (var s in shown) s.Materialise(null, null, cache);
        }
        vm.TogglePlaybackCommand.Execute(null);
        return shown;
    }

    private static byte[] Bytes(SKImage image)
    {
        using var bmp = SKBitmap.FromImage(image);
        return bmp.Bytes;
    }

    [AvaloniaFact]
    public void ASecondLapIsShownFromTheCacheWithoutBuildingTheFrames()
    {
        var cache = new ComposeCache(256L * 1024 * 1024);
        var vm = Vm(cache);
        Lap(vm, cache);
        var served = vm.PlaybackFramesFromCache;

        var lapTwo = Lap(vm, cache, materialise: false);

        Assert.Equal(3, lapTwo.Count);
        Assert.All(lapTwo, s =>
        {
            Assert.True(s.FromCache, "a frame already composed was built again");
            Assert.NotNull(s.Image); // finished on arrival: nothing for the render thread to do
        });
        Assert.Equal(served + 3, vm.PlaybackFramesFromCache);
    }

    [AvaloniaFact]
    public void TheFrameShownIsThePictureTheFirstLapComposed()
    {
        var cache = new ComposeCache(256L * 1024 * 1024);
        var vm = Vm(cache);
        var lapOne = Lap(vm, cache).Select(s => Bytes(s.Image!)).ToList();

        var lapTwo = Lap(vm, cache, materialise: false);

        for (var i = 0; i < 3; i++)
        {
            Assert.True(lapOne[i].AsSpan().SequenceEqual(Bytes(lapTwo[i].Image!)), $"frame {i} differs from the one composed");
        }
    }

    [AvaloniaFact]
    public void TheCacheStillOwnsWhatAFrameWasShownFrom()
    {
        var cache = new ComposeCache(256L * 1024 * 1024);
        var vm = Vm(cache);
        Lap(vm, cache);
        var count = cache.Count;

        var lapTwo = Lap(vm, cache, materialise: false);
        var images = lapTwo.Select(s => s.Image!).ToList();
        foreach (var s in lapTwo) s.Dispose();

        Assert.Equal(count, cache.Count);
        Assert.All(images, i => Assert.True(i.Handle != IntPtr.Zero, "a frame freed the composite the cache owns"));
    }
}
