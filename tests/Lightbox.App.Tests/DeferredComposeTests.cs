using Lightbox.App.Rendering;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The composite the canvas performs is the composite the publisher used to.
/// </summary>
/// <remarks>
/// <para>
/// <b>B125 stage 3b, measured against stage 2's harness.</b> Moving a composite
/// from the UI thread into the draw op is only acceptable if the pixels do not
/// change, and "looks the same" is not a claim this project accepts —
/// B156 through B164 were diagnosed from field captures precisely because
/// nothing could assert on a composite.
/// </para>
/// <para>
/// The reference here is the culled compose as it was performed at publish time,
/// reproduced literally. If <see cref="DeferredCompose"/> ever drifts from it,
/// these fail rather than the difference reaching an artist as a soft edge or a
/// shifted layer.
/// </para>
/// </remarks>
public class DeferredComposeTests(ITestOutputHelper output)
{
    private static SKBitmap Layer(int w, int h, byte seed)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = new SKColor(seed, (byte)(255 - seed), 128, 220) };
        canvas.DrawRect(seed % 23, seed % 11, w * 0.55f, h * 0.65f, paint);
        return bmp;
    }

    private static List<RenderPass> Passes(int w, int h) =>
    [
        new(Layer(w, h, 40), null, 1.0, SKBlendMode.SrcOver),
        new(Layer(w, h, 170), null, 0.6, SKBlendMode.Multiply),
        new(Layer(w, h, 90), new SKColor(0xd0, 0x40, 0x40), 0.35, SKBlendMode.SrcOver),
    ];

    /// <summary>
    /// The publisher's culled compose, reproduced exactly as it stood before
    /// stage 3b moved it. The point of copying it rather than calling the new
    /// code is that a shared helper would agree with itself no matter what.
    /// </summary>
    private static SKImage ComposeAsThePublisherDid(
        IReadOnlyList<RenderPass> passes, SKColor background, double renderScale,
        SKImageInfo info, SKRectI viewport)
    {
        using var surface = SKSurface.Create(info)!;
        var canvas = surface.Canvas;
        canvas.Clear(background);
        canvas.Scale((float)renderScale, (float)renderScale);
        canvas.Translate(-viewport.Left, -viewport.Top);

        var visible = new SKRect(viewport.Left, viewport.Top, viewport.Right, viewport.Bottom);
        foreach (var pass in passes)
        {
            if (pass.Bitmap is null) continue;
            using var paint = new SKPaint { BlendMode = pass.Blend };
            if (pass.Opacity < 1.0)
                paint.Color = paint.Color.WithAlpha((byte)(pass.Opacity * 255));
            if (pass.Tint.HasValue)
                paint.ColorFilter = SKColorFilter.CreateBlendMode(pass.Tint.Value, SKBlendMode.Multiply);
            canvas.DrawBitmap(pass.Bitmap, visible, visible, paint);

            if (pass.Overlay is { } overlay)
            {
                using var overlayPaint = new SKPaint
                {
                    BlendMode = overlay.Erases ? SKBlendMode.DstOut : SKBlendMode.SrcOver,
                };
                if (overlay.Opacity < 1.0)
                    overlayPaint.Color = overlayPaint.Color.WithAlpha((byte)(overlay.Opacity * 255));
                canvas.DrawBitmap(overlay.Scratch, visible, visible, overlayPaint);
            }
        }
        canvas.Flush();
        return surface.Snapshot();
    }

    private static byte[] Bytes(SKImage image, SKImageInfo info)
    {
        var buffer = new byte[info.BytesSize];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(
            buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            Assert.True(image.ReadPixels(info, handle.AddrOfPinnedObject(), info.RowBytes, 0, 0));
        }
        finally { handle.Free(); }
        return buffer;
    }

    private static int FirstDifference(byte[] a, byte[] b)
    {
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++) if (a[i] != b[i]) return i;
        return a.Length == b.Length ? -1 : Math.Min(a.Length, b.Length);
    }

    /// <summary>
    /// The whole claim of stage 3b, at three compose scales because the scale is
    /// applied as a canvas transform and rounding is where a "faithful move"
    /// usually stops being faithful.
    /// </summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(0.5)]
    [InlineData(0.37)]
    public void DeferringTheComposeChangesNoPixels(double scale)
    {
        var viewport = SKRectI.Create(12, 8, 96, 72);
        var info = new SKImageInfo(
            (int)Math.Ceiling(viewport.Width * scale),
            (int)Math.Ceiling(viewport.Height * scale),
            SKColorType.Rgba8888, SKAlphaType.Premul);
        var passes = Passes(160, 120);
        var background = new SKColor(0xf2, 0xf0, 0xea);

        using var reference = ComposeAsThePublisherDid(passes, background, scale, info, viewport);
        var work = new DeferredCompose(passes, background, scale, info, viewport);
        using var moved = work.Compose(null, out var gpuBacked);

        var diff = FirstDifference(Bytes(reference, info), Bytes(moved, info));
        output.WriteLine($"scale {scale}: {info.Width}x{info.Height}, gpu={gpuBacked}, diff={diff}");
        Assert.Equal(-1, diff);
    }

    /// <summary>
    /// A snapshot that carries a composite instead of an image has no pixels
    /// until somebody asks — which is a saving, not a bug: a publish nobody ever
    /// draws now costs nothing at all.
    /// </summary>
    [Fact]
    public void ADeferredSnapshotAllocatesNothingUntilItIsDrawn()
    {
        var info = new SKImageInfo(32, 24, SKColorType.Rgba8888, SKAlphaType.Premul);
        var work = new DeferredCompose(
            Passes(64, 48), SKColors.White, 1.0, info, SKRectI.Create(0, 0, 32, 24));

        var snapshot = new RenderSnapshot(null, 64, 48, 1, null, null, null, null, work);

        Assert.Null(snapshot.Image);

        using var composed = snapshot.Materialise(null);

        Assert.NotNull(snapshot.Image);
        Assert.Equal(32, composed.Width);
    }

    /// <summary>
    /// Composed once and kept. A snapshot is drawn again on a resize, a view
    /// change or an ordinary compositor repaint, and recomposing per draw would
    /// turn one publish into many composites — the opposite of the point.
    /// </summary>
    [Fact]
    public void MaterialisingTwiceComposesOnce()
    {
        var info = new SKImageInfo(32, 24, SKColorType.Rgba8888, SKAlphaType.Premul);
        var work = new DeferredCompose(
            Passes(64, 48), SKColors.White, 1.0, info, SKRectI.Create(0, 0, 32, 24));
        var snapshot = new RenderSnapshot(null, 64, 48, 1, null, null, null, null, work);

        var first = snapshot.Materialise(null);
        var second = snapshot.Materialise(null);

        Assert.Same(first, second);
    }

    /// <summary>
    /// <b>A freed snapshot refuses to compose rather than quietly resurrecting.</b>
    /// Since stage 3b a null image means two things — never composed, and freed —
    /// and the difference is what <c>HeldImagesAlive</c> checks to catch B130.
    /// Recomposing here would hand the render thread pixels whose passes have
    /// already been released.
    /// </summary>
    [Fact]
    public void AFreedSnapshotWillNotComposeAgain()
    {
        var info = new SKImageInfo(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul);
        var work = new DeferredCompose(
            Passes(32, 32), SKColors.White, 1.0, info, SKRectI.Create(0, 0, 16, 16));
        var snapshot = new RenderSnapshot(null, 32, 32, 1, null, null, null, null, work);

        snapshot.Dispose();

        Assert.True(snapshot.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => snapshot.Materialise(null));
    }
}
