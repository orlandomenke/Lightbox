using Lightbox.Raster;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The overall limit across picture stores (Q221, docs/DESIGN-memory-for-pictures.md):
/// the least recently used picture anywhere goes first, and nothing that may not go does.
/// </summary>
[Collection("PictureMemory")] // the broker is static
public sealed class PictureMemoryTests : IDisposable
{
    private readonly long _limit = PictureMemory.Limit;

    public void Dispose() => PictureMemory.Limit = _limit;

    /// <summary>A store of entries, each (last used, bytes), with some that may not go.</summary>
    private sealed class FakeStore : IPictureStore
    {
        public readonly List<(long Used, long Bytes, bool Pinned)> Entries = [];
        public long Bytes => Entries.Sum(e => e.Bytes);

        public long? OldestEvictable =>
            Entries.Where(e => !e.Pinned).Select(e => (long?)e.Used).DefaultIfEmpty(null).Min();

        public long EvictOldest()
        {
            var victim = Entries.Where(e => !e.Pinned).OrderBy(e => e.Used).Cast<(long, long, bool)?>().FirstOrDefault();
            if (victim is not { } v) return 0;
            Entries.Remove(v);
            return v.Item2;
        }
    }

    [Fact]
    public void TheOldestPictureAcrossStoresGoesFirstUntilUnderTheLimit()
    {
        var a = new FakeStore { Entries = { (10, 100, false), (40, 100, false) } };
        var b = new FakeStore { Entries = { (20, 100, false), (30, 100, false) } };
        PictureMemory.Register(a);
        PictureMemory.Register(b);
        try
        {
            var freed = PictureMemory.Enforce(limit: 250);

            Assert.Equal(200, freed); // 400 down to 200: the two oldest, 10 (a) then 20 (b)
            Assert.Equal([40L], a.Entries.Select(e => e.Used));
            Assert.Equal([30L], b.Entries.Select(e => e.Used));
        }
        finally
        {
            PictureMemory.Unregister(a);
            PictureMemory.Unregister(b);
        }
    }

    [Fact]
    public void APinnedPictureNeverGoesEvenWhenItIsTheOldest()
    {
        var a = new FakeStore { Entries = { (1, 100, true), (50, 100, false) } };
        PictureMemory.Register(a);
        try
        {
            PictureMemory.Enforce(limit: 100);

            Assert.Contains(a.Entries, e => e.Used == 1);
            Assert.DoesNotContain(a.Entries, e => e.Used == 50);
        }
        finally
        {
            PictureMemory.Unregister(a);
        }
    }

    [Fact]
    public void WhenNothingMoreMayGoItStopsRatherThanSpinning()
    {
        var a = new FakeStore { Entries = { (1, 500, true), (2, 500, true) } };
        PictureMemory.Register(a);
        try
        {
            Assert.Equal(0, PictureMemory.Enforce(limit: 10));
            Assert.Equal(2, a.Entries.Count);
        }
        finally
        {
            PictureMemory.Unregister(a);
        }
    }

    [Fact]
    public void UnderTheLimitNothingGoes()
    {
        var a = new FakeStore { Entries = { (1, 100, false) } };
        PictureMemory.Register(a);
        try
        {
            Assert.Equal(0, PictureMemory.Enforce(limit: 1000));
            Assert.Single(a.Entries);
        }
        finally
        {
            PictureMemory.Unregister(a);
        }
    }

    /// <summary>
    /// The render-thread caches cannot be evicted from here, so their two
    /// slices come off the top: the six caches together stay within the limit.
    /// </summary>
    [Fact]
    public void UnaskedTheBrokeredStoresKeepToTheLimitLessTheRenderSlices()
    {
        PictureMemory.Limit = 800;
        var a = new FakeStore();
        for (var i = 0; i < 8; i++) a.Entries.Add((i, 100, false));
        PictureMemory.Register(a);
        try
        {
            PictureMemory.Enforce();

            Assert.Equal(600, PictureMemory.Brokered);
            Assert.Equal(600, a.Bytes);
        }
        finally
        {
            PictureMemory.Unregister(a);
        }
    }

    [Fact]
    public void ByDefaultTheLimitIsAnEighthOfTheMachineWithinItsFloor()
    {
        var expected = Math.Clamp(MemoryBudget.Available / 8, MemoryBudget.PicturesFloorBytes, MemoryBudget.PicturesCeilingBytes);
        Assert.Equal(expected, MemoryBudget.Pictures());
    }
}

/// <summary>The still-image cache as a brokered store: its oldest unpinned frame, above its floor.</summary>
public sealed class FrameBitmapCacheStoreTests
{
    private static SkiaSharp.SKBitmap Small() =>
        new(new SkiaSharp.SKImageInfo(4, 4, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul));

    [Fact]
    public void TheOldestUnpinnedFrameIsOfferedAndEvicted()
    {
        var cache = new FrameBitmapCache();
        var frames = Enumerable.Range(0, 10).Select(i => new Lightbox.Core.Documents.Frame { Id = $"f{i}" }).ToList();
        foreach (var f in frames)
        {
            Assert.True(cache.InsertWanted(f, 4, 4, 1.0, 0, Small())); // most recent first: f9 newest, f0 oldest
            Thread.Sleep(1);
        }
        IPictureStore store = cache;
        cache.Get(frames[0], 4, 4);            // touching f0 makes f1 the oldest
        cache.Pin(cache.Get(frames[1], 4, 4)); // touching and pinning f1 makes f2 the oldest that may go
        Assert.NotNull(store.OldestEvictable);

        var before = store.Bytes;
        var freed = store.EvictOldest();

        Assert.Equal(4 * 4 * 4, freed);
        Assert.Equal(before - freed, store.Bytes);
        Assert.False(cache.Holds(frames[2], 4, 4, 1.0, 0));
        Assert.True(cache.Holds(frames[0], 4, 4, 1.0, 0));
    }

    [Fact]
    public void AtItsFloorNothingIsOffered()
    {
        var cache = new FrameBitmapCache();
        for (var i = 0; i < 6; i++) cache.InsertWanted(new Lightbox.Core.Documents.Frame { Id = $"f{i}" }, 4, 4, 1.0, 0, Small());
        IPictureStore store = cache;
        Assert.Null(store.OldestEvictable);
        Assert.Equal(0, store.EvictOldest());
    }
}
