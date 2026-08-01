namespace Lightbox.Core.Documents;

public enum LayerKind
{
    Painted,
    Vector,
}

/// <summary>
/// How a layer composites over the layers below it (Photoshop-style).
/// Render-time only: layer pixels are stored un-blended, so the stroke
/// record stays the single source of truth.
/// </summary>
public enum LayerBlendMode
{
    Normal,
    Multiply,
    Screen,
    Overlay,
    Darken,
    Lighten,
    ColorDodge,
    ColorBurn,
    HardLight,
    SoftLight,
    Difference,
    Exclusion,
    Hue,
    Saturation,
    Color,
    Luminosity,
}

/// <summary>
/// A cel on the timeline. <c>Frame == null</c> means "hold the previous
/// exposed frame" — the classic exposure-sheet model.
/// </summary>
public sealed class Cel
{
    public Frame? Frame { get; set; }
}

/// <summary>
/// A layer folder: layers referencing it render as a visual group in the
/// docker, and its visibility gates every member. Members stay ordinary
/// layers in Scene.Layers (contiguous, kept so by the group operations) —
/// compositing order is unchanged.
/// </summary>
public sealed class LayerGroup
{
    public string Id { get; set; } = Ids.NewId("group");

    public string Name { get; set; } = "Folder";

    public bool Visible { get; set; } = true;

    /// <summary>Header accent color in the docker (hex, e.g. "#4a6ea9").</summary>
    public string Color { get; set; } = "#4a6ea9";

    /// <summary>Docker-only view preference (not undoable).</summary>
    public bool Collapsed { get; set; }
}

public sealed class Layer
{
    public string Id { get; set; } = Ids.NewId("layer");

    public string Name { get; set; } = "Layer";

    public LayerKind Kind { get; set; } = LayerKind.Painted;

    public bool Visible { get; set; } = true;

    /// <summary>Whether this layer participates in onion-skin ghosting.</summary>
    public bool OnionEnabled { get; set; } = true;

    public double Opacity { get; set; } = 1;

    public LayerBlendMode BlendMode { get; set; } = LayerBlendMode.Normal;

    /// <summary>The layer folder this layer belongs to (null = ungrouped).</summary>
    public string? GroupId { get; set; }

    /// <summary>One entry per timeline frame; a null Frame is a hold.</summary>
    public List<Cel> Cels { get; set; } = [];
}
