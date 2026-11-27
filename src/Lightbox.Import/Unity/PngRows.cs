using System.Buffers.Binary;
using System.IO.Compression;

namespace Lightbox.Import.Unity;

/// <summary>
/// A PNG this reader does not handle — palette, 16-bit, grey, interlaced, a
/// colour key. Distinct from a broken file, so a caller can choose another
/// decoder for it rather than report it as damaged.
/// </summary>
public sealed class PngUnsupportedException(string message) : FormatException(message);

/// <summary>
/// Reads a PNG one row at a time, top to bottom, as 8-bit RGBA (Q301).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not Skia.</b> The SkiaSharp build Lightbox ships has no row-at-a-time
/// PNG decode (<c>StartScanlineDecode</c> is unimplemented), so cutting a few
/// cells out of a game atlas meant decoding all of it — a quarter of a gigabyte
/// for one real character sheet. This keeps one row and the row above it.
/// </para>
/// <para>
/// <b>Scope, from the PNG specification (W3C, second edition):</b> 8-bit
/// truecolour (type 2) and truecolour with alpha (type 6), not interlaced — what
/// Unity, Photoshop and Skia write for sprite sheets. Everything else raises
/// <see cref="PngUnsupportedException"/>. Values are handed back as stored: no
/// gamma, no colour profile, nothing premultiplied, which is also what makes a
/// hash of them a hash of the file's own pixels.
/// </para>
/// <para>
/// <b>Hostile files (S1).</b> Every chunk's CRC is checked and its length capped;
/// a critical chunk this reader does not know is refused; the image data is
/// inflated only as far as the rows asked for, so a decompression bomb costs
/// nothing past the last row read; and running out of data is a
/// <see cref="FormatException"/>, never a short image.
/// </para>
/// </remarks>
public sealed class PngRows : IDisposable
{
    /// <summary>A chunk longer than this is refused.</summary>
    public const int MaxChunkBytes = 64 * 1024 * 1024;

    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly ZLibStream _inflate;
    private readonly IdatStream _idat;
    private readonly string _label;
    private readonly int _bpp;
    private byte[] _row;
    private byte[] _prior;
    private int _next;

    public int Width { get; }
    public int Height { get; }
    public bool HasAlpha => _bpp == 4;

    private PngRows(Stream source, string label, int width, int height, int bpp, (int Length, byte[] Type) firstIdat)
    {
        _label = label;
        Width = width;
        Height = height;
        _bpp = bpp;
        _row = new byte[1 + width * bpp];
        _prior = new byte[1 + width * bpp];
        _idat = new IdatStream(source, label, firstIdat.Length);
        _inflate = new ZLibStream(_idat, CompressionMode.Decompress, leaveOpen: true);
    }

