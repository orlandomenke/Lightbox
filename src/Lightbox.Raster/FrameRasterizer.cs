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
    /// <param name="origin">
    /// Where the paper's top-left corner sits in stroke coordinates —
    /// <see cref="Scene.Left"/> and <see cref="Scene.Top"/>, which are non-zero
    /// only once the canvas has been grown or cropped on that side. Default is
    /// the ordinary document whose rectangle starts at zero.
    /// </param>
    public static SKBitmap Rasterize(
        IReadOnlyList<Stroke> strokes, int width, int height, double outputScale = 1.0,
        SKPointI origin = default)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(Scaled(info, outputScale));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        // Grouped rather than one at a time: strokes the record says were still
        // wet together share a wet region, and simulating them separately is
        // what makes a wash read as ribbons. A run of one is the ordinary case
        // and is unchanged.
        foreach (var run in WetRun.Split(strokes))
        {
            BrushEngine.StampWetRun(
                canvas, run, info, bitmap, outputScale: outputScale, origin: origin);
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
        // The mutator announces itself: this bitmap is (deliberately) the one
        // the frame cache holds, and any cache keyed on its identity — a tile
        // split, a baked layer stack — must see the content move.
        BitmapVersion.Bump(layer);
    }

    /// <summary>
    /// Stamp a whole stroke list onto an existing layer bitmap in place, with
    /// the wet runs grouped as <see cref="Rasterize"/> groups them.
    /// </summary>
    /// <remarks>
    /// For the callers that have pixels to draw over — a baseline image, a
    /// partial render — and so cannot start from a fresh bitmap. Appending one
    /// stroke at a time would put a seam back into every wash, and the result
    /// would then differ from the same strokes reloaded.
    /// </remarks>
    public static void AppendAll(SKBitmap layer, IReadOnlyList<Stroke> strokes)
    {
        var info = new SKImageInfo(layer.Width, layer.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(layer);
        foreach (var run in WetRun.Split(strokes))
        {
            BrushEngine.StampWetRun(canvas, run, info, layer);
        }
        canvas.Flush();
        BitmapVersion.Bump(layer);
    }

    /// <summary>
    /// Live-preview stamp: work is bounded to the new segment and the
    /// stroke-global effects wait for the commit — this is what keeps effect
    /// brushes responsive while drawing. The committed frame is always
    /// re-rendered exactly via <see cref="Append"/>/<see cref="Rasterize"/>.
    /// </summary>
    /// <param name="readFrom">
    /// What an effect brush samples, when that must not be the bitmap it is
    /// writing into. Smudge and blur read the pixels they sit on, and the exact
    /// render gives every dab of a stroke the same <em>pre-stroke</em> pixels —
    /// so a live preview that samples its own accumulated output is applying
    /// the effect once per pointer event instead of once per stroke. Defaults
    /// to <paramref name="layer"/>, which is right for every other brush.
    /// </param>
    /// <param name="dabs">
    /// The whole stroke already walked, when the caller is tracking a drag. Effect brushes need the
    /// commit's dab positions and a per-segment walk cannot give them (B54); null walks here.
    /// </param>
    /// <param name="fromDab">The first dab not settled yet — see <see cref="BrushEngine.StampStroke"/>.</param>
    public static void AppendDraft(
        SKBitmap layer, Stroke stroke, SKBitmap? readFrom = null,
        IReadOnlyList<BrushEngine.Dab>? dabs = null, int fromDab = 0)
    {
        var info = new SKImageInfo(layer.Width, layer.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(layer);
        BrushEngine.StampStroke(
            canvas, stroke, info, readFrom ?? layer, draft: true, draftDabs: dabs, draftFromDab: fromDab);
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
    /// <param name="backdrop">
    /// The composite of the layers beneath this one, for strokes that sample
    /// all layers. Null everywhere that has no stack to hand — a thumbnail, a
    /// symbol tile — and those fall back to sampling the layer itself.
    /// </param>
    public static SKBitmap Materialize(
        Frame frame, int width, int height, double outputScale = 1.0, int celIndex = 0,
        SKBitmap? backdrop = null)
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
        // Grouped for the same reason `Rasterize` groups — and it has to be the
        // same grouping, because this is the path the frame cache renders
        // through and that one is what a test compares against.
        foreach (var run in WetRun.Split(frame.Strokes))
        {
            BrushEngine.StampWetRun(
                canvas, run, info, bitmap, outputScale: outputScale, backdrop: backdrop);
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
