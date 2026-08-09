using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// A cached frame bitmap somebody is still reading is not disposed when the
/// cache lets go of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gate on B125 stage 2, built before anything crosses a thread.</b>
/// Compositing is about to move off the UI thread: the view model will publish a
/// pass list and the canvas will composite from it on the render thread. Every
/// pass bitmap comes from <see cref="FrameBitmapCache"/>, whose eviction
/// disposes — so an eviction between publish and render would free pixels Skia
/// is about to read.
/// </para>
/// <para>
/// <b>That is not a hypothetical failure mode in this codebase.</b> B130 was the
/// same shape one level up: a snapshot disposed while the compositor still held
/// it, an access violation inside <c>sk_canvas_draw_image_rect</c>, and — because
/// a native crash bypasses the reporter's three managed channels — an empty log
/// and "Lightbox dies as soon as I touch anything". These tests exist so that
/// cannot arrive a second time by the route the GPU work opens.
/// </para>
/// <para>
/// <b>Reading <c>bmp.Handle</c> is how a disposed bitmap is detected</b>, because
/// the failure being guarded against is native. A disposed <c>SKBitmap</c> zeroes
/// its handle; touching the pixels of one is the access violation itself, which
/// would take the test host down rather than fail a test.
/// </para>
/// </remarks>
public class FrameBitmapPinningTests(ITestOutputHelper output)
{
    private static bool Alive(SKBitmap bmp) => bmp.Handle != nint.Zero;

    /// <summary>A frame with enough pixels that a small budget evicts it.</summary>
    private static SKBitmap Fill(FrameBitmapCache cache, Frame frame, int size = 64) =>
        cache.Get(frame, size, size);

    private static Frame NewFrame() => new();

    /// <summary>
    /// The case the whole protocol exists for: the cache gives up a bitmap that
    /// is still being read.
    /// </summary>
    [Fact]
    public void EvictingAPinnedBitmapDoesNotDisposeIt()
    {
        using var cache = new FrameBitmapCache();
        var frame = NewFrame();
        var bmp = Fill(cache, frame);

        cache.Pin(bmp);
        cache.Invalidate(frame.Id);   // the cache lets go

        output.WriteLine($"after eviction: alive={Alive(bmp)}, awaiting={cache.AwaitingUnpinBytes} bytes");

        Assert.True(Alive(bmp),
            "the bitmap was disposed while pinned — this is B130's native crash, "
            + "arriving by the route B125 stage 2 opens");
        Assert.True(cache.AwaitingUnpinBytes > 0, "the deferred bytes were not reported");
    }

    /// <summary>
    /// And it must actually be freed afterwards, or the protocol is a leak
    /// wearing a safety property.
    /// </summary>
    [Fact]
    public void UnpinningAfterEvictionDisposesIt()
    {
        using var cache = new FrameBitmapCache();
        var frame = NewFrame();
        var bmp = Fill(cache, frame);

        cache.Pin(bmp);
        cache.Invalidate(frame.Id);
        Assert.True(Alive(bmp));

        cache.Unpin(bmp);

        output.WriteLine($"after unpin: alive={Alive(bmp)}, awaiting={cache.AwaitingUnpinBytes} bytes");
        Assert.False(Alive(bmp), "the deferred disposal never happened — this leaks a frame per eviction");
        Assert.Equal(0, cache.AwaitingUnpinBytes);
        Assert.Equal(0, cache.PinnedCount);
    }

    /// <summary>
    /// Unpinning something the cache still holds must not free it. The pin is a
    /// stay of execution, not ownership.
    /// </summary>
    [Fact]
    public void UnpinningABitmapTheCacheStillHoldsLeavesItAlone()
    {
        using var cache = new FrameBitmapCache();
        var frame = NewFrame();
        var bmp = Fill(cache, frame);

        cache.Pin(bmp);
        cache.Unpin(bmp);

        Assert.True(Alive(bmp), "unpinning disposed a bitmap that is still in the cache");
        Assert.Equal(0, cache.AwaitingUnpinBytes);
    }

    /// <summary>
    /// <b>The case a boolean flag gets wrong, which is why the pin is counted.</b>
    /// One bitmap can be in two published pass lists at once — the same cel
    /// exposed on two layers, or a publish overlapping the one before it — and a
    /// flag would free it on the first release while the second reader was still
    /// going.
    /// </summary>
    [Fact]
    public void TwoReadersNeedTwoReleases()
    {
        using var cache = new FrameBitmapCache();
        var frame = NewFrame();
        var bmp = Fill(cache, frame);

        cache.Pin(bmp);
        cache.Pin(bmp);
        cache.Invalidate(frame.Id);

        cache.Unpin(bmp);
        Assert.True(Alive(bmp),
            "freed on the first release while a second reader still held it — "
            + "this is the bug a boolean pin would have");

        cache.Unpin(bmp);
        Assert.False(Alive(bmp), "still alive after the last release");
    }

    /// <summary>
    /// Closing a document tears the whole cache down at once, which is the more
    /// likely trigger than eviction and the same use-after-free.
    /// </summary>
    [Fact]
    public void ClearingTheCacheDefersAPinnedBitmapToo()
    {
        var cache = new FrameBitmapCache();
        var frame = NewFrame();
        var bmp = Fill(cache, frame);

        cache.Pin(bmp);
        cache.Clear();

        Assert.True(Alive(bmp), "a document close disposed a bitmap a render was still reading");

        cache.Unpin(bmp);
        Assert.False(Alive(bmp));
    }

    /// <summary>
    /// Unpinning something that was never pinned is a no-op rather than a throw:
    /// the release path will run in a finally on the render thread, and an
    /// exception there takes the compositor down with it.
    /// </summary>
    [Fact]
    public void UnpinningSomethingNeverPinnedIsHarmless()
    {
        using var cache = new FrameBitmapCache();
        var bmp = Fill(cache, NewFrame());

        cache.Unpin(bmp);

        Assert.True(Alive(bmp));
        Assert.Equal(0, cache.PinnedCount);
    }
}
