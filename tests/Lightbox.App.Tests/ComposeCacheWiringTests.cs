using Lightbox.App.Rendering;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The cache is actually invoked, and a borrowed composite is not freed by the
/// snapshot that borrowed it.
/// </summary>
/// <remarks>
/// <b>These exist because B125 stage 1 did not have them.</b> That stage shipped
/// a complete, tested pin protocol and nothing called it — <c>grep</c> for
/// <c>.Pin(</c> outside the cache returned nothing — and a tested mechanism
/// nobody invokes is indistinguishable from a working one right up until the
/// crash it exists to prevent. <c>ComposeCacheTests</c> covers the protocol;
/// this covers the wire.
/// </remarks>
public class ComposeCacheWiringTests(ITestOutputHelper output)
{
    private static SKImage Image(int side = 8)
    {
        var info = new SKImageInfo(side, side, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)!;
        surface.Canvas.Clear(SKColors.SeaGreen);
        return surface.Snapshot();
    }

    private static ComposeKey Key(int frame, int epoch = 0) =>
        new(new FrameFingerprint(frame, 1.0, SKRectI.Create(0, 0, 64, 48), null, 2), epoch);

    private static bool Alive(SKImage image) => image.Handle != IntPtr.Zero;

    /// <summary>
    /// A snapshot with the composite still to do, which is the only shape the
    /// cache can serve.
    /// </summary>
    /// <remarks>
    /// <b>Not a snapshot carrying an image, and the first version of these tests
    /// made exactly that mistake.</b> <see cref="RenderSnapshot.Materialise(GRContext?, LayerTextureCache?, ComposeCache?)"/>
    /// returns an image it already has before it ever reaches the cache — as it
    /// must; there is nothing left to compose — so a keyed snapshot built that
    /// way exercised none of this and quietly asserted against its own input.
    /// The publisher now refuses to key such a publish for the same reason.
    /// </remarks>
    private static RenderSnapshot Deferred(SKColor colour, ComposeKey? key)
    {
        var layer = new SKBitmap(new SKImageInfo(64, 48, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(layer)) canvas.Clear(colour);
        var work = new DeferredCompose(
            [new RenderPass(layer, null, 1.0)], SKColors.White, 1.0,
            new SKImageInfo(64, 48, SKColorType.Rgba8888, SKAlphaType.Premul),
            SKRectI.Create(0, 0, 64, 48));
        return new RenderSnapshot(null, 64, 48, deferred: work) { CacheKey = key };
    }

    /// <summary>
    /// A snapshot carrying a key hands its composite to the cache, and stops
    /// owning it — so disposing the snapshot must not free the pixels a later
    /// frame is going to reuse.
    /// </summary>
    [Fact]
    public void AKeyedSnapshotGivesItsCompositeToTheCacheAndStopsOwningIt()
    {
        var cache = new ComposeCache(1024 * 1024);
        var snapshot = Deferred(SKColors.SeaGreen, Key(0));
        var composed = snapshot.Materialise(null, null, cache);

        snapshot.Dispose();

        Assert.True(Alive(composed), "the snapshot freed a composite the cache now owns");
        Assert.Same(composed, cache.Acquire(Key(0)));
    }

    /// <summary>The second lap composes nothing, which is the whole feature.</summary>
    [Fact]
    public void TheSecondLapIsServedFromTheCache()
    {
        var cache = new ComposeCache(1024 * 1024);
        SKImage first;
        using (var lapOne = Deferred(SKColors.SeaGreen, Key(0)))
        {
            first = lapOne.Materialise(null, null, cache);
        }

        using var lapTwo = Deferred(SKColors.Crimson, Key(0));
        var served = lapTwo.Materialise(null, null, cache);

        Assert.Same(first, served);
        Assert.True(lapTwo.FromCache);
        output.WriteLine($"{cache.Hits} hit(s), {cache.Misses} miss(es)");
        Assert.Equal(1, cache.Hits);
    }

    /// <summary>
    /// A snapshot that borrowed must release rather than dispose, and the
    /// release must not free something still cached.
    /// </summary>
    [Fact]
    public void ABorrowingSnapshotReleasesRatherThanFrees()
    {
        var cache = new ComposeCache(1024 * 1024);
        SKImage first;
        using (var lapOne = Deferred(SKColors.SeaGreen, Key(0)))
        {
            first = lapOne.Materialise(null, null, cache);
        }

        var borrower = Deferred(SKColors.Crimson, Key(0));
        borrower.Materialise(null, null, cache);
        borrower.Dispose();

        Assert.True(Alive(first), "a borrowed composite was freed by its borrower");
        Assert.Same(first, cache.Acquire(Key(0)));
    }

    /// <summary>
    /// An unkeyed snapshot — every publish that is not a plain playback frame —
    /// keeps the behaviour it has always had: it owns its composite and frees it.
    /// </summary>
    [Fact]
    public void AnUnkeyedSnapshotStillOwnsAndFreesItsImage()
    {
        var cache = new ComposeCache(1024 * 1024);
        var snapshot = Deferred(SKColors.SeaGreen, key: null);
        var composed = snapshot.Materialise(null, null, cache);

        snapshot.Dispose();

        Assert.False(Alive(composed), "an unkeyed snapshot should still free its own composite");
        Assert.Equal(0, cache.Count);
    }

    /// <summary>With no cache at all — every caller but the canvas — nothing changes.</summary>
    [Fact]
    public void WithNoCacheASnapshotBehavesExactlyAsBefore()
    {
        var snapshot = Deferred(SKColors.SeaGreen, Key(0));
        var composed = snapshot.Materialise(null, null, null);

        snapshot.Dispose();

        Assert.False(Alive(composed));
    }

    /// <summary>
    /// The epoch is what makes a cached composite safe across a lap. A drawing
    /// edit bumps it, and the frame that would otherwise have hit must miss.
    /// </summary>
    [Fact]
    public void AnEditMakesTheSameFrameMiss()
    {
        var cache = new ComposeCache(1024 * 1024);
        using (var before = Deferred(SKColors.SeaGreen, Key(0, epoch: 4)))
        {
            before.Materialise(null, null, cache);
        }

        using var afterEdit = Deferred(SKColors.Crimson, Key(0, epoch: 5));
        afterEdit.Materialise(null, null, cache);

        Assert.False(afterEdit.FromCache, "a frame drawn on since must not be served from the cache");
    }

    /// <summary>
    /// The host clears on an edit, and a live reader keeps its pixels through
    /// it — the clear happens on the UI thread while the render thread may
    /// still be drawing the frame it was handed.
    /// </summary>
    [Fact]
    public void InvalidatingTheHostLeavesALiveFrameAlone()
    {
        ComposeCacheHost.ResetForTests();
        var snapshot = Deferred(SKColors.SeaGreen, Key(0));
        var composed = snapshot.Materialise(null, null, ComposeCacheHost.Shared);

        ComposeCacheHost.Invalidate();

        Assert.True(Alive(composed), "invalidating freed a frame the render thread still holds");
        Assert.Equal(0, ComposeCacheHost.Shared.Count);
        snapshot.Dispose();
        Assert.False(Alive(composed));
        ComposeCacheHost.ResetForTests();
    }
}
