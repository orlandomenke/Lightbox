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
internal sealed class PsdCursor
{
    private int _pos;

    public PsdCursor(byte[] data, int pos, int end)
    {
        Data = data;
        End = Math.Clamp(end, 0, data.Length);
        Pos = pos;
    }

    public PsdCursor(byte[] data) : this(data, 0, data.Length) { }

    public byte[] Data { get; }

    /// <summary>
    /// Where the next read starts. <b>Setting it outside the section is a
    /// format error, not a silent seek.</b>
    /// </summary>
    /// <remarks>
    /// Guarded rather than a plain field, because this is the invariant every
    /// other bounds check here assumes. <see cref="Has"/> asks whether
    /// <c>Pos + count</c> fits; a negative <c>Pos</c> makes that always true, so
    /// one unchecked assignment turns every subsequent check into a no-op and
    /// the next read indexes the array out of bounds. A crafted PSB channel
    /// length reached exactly that, through an <c>(int)</c> truncation in the
    /// caller — so the guard lives here, once, rather than at each call site
    /// that computes a position.
    /// </remarks>
    public int Pos
    {
        get => _pos;
        set
        {
            if (value < 0 || value > End)
                throw new FormatException($"PSD: cursor moved to {value}, outside 0..{End}.");
            _pos = value;
        }
    }

    public int End { get; }

    public int Remaining => Math.Max(0, End - Pos);

    /// <summary>Whether <paramref name="count"/> more bytes are available.</summary>
    /// <remarks>
    /// <b>Subtraction, not addition</b>, and that is the whole point. Every length
    /// in a PSB is a 64-bit field an artist did not write. <c>Pos + count</c> in
    /// 32 bits wraps negative for a large one and passes a "does it fit" test
    /// written the obvious way; widening to <see cref="long"/> only moves the
    /// wrap, because a length of <see cref="long.MaxValue"/> overflows that too.
    /// <c>End - Pos</c> cannot overflow — both are non-negative and no larger
    /// than the array — so comparing the count against it is total.
    /// </remarks>
    public bool Has(long count) =>
        count >= 0 && _pos >= 0 && _pos <= End && count <= End - _pos;

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
        if (!Has(count))
            throw new FormatException($"PSD: skipping {count} at byte {Pos} runs past the section.");
        Pos += (int)count; // Has() has established this fits in the section.
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
