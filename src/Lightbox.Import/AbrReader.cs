using System.Buffers.Binary;
using System.Text;

namespace Lightbox.Import;

/// <summary>
/// Photoshop brush file (.abr). Extracts SAMPLED brushes (tip bitmaps +
/// spacing): versions 1/2 fully; version 6+ by walking the `8BIM samp`
/// section (the layout GIMP's loader established: sub-version 1 → 47-byte
/// brush preamble, sub-version 2 → 301). Computed/bristle brushes have no
/// bitmap and are skipped. 8-bit depth only.
/// </summary>
public static class AbrReader
{
    public static List<ImportedBrush> Read(byte[] data)
    {
        if (data.Length < 4) throw new FormatException("ABR: file too short.");
        var version = BinaryPrimitives.ReadInt16BigEndian(data);
        return version switch
        {
            1 or 2 => ReadV12(data, version),
            >= 6 => ReadV6(data),
            _ => throw new FormatException($"ABR: unsupported version {version}."),
        };
    }

    // ---- v1 / v2 --------------------------------------------------------------

    private static List<ImportedBrush> ReadV12(byte[] data, int version)
    {
        var brushes = new List<ImportedBrush>();
        int count = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(2));
        var pos = 4;
        for (var i = 0; i < count && pos + 6 <= data.Length; i++)
        {
            int type = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(pos));
            var blockLen = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos + 2));
            var block = pos + 6;
            pos = block + blockLen;
            if (type != 2) continue; // 1 = computed brush: no bitmap to import

            var p = block + 4; // misc
            int spacing = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(p));
            p += 2;
            var name = $"ABR brush {i + 1}";
            if (version == 2)
            {
                var chars = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(p));
                p += 4;
                name = Encoding.BigEndianUnicode.GetString(data, p, chars * 2).TrimEnd('\0');
                p += chars * 2;
            }
            p += 1; // antialiasing
            p += 8; // short bounds
            var top = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(p));
            var left = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(p + 4));
            var bottom = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(p + 8));
            var right = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(p + 12));
            p += 16;
            int depth = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(p));
            p += 2;
            var width = right - left;
            var height = bottom - top;
            if (depth != 8 || width is <= 0 or > 4096 || height is <= 0 or > 4096) continue;
            if (p + width * height > data.Length) continue;

            AddTip(brushes, name, data.AsSpan(p, width * height).ToArray(), width, height, spacing);
        }
        return brushes;
    }

    // ---- v6+ ------------------------------------------------------------------

    private static List<ImportedBrush> ReadV6(byte[] data)
    {
        var brushes = new List<ImportedBrush>();
        int subVersion = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(2));
        // 8BIM samp section header
        if (data.Length < 16 || Encoding.ASCII.GetString(data, 4, 8) != "8BIMsamp")
            throw new FormatException("ABR v6: missing 8BIM samp section.");
        var sectionLen = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(12));
        var pos = 16;
        var end = Math.Min(data.Length, 16 + sectionLen);
        var index = 0;

        while (pos + 4 <= end)
        {
            var brushLen = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos));
            pos += 4;
            var next = pos + ((brushLen + 3) & ~3);
            var p = pos + (subVersion == 1 ? 47 : 301);
            index++;
            if (p + 19 <= end && p + 19 <= pos + brushLen)
            {
                var top = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(p));
                var left = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(p + 4));
                var bottom = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(p + 8));
                var right = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(p + 12));
                int depth = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(p + 16));
                var compression = data[p + 18];
                var width = right - left;
                var height = bottom - top;
                p += 19;

                if (depth == 8 && width is > 0 and <= 4096 && height is > 0 and <= 4096)
                {
                    var pixels = compression == 0
                        ? ReadRaw(data, p, width, height)
                        : PackBits.Decode(data, p, width, height, pos + brushLen);
                    if (pixels is not null)
                    {
                        AddTip(brushes, $"ABR brush {index}", pixels, width, height, spacingPercent: 0);
                    }
                }
            }
            pos = next;
        }
        return brushes;
    }

    private static byte[]? ReadRaw(byte[] data, int p, int width, int height)
    {
        var len = width * height;
        return p + len <= data.Length ? data.AsSpan(p, len).ToArray() : null;
    }

    private static void AddTip(List<ImportedBrush> brushes, string name, byte[] gray, int width, int height, int spacingPercent)
    {
        var tip = BrushImport.EncodeTip(width, height, (x, y) => gray[y * width + x]);
        brushes.Add(new ImportedBrush(
            name,
            tip,
            Math.Max(width, height),
            BrushImport.NormalizeSpacing(spacingPercent)));
    }
}
