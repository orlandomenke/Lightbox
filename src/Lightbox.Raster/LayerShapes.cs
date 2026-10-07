using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// The one place that answers "what carves this layer's output" — its own
/// painted mask (Q147), and the clipping base beneath it. Every compositor
/// (canvas publish, exports, previews, the fill's sampling composite) asks
/// here, so a masked layer cannot look different in two of them.
/// </summary>
/// <remarks>
/// <para>
/// Two-phase like <see cref="ScenePassBuilder"/> itself: <see cref="For"/>
/// describes the shapes as frames without touching a pixel, and
/// <see cref="Resolve"/> fetches them through the frame cache — a mask's
/// drawing is an ordinary <see cref="Frame"/>, so it caches, invalidates and
/// re-renders through exactly the machinery cels already use.
/// </para>
/// <para>
/// <b>The clipping base is positional</b>: the first layer beneath that is
/// not itself clipped, so consecutive clipped layers share one base —
/// Photoshop's rule, and the reason <see cref="Layer.ClipToBelow"/> is a flag
/// rather than an id. A clipped layer whose base is hidden, or has nothing
/// exposed at this frame, shows nothing — which <see cref="For"/> reports as
/// an empty list so the caller can skip the pass outright rather than
/// compositing a fully carved bitmap.
/// </para>
/// </remarks>
public static class LayerShapes
{
    /// <summary>
    /// A shape described but not fetched: a frame whose render carves the pass.
    /// </summary>
    /// <param name="Carve">
    /// A mask carving <paramref name="Frame"/> before it counts as coverage —
    /// set only on a member of a folder's shape whose layer is masked.
    /// </param>
    /// <param name="Or">
    /// More coverage, unioned with this one before the carve (Q215): a
    /// folder's shape is everything beneath, not one layer, so it is the
    /// first member <em>or</em> any of the rest. Null on every other shape.
    /// </param>
    public readonly record struct ShapeSpec(
        Frame Frame, bool Inverted,
        Frame? Carve = null, bool CarveInverted = false,
        IReadOnlyList<ShapeSpec>? Or = null);

    /// <summary>
    /// The shapes carving <paramref name="scene"/>.Layers[<paramref name="layerIndex"/>]
    /// at <paramref name="frameIndex"/>. Null for the unshaped layer — the
    /// path that existed before shapes did, and the one every ordinary layer
    /// must keep taking. An <b>empty</b> list means the layer contributes
    /// nothing at all this frame (a clipped layer over an empty or hidden
    /// base): skip the pass.
    /// </summary>
    public static List<ShapeSpec>? For(Scene scene, int layerIndex, int frameIndex) =>
        For(scene.Layers, scene.IsLayerVisible, layerIndex, frameIndex, scene.LayerGroups);

    /// <summary>
    /// The layer-list form, for compositors that hold layers without a scene
    /// around them (a reference view of another document). Without
    /// <paramref name="folders"/> no folder keeps its layers inside — a
    /// reference view copies layers, not folders.
    /// </summary>
    public static List<ShapeSpec>? For(
        IReadOnlyList<Layer> layers, Func<Layer, bool> visible, int layerIndex, int frameIndex,
        IReadOnlyList<LayerGroup>? folders = null)
    {
        var layer = layers[layerIndex];
        List<ShapeSpec>? shapes = null;

        if (layer.IsMasked)
        {
            shapes = [new ShapeSpec(layer.Mask!.Frame, layer.Mask.IsInverted)];
        }

        if (layer.IsClipped && BaseOf(layers, layerIndex) is { } clipBase)
        {
            var baseFrame = visible(clipBase)
                ? ExposureSheet.ExposedFrame(clipBase, frameIndex)
                : null;
            if (baseFrame is null) return [];

            shapes ??= [];
            shapes.Add(new ShapeSpec(baseFrame, Inverted: false));
            // The base's own mask carves its render, so it must carve what
            // clips to it too, or the clipped layer would show over content
            // the base itself is hiding.
            if (clipBase.IsMasked)
            {
                shapes.Add(new ShapeSpec(clipBase.Mask!.Frame, clipBase.Mask.IsInverted));
            }
        }

        // Every folder this layer sits in that keeps its layers inside, from
        // the innermost out — nested shapes intersect, as nested clips would.
        if (folders is not null && layer.GroupId is not null && AnyFolderShape(folders))
        {
            var folder = FindFolder(folders, layer.GroupId);
            for (var depth = 0; folder is not null && depth < FolderTree.MaxDepth;
                 depth++, folder = FindFolder(folders, folder.ParentId))
            {
                var top = ShapeTopBelow(layers, folders, folder, layerIndex);
                if (top < 0) continue;
                if (FolderShape(layers, visible, folders, folder, top, frameIndex) is not { } shape)
                {
                    return []; // the whole shape is hidden or empty this frame
                }
                shapes ??= [];
                shapes.Add(shape);
            }
        }

        return shapes;
    }

