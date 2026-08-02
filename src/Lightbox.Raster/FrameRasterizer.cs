using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// strokes[] → pixels. The single shared pipeline for live painting,
/// inbetween re-render, AI strokes, and undo re-render.
/// </summary>
public static class FrameRasterizer
{
    /// <summary>
    /// Render a stroke list to a fresh transparent bitmap.
    /// </summary>
    /// <param name="width">Document width. Never multiply this by the scale yourself.</param>
    /// <param name="height">Document height.</param>
    /// <param name="outputScale">
    /// Pixels per document unit. Above 1 the strokes are re-rasterised sharp
    /// rather than magnified — which is what lets a camera push past 100%
    /// without revealing pixels. The stroke geometry is untouched at every
    /// scale, so the dab dynamics land identically (see
    /// <see cref="BrushEngine.StampStroke"/>).
    /// </param>
    public static SKBitmap Rasterize(
        IReadOnlyList<Stroke> strokes, int width, int height, double outputScale = 1.0)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(Scaled(info, outputScale));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        foreach (var stroke in strokes)
        {
            BrushEngine.StampStroke(canvas, stroke, info, bitmap, outputScale: outputScale);
        }
        canvas.Flush();
        return bitmap;
    }

    /// <summary>The pixels a document-sized render needs at a given output scale.</summary>
    internal static SKImageInfo Scaled(SKImageInfo info, double outputScale) =>
        outputScale == 1.0
            ? info
            : info.WithSize(
                Math.Max(1, (int)Math.Ceiling(info.Width * outputScale)),
                Math.Max(1, (int)Math.Ceiling(info.Height * outputScale)));

    /// <summary>Stamp one more stroke onto an existing layer bitmap in place.</summary>
    public static void Append(SKBitmap layer, Stroke stroke)
    {
        var info = new SKImageInfo(layer.Width, layer.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(layer);
        BrushEngine.StampStroke(canvas, stroke, info, layer);
        canvas.Flush();
    }

    /// <summary>
    /// Live-preview stamp: work is bounded to the new segment and the
    /// stroke-global effects wait for the commit — this is what keeps effect
    /// brushes responsive while drawing. The committed frame is always
    /// re-rendered exactly via <see cref="Append"/>/<see cref="Rasterize"/>.
    /// </summary>
    public static void AppendDraft(SKBitmap layer, Stroke stroke)
    {
        var info = new SKImageInfo(layer.Width, layer.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(layer);
        BrushEngine.StampStroke(canvas, stroke, info, layer, draft: true);
        canvas.Flush();
    }

    /// <summary>
    /// Materialize a painted frame's pixels: baseline PNG (if any) with the
    /// stroke record stamped on top, in order. Strokes are never baked into
    /// the baseline, so this is repeatable and always current.
    /// </summary>
    /// <param name="celIndex">
    /// Where on the timeline this cel sits. Only read by the symbol pass, and
    /// only when the frame places one — it is what makes a placed cycle advance
    /// with the sequence instead of freezing on its first drawing.
    /// </param>
    public static SKBitmap Materialize(
        PaintedFrame frame, int width, int height, double outputScale = 1.0, int celIndex = 0)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var scaled = Scaled(info, outputScale);
        var bitmap = new SKBitmap(scaled);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        if (!string.IsNullOrEmpty(frame.PngBase64))
        {
            // The one thing here that cannot be re-rendered sharp: a baseline
            // is stored pixels, so above 1x it magnifies. Nothing in the app
            // creates one — the only writer rewrites an already-nonempty
            // baseline — but a document that carries one will show it.
            using var baseline = PngCodec.Decode(frame.PngBase64);
            using var image = SKImage.FromBitmap(baseline);
            canvas.DrawImage(
                image,
                new SKRect(0, 0, scaled.Width, scaled.Height),
                new SKSamplingOptions(SKFilterMode.Linear));
        }
        foreach (var stroke in frame.Strokes)
        {
            BrushEngine.StampStroke(canvas, stroke, info, bitmap, outputScale: outputScale);
        }
        // Placements last, over the strokes. A placement is a drawing put on
        // top of this cel, not one mixed into it — and the ordering has to be
        // fixed, because "over" is the only answer that stays true when the
        // symbol is edited later.
        SymbolRasterizer.StampPlacements(canvas, frame, info, celIndex, outputScale);
        canvas.Flush();
        return bitmap;
    }
}
