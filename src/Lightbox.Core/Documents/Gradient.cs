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
/// <remarks>
/// <see cref="Alpha"/> is the fallback track. When the gradient declares
/// <see cref="Gradient.AlphaStops"/>, opacity comes from there instead and
/// this value is not read — see <see cref="GradientAlphaStop"/> for why the
/// two are separate.
/// </remarks>
public sealed class GradientStop
{
    public double Position { get; set; }

    public string Color { get; set; } = "#000000";

    public double Alpha { get; set; } = 1.0;

    /// <summary>A copy holding no reference in common with this one.</summary>
    public GradientStop Clone() => (GradientStop)MemberwiseClone();
}

/// <summary>
/// One opacity stop, at its own position along the axis.
/// </summary>
/// <remarks>
/// Opacity gets a track of its own because it genuinely changes at different
/// places from colour. A sky fading out at the top while going orange in the
/// middle needs two stops in one place and one in another, and tying them
/// together forces a stop into the colour ramp that has no business being
/// there — you end up authoring a colour you did not want in order to place
/// an opacity you did.
///
/// This is why Photoshop and Krita both show two rows of markers, and why
/// this model has two lists. Empty means "no separate track": the colour
/// stops' own <see cref="GradientStop.Alpha"/> is used, so a gradient made
/// before this existed writes no new key and renders exactly as it did.
/// </remarks>
public sealed class GradientAlphaStop
{
    public double Position { get; set; }

    public double Alpha { get; set; } = 1.0;

    /// <summary>A copy holding no reference in common with this one.</summary>
    public GradientAlphaStop Clone() => (GradientAlphaStop)MemberwiseClone();
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

    /// <summary>
    /// Opacity along the axis, independent of colour.
    /// </summary>
    /// <remarks>
    /// Null is the ordinary state — the same discipline the camera follows —
    /// and means the colour stops carry their own alpha. A gradient that never
    /// separates the two writes no key at all, so a document made before this
    /// existed and one made after are byte-identical when they mean the same
    /// thing.
    /// </remarks>
    public List<GradientAlphaStop>? AlphaStops { get; set; }

    /// <summary>Whether opacity is authored separately from colour. Derived; never serialized.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasAlphaTrack => AlphaStops is { Count: > 0 };

    /// <summary>The alpha track, creating it on first use.</summary>
    public List<GradientAlphaStop> EnsureAlphaTrack() => AlphaStops ??= [];

    /// <summary>A copy holding no reference in common with this one.</summary>
    public Gradient Clone()
    {
        var copy = (Gradient)MemberwiseClone();
        copy.Stops = Stops.Select(s => s.Clone()).ToList();
        copy.AlphaStops = AlphaStops?.Select(s => s.Clone()).ToList();
        return copy;
    }
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
        var sampled = SampleColorStops(stops, t);
        // The alpha track overrides the colour stops' own alpha when it
        // exists. When it does not — which is every gradient authored before
        // the track did — nothing changes.
        return gradient.HasAlphaTrack
            ? (sampled.R, sampled.G, sampled.B, SampleAlpha(gradient.AlphaStops!, t))
            : sampled;
    }

    private static (byte R, byte G, byte B, byte A) SampleColorStops(
        IReadOnlyList<GradientStop> stops, double t)
    {
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

    /// <summary>Opacity from the alpha track. Same shape as the colour walk.</summary>
    public static byte SampleAlpha(IReadOnlyList<GradientAlphaStop> track, double t)
    {
        var stops = track.OrderBy(s => s.Position).ToList();
        if (stops.Count == 0) return 255;
        if (stops.Count == 1 || t <= stops[0].Position) return Byte(stops[0].Alpha);
        if (t >= stops[^1].Position) return Byte(stops[^1].Alpha);

        for (var i = 0; i < stops.Count - 1; i++)
        {
            var a = stops[i];
            var b = stops[i + 1];
            if (t < a.Position || t > b.Position) continue;
            var span = b.Position - a.Position;
            var f = span <= 1e-9 ? 1.0 : (t - a.Position) / span;
            // Alpha is coverage, not light: it interpolates as it is written.
            return Byte(a.Alpha * (1 - f) + b.Alpha * f);
        }
        return Byte(stops[^1].Alpha);
    }

    private static byte Byte(double alpha) => (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255);

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