    /// <summary>
    /// The folder whose shape carves <paramref name="scene"/>.Layers[<paramref name="layerIndex"/>]
    /// — the innermost one — or null. The docker's question: "is this layer
    /// being kept inside something, and what?"
    /// </summary>
    public static LayerGroup? FolderShapingOf(Scene scene, int layerIndex)
    {
        var layers = scene.Layers;
        var folders = scene.LayerGroups;
        if (layers[layerIndex].GroupId is null || !AnyFolderShape(folders)) return null;
        var folder = FindFolder(folders, layers[layerIndex].GroupId);
        for (var depth = 0; folder is not null && depth < FolderTree.MaxDepth;
             depth++, folder = FindFolder(folders, folder.ParentId))
        {
            if (ShapeTopBelow(layers, folders, folder, layerIndex) >= 0) return folder;
        }
        return null;
    }

    /// <summary>
    /// Whether a folder's shape has anything in it on this frame — false when
    /// every layer up to the shape is hidden, empty or an adjustment, which is
    /// when the layers above show nothing and the docker should say why.
    /// </summary>
    public static bool FolderShapeHasContent(Scene scene, LayerGroup folder, int frameIndex) =>
        FolderShapeOf(scene, folder, frameIndex) is not null;

    /// <summary>
    /// <paramref name="folder"/>'s shape on this frame, described — null when
    /// it has none or nothing in it shows. For the callers that bake it rather
    /// than composite it (merge-down).
    /// </summary>
    public static ShapeSpec? FolderShapeOf(Scene scene, LayerGroup folder, int frameIndex)
    {
        var top = scene.Layers.FindIndex(l => l.Id == folder.ShapeLayerId);
        return top >= 0 && IsWithin(scene.LayerGroups, scene.Layers[top], folder)
            ? FolderShape(scene.Layers, scene.IsLayerVisible, scene.LayerGroups, folder, top, frameIndex)
            : null;
    }

    private static bool AnyFolderShape(IReadOnlyList<LayerGroup> folders)
    {
        for (var i = 0; i < folders.Count; i++)
        {
            if (folders[i].ShapeLayerId is not null) return true;
        }
        return false;
    }

    private static LayerGroup? FindFolder(IReadOnlyList<LayerGroup> folders, string? id)
    {
        if (id is null) return null;
        for (var i = 0; i < folders.Count; i++)
        {
            if (folders[i].Id == id) return folders[i];
        }
        return null;
    }

    /// <summary>Whether a layer is inside <paramref name="folder"/> at any depth, bounded as the tree is.</summary>
    private static bool IsWithin(IReadOnlyList<LayerGroup> folders, Layer layer, LayerGroup folder)
    {
        var f = FindFolder(folders, layer.GroupId);
        for (var depth = 0; f is not null && depth < FolderTree.MaxDepth; depth++, f = FindFolder(folders, f.ParentId))
        {
            if (f.Id == folder.Id) return true;
        }
        return false;
    }

    /// <summary>
    /// The index of <paramref name="folder"/>'s shape layer when it sits
    /// beneath <paramref name="layerIndex"/> inside the folder, else -1 — the
    /// shape layer itself and everything under it are the shape, not carved by it.
    /// </summary>
    private static int ShapeTopBelow(
        IReadOnlyList<Layer> layers, IReadOnlyList<LayerGroup> folders, LayerGroup folder, int layerIndex)
    {
        if (folder.ShapeLayerId is not { } id) return -1;
        for (var i = layerIndex - 1; i >= 0 && IsWithin(folders, layers[i], folder); i--)
        {
            if (layers[i].Id == id) return i;
        }
        return -1;
    }

