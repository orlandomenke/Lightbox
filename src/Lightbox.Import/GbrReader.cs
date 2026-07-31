using System.Buffers.Binary;
using System.Text;

namespace Lightbox.Import;

/// <summary>
/// GIMP brush (.gbr) v2 — the tip format Krita also uses. Big-endian header:
/// headerSize, version, width, height, bytesPerPixel (1 gray / 4 RGBA),
/// magic "GIMP", spacing (percent), name (headerSize−28 bytes), then raw
/// pixel data. Gray value = paint amount → tip alpha.
/// </summary>
public static class GbrReader
{
    public static List<ImportedBrush> Read(byte[] data)
    {
        var brush = ReadOne(data, 0, out _);
        return [brush];
    }

    internal static ImportedBrush ReadOne(byte[] data, int offset, out int consumed)
    {
        var s = data.AsSpan(offset);
        if (s.Length < 28) throw new FormatException("GBR: file too short.");
        var headerSize = (int)BinaryPrimitives.ReadUInt32BigEndian(s);
        var version = BinaryPrimitives.ReadUInt32BigEndian(s[4..]);
        var width = (int)BinaryPrimitives.ReadUInt32BigEndian(s[8..]);
        var height = (int)BinaryPrimitives.ReadUInt32BigEndian(s[12..]);
        var bytesPerPixel = (int)BinaryPrimitives.ReadUInt32BigEndian(s[16..]);
        if (version < 2) throw new FormatException("GBR: only version 2 brushes are supported.");
        if (BinaryPrimitives.ReadUInt32BigEndian(s[20..]) != 0x47494D50) // "GIMP"
            throw new FormatException("GBR: bad magic.");
        var spacing = (int)BinaryPrimitives.ReadUInt32BigEndian(s[24..]);
        if (width is <= 0 or > 4096 || height is <= 0 or > 4096 || bytesPerPixel is not (1 or 4))
            throw new FormatException("GBR: unsupported dimensions or depth.");

        var name = Encoding.UTF8.GetString(s[28..headerSize]).TrimEnd('\0');
        var dataLen = width * height * bytesPerPixel;
        if (s.Length < headerSize + dataLen) throw new FormatException("GBR: truncated pixel data.");
        var pixels = s.Slice(headerSize, dataLen).ToArray();

        var tip = BrushImport.EncodeTip(width, height, (x, y) =>
            bytesPerPixel == 1
                ? pixels[y * width + x]
                : pixels[(y * width + x) * 4 + 3]);

        consumed = headerSize + dataLen;
        return new ImportedBrush(
            string.IsNullOrWhiteSpace(name) ? "GIMP brush" : name,
            tip,
            Math.Max(width, height),
            BrushImport.NormalizeSpacing(spacing));
    }
}

/// <summary>
/// GIMP brush pipe (.gih): a text header (name line, then count + params
/// line), followed by that many concatenated GBR brushes. Imports the first
/// few tips as individual presets.
/// </summary>
public static class GihReader
{
    private const int MaxTips = 8;

    public static List<ImportedBrush> Read(byte[] data)
    {
        var firstNl = Array.IndexOf(data, (byte)'\n');
        var secondNl = firstNl < 0 ? -1 : Array.IndexOf(data, (byte)'\n', firstNl + 1);
        if (firstNl < 0 || secondNl < 0) throw new FormatException("GIH: missing text header.");

        var name = Encoding.UTF8.GetString(data, 0, firstNl).Trim();
        var paramsLine = Encoding.UTF8.GetString(data, firstNl + 1, secondNl - firstNl - 1).Trim();
        var count = int.TryParse(paramsLine.Split(' ')[0], out var n) ? n : 0;
        if (count <= 0) throw new FormatException("GIH: bad brush count.");

        var brushes = new List<ImportedBrush>();
        var offset = secondNl + 1;
        for (var i = 0; i < Math.Min(count, MaxTips); i++)
        {
            var brush = GbrReader.ReadOne(data, offset, out var consumed);
            brushes.Add(brush with { Name = count > 1 ? $"{name} {i + 1}" : name });
            offset += consumed;
        }
        return brushes;
    }
}
