using System.Buffers.Binary;
using System.Text;

namespace Lightbox.Import;

/// <summary>A reference into a keyed archive's object table.</summary>
internal readonly record struct PlistUid(int Index);

/// <summary>
/// Apple's binary property list (<c>bplist00</c>), read into plain values.
/// </summary>
/// <remarks>
/// <para>
/// Written from the format's public description — the layout comment at the head of
/// Apple's open-source <c>CFBinaryPList.c</c>: an eight-byte header, the objects, a
/// table of their offsets, and a 32-byte trailer saying how wide an offset and an
/// object reference are, how many objects there are, which is the top one and where
/// the table starts. Each object opens with a marker byte whose high nibble is its
/// type and whose low nibble is its size or count.
/// </para>
/// <para>
/// <b>Every number in the file is the file's to lie about</b>, and a reference
/// costs the file one byte while costing the reader whatever it points at. Four
/// things answer that:
/// </para>
/// <list type="bullet">
/// <item>A count is checked against the bytes that would have to hold it, and an
/// offset against the buffer, before either is used; no container is sized from a
/// count.</item>
/// <item><b>An object is built once.</b> A second reference to it gets the value
/// already built, so ninety-nine thousand references to one four-megabyte blob are
/// one blob. A reference to an object that is still being built is a container
/// that contains itself, and is refused there.</item>
/// <item><b>What is built is paid for from a budget</b> set by the size of the file
/// (<see cref="BudgetPerByte"/> bytes of value per byte of file). Many table entries
/// may point at one offset, which building once does not see; the budget does.</item>
/// <item>Nesting is limited.</item>
/// </list>
/// <para>
/// A file that breaks any of these is a <see cref="FormatException"/>.
/// </para>
/// <para>
/// Values come back as <c>bool</c>, <c>long</c>, <c>double</c>, <c>string</c>,
/// <c>byte[]</c>, <see cref="PlistUid"/>, <c>List&lt;object?&gt;</c> and
/// <c>Dictionary&lt;string, object?&gt;</c>. Dates are read as their number; a
/// dictionary key that is not a string is skipped.
/// </para>
/// </remarks>
internal sealed class BinaryPlist
{
    /// <summary>How deep containers may nest. A brush's settings nest three deep.</summary>
    private const int MaxDepth = 24;

    /// <summary>How many values one file may build, however it arranges its references.</summary>
    private const int MaxValues = 100_000;

    /// <summary>
    /// Bytes of built value allowed per byte of file. A real brush's settings come to
    /// about three: its strings double as UTF-16 and each value has some overhead.
    /// </summary>
    internal const int BudgetPerByte = 16;

    /// <summary>What the smallest file may still build: the fixed cost of a few values.</summary>
    private const int BudgetFloor = 64 * 1024;

    private static readonly object Building = new();

    private readonly byte[] _data;
    private readonly int _offsetSize;
    private readonly int _refSize;
    private readonly int _count;
    private readonly int _table;
    private readonly Dictionary<int, object?> _done = [];
    private long _budget;
    private int _built;

    private BinaryPlist(byte[] data, int offsetSize, int refSize, int count, int table)
    {
        _data = data;
        _offsetSize = offsetSize;
        _refSize = refSize;
        _count = count;
        _table = table;
        _budget = Math.Max(BudgetFloor, (long)data.Length * BudgetPerByte);
    }

    /// <summary>Read the top object of a binary property list.</summary>
    public static object? Read(byte[] data)
    {
        if (data.Length < 8 + 32 || !data.AsSpan(0, 8).SequenceEqual("bplist00"u8))
        {
            throw new FormatException("Not a binary property list.");
        }
        var trailer = data.AsSpan(data.Length - 32);
        int offsetSize = trailer[6];
        int refSize = trailer[7];
        var count = BinaryPrimitives.ReadUInt64BigEndian(trailer[8..]);
        var top = BinaryPrimitives.ReadUInt64BigEndian(trailer[16..]);
        var table = BinaryPrimitives.ReadUInt64BigEndian(trailer[24..]);

        if (offsetSize is < 1 or > 8 || refSize is < 1 or > 8) throw Bad("its trailer");
        // The table has to fit between the header and the trailer, which bounds the
        // object count by the size of the file rather than by what the file says.
        var room = (ulong)(data.Length - 32 - 8);
        if (table < 8 || table > (ulong)(data.Length - 32)) throw Bad("its offset table");
        if (count == 0 || count > room / (ulong)offsetSize) throw Bad("its object count");
        if (table + count * (ulong)offsetSize > (ulong)(data.Length - 32)) throw Bad("its offset table");
        if (top >= count) throw Bad("its top object");

        var plist = new BinaryPlist(data, offsetSize, refSize, (int)count, (int)table);
        return plist.Object((int)top, depth: 0);
    }

    private static FormatException Bad(string what) =>
        new($"The property list is damaged: {what} does not fit the file.");

