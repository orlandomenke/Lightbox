using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// B28's scan protection, extended to the tile store now that playback
/// publishes through it (Q62). The mirror of <see cref="ScanEvictionTests"/>,
/// held to the same numbers.
/// </summary>
/// <remarks>
/// The cost of not having this was measured before it was built: on dense ink
/// at 1440p, tile thrash ran 277 ms a frame against the 186 ms of the bitmap
/// thrash it replaced — a tile-frame miss rebuilds every tile the frame's ink
/// reaches plus its stroke index, so a thrashing tile cache is dearer per miss
/// than a thrashing bitmap cache. The policy flip is what keeps the tile path
/// from being a downgrade on exactly the documents big enough to need it.
/// </remarks>
[Collection("FrameCacheBudget")]
public class TileScanEvictionTests
{
    private const int W = 320, H = 180;

    private static List<Frame> Sheet(int n)
    {
        var frames = new List<Frame>();
        for (var i = 0; i < n; i++)
        {
            var f = new Frame();
            // Ink spanning several tiles, so a frame's entry has real weight
            // and the byte budget bites after a handful of frames.
            f.Strokes.Add(new Stroke
            {
                Tool = ToolKind.Brush,
                Color = "#303030",
                Points = [new StrokePoint(10 + i, 20, 1), new StrokePoint(300 + i, 160, 1)],
                Brush = new BrushSettings { Size = 24, Hardness = 0.8, Opacity = 1, Flow = 1 },
            });
            frames.Add(f);
        }
        return frames;
    }

    /// <summary>Walk the sheet in order twice; count how many of the second lap hit.</summary>
    private static (int Hits, int Total) SecondLapHits(FrameBitmapCache.EvictionOrder order, int n)
    {
        var previous = TileFrameCache.ByteBudget;
        try
        {
            using var cache = new TileFrameCache { Eviction = order };
            var frames = Sheet(n);

            // Size the budget to hold about half the sheet, measured rather
            // than assumed: tile entries vary with ink reach, where a frame
            // bitmap is always w*h*4.
            foreach (var f in frames) cache.Get(f, W, H);
            var perFrame = Math.Max(1, cache.AllocatedBytes / Math.Max(1, cache.CachedFrames));
            cache.Clear();
            TileFrameCache.ByteBudget = perFrame * (n / 2);

            // Identity, not counts — a miss that inserts one entry and evicts
            // another leaves every total where it was (see ScanEvictionTests).
            var first = new List<TileStore>();
            foreach (var f in frames) first.Add(cache.Get(f, W, H).Store);

            var hits = 0;
            for (var i = 0; i < frames.Count; i++)
            {
                if (ReferenceEquals(cache.Get(frames[i], W, H).Store, first[i])) hits++;
            }
            return (hits, n);
        }
        finally
        {
            TileFrameCache.ByteBudget = previous;
        }
    }

    [AvaloniaFact]
    public void AnLruScanThrowsAwayEverythingItIsAboutToNeed()
    {
        // Pins the reason the mode exists, so deleting it does not read as
        // harmless — same shape as the bitmap cache's own pin.
        var (hits, total) = SecondLapHits(FrameBitmapCache.EvictionOrder.LeastRecent, 24);

        Assert.True(hits <= total / 8, $"expected an LRU scan to miss nearly everything, got {hits}/{total}");
    }

    [AvaloniaFact]
    public void EvictingTheMostRecentKeepsHalfTheSheetResident()
    {
        var (hits, total) = SecondLapHits(FrameBitmapCache.EvictionOrder.MostRecent, 24);

        Assert.True(hits >= total / 3, $"scan eviction bought nothing: {hits}/{total}");
    }

    [AvaloniaFact]
    public void TheFrameBeingShownIsNeverTheOneEvicted()
    {
        var previous = TileFrameCache.ByteBudget;
        try
        {
            using var cache = new TileFrameCache
            {
                Eviction = FrameBitmapCache.EvictionOrder.MostRecent,
            };
            var frames = Sheet(12);
            foreach (var f in frames) cache.Get(f, W, H);
            var perFrame = Math.Max(1, cache.AllocatedBytes / Math.Max(1, cache.CachedFrames));
            cache.Clear();
            TileFrameCache.ByteBudget = perFrame * 3;

            foreach (var f in frames) cache.Get(f, W, H);
            var count = cache.CachedFrames;
            var bytes = cache.AllocatedBytes;

            cache.Get(frames[^1], W, H);

            Assert.Equal(count, cache.CachedFrames);
            Assert.Equal(bytes, cache.AllocatedBytes);
        }
        finally
        {
            TileFrameCache.ByteBudget = previous;
        }
    }

    [AvaloniaFact]
    public void DrawingKeepsTheLruItWants()
    {
        using var cache = new TileFrameCache();
        Assert.Equal(FrameBitmapCache.EvictionOrder.LeastRecent, cache.Eviction);
    }

    [AvaloniaFact]
    public void AWarmAtTheTailSurvivesAScanEviction()
    {
        // The prewarmer and scan eviction agree by construction: InsertWarm
        // enters at the tail and never evicts, the most-recent victim is the
        // head's neighbour — so churn lands on the recently shown frames and
        // the warmed ones, the frames about to be needed, stay put. This test
        // is that sentence, so a change to either end has to argue with it.
        var previous = TileFrameCache.ByteBudget;
        try
        {
            using var cache = new TileFrameCache
            {
                Eviction = FrameBitmapCache.EvictionOrder.MostRecent,
            };
            var frames = Sheet(8);

            foreach (var f in frames) cache.Get(f, W, H);
            var perFrame = Math.Max(1, cache.AllocatedBytes / Math.Max(1, cache.CachedFrames));
            cache.Clear();
            TileFrameCache.ByteBudget = perFrame * 4;

            // A warm enters at the tail while there is room for it.
            var (store, pyramid) = TileFrameCache.RenderDetached(frames[7], W, H);
            Assert.True(cache.InsertWarm(frames[7], store, pyramid));

            // Sync misses push the cache over budget; the scan victims must be
            // the recent sync entries, never the warm.
            foreach (var f in frames.Take(6)) cache.Get(f, W, H);

            Assert.True(
                cache.Holds(frames[7].Id),
                "scan eviction took the prewarmed frame — the one entry it exists to protect");
        }
        finally
        {
            TileFrameCache.ByteBudget = previous;
        }
    }
}

/// <summary>The flip has to reach the tile cache when playback starts.</summary>
[Collection("BrushState")]
public class TilePlaybackEvictionTests : BrushStateIsolated
{
    [AvaloniaFact]
    public void PlaybackFlipsTheTileCacheToScanEvictionAndBack()
    {
        var vm = VmLayers.PaperVm();

        Assert.Equal(FrameBitmapCache.EvictionOrder.LeastRecent, vm.TileFrames.Eviction);
        vm.TogglePlaybackCommand.Execute(null);
        Assert.Equal(FrameBitmapCache.EvictionOrder.MostRecent, vm.TileFrames.Eviction);
        vm.TogglePlaybackCommand.Execute(null);
        Assert.Equal(FrameBitmapCache.EvictionOrder.LeastRecent, vm.TileFrames.Eviction);
    }
}
