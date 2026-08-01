using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// The in-progress stroke drawn on top of its layer. Kept separate from the
/// layer bitmap so a live preview never has to copy the layer (a full-canvas
/// copy costs ~1 s at 4K): the dabs accumulate in their own scratch and are
/// composited over the layer here, with the stroke's opacity applied once.
/// An eraser stroke removes only THIS layer's pixels, so the pair is
/// composited in isolation.
/// </summary>
public sealed record StrokeOverlay(SKBitmap Scratch, double Opacity, bool Erases);

/// <summary>One compositing pass: a layer bitmap with optional tint, opacity and blend mode.</summary>
public sealed record RenderPass(
    SKBitmap Bitmap,
    SKColor? Tint,
    double Opacity,
    SKBlendMode Blend = SKBlendMode.SrcOver,
    StrokeOverlay? Overlay = null);

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

    /// <summary>
    /// Composite into an existing (reusable) surface — the hot path during
    /// painting. <paramref name="clip"/> limits the work to a document region
    /// (null = the whole canvas); everything outside it keeps the surface's
    /// previous contents, so a live stroke only repaints what it touched.
    /// </summary>
    public static void ComposeInto(
        SKSurface surface,
        IReadOnlyList<RenderPass> passes,
        SKColor? background = null,
        SKRectI? clip = null,
        double scale = 1.0)
    {
        var canvas = surface.Canvas;
        canvas.Save();
        // The clip arrives in document coordinates; the surface may be smaller
        // than the document when the canvas cannot display full detail.
        if (clip is { } r)
        {
            canvas.ClipRect(SKRect.Create(
                (float)Math.Floor(r.Left * scale),
                (float)Math.Floor(r.Top * scale),
                (float)Math.Ceiling(r.Width * scale) + 1,
                (float)Math.Ceiling(r.Height * scale) + 1));
        }
        canvas.Clear(background ?? SKColors.White);
        if (scale != 1.0) canvas.Scale((float)scale);

        foreach (var pass in passes)
        {
            var alpha = (byte)Math.Round(Math.Clamp(pass.Opacity, 0, 1) * 255);
            using var paint = new SKPaint
            {
                Color = SKColors.White.WithAlpha(alpha),
                BlendMode = pass.Blend,
            };
            if (pass.Tint is { } tint)
            {
                paint.ColorFilter = SKColorFilter.CreateBlendMode(tint, SKBlendMode.SrcIn);
            }

            if (pass.Overlay is not { } overlay)
            {
                DrawLayer(canvas, pass.Bitmap, paint);
                continue;
            }

            using var strokePaint = new SKPaint
            {
                Color = SKColors.White.WithAlpha(
                    (byte)Math.Round(Math.Clamp(overlay.Opacity, 0, 1) * 255)),
                BlendMode = overlay.Erases ? SKBlendMode.DstOut : SKBlendMode.SrcOver,
            };

            // Isolation is only needed when the stroke must combine with its
            // own layer before that layer meets the ones below — an eraser
            // (which would otherwise cut through everything) or a layer that
            // is transparent or blended. Skipping the offscreen layer in the
            // ordinary case roughly halves the cost of a live repaint.
            var needsIsolation = overlay.Erases || alpha != 255 || pass.Blend != SKBlendMode.SrcOver;
            if (!needsIsolation)
            {
                DrawLayer(canvas, pass.Bitmap, paint);
                DrawLayer(canvas, overlay.Scratch, strokePaint);
                continue;
            }

            // SaveLayer allocates the current clip only, so a bounded live
            // region stays affordable even on a huge canvas.
            canvas.SaveLayer(paint);
            DrawLayer(canvas, pass.Bitmap, null);
            DrawLayer(canvas, overlay.Scratch, strokePaint);
            canvas.Restore();
        }
        canvas.Restore();
        canvas.Flush();
    }

    /// <summary>
    /// Blit a layer bitmap at the origin. Going through a zero-copy image
    /// view rather than <c>DrawBitmap</c> matters enormously under a clip:
    /// drawing a 4K bitmap into a small dirty region costs ~5.5 ms the
    /// direct way and ~0.5 ms this way, because Skia stops re-wrapping the
    /// mutable bitmap on every call. The view is a live window onto the same
    /// pixels and never outlives this call.
    /// </summary>
    private static void DrawLayer(SKCanvas canvas, SKBitmap bitmap, SKPaint? paint)
    {
        using var pixels = bitmap.PeekPixels();
        if (pixels is not null)
        {
            using var view = SKImage.FromPixels(pixels);
            if (view is not null)
            {
                canvas.DrawImage(view, 0, 0, Downscale, paint);
                return;
            }
        }
        canvas.DrawBitmap(bitmap, 0, 0, paint);
    }

    /// <summary>
    /// Sampling for the layer blit. Linear is the honest choice when the
    /// compose surface is smaller than the document: nearest aliases thin
    /// line art badly, and mipmaps cost an order of magnitude more to build
    /// than the blit itself saves.
    /// </summary>
    private static readonly SKSamplingOptions Downscale = new(SKFilterMode.Linear);

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
