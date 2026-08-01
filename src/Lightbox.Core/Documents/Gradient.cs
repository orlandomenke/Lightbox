namespace Lightbox.Core.Documents;

public enum GradientKind
{
    Linear,
    Radial,
}

/// <summary>What happens outside the gradient's own axis.</summary>
public enum GradientSpread
{
    /// <summary>Hold the end colours. What a gradient fill normally wants.</summary>
    Pad,

    Repeat,

    /// <summary>Repeat, alternating direction, so the seams do not show.</summary>
    Mirror,
}

/// <summary>
/// One colour stop. <see cref="Position"/> is 0–1 along the gradient's axis.
/// Alpha is carried separately from the colour so a stop can fade out without
/// the hex string having to grow a fourth pair.
/// </summary>
public sealed class GradientStop
{
    public double Position { get; set; }

    public string Color { get; set; } = "#000000";

    public double Alpha { get; set; } = 1.0;
}

/// <summary>
/// A named gradient in the document, referenced by id from the strokes that
/// paint it — the same arrangement as clip regions and brush tips, and for
/// the same reason: the record has to re-render identically from the file
/// alone, so the definition travels with the document rather than living in
/// app settings.
/// </summary>
public sealed class Gradient
{
    public string Id { get; set; } = Ids.NewId("grad");

    public string Name { get; set; } = "Gradient";

    public GradientKind Kind { get; set; } = GradientKind.Linear;

    public GradientSpread Spread { get; set; } = GradientSpread.Pad;

    public List<GradientStop> Stops { get; set; } =
    [
        new() { Position = 0, Color = "#000000" },
        new() { Position = 1, Color = "#ffffff" },
    ];
}

/// <summary>Colour along a gradient. Pure, so a re-render always agrees.</summary>
public static class GradientOps
{
    /// <summary>Stops in position order. The stored list is not kept sorted by callers.</summary>
    public static IReadOnlyList<GradientStop> Ordered(Gradient gradient) =>
        gradient.Stops.OrderBy(s => s.Position).ToList();

    /// <summary>
    /// The colour at <paramref name="t"/> along the axis, as
    /// (r, g, b, a) bytes.
    ///
    /// Interpolation is in LINEAR light, not in sRGB. Mixing sRGB values
    /// directly is the reason naive gradients go muddy through the middle —
    /// a red-to-green ramp passes through a dark olive rather than a bright
    /// one — and this app already treats colour as light everywhere else
    /// (see the pigment model), so doing it differently here would look wrong
    /// beside a painted stroke.
    /// </summary>
    public static (byte R, byte G, byte B, byte A) Sample(Gradient gradient, double t)
    {
        var stops = Ordered(gradient);
        if (stops.Count == 0) return (0, 0, 0, 0);

        t = Wrap(t, gradient.Spread);
        if (stops.Count == 1 || t <= stops[0].Position) return Bytes(stops[0], 1, stops[0], 0);
        if (t >= stops[^1].Position) return Bytes(stops[^1], 1, stops[^1], 0);

        for (var i = 0; i < stops.Count - 1; i++)
        {
            var a = stops[i];
            var b = stops[i + 1];
            if (t < a.Position || t > b.Position) continue;
            var span = b.Position - a.Position;
            // Coincident stops are a hard colour boundary, which is a real
            // thing to author — a stripe, a cel-shading terminator.
            var f = span <= 1e-9 ? 1.0 : (t - a.Position) / span;
            return Bytes(a, 1 - f, b, f);
        }
        return Bytes(stops[^1], 1, stops[^1], 0);
    }

    private static double Wrap(double t, GradientSpread spread)
    {
        switch (spread)
        {
            case GradientSpread.Repeat:
                t -= Math.Floor(t);
                return t;
            case GradientSpread.Mirror:
                var period = Math.Abs(t) % 2.0;
                return period > 1.0 ? 2.0 - period : period;
            default:
                return Math.Clamp(t, 0, 1);
        }
    }

    private static (byte R, byte G, byte B, byte A) Bytes(
        GradientStop a, double wa, GradientStop b, double wb)
    {
        var (ar, ag, ab) = GimpPalette.Rgb(a.Color);
        var (br, bg, bb) = GimpPalette.Rgb(b.Color);
        return (
            FromLinear(ToLinear(ar) * wa + ToLinear(br) * wb),
            FromLinear(ToLinear(ag) * wa + ToLinear(bg) * wb),
            FromLinear(ToLinear(ab) * wa + ToLinear(bb) * wb),
            // Alpha is coverage, not light: it interpolates as it is written.
            (byte)Math.Round(Math.Clamp(a.Alpha * wa + b.Alpha * wb, 0, 1) * 255));
    }

    /// <summary>sRGB byte to linear 0–1.</summary>
    public static double ToLinear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>Linear 0–1 back to an sRGB byte.</summary>
    public static byte FromLinear(double linear)
    {
        var c = Math.Clamp(linear, 0, 1);
        var s = c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
        return (byte)Math.Round(Math.Clamp(s, 0, 1) * 255);
    }
}
