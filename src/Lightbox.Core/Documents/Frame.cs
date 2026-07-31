namespace Lightbox.Core.Documents;

/// <summary>
/// Base of the frame hierarchy. Serialized polymorphically with a "kind"
/// discriminator ("vector" | "painted") — see Serialization/FrameConverter.
/// </summary>
public abstract class Frame
{
    public string Id { get; set; } = Ids.NewId("f");
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
}
