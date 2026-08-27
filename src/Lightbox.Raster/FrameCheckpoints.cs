using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// Taking, and trusting, a drawing's <see cref="StrokeCheckpoint"/> (B30).
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem, in one number: 10 000 strokes is 106 seconds.</b> The
/// document stores strokes and the pixels are derived (invariant 1), so
/// materialising a drawing replays its whole record — <c>n^1.00</c> at ~10.7 ms
/// a painterly stroke, with no cliff to avoid because the cost simply
/// accumulates. For one cel of a sequence that is nothing. For a painting
/// somebody returns to for a week it is the dominant cost of using the
/// application, paid on every open, every undo past the top of the stack and
/// every frame of an export.
/// </para>
/// <para>
/// <b>The answer is not to replay faster, it is to replay less.</b> Store the
/// rendered result beside the strokes and replay only what came after it —
/// strokes stay the truth, the image is a shortcut that can always be thrown
/// away. Nobody else in this field sits where Lightbox does (geometry as the
/// document, expensive per-dab marks, on the CPU); the ones who keep mark
/// quality store pixels, and the ones who keep geometry make their marks cheap.
/// PSD's flattened composite and <c>.kra</c>'s per-layer images are this, and
/// have been since 1990. <c>docs/DESIGN-raster-checkpoint.md</c> argues it out.
/// </para>
/// <para>
/// <b>It is a prefix, which is what makes it hold up under painting.</b>
/// Painting appends: a checkpoint taken at stroke 4 000 keeps working through
/// stroke 4 001, 4 002 and the rest of the session, because the strokes after
/// it are ordinary strokes and get stamped as usual. What it does <em>not</em>
/// help is editing an old stroke or undoing past the checkpoint — both change
/// the prefix, both drop it, and both then cost what they cost today. That is
/// the honest shape of the win and it is the shape painting has.
/// </para>
/// <para>
/// <b>Three separations, each of which has to hold.</b> Nothing here decides
/// when to run — the caller does, on save. Nothing here mutates a document —
/// the caller attaches. And the render happens on whatever thread calls
/// <see cref="Render"/>, from a private copy of the strokes made by
/// <see cref="Plan"/>, so the artist's own record is never read by a worker
/// while they paint on it.
/// </para>
/// </remarks>
public static class FrameCheckpoints
{
    /// <summary>
    /// How many strokes a drawing needs before its pixels are worth storing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The threshold is what stops this from being a tax on animation.</b> A
    /// checkpoint is a full-canvas image — about 3.2 MB at 1080p on painterly
    /// art — and a sequence is two hundred drawings. Writing one per cel would
    /// take a problem paintings have and give it to documents that do not have
    /// it: the median animation cel is a character on empty paper with tens of
    /// strokes, which replays in a few milliseconds and would be paying
    /// megabytes for it.
    /// </para>
    /// <para>
    /// 250 because that is the smallest drawing B30 measured, at <b>2.68 s</b> to
    /// rebuild — unambiguously worth an image, where a cel of twenty strokes
    /// unambiguously is not. The number prices a wait against bytes, so it is a
    /// judgement rather than a derivation, and it is here as one constant so the
    /// judgement can be revisited in one place.
    /// </para>
    /// </remarks>
    public const int MinStrokes = 250;

    /// <summary>
    /// A checkpoint waiting to be rendered: everything <see cref="Render"/>
    /// needs, and no reference to anything the artist can still edit.
    /// </summary>
    /// <remarks>
    /// The strokes are a private deep copy for one reason: the render runs on a
    /// worker and the artist keeps painting. Reading the live list from another
    /// thread while it grows is a torn read, and the failure would be rare,
    /// timing-dependent and impossible to reproduce — the worst shape a bug can
    /// have. <c>Doc.Clone</c> prices a whole document at 5.8 ms at 5 000 strokes
    /// (B142), so a prefix of one frame is cheap enough to pay on the UI thread.
    /// </remarks>
    public sealed record CheckpointPlan(
        string FrameId, IReadOnlyList<Stroke> Strokes, string Fingerprint, int Width, int Height);

