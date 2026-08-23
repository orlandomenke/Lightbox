using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// Merge a layer into the one below it (Photoshop's Ctrl+E), drawing by
/// drawing along the exposure sheet.
/// </summary>
/// <remarks>
/// <para>
/// <b>The stroke record survives wherever the merge can be exact without
/// pixels, and only there.</b> Appending the upper layer's strokes after the
/// lower's reproduces the composite bit-for-bit only when nothing about the
/// upper layer reads what is underneath it: Normal blend at full opacity, no
/// baseline (a baseline sits <em>below</em> strokes, and there is only one
/// slot), no erasers or clear-regions (they would start eating the lower
/// layer's marks), no alpha-locked or smudge strokes (both sample the canvas
/// so far, which the merge changes). Every drawing that passes keeps its
/// strokes — inbetweenable, editable, invariant 1 intact.
/// </para>
/// <para>
/// Everything else is baked: both drawings are materialized, the upper is
/// composited over the lower through the upper layer's blend and opacity, and
/// the result becomes the merged frame's pixel baseline — exactly the
/// "imported or flattened pixels" a baseline exists to carry. That is what
/// merging <em>means</em> for a Multiply layer at 60%; the cost (the AI cannot
/// read pixels) is what the Q52 warning names before this runs. As in
/// Photoshop, a non-Normal blend is baked against the layer below only, so a
/// mark whose blend was interacting with layers deeper in the stack can shift.
/// </para>
/// <para>
/// The decision is taken per exposed pair, not per layer, so one baked
/// drawing does not cost the other eleven their stroke records.
/// </para>
/// </remarks>
public static class LayerMerge
{
    /// <summary>
    /// Would merging <paramref name="upper"/> into <paramref name="lower"/>
    /// turn any drawing into pixels? Feeds the Q52 warning, which is shown
    /// before the merge and only when AI is enabled.
    /// </summary>
    public static bool WouldBakePixels(Layer upper, Layer lower)
    {
        var count = CelSpan(upper, lower);
        for (var t = 0; t < count; t++)
        {
            if (!IsExposureBoundary(upper, lower, t)) continue;
            var below = ExposureSheet.ExposedFrame(lower, t);
            var above = ExposureSheet.ExposedFrame(upper, t);
            if (above is null && below is null) continue;
            if (!PairIsLossless(upper, lower, below, above)) return true;
        }
        return false;
    }

    /// <summary>
    /// Rebuild <paramref name="lower"/>'s cels so it renders as the pair did,
    /// walking both exposure patterns. The caller removes
    /// <paramref name="upper"/> from the scene (inside the same undo step).
    /// </summary>
    public static void MergeDown(Scene scene, Layer upper, Layer lower)
    {
        var count = CelSpan(upper, lower);
        var merged = new List<Cel>(count);
        for (var t = 0; t < count; t++)
        {
            if (!IsExposureBoundary(upper, lower, t))
            {
                merged.Add(new Cel()); // both hold — the merged layer holds too
                continue;
            }
            var below = ExposureSheet.ExposedFrame(lower, t);
            var above = ExposureSheet.ExposedFrame(upper, t);
            merged.Add(new Cel { Frame = MergedFrame(scene, upper, lower, below, above, t) });
        }
        lower.Cels = merged;
        // An applying mask on the lower layer was baked into every merged
        // drawing above, so keeping it would carve the same shape twice. A
        // disabled mask was not baked and stays, still re-enableable — though
        // it will then carve the merged content.
        if (lower.IsMasked) lower.Mask = null;
        // Same for a live stack: a pair with one always bakes (B286), so the
        // merged pixels already carry the filter everywhere and keeping the
        // stack would apply it twice. A fully disabled stack was not baked
        // and stays, like a disabled mask.
        if (LiveStack(lower) is not null) lower.Effects = null;
    }

    /// <summary>Cels to walk: every index either layer keys, and at least one.</summary>
    private static int CelSpan(Layer upper, Layer lower) =>
        Math.Max(1, Math.Max(upper.Cels.Count, lower.Cels.Count));

    /// <summary>
    /// Does the composite of the two layers change at index <paramref name="t"/>?
    /// It can only change where one of them keys a new drawing; everywhere else
    /// the merged layer can hold, which is what keeps a merge of two held
    /// drawings from exploding into per-frame copies.
    /// </summary>
    private static bool IsExposureBoundary(Layer upper, Layer lower, int t) =>
        t == 0
        || ExposureSheet.FrameAtExactIndex(upper, t) is not null
        || ExposureSheet.FrameAtExactIndex(lower, t) is not null;

    private static Frame? MergedFrame(
        Scene scene, Layer upper, Layer lower, Frame? below, Frame? above, int t)
    {
        if (above is null && below is null) return null;
        return PairIsLossless(upper, lower, below, above)
            ? ConcatenatedFrame(below, above)
            : BakedFrame(scene, upper, lower, below, above, t);
    }

