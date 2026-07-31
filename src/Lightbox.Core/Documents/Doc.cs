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
}
