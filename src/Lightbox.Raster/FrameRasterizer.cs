using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// strokes[] → pixels. The single shared pipeline for live painting,
/// inbetween re-render, AI strokes, and undo re-render.
/// </summary>
public static class FrameRasterizer
{
    /// <summary>Render a stroke list to a fresh transparent bitmap.</summary>
    public static SKBitmap Rasterize(IReadOnlyList<Stroke> strokes, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        foreach (var stroke in strokes)
        {
            BrushEngine.StampStroke(canvas, stroke, info, bitmap);
        }
        canvas.Flush();
        return bitmap;
    }

    /// <summary>Stamp one more stroke onto an existing layer bitmap in place.</summary>
    public static void Append(SKBitmap layer, Stroke stroke)
    {
        var info = new SKImageInfo(layer.Width, layer.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(layer);
        BrushEngine.StampStroke(canvas, stroke, info, layer);
        canvas.Flush();
    }

    /// <summary>
    /// Materialize a painted frame's pixels: baseline PNG (if any) with the
    /// stroke record stamped on top, in order. Strokes are never baked into
    /// the baseline, so this is repeatable and always current.
    /// </summary>
    public static SKBitmap Materialize(PaintedFrame frame, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        if (!string.IsNullOrEmpty(frame.PngBase64))
        {
            using var baseline = PngCodec.Decode(frame.PngBase64);
            canvas.DrawBitmap(baseline, 0, 0);
        }
        foreach (var stroke in frame.Strokes)
        {
            BrushEngine.StampStroke(canvas, stroke, info, bitmap);
        }
        canvas.Flush();
        return bitmap;
    }
}
