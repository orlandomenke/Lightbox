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
        Label = Label,
    };
}