    /// <summary>The value at a table index: built the first time, handed back after.</summary>
    private object? Object(int index, int depth)
    {
        if (index < 0 || index >= _count) throw Bad("an object reference");
        if (_done.TryGetValue(index, out var known))
        {
            if (ReferenceEquals(known, Building))
            {
                throw new FormatException("The property list is damaged: a value contains itself.");
            }
            return known;
        }
        _done[index] = Building;
        var value = Build(index, depth);
        _done[index] = value;
        return value;
    }

    /// <summary>Take <paramref name="bytes"/> from the budget, or refuse the file.</summary>
    private void Charge(long bytes)
    {
        _budget -= bytes;
        if (_budget < 0) throw new FormatException("The property list unpacks to more than a brush does.");
    }

    private object? Build(int index, int depth)
    {
        if (depth > MaxDepth) throw new FormatException("The property list nests deeper than a brush does.");
        if (++_built > MaxValues) throw new FormatException("The property list holds more than a brush does.");
        Charge(32);

        var offset = Unsigned(_table + index * _offsetSize, _offsetSize);
        if (offset < 8 || offset >= (ulong)_table) throw Bad("an object");
        var at = (int)offset;
        var marker = _data[at];
        var type = marker >> 4;
        var low = marker & 0x0F;
        var p = at + 1;

        switch (type)
        {
            case 0x0:
                return low switch { 0x8 => false, 0x9 => true, _ => null };
            case 0x1:
            {
                var size = 1 << low;
                if (size > 8) size = 8;      // 16-byte ints exist; the low eight bytes are the value
                var skip = (1 << low) - size;
                Need(p, 1 << low);
                return (long)Unsigned(p + skip, size);
            }
            case 0x2:
            case 0x3:
            {
                var size = type == 0x3 ? 8 : 1 << low;
                Need(p, size);
                return size switch
                {
                    4 => BinaryPrimitives.ReadSingleBigEndian(_data.AsSpan(p)),
                    8 => BinaryPrimitives.ReadDoubleBigEndian(_data.AsSpan(p)),
                    _ => throw Bad("a number"),
                };
            }
            case 0x4:
            {
                var n = Count(low, ref p);
                Need(p, n);
                Charge(n);
                return _data.AsSpan(p, n).ToArray();
            }
            case 0x5:
            {
                var n = Count(low, ref p);
                Need(p, n);
                Charge(n * 2L);
                return Encoding.ASCII.GetString(_data, p, n);
            }
            case 0x6:
            {
                var n = Count(low, ref p);
                Need(p, n * 2L);
                Charge(n * 2L);
                return Encoding.BigEndianUnicode.GetString(_data, p, n * 2);
            }
            case 0x8:
            {
                var size = low + 1;
                if (size > 8) throw Bad("a reference");
                Need(p, size);
                var value = Unsigned(p, size);
                return new PlistUid(value > int.MaxValue ? -1 : (int)value);
            }
            case 0xA:
            {
                var n = Count(low, ref p);
                Need(p, (long)n * _refSize);
                Charge(n * 8L);
                var list = new List<object?>();
                for (var i = 0; i < n; i++) list.Add(Object(Ref(p + i * _refSize), depth + 1));
                return list;
            }
            case 0xD:
            {
                var n = Count(low, ref p);
                Need(p, (long)n * _refSize * 2);
                Charge(n * 48L);
                var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (var i = 0; i < n; i++)
                {
                    if (Object(Ref(p + i * _refSize), depth + 1) is not string key) continue;
                    dict[key] = Object(Ref(p + (n + i) * _refSize), depth + 1);
                }
                return dict;
            }
            default:
                throw Bad("an object of a kind this reader does not know");
        }
    }

    /// <summary>A size or count: the low nibble, or an integer object after the marker when that is 15.</summary>
    private int Count(int low, ref int p)
    {
        if (low != 0x0F) return low;
        Need(p, 1);
        var marker = _data[p];
        if (marker >> 4 != 0x1) throw Bad("a count");
        var size = 1 << (marker & 0x0F);
        if (size > 8) throw Bad("a count");
        Need(p + 1, size);
        var value = Unsigned(p + 1, size);
        p += 1 + size;
        // Nothing in the file can be longer than the file. This is the check that
        // turns "two thousand million entries" into a refusal before any list is made.
        if (value > (ulong)_data.Length) throw Bad("a count");
        return (int)value;
    }

    private int Ref(int at)
    {
        var value = Unsigned(at, _refSize);
        if (value >= (ulong)_count) throw Bad("an object reference");
        return (int)value;
    }

    private void Need(int at, long length)
    {
        // Objects live before the offset table; anything running past it is not one.
        if (length < 0 || at < 0 || at + length > _table) throw Bad("an object");
    }

    private ulong Unsigned(int at, int size)
    {
        if (at < 0 || (long)at + size > _data.Length) throw Bad("a number");
        ulong value = 0;
        for (var i = 0; i < size; i++) value = value << 8 | _data[at + i];
        return value;
    }
}
