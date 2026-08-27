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
        foreach (var stroke in strokes)
        {
            BrushEngine.StampStroke(
                canvas, stroke, info, bitmap, outputScale: outputScale, origin: origin);
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
    /// Rebuild one rectangle of a frame's pixels in place: clear it, then replay
    /// only the strokes that reach it, in record order. Returns false — having
    /// touched nothing — when the frame is one this cannot rebuild exactly, and
    /// the caller must fall back to re-rendering the whole drawing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B327: <see cref="Append"/> run backwards.</b> Committing a stroke is
    /// bounded work; undoing it dropped the frame's bitmap and re-stamped every
    /// stroke on it, so drawing was O(1) and taking it back was O(n) —
    /// <b>3 092 ms per Ctrl+Z at 1 600 strokes</b>, dead linear. This makes undo
    /// cost what the mark cost, which is invariant 6's shape.
    /// </para>
    /// <para>
    /// <b>Invariant 2 is what makes the replay exact rather than approximate.</b>
    /// <c>Hash01</c> seeds every dab dynamic from the IEEE-754 bits of its
    /// position, so a stroke replayed under a clip lands the same dabs it landed
    /// the first time. The clip is on the <em>canvas</em> and never on the
    /// geometry — invariant 7's rule, and the same reason output scale is a
    /// surface transform. <c>StampStroke</c> is still handed the whole-document
    /// <paramref name="info"/> so the stroke-global passes (wet edge, medium,
    /// granulation, texture) compute over the whole stroke and Skia clips the
    /// drawing: <b>compute whole, clip to the region</b>, which is the rule
    /// <c>RasterizeByTile</c> already lives by.
    /// </para>
    /// <para>
    /// <b>What it refuses, and why refusing is the safe direction.</b> Falling
    /// back costs a re-render; a wrong fast path leaves ink on the canvas the
    /// document no longer describes. So this returns false for anything it
    /// cannot promise, and the caller is expected to treat false as ordinary.
    /// </para>
    /// </remarks>
    /// <param name="region">
    /// The rectangle to rebuild, in <b>surface</b> pixels — already scaled, and
    /// already clamped to the bitmap by the caller.
    /// </param>
    public static bool RepaintRegion(
        SKBitmap layer, Frame frame, SKRectI region, int width, int height,
        double outputScale = 1.0, SKBitmap? backdrop = null)
    {
        if (region.Width <= 0 || region.Height <= 0) return false;
        if (!CanRepaintRegion(frame)) return false;

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

        // Asked before anything is painted, so a refusal leaves the bitmap as it
        // was rather than half-rebuilt.
        var replay = new List<Stroke>();
        foreach (var stroke in frame.Strokes)
        {
            if (BrushEngine.ReachBounds(stroke) is not { } reach) continue;
            if (!Scale(reach, outputScale).IntersectsWith(region)) continue;
            // An effect brush reads the pixels it sits on. Outside the clip this
            // bitmap holds the drawing as it stands *now* — every stroke, including
            // ones painted after this one — where a full render would have shown it
            // only what came before. A smudge whose dabs sample across the region
            // edge would therefore drag future ink into the past. No coordinate
            // fixes that, so the whole repaint goes back to the slow path.
            if (stroke.Brush.Kind is BrushKind.Smudge or BrushKind.Blur) return false;
            replay.Add(stroke);
        }

        using var canvas = new SKCanvas(layer);
        canvas.Save();
        canvas.ClipRect(SKRect.Create(region.Left, region.Top, region.Width, region.Height));
        canvas.Clear(SKColors.Transparent);
        foreach (var stroke in replay)
        {
            BrushEngine.StampStroke(
                canvas, stroke, info, layer, outputScale: outputScale, backdrop: backdrop);
        }
        canvas.Restore();
        canvas.Flush();
        // Same announcement Append makes, and for the same reason: this bitmap is
        // the one the frame cache holds, and anything keyed on its identity — a
        // tile split, a baked layer stack — must see the content move.
        BitmapVersion.Bump(layer);
        return true;
    }

    /// <summary>
    /// Whether a frame's pixels can be rebuilt a region at a time at all.
    /// </summary>
    /// <remarks>
    /// Three refusals, each because the region would not hold the whole answer.
    /// A <b>baseline</b> is stored pixels under the strokes, so clearing a
    /// rectangle destroys part of it and only a crop would put it back (nothing
    /// in the app writes one, so this is a guard rather than a case). A
    /// <b>placement</b> is stamped over the strokes by <c>SymbolRasterizer</c>
    /// and answers to a symbol that can be edited elsewhere, so its footprint is
    /// not this frame's to compute. A frame that <b>samples the layers beneath
    /// it live</b> is never cached in the first place — <c>FrameBitmapCache.Get</c>
    /// drops it on every read — so there is no bitmap here to patch.
    /// </remarks>
    public static bool CanRepaintRegion(Frame frame) =>
        string.IsNullOrEmpty(frame.PngBase64)
        && frame.Placements is not { Count: > 0 }
        && !frame.Strokes.Any(s => s.Brush.SampleSource == SampleSource.AllLayersLive);

    /// <summary>A reach in stroke coordinates as surface pixels.</summary>
    private static SKRectI Scale(SKRectI rect, double outputScale)
    {
        if (outputScale == 1.0) return rect;
        return new SKRectI(
            (int)Math.Floor(rect.Left * outputScale),
            (int)Math.Floor(rect.Top * outputScale),
            (int)Math.Ceiling(rect.Right * outputScale),
            (int)Math.Ceiling(rect.Bottom * outputScale));
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
    /// <param name="checkpoint">
    /// A rendering of the frame's leading strokes to start from instead of
    /// replaying them (B30), <b>already validated by the caller</b> through
    /// <see cref="FrameCheckpoints.Usable"/>. Null is the ordinary case and the
    /// default: this is opt-in, so a caller that has not thought about it — an
    /// export, a thumbnail, a symbol tile — gets the record replayed in full, as
    /// it always has been.
    /// </param>
    public static SKBitmap Materialize(
        Frame frame, int width, int height, double outputScale = 1.0, int celIndex = 0,
        SKBitmap? backdrop = null, StrokeCheckpoint? checkpoint = null)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var scaled = Scaled(info, outputScale);
        var bitmap = new SKBitmap(scaled);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        var replayFrom = 0;
        if (checkpoint is not null && CheckpointApplies(frame, checkpoint, outputScale, width, height))
        {
            using var stored = FrameCheckpoints.Pixels(checkpoint);
            if (stored is not null)
            {
                // Drawn 1:1, never scaled — `CheckpointApplies` has established
                // the surface is document-sized. A checkpoint is the only image
                // in this method that must not be resampled: it is a rendering of
                // strokes, so if it does not fit, the answer is the strokes.
                canvas.DrawBitmap(stored, 0, 0);
                replayFrom = checkpoint.Strokes;
            }
            // Null means the pixels would not decode, and that is not an error:
            // `replayFrom` stays 0 and the whole record is replayed onto a canvas
            // nothing has been drawn on. B137's rule (see `CheckpointCodec`).
        }

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
        for (var i = replayFrom; i < frame.Strokes.Count; i++)
        {
            BrushEngine.StampStroke(
                canvas, frame.Strokes[i], info, bitmap, outputScale: outputScale, backdrop: backdrop);
        }
        // Placements last, over the strokes. A placement is a drawing put on
        // top of this cel, not one mixed into it — and the ordering has to be
        // fixed, because "over" is the only answer that stays true when the
        // symbol is edited later.
        SymbolRasterizer.StampPlacements(canvas, frame, info, celIndex, outputScale);
        canvas.Flush();
        return bitmap;
    }

    /// <summary>
    /// Whether a validated checkpoint may be used for <em>this</em> render.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The caller has already established that the checkpoint describes the
    /// record (<see cref="FrameCheckpoints.Usable"/>). What is left is whether
    /// it describes the render being asked for, which is a different question
    /// and is asked here so no call site can forget it.
    /// </para>
    /// <para>
    /// <b>Output scale is the one that matters, and it is invariant 7.</b> A
    /// checkpoint is pixels at document resolution. Above 1× the strokes are
    /// re-rasterised sharp rather than magnified — that is what lets a camera
    /// push past 100% without revealing pixels, and what makes an export at 4K
    /// from a screen-sized document a real render. Starting one of those from a
    /// stored image would blow the image up and quietly hand back the one thing
    /// the whole geometry-as-truth bet exists to avoid. So: 1× or the record.
    /// </para>
    /// <para>
    /// The rest is belt and braces on the same conclusion — the drawing must
    /// still have the strokes the checkpoint claims, and the surface must be the
    /// size those pixels were drawn for.
    /// </para>
    /// </remarks>
    private static bool CheckpointApplies(
        Frame frame, StrokeCheckpoint checkpoint, double outputScale, int width, int height) =>
        outputScale == 1.0
        && checkpoint.IsUsable
        && checkpoint.Strokes <= frame.Strokes.Count
        && checkpoint.Width == width
        && checkpoint.Height == height
        // A baseline is drawn *after* the checkpoint below and would land on top
        // of it, which is the one ordering this method can produce that is simply
        // wrong. `FrameCheckpoints.CanCheckpoint` refuses to make a checkpoint for
        // a frame with a baseline, so this cannot arise from the application — it
        // takes a hand-edited file. Refusing here costs a replay and means the
        // render path does not depend on the writer having been careful.
        && !frame.HasBaseline;

}