    /// <summary>
    /// The union of everything in <paramref name="folder"/> from its bottom up
    /// to <paramref name="top"/>, or null when none of it shows this frame.
    /// </summary>
    /// <remarks>
    /// A clipped member is left out when it has a base: its coverage is inside
    /// its base's already. Adjustment layers have no content to cover with.
    /// Each member's own mask carves its coverage, as it carves its render.
    /// </remarks>
    private static ShapeSpec? FolderShape(
        IReadOnlyList<Layer> layers, Func<Layer, bool> visible, IReadOnlyList<LayerGroup> folders,
        LayerGroup folder, int top, int frameIndex)
    {
        ShapeSpec? first = null;
        List<ShapeSpec>? rest = null;
        for (var i = top; i >= 0 && IsWithin(folders, layers[i], folder); i--)
        {
            var member = layers[i];
            if (member.IsAdjustment || !visible(member)) continue;
            if (member.IsClipped && BaseOf(layers, i) is not null) continue;
            if (ExposureSheet.ExposedFrame(member, frameIndex) is not { } frame) continue;
            var spec = member.IsMasked
                ? new ShapeSpec(frame, Inverted: false, member.Mask!.Frame, member.Mask.IsInverted)
                : new ShapeSpec(frame, Inverted: false);
            if (first is null) first = spec;
            else (rest ??= []).Add(spec);
        }
        return first is { } f ? f with { Or = rest } : null;
    }

    /// <summary>
    /// The clipping base: the first layer beneath that is not itself clipped,
    /// or null for a clipped layer with nothing to clip to — which renders
    /// unclipped rather than vanishing, so dragging a layer to the bottom of
    /// the stack degrades visibly instead of silently.
    /// </summary>
    /// <remarks>
    /// <b>The search stops at the folder (Q215).</b> Only a layer in the same
    /// folder can be the base: a clipped layer at the bottom of a folder has
    /// nothing to clip to, and a layer directly above a folder does not reach
    /// into it. A folder in the docker reads as a boundary, so it is one.
    /// </remarks>
    public static Layer? BaseOf(IReadOnlyList<Layer> layers, int layerIndex)
    {
        var folder = layers[layerIndex].GroupId;
        for (var i = layerIndex - 1; i >= 0; i--)
        {
            if (layers[i].GroupId != folder) return null;
            if (!layers[i].IsClipped) return layers[i];
        }
        return null;
    }

    /// <summary>
    /// Whether anything carves this layer at all — the allocation-free half
    /// of <see cref="For"/>, for callers that only gate on it (the tile
    /// warm-ahead runs per lookahead frame per layer during playback, and a
    /// discarded list there is a discarded list per tick).
    /// </summary>
    public static bool Carves(Scene scene, int layerIndex)
    {
        var layer = scene.Layers[layerIndex];
        return layer.IsMasked
            || (layer.IsClipped && BaseOf(scene.Layers, layerIndex) is not null)
            || FolderShapingOf(scene, layerIndex) is not null;
    }

    /// <summary>The described shapes with their fetches done, ready for a <see cref="RenderPass"/>.</summary>
    public static List<PassShape>? Resolve(
        List<ShapeSpec>? specs, FrameBitmapCache cache, int width, int height, int celIndex = 0)
    {
        if (specs is null || specs.Count == 0) return null;
        var shapes = new List<PassShape>(specs.Count);
        foreach (var spec in specs)
        {
            shapes.Add(Resolve(spec, cache, width, height, celIndex));
        }
        return shapes;
    }

    private static PassShape Resolve(
        in ShapeSpec spec, FrameBitmapCache cache, int width, int height, int celIndex)
    {
        List<PassShape>? or = null;
        if (spec.Or is { Count: > 0 } members)
        {
            or = new List<PassShape>(members.Count);
            foreach (var member in members) or.Add(Resolve(member, cache, width, height, celIndex));
        }
        return new PassShape(
            cache.Get(spec.Frame, width, height, celIndex: celIndex), spec.Inverted,
            Carve: spec.Carve is { } carve ? cache.Get(carve, width, height, celIndex: celIndex) : null,
            CarveInverted: spec.CarveInverted,
            Or: or);
    }
}
