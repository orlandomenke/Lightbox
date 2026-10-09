using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The still cache never evicts what the frame on screen is using. On a large
/// document at 4K one frame's drawings outgrew the byte budget (30 layers and
/// their onion ghosts, ~3 GB), so every publish evicted what it had just made
/// and rendered it again: the lab's flip test spent 204 s of UI time on 542
/// renders of 203 drawings (2026-10-09).
/// </summary>
[Collection("FrameCacheBudget")]
public sealed class StillWorkingSetTests
{
    private const int W = 64, H = 64;

    private static List<Frame> Drawings(int n) =>
        Enumerable.Range(0, n).Select(i => new Frame
        {
            Strokes =
            [
                new Stroke
                {
                    Color = "#202020",
                    Brush = new BrushSettings { Size = 6, Hardness = 1, Opacity = 1, Flow = 1 },
                    Points = [new StrokePoint(4 + i, 8, 1), new StrokePoint(60 - i, 56, 1)],
                },
            ],
        }).ToList();

    private static void Publish(FrameBitmapCache cache, IEnumerable<Frame> frame)
    {
        using var publish = cache.Publishing(frame.Select(f => (f, 0)), W, H);
        foreach (var f in frame) cache.Get(f, W, H);
    }

    /// <summary>
    /// Only the most recent publish is protected, so the overshoot is one
    /// frame's worth. Protecting the one before as well let playback, where
    /// every tick is a publish of a different frame, hold two frames past the
    /// budget: about 6 GB on a 30-layer 4K document with ghosts, more than the
    /// 8 GB minimum spec can spare (the leak review, 2026-10-09).
    /// </summary>
    [Fact]
    public void OnlyTheFrameOnScreenIsHeldPastTheBudget()
    {
        var before = FrameBitmapCache.ByteBudget;
        try
        {
            FrameBitmapCache.ByteBudget = (long)W * H * 4 * 8; // room for 8 of a frame's 12
            var cache = new FrameBitmapCache();

            Publish(cache, Drawings(12));
            Publish(cache, Drawings(12));

            Assert.Equal(12, cache.CachedFrames);
        }
        finally
        {
            FrameBitmapCache.ByteBudget = before;
        }
    }

    /// <summary>
    /// A republish with one changed drawing renders that drawing, not the frame.
    /// With only the newest publish held, the one miss evicted the stills this
    /// publish had not fetched yet, each of those then missed and evicted the
    /// next, and an over-budget frame re-rendered nearly whole (the leak review).
    /// </summary>
    [Fact]
    public void ARepublishWithOneChangedDrawingRendersOnlyThatOne()
    {
        var before = FrameBitmapCache.ByteBudget;
        try
        {
            FrameBitmapCache.ByteBudget = (long)W * H * 4 * 8;
            var cache = new FrameBitmapCache();
            var frame = Drawings(12);
            Publish(cache, frame);

            frame[0] = Drawings(13)[12]; // the first layer's drawing, edited
            var misses = cache.Misses;
            Publish(cache, frame);

            Assert.Equal(misses + 1, cache.Misses);
        }
        finally
        {
            FrameBitmapCache.ByteBudget = before;
        }
    }

    /// <summary>A publish inside a publish does not end the outer one's protection.</summary>
    [Fact]
    public void ANestedPublishLeavesTheOuterOneProtecting()
    {
        var before = FrameBitmapCache.ByteBudget;
        try
        {
            FrameBitmapCache.ByteBudget = (long)W * H * 4 * 8;
            var cache = new FrameBitmapCache();
            var frame = Drawings(12);

            using (cache.Publishing())
            {
                using (cache.Publishing()) cache.Get(frame[0], W, H);
                foreach (var f in frame.Skip(1)) cache.Get(f, W, H);
            }
            var misses = cache.Misses;
            Publish(cache, frame);

            Assert.Equal(misses, cache.Misses);
        }
        finally
        {
            FrameBitmapCache.ByteBudget = before;
        }
    }

    [Fact]
    public void AFrameThatOutgrowsTheBudgetIsNotRenderedAgainOnTheNextPublish()
    {
        var before = FrameBitmapCache.ByteBudget;
        try
        {
            FrameBitmapCache.ByteBudget = (long)W * H * 4 * 8; // room for 8 of the frame's 12
            var cache = new FrameBitmapCache();
            var frame = Drawings(12);

            Publish(cache, frame);
            var misses = cache.Misses;
            Publish(cache, frame);
            Publish(cache, frame);

            Assert.Equal(12, misses);
            Assert.Equal(misses, cache.Misses);
        }
        finally
        {
            FrameBitmapCache.ByteBudget = before;
        }
    }

    /// <summary>
    /// Stills rendered off the UI thread for this publish and taken in with
    /// <see cref="FrameBitmapCache.InsertWanted"/> are the frame on screen too:
    /// the publish's own fetches must not evict them to make room for each other.
    /// </summary>
    [Fact]
    public void StillsTakenInForThisPublishAreTheFrameOnScreen()
    {
        var before = FrameBitmapCache.ByteBudget;
        try
        {
            FrameBitmapCache.ByteBudget = (long)W * H * 4 * 8; // room for 8 of the frame's 12
            var cache = new FrameBitmapCache();
            var frame = Drawings(12);

            cache.BeginPublish();
            foreach (var f in frame) cache.InsertWanted(f, W, H, 1.0, 0, FrameBitmapCache.RenderDetached(f, W, H));
            var misses = cache.Misses;
            foreach (var f in frame) cache.Get(f, W, H);

            Assert.Equal(misses, cache.Misses);
        }
        finally
        {
            FrameBitmapCache.ByteBudget = before;
        }
    }

    /// <summary>
    /// The overshoot is one frame's worth, not a leak: once the playhead has
    /// moved on, the old frame's drawings go back under the budget.
    /// </summary>
    [Fact]
    public void AFrameLeftBehindGoesBackUnderTheBudget()
    {
        var before = FrameBitmapCache.ByteBudget;
        try
        {
            var one = (long)W * H * 4;
            FrameBitmapCache.ByteBudget = one * 8;
            var cache = new FrameBitmapCache();
            var here = Drawings(12);
            var there = Drawings(12);

            Publish(cache, here);
            Publish(cache, there);
            Publish(cache, there);

            Assert.True(cache.CachedBytes <= one * 12, $"{cache.CachedBytes / one} drawings held after leaving a frame");
            Assert.False(cache.Holds(here[0], W, H, 1.0, 0));
        }
        finally
        {
            FrameBitmapCache.ByteBudget = before;
        }
    }
}
