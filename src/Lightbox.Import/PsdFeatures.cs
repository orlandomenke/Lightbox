namespace Lightbox.Import;

/// <summary>
/// The Photoshop layer features Lightbox has no model for, keyed by the tagged
/// block that announces each one.
/// </summary>
/// <remarks>
/// <para>
/// Every entry is here because <b>ignoring it would change what the drawing looks
/// like</b>. That is the whole test for membership: an adjustment layer recolours
/// everything beneath it, a mask hides part of its own layer, a layer style adds
/// pixels that are not in any channel. Drop any of them and the import is a
/// picture the artist never saved.
/// </para>
/// <para>
/// Contrast the things deliberately <em>not</em> here, which are read and mapped
/// rather than refused: layer id, colour label, protection flags, the Unicode
/// name, the folder brackets, and metadata blocks. Losing those loses
/// bookkeeping, not the image.
/// </para>
/// <para>
/// The remedy on each is a Photoshop menu path, because the refusal exists to be
/// acted on. Two shapes recur: <em>rasterize</em>, for a layer whose pixels
/// Photoshop can bake in place, and <em>merge down</em>, for an adjustment whose
/// effect belongs to the layers underneath it.
/// </para>
/// </remarks>
internal static class PsdFeatures
{
    private const string Rasterize = "rasterize it (Layer ▸ Rasterize ▸ Layer)";
    private const string MergeDown = "merge it into the layer below (Layer ▸ Merge Down)";

    public static readonly Dictionary<string, (string Feature, string Remedy)> Unsupported = new()
    {
        // Generated content: no channel data to read at all.
        ["SoCo"] = ("A solid-colour fill layer", Rasterize),
        ["GdFl"] = ("A gradient fill layer", Rasterize),
        ["PtFl"] = ("A pattern fill layer", Rasterize),

        // Live content Photoshop re-renders on demand.
        ["TySh"] = ("A text layer", "rasterize it (Layer ▸ Rasterize ▸ Type)"),
        ["tySh"] = ("A text layer", "rasterize it (Layer ▸ Rasterize ▸ Type)"),
        ["SoLd"] = ("A smart object", "rasterize it (Layer ▸ Rasterize ▸ Smart Object)"),
        ["SoLE"] = ("A linked smart object", "rasterize it (Layer ▸ Rasterize ▸ Smart Object)"),
        ["PlLd"] = ("A placed object", Rasterize),

        // Pixels added at composite time, present in no channel.
        ["lfx2"] = ("Layer effects", "rasterize them (Layer ▸ Rasterize ▸ Layer Style)"),
        ["lrFX"] = ("Layer effects", "rasterize them (Layer ▸ Rasterize ▸ Layer Style)"),
        ["lfxs"] = ("Layer effects", "rasterize them (Layer ▸ Rasterize ▸ Layer Style)"),

        // Shape-driven visibility.
        ["vmsk"] = ("A vector mask", "apply it (Layer ▸ Rasterize ▸ Vector Mask)"),
        ["vsms"] = ("A vector mask", "apply it (Layer ▸ Rasterize ▸ Vector Mask)"),

        // Adjustment layers, which recolour everything beneath them.
        ["levl"] = ("A Levels adjustment layer", MergeDown),
        ["curv"] = ("A Curves adjustment layer", MergeDown),
        ["brit"] = ("A Brightness/Contrast adjustment layer", MergeDown),
        ["blnc"] = ("A Color Balance adjustment layer", MergeDown),
        ["hue "] = ("A Hue/Saturation adjustment layer", MergeDown),
        ["hue2"] = ("A Hue/Saturation adjustment layer", MergeDown),
        ["selc"] = ("A Selective Color adjustment layer", MergeDown),
        ["thrs"] = ("A Threshold adjustment layer", MergeDown),
        ["nvrt"] = ("An Invert adjustment layer", MergeDown),
        ["post"] = ("A Posterize adjustment layer", MergeDown),
        ["mixr"] = ("A Channel Mixer adjustment layer", MergeDown),
        ["grdm"] = ("A Gradient Map adjustment layer", MergeDown),
        ["phfl"] = ("A Photo Filter adjustment layer", MergeDown),
        ["expA"] = ("An Exposure adjustment layer", MergeDown),
        ["vibA"] = ("A Vibrance adjustment layer", MergeDown),
        ["blwh"] = ("A Black & White adjustment layer", MergeDown),
        ["clrL"] = ("A Color Lookup adjustment layer", MergeDown),
    };
}
