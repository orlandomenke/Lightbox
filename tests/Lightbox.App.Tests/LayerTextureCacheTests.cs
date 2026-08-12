using Lightbox.App.Rendering;
using Lightbox.Raster;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Resident layer textures: the key, the invalidation, the budget, the disposal.
/// </summary>
/// <remarks>
/// <para>
/// <b>What these can and cannot check, stated rather than implied.</b> There is
/// no graphics context in this repository, so the upload itself cannot run — the
/// same reason B122 shipped as an inference and the render report exists at all.
/// The upload is therefore behind an injectable delegate, and these exercise
/// everything around it: whether an entry is found, when it is thrown away, what
/// happens when the driver refuses, and whether anything leaks.
/// </para>
/// <para>
/// <b>That is not a weaker test than it looks.</b> Every way this cache can be
/// *wrong* rather than merely *slow* is in the parts covered here: a key that
/// misses a content change shows stale art, and a key that never invalidates
/// leaks a texture per stroke. Uploading is the part that either works on the
/// hardware or does not, and no cleverness here decides that.
/// </para>
/// </remarks>
public class LayerTextureCacheTests(ITestOutputHelper output)
{
    /// <summary>A stand-in for a texture, counting how many were made.</summary>
    private sealed class Counting
    {
        public int Uploads;
        public readonly List<SKImage> Made = [];

        public SKImage? Upload(GRContext gpu, SKBitmap bitmap)
        {
            Uploads++;
            var image = SKImage.FromBitmap(bitmap)!;
            Made.Add(image);
            return image;
        }

        public int Disposed => Made.Count(i => i.Handle == nint.Zero);
    }

