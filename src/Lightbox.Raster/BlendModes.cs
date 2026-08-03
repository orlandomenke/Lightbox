using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// The one translation from a document's blend mode to Skia's.
/// </summary>
/// <remarks>
/// It lives here rather than in the app's compositor because two things need
/// it now: a layer landing on the frame, and a stroke landing on the layer. Two
/// copies of a sixteen-way switch is two chances for a brush's Multiply and a
/// layer's Multiply to stop meaning the same thing.
/// </remarks>
public static class BlendModes
{
    /// <summary>Photoshop-style blend modes map 1:1 onto Skia's.</summary>
    public static SKBlendMode ToSkia(LayerBlendMode mode) => mode switch
    {
        LayerBlendMode.Multiply => SKBlendMode.Multiply,
        LayerBlendMode.Screen => SKBlendMode.Screen,
        LayerBlendMode.Overlay => SKBlendMode.Overlay,
        LayerBlendMode.Darken => SKBlendMode.Darken,
        LayerBlendMode.Lighten => SKBlendMode.Lighten,
        LayerBlendMode.ColorDodge => SKBlendMode.ColorDodge,
        LayerBlendMode.ColorBurn => SKBlendMode.ColorBurn,
        LayerBlendMode.HardLight => SKBlendMode.HardLight,
        LayerBlendMode.SoftLight => SKBlendMode.SoftLight,
        LayerBlendMode.Difference => SKBlendMode.Difference,
        LayerBlendMode.Exclusion => SKBlendMode.Exclusion,
        LayerBlendMode.Hue => SKBlendMode.Hue,
        LayerBlendMode.Saturation => SKBlendMode.Saturation,
        LayerBlendMode.Color => SKBlendMode.Color,
        LayerBlendMode.Luminosity => SKBlendMode.Luminosity,
        _ => SKBlendMode.SrcOver,
    };

    /// <summary>
    /// How a finished stroke lands on the layer it was painted on.
    /// </summary>
    /// <remarks>
    /// The eraser is not a blend mode and must not become one: it takes alpha
    /// away, which no separable blend does, so it keeps <c>DstOut</c> whatever
    /// the brush says. Every other tool asks the brush, and a brush that never
    /// set one composites with <c>SrcOver</c> exactly as it always did.
    /// </remarks>
    public static SKBlendMode ForStroke(Stroke stroke) =>
        stroke.Tool == ToolKind.Eraser ? SKBlendMode.DstOut : ToSkia(stroke.Brush.BlendOrNormal);
}
