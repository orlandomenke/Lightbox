using Lightbox.Core.Documents;
using Lightbox.Core.Effects;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// Draws a <see cref="StrokeGroup"/>: its strokes on a surface of their own,
/// then that surface put onto the drawing the way the layer it came from was
/// put onto the layer below (Q265, <c>docs/DESIGN-stroke-groups.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the merge's bake, run when the drawing is rendered instead of
/// once when it was merged.</b> The order is the compositor's (Q155) and
/// <see cref="LayerMerge"/>'s: content → filter effects → mask carve → clip
/// carve → styles → blend and opacity. Each child goes through
/// <see cref="BrushEngine.StampStroke"/>, so an eraser, an alpha-locked stroke,
/// a smudge and a nested group behave inside the group exactly as they did on
/// their layer — and reach nothing outside it, because the surface they are
/// stamped on holds nothing else.
/// </para>
/// <para>
/// <b>What it costs: a surface per group per render.</b> The scratch is the
/// size of the target, cleared, whatever part of it is wanted. A repaint of one
/// rectangle bounds the <em>strokes</em> (the children are handed the same
/// window the group was) and the composite (the caller's clip), not the
/// allocation.
/// </para>
/// </remarks>
internal static class StrokeGroupRenderer
{
    /// <summary>Stamp one group onto <paramref name="target"/>. Parameters as <see cref="BrushEngine.StampStroke"/>.</summary>
    /// <param name="targetPixels">
    /// The pixels <paramref name="target"/> draws into, which a clipped group
    /// is carved by. Every frame render hands them over. Without them a
    /// clipped group at Normal blend still lands exactly (source-atop is the
    /// same picture); one with another blend lands unclipped, because the
    /// carve has nothing to read.
    /// </param>
    /// <param name="entry">
    /// The stroke that holds the group. Its <see cref="Stroke.ClipId"/> is the
    /// selection the group is confined to — set when a region-limited
    /// transform cut it in two, as it does a single stroke.
    /// </param>
    public static void Stamp(
        SKCanvas target, Stroke entry, StrokeGroup group, SKImageInfo info, SKBitmap? targetPixels,
        double outputScale, SKBitmap? backdrop, SKPointI origin, SKRectI? window, int frameIndex)
    {
        if (group.Strokes.Count == 0 && string.IsNullOrEmpty(group.PngBase64)) return;

        var scaled = FrameRasterizer.Scaled(info, outputScale);
        using var scratch = new SKBitmap(scaled);
        using (var canvas = new SKCanvas(scratch))
        {
            canvas.Clear(SKColors.Transparent);
            FrameRasterizer.DrawBaseline(canvas, group.PngBase64, scaled, outputScale, origin);
            foreach (var child in group.Strokes)
            {
                BrushEngine.StampStroke(
                    canvas, child, info, scratch, outputScale: outputScale, backdrop: backdrop,
                    origin: origin, window: window, frameIndex: frameIndex);
            }
            canvas.Flush();
        }

        var effects = LiveEffects(group);
        LayerMerge.ApplyFilter(scratch, Effects.EffectRegistry.FilterFor(effects, frameIndex, (float)outputScale));
        if (group.Mask is { Applies: true } mask)
        {
            using var coverage = FrameRasterizer.Materialize(
                mask.Frame, info.Width, info.Height, outputScale, origin: origin);
            LayerMerge.Carve(scratch, coverage, mask.IsInverted);
        }
        var clipped = group.Clip == true;
        var blend = BlendModes.ToSkia(group.Blend ?? LayerBlendMode.Normal);
        if (clipped && targetPixels is not null)
        {
            // By what is on the drawing so far — the lower layer's result, as
            // the compositor carved a clipped layer by its base.
            LayerMerge.Carve(scratch, targetPixels, inverted: false);
        }
        else if (clipped && blend == SKBlendMode.SrcOver)
        {
            blend = SKBlendMode.SrcATop;
        }
        LayerMerge.ApplyFilter(scratch, Effects.EffectRegistry.StyleFor(effects, frameIndex, (float)outputScale));
        if (entry.ClipId is not null)
        {
            // Last, over the finished result: a selection cuts what is on the
            // page, styles and all — the pixels a flattened drawing would have
            // had there.
            using var canvas = new SKCanvas(scratch);
            BrushEngine.ApplyClip(
                canvas, entry, scaled, SKRectI.Create(0, 0, scaled.Width, scaled.Height), outputScale, origin);
            canvas.Flush();
        }

        using var paint = new SKPaint
        {
            BlendMode = blend,
            Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(group.Opacity ?? 1, 0, 1) * 255)),
        };
        target.DrawBitmap(scratch, 0, 0, paint);
    }

    private static EffectStack? LiveEffects(StrokeGroup group) =>
        group.Effects is { AppliesAnything: true } effects ? effects : null;

    /// <summary>
    /// Every pixel a group can touch: the union of what its strokes can, or
    /// the whole surface when it carries stored pixels or a filter, either of
    /// which can land anywhere. A mask and a clip only take away.
    /// </summary>
    public static SKRectI? Reach(StrokeGroup group, SKImageInfo info, SKPointI origin)
    {
        if (!string.IsNullOrEmpty(group.PngBase64) || LiveEffects(group) is not null)
        {
            return SKRectI.Create(0, 0, info.Width, info.Height);
        }
        SKRectI? union = null;
        foreach (var child in group.Strokes)
        {
            if (BrushEngine.CommitRegion(child, info, origin) is not { } reach) continue;
            union = union is { } so ? SKRectI.Union(so, reach) : reach;
        }
        return union;
    }

    /// <summary>Where a group is, unclamped: the union of where its strokes are.</summary>
    public static SKRectI? ReachBounds(StrokeGroup group)
    {
        SKRectI? union = null;
        foreach (var child in group.Strokes)
        {
            if (BrushEngine.ReachBounds(child) is not { } reach) continue;
            union = union is { } so ? SKRectI.Union(so, reach) : reach;
        }
        return union;
    }

    /// <summary>
    /// Does this stroke — or, for a group, anything inside it at any depth —
    /// read the pixels it sits on? Smudge, blur and a mixing brush do, and
    /// every bounded render path has to refuse them: outside the rectangle
    /// being rebuilt, the surface is not what those strokes saw.
    /// </summary>
    public static bool ReadsPixels(Stroke stroke)
    {
        if (stroke.Group is { } group)
        {
            foreach (var child in group.Strokes)
                if (ReadsPixels(child)) return true;
            return false;
        }
        return stroke.Brush.Kind is BrushKind.Smudge or BrushKind.Blur || stroke.Brush.Mixing is not null;
    }

    /// <summary>Does anything in the list, through its groups, sample the layers beneath it live?</summary>
    public static bool SamplesLive(List<Stroke> strokes)
    {
        foreach (var stroke in strokes)
        {
            if (stroke.Group is { } group ? SamplesLive(group.Strokes) : stroke.Brush.SampleSource == SampleSource.AllLayersLive)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Is there a group here, at any depth, whose result is not bounded by its
    /// strokes — stored pixels or a filter? A region repaint clears a rectangle
    /// and replays into it; neither of these can be replayed into a rectangle.
    /// </summary>
    public static bool HasUnboundedGroup(List<Stroke> strokes)
    {
        foreach (var stroke in strokes)
        {
            if (stroke.Group is not { } group) continue;
            if (!string.IsNullOrEmpty(group.PngBase64) || LiveEffects(group) is not null) return true;
            if (HasUnboundedGroup(group.Strokes)) return true;
            if (group.Mask is { } mask && HasUnboundedGroup(mask.Frame.Strokes)) return true;
        }
        return false;
    }

    /// <summary>
    /// Does this drawing render differently depending on which cel shows it?
    /// A group with an effect that changes over time does: its parameters are
    /// read at the cel being drawn. Decides whether the timeline position
    /// is part of a cached render's identity, as a placed symbol's is.
    /// </summary>
    public static bool DependsOnCel(List<Stroke> strokes)
    {
        foreach (var stroke in strokes)
        {
            if (stroke.Group is not { } group) continue;
            if (ChangesOverTime(LiveEffects(group)) || DependsOnCel(group.Strokes)) return true;
        }
        return false;
    }

    /// <summary>
    /// Does this stack draw differently at different cels — a keyed parameter,
    /// or an effect seeded from the frame? A constant blur does not, and a
    /// drawing held for 24 cels under one is still one picture.
    /// </summary>
    private static bool ChangesOverTime(EffectStack? stack)
    {
        if (stack is null) return false;
        foreach (var use in stack.Uses)
        {
            if (!use.Applies) continue;
            if (Effects.EffectRegistry.Resolve(use.Kind) is { TimeSeeded: true }) return true;
            foreach (var param in use.Params.Values)
                if (param.IsAnimated) return true;
        }
        return false;
    }
}
