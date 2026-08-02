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

    /// <summary>Locking a folder locks every layer inside it.</summary>
    public bool Locked { get; set; }

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

    /// <summary>
    /// Blocks everything that changes pixels or geometry — paint, fill,
    /// transform, delete, blank, cel edits, and external writes. Visibility,
    /// opacity, blend mode and reordering stay available, so a locked layer
    /// is still useful as reference. A locked layer still renders and still
    /// exports: locking is about editing, not about hiding.
    /// </summary>
    public bool Locked { get; set; }

    /// <summary>
    /// Restrict painting to pixels that already have content, so colour can
    /// be changed without altering the silhouette. Recorded per stroke at
    /// paint time (see <see cref="Stroke.AlphaLocked"/>) rather than consulted
    /// at render time, or toggling it would repaint existing art.
    /// </summary>
    public bool AlphaLocked { get; set; }

    /// <summary>
    /// This layer is the document's paper. It holds the background colour as
    /// real, editable content rather than as a scene property, so unlocking
    /// it and painting on it works like any other layer — and erasing it
    /// genuinely reveals transparency instead of a colour the renderer keeps
    /// putting back.
    /// </summary>
    public bool IsBackground { get; set; }

    /// <summary>Whether this layer participates in onion-skin ghosting.</summary>
    public bool OnionEnabled { get; set; } = true;

    public double Opacity { get; set; } = 1;

    public LayerBlendMode BlendMode { get; set; } = LayerBlendMode.Normal;

    /// <summary>The layer folder this layer belongs to (null = ungrouped).</summary>
    public string? GroupId { get; set; }

    /// <summary>One entry per timeline frame; a null Frame is a hold.</summary>
    public List<Cel> Cels { get; set; } = [];
}
