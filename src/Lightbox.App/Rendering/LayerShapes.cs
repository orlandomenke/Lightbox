using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using SkiaSharp;

namespace Lightbox.App.Rendering;

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
internal static class LayerShapes
{
    /// <summary>A shape described but not fetched: a frame whose render carves the pass.</summary>
    internal readonly record struct ShapeSpec(Frame Frame, bool Inverted);

    /// <summary>
    /// The shapes carving <paramref name="scene"/>.Layers[<paramref name="layerIndex"/>]
    /// at <paramref name="frameIndex"/>. Null for the unshaped layer — the
    /// path that existed before shapes did, and the one every ordinary layer
    /// must keep taking. An <b>empty</b> list means the layer contributes
    /// nothing at all this frame (a clipped layer over an empty or hidden
    /// base): skip the pass.
    /// </summary>
    internal static List<ShapeSpec>? For(Scene scene, int layerIndex, int frameIndex) =>
        For(scene.Layers, scene.IsLayerVisible, layerIndex, frameIndex);

    /// <summary>
    /// The layer-list form, for compositors that hold layers without a scene
    /// around them (a reference view of another document).
    /// </summary>
    internal static List<ShapeSpec>? For(
        IReadOnlyList<Layer> layers, Func<Layer, bool> visible, int layerIndex, int frameIndex)
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

        return shapes;
    }

    /// <summary>
    /// The clipping base: the first layer beneath that is not itself clipped,
    /// or null for a clipped layer with nothing to clip to — which renders
    /// unclipped rather than vanishing, so dragging a layer to the bottom of
    /// the stack degrades visibly instead of silently.
    /// </summary>
    internal static Layer? BaseOf(IReadOnlyList<Layer> layers, int layerIndex)
    {
        for (var i = layerIndex - 1; i >= 0; i--)
        {
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
    internal static bool Carves(Scene scene, int layerIndex)
    {
        var layer = scene.Layers[layerIndex];
        return layer.IsMasked
            || (layer.IsClipped && BaseOf(scene.Layers, layerIndex) is not null);
    }

    /// <summary>The described shapes with their fetches done, ready for a <see cref="RenderPass"/>.</summary>
    internal static List<PassShape>? Resolve(
        List<ShapeSpec>? specs, FrameBitmapCache cache, int width, int height, int celIndex = 0)
    {
        if (specs is null || specs.Count == 0) return null;
        var shapes = new List<PassShape>(specs.Count);
        foreach (var spec in specs)
        {
            shapes.Add(new PassShape(
                cache.Get(spec.Frame, width, height, celIndex: celIndex), spec.Inverted));
        }
        return shapes;
    }
}