    /// <summary>
    /// Whether this drawing's pixels may be stored beside its strokes at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four refusals, each because a checkpoint would be a promise this cannot
    /// keep. Refusing costs a slow open; a wrong checkpoint shows an artist ink
    /// their document does not describe, so every one of these errs toward the
    /// replay.
    /// </para>
    /// <list type="bullet">
    /// <item><b>A baseline</b> is stored pixels beneath the strokes, so covering
    /// it would fold content-with-provenance into derived state and make the
    /// fingerprint answer for bytes it does not hash. Nothing in the app writes
    /// one, and <c>CanTileFrame</c> already refuses these.</item>
    /// <item><b>Rig-bound strokes and correctives</b> render differently
    /// depending on where the playhead is — that is what binding means — and a
    /// checkpoint holds one pose. Freezing it would nail a cutout limb to
    /// whichever frame happened to be showing when the document was saved.</item>
    /// <item><b>A stroke sampling the layers beneath it live</b> depends on
    /// pixels that are not on this frame at all. Such a frame is dropped from the
    /// bitmap cache on every read, so there is nothing here worth storing.</item>
    /// <item><b>Too few strokes</b> — see <see cref="MinStrokes"/>.</item>
    /// </list>
    /// <para>
    /// <b>Placements are deliberately not on the list.</b> A symbol is stamped
    /// over the strokes, after them, so it is never inside a checkpoint and stays
    /// answerable to the symbol it names — which is what has to remain true when
    /// that symbol is edited somewhere else.
    /// </para>
    /// </remarks>
    public static bool CanCheckpoint(Frame frame)
    {
        if (frame.Strokes.Count < MinStrokes) return false;
        if (frame.HasBaseline) return false;
        if (frame.HasBoundStrokes || frame.HasCorrectives) return false;
        foreach (var stroke in frame.Strokes)
            if (stroke.Brush.SampleSource == SampleSource.AllLayersLive) return false;
        return true;
    }

    /// <summary>
    /// What to render for this drawing, or null when there is nothing to gain.
    /// </summary>
    /// <remarks>
    /// Call on the thread that owns the document. Null covers both "may not" and
    /// "need not": a drawing whose checkpoint already covers every stroke it has
    /// is finished, and re-rendering it would spend a worker to produce the same
    /// bytes.
    /// </remarks>
    public static CheckpointPlan? Plan(Doc doc, Frame frame)
    {
        if (!CanCheckpoint(frame)) return null;

        var count = frame.Strokes.Count;

        // Hashed once and used twice. Asking `CheckpointFingerprint.Matches`
        // whether the held one is current and then computing the new plan's
        // fingerprint separately walks the whole record twice — which on the
        // documents this feature is for is the difference between one pass and
        // two over several megabytes of JSON, on the UI thread, every save.
        var fingerprint = CheckpointFingerprint.Of(doc, frame, count);

        if (frame.Checkpoint is { IsUsable: true } held
            && held.Strokes == count
            && held.Width == doc.Scene.Width
            && held.Height == doc.Scene.Height
            && held.Fingerprint == fingerprint)
        {
            return null;
        }

        var copy = new List<Stroke>(count);
        for (var i = 0; i < count; i++) copy.Add(frame.Strokes[i].Clone(newId: false));

        return new CheckpointPlan(
            frame.Id, copy, fingerprint, doc.Scene.Width, doc.Scene.Height);
    }

    /// <summary>
    /// Render a planned checkpoint. Safe to call from a worker; touches no
    /// document.
    /// </summary>
    public static StrokeCheckpoint? Render(CheckpointPlan plan)
    {
        // Exactly what `Materialize` would draw for this prefix: no baseline (the
        // eligibility rules saw to that) and no placements (they stamp after the
        // strokes, so they are never covered). Anything else here would make the
        // checkpoint a different picture from the replay it stands in for.
        using var pixels = FrameRasterizer.Rasterize(plan.Strokes, plan.Width, plan.Height);
        if (CheckpointCodec.Encode(pixels) is not { } encoded) return null;

        return new StrokeCheckpoint
        {
            Strokes = plan.Strokes.Count,
            Fingerprint = plan.Fingerprint,
            PixelsBase64 = encoded,
            Width = plan.Width,
            Height = plan.Height,
        };
    }

    /// <summary>
    /// This drawing's checkpoint if it still describes it, or null.
    /// </summary>
    /// <remarks>
    /// The whole of the trust decision, in one place and asked every time.
    /// Nothing anywhere is required to have remembered to drop a stale
    /// checkpoint: staleness is discovered here, by recomputing what the pixels
    /// were made from and comparing. See <c>CheckpointFingerprint</c>.
    /// </remarks>
    public static StrokeCheckpoint? Usable(Doc doc, Frame frame) =>
        frame.Checkpoint is { } checkpoint && CheckpointFingerprint.Matches(doc, frame, checkpoint)
            ? checkpoint
            : null;

    /// <summary>
    /// Decode a checkpoint's pixels for a render, or null if they cannot be had.
    /// </summary>
    internal static SKBitmap? Pixels(StrokeCheckpoint checkpoint) =>
        CheckpointCodec.Decode(checkpoint.PixelsBase64, checkpoint.Width, checkpoint.Height);
}
