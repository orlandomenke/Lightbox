using System.Buffers.Binary;

namespace Lightbox.Import;

/// <summary>
/// Photoshop's RLE scheme: a table of per-scanline compressed byte counts,
/// then one PackBits run-length stream per row.
/// </summary>
/// <remarks>
/// <para>
/// Extracted so there is <b>one</b> implementation rather than one per format.
/// <see cref="AbrReader"/> had the first, for sampled brush tips; PSD channel
/// data is the identical scheme, and a subtly different second copy of a
/// run-length decoder is the kind of thing that rots without anyone noticing —
/// the two would drift on exactly the malformed input that matters.
/// </para>
/// <para>
/// <b>Every entry point is total.</b> These read files an artist did not write
/// and we did not validate, so a truncated row, a length table pointing past
/// the end of the buffer, or a run that overshoots its scanline returns null or
/// stops rather than throwing — a corrupt file should be reported, never crash
/// the import. Rows are decoded independently, so damage stays local to the row
/// that carries it.
/// </para>
/// </remarks>
internal static class PackBits
{
    /// <summary>
    /// Read <paramref name="count"/> scanline byte-counts, advancing
    /// <paramref name="pos"/> past the table. Null when the table does not fit.
    /// </summary>
    /// <param name="wide">
    /// PSB (the large-document variant) writes each count as an int32 where PSD
    /// writes an int16. Getting this wrong misreads every row length in the
    /// file, so it is a required decision rather than a sniffed one.
    /// </param>
    public static int[]? ReadRowLengths(byte[] data, ref int pos, int count, bool wide)
    {
        if (count < 0) return null;
        var stride = wide ? 4 : 2;
        if (pos < 0 || (long)pos + (long)count * stride > data.Length) return null;

        var lengths = new int[count];
        for (var i = 0; i < count; i++)
        {
            lengths[i] = wide
                ? BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos))
                : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos));
            if (lengths[i] < 0) return null;
            pos += stride;
        }
        return lengths;
    }

    /// <summary>
    /// Decode <paramref name="rowLengths"/>.Length scanlines of
    /// <paramref name="width"/> bytes each, starting at <paramref name="pos"/>.
    /// </summary>
    /// <remarks>
    /// A row that decodes short leaves the remainder of its scanline at zero,
    /// which for a PSD channel reads as transparent or black rather than as
    /// whatever happened to be in memory.
    /// </remarks>
    /// <returns>
    /// <paramref name="width"/> × rows bytes, or null when the compressed data
    /// runs past <paramref name="limit"/> or the end of the buffer.
    /// </returns>
    public static byte[]? DecodeRows(byte[] data, int pos, int[] rowLengths, int width, int limit)
    {
        if (width <= 0) return null;
        var height = rowLengths.Length;
        var total = (long)width * height;
        if (total is 0 or > int.MaxValue) return null;

        var pixels = new byte[(int)total];
        for (var row = 0; row < height; row++)
        {
            // Long arithmetic: a PSB scanline length is a full int32 an artist did
            // not write, and `pos + length` in 32 bits wraps negative for a large
            // one — which passes a "too big?" test and then indexes the array with
            // the negative result on the following row.
            var src = pos;
            var end = (long)pos + rowLengths[row];
            if (end > data.Length || end > limit || end < pos) return null;
            var srcEnd = (int)end;

            var dst = row * width;
            var dstEnd = dst + width;
            while (src < srcEnd && dst < dstEnd)
            {
                int n = (sbyte)data[src++];
                if (n >= 0)
                {
                    // A literal run of n+1 bytes copied straight through.
                    var run = n + 1;
                    if (src + run > srcEnd) run = srcEnd - src;
                    if (run > dstEnd - dst) run = dstEnd - dst;
                    if (run <= 0) break;
                    Array.Copy(data, src, pixels, dst, run);
                    src += n + 1;
                    dst += run;
                }
                else if (n != -128)
                {
                    // One byte repeated 1-n times. -128 is a no-op by spec.
                    if (src >= srcEnd) break;
                    var value = data[src++];
                    var run = Math.Min(1 - n, dstEnd - dst);
                    for (var k = 0; k < run; k++) pixels[dst++] = value;
                }
            }
            pos = srcEnd;
        }
        return pixels;
    }

    /// <summary>
    /// The common case: a length table immediately followed by its own rows.
    /// </summary>
    public static byte[]? Decode(byte[] data, int pos, int width, int height, int limit, bool wide = false)
    {
        var lengths = ReadRowLengths(data, ref pos, height, wide);
        return lengths is null ? null : DecodeRows(data, pos, lengths, width, limit);
    }
}