    private static SKBitmap Bitmap(int size = 64)
    {
        var bmp = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Green);
        return bmp;
    }

    /// <summary>
    /// The point of the whole thing: a layer showing the same drawing as last
    /// frame is not uploaded again. During playback this is every held layer and
    /// every layer on a hold — 26% to 59% of layer draws, per B165's measurement.
    /// </summary>
    [Fact]
    public void TheSameBitmapIsUploadedOnce()
    {
        var counting = new Counting();
        using var cache = new LayerTextureCache(counting.Upload);
        var bmp = Bitmap();

        for (var i = 0; i < 10; i++) cache.Resident(null!, bmp);

        output.WriteLine($"{counting.Uploads} uploads for 10 draws, {cache.Hits} hits");
        Assert.Equal(1, counting.Uploads);
        Assert.Equal(9, cache.Hits);
    }

    /// <summary>
    /// <b>The trap identity alone falls into, and the one that shows stale art.</b>
    /// Committing a stroke does not replace a cached layer bitmap —
    /// <c>FrameRasterizer.Append</c> stamps into it in place (invariant 6) — so an
    /// instance survives its pixels changing. Keying on the instance alone would
    /// serve the pre-stroke texture forever, and the artist would watch their mark
    /// fail to appear.
    /// </summary>
    [Fact]
    public void AnInPlaceEditInvalidatesTheTexture()
    {
        var counting = new Counting();
        using var cache = new LayerTextureCache(counting.Upload);
        var bmp = Bitmap();

        cache.Resident(null!, bmp);
        BitmapVersion.Bump(bmp);          // the mark lands, in place
        cache.Resident(null!, bmp);

        output.WriteLine($"{counting.Uploads} uploads across one in-place edit");
        Assert.Equal(2, counting.Uploads);
    }

    /// <summary>
    /// And the stale one is freed rather than kept. An in-place edit happens per
    /// stroke commit, so keeping every version a bitmap ever had would leak a
    /// texture per stroke — which on a 4K layer is 33 MB a mark.
    /// </summary>
    [Fact]
    public void TheSupersededTextureIsFreedRatherThanHeld()
    {
        var counting = new Counting();
        using var cache = new LayerTextureCache(counting.Upload);
        var bmp = Bitmap();

        cache.Resident(null!, bmp);
        var afterFirst = cache.ResidentBytes;
        BitmapVersion.Bump(bmp);
        cache.Resident(null!, bmp);

        output.WriteLine($"{cache.Count} resident, {cache.ResidentBytes} bytes, {counting.Disposed} freed");
        Assert.Equal(1, cache.Count);
        Assert.Equal(afterFirst, cache.ResidentBytes);
        Assert.Equal(1, counting.Disposed);
    }

    /// <summary>
    /// Two different drawings are two textures — the ordinary playback case for a
    /// layer that is actually animating.
    /// </summary>
    [Fact]
    public void DifferentBitmapsGetDifferentTextures()
    {
        var counting = new Counting();
        using var cache = new LayerTextureCache(counting.Upload);

        cache.Resident(null!, Bitmap());
        cache.Resident(null!, Bitmap());

        Assert.Equal(2, counting.Uploads);
        Assert.Equal(2, cache.Count);
    }

    /// <summary>
    /// <b>The budget is enforced, and the least recently used goes first.</b> VRAM
    /// is scarcer than RAM and on integrated graphics it is the memory the CPU is
    /// already competing for, so an unbounded texture cache is not a slow frame —
    /// it is a driver refusing the next allocation.
    /// </summary>
    [Fact]
    public void TheOldestTextureIsEvictedWhenTheBudgetIsExceeded()
    {
        var counting = new Counting();
        using var cache = new LayerTextureCache(counting.Upload);
        var one = Bitmap();
        var two = Bitmap();
        var three = Bitmap();
        cache.BudgetBytes = one.Info.BytesSize * 2L;

        cache.Resident(null!, one);
        cache.Resident(null!, two);
        cache.Resident(null!, one);     // one is now the more recently used
        cache.Resident(null!, three);   // over budget: two should go

        output.WriteLine($"{cache.Count} resident after 3 uploads under a 2-texture budget, "
            + $"{cache.Evictions} evictions");
        Assert.Equal(2, cache.Count);
        Assert.Equal(1, cache.Evictions);
        Assert.True(cache.ResidentBytes <= cache.BudgetBytes);

        // The one that survived is the one that was used most recently.
        counting.Uploads = 0;
        cache.Resident(null!, one);
        Assert.Equal(0, counting.Uploads);
    }

    /// <summary>
    /// <b>A refused upload is a fallback, not a crash.</b> The driver may decline
    /// — out of memory, a lost context, a format it will not take — and the caller
    /// then draws the CPU bitmap, which is exactly today's behaviour. Throwing
    /// here would take the compositor down over an optimisation.
    /// </summary>
    [Fact]
    public void ARefusedUploadFallsBackRatherThanThrowing()
    {
        using var cache = new LayerTextureCache((_, _) => null);

        var texture = cache.Resident(null!, Bitmap());

        Assert.Null(texture);
        Assert.Equal(1, cache.FailedUploads);
        Assert.Equal(0, cache.Count);
    }

    /// <summary>
    /// A refusal must not be remembered as a hit — otherwise one transient
    /// failure would permanently pin a layer to the CPU path.
    /// </summary>
    [Fact]
    public void ARefusalIsRetriedRatherThanCached()
    {
        var refuse = true;
        var uploads = 0;
        using var cache = new LayerTextureCache((_, bmp) =>
        {
            uploads++;
            return refuse ? null : SKImage.FromBitmap(bmp);
        });
        var bmp = Bitmap();

        cache.Resident(null!, bmp);
        refuse = false;
        var second = cache.Resident(null!, bmp);

        Assert.Equal(2, uploads);
        Assert.NotNull(second);
    }

    /// <summary>
    /// A lost or replaced context invalidates every handle at once, so the cache
    /// has to be droppable wholesale — and drop the textures rather than leak
    /// them.
    /// </summary>
    [Fact]
    public void ClearingFreesEverything()
    {
        var counting = new Counting();
        var cache = new LayerTextureCache(counting.Upload);
        cache.Resident(null!, Bitmap());
        cache.Resident(null!, Bitmap());

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.ResidentBytes);
        Assert.Equal(2, counting.Disposed);
    }

    /// <summary>
    /// Disposal is the same, since the canvas owns one for its lifetime.
    /// </summary>
    [Fact]
    public void DisposingFreesEverything()
    {
        var counting = new Counting();
        var cache = new LayerTextureCache(counting.Upload);
        cache.Resident(null!, Bitmap());

        cache.Dispose();

        Assert.Equal(1, counting.Disposed);
    }
}
