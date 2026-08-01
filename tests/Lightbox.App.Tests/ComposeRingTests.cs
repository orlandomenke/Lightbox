using Lightbox.App.Rendering;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The compose ring rotates three back-buffers so Skia never has to duplicate
/// a surface whose snapshot is still live. The subtlety it has to get right is
/// what happens to the buffers that sat out: they must come back correct
/// (<see cref="EveryPublishIsACorrectFullComposite"/>) without paying to
/// recomposite everything that changed while they were idle
/// (<see cref="AfterALargeChange_TheNextPublishesStillOnlyRepaintTheirOwnDirtyRect"/>),
/// which is what made the first three events of every stroke cost ~80 ms at 4K.
/// </summary>
public class ComposeRingTests
{
    private static readonly SKImageInfo Info =
        new(200, 120, SKColorType.Rgba8888, SKAlphaType.Premul);

    /// <summary>
    /// A stand-in scene: coloured squares on white. Compositing it honours the
    /// clip the ring hands over, exactly as <c>SceneRenderer.ComposeInto</c> does,
    /// so a buffer that is told to repaint too little shows it as stale pixels.
    /// </summary>
    private sealed class Squares
    {
        public readonly List<(SKRectI Rect, SKColor Color)> Items = [];

        public void Paint(SKSurface surface, SKRectI? clip, double scale)
        {
            var canvas = surface.Canvas;
            canvas.Save();
            canvas.ResetMatrix();
            if (clip is { } r)
            {
                canvas.ClipRect(SKRect.Create(
                    (float)Math.Floor(r.Left * scale),
                    (float)Math.Floor(r.Top * scale),
                    (float)Math.Ceiling(r.Width * scale) + 1,
                    (float)Math.Ceiling(r.Height * scale) + 1));
            }
            canvas.Clear(SKColors.White);
            if (scale != 1.0) canvas.Scale((float)scale);
            foreach (var (rect, color) in Items)
            {
                using var paint = new SKPaint { Color = color };
                canvas.DrawRect(SKRect.Create(rect.Left, rect.Top, rect.Width, rect.Height), paint);
            }
            canvas.Restore();
        }
    }

    private static SKColor Sample(SKImage image, int x, int y)
    {
        using var pixels = image.PeekPixels();
        Assert.NotNull(pixels);
        return pixels.GetPixelColor(x, y);
    }

    [Fact]
    public void AfterALargeChange_TheNextPublishesStillOnlyRepaintTheirOwnDirtyRect()
    {
        using var ring = new ComposeRing();
        var clips = new List<SKRectI?>();

        SKImage Publish(SKRectI? dirty)
        {
            var image = ring.Publish(Info, dirty, (surface, clip) =>
            {
                clips.Add(clip);
                surface.Canvas.Clear(SKColors.White);
            });
            return image;
        }

        // Warm all three surfaces into existence; each one's first paint is
        // necessarily a full repaint and is not what this test is about.
        for (var i = 0; i < 3; i++) Publish(null).Dispose();
        clips.Clear();

        // A stroke crossing the canvas: one big dirty region.
        var big = new SKRectI(0, 0, 200, 120);
        Publish(big).Dispose();
        clips.Clear();

        // The events that follow touch a dab-sized region each. Every one of
        // them must be told to repaint only that dab. Before the copy-forward,
        // the two buffers that sat out the big change each carried it as an
        // outstanding union, so the next two publishes repainted the whole
        // canvas — the measured 80 ms, 80 ms prefix on a 4K stroke.
        for (var i = 0; i < 6; i++)
        {
            var dab = new SKRectI(10 + i * 4, 10, 30 + i * 4, 30);
            Publish(dab).Dispose();
            Assert.Equal(dab, clips[^1]);
        }
    }

    [Fact]
    public void ABufferStillHoldingASnapshot_IsCaughtUpOnceItComesFree()
    {
        using var ring = new ComposeRing();
        var clips = new List<SKRectI?>();
        var held = new List<SKImage>();

        void Publish(SKRectI? dirty, bool release)
        {
            var image = ring.Publish(Info, dirty, (surface, clip) =>
            {
                clips.Add(clip);
                surface.Canvas.Clear(SKColors.White);
            });
            if (release) image.Dispose();
            else held.Add(image);
        }

        for (var i = 0; i < 3; i++) Publish(null, release: true);

        // Hold this one, the way the render thread holds the frame on screen.
        Publish(new SKRectI(0, 0, 200, 120), release: false);
        // Two more big changes land while it is still on screen.
        Publish(new SKRectI(0, 0, 200, 120), release: true);
        Publish(new SKRectI(0, 0, 200, 120), release: true);

        // It comes free. Catching it up is a copy, not a recomposite, so the
        // publish that lands on it next is still told to paint only its dab.
        foreach (var image in held) image.Dispose();
        held.Clear();
        clips.Clear();

        for (var i = 0; i < 4; i++)
        {
            var dab = new SKRectI(50, 50, 70, 70);
            Publish(dab, release: true);
            Assert.Equal(dab, clips[^1]);
        }
    }

