using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>Which of the two inks the brush ring is drawn in.</summary>
public enum CursorInk
{
    /// <summary>A dark line, for a ring sitting on light artwork.</summary>
    Dark,

    /// <summary>A light line, for a ring sitting on dark artwork.</summary>
    Light,
}

/// <summary>
/// The brush ring's one line, and which ink keeps it visible.
/// </summary>
/// <remarks>
/// <para>
/// <b>The ring used to be two lines — a dark outline with a light one inside
/// it</b> — which is the standard trick for staying visible over anything, and
/// it costs 2.4 px of the drawing all the way round the brush. At the size an
/// artist actually paints at, that is a band thick enough to hide the edge you
/// are aiming at, and hiding the edge is the one thing the ring exists not to
/// do. One line, in whichever ink survives the artwork beneath it, buys the
/// same visibility for less than half the ink.
/// </para>
/// <para>
/// <b>Why not a difference blend, which needs no sample at all.</b> Inverting
/// the destination is the obvious way to guarantee contrast and it has one
/// value where it fails completely: mid-grey inverts to mid-grey, so the ring
/// disappears over exactly the tone a painter spends most of their time in. A
/// sampled choice between two inks has no such hole — the worst case is a ring
/// at half contrast rather than no ring at all.
/// </para>
/// <para>
/// <b>Sampled at the pointer, not around the rim.</b> The ring is one colour,
/// so a big brush whose centre is on paper and whose rim crosses a black line
/// will fade where it crosses. That is the accepted cost of a single line: the
/// centre is where the artist is looking, and the alternative is either a
/// per-rim-point read on every hover or the two-line band this replaced.
/// </para>
/// <para>
/// A pure function with its constants beside it, for the reason
/// <see cref="CanvasCursor"/> and <see cref="PickRing"/> are: the canvas control
/// cannot be driven by synthetic input in this environment, so a decision made
/// inside it ships unguarded, and this one is testable with no window.
/// </para>
/// </remarks>
public static class CursorContrast
{
    /// <summary>The line's width in screen pixels when nobody has said otherwise.</summary>
    /// <remarks>
    /// Thinner than the 1.2 px each of the two lines it replaces, because one
    /// line at full weight reads heavier than either half of a pair did.
    /// </remarks>
    /// <remarks>
    /// <b>double, not float, all the way through this type.</b> These reach a
    /// settings file an artist reads, and a float round trip turns a stored 2.2
    /// into 2.20000004768372 — in the JSON, and in the box on the page. Skia
    /// wants a float and gets one at the last moment, which is the only place
    /// the narrowing is invisible.
    /// </remarks>
    public const double DefaultWidth = 1.1;

    /// <summary>The narrowest line worth offering, in screen pixels.</summary>
    /// <remarks>
    /// Below about half a pixel anti-aliasing is doing all the work: the line
    /// stops getting thinner and starts getting fainter, which is the other
    /// control. Two settings that do the same thing is how a preferences page
    /// stops meaning anything.
    /// </remarks>
    public const double MinWidth = 0.5;

    /// <summary>The widest, in screen pixels.</summary>
    /// <remarks>
    /// Three pixels is thicker than the pair this replaced, and it is offered
    /// because a high-DPI panel and poor eyesight are both real — not because
    /// it is a good default.
    /// </remarks>
    public const double MaxWidth = 3;

    /// <summary>How opaque the dark ink is, 0 to 1, when nobody has said otherwise.</summary>
    /// <remarks>
    /// <b>Two thirds, not full.</b> A line the drawing shows through is one an
    /// artist aims past; a solid one is one they look at. This is the number to
    /// turn when the ring is either lost or in the way, and it is why the
    /// control is called contrast rather than opacity: what is being set is how
    /// far the ring stands off the artwork, and the alpha is only how that is
    /// achieved.
    /// </remarks>
    public const double DefaultContrast = 0.65;

    /// <summary>The faintest ring worth offering.</summary>
    /// <remarks>
    /// A quarter is already very faint over a low-contrast painting. Below it
    /// the ring is not subtle, it is missing — and a control whose bottom end
    /// cannot be told apart from a broken build is a support question.
    /// </remarks>
    public const double MinContrast = 0.25;

    /// <summary>The strongest: solid ink, for a panel that needs it.</summary>
    public const double MaxContrast = 1;

    /// <summary>
    /// How much more opaque the light ink is than the dark one at the same
    /// setting.
    /// </summary>
    /// <remarks>
    /// <b>Matched by eye, not by number.</b> White over dark artwork reads
    /// fainter than black over light artwork at the same alpha, so one control
    /// moving both equally would drift out of balance at one end. Carried here
    /// so the two inks stay a pair however the artist sets them.
    /// </remarks>
    public const double LightUplift = 0.06;

    /// <summary>Below this luminance the ring goes <see cref="CursorInk.Light"/>.</summary>
    public const float ToLight = 0.45f;

    /// <summary>Above this luminance the ring goes <see cref="CursorInk.Dark"/>.</summary>
    /// <remarks>
    /// <b>The gap between the two is deliberate.</b> A single threshold makes the
    /// ring flicker between inks while the pointer sits on a gradient or a dithered
    /// edge, which is far more distracting than either ink is. Inside the gap the
    /// previous choice stands, and both inks are legible there anyway.
    /// </remarks>
    public const float ToDark = 0.55f;

    /// <summary>Perceived lightness of a colour, 0 (black) to 1 (white).</summary>
    /// <remarks>
    /// Rec. 709 weights on the gamma-encoded channels rather than linearised
    /// ones. The question here is "does a thin dark line show up on this", which
    /// is a question about the display's tones, not about physical light — and
    /// linearising would push the crossover down to about 0.18, putting the ring
    /// in white over most of a mid-toned painting.
    /// </remarks>
    public static float Luminance(SKColor color) =>
        (0.2126f * color.Red + 0.7152f * color.Green + 0.0722f * color.Blue) / 255f;

    /// <summary>
    /// The ink for artwork of this colour, given the ink the ring is wearing now.
    /// </summary>
    public static CursorInk Choose(SKColor under, CursorInk previous)
    {
        var luma = Luminance(under);
        if (luma >= ToDark) return CursorInk.Dark;
        if (luma <= ToLight) return CursorInk.Light;
        return previous;
    }

    /// <summary>The colour to stroke with, at a given contrast.</summary>
    public static SKColor ColorFor(CursorInk ink, double contrast = DefaultContrast)
    {
        var c = ClampContrast(contrast);
        if (ink == CursorInk.Light) c = Math.Min(MaxContrast, c + LightUplift);
        var alpha = (byte)Math.Round(c * 255);
        return ink == CursorInk.Light
            ? new SKColor(255, 255, 255, alpha)
            : new SKColor(0, 0, 0, alpha);
    }

    /// <summary>A contrast that may have come from a settings file, brought inside the range.</summary>
    /// <remarks>
    /// Clamped rather than trusted, here rather than at each caller: the value
    /// arrives from a JSON file an artist can edit, and a zero in it would make
    /// the ring vanish with nothing in the interface to explain why.
    /// </remarks>
    public static double ClampContrast(double contrast) =>
        double.IsNaN(contrast) ? DefaultContrast : Math.Clamp(contrast, MinContrast, MaxContrast);

    /// <inheritdoc cref="ClampContrast"/>
    public static double ClampWidth(double width) =>
        double.IsNaN(width) ? DefaultWidth : Math.Clamp(width, MinWidth, MaxWidth);
}