    /// <summary>
    /// Can this pair merge by appending the upper drawing's strokes after the
    /// lower's, with the render staying identical?
    /// </summary>
    private static bool PairIsLossless(Layer upper, Layer lower, Frame? below, Frame? above)
    {
        // A mask on the lower layer is baked into every merged drawing —
        // including the ones the upper layer leaves untouched — because the
        // merge clears the mask afterwards, and a cleared mask must already
        // be in the pixels everywhere.
        if (lower.IsMasked) return false;
        // A live effect stack is applied at composite time and strokes
        // cannot carry a filter — either layer having one forces the bake,
        // which runs the content → filter → carve → style pipeline (B286).
        if (LiveStack(upper) is not null || LiveStack(lower) is not null) return false;
        if (above is null) return true; // the lower drawing carries over untouched
        // The upper layer's mask carves its render, and its clip carves it by
        // the lower's alpha; strokes carry neither. (A pair clipped to the
        // same deeper base keeps clipping after the merge, so that carve is
        // not baked and does not force one.)
        if (upper.IsMasked || (upper.IsClipped && !lower.IsClipped)) return false;
        // A blend or opacity on the upper layer is applied at composite time;
        // its strokes carry neither, so they cannot reproduce it from inside
        // the merged layer.
        if (upper.BlendMode != LayerBlendMode.Normal || upper.Opacity < 1) return false;
        if (below is null)
        {
            // Nothing underneath: the upper drawing carries over as-is, and
            // even its erasers keep meaning what they meant.
            return true;
        }
        // A baseline can only sit at the bottom of a frame, and the bottom is
        // where the lower layer's content now lives.
        if (above.HasBaseline) return false;
        // Placements render over every stroke of their frame. The lower
        // drawing's symbols used to sit under the upper layer's marks; adding
        // strokes above them would reorder the stack.
        if (below.HasPlacements && above.Strokes.Count > 0) return false;
        foreach (var stroke in above.Strokes)
        {
            // Anything that reads or removes what is underneath changes
            // meaning when "underneath" gains the lower layer's marks.
            if (stroke.Tool is ToolKind.Eraser or ToolKind.ClearRegion) return false;
            if (stroke.AlphaLocked) return false;
            if (stroke.Brush.Kind == BrushKind.Smudge) return false;
        }
        return true;
    }

    /// <summary>
    /// The layer's stack when it filters its own content — null for an
    /// ordinary layer and for an adjustment layer, whose stack reads the
    /// backdrop and never survives a content merge.
    /// </summary>
    private static Core.Effects.EffectStack? LiveStack(Layer layer) =>
        layer.HasLiveEffects && !layer.IsAdjustment ? layer.Effects : null;

    /// <summary>Redraw a materialized drawing through a filter, in place; null is identity.</summary>
    private static void ApplyFilter(SKBitmap target, SKImageFilter? filter)
    {
        if (filter is null) return;
        using var source = target.Copy();
        using var canvas = new SKCanvas(target);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { ImageFilter = filter };
        canvas.DrawBitmap(source, 0, 0, paint);
        canvas.Flush();
    }

    /// <summary>
    /// Apply a painted mask to a materialized drawing in place: the drawing
    /// keeps its pixels where the mask's strokes have coverage (or loses them
    /// there, inverted) — the same DstIn/DstOut the compositor applies live.
    /// </summary>
    private static void CarveByMask(SKBitmap target, LayerMask mask, Scene scene)
    {
        using var coverage = FrameRasterizer.Materialize(
            mask.Frame, scene.Width, scene.Height);
        Carve(target, coverage, mask.IsInverted);
    }

    private static void Carve(SKBitmap target, SKBitmap coverage, bool inverted)
    {
        using var canvas = new SKCanvas(target);
        using var paint = new SKPaint
        {
            BlendMode = inverted ? SKBlendMode.DstOut : SKBlendMode.DstIn,
        };
        canvas.DrawBitmap(coverage, 0, 0, paint);
        canvas.Flush();
    }

    /// <summary>Lower drawing's content with the upper drawing's stamped after it.</summary>
    private static Frame ConcatenatedFrame(Frame? below, Frame? above)
    {
        var frame = new Frame
        {
            Role = below?.Role ?? above!.Role,
            PngBase64 = below?.PngBase64,
            Chart = Copy(below?.Chart) ?? Copy(above?.Chart),
            Ai = below?.Ai ?? above?.Ai,
        };
        // Fresh stroke ids: a drawing held on one layer while the other keys
        // gets merged more than once, and ids key caches across the document.
        if (below is not null) frame.Strokes.AddRange(below.Strokes.Select(s => s.Clone()));
        if (above is not null) frame.Strokes.AddRange(above.Strokes.Select(s => s.Clone()));
        frame.Placements = ConcatPlacements(below, above);
        (frame.Anchors, frame.Shapes) = UnionMarkers(below, above);
        return frame;
    }

