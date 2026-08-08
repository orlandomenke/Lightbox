namespace Lightbox.Core.Documents;

/// <summary>
/// The animator's classification of a drawing — key pose, breakdown, or
/// inbetween. Purely a marker (colors the timeline cell); it never changes
/// how the frame renders.
/// </summary>
public enum FrameRole
{
    Key,
    Breakdown,
    Inbetween,
}

/// <summary>
/// One drawing: strokes, an optional pixel baseline, optional placed symbols,
/// and the anchors and hitboxes that travel with it.
/// </summary>
/// <remarks>
/// <para>
/// <b>There used to be two of these and the split was fiction.</b>
/// <c>PaintedFrame</c> held a baseline, strokes and placements;
/// <c>VectorFrame</c> held strokes alone. Both stamped those strokes through the
/// same <c>BrushEngine</c>, so a line on a "vector" layer rendered identically to
/// the same line on a "raster" one. The vector class was therefore not a
/// different kind of drawing — it was this one with two abilities removed, and
/// the only thing the distinction decided was what a frame could not hold.
/// </para>
/// <para>
/// That is what made <b>B132</b> possible: placing a symbol on a vector layer
/// returned early because the frame had nowhere to put it, silently, with no
/// design note anywhere giving a reason. Merging the classes deletes the
/// restriction rather than documenting it.
/// </para>
/// <para>
/// <b>Nothing became more capable.</b> Every tool already worked on both kinds;
/// the record already kept stroke provenance everywhere. What went away is a
/// question an artist was asked at layer-creation time that had no meaningful
/// answer — see Q52 — and a guard that made one answer quietly worse than the
/// other.
/// </para>
/// <para>
/// Still serialized with a <c>"kind"</c> discriminator on <em>read</em> so every
/// existing file loads; it is no longer written. See
/// <c>Serialization/FrameConverter</c>.
/// </para>
/// </remarks>
public sealed class Frame
{
    public string Id { get; set; } = Ids.NewId("f");

    public FrameRole Role { get; set; } = FrameRole.Key;

    /// <summary>
    /// Where this drawing's named anchors sit, keyed by <see cref="Anchor.Id"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On the frame rather than in a table keyed by frame index, and that is the
    /// load-bearing choice: a hold, a re-time, a cel drag and a timing preset all
    /// move drawings around the sheet, and an index-keyed table would silently
    /// point at the wrong drawing after any of them. Here the anchor travels with
    /// the drawing for free.
    /// </para>
    /// <para>
    /// Null until an anchor is placed, and absent from the file until then — the
    /// camera's rule. A document that never sockets anything writes no anchor key.
    /// </para>
    /// </remarks>
    public Dictionary<string, AnchorPoint>? Anchors { get; set; }

    /// <summary>
    /// This drawing's collision rectangles, keyed by <see cref="CollisionShape.Id"/>.
    /// </summary>
    /// <remarks>
    /// On the frame for exactly the reasons <see cref="Anchors"/> is, and with one
    /// extra payoff: <b>absence is the off state</b>. A hitbox that is only active
    /// on the two contact frames is a shape placed on those two drawings and
    /// nowhere else, so there is no "active" flag that can fall out of step with
    /// the animation after a re-time.
    /// </remarks>
    public Dictionary<string, ShapeBox>? Shapes { get; set; }
    /// <summary>
    /// Baseline pixels as bare base64 of a PNG (no data-URL prefix), or null when
    /// this drawing has none — which is every frame painted in the app.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A frame's pixels are <c>baseline (if any) + strokes stamped on top, in
    /// order</c>. The stroke record is what makes inbetweening, deterministic
    /// re-render and cheap undo possible; the baseline carries imported or
    /// flattened pixels that have no stroke provenance. <b>Strokes are never
    /// baked into the baseline.</b>
    /// </para>
    /// <para>
    /// <b>Nullable, not empty string.</b> Absence is the off state, as it is for
    /// <see cref="Anchors"/> and the camera — a frame nobody imported into writes
    /// no baseline key at all.
    /// </para>
    /// </remarks>
    public string? PngBase64 { get; set; }

    /// <summary>Strokes painted on top of the baseline, in paint order.</summary>
    public List<Stroke> Strokes { get; set; } = [];

    /// <summary>
    /// Symbols placed on this frame, over the strokes.
    /// </summary>
    /// <remarks>
    /// Null rather than empty, and absent from the file until something is
    /// placed — the camera's rule again. A document that never places a symbol
    /// must serialize exactly as it did before symbols existed.
    /// </remarks>
    public List<SymbolPlacement>? Placements { get; set; }

    /// <summary>Whether anything is placed here at all.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasPlacements => Placements is { Count: > 0 };

    /// <summary>Whether this drawing carries imported or flattened pixels.</summary>
    /// <remarks>
    /// <b>This is what "did this come from outside" should be asked of</b>, rather
    /// than <see cref="Layer.Kind"/>. Every layer in every document written before
    /// the two frame classes merged is <c>LayerKind.Painted</c> — including the
    /// ones drawn entirely in the app — so the field cannot retroactively mean
    /// "imported". A non-empty baseline can, because it is a fact about the
    /// drawing rather than a label chosen when the layer was made.
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasBaseline => !string.IsNullOrEmpty(PngBase64);
}