    /// <summary>Reads the header and every chunk up to the image data.</summary>
    /// <exception cref="PngUnsupportedException">A valid PNG of a kind this does not read.</exception>
    /// <exception cref="FormatException">Not a PNG, or damaged.</exception>
    public static PngRows Open(Stream source, string label)
    {
        Span<byte> sig = stackalloc byte[8];
        if (!ReadFully(source, sig) || !sig.SequenceEqual(Signature))
            throw new FormatException($"{label}: not a PNG.");

        var (len, type, data) = ReadChunk(source, label, keep: true, expect: ("IHDR", 13));
        if (!type.AsSpan().SequenceEqual("IHDR"u8) || len != 13)
            throw new FormatException($"{label}: the PNG does not start with its header.");
        var width = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(data), int.MaxValue);
        var height = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)), int.MaxValue);
        int depth = data[8], colour = data[9], compression = data[10], filter = data[11], interlace = data[12];
        if (width is <= 0 or > PngHeader.MaxSide || height is <= 0 or > PngHeader.MaxSide)
            throw new FormatException($"{label}: {width}×{height} is not a usable texture size.");
        if (compression != 0 || filter != 0 || interlace > 1)
            throw new FormatException($"{label}: an unknown PNG compression, filter or interlace method.");
        if (depth != 8 || colour is not (2 or 6))
            throw new PngUnsupportedException($"{label}: a {depth}-bit PNG of colour type {colour}; only 8-bit RGB and RGBA are read row by row.");
        if (interlace == 1)
            throw new PngUnsupportedException($"{label}: an interlaced PNG; its rows do not arrive in order.");

        while (true)
        {
            var (clen, ctype, _) = ReadChunk(source, label, keep: false, stopAtIdat: true);
            var t = ctype.AsSpan();
            if (t.SequenceEqual("IDAT"u8))
                return new PngRows(source, label, width, height, colour == 6 ? 4 : 3, (clen, ctype));
            if (t.SequenceEqual("IEND"u8))
                throw new FormatException($"{label}: the PNG has no image data.");
            if (t.SequenceEqual("tRNS"u8))
                throw new PngUnsupportedException($"{label}: a PNG with a transparent colour key.");
            // Critical chunks have an upper-case first letter (spec 5.4); one we
            // do not know changes how the image must be read, so it is refused.
            if (ctype[0] is >= (byte)'A' and <= (byte)'Z' && !t.SequenceEqual("PLTE"u8))
                throw new PngUnsupportedException($"{label}: a critical PNG chunk this reader does not know ({System.Text.Encoding.ASCII.GetString(ctype)}).");
        }
    }

    /// <summary>The next row as RGBA, <see cref="Width"/> × 4 bytes. False after the last row.</summary>
    /// <exception cref="FormatException">The image data is damaged or ends early.</exception>
    public bool ReadRow(Span<byte> rgba)
    {
        if (_next >= Height) return false;
        if (rgba.Length < Width * 4) throw new ArgumentException("row buffer too small", nameof(rgba));

        (_row, _prior) = (_prior, _row);
        try
        {
            _inflate.ReadExactly(_row);
        }
        // ZLibException is an IOException: inflate rejecting data whose chunk CRC
        // was valid. Damage, whatever the layer that noticed it.
        catch (Exception e) when (e is EndOfStreamException or InvalidDataException or IOException)
        {
            throw new FormatException($"{_label}: the image data is damaged or ends at row {_next} of {Height}.");
        }
        Unfilter(_row, _prior, _bpp, _next == 0, _label, _next);

        var src = _row.AsSpan(1);
        if (_bpp == 4) src.CopyTo(rgba);
        else
            for (int x = 0, s = 0, d = 0; x < Width; x++, s += 3, d += 4)
            {
                rgba[d] = src[s];
                rgba[d + 1] = src[s + 1];
                rgba[d + 2] = src[s + 2];
                rgba[d + 3] = 255;
            }
        _next++;
        return true;
    }

    // Spec section 9: each row starts with its filter type; "prior" is the row
    // above (all zero for the first), "a" the byte one pixel to the left.
    private static void Unfilter(byte[] row, byte[] prior, int bpp, bool first, string label, int y)
    {
        var filter = row[0];
        var n = row.Length;
        switch (filter)
        {
            case 0:
                break;
            case 1:
                for (var i = 1 + bpp; i < n; i++) row[i] += row[i - bpp];
                break;
            case 2:
                if (!first) for (var i = 1; i < n; i++) row[i] += prior[i];
                break;
            case 3:
                for (var i = 1; i < n; i++)
                {
                    var a = i > bpp ? row[i - bpp] : 0;
                    var b = first ? 0 : prior[i];
                    row[i] += (byte)((a + b) >> 1);
                }
                break;
            case 4:
                for (var i = 1; i < n; i++)
                {
                    int a = i > bpp ? row[i - bpp] : 0;
                    int b = first ? 0 : prior[i];
                    int c = i > bpp && !first ? prior[i - bpp] : 0;
                    row[i] += Paeth(a, b, c);
                }
                break;
            default:
                throw new FormatException($"{label}: row {y} has an unknown filter type {filter}.");
        }
    }

    private static byte Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return (byte)(pa <= pb && pa <= pc ? a : pb <= pc ? b : c);
    }

    /// <summary>
    /// Checks the CRC of the image-data chunk reading stopped in. Each earlier
    /// chunk was checked as it ended; this reads the rest of the current one as
    /// compressed bytes (never inflated), so a reader that stops early still
    /// never hands back rows from a damaged chunk.
    /// </summary>
    /// <exception cref="FormatException">The chunk is damaged.</exception>
    public void VerifyChunk() => _idat.FinishChunk();

    public void Dispose()
    {
        _inflate.Dispose();
        _idat.Dispose();
    }

    private static bool ReadFully(Stream s, Span<byte> buf)
    {
        var read = 0;
        while (read < buf.Length)
        {
            var n = s.Read(buf[read..]);
            if (n == 0) return false;
            read += n;
        }
        return true;
    }

    /// <summary>
    /// One chunk: length, type and (when <paramref name="keep"/>) data, CRC
    /// checked. With <paramref name="stopAtIdat"/> an IDAT's data is left unread
    /// for <see cref="IdatStream"/>.
    /// </summary>
    private static (int Length, byte[] Type, byte[] Data) ReadChunk(Stream s, string label, bool keep, bool stopAtIdat = false,
        (string Type, uint Length)? expect = null)
    {
        Span<byte> head = stackalloc byte[8];
        if (!ReadFully(s, head)) throw new FormatException($"{label}: the PNG ends inside its chunk list.");
        var length = BinaryPrimitives.ReadUInt32BigEndian(head);
        if (length > MaxChunkBytes) throw new FormatException($"{label}: a PNG chunk of {length} bytes.");
        var type = head[4..].ToArray();
        foreach (var b in type)
            if (b is not (>= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z'))
                throw new FormatException($"{label}: a PNG chunk with an invalid name.");
        if (stopAtIdat && type.AsSpan().SequenceEqual("IDAT"u8)) return ((int)length, type, []);
        // A header's length is known; checking it first means a file claiming a
        // 64 MB header costs nothing before it is refused.
        if (expect is { } x && (length != x.Length || System.Text.Encoding.ASCII.GetString(type) != x.Type))
            throw new FormatException($"{label}: the PNG does not start with its header.");

        var crc = Crc32.Update(Crc32.Seed, type);
        byte[] data = keep ? new byte[length] : [];
        var buf = keep ? data : new byte[Math.Min(length, 81920u)];
        var left = (long)length;
        while (left > 0)
        {
            var span = keep ? data.AsSpan((int)(length - left), (int)left) : buf.AsSpan(0, (int)Math.Min(left, buf.Length));
            var n = s.Read(span);
            if (n == 0) throw new FormatException($"{label}: the PNG ends inside a chunk.");
            crc = Crc32.Update(crc, span[..n]);
            left -= n;
        }
        CheckCrc(s, crc, label, type);
        return ((int)length, type, data);
    }

    private static void CheckCrc(Stream s, uint crc, string label, byte[] type)
    {
        Span<byte> stored = stackalloc byte[4];
        if (!ReadFully(s, stored)) throw new FormatException($"{label}: the PNG ends inside a chunk.");
        if (BinaryPrimitives.ReadUInt32BigEndian(stored) != Crc32.Finish(crc))
            throw new FormatException($"{label}: the PNG's {System.Text.Encoding.ASCII.GetString(type)} chunk is damaged (checksum).");
    }

    /// <summary>The concatenated data of consecutive IDAT chunks, each CRC-checked as it ends.</summary>
    private sealed class IdatStream(Stream source, string label, int firstLength) : Stream
    {
        private long _left = firstLength;
        private uint _crc = Crc32.Update(Crc32.Seed, "IDAT"u8);
        private bool _done;

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            Span<byte> head = stackalloc byte[8];
            while (!_done && _left == 0)
            {
                CheckCrc(source, _crc, label, "IDAT"u8.ToArray());
                if (!ReadFully(source, head)) throw new FormatException($"{label}: the PNG ends inside its image data.");
                if (!head[4..].SequenceEqual("IDAT"u8)) { _done = true; break; }
                var length = BinaryPrimitives.ReadUInt32BigEndian(head);
                if (length > MaxChunkBytes) throw new FormatException($"{label}: a PNG chunk of {length} bytes.");
                _left = length;
                _crc = Crc32.Update(Crc32.Seed, "IDAT"u8);
            }
            if (_done || buffer.Length == 0) return 0;
            var n = source.Read(buffer[..(int)Math.Min(buffer.Length, _left)]);
            if (n == 0) throw new FormatException($"{label}: the PNG ends inside its image data.");
            _crc = Crc32.Update(_crc, buffer[..n]);
            _left -= n;
            return n;
        }

        public void FinishChunk()
        {
            if (_done) return;
            var buf = new byte[81920];
            while (_left > 0)
            {
                var n = source.Read(buf, 0, (int)Math.Min(buf.Length, _left));
                if (n == 0) throw new FormatException($"{label}: the PNG ends inside its image data.");
                _crc = Crc32.Update(_crc, buf.AsSpan(0, n));
                _left -= n;
            }
            CheckCrc(source, _crc, label, "IDAT"u8.ToArray());
            _done = true;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>The PNG CRC (ISO 3309 / ITU-T V.42), as the specification's annex gives it.</summary>
    internal static class Crc32
    {
        public const uint Seed = 0xFFFFFFFF;
        private static readonly uint[] Table = Build();

        private static uint[] Build()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }

        public static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        public static uint Finish(uint crc) => crc ^ 0xFFFFFFFF;
    }
}
