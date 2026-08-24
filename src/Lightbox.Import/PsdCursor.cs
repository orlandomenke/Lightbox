using System.Buffers.Binary;
using System.Text;

namespace Lightbox.Import;

/// <summary>
/// A bounds-checked sequential reader over big-endian PSD bytes.
/// </summary>
/// <remarks>
/// Every field in a PSD is big-endian and most are read one after another, so a
/// cursor keeps the parser readable and — more to the point — puts the bounds
/// check in <em>one</em> place. A format parser reading files nobody validated is
/// exactly where an off-by-one becomes an out-of-range crash on somebody's
/// artwork, so overrunning throws <see cref="FormatException"/> ("this file is
/// wrong") rather than <c>IndexOutOfRangeException</c> ("Lightbox is wrong").
/// </remarks>
internal sealed class PsdCursor(byte[] data, int pos, int end)
{
    public byte[] Data { get; } = data;

    public int Pos { get; set; } = pos;

    public int End { get; private set; } = Math.Min(end, data.Length);

    public PsdCursor(byte[] data) : this(data, 0, data.Length) { }

    public int Remaining => Math.Max(0, End - Pos);

    public bool Has(long count) => count >= 0 && Pos + count <= End;

    private void Need(int count)
    {
        if (!Has(count))
            throw new FormatException($"PSD: truncated at byte {Pos} (needed {count} more).");
    }

    public byte U8()
    {
        Need(1);
        return Data[Pos++];
    }

    public int U16()
    {
        Need(2);
        var v = BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan(Pos));
        Pos += 2;
        return v;
    }

    public short I16()
    {
        Need(2);
        var v = BinaryPrimitives.ReadInt16BigEndian(Data.AsSpan(Pos));
        Pos += 2;
        return v;
    }

    public int I32()
    {
        Need(4);
        var v = BinaryPrimitives.ReadInt32BigEndian(Data.AsSpan(Pos));
        Pos += 4;
        return v;
    }

    public long I64()
    {
        Need(8);
        var v = BinaryPrimitives.ReadInt64BigEndian(Data.AsSpan(Pos));
        Pos += 8;
        return v;
    }

    /// <summary>An int32, or an int64 in a PSB — the difference the format makes everywhere.</summary>
    public long Size(bool wide) => wide ? I64() : I32();

    public string Ascii(int count)
    {
        Need(count);
        var s = Encoding.ASCII.GetString(Data, Pos, count);
        Pos += count;
        return s;
    }

    /// <summary>A length-prefixed string padded so the whole field is a multiple of <paramref name="align"/>.</summary>
    public string PascalString(int align)
    {
        var length = U8();
        Need(length);
        var s = Encoding.Latin1.GetString(Data, Pos, length);
        Pos += length;
        var written = 1 + length;
        var pad = (align - written % align) % align;
        Skip(Math.Min(pad, Remaining));
        return s;
    }

    public void Skip(long count)
    {
        if (count < 0) throw new FormatException($"PSD: negative length {count} at byte {Pos}.");
        Need((int)Math.Min(count, int.MaxValue));
        Pos += (int)count;
    }

    /// <summary>Peek four ASCII bytes without moving, for signature sniffing.</summary>
    public string PeekAscii4() =>
        Has(4) ? Encoding.ASCII.GetString(Data, Pos, 4) : "";

    /// <summary>Read a length-prefixed section and return a cursor bounded to it.</summary>
    public PsdCursor Section(bool wide, out long length)
    {
        length = Size(wide);
        if (length < 0 || !Has(length))
            throw new FormatException($"PSD: section length {length} at byte {Pos} runs past the file.");
        var section = new PsdCursor(Data, Pos, Pos + (int)length);
        Pos += (int)length;
        return section;
    }

    /// <summary>Narrow this cursor's end, for a section parsed in place.</summary>
    public PsdCursor Bounded(long length)
    {
        if (length < 0 || !Has(length))
            throw new FormatException($"PSD: length {length} at byte {Pos} runs past the file.");
        return new PsdCursor(Data, Pos, Pos + (int)length);
    }
}
