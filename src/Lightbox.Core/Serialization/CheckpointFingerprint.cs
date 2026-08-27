using System.Security.Cryptography;
using System.Text.Json;
using Lightbox.Core.Documents;

namespace Lightbox.Core.Serialization;

/// <summary>
/// What a <see cref="StrokeCheckpoint"/> was made from, as one string.
/// </summary>
/// <remarks>
/// <para>
/// <b>Validation by recomputation, not by invalidation.</b> The obvious design
/// is a funnel — every edit path calls something that drops the checkpoint —
/// and it is the design that fails by showing stale art, because it fails
/// whenever a new edit path forgets to call it. There is no forgetting here: a
/// checkpoint is accepted only when this function, run again on the document as
/// it stands now, produces the string the checkpoint was stored with.
/// A missed call site cannot make a checkpoint wrong; it can only make one
/// linger in a file until the next save replaces it.
/// </para>
/// <para>
/// <b>The bytes hashed are the bytes the document is made of</b>, which is what
/// makes this self-maintaining. A field that reaches the pixels but is not
/// serialized cannot survive a reload, so by invariant 1 it cannot affect a
/// reloaded render — meaning every field that matters is in the JSON, and
/// hashing the JSON therefore covers fields nobody has written yet. Hashing a
/// hand-picked list of properties would have to be extended by whoever adds the
/// next brush setting, and would fail silently when they did not.
/// </para>
/// <para>
/// <b>Two halves, because they are different sizes.</b> The covered strokes are
/// hashed exactly — they are the thing the pixels are of. Everything a render
/// resolves <em>for</em> them (tips, textures, clips, swatches, ramps) is
/// hashed wholesale through <c>Doc.RenderShell</c>, which subtracts rather than
/// enumerates. See that method for what comes out and why.
/// </para>
/// <para>
/// SHA-256 rather than something cheaper: a collision here is stale art on an
/// artist's canvas, the hardware does it at gigabytes a second, and the input
/// is a few megabytes. Measured on a 2 000-stroke painting the whole
/// fingerprint is single-digit milliseconds against the twenty-one seconds of
/// replay it decides.
/// </para>
/// </remarks>
public static class CheckpointFingerprint
{
    /// <summary>
    /// The fingerprint of this frame's first <paramref name="strokes"/> marks,
    /// as rendered in this document.
    /// </summary>
    public static string Of(Doc doc, Frame frame, int strokes)
    {
        var count = Math.Clamp(strokes, 0, frame.Strokes.Count);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var sink = new HashSink(hash);

        // The count first, so a prefix of n and a prefix of n+1 cannot hash the
        // same way if the extra stroke happens to serialize to nothing.
        JsonSerializer.Serialize(sink, count, DocJson.Compact);
        JsonSerializer.Serialize(sink, doc.RenderShell(), DocJson.Compact);
        for (var i = 0; i < count; i++)
            JsonSerializer.Serialize(sink, frame.Strokes[i], DocJson.Compact);

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// Whether these pixels still describe this drawing's leading strokes.
    /// </summary>
    /// <remarks>
    /// The count is checked before the hash is computed, so the common refusal —
    /// a checkpoint covering more strokes than the frame now has, which is what
    /// an undo leaves behind — costs nothing.
    /// </remarks>
    public static bool Matches(Doc doc, Frame frame, StrokeCheckpoint checkpoint) =>
        checkpoint.IsUsable
        && checkpoint.Strokes <= frame.Strokes.Count
        && checkpoint.Width == doc.Scene.Width
        && checkpoint.Height == doc.Scene.Height
        && Of(doc, frame, checkpoint.Strokes) == checkpoint.Fingerprint;

    /// <summary>
    /// A write-only stream that hashes what it is given and keeps none of it.
    /// </summary>
    /// <remarks>
    /// The serializer wants somewhere to write and the alternative is a string:
    /// a 2 000-stroke painting is around 9 MB of JSON, which is 9 MB of garbage
    /// per validation and lands on the interaction that follows. This costs the
    /// serializer's own buffer and nothing else.
    /// </remarks>
    private sealed class HashSink(IncrementalHash hash) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) =>
            hash.AppendData(buffer, offset, count);

        public override void Write(ReadOnlySpan<byte> buffer) => hash.AppendData(buffer);

        public override void WriteByte(byte value) => hash.AppendData([value]);

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
