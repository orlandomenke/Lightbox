using Lightbox.App.Rendering;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The composite cache, and mostly its lifetime rather than its LRU.
/// </summary>
/// <remarks>
/// <para>
/// <b>The LRU is the easy half.</b> What this has to get right is that an image
/// handed to a reader is never freed underneath it — B130's shape, an access
/// violation inside <c>sk_canvas_draw_image_rect</c> with no managed stack and
/// an empty log. So the assertions below check <see cref="SKObject.Handle"/>,
/// which a disposed native object zeroes: "still alive" is a fact here rather
/// than an absence of a crash.
/// </para>
/// <para>
/// Everything runs with <c>gpuBacked: false</c>, because a GPU image is routed
/// to <see cref="GpuImageReaper"/> instead of disposed and there is no graphics
/// context in this repository to make one. <see cref="AGpuImageGoesToTheReaper"/>
/// pins that routing, which is the part a test here can reach.
/// </para>
/// </remarks>
public class ComposeCacheTests(ITestOutputHelper output)
{
    private static SKImage Image(int side = 8)
    {
        var info = new SKImageInfo(side, side, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)!;
        surface.Canvas.Clear(SKColors.CornflowerBlue);
        return surface.Snapshot();
    }

    private static ComposeKey Key(int frame, int epoch = 0) =>
        new(new FrameFingerprint(frame, 1.0, SKRectI.Create(0, 0, 64, 48), null, 2), epoch);

    private static bool Alive(SKImage image) => image.Handle != IntPtr.Zero;

    [Fact]
    public void AnEmptyCacheMissesAndSaysSo()
    {
        var cache = new ComposeCache(1024 * 1024);

        Assert.Null(cache.Acquire(Key(0)));
        Assert.Equal(1, cache.Misses);
        Assert.Equal(0, cache.Hits);
    }

    [Fact]
    public void AStoredCompositeComesBack()
    {
        var cache = new ComposeCache(1024 * 1024);
        var image = Image();
        Assert.True(cache.Store(Key(0), image, 256, gpuBacked: false));

        var found = cache.Acquire(Key(0));

        Assert.Same(image, found);
        Assert.Equal(1, cache.Hits);
    }

    /// <summary>
    /// The whole point: a frame composed on lap one is still there on lap two,
    /// where the single-slot check B165 uses has long since been overwritten.
    /// </summary>
    [Fact]
    public void AFrameSurvivesALapOfTheLoop()
    {
        var cache = new ComposeCache(1024 * 1024);
        cache.Store(Key(0), Image(), 256, gpuBacked: false);
        for (var frame = 1; frame < 12; frame++) cache.Store(Key(frame), Image(), 256, gpuBacked: false);

        Assert.NotNull(cache.Acquire(Key(0)));
    }

    /// <summary>An edit is a new epoch, and a new epoch is a different composite.</summary>
    [Fact]
    public void ADrawingEditMissesTheCacheItWouldOtherwiseHit()
    {
        var cache = new ComposeCache(1024 * 1024);
        cache.Store(Key(3, epoch: 1), Image(), 256, gpuBacked: false);

        Assert.NotNull(cache.Acquire(Key(3, epoch: 1)));
        Assert.Null(cache.Acquire(Key(3, epoch: 2)));
    }

    [Fact]
    public void TheBudgetIsEnforced()
    {
        var cache = new ComposeCache(budgetBytes: 1000);
        for (var frame = 0; frame < 10; frame++)
        {
            var image = Image();
            cache.Store(Key(frame), image, 400, gpuBacked: false);
            cache.Release(image);
        }

        output.WriteLine($"{cache.Count} entries, {cache.CachedBytes} bytes, {cache.Evictions} evicted");
        Assert.True(cache.CachedBytes <= 1000, $"{cache.CachedBytes} bytes over a 1000 budget");
        Assert.True(cache.Evictions > 0);
    }

    /// <summary>The one just stored is never the one evicted, or a full cache is a slow no-op.</summary>
    [Fact]
    public void TheNewestEntrySurvivesATrimItCaused()
    {
        var cache = new ComposeCache(budgetBytes: 100);
        var first = Image();
        cache.Store(Key(0), first, 400, gpuBacked: false);
        cache.Release(first);
        var second = Image();
        cache.Store(Key(1), second, 400, gpuBacked: false);

        Assert.NotNull(cache.Acquire(Key(1)));
    }

    // ---- the lifetime, which is what this cache is actually about -------------

