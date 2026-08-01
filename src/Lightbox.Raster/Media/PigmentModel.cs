using SkiaSharp;

namespace Lightbox.Raster.Media;

/// <summary>
/// Kubelka-Munk pigment optics — the difference between paint and coloured film.
///
/// Source-over says "the result is a weighted average of the two colours", which
/// is what a photographic dissolve does. Paint does not average: light enters the
/// layer, is absorbed and scattered inside it, some of it reaches the surface
/// underneath and comes back up through the layer again. A yellow glaze over a
/// blue underpainting reads green because the yellow removes blue light from what
/// the blue layer sends back, not because yellow and blue were averaged.
///
/// This is the two-constant model: each pigment has an absorption coefficient K
/// and a scattering coefficient S per channel, and the reflectance of a layer of
/// finite thickness X over a backing of reflectance Rg is the standard hyperbolic
/// solution (Kubelka 1948). See <see cref="Layer"/> for the arithmetic and for the
/// degenerate cases, which are all handled by one algebraic rearrangement rather
/// than by a thicket of branches.
///
/// Everything inside is LINEAR reflectance in 0..1. sRGB byte values are display
/// encoding, not light; running Kubelka-Munk on gamma-encoded numbers gives the
/// wrong answer by roughly a factor of two in the midtones. Conversion in and out
/// happens at the API boundary — see <see cref="FromLinear"/>/<see cref="ToLinear"/>.
///
/// Determinism: no state, no RNG, no lookup that depends on anything but the
/// arguments. The only library calls are Sqrt/Exp/Pow, which are stable for a
/// given input on a given runtime, so repeated renders are bit-identical.
/// </summary>
public readonly struct Pigment
{
    /// <summary>
    /// Scattering of a fully hiding pigment. Chosen empirically: at this optical
    /// depth even a near-white pigment (whose scattering has to do all the work,
    /// since it barely absorbs) reaches its mass tone to inside one 8-bit level
    /// over a black backing. Larger buys nothing; much smaller lets white ghost.
    /// </summary>
    private const double FullHidingScatter = 512.0;

    /// <summary>Linear reflectance at which S hits <see cref="FullHidingScatter"/>: 512/513.</summary>
    private const double FullHidingReflectance = FullHidingScatter / (FullHidingScatter + 1.0);

    /// <summary>
    /// Absorption a glaze keeps when scattering has gone to zero. Without this,
    /// <c>hiding = 0</c> would mean K = S = 0 — an invisible layer — whereas a
    /// transparent glaze is precisely a layer that absorbs without scattering.
    /// At thickness 1 a neutral grey glaze passes about half the backdrop through.
    /// </summary>
    private const double GlazeAbsorption = 0.5;

    /// <summary>
    /// Real paint films neither absorb every photon nor reflect every photon, and
    /// K/S is singular at both ends. Clamping reflectance to this margin keeps the
    /// coefficients finite. It sits below one 8-bit level — the first sRGB step is
    /// linear 3.04e-4 — so a clamped black still encodes to exactly 0 and a clamped
    /// white to exactly 255, which is why it cannot be loosened much: at 8e-4 a
    /// black pigment would render as (2,2,2).
    /// </summary>
    private const double ReflectanceMargin = 1e-4;

    /// <summary>coth overflows in double past here, and is 1.0 to every bit anyway.</summary>
    private const double CothSaturates = 350.0;

    /// <summary>Below this the series expansion of coth is more accurate than Exp, and 0 is legal.</summary>
    private const double CothSeries = 1e-4;

    /// <summary>
    /// Thicknesses at or below this are treated as no paint at all. Not just an
    /// optimisation: the X -> 0 limit runs through 1/X, which overflows to
    /// infinity for denormal thicknesses and turns the ratio into a NaN. Even the
    /// strongest absorber this model can build shifts an 8-bit channel by less
    /// than a millionth of a level at 1e-9, so nothing visible is being discarded.
    /// </summary>
    private const double NoPaint = 1e-9;

    private readonly double _kR, _kG, _kB;
    private readonly double _sR, _sG, _sB;

    // sqrt(K(K+2S)) per channel. Precomputed because Over runs per pixel while a
    // Pigment is built once per stroke; it also happens to be the one form of the
    // Kubelka-Munk "b" term that stays finite as S goes to zero.
    private readonly double _bR, _bG, _bB;

    private Pigment(double kR, double kG, double kB, double sR, double sG, double sB)
    {
        _kR = kR; _kG = kG; _kB = kB;
        _sR = sR; _sG = sG; _sB = sB;
        _bR = Beta(kR, sR);
        _bG = Beta(kG, sG);
        _bB = Beta(kB, sB);
    }

    /// <summary>
    /// Derive K and S from a chosen colour. <paramref name="hiding"/> 0..1 is
    /// opacity in the pigment sense: 0 is a transparent glaze, 1 is fully hiding.
    ///
    /// The colour is read as the pigment's mass tone — the reflectance an
    /// infinitely thick film of it would have — which fixes the ratio K/S at
    /// (1-R)^2 / 2R, the Kubelka-Munk inversion. <paramref name="hiding"/> then
    /// picks how much pigment is actually there.
    ///
    /// Calibrating that dial needs a reference, because how much of the backdrop
    /// a layer swallows depends on its colour: a dark pigment hides by absorbing
    /// and needs almost no scattering, a white one has nothing but scattering to
    /// hide with and needs a great deal. White is therefore the honest yardstick.
    /// A white layer over black reflects SX/(SX+1), so <paramref name="hiding"/>
    /// is read as that reflectance in display terms — hiding 0.5 is the S that
    /// would put a white layer at mid grey over black — and inverted to
    /// S = L/(1-L). Coloured pigments then hide at least that much, never less.
    /// The dial spends most of its travel where the eye can see a difference,
    /// which a raw linear or geometric ramp in S does not.
    ///
    /// Two honest consequences worth knowing before wiring this up. A white pigment
    /// absorbs almost nothing, so at hiding 0 it is genuinely invisible — whites
    /// have to be opaque, that is physics and not a gap in the mapping. And only
    /// hiding 1 hands back the colour that was picked: at 0.99 the worst channel is
    /// 3 levels off, at 0.95 it is 13, at 0.85 it is 38. Some of that is the glaze
    /// absorption floor darkening the mass tone, most of it is simply that a
    /// not-quite-hiding layer lets the backdrop through, which is the point. If the
    /// UI needs "the swatch is the colour you get", pin hiding at 1 and let
    /// thickness carry the transparency.
    /// </summary>
    public static Pigment FromColor(SKColor color, double hiding)
    {
        var l = Decode(Math.Clamp(hiding, 0.0, 1.0));
        var s = l >= FullHidingReflectance ? FullHidingScatter : l / (1.0 - l);

        // K/S is set by the colour; the absolute K is S plus a floor, so that the
        // ratio converges to the colour's own as the layer becomes hiding, while a
        // zero-scattering glaze still absorbs.
        var scale = s + GlazeAbsorption;
        return new Pigment(
            KOverS(ToLinear(color.Red)) * scale,
            KOverS(ToLinear(color.Green)) * scale,
            KOverS(ToLinear(color.Blue)) * scale,
            s, s, s);
    }

    /// <summary>
    /// Build a pigment straight from coefficients. Escape hatch for callers that
    /// have measured K and S rather than picked a colour; also what <see cref="Mix"/>
    /// uses. Negative coefficients are meaningless and are clamped to zero.
    /// </summary>
    public static Pigment FromCoefficients(
        double kR, double kG, double kB, double sR, double sG, double sB) =>
        new(Math.Max(kR, 0), Math.Max(kG, 0), Math.Max(kB, 0),
            Math.Max(sR, 0), Math.Max(sG, 0), Math.Max(sB, 0));

    /// <summary>
    /// This pigment at <paramref name="thickness"/> (0..1) composited over
    /// <paramref name="backdrop"/>.
    ///
    /// The backdrop is treated as an opaque backing — that is what a reflectance
    /// model means — and its alpha is passed through untouched. Coverage in the
    /// alpha sense is a separate question; ask <see cref="Coverage"/> for it.
    ///
    /// Thickness 0 returns the backdrop bit-for-bit, not "very nearly the backdrop":
    /// a dab of zero paint must not shift the canvas by a rounding step.
    /// </summary>
    public SKColor Over(SKColor backdrop, double thickness)
    {
        var x = Math.Clamp(thickness, 0.0, 1.0);
        if (x <= NoPaint) return backdrop;

        // Over an opaque backing this is plain Kubelka-Munk, and it is the
        // overwhelmingly common case — every pixel of an already-painted
        // region. Kept as a straight-line fast path because the alternative
        // below costs a second full evaluation.
        if (backdrop.Alpha == 255)
        {
            return new SKColor(
                FromLinear(Layer(_kR, _sR, _bR, x, ToLinear(backdrop.Red))),
                FromLinear(Layer(_kG, _sG, _bG, x, ToLinear(backdrop.Green))),
                FromLinear(Layer(_kB, _sB, _bB, x, ToLinear(backdrop.Blue))),
                255);
        }

        // Kubelka-Munk answers "what colour is this film over that backing",
        // which presumes there is a backing. Where the layer is transparent
        // there is not: keeping the backdrop's alpha would return a fully
        // computed colour at alpha 0, so a stroke onto blank canvas would
        // vanish. The pigment sees its own mass tone there, and it adds
        // opacity in proportion to how much it hides.
        var backing = Blend(MassTone, backdrop, backdrop.Alpha / 255.0);
        var alpha = backdrop.Alpha / 255.0;
        var laid = alpha + (1.0 - alpha) * Coverage(x);

        return new SKColor(
            FromLinear(Layer(_kR, _sR, _bR, x, ToLinear(backing.Red))),
            FromLinear(Layer(_kG, _sG, _bG, x, ToLinear(backing.Green))),
            FromLinear(Layer(_kB, _sB, _bB, x, ToLinear(backing.Blue))),
            (byte)Math.Round(Math.Clamp(laid, 0.0, 1.0) * 255));
    }

    /// <summary>Linear-light interpolation, t=0 giving <paramref name="a"/>.</summary>
    private static SKColor Blend(SKColor a, SKColor b, double t) => new(
        FromLinear(ToLinear(a.Red) + (ToLinear(b.Red) - ToLinear(a.Red)) * t),
        FromLinear(ToLinear(a.Green) + (ToLinear(b.Green) - ToLinear(a.Green)) * t),
        FromLinear(ToLinear(a.Blue) + (ToLinear(b.Blue) - ToLinear(a.Blue)) * t),
        255);

    /// <summary>
    /// How much of the backdrop this layer hides at <paramref name="thickness"/>,
    /// 0 (invisible) to 1 (the backdrop cannot be seen at all). Defined the way a
    /// paint maker defines it: the contrast left between a white and a black
    /// backing, worst channel, subtracted from one.
    /// </summary>
    public double Coverage(double thickness)
    {
        var x = Math.Clamp(thickness, 0.0, 1.0);
        if (x <= NoPaint) return 0.0;

        var contrast = Math.Max(
            Math.Max(Contrast(_kR, _sR, _bR, x), Contrast(_kG, _sG, _bG, x)),
            Contrast(_kB, _sB, _bB, x));
        return 1.0 - contrast;
    }

    /// <summary>
    /// The reflectance of an infinitely thick film of this pigment — what the
    /// pigment "is", independent of anything under it.
    /// </summary>
    public SKColor MassTone => new(
        FromLinear(Infinite(_kR, _sR, _bR)),
        FromLinear(Infinite(_kG, _sG, _bG)),
        FromLinear(Infinite(_kB, _sB, _bB)));

    /// <summary>
    /// Blend two pigments, for smudge and brush pickup. t=0 gives a, t=1 gives b.
    ///
    /// Mixing is linear in K and in S, which is the whole reason for carrying two
    /// constants: it is why mixed yellow and blue paint comes out green while
    /// mixed yellow and blue light comes out grey. Interpolating the mass tones
    /// instead would throw that away and give the source-over answer.
    /// </summary>
    public static Pigment Mix(in Pigment a, in Pigment b, double t)
    {
        var u = Math.Clamp(t, 0.0, 1.0);
        var v = 1.0 - u;
        return new Pigment(
            v * a._kR + u * b._kR,
            v * a._kG + u * b._kG,
            v * a._kB + u * b._kB,
            v * a._sR + u * b._sR,
            v * a._sG + u * b._sG,
            v * a._sB + u * b._sB);
    }

    /// <summary>
    /// Reflectance of a layer (K, S) of thickness X over a backing of reflectance
    /// Rg. The textbook form is
    ///
    ///     a = (S+K)/S,  b = sqrt(a^2 - 1)
    ///     R = (1 - Rg(a - b·coth(bSX))) / (a - Rg + b·coth(bSX))
    ///
    /// which is unusable as written: a and b both blow up as S goes to zero and
    /// (a - b) is then a catastrophic subtraction of two huge nearly-equal numbers.
    /// Multiplying numerator and denominator through by S clears every division by
    /// S and leaves three finite quantities:
    ///
    ///     P = S + K              (Sa)
    ///     beta = sqrt(K(K+2S))   (Sb)
    ///     Q = beta·coth(beta·X)  (Sb·coth(bSX))
    ///     R = (S - Rg(P - Q)) / (P + Q - Rg·S)
    ///
    /// All four degenerate cases then fall out of the same expression:
    ///
    ///   S -> 0 (pure absorber): P and beta both tend to K and the expression
    ///     collapses to Rg·exp(-2KX) — Beer-Lambert, light down through the film,
    ///     off the backing and back up. No branch needed, it is the limit.
    ///   K -> 0 (pure scatterer): beta is 0, Q is 1/X by the series below, giving
    ///     the familiar SX/(SX+1) over a black backing.
    ///   X -> 0: Q diverges and R tends to Rg. The caller cuts this off at
    ///     <see cref="NoPaint"/>, both so the backdrop comes back bit-identical
    ///     rather than merely close, and because 1/X overflows for denormal X.
    ///   beta·X large (a hiding layer): coth is 1, Q is beta, and R reduces to
    ///     S/(P+beta) = the mass tone, with Rg cancelling out entirely. A fully
    ///     hiding layer cannot see what is underneath it.
    ///
    /// The denominator is P + Q - Rg·S >= S(1-Rg) + Q > 0 for X > 0, so there is
    /// no division to guard. The final clamp only catches float drift at the ends.
    /// </summary>
    private static double Layer(double k, double s, double beta, double x, double rg)
    {
        var p = s + k;
        var q = Coth(beta, x);
        var r = (s - rg * (p - q)) / (p + q - rg * s);
        return r <= 0.0 ? 0.0 : r >= 1.0 ? 1.0 : r;
    }

    /// <summary>beta·coth(beta·X), the only transcendental in the hot path.</summary>
    private static double Coth(double beta, double x)
    {
        var z = beta * x;
        // coth(z) ~ 1/z + z/3 near zero; this is also the K = 0 case, where the
        // leading term 1/X is the whole answer and beta·coth would be 0·infinity.
        if (z < CothSeries) return 1.0 / x + beta * z / 3.0;
        if (z > CothSaturates) return beta;
        var e = Math.Exp(-2.0 * z);
        return beta * (1.0 + e) / (1.0 - e);
    }

    /// <summary>Reflectance at infinite thickness: S/(S+K+beta), the stable form of a-b.</summary>
    private static double Infinite(double k, double s, double beta)
    {
        var d = s + k + beta;
        return d <= 0.0 ? 0.0 : s / d;
    }

    /// <summary>Reflectance difference this layer leaves between a white and a black backing.</summary>
    private static double Contrast(double k, double s, double beta, double x) =>
        Layer(k, s, beta, x, 1.0) - Layer(k, s, beta, x, 0.0);

    /// <summary>
    /// The Kubelka-Munk inversion: the K/S a pigment must have for an infinitely
    /// thick film of it to reflect <paramref name="r"/>.
    /// </summary>
    private static double KOverS(double r)
    {
        var c = Math.Clamp(r, ReflectanceMargin, 1.0 - ReflectanceMargin);
        var d = 1.0 - c;
        return d * d / (2.0 * c);
    }

    private static double Beta(double k, double s) => Math.Sqrt(k * (k + 2.0 * s));

    // --- sRGB <-> linear light -------------------------------------------------
    // IEC 61966-2-1. The 256-entry decode table is exact (one entry per possible
    // input) and costs one indexed load instead of a Pow in the inner loop; the
    // encode has to stay a Pow because its input is continuous.

    private static readonly double[] DecodeTable = BuildDecode();

    private static double[] BuildDecode()
    {
        var t = new double[256];
        for (var i = 0; i < 256; i++) t[i] = Decode(i / 255.0);
        return t;
    }

    /// <summary>The sRGB EOTF on a continuous 0..1 signal.</summary>
    private static double Decode(double u) =>
        u <= 0.04045 ? u / 12.92 : Math.Pow((u + 0.055) / 1.055, 2.4);

    /// <summary>sRGB byte to linear reflectance 0..1.</summary>
    public static double ToLinear(byte srgb) => DecodeTable[srgb];

    /// <summary>Linear reflectance 0..1 to sRGB byte. Exact inverse of <see cref="ToLinear"/>.</summary>
    public static byte FromLinear(double linear)
    {
        var v = linear <= 0.0 ? 0.0 : linear >= 1.0 ? 1.0 : linear;
        var u = v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1.0 / 2.4) - 0.055;
        return (byte)(u * 255.0 + 0.5);
    }
}
