using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The frames the next stroke will need are rendered during the idle, off the
/// UI thread (B332).
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is a fix for.</b> A frame-cache miss renders the whole frame
/// synchronously on the calling thread — measured at <b>797 ms</b> on a lighter
/// frame than the owner's — and mid-stroke that thread is the UI thread. The
/// capture of 2026-08-28 00:50 shows the cost in the form the artist feels:
/// two strokes read <c>following: never</c>, the first of the session and the
/// first after a thirteen-second pause, both beside the session's single
/// 1131 ms build with two misses in it. Every other stroke began following in
/// 28-39 ms. <b>The freeze does not interrupt a stroke; it eats the first one
/// after an idle.</b>
/// </para>
/// <para>
/// <b>Why warming is the only shape that fits.</b> Two earlier attempts on this
/// entry failed on the same point: a miss during a stroke has no window to be
/// warmed in, because the next publish asks immediately. Serving the last good
/// render instead was built, measured and reverted — it avoided one stall in a
/// session. The idle is the one moment with a window, and filling it has no
/// visible trade at all: nothing stale is shown, because nothing is shown until
/// the pixels exist.
/// </para>
/// </remarks>
public class WarmWhileIdleTests
{
    private static Scene SceneWithArt()
    {
        var scene = new Scene { Width = 320, Height = 240, FrameCount = 1 };
        var layer = new Layer { Name = "art" };
        var frame = new Frame();
        frame.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#203040",
            Brush = new BrushSettings { Size = 12, Hardness = 0.8, Flow = 1, Opacity = 1 },
            Points = [new StrokePoint(20, 20, 1), new StrokePoint(200, 180, 1)],
        });
        layer.Cels.Add(new Cel { Frame = frame });
        scene.Layers.Add(layer);
        return scene;
    }

    /// <summary>
    /// <b>The property the fix rests on: a warm entry is indistinguishable from
    /// one the cache rendered itself.</b> If it were not, warming would trade a
    /// stall for a wrong picture, which is the trade every other attempt on B332
    /// was rejected for.
    /// </summary>
    [Fact]
    public void AWarmedFrameHoldsExactlyWhatARenderWouldHave()
    {
        var scene = SceneWithArt();
        var frame = scene.Layers[0].Cels[0].Frame!;

        using var cold = new FrameBitmapCache();
        var rendered = cold.Get(frame, scene.Width, scene.Height, celIndex: 0);

        using var warmed = new FrameBitmapCache();
        // What the prewarmer produces off the UI thread, and hands back.
        var detached = FrameBitmapCache.RenderDetached(frame, scene.Width, scene.Height);
        Assert.True(warmed.InsertWarm(frame, scene.Width, scene.Height, 1.0, 0, detached));

        // And the very next lookup is served, not rendered.
        var before = warmed.Misses;
        var served = warmed.Get(frame, scene.Width, scene.Height, celIndex: 0);
        Assert.Equal(before, warmed.Misses);

        Assert.Equal(rendered.Width, served.Width);
        Assert.Equal(rendered.Height, served.Height);
        for (var y = 0; y < rendered.Height; y += 7)
        {
            for (var x = 0; x < rendered.Width; x += 7)
            {
                Assert.Equal(rendered.GetPixel(x, y), served.GetPixel(x, y));
            }
        }
    }

    /// <summary>
    /// <b>A warm only helps if the lookup that follows can find it</b>, which
    /// means the key it is stored under has to be the key the publish asks with.
    /// A warm at the wrong size is work thrown away in silence — the same class
    /// of failure as a report line that prints nothing.
    /// </summary>
    [Fact]
    public void AWarmAtTheWrongSizeDoesNotSatisfyTheLookup()
    {
        var scene = SceneWithArt();
        var frame = scene.Layers[0].Cels[0].Frame!;

        using var cache = new FrameBitmapCache();
        var detached = FrameBitmapCache.RenderDetached(frame, scene.Width / 2, scene.Height / 2);
        cache.InsertWarm(frame, scene.Width / 2, scene.Height / 2, 1.0, 0, detached);

        Assert.False(
            cache.Holds(frame, scene.Width, scene.Height, 1.0, 0),
            "a warm at half size must not answer for the full-size key, or the publish "
            + "still misses and the warm cost a worker for nothing");
    }

    /// <summary>
    /// <b>The guard that keeps this from becoming B322's fourth attempt.</b> That
    /// one added work proportional to the mark during a stroke and took building
    /// a frame from 2.41 ms to 23.06. Warming is only ever allowed to run while
    /// nothing is being drawn, so the worst it can cost is a worker rendering a
    /// frame nobody needed.
    /// </summary>
    [Fact]
    public void AFrameThatSamplesTheLayersBeneathIsNotWarmed()
    {
        var scene = SceneWithArt();
        var frame = scene.Layers[0].Cels[0].Frame!;
        frame.Strokes[0].Brush.SampleSource = SampleSource.AllLayersLive;

        Assert.False(
            FrameBitmapCache.CanCache(frame),
            "a frame that samples the layers beneath is deliberately never cached, so a "
            + "warm of it would be discarded on arrival and the request is pure waste");
    }
}
