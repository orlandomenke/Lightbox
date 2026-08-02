namespace Lightbox.Core.Documents;

/// <summary>
/// One brush stroke: the atomic unit of drawing, and the unit of exchange
/// with the AI. A stroke is pure data — geometry plus paint parameters —
/// so it can be serialized to JSON, interpolated, and re-rendered
/// deterministically.
/// </summary>
public sealed class Stroke
{
    public string Id { get; set; } = Ids.NewId("s");

    public ToolKind Tool { get; set; } = ToolKind.Brush;

    /// <summary>CSS-style hex color, e.g. "#1a1a1a".</summary>
    public string Color { get; set; } = "#1a1a1a";

    public BrushSettings Brush { get; set; } = new();

    public List<StrokePoint> Points { get; set; } = [];

    /// <summary>Inner contours of a <see cref="ToolKind.Fill"/> stroke (even-odd holes); null otherwise.</summary>
    public List<List<StrokePoint>>? Holes { get; set; }

    /// <summary>
    /// Key into <see cref="Doc.ClipRegions"/>: the selection that was active
    /// when this stroke was painted, applied on every re-render so the
    /// document stays self-contained.
    /// </summary>
    public string? ClipId { get; set; }

    /// <summary>
    /// The gradient this stroke paints, for <see cref="ToolKind.Gradient"/>.
    /// Resolved against <see cref="Doc.Gradients"/>, so the mark is
    /// reproducible from the record alone.
    /// </summary>
    public string? GradientId { get; set; }

    /// <summary>
    /// The palette swatch this stroke's colour comes from, or null for a
    /// literal colour.
    ///
    /// When set, the swatch wins at render time — that is the whole point:
    /// edit the swatch and every stroke referencing it changes, on every
    /// frame and every layer, vector and raster alike. <see cref="Color"/> is
    /// still kept up to date as the last resolved value, so a document whose
    /// palette has been stripped, or whose swatch was deleted, still renders
    /// the colour the artist last saw rather than falling back to black.
    /// </summary>
    public string? SwatchId { get; set; }

    /// <summary>
    /// Painted on an alpha-locked layer: this stroke may only touch pixels
    /// that already had content beneath it. Recorded here rather than read
    /// from the layer at render time, so flipping the layer's alpha lock
    /// never repaints work already done (invariant 4). The mask itself is not
    /// stored — the rasterizer stamps strokes in order, so the content before
    /// this stroke is exactly what it has already drawn.
    /// </summary>
    public bool AlphaLocked { get; set; }

    /// <summary>
    /// Optional semantic name ("head-outline", "left-arm"). When two
    /// keyframes label a stroke identically, the inbetweener matches them
    /// directly, and an LLM can use labels to track anatomy across frames.
    /// </summary>
    public string? Label { get; set; }

    public Stroke Clone() => new()
    {
        Id = Ids.NewId("s"),
        Tool = Tool,
        Color = Color,
        Brush = Brush.Clone(),
        Points = [.. Points],
        Holes = Holes?.Select(h => new List<StrokePoint>(h)).ToList(),
        ClipId = ClipId,
        AlphaLocked = AlphaLocked,
        Label = Label,
    };
}
