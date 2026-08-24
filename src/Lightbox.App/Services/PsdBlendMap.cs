using Lightbox.Core.Documents;
using Lightbox.Import;

namespace Lightbox.App.Services;

/// <summary>
/// Photoshop's four-character blend keys to <see cref="LayerBlendMode"/>.
/// </summary>
/// <remarks>
/// <para>
/// The other half of <see cref="PsdBlend"/>, which lives in
/// <c>Lightbox.Import</c> and cannot see the document model. The reader decides
/// which keys it will emit; this decides what each one becomes. The two can
/// drift, so <c>PsdImportTests</c> walks <see cref="PsdBlend.SupportedKeys"/> and
/// asserts every one of them resolves here — a mode added to the reader without a
/// home in the model fails a test rather than quietly compositing as Normal.
/// </para>
/// <para>
/// <c>pass</c> maps to Normal because Photoshop's pass-through means "do not
/// isolate this group", which is already how a Lightbox folder behaves.
/// </para>
/// </remarks>
public static class PsdBlendMap
{
    /// <summary>The Lightbox mode for a Photoshop key, or null if there is none.</summary>
    public static LayerBlendMode? For(string key) => key switch
    {
        "norm" or "pass" => LayerBlendMode.Normal,
        "mul " => LayerBlendMode.Multiply,
        "scrn" => LayerBlendMode.Screen,
        "over" => LayerBlendMode.Overlay,
        "dark" => LayerBlendMode.Darken,
        "lite" => LayerBlendMode.Lighten,
        "div " => LayerBlendMode.ColorDodge,
        "idiv" => LayerBlendMode.ColorBurn,
        "hLit" => LayerBlendMode.HardLight,
        "sLit" => LayerBlendMode.SoftLight,
        "diff" => LayerBlendMode.Difference,
        "smud" => LayerBlendMode.Exclusion,
        "hue " => LayerBlendMode.Hue,
        "sat " => LayerBlendMode.Saturation,
        "colr" => LayerBlendMode.Color,
        "lum " => LayerBlendMode.Luminosity,
        _ => null,
    };
}
