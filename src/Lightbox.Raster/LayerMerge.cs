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
            if (!PairIsLossless(upper, below, above)) return true;
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
            merged.Add(new Cel { Frame = MergedFrame(scene, upper, below, above, t) });
        }
        lower.Cels = merged;
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
        Scene scene, Layer upper, Frame? below, Frame? above, int t)
    {
        if (above is null && below is null) return null;
        return PairIsLossless(upper, below, above)
            ? ConcatenatedFrame(below, above)
            : BakedFrame(scene, upper, below, above, t);
    }

    /// <summary>
    /// Can this pair merge by appending the upper drawing's strokes after the
    /// lower's, with the render staying identical?
    /// </summary>
    private static bool PairIsLossless(Layer upper, Frame? below, Frame? above)
    {
        if (above is null) return true; // the lower drawing carries over untouched
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
        Scene scene, Layer upper, Frame? below, Frame? above, int t)
    {
        using var bitmap = below is null
            ? NewCanvas(scene)
            : FrameRasterizer.Materialize(below, scene.Width, scene.Height, celIndex: t);
        if (above is not null)
        {
            using var top = FrameRasterizer.Materialize(above, scene.Width, scene.Height, celIndex: t);
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint
            {
                BlendMode = BlendModes.ToSkia(upper.BlendMode),
                Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(upper.Opacity, 0, 1) * 255)),
            };
            canvas.DrawBitmap(top, 0, 0, paint);
            canvas.Flush();
        }
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
