namespace Lightbox.Core.Documents;

/// <summary>
/// A rendering of the first <see cref="Strokes"/> marks of a drawing, kept
/// beside them so reopening does not have to replay them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Derived state, never truth (B30).</b> The strokes remain the document —
/// invariant 1 — and this is a shortcut that may be thrown away at any moment
/// and recomputed. The direction is what matters: pixels are derived from the
/// record and the record is never derived from the pixels. Delete every
/// checkpoint in a document and not one pixel changes; that is a test
/// (<c>ACheckpointedRenderIsBitIdenticalToAReplay</c>) rather than an
/// intention.
/// </para>
/// <para>
/// <b>Why it is not <see cref="Frame.PngBase64"/>, which is the obvious home
/// and a trap.</b> Three separate things break, and each is enough on its own:
/// <c>FrameRasterizer.Materialize</c> draws the baseline and <em>then</em>
/// replays the whole record, so the art would double;
/// <c>TileFrameCache.CanTileFrame</c> refuses a frame with a baseline, so every
/// checkpointed painting would lose the tiling it most needs; and
/// <c>UnseenByTheModel</c> reads a baseline to mean <em>"pixels the inbetweener
/// cannot read"</em>, which a rendering of perfectly readable strokes is not. A
/// baseline is content with provenance; this is derived state. Same bytes,
/// opposite standing, different fields.
/// </para>
/// <para>
/// <b>The pixels are inside a PNG and are deliberately not called one.</b> They
/// are <em>premultiplied</em> bytes carried in a container that has been told
/// they are straight, which means the file is a byte store rather than a
/// picture — see <c>CheckpointCodec</c>, which is the only thing that may
/// encode or decode this field. Handing it to <c>PngCodec</c> gives wrong
/// colour quietly, because <c>SKBitmap.Decode</c> returns <c>Bgra8888</c> and
/// swaps red with blue: 230 902 of 614 400 bytes on a 250-stroke painting,
/// worst channel error 164, which reads like a precision problem and is not
/// one. A field called <c>pngBase64</c> would have invited exactly that call.
/// </para>
/// <para>
/// Shared by reference when a frame is cloned, on <c>BakedSample</c>'s
/// precedent and for its reason: this can be megabytes, copying it buys a
/// guarantee nothing needs, and nothing ever edits one — a checkpoint is
/// replaced or dropped, never amended.
/// </para>
/// </remarks>
public sealed class StrokeCheckpoint
{
    /// <summary>
    /// How many leading strokes of the frame these pixels already contain.
    /// </summary>
    /// <remarks>
    /// A <em>prefix</em>, which is what makes the whole thing worth having:
    /// painting appends, so a checkpoint taken at stroke 4 000 keeps working as
    /// the artist paints 4 001 onward, and only the tail is replayed. It is
    /// also why a stale checkpoint is cheap — the strokes after it are ordinary
    /// strokes.
    /// </remarks>
    public int Strokes { get; set; }

    /// <summary>
    /// What the covered strokes, and everything a render resolves for them,
    /// hashed to when these pixels were made.
    /// </summary>
    /// <remarks>
    /// The whole of the invalidation, and deliberately so: a checkpoint is
    /// accepted by <em>recomputing</em> this and comparing, never by trusting
    /// that some edit path remembered to drop it. See
    /// <c>CheckpointFingerprint</c> for why that direction was chosen.
    /// </remarks>
    public string Fingerprint { get; set; } = "";

    /// <summary>
    /// The rendered pixels — see the remarks on the class about what this is
    /// and which codec may touch it.
    /// </summary>
    public string PixelsBase64 { get; set; } = "";

    /// <summary>Width of <see cref="PixelsBase64"/>, in document pixels.</summary>
    /// <remarks>
    /// Stored rather than assumed from the scene, because a canvas resize is an
    /// edit the fingerprint sees but a decode would meet first. Checked before
    /// the pixels are used at all, so a checkpoint from a differently sized
    /// document is refused rather than stretched.
    /// </remarks>
    public int Width { get; set; }

    /// <inheritdoc cref="Width"/>
    public int Height { get; set; }

    /// <summary>Whether this carries enough to be worth trying to use.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsUsable =>
        Strokes > 0 && Width > 0 && Height > 0
        && Fingerprint.Length > 0 && PixelsBase64.Length > 0;
}
