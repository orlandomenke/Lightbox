using System.Globalization;
using System.Text;

namespace Lightbox.Core.Documents;

/// <summary>One named colour in a palette. The name is what makes a palette
/// a vocabulary rather than a row of squares — "skin shadow", not "#b07a5e".</summary>
public sealed class Swatch
{
    /// <summary>
    /// Stable identity, so a stroke can reference the swatch rather than
    /// copying its colour. Recolouring the swatch then recolours the art.
    /// </summary>
    public string Id { get; set; } = Ids.NewId("sw");

    public string Color { get; set; } = "#000000";

    public string? Name { get; set; }
}

/// <summary>
/// A named set of colours belonging to the document.
///
/// Per document rather than per application, deliberately. A character's
/// palette is part of the character — the whole point of a character-based
/// project is that every animation of it shares one — and a palette that
/// lived in app settings could not travel with the file or be reviewed
/// alongside the art.
/// </summary>
public sealed class Palette
{
    public string Id { get; set; } = Ids.NewId("pal");

    public string Name { get; set; } = "Palette";

    /// <summary>Preferred grid width when shown as swatches. GIMP records it; so do we.</summary>
    public int Columns { get; set; } = 8;

    public List<Swatch> Swatches { get; set; } = [];
}

/// <summary>
/// Reading and writing GIMP's <c>.gpl</c>, which is the one palette format
/// everything speaks — Krita, Inkscape, Aseprite, Blender and GIMP itself all
/// read and write it, and it is plain text. Inventing a format here would
/// mean an artist's existing palettes could not come with them.
/// </summary>
public static class GimpPalette
{
    public static string Write(Palette palette)
    {
        var text = new StringBuilder();
        text.Append("GIMP Palette\n");
        text.Append(CultureInfo.InvariantCulture, $"Name: {palette.Name}\n");
        text.Append(CultureInfo.InvariantCulture, $"Columns: {Math.Max(1, palette.Columns)}\n");
        text.Append("#\n");
        foreach (var swatch in palette.Swatches)
        {
            var (r, g, b) = Rgb(swatch.Color);
            text.Append(CultureInfo.InvariantCulture, $"{r,3} {g,3} {b,3}");
            if (!string.IsNullOrWhiteSpace(swatch.Name)) text.Append('\t').Append(swatch.Name);
            text.Append('\n');
        }
        return text.ToString();
    }

    /// <summary>
    /// Parse a <c>.gpl</c>. Forgiving on purpose: files in the wild have
    /// missing headers, CRLF, tabs or runs of spaces between components, and
    /// comment lines anywhere. A palette that half-loads is more useful than
    /// an exception.
    /// </summary>
    public static Palette Read(string text, string fallbackName = "Imported")
    {
        var palette = new Palette { Name = fallbackName, Swatches = [] };
        var sawColumns = false;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('#')) continue;
            if (line.StartsWith("GIMP Palette", StringComparison.OrdinalIgnoreCase)) continue;

            if (line.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
            {
                var name = line[5..].Trim();
                if (name.Length > 0) palette.Name = name;
                continue;
            }
            if (line.StartsWith("Columns:", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(line[8..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cols)
                    && cols > 0)
                {
                    palette.Columns = cols;
                    sawColumns = true;
                }
                continue;
            }

            // "R G B<tab>optional name" — the name may itself contain spaces,
            // so only the first three fields are parsed as numbers.
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) continue;
            if (!byte.TryParse(parts[0], out var r)) continue;
            if (!byte.TryParse(parts[1], out var g)) continue;
            if (!byte.TryParse(parts[2], out var b)) continue;

            var label = parts.Length > 3 ? string.Join(' ', parts[3..]) : null;
            palette.Swatches.Add(new Swatch
            {
                Color = $"#{r:x2}{g:x2}{b:x2}",
                Name = string.IsNullOrWhiteSpace(label) ? null : label,
            });
        }

        if (!sawColumns) palette.Columns = Math.Clamp((int)Math.Ceiling(Math.Sqrt(palette.Swatches.Count)), 1, 16);
        return palette;
    }

    /// <summary>#rrggbb (or #rgb) to bytes; anything unparseable reads as black.</summary>
    public static (byte R, byte G, byte B) Rgb(string hex)
    {
        var s = hex.TrimStart('#');
        if (s.Length == 3)
        {
            s = $"{s[0]}{s[0]}{s[1]}{s[1]}{s[2]}{s[2]}";
        }
        if (s.Length < 6) return (0, 0, 0);
        return (Hex(s, 0), Hex(s, 2), Hex(s, 4));

        static byte Hex(string s, int at) =>
            byte.TryParse(s.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
                ? v : (byte)0;
    }
}
