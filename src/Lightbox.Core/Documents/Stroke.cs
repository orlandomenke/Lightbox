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
    /// Which palette that swatch came from, or null when nothing recorded it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q30 step 2, and the one hazard scoping introduces.</b> Palettes are
    /// the deliberate exception to *resources are a library, not something
    /// rendering reads* — live recolour is the feature, so a swatch id really is
    /// resolved at render time. That was safe while every document saw every
    /// palette. Once palettes are scoped, moving a document between folders
    /// changes which swatches resolve, and a swatch id that means one colour
    /// under the knight can mean another under the goblin.
    /// </para>
    /// <para>
    /// Independently authored palettes will not collide, because swatch ids are
    /// generated. <b>Duplicated palettes will</b> — and duplicating a palette to
    /// tweak it is the obvious thing to do, which is what turns this from
    /// theoretical into the next bug report.
    /// </para>
    /// <para>
    /// Nullable and absent from the file unless a stroke was painted from a
    /// palette, so nothing that paints a literal colour carries the key.
    /// </para>
    /// </remarks>
    public string? PaletteId { get; set; }

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

    /// <summary>
    /// The pixels a baked all-layers smudge sampled, and where they sat.
    /// </summary>
    /// <remarks>
    /// Null on every other stroke, and absent from the file — this is the one
    /// thing in the record that is pixels rather than instructions, so it is
    /// carried only by the strokes that asked to freeze what was underneath
    /// them. Cropped to the stroke's own reach, not the canvas, because the
    /// alternative is a full-size PNG per smudge.
    /// </remarks>
    public BakedSample? Baked { get; set; }

    /// <summary>
    /// A copy with a fresh id and everything else intact.
    /// </summary>
    /// <remarks>
    /// <b>Every property belongs here.</b> This is what duplicating a cel and
    /// generating an inbetween both go through, and it silently omitted
    /// <see cref="SwatchId"/> and <see cref="GradientId"/> — so a copied
    /// drawing kept its colour but lost its link to the palette, and
    /// recolouring the swatch afterwards changed the original and not the copy.
    /// A field added to a stroke and not added here does not fail: it goes
    /// quiet, which is why the list is exhaustive rather than "the ones that
    /// matter".
    /// </remarks>
    public Stroke Clone() => new()
    {
        Id = Ids.NewId("s"),
        Tool = Tool,
        Color = Color,
        SwatchId = SwatchId,
        PaletteId = PaletteId,
        GradientId = GradientId,
        Brush = Brush.Clone(),
        Points = [.. Points],
        Holes = Holes?.Select(h => new List<StrokePoint>(h)).ToList(),
        ClipId = ClipId,
        AlphaLocked = AlphaLocked,
        Label = Label,
        Baked = Baked,
    };
}

/// <summary>
/// A frozen backdrop: the composite a stroke read, and the document-space
/// corner it starts at.
/// </summary>
/// <remarks>
/// Shared by reference when a stroke is cloned. It is immutable in practice —
/// nothing edits a baked sample, it is replaced or dropped — and copying a
/// megabyte of base64 per cel duplication would be a poor trade for a
/// guarantee nothing needs.
/// </remarks>
public sealed class BakedSample
{
    /// <summary>Bare base64 of a PNG, no data-URL prefix.</summary>
    public string PngBase64 { get; set; } = "";

    public int X { get; set; }

    public int Y { get; set; }
}
