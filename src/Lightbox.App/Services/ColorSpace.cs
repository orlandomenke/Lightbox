namespace Lightbox.App.Services;

/// <summary>
/// Color-model conversions for the color docker. All RGB values are 0..1,
/// hue is 0..360, every other channel 0..1. CMYK is the naive (non-ICC)
/// conversion — fine for picking, not for print proofing.
/// </summary>
public static class ColorSpace
{
    // ---- hex ---------------------------------------------------------------

    public static (double R, double G, double B)? HexToRgb(string hex)
    {
        var s = hex.Trim().TrimStart('#');
        if (s.Length != 6 || !int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v))
            return null;
        return (((v >> 16) & 0xff) / 255.0, ((v >> 8) & 0xff) / 255.0, (v & 0xff) / 255.0);
    }

    public static string RgbToHex(double r, double g, double b) =>
        $"#{ToByte(r):x2}{ToByte(g):x2}{ToByte(b):x2}";

    private static int ToByte(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);

    // ---- HSV ---------------------------------------------------------------

    public static (double H, double S, double V) RgbToHsv(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        return (HueOf(r, g, b, max, d), max <= 0 ? 0 : d / max, max);
    }

    public static (double R, double G, double B) HsvToRgb(double h, double s, double v)
    {
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);
        var c = v * s;
        return FromChroma(h, c, v - c);
    }

    // ---- HSL ---------------------------------------------------------------

    public static (double H, double S, double L) RgbToHsl(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        var l = (max + min) / 2;
        var s = d <= 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
        return (HueOf(r, g, b, max, d), s, l);
    }

    public static (double R, double G, double B) HslToRgb(double h, double s, double l)
    {
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        return FromChroma(h, c, l - c / 2);
    }

    // ---- CMYK --------------------------------------------------------------

    public static (double C, double M, double Y, double K) RgbToCmyk(double r, double g, double b)
    {
        var k = 1 - Math.Max(r, Math.Max(g, b));
        if (k >= 1) return (0, 0, 0, 1);
        return ((1 - r - k) / (1 - k), (1 - g - k) / (1 - k), (1 - b - k) / (1 - k), k);
    }

    public static (double R, double G, double B) CmykToRgb(double c, double m, double y, double k)
    {
        c = Math.Clamp(c, 0, 1);
        m = Math.Clamp(m, 0, 1);
        y = Math.Clamp(y, 0, 1);
        k = Math.Clamp(k, 0, 1);
        return ((1 - c) * (1 - k), (1 - m) * (1 - k), (1 - y) * (1 - k));
    }

    // ---- shared pieces -------------------------------------------------------

    private static double HueOf(double r, double g, double b, double max, double d)
    {
        if (d <= 0) return 0;
        var h = 60 * (max == r
            ? ((g - b) / d + (g < b ? 6 : 0))
            : max == g
                ? (b - r) / d + 2
                : (r - g) / d + 4);
        return h % 360;
    }

    private static (double R, double G, double B) FromChroma(double h, double c, double m)
    {
        h = ((h % 360) + 360) % 360 / 60;
        var x = c * (1 - Math.Abs(h % 2 - 1));
        var (r, g, b) = (int)h switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return (r + m, g + m, b + m);
    }
}
