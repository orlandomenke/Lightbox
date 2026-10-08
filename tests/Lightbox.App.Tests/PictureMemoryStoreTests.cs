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

    /// <summary>
    /// A cleared cache forgets when its pictures were used. A stamp that
    /// survived Clear would make a picture put back afterwards look old, and the
    /// broker would take it ahead of pictures genuinely older in other stores.
    /// </summary>
    [Fact]
    public void AClearedFlattenCacheForgetsWhenItsPicturesWereUsed()
    {
        using var cache = new TileFlattenCache();
        cache.Insert("a", 1, 0, Viewport, Bitmap());
        cache.Insert("b", 1, 0, Viewport, Bitmap());
        cache.Get("a", 1, 0, Viewport); // stamped now
        cache.Clear();
        Thread.Sleep(1);
        var afterClear = PictureMemory.Clock();

        cache.Insert("a", 1, 0, Viewport, Bitmap()); // oldest of the three
        cache.Insert("b", 1, 0, Viewport, Bitmap());
        cache.Insert("c", 1, 0, Viewport, Bitmap());

        Assert.True(((IPictureStore)cache).OldestEvictable >= afterClear);
    }

    [Fact]
    public void AClearedTileCacheForgetsWhenItsFramesWereUsed()
    {
        using var cache = new TileFrameCache();
        var frames = Enumerable.Range(0, 2).Select(Inked).ToList();
        cache.Get(frames[0], 320, 180);
        cache.Get(frames[1], 320, 180);
        cache.Get(frames[0], 320, 180); // stamped now
        cache.Clear();
        Thread.Sleep(1);
        var afterClear = PictureMemory.Clock();

        cache.Get(frames[0], 320, 180); // oldest of the two
        cache.Get(frames[1], 320, 180);

        Assert.True(((IPictureStore)cache).OldestEvictable >= afterClear);
    }

    /// <summary>
    /// A render-thread slice never cuts a cache below its own floor. Found on a
    /// CI runner with about 6 GB free: an eighth of its limit gave the compose
    /// cache 96 MB, under the 128 MB at which a loop stops fitting at all.
    /// </summary>
    [Fact]
    public void ASmallLimitSlicesTheRenderThreadCachesNoLowerThanTheirFloors()
    {
        var previousLimit = PictureMemory.Limit;
        try
        {
            PictureMemory.Limit = MemoryBudget.PicturesFloorBytes; // slice: 64 MB

            Assert.Equal(128L * 1024 * 1024, new ComposeCache(1024L * 1024 * 1024).BudgetBytes);
            Assert.Equal(1024L * 1024, new ComposeCache(1024L * 1024).BudgetBytes); // its own cap still holds
            using var textures = new LayerTextureCache();
            Assert.True(textures.BudgetBytes >= 64L * 1024 * 1024);
        }
        finally
        {
            PictureMemory.Limit = previousLimit;
        }
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

    private static long MachineCeiling => Math.Max(
        MemoryBudget.PicturesFloorBytes,
        Math.Min(MemoryBudget.PicturesCeilingBytes, MemoryBudget.Available / 2));

    /// <summary>
    /// The settings file is input (Q200's precedent): a hand-edited figure, or one
    /// carried from a bigger machine, is held to the range on the way in, not
    /// applied as written.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(-5)]
    [InlineData(int.MaxValue)]
    public void ASavedFigureOutsideTheRangeIsHeldToItOnLoad(int saved)
    {
        var previous = FrameBitmapCache.ByteBudget;
        var previousLimit = PictureMemory.Limit;
        try
        {
            new AppSettings { MemoryForPicturesMb = saved }.Save();

            _ = new ViewModels.MainViewModel(null);

            Assert.InRange(PictureMemory.Limit, MemoryBudget.PicturesFloorBytes, MachineCeiling);
            Assert.Equal(saved < 0 ? MemoryBudget.PicturesFloorBytes : MachineCeiling, PictureMemory.Limit);
        }
        finally
        {
            FrameBitmapCache.ByteBudget = previous;
            PictureMemory.Limit = previousLimit;
        }
    }

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
    /// The floor is where one 1080p frame's layers stop fitting. The ceiling is
    /// half the machine, at most 16 GB: past half, idle warming would fill what
    /// the rest of the computer needs.
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
            Assert.Equal(MachineCeiling / Mb, vm.MemoryForPicturesMb);

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
