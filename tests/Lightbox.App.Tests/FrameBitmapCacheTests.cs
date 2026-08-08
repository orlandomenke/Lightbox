using Lightbox.App.Rendering;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// The frame cache is the biggest single consumer of memory in the app, and
/// "the scene is the world" — a canvas wide enough for a camera pan — makes
/// both of its defects reachable rather than theoretical.
/// </summary>
[Collection("FrameCacheBudget")]
public class FrameBitmapCacheTests : IDisposable
{
    private readonly long _budget = FrameBitmapCache.ByteBudget;

    public void Dispose() => FrameBitmapCache.ByteBudget = _budget;

    private static Frame FrameWithInk(int seed) => new()
    {
        Strokes =
        [
            new Stroke
            {
                Tool = ToolKind.Brush,
                Color = "#204080",
                Points = [new StrokePoint(10 + seed, 10, 1), new StrokePoint(40 + seed, 40, 1)],
                Brush = new BrushSettings { Size = 8, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
            },
        ],
    };

    [Fact]
    public void TheByteBudgetIsHonouredEvenWhenItCannotAffordTheFrameFloor()
    {
        // A frame here is 4 MB, and the budget only has room for two. The
        // floor used to win outright, so a wide scene sat at six frames —
        // 622 MB at 12000x2160 against a 512 MB budget — with no way down.
        FrameBitmapCache.ByteBudget = 9L * 1024 * 1024;
        using var cache = new FrameBitmapCache();

        for (var i = 0; i < 10; i++) cache.Get(FrameWithInk(i), 1000, 1000);

        Assert.True(cache.CachedBytes <= FrameBitmapCache.ByteBudget,
            $"cache held {cache.CachedBytes / (1024 * 1024)} MB against a " +
            $"{FrameBitmapCache.ByteBudget / (1024 * 1024)} MB budget");
        Assert.True(cache.CachedFrames >= 1, "the cache evicted everything");
    }

    [Fact]
    public void TheFrameFloorStillApplies_WhenTheBudgetCanAffordIt()
    {
        // The floor is a preference, not dead code: with room to spare, onion
        // skin's working set stays cached.
        FrameBitmapCache.ByteBudget = 512L * 1024 * 1024;
        using var cache = new FrameBitmapCache();

        for (var i = 0; i < 10; i++) cache.Get(FrameWithInk(i), 200, 200);

        Assert.True(cache.CachedFrames >= 6, $"only {cache.CachedFrames} frames cached");
    }

    [Fact]
    public void TheSameFrameAtTwoScalesIsCachedTwice_RatherThanThrashing()
    {
        using var cache = new FrameBitmapCache();
        var frame = FrameWithInk(0);

        var once = cache.Get(frame, 200, 200);
        var twice = cache.Get(frame, 200, 200, 2.0);

        Assert.Equal(200, once.Width);
        Assert.Equal(400, twice.Width);
        Assert.Equal(2, cache.CachedFrames);

        // And both survive: keyed on the id alone, an editing render at 1x and
        // an export render at 2x evicted each other on every single access.
        Assert.Same(once, cache.Get(frame, 200, 200));
        Assert.Same(twice, cache.Get(frame, 200, 200, 2.0));
        Assert.Equal(2, cache.CachedFrames);
    }

    [Fact]
    public void TheSameFrameAtTwoDocumentSizesIsAlsoCachedSeparately()
    {
        using var cache = new FrameBitmapCache();
        var frame = FrameWithInk(0);

        var small = cache.Get(frame, 200, 200);
        var large = cache.Get(frame, 400, 300);

        Assert.Equal(200, small.Width);
        Assert.Equal(400, large.Width);
        Assert.Same(small, cache.Get(frame, 200, 200));
    }

    [Fact]
    public void InvalidatingAFrameDropsEveryRenderOfIt()
    {
        // A frame cached at several sizes and scales is still one drawing. A
        // stroke on it makes all of them wrong, and an invalidation that
        // dropped only one would leave the others to be served as current.
        using var cache = new FrameBitmapCache();
        var frame = FrameWithInk(0);

        cache.Get(frame, 200, 200);
        cache.Get(frame, 200, 200, 2.0);
        cache.Get(frame, 400, 300);
        Assert.Equal(3, cache.CachedFrames);

        cache.Invalidate(frame.Id);
        Assert.Equal(0, cache.CachedFrames);
        Assert.Equal(0, cache.CachedBytes);
    }

    [Fact]
    public void InvalidatingOneFrameLeavesTheOthersAlone()
    {
        using var cache = new FrameBitmapCache();
        var a = FrameWithInk(0);
        var b = FrameWithInk(1);
        cache.Get(a, 200, 200);
        cache.Get(b, 200, 200);

        cache.Invalidate(a.Id);
        Assert.Equal(1, cache.CachedFrames);
        Assert.Same(cache.Get(b, 200, 200), cache.Get(b, 200, 200));
    }

    [Fact]
    public void CachedBytesTracksWhatIsActuallyHeld()
    {
        using var cache = new FrameBitmapCache();
        var frame = FrameWithInk(0);
        cache.Get(frame, 100, 100);
        Assert.Equal(100L * 100 * 4, cache.CachedBytes);

        cache.Get(frame, 100, 100, 2.0);
        Assert.Equal(100L * 100 * 4 + 200L * 200 * 4, cache.CachedBytes);

        cache.Clear();
        Assert.Equal(0, cache.CachedBytes);
    }
}
