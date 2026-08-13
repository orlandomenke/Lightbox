namespace Lightbox.Core.Projects;

/// <summary>
/// How an asset kind announces itself: a designation an artist reads, and a
/// glyph they recognise before reading.
/// </summary>
/// <remarks>
/// <para>
/// <b>One registry, because there were about to be three.</b> The docker's
/// declaration labels, the project window's asset chips and the Assets tab all
/// need to say "this is a palette" — and each writing its own switch is how the
/// same asset ends up wearing two names and no icon. Model code for the reason
/// Q29 gave about the tree: the docker and the window are two surfaces onto one
/// project, and anything both need to <em>know</em> lives here.
/// </para>
/// <para>
/// <b>The designation is automatic, not authored.</b> A folder's glyph is the
/// artist's (Q38); an asset's is not, on purpose — the whole point is that a
/// palette is recognisable as a palette wherever it appears, which only works
/// if nothing can rename it per row.
/// </para>
/// </remarks>
public static class AssetKinds
{
    /// <summary>The word beside the asset's name — "Palette", "Brush tip".</summary>
    public static string LabelOf(string kind) => kind switch
    {
        PaletteScopes.Kind => "Palette",
        GradientScopes.Kind => "Gradient",
        GuideScopes.Kind => "Guides",
        SymbolScopes.Kind => "Symbol",
        TipScopes.Kind => "Brush tip",
        TemplateScopes.Kind => "Template",
        ExportScopes.Kind => "Export",
        ReferenceScopes.Kind => "Reference",
        _ => kind,
    };

    /// <summary>
    /// The glyph in front of the asset's name. Unique per kind, so a row is
    /// recognisable before it is read.
    /// </summary>
    /// <remarks>
    /// ▤ for a reference sheet predates this registry — the docker chose it as
    /// "a sheet of panels, distinct from ▣'s single drawing" — so the registry
    /// adopts it rather than the docker changing. The rest follow the same
    /// rule the folder glyph picker uses: recognisable at 11px, no colour the
    /// theme has to own where a plain form exists.
    /// </remarks>
    public static string GlyphOf(string kind) => kind switch
    {
        PaletteScopes.Kind => "🎨",
        GradientScopes.Kind => "◧",
        GuideScopes.Kind => "📐",
        SymbolScopes.Kind => "❖",
        TipScopes.Kind => "🖌",
        TemplateScopes.Kind => "📄",
        ExportScopes.Kind => "📦",
        ReferenceScopes.Kind => "▤",
        _ => "▣",
    };
}