    /// <summary>
    /// Both drawings rendered and composited through the upper layer's blend
    /// and opacity, kept as the merged frame's pixel baseline.
    /// </summary>
    private static Frame BakedFrame(
        Scene scene, Layer upper, Layer lower, Frame? below, Frame? above, int t)
    {
        using var bitmap = below is null
            ? NewCanvas(scene)
            : FrameRasterizer.Materialize(below, scene.Width, scene.Height, celIndex: t);
        // The clip base is the lower's content and mask, never its filters —
        // the compositor's shapes resolve from frames the same way — so it
        // is taken before the lower's own pipeline runs (B286).
        SKBitmap? clipBase = null;
        if (above is not null && upper.IsClipped && !lower.IsClipped)
        {
            clipBase = bitmap.Copy();
            if (lower.Mask is { } clipMask && clipMask.Applies)
            {
                CarveByMask(clipBase, clipMask, scene);
            }
        }
        // The lower layer's own pipeline, the compositor's order (Q155):
        // content → filter effects → mask carve → styles.
        ApplyFilter(bitmap, Effects.EffectRegistry.FilterFor(LiveStack(lower), t));
        if (lower.Mask is { } lowerMask && lowerMask.Applies)
        {
            CarveByMask(bitmap, lowerMask, scene);
        }
        ApplyFilter(bitmap, Effects.EffectRegistry.StyleFor(LiveStack(lower), t));
        if (above is not null)
        {
            using var top = FrameRasterizer.Materialize(above, scene.Width, scene.Height, celIndex: t);
            ApplyFilter(top, Effects.EffectRegistry.FilterFor(LiveStack(upper), t));
            if (upper.Mask is { } upperMask && upperMask.Applies)
            {
                CarveByMask(top, upperMask, scene);
            }
            // The upper layer clipped to the lower: its pixels only ever
            // showed where the lower (mask included, applied just above) had
            // content — so carve before compositing, against the lower's
            // render as it stands, which is exactly what the compositor
            // intersected. Not when both are clipped to a deeper base: that
            // carve survives the merge at composite time.
            if (clipBase is not null)
            {
                Carve(top, clipBase, inverted: false);
            }
            ApplyFilter(top, Effects.EffectRegistry.StyleFor(LiveStack(upper), t));
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint
            {
                BlendMode = BlendModes.ToSkia(upper.BlendMode),
                Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(upper.Opacity, 0, 1) * 255)),
            };
            canvas.DrawBitmap(top, 0, 0, paint);
            canvas.Flush();
        }
        clipBase?.Dispose();
        var frame = new Frame
        {
            Role = below?.Role ?? above!.Role,
            PngBase64 = PngCodec.Encode(bitmap),
            Chart = Copy(below?.Chart) ?? Copy(above?.Chart),
            Ai = below?.Ai ?? above?.Ai,
        };
        // Placements are baked into the pixels above; carrying them as live
        // placements too would draw every symbol twice.
        (frame.Anchors, frame.Shapes) = UnionMarkers(below, above);
        return frame;
    }

    private static SKBitmap NewCanvas(Scene scene)
    {
        var bitmap = new SKBitmap(new SKImageInfo(
            scene.Width, scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Transparent);
        return bitmap;
    }

    private static List<SymbolPlacement>? ConcatPlacements(Frame? below, Frame? above)
    {
        if (!(below?.HasPlacements ?? false) && !(above?.HasPlacements ?? false)) return null;
        var placements = new List<SymbolPlacement>();
        if (below?.Placements is { } b) placements.AddRange(b.Select(p => p.Clone()));
        if (above?.Placements is { } a) placements.AddRange(a.Select(p => p.Clone()));
        return placements;
    }

    /// <summary>
    /// Anchors and hitboxes from both drawings, the upper winning an id clash
    /// (ids are generated, so a clash means the drawings share provenance and
    /// the upper is the later edit).
    /// </summary>
    private static (Dictionary<string, AnchorPoint>?, Dictionary<string, ShapeBox>?) UnionMarkers(
        Frame? below, Frame? above)
    {
        var anchors = Union(below?.Anchors, above?.Anchors);
        var shapes = Union(below?.Shapes, above?.Shapes);
        return (anchors, shapes);
    }

    private static Dictionary<string, T>? Union<T>(
        Dictionary<string, T>? first, Dictionary<string, T>? second)
    {
        if (first is null && second is null) return null;
        var union = first is null ? [] : new Dictionary<string, T>(first);
        if (second is not null)
        {
            foreach (var (key, value) in second) union[key] = value;
        }
        return union;
    }

    private static List<double>? Copy(List<double>? chart) => chart is null ? null : [.. chart];
}