    /// <summary>
    /// The catch-up must never dispose the image the canvas is still showing.
    ///
    /// SkiaSharp returns the <em>same</em> <see cref="SKImage"/> object from
    /// <c>Snapshot()</c> when the surface has not been drawn to since — which
    /// is exactly the state of the buffer a catch-up copies from. Taking a
    /// snapshot there and disposing it with a <c>using</c> therefore tears
    /// down the object <c>CanvasControl</c> is holding to render, behind the
    /// back of its own retirement queue: a use-after-dispose on a live native
    /// image, not merely a stale pixel.
    /// </summary>
    [Fact]
    public void CatchingUpDoesNotDisposeTheImageTheCanvasIsStillShowing()
    {
        using var ring = new ComposeRing();
        SKImage Publish(SKRectI? dirty) =>
            ring.Publish(Info, dirty, (s, _) => s.Canvas.Clear(SKColors.White));

        for (var i = 0; i < 3; i++) Publish(null).Dispose();

        // The frame on screen: the consumer keeps it alive deliberately.
        var onScreen = Publish(new SKRectI(0, 0, 64, 64));
        // The next pointer event publishes again, and its catch-up runs.
        using var next = Publish(new SKRectI(1, 1, 4, 4));

        Assert.NotEqual(IntPtr.Zero, onScreen.Handle);
        using (var pixels = onScreen.PeekPixels()) Assert.NotNull(pixels);
        onScreen.Dispose();
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.4)]
    public void EveryPublishIsACorrectFullComposite(double scale)
    {
        var info = new SKImageInfo(
            (int)Math.Ceiling(200 * scale),
            (int)Math.Ceiling(120 * scale),
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        using var ring = new ComposeRing();
        var scene = new Squares();

        // Add one square per publish, telling the ring only about that square.
        // Anything the copy-forward gets wrong — the wrong region, a blend
        // instead of a replace, the document/device scale mixed up — shows as
        // a square missing from a later frame.
        var colors = new[]
        {
            SKColors.Red, SKColors.Lime, SKColors.Blue,
            SKColors.Magenta, SKColors.Cyan, SKColors.Black, SKColors.Orange,
        };
        SKImage? last = null;
        for (var i = 0; i < colors.Length; i++)
        {
            var rect = new SKRectI(10 + i * 20, 20, 10 + i * 20 + 16, 60);
            scene.Items.Add((rect, colors[i]));
            var dirty = i == 0 ? (SKRectI?)null : rect;
            last?.Dispose();
            last = ring.Publish(info, dirty, (surface, clip) => scene.Paint(surface, clip, scale), scale);
        }

        Assert.NotNull(last);
        for (var i = 0; i < colors.Length; i++)
        {
            // Sample the middle of each square, well inside it at either scale.
            var x = (int)((10 + i * 20 + 8) * scale);
            var y = (int)(40 * scale);
            Assert.Equal(colors[i], Sample(last, x, y));
        }
        // And the paper between the squares survived.
        Assert.Equal(SKColors.White, Sample(last, (int)(190 * scale), (int)(100 * scale)));
        last.Dispose();
    }

    [Fact]
    public void InvalidateAll_ForcesAFullRepaintEvenWithASmallDirtyRect()
    {
        using var ring = new ComposeRing();
        var clips = new List<SKRectI?>();
        for (var i = 0; i < 3; i++)
        {
            ring.Publish(Info, null, (s, c) => { clips.Add(c); s.Canvas.Clear(SKColors.White); }).Dispose();
        }

        ring.InvalidateAll();
        clips.Clear();
        for (var i = 0; i < 3; i++)
        {
            ring.Publish(Info, new SKRectI(1, 1, 2, 2), (s, c) =>
            {
                clips.Add(c);
                s.Canvas.Clear(SKColors.White);
            }).Dispose();
        }

        // The first publish after an invalidate must repaint in full; the
        // others may be caught up by a copy from it.
        Assert.Null(clips[0]);
    }
}
