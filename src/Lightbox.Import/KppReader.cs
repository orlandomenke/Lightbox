using System.Buffers.Binary;
using System.Text;
using System.Xml.Linq;

namespace Lightbox.Import;

/// <summary>
/// Krita brush preset (.kpp): a PNG whose text chunk `preset` carries the
/// paintop XML. Krita's engines don't map 1:1 onto Lightbox's, so this
/// imports the shared parameter subset — size, spacing, opacity, flow — with
/// a round tip. (Tips referenced as external Krita resources aren't inside
/// the file.)
/// </summary>
public static class KppReader
{
    public static ImportedBrush Read(string fileName, byte[] data)
    {
        var xml = FindPresetChunk(data)
                  ?? throw new FormatException("KPP: no `preset` text chunk in the PNG.");
        var doc = XDocument.Parse(xml);
        var root = doc.Root ?? throw new FormatException("KPP: empty preset XML.");

        var name = root.Attribute("name")?.Value;
        double size = 24, spacing = 0.15, opacity = 1, flow = 1;

        foreach (var param in root.Elements("param"))
        {
            var key = param.Attribute("name")?.Value ?? "";
            var value = param.Value;
            if (key.Contains("opacity", StringComparison.OrdinalIgnoreCase) && TryNum(value, out var o))
                opacity = Math.Clamp(o, 0.01, 1);
            else if (key.Contains("flow", StringComparison.OrdinalIgnoreCase) && TryNum(value, out var f))
                flow = Math.Clamp(f, 0.01, 1);
            else if (key.Equals("Spacing", StringComparison.OrdinalIgnoreCase) && TryNum(value, out var sp) && sp > 0)
                spacing = Math.Clamp(sp, 0.02, 2);
            else if (key == "brush_definition")
            {
                // Embedded <Brush ... diameter="40" spacing="0.1"> XML.
                try
                {
                    var brush = XDocument.Parse(value).Root;
                    if (TryNum(brush?.Attribute("diameter")?.Value, out var d) && d > 0) size = d;
                    if (TryNum(brush?.Attribute("spacing")?.Value, out var bs) && bs > 0)
                        spacing = Math.Clamp(bs, 0.02, 2);
                }
                catch
                {
                    // partial import stays partial
                }
            }
        }

        return new ImportedBrush(
            string.IsNullOrWhiteSpace(name) ? fileName : name!,
            TipPngBase64: null,
            size,
            spacing,
            opacity,
            flow);
    }

    private static bool TryNum(string? s, out double value) =>
        double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out value);

    /// <summary>Walk PNG chunks for tEXt/iTXt with the keyword `preset`.</summary>
    private static string? FindPresetChunk(byte[] data)
    {
        if (data.Length < 8) return null;
        var pos = 8; // PNG signature
        while (pos + 12 <= data.Length)
        {
            var len = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos));
            var type = Encoding.ASCII.GetString(data, pos + 4, 4);
            var body = pos + 8;
            if (len < 0 || body + len > data.Length) return null;

            if (type is "tEXt" or "iTXt")
            {
                var nul = Array.IndexOf(data, (byte)0, body, len);
                if (nul > body)
                {
                    var keyword = Encoding.ASCII.GetString(data, body, nul - body);
                    if (keyword == "preset")
                    {
                        var textStart = type == "iTXt" ? nul + 5 : nul + 1; // iTXt: flags + empty lang/translated
                        while (textStart < body + len && data[textStart] == 0) textStart++;
                        return Encoding.UTF8.GetString(data, textStart, body + len - textStart);
                    }
                }
            }
            pos = body + len + 4; // + CRC
        }
        return null;
    }
}
