using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>One compositing pass: a layer bitmap with optional tint, opacity and blend mode.</summary>
public sealed record RenderPass(SKBitmap Bitmap, SKColor? Tint, double Opacity, SKBlendMode Blend = SKBlendMode.SrcOver);

/// <summary>
/// Pure SkiaSharp scene compositing: white paper, then passes in order
/// (onion-skin ghosts first, live layers on top). Tinting replaces the pass's
/// color while keeping its alpha — the classic onion-skin look.
/// Runs entirely on the UI thread; the result is an immutable SKImage.
/// </summary>
public static class SceneRenderer
{
    public static SKImage Compose(int width, int height, IReadOnlyList<RenderPass> passes, SKColor? background = null)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Could not create compose surface.");
        ComposeInto(surface, passes, background);
        return surface.Snapshot();
    }

    /// <summary>The scene's paper color (or full transparency) as an SKColor.</summary>
    public static SKColor BackgroundOf(Lightbox.Core.Documents.Scene scene) =>
        scene.TransparentBackground
            ? SKColors.Transparent
            : Lightbox.Raster.BrushEngine.ParseColor(scene.BackgroundColor);

    /// <summary>Composite into an existing (reusable) surface — the hot path during painting.</summary>
    public static void ComposeInto(SKSurface surface, IReadOnlyList<RenderPass> passes, SKColor? background = null)
    {
        var canvas = surface.Canvas;
        canvas.Clear(background ?? SKColors.White);

        foreach (var pass in passes)
        {
            using var paint = new SKPaint
            {
                Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(pass.Opacity, 0, 1) * 255)),
                BlendMode = pass.Blend,
            };
            if (pass.Tint is { } tint)
            {
                paint.ColorFilter = SKColorFilter.CreateBlendMode(tint, SKBlendMode.SrcIn);
            }
            canvas.DrawBitmap(pass.Bitmap, 0, 0, paint);
        }
        canvas.Flush();
    }

    public static readonly SKColor OnionPrevTint = new(0xd0, 0x40, 0x40);
    public static readonly SKColor OnionNextTint = new(0x30, 0x60, 0xc0);

    /// <summary>Photoshop-style layer blend modes map 1:1 onto Skia's.</summary>
    public static SKBlendMode ToSkia(LayerBlendMode mode) => mode switch
    {
        LayerBlendMode.Multiply => SKBlendMode.Multiply,
        LayerBlendMode.Screen => SKBlendMode.Screen,
        LayerBlendMode.Overlay => SKBlendMode.Overlay,
        LayerBlendMode.Darken => SKBlendMode.Darken,
        LayerBlendMode.Lighten => SKBlendMode.Lighten,
        LayerBlendMode.ColorDodge => SKBlendMode.ColorDodge,
        LayerBlendMode.ColorBurn => SKBlendMode.ColorBurn,
        LayerBlendMode.HardLight => SKBlendMode.HardLight,
        LayerBlendMode.SoftLight => SKBlendMode.SoftLight,
        LayerBlendMode.Difference => SKBlendMode.Difference,
        LayerBlendMode.Exclusion => SKBlendMode.Exclusion,
        LayerBlendMode.Hue => SKBlendMode.Hue,
        LayerBlendMode.Saturation => SKBlendMode.Saturation,
        LayerBlendMode.Color => SKBlendMode.Color,
        LayerBlendMode.Luminosity => SKBlendMode.Luminosity,
        _ => SKBlendMode.SrcOver,
    };
}
