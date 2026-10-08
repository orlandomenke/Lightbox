using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.Raster.Tests;

/// <summary>
/// B408: a warm the screen wants now goes in even when the cache is full; a
/// guess still does not.
/// </summary>
public class WantedInsertTests
{
    private static SKBitmap Small() => new(new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul));

    private static FrameBitmapCache Full()
    {
        var cache = new FrameBitmapCache();
        for (var i = 0; i < FrameBitmapCache.MaxEntries; i++)
        {
            Assert.True(cache.InsertWarm(new Frame { Id = $"held{i}" }, 4, 4, 1.0, 0, Small()));
        }
        return cache;
    }

    [Fact]
    public void AGuessIsRefusedByAFullCache()
    {
        var cache = Full();
        using var bmp = Small();
        Assert.False(cache.InsertWarm(new Frame { Id = "guess" }, 4, 4, 1.0, 0, bmp));
    }

    [Fact]
    public void AWantedFrameGoesInAndTheCacheStaysWithinItsCap()
    {
        var cache = Full();
        var wanted = new Frame { Id = "wanted" };

        Assert.True(cache.InsertWanted(wanted, 4, 4, 1.0, 0, Small()));

        Assert.True(cache.Holds(wanted, 4, 4, 1.0, 0));
        Assert.Equal(FrameBitmapCache.MaxEntries, cache.CachedFrames);
    }
}
