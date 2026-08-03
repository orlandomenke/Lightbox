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
/// Base of the frame hierarchy. Serialized polymorphically with a "kind"
/// discriminator ("vector" | "painted") — see Serialization/FrameConverter.
/// </summary>
public abstract class Frame
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
}

/// <summary>A frame on a vector layer: strokes are the artwork.</summary>
public sealed class VectorFrame : Frame
{
    public List<Stroke> Strokes { get; set; } = [];
}

/// <summary>
/// A frame on a painted (raster) layer. Its pixels are defined as
/// <c>baseline PNG (if any) + strokes stamped on top, in order</c>:
/// the stroke record is what enables inbetweening, deterministic re-render,
/// and cheap undo, while the baseline carries imported/flattened pixels that
/// have no stroke provenance. Frames painted in-app have an empty baseline.
/// Strokes are never baked into the baseline.
/// </summary>
public sealed class PaintedFrame : Frame
{
    /// <summary>Baseline pixels as bare base64 of a PNG (no data-URL prefix). Empty when the frame was painted from scratch in-app.</summary>
    public string PngBase64 { get; set; } = "";

    /// <summary>Strokes painted on top of the baseline, in paint order.</summary>
    public List<Stroke> Strokes { get; set; } = [];

    /// <summary>
    /// Symbols placed on this frame, over the strokes.
    /// </summary>
    /// <remarks>
    /// Null rather than empty, and absent from the file until something is
    /// placed — the camera's rule again, and the fifth time it has earned its
    /// keep. A document that never places a symbol must serialize exactly as
    /// it did before symbols existed.
    /// </remarks>
    public List<SymbolPlacement>? Placements { get; set; }

    /// <summary>Whether anything is placed here at all.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasPlacements => Placements is { Count: > 0 };
}
