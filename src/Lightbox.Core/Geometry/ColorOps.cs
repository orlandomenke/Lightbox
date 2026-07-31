namespace Lightbox.Core.Geometry;

public static class ColorOps
{
    public static (byte R, byte G, byte B) HexToRgb(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 3) h = $"{h[0]}{h[0]}{h[1]}{h[1]}{h[2]}{h[2]}";
        if (h.Length != 6) throw new FormatException($"Not a hex color: \"{hex}\"");
        var v = Convert.ToInt32(h, 16);
        return ((byte)((v >> 16) & 255), (byte)((v >> 8) & 255), (byte)(v & 255));
    }

    public static string RgbToHex(double r, double g, double b)
    {
        static int C(double n) => (int)Math.Round(Math.Clamp(n, 0, 255));
        return $"#{C(r):x2}{C(g):x2}{C(b):x2}";
    }

    public static string LerpColor(string a, string b, double t)
    {
        var (ar, ag, ab) = HexToRgb(a);
        var (br, bg, bb) = HexToRgb(b);
        return RgbToHex(
            GeometryOps.Lerp(ar, br, t),
            GeometryOps.Lerp(ag, bg, t),
            GeometryOps.Lerp(ab, bb, t));
    }
}
