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
    /// Imported paper textures (id → PNG, base64), referenced by
    /// <see cref="BrushSettings.TextureId"/>.
    /// </summary>
    /// <remarks>
    /// Null until a texture is imported, and absent from the file until then —
    /// unlike <see cref="BrushTips"/>, which predates the rule and writes an
    /// empty object. Same reasoning as tips otherwise: the pixels travel with
    /// the document so it renders with nothing else installed.
    /// </remarks>
    public Dictionary<string, string>? Textures { get; set; }

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

    /// <summary>
    /// Symbols this document carries itself, keyed by id — or null, which is
    /// the ordinary case.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A symbol normally lives on the <em>project</em>, above the animations
    /// that place it; that is what makes editing the sword once change every
    /// animation holding it. This key exists for the one moment that stops
    /// being true: <c>ProjectIo.Flatten</c> copies the symbols an exported
    /// document references into it, so the file that leaves the application
    /// renders from its own record. It is the same job
    /// <see cref="Palettes"/> and <see cref="Gradients"/> already do, and the
    /// place invariant 1 is repaid.
    /// </para>
    /// <para>
    /// Nothing in the app writes this while a document is being drawn, so a
    /// working file carries no symbol key at all.
    /// </para>
    /// </remarks>
    public Dictionary<string, Symbol>? Symbols { get; set; }

    /// <summary>Whether this document carries symbols of its own. Derived; not serialized.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasSymbols => Symbols is { Count: > 0 };

}

/// <summary>A recorded selection: closed contours (even-odd) plus edge feather.</summary>
public sealed class ClipRegion
{
    public List<List<StrokePoint>> Contours { get; set; } = [];

    /// <summary>Gaussian edge softness in pixels (0 = hard edge).</summary>
    public double Feather { get; set; }
}
