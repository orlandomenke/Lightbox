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
    /// <summary>How wide the ring's single line is, in screen pixels.</summary>
    /// <remarks>
    /// Thinner than the 1.2 px each of the two lines it replaces, because one
    /// line at full weight reads heavier than either half of a pair did.
    /// </remarks>
    public const float StrokeWidth = 1.1f;

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

    /// <summary>The dark ink: black, held well below full so it reads as chrome.</summary>
    public static readonly SKColor Dark = new(0, 0, 0, 165);

    /// <summary>The light ink, a little stronger than the dark one.</summary>
    /// <remarks>
    /// White at the same alpha over dark artwork reads fainter than black does
    /// over light artwork, so the two are matched by eye rather than by number.
    /// </remarks>
    public static readonly SKColor Light = new(255, 255, 255, 180);

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

    /// <summary>The colour to stroke with.</summary>
    public static SKColor ColorFor(CursorInk ink) => ink == CursorInk.Light ? Light : Dark;
}
