using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The overall limit for pictures (Q221, docs/DESIGN-memory-for-pictures.md):
/// each brokered store offers its least recently used picture, never the one it
/// must keep, and the setting is saved only when the artist sets it.
/// </summary>
[Collection("FrameCacheBudget")]
public class PictureMemoryStoreTests
{
    private static SKBitmap Bitmap() =>
        new(new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul));

    private static readonly SKRectI Viewport = SKRectI.Create(0, 0, 64, 64);

    private static Frame Inked(int i)
    {
        var f = new Frame();
        f.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#303030",
            Points = [new StrokePoint(10 + i, 20, 1), new StrokePoint(300 + i, 160, 1)],
            Brush = new BrushSettings { Size = 24, Hardness = 0.8, Opacity = 1, Flow = 1 },
        });
        return f;
    }

    [Fact]
    public void AFlattenOffersItsOldestUnpinnedAndKeepsTheNewest()
    {
        using var cache = new TileFlattenCache();
        var oldest = Bitmap();
        var middle = Bitmap();
        var newest = Bitmap();
        cache.Insert("a", 1, 0, Viewport, oldest);
        cache.Insert("b", 1, 0, Viewport, middle);
        cache.Insert("c", 1, 0, Viewport, newest);
        cache.Pin(oldest);
        IPictureStore store = cache;

        var freed = store.EvictOldest();

        Assert.Equal(64 * 64 * 4, freed);
        // Only the newest left unpinned, and the newest is never offered.
        Assert.Equal(0, store.EvictOldest());
        Assert.Null(store.OldestEvictable);
        // Read last: a Get makes what it reads the newest.
        Assert.Same(oldest, cache.Get("a", 1, 0, Viewport)); // pinned: stays though oldest
        Assert.Null(cache.Get("b", 1, 0, Viewport));         // the oldest that may go
        cache.Unpin(oldest);
    }

    [Fact]
    public void TileFramesOfferTheLeastRecentlyUsedAndKeepOne()
    {
        using var cache = new TileFrameCache();
        var frames = Enumerable.Range(0, 3).Select(Inked).ToList();
        foreach (var f in frames)
        {
            cache.Get(f, 320, 180);
            Thread.Sleep(1);
        }
        cache.Get(frames[0], 320, 180); // touched: frames[1] is now the oldest
        IPictureStore store = cache;

        Assert.True(store.EvictOldest() > 0);
        Assert.False(cache.Holds(frames[1].Id));
        Assert.True(cache.Holds(frames[0].Id));

        Assert.True(store.EvictOldest() > 0);
        Assert.Equal(1, cache.CachedFrames);
        Assert.Null(store.OldestEvictable);
        Assert.Equal(0, store.EvictOldest());
        Assert.Equal(cache.AllocatedBytes, store.Bytes);
    }

    [Fact]
    public void UndoPixelsGoOldestStepFirst()
    {
        var stills = new FrameBitmapCache();
        var frame = Inked(0);
        stills.InsertWanted(frame, 64, 64, 1.0, 0, Bitmap());
        using var snaps = new MarkSnapshot();
        snaps.Hold(stills, frame, SKRectI.Create(0, 0, 16, 16));
        snaps.Promote(1);
        Thread.Sleep(1);
        snaps.Hold(stills, frame, SKRectI.Create(0, 0, 8, 8));
        snaps.Promote(2);
        IPictureStore store = snaps;

        var freed = store.EvictOldest();

        Assert.Equal(16 * 16 * 4, freed); // step 1's patch, not step 2's
        Assert.Equal(1, snaps.Steps);
        Assert.Equal(8 * 8 * 4, store.Bytes);
    }

    /// <summary>Absent unless set: unset follows whichever machine opens the file.</summary>
    [Fact]
    public void TheLimitIsSavedOnlyWhenSet()
    {
        Assert.DoesNotContain("\"MemoryForPicturesMb\"", new AppSettings().Serialize());

        var set = new AppSettings { MemoryForPicturesMb = 2048 };
        Assert.Equal(2048, AppSettings.Deserialize(set.Serialize()).MemoryForPicturesMb);
    }
}

/// <summary>
/// The setting itself (Q221). Saving writes the settings file, so these run
/// with the profile redirected, as every test that saves does.
/// </summary>
[Collection("BrushState")]
public class MemoryForPicturesSettingTests : BrushStateIsolated
{
    private const long Mb = 1024 * 1024;

    [AvaloniaFact]
    public void SettingTheLimitAppliesItSavesItAndTheStillCacheMayFillIt()
    {
        var vm = new ViewModels.MainViewModel(null);
        var previous = FrameBitmapCache.ByteBudget;
        var previousLimit = PictureMemory.Limit;
        try
        {
            vm.MemoryForPicturesMb = 1024;

            Assert.Equal(1024 * Mb, PictureMemory.Limit);
            Assert.Equal(PictureMemory.Brokered, FrameBitmapCache.ByteBudget);
            Assert.Equal(PictureMemory.Limit, PictureMemory.Brokered + 2 * PictureMemory.RenderSlice);
            Assert.Equal(1024, AppSettings.Load().MemoryForPicturesMb);
        }
        finally
        {
            FrameBitmapCache.ByteBudget = previous;
            PictureMemory.Limit = previousLimit;
        }
    }

    /// <summary>
    /// The floor is where one 1080p frame's layers stop fitting; the ceiling is
    /// the derivation's, because past it nothing would spend the bytes.
    /// </summary>
    [AvaloniaFact]
    public void TheSettingIsHeldBetweenTheDerivationsFloorAndCeiling()
    {
        var vm = new ViewModels.MainViewModel(null);
        var previous = FrameBitmapCache.ByteBudget;
        var previousLimit = PictureMemory.Limit;
        try
        {
            vm.MemoryForPicturesMb = int.MaxValue;
            Assert.Equal(MemoryBudget.PicturesCeilingBytes / Mb, vm.MemoryForPicturesMb);

            vm.MemoryForPicturesMb = 1;
            Assert.Equal(MemoryBudget.PicturesFloorBytes / Mb, vm.MemoryForPicturesMb);
            Assert.Equal(PictureMemory.Brokered, FrameBitmapCache.ByteBudget);
        }
        finally
        {
            FrameBitmapCache.ByteBudget = previous;
            PictureMemory.Limit = previousLimit;
        }
    }
}
