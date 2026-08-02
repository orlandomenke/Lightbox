namespace Lightbox.Core.Documents;

/// <summary>
/// Root of a Lightbox document. Everything below this is plain JSON —
/// that is the "AI-native" contract: an LLM can read and write any part
/// of a document.
/// </summary>
public sealed class Doc
{
    public int Version { get; set; } = 1;

    public Scene Scene { get; set; } = new();

    /// <summary>Character sheets: reference art outside the timeline.</summary>
    public List<ReferenceSheet> ReferenceSheets { get; set; } = [];

    /// <summary>
    /// Custom brush tip shapes (id → grayscale PNG, base64). Strokes reference
    /// them by <see cref="BrushSettings.TipId"/>, so a document re-renders
    /// with no external resources.
    /// </summary>
    public Dictionary<string, string> BrushTips { get; set; } = [];

    /// <summary>
    /// Selections that strokes were painted under (id → region). Referenced by
    /// <see cref="Stroke.ClipId"/> so clipped strokes re-render identically
    /// from the document alone.
    /// </summary>
    public Dictionary<string, ClipRegion> ClipRegions { get; set; } = [];

    /// <summary>
    /// The document's colour palettes. Per document rather than per
    /// application: a character's palette is part of the character, and one
    /// living in app settings could not travel with the file.
    /// </summary>
    public List<Palette> Palettes { get; set; } = [];

    /// <summary>
    /// The folders those palettes are filed under, once there are any.
    /// </summary>
    /// <remarks>
    /// Null rather than empty, and absent from the file until a folder is
    /// made — the same rule the camera follows. A document with three palettes
    /// and no filing system should not carry the machinery for one.
    /// </remarks>
    public List<PaletteFolder>? PaletteFolders { get; set; }

    /// <summary>
    /// Gradients referenced by <see cref="Stroke.GradientId"/>, keyed by id —
    /// the same arrangement as clip regions and brush tips, so a reload
    /// re-renders from the document alone.
    /// </summary>
    public Dictionary<string, Gradient> Gradients { get; set; } = [];
}

/// <summary>A recorded selection: closed contours (even-odd) plus edge feather.</summary>
public sealed class ClipRegion
{
    public List<List<StrokePoint>> Contours { get; set; } = [];

    /// <summary>Gaussian edge softness in pixels (0 = hard edge).</summary>
    public double Feather { get; set; }
}
