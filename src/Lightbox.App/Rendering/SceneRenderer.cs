using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>One compositing pass: a layer bitmap with optional tint and opacity.</summary>
public sealed record RenderPass(SKBitmap Bitmap, SKColor? Tint, double Opacity);

/// <summary>
/// Pure SkiaSharp scene compositing: white paper, then passes in order
/// (onion-skin ghosts first, live layers on top). Tinting replaces the pass's
/// color while keeping its alpha — the classic onion-skin look.
/// Runs entirely on the UI thread; the result is an immutable SKImage.
/// </summary>
public static class SceneRenderer
{
    public static SKImage Compose(int width, int height, IReadOnlyList<RenderPass> passes)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Could not create compose surface.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        foreach (var pass in passes)
        {
            using var paint = new SKPaint
            {
                Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(pass.Opacity, 0, 1) * 255)),
            };
            if (pass.Tint is { } tint)
            {
                paint.ColorFilter = SKColorFilter.CreateBlendMode(tint, SKBlendMode.SrcIn);
            }
            canvas.DrawBitmap(pass.Bitmap, 0, 0, paint);
        }
        canvas.Flush();
        return surface.Snapshot();
    }

    public static readonly SKColor OnionPrevTint = new(0xd0, 0x40, 0x40);
    public static readonly SKColor OnionNextTint = new(0x30, 0x60, 0xc0);
}