    /// <summary>
    /// Evicting while somebody is reading must remove the entry — the budget has
    /// to stay enforceable — and must not free the pixels.
    /// </summary>
    [Fact]
    public void AnEvictedCompositeIsNotFreedWhileItIsBeingRead()
    {
        var cache = new ComposeCache(budgetBytes: 500);
        var held = Image();
        cache.Store(Key(0), held, 400, gpuBacked: false);          // stored with one hold
        for (var frame = 1; frame < 6; frame++)  // push it out
        {
            var filler = Image();
            cache.Store(Key(frame), filler, 400, gpuBacked: false);
            cache.Release(filler);
        }

        Assert.Null(cache.Acquire(Key(0)));      // gone from the cache
        Assert.True(Alive(held), "the reader's image was freed underneath it — B130");
    }

    [Fact]
    public void TheLastReaderOfAnEvictedCompositeFreesIt()
    {
        var cache = new ComposeCache(budgetBytes: 500);
        var held = Image();
        cache.Store(Key(0), held, 400, gpuBacked: false);
        for (var frame = 1; frame < 6; frame++)
        {
            var filler = Image();
            cache.Store(Key(frame), filler, 400, gpuBacked: false);
            cache.Release(filler);
        }
        Assert.True(Alive(held));

        cache.Release(held);

        Assert.False(Alive(held), "an evicted composite nobody is reading should be freed");
    }

    /// <summary>
    /// Two live snapshots can hold one composite — the retirement queue keeps
    /// several. A flag rather than a count would free it on the first release.
    /// </summary>
    [Fact]
    public void TwoHoldsNeedTwoReleases()
    {
        var cache = new ComposeCache(budgetBytes: 500);
        var image = Image();
        cache.Store(Key(0), image, 400, gpuBacked: false);         // hold 1
        Assert.Same(image, cache.Acquire(Key(0)));  // hold 2
        cache.Clear();           // out of the cache, two readers

        cache.Release(image);
        Assert.True(Alive(image), "freed while a second reader still had it");

        cache.Release(image);
        Assert.False(Alive(image));
    }

    /// <summary>Releasing something still cached must not free it.</summary>
    [Fact]
    public void ReleasingAStillCachedCompositeLeavesItAlone()
    {
        var cache = new ComposeCache(1024 * 1024);
        var image = Image();
        cache.Store(Key(0), image, 256, gpuBacked: false);

        cache.Release(image);

        Assert.True(Alive(image));
        Assert.Same(image, cache.Acquire(Key(0)));
    }

    [Fact]
    public void ClearingLeavesALiveReadersImageAlone()
    {
        var cache = new ComposeCache(1024 * 1024);
        var image = Image();
        cache.Store(Key(0), image, 256, gpuBacked: false);

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.True(Alive(image), "clearing freed pixels a reader still had");
        cache.Release(image);
        Assert.False(Alive(image));
    }

    [Fact]
    public void ClearingFreesWhatNobodyIsReading()
    {
        var cache = new ComposeCache(1024 * 1024);
        var image = Image();
        cache.Store(Key(0), image, 256, gpuBacked: false);
        cache.Release(image);

        cache.Clear();

        Assert.False(Alive(image));
    }

    /// <summary>
    /// A duplicate key keeps what is cached and refuses the newcomer, so the
    /// caller knows it still owns the pixels it just made.
    /// </summary>
    [Fact]
    public void ADuplicateKeyIsRefusedRatherThanOrphaningTheEntry()
    {
        var cache = new ComposeCache(1024 * 1024);
        var first = Image();
        cache.Store(Key(0), first, 256, gpuBacked: false);
        var second = Image();

        Assert.False(cache.Store(Key(0), second, 256, gpuBacked: false));
        Assert.Same(first, cache.Acquire(Key(0)));
        second.Dispose();
    }

    /// <summary>
    /// B179: a GPU image released off the render thread is parked rather than
    /// freed, so it goes to the reaper instead of being disposed here.
    /// </summary>
    [Fact]
    public void AGpuImageGoesToTheReaper()
    {
        GpuImageReaper.ResetForTests();
        var cache = new ComposeCache(1024 * 1024);
        var image = Image();
        cache.Store(Key(0), image, 256, gpuBacked: true);
        cache.Clear();

        cache.Release(image);

        Assert.True(Alive(image), "a GPU image must not be disposed off the context's thread");
        Assert.True(GpuImageReaper.PendingCount > 0, "it should be queued for the draw op to free");
        GpuImageReaper.ResetForTests();
    }
}
