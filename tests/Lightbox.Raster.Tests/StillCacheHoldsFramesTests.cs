using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The still cache is bounded by its byte budget, not by a count of pictures
/// that dates from when one entry was one frame.
/// </summary>
/// <remarks>
/// <para>
/// The cap of 96 entries was set when the cache held whole frames (M15c). It
/// holds one picture per drawing per layer now, so on the lab's 30-layer
/// document 96 entries were three frames, while the byte budget on the
/// machine it ran on (an eighth of 31 GB) had room for sixteen. Flipping back
/// and forth across five keys re-rendered every drawing it had held a moment
/// before: 1.2 s a flip on average (the lab, 2026-10-10).
/// </para>
/// </remarks>
[Collection("FrameCacheBudget")]
public sealed class StillCacheHoldsFramesTests
{
    private const int W = 64, H = 64;
    private const int Layers = 30;

    private static List<Frame> Drawings(int frame) =>
        Enumerable.Range(0, Layers).Select(i => new Frame
        {
            Id = $"f{frame}-l{i}",
            Strokes =
            [
                new Stroke
                {
                    Color = "#202020",
                    Brush = new BrushSettings { Size = 6, Hardness = 1, Opacity = 1, Flow = 1 },
                    Points = [new StrokePoint(4 + i, 8 + frame, 1), new StrokePoint(60 - i, 56, 1)],
                },
            ],
        }).ToList();

    private static void Publish(FrameBitmapCache cache, List<Frame> frame)
    {
        using var publish = cache.Publishing(frame.Select(f => (f, 0)), W, H);
        foreach (var f in frame) cache.Get(f, W, H);
    }

    /// <summary>
    /// Six frames of thirty layers, flipped through and back, with a budget
    /// that has room for all of them: the way back renders nothing.
    /// </summary>
    [Fact]
    public void FlippingBackWithinTheBudgetRendersNothing()
    {
        var before = FrameBitmapCache.ByteBudget;
        try
        {
            FrameBitmapCache.ByteBudget = (long)W * H * 4 * Layers * 8; // room for eight frames
            var cache = new FrameBitmapCache();
            var frames = Enumerable.Range(0, 6).Select(Drawings).ToList();

            foreach (var f in frames) Publish(cache, f);
            var rendered = cache.Misses;
            for (var i = frames.Count - 1; i >= 0; i--) Publish(cache, frames[i]);

            Assert.Equal(6 * Layers, cache.CachedFrames);
            Assert.Equal(rendered, cache.Misses);
        }
        finally
        {
            FrameBitmapCache.ByteBudget = before;
        }
    }

    /// <summary>
    /// Room is made before a batch renders, not after it arrives: the batch is
    /// sized by the budget, so without this the old contents and the whole batch
    /// were held at once — twice the budget (the leak review). The frame on
    /// screen stays.
    /// </summary>
    [Fact]
    public void RoomIsMadeBeforeABatchArrivesAndTheFrameOnScreenStays()
    {
        var before = FrameBitmapCache.ByteBudget;
        try
        {
            var perStill = (long)W * H * 4;
            FrameBitmapCache.ByteBudget = perStill * Layers * 4; // room for four frames
            var cache = new FrameBitmapCache();
            var frames = Enumerable.Range(0, 4).Select(Drawings).ToList();
            foreach (var f in frames) Publish(cache, f);
            Assert.Equal(FrameBitmapCache.ByteBudget, cache.CachedBytes);

            // The last frame is on screen; a batch of two frames' worth is coming.
            using (cache.Publishing(frames[^1].Select(f => (f, 0)), W, H))
            {
                cache.MakeRoom(perStill * Layers * 2);

                Assert.True(cache.CachedBytes + perStill * Layers * 2 <= FrameBitmapCache.ByteBudget,
                    $"{cache.CachedBytes} held with {perStill * Layers * 2} coming, against {FrameBitmapCache.ByteBudget}");
                Assert.All(frames[^1], f => Assert.True(cache.Holds(f, W, H, 1.0, 0), $"{f.Id} on screen was evicted"));
            }
        }
        finally
        {
            FrameBitmapCache.ByteBudget = before;
        }
    }

    /// <summary>The budget still binds: past it, the oldest frames go.</summary>
    [Fact]
    public void PastTheBudgetTheOldestGo()
    {
        var before = FrameBitmapCache.ByteBudget;
        try
        {
            FrameBitmapCache.ByteBudget = (long)W * H * 4 * Layers * 4; // room for four frames
            var cache = new FrameBitmapCache();
            for (var f = 0; f < 6; f++) Publish(cache, Drawings(f));

            Assert.True(cache.CachedBytes <= FrameBitmapCache.ByteBudget,
                $"{cache.CachedBytes} bytes held against a budget of {FrameBitmapCache.ByteBudget}");
            Assert.True(cache.CachedFrames >= 3 * Layers, $"only {cache.CachedFrames} pictures held");
        }
        finally
        {
            FrameBitmapCache.ByteBudget = before;
        }
    }
}
