using Lightbox.Core.Documents;
using Lightbox.Core.Effects;
using Lightbox.Raster.Effects;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// The effect half of what <see cref="LayerShapes"/> is for shapes: one place
/// that answers how a layer's stacks reach a pass list, asked by every
/// compositor that builds <see cref="RenderPass"/>es by hand (exporters, the
/// MCP render, the fill's sampling composite, the navigator) so an effect
/// cannot look different in two of them. The canvas publish asks the same
/// questions through <see cref="ScenePassBuilder"/>'s specs.
/// </summary>
public static class EffectPasses
{
    /// <summary>
    /// Whether anything in the document filters anything right now — the
    /// tile path's document-level gate, beside the camera's.
    /// </summary>
    public static bool AnyLive(Scene scene) =>
        (scene.Effects is { } fx && fx.AppliesAnything)
        || scene.Layers.Exists(l => l.HasLiveEffects);

    /// <summary>
    /// The layer's own stack as a filter for its content pass, or null for
    /// every ordinary layer — and for an adjustment layer, whose stack
    /// belongs to the backdrop, not to content it does not have.
    /// </summary>
    public static SKImageFilter? SelfFilter(Layer layer, int frame) =>
        layer.HasLiveEffects && !layer.IsAdjustment
            ? EffectRegistry.FilterFor(layer.Effects, frame)
            : null;

    /// <summary>
    /// The layer's styles as a filter for the group outside its mask carve
    /// (Q155) — glow, stroke, shadow, bevel decorate the carved silhouette —
    /// or null for a layer with none.
    /// </summary>
    public static SKImageFilter? SelfStyle(Layer layer, int frame) =>
        layer.HasLiveEffects && !layer.IsAdjustment
            ? EffectRegistry.StyleFor(layer.Effects, frame)
            : null;

    /// <summary>
    /// The adjustment pass for scene.Layers[<paramref name="layerIndex"/>],
    /// or null when it currently draws nothing (no live stack, hidden, or a
    /// clipped adjustment over an empty base).
    /// </summary>
    public static RenderPass? AdjustmentPass(
        Scene scene, int layerIndex, int frameIndex, FrameBitmapCache cache) =>
        AdjustmentPass(
            scene.Layers, scene.IsLayerVisible, layerIndex, frameIndex, cache,
            scene.Width, scene.Height);

    /// <summary>The layer-list form, for the compositors <see cref="LayerShapes"/> already serves that way.</summary>
    public static RenderPass? AdjustmentPass(
        IReadOnlyList<Layer> layers, Func<Layer, bool> visible, int layerIndex,
        int frameIndex, FrameBitmapCache cache, int width, int height)
    {
        var layer = layers[layerIndex];
        if (!layer.HasLiveEffects) return null;
        var shapes = LayerShapes.For(layers, visible, layerIndex, frameIndex);
        if (shapes is { Count: 0 }) return null;
        return new RenderPass(
            null, null, layer.Opacity,
            Shapes: LayerShapes.Resolve(shapes, cache, width, height, frameIndex),
            AdjustStack: layer.Effects, EffectFrame: frameIndex);
    }

    /// <summary>
    /// The scene stack as the final pass — the whole composite through the
    /// grade, before the camera draw — or null for the document that never
    /// grades, which must not pay for one.
    /// </summary>
    public static RenderPass? SceneStackPass(Scene scene, int frameIndex) =>
        scene.Effects is { } fx && fx.AppliesAnything
            ? new RenderPass(null, null, 1.0, AdjustStack: fx, EffectFrame: frameIndex)
            : null;
}
