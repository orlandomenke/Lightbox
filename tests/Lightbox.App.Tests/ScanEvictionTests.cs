using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// B28: an LRU against a sequential scan has a zero hit rate, and playing,
/// scrubbing and exporting are all sequential scans.
/// </summary>
[Collection("FrameCacheBudget")]
public class ScanEvictionTests
{
    private const int W = 320, H = 180;

    private static List<Frame> Sheet(int n)
    {
        var frames = new List<Frame>();
        for (var i = 0; i < n; i++)
        {
            var f = new PaintedFrame();
            f.Strokes.Add(new Stroke
            {
                Tool = ToolKind.Brush,
                Color = "#303030",
                Points = [new StrokePoint(10 + i, 20, 1), new StrokePoint(60 + i, 90, 1)],
                Brush = new BrushSettings { Size = 8, Hardness = 0.8, Opacity = 1, Flow = 1 },
            });
            frames.Add(f);
        }
        return frames;
    }

    /// <summary>Walk the sheet in order twice; count how many of the second lap hit.</summary>
    private static (int Hits, int Total) SecondLapHits(FrameBitmapCache.EvictionOrder order, int n)
    {
        var previous = FrameBitmapCache.ByteBudget;
        // Room for about half the sheet, which is the interesting case: enough
        // for a policy to matter, not enough for the question to go away.
        FrameBitmapCache.ByteBudget = W * (long)H * 4 * (n / 2);
        try
        {
            using var cache = new FrameBitmapCache { Eviction = order };
            var frames = Sheet(n);

            // Identity, not counts. A miss that inserts one entry and evicts
            // one leaves both the frame count and the byte total exactly where
            // they were, so counting them reports every miss as a hit — which
            // it did, 24 out of 24, on a cache that was thrashing.
            var first = new List<SKBitmap>();
            foreach (var f in frames) first.Add(cache.Get(f, W, H));

            var hits = 0;
            for (var i = 0; i < frames.Count; i++)
            {
                if (ReferenceEquals(cache.Get(frames[i], W, H), first[i])) hits++;
            }
            return (hits, n);
        }
        finally
        {
            FrameBitmapCache.ByteBudget = previous;
        }
    }

    [AvaloniaFact]
    public void AnLruScanThrowsAwayEverythingItIsAboutToNeed()
    {
        // The defect, as a measurement rather than an argument. This is not a
        // test of the fix — it pins the reason the fix exists, so deleting the
        // mode does not read as harmless.
        var (hits, total) = SecondLapHits(FrameBitmapCache.EvictionOrder.LeastRecent, 24);


        Assert.True(hits <= total / 8, $"expected an LRU scan to miss nearly everything, got {hits}/{total}");
    }

    [AvaloniaFact]
    public void EvictingTheMostRecentKeepsHalfTheSheetResident()
    {
        // Half a cache beats none: the frames that arrived first stay put and
        // the tail of the sheet fights over what is left.
        var (hits, total) = SecondLapHits(FrameBitmapCache.EvictionOrder.MostRecent, 24);


        Assert.True(hits >= total / 3, $"scan eviction bought nothing: {hits}/{total}");
    }

    [AvaloniaFact]
    public void ScanEvictionIsBetterThanLruOnAScan()
    {
        var lru = SecondLapHits(FrameBitmapCache.EvictionOrder.LeastRecent, 24).Hits;
        var mru = SecondLapHits(FrameBitmapCache.EvictionOrder.MostRecent, 24).Hits;

        Assert.True(mru > lru, $"the two policies performed the same: {lru} vs {mru}");
    }

    [AvaloniaFact]
    public void TheFrameBeingShownIsNeverTheOneEvicted()
    {
        // Evicting what was just inserted would make the cache a no-op on
        // exactly the frame the artist is looking at.
        var previous = FrameBitmapCache.ByteBudget;
        FrameBitmapCache.ByteBudget = W * (long)H * 4 * 3;
        try
        {
            using var cache = new FrameBitmapCache { Eviction = FrameBitmapCache.EvictionOrder.MostRecent };
            var frames = Sheet(12);
            foreach (var f in frames) cache.Get(f, W, H);

            var bytes = cache.CachedBytes;
            var count = cache.CachedFrames;
            cache.Get(frames[^1], W, H);

            Assert.Equal(count, cache.CachedFrames);
            Assert.Equal(bytes, cache.CachedBytes);
        }
        finally
        {
            FrameBitmapCache.ByteBudget = previous;
        }
    }

    [AvaloniaFact]
    public void DrawingKeepsTheLruItWants()
    {
        // The default has to stay LRU: while drawing, the frames an artist
        // returns to are the ones they touched last, which is the opposite
        // prediction.
        using var cache = new FrameBitmapCache();
        Assert.Equal(FrameBitmapCache.EvictionOrder.LeastRecent, cache.Eviction);
    }
}

/// <summary>The policy has to actually change when playback starts.</summary>
[Collection("BrushState")]
public class PlaybackEvictionTests : BrushStateIsolated
{
    [AvaloniaFact]
    public void PlayingSwitchesTheCacheToScanEviction()
    {
        // Without this the policy exists and nothing ever selects it, which is
        // the shape of every dead setting in the ledger.
        var vm = new ViewModels.MainViewModel(null);
        Assert.Equal(FrameBitmapCache.EvictionOrder.LeastRecent, vm.FrameCacheEviction);

        vm.IsPlaying = true;
        Assert.Equal(FrameBitmapCache.EvictionOrder.MostRecent, vm.FrameCacheEviction);

        vm.IsPlaying = false;
        Assert.Equal(FrameBitmapCache.EvictionOrder.LeastRecent, vm.FrameCacheEviction);
    }
}
