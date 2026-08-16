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
/// Where a drawing came from, when it did not come from the artist's hand.
/// </summary>
/// <remarks>
/// Q31: stored on the frame, and <b>absent unless AI touched it</b> — a
/// document that never used the AI is byte-identical to one from before the
/// feature existed. It records provenance, never behaviour: deleting it
/// changes no pixel, because the strokes are ordinary strokes.
/// <para>
/// <paramref name="Attempts"/> is how many times the model was asked before
/// this drawing was defensible — <b>absent unless it took more than one</b>,
/// under the same rule the record itself follows. It is the only durable trace
/// of a repair: the status line saying so is gone by the next action, and
/// "how often does my model need a second go" is the number that tells an
/// artist whether the model they brought is borderline (Q85).
/// </para>
/// </remarks>
public sealed record AiProvenance(string Provider, string? Model = null, int? Attempts = null);

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

    /// <summary>
    /// The timing chart on this extreme (Q58), or null — and null is the
    /// default and writes no key. Fractions strictly between 0 and 1,
    /// ascending: where each inbetween sits on the travel from this drawing
    /// to the next key. Three rungs ask for three inbetweens.
    /// </summary>
    /// <remarks>
    /// On the frame for the reasons <see cref="Anchors"/> is: a re-time, a
    /// cel drag and a timing preset all move drawings around the sheet, and
    /// the chart describes <em>this extreme's</em> spacing, so it must travel
    /// with the drawing. Meaningful on a <see cref="FrameRole.Key"/> frame;
    /// kept but ignored elsewhere, so demoting and re-promoting an extreme
    /// does not silently destroy its chart.
    /// </remarks>
    public List<double>? Chart { get; set; }

    /// <summary>
    /// Which AI produced this drawing, or null for every frame an artist drew.
    /// </summary>
    /// <remarks>
    /// Null is the ordinary answer and writes no key — the camera's rule, and
    /// the Q31 decision. The one thing this must never become is behaviour: it
    /// is a record of where the frame came from, and rendering never reads it.
    /// </remarks>
    public AiProvenance? Ai { get; set; }

    /// <summary>Whether anything is placed here at all.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasPlacements => Placements is { Count: > 0 };

    /// <summary>
    /// Whether any stroke here follows the rig. Derived; never serialized.
    /// </summary>
    /// <remarks>
    /// The render-time twin of <see cref="HasPlacements"/>: a bound frame's
    /// pixels depend on where the playhead is (the pose track interpolates by
    /// frame index), so — exactly like a frame that places a symbol — its
    /// cached bitmap must be keyed per timeline position and it cannot take
    /// the tile path, whose store knows frames only by id.
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasBoundStrokes
    {
        get
        {
            foreach (var stroke in Strokes)
                if (stroke.Weights is { Count: > 0 }) return true;
            return false;
        }
    }

    /// <summary>
    /// Shapes the artist drew at joint extremes, or null — and null is every
    /// ordinary drawing (Q100).
    /// </summary>
    /// <remarks>
    /// On the frame because a corrective names strokes by id, so it belongs to
    /// the drawing that holds them and travels with it through copy, paste and
    /// symbols. It fixes <em>a drawing</em>: the cutout workflow, where a limb
    /// is one drawing reused across the sequence. It is not a tool for two
    /// hundred hand-drawn frames, and the manual says so.
    /// </remarks>
    public List<Corrective>? Correctives { get; set; }

    /// <summary>Whether the rig reshapes this drawing at some joint angle.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasCorrectives => Correctives is { Count: > 0 };

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

    /// <summary>A copy holding no reference in common with this one, id included.</summary>
    /// <remarks>
    /// <para>
    /// The undo path's deep copy (B142). Distinct from
    /// <c>DocumentEditor.CloneFrame</c>, which gives the copy a <em>fresh</em> id
    /// because it is duplicating a drawing an artist can see; a snapshot has to
    /// restore the frame that was there, so the id is carried.
    /// </para>
    /// <para>
    /// <c>AnchorPoint</c> and <c>ShapeBox</c> are positional records and immutable,
    /// so a new dictionary over the same values is a complete copy.
    /// </para>
    /// </remarks>
    public Frame Clone()
    {
        var copy = (Frame)MemberwiseClone();
        copy.Strokes = Strokes.Select(s => s.Clone(newId: false)).ToList();
        copy.Placements = Placements?.Select(p => p.Clone()).ToList();
        copy.Anchors = Anchors is null ? null : new Dictionary<string, AnchorPoint>(Anchors);
        copy.Shapes = Shapes is null ? null : new Dictionary<string, ShapeBox>(Shapes);
        copy.Chart = Chart is null ? null : [.. Chart];
        copy.Correctives = Correctives?.Select(c => c.Clone()).ToList();
        return copy;
    }
}
