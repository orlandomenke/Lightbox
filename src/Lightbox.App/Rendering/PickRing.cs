using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// The eyedropper's ring: the colour under the pointer against the colour in
/// hand, drawn around the pointer with a hole in the middle.
/// </summary>
/// <remarks>
/// <para>
/// <b>Picking a colour is a comparison, and the application was only showing
/// one side of it.</b> An artist reaching for a shadow off the drawing is not
/// asking "what is this pixel", they are asking "is this different enough from
/// what I already have" — and the answer lived in a docker on the other side of
/// the window, which is exactly where you are not looking while aiming at a
/// drawing. Both colours at the pointer turns a glance across the window into
/// no glance at all.
/// </para>
/// <para>
/// <b>The hole is the reason this is a ring rather than a swatch.</b> A filled
/// disc under the pointer covers the one pixel the whole gesture is about. The
/// middle is left untouched so the artwork shows through it, which also makes
/// the ring self-checking: the colour in the top half should match what you can
/// see through the hole, and when it does not, the pick is about to take
/// something you did not mean.
/// </para>
/// <para>
/// <b>Sampled on top, in hand below</b>, matching every other application that
/// has this ring — an artist who has used one should not have to learn which
/// way round ours is.
/// </para>
/// <para>
/// A pure function of four values with its painting beside it, for the reason
/// <see cref="CanvasCursor"/> is a pure function: the canvas control cannot be
/// driven by synthetic input in this environment, so anything decided inside it
/// ships unguarded. Here the decision is testable with no window and the drawing
/// is testable against a bare <see cref="SKSurface"/>.
/// </para>
/// </remarks>
public readonly record struct PickRing(float X, float Y, SKColor Sampled, SKColor Current)
{
    /// <summary>Outer edge of the two swatches, in screen pixels.</summary>
    /// <remarks>
    /// How much drawing the gizmo is allowed to cover, and the only one of these
    /// three numbers that is about size rather than weight.
    /// </remarks>
    public const float OuterRadius = 32f;

    /// <summary>How thick the two swatches are, in screen pixels.</summary>
    /// <remarks>
    /// <b>The one to turn when the ring feels wrong</b>, because it is the only
    /// one that trades the two things being balanced. Thicker is easier to
    /// compare and covers more of the drawing; thinner reads as chrome and
    /// eventually stops being a colour at all. It is not free to shrink: the
    /// rims below take a fixed bite out of the inside edge, so halving the band
    /// more than halves the colour left in it.
    /// </remarks>
    public const float BandWidth = 12f;

    /// <summary>The clean middle, in screen pixels: nothing is drawn inside it.</summary>
    /// <remarks>
    /// Derived, so thinning the band widens the hole rather than shrinking the
    /// ring — an artist asking for a lighter gizmo is not asking for a smaller
    /// one. It has a floor rather than a target: the platform pointer draws a
    /// crosshair whose arms reach 7px, and a hole that does not clear it is a
    /// hole with a crosshair in it and no artwork, which is
    /// <c>TheHoleClearsThePlatformCrosshair</c>'s job to catch.
    /// </remarks>
    public const float HoleRadius = OuterRadius - BandWidth;

    /// <summary>
    /// Half the gap left between the two swatches, in screen pixels.
    /// </summary>
    /// <remarks>
    /// A gap rather than a drawn divider, and that is the cheap answer to a
    /// problem a line has: the two swatches are arbitrary colours, so any line
    /// between them disappears against one of them sooner or later. A slot of
    /// artwork cannot, because it is not a colour.
    /// </remarks>
    public const float SeamHalfHeight = 1f;

    /// <summary>
    /// The ring to draw, or null when there is nothing to show.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keyed on the <em>intent</em> rather than on the active tool, so the
    /// eyedropper an artist borrows by holding Ctrl gets the ring too. That is
    /// the case it matters in most: the borrow exists because the colour is
    /// already on the canvas and fetching a tool breaks the stroke, and being
    /// able to compare it without breaking the stroke either is the same
    /// argument one step further.
    /// </para>
    /// <para>
    /// A null <paramref name="sampled"/> is the pointer being somewhere the pick
    /// would take nothing — off the paper, mainly. No ring is the honest answer:
    /// a ring showing the last colour it managed to read would be a promise the
    /// click will not keep.
    /// </para>
    /// </remarks>
    public static PickRing? For(
        CanvasCursorKind intent, (float X, float Y)? at, SKColor? sampled, SKColor current) =>
        intent == CanvasCursorKind.Pick && at is { } p && sampled is { } s
            ? new PickRing(p.X, p.Y, s, current)
            : null;

    /// <summary>Paint the ring, in screen space, centred on the pointer.</summary>
    /// <remarks>
    /// <para>
    /// Screen pixels throughout and never scaled by the zoom: this is chrome,
    /// and chrome that grew with the view would be a hairline at 25% and a
    /// dinner plate at 800% — the same rule the guides, the camera frame and the
    /// transform gizmo are drawn under.
    /// </para>
    /// <para>
    /// Every outline is two passes in opposite tones, which is the marching-ants
    /// rule applied to a gizmo that has to survive being held over any drawing
    /// <em>and</em> over two arbitrary swatches. The pale pass on the outer edge
    /// is what keeps the ring visible against black; the dark pass is what keeps
    /// it visible against white. The hole's rim gets the same pair pushed
    /// outward, because a halo centred on the rim would tint the pixel the
    /// artist is looking at, which is the one thing this must not do.
    /// </para>
    /// </remarks>
    public static void Draw(SKCanvas canvas, PickRing ring)
    {
        var middle = (OuterRadius + HoleRadius) / 2f;

        using var swatch = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = BandWidth,
        };

        // The two halves, each a stroked circle clipped to its side of the seam:
        // a stroke of the band's width at the band's middle radius IS the
        // annulus, and clipping it is how the hole stays a hole — a filled path
        // with a hole in it would be the same picture and a great deal more
        // arithmetic to get wrong.
        DrawHalf(canvas, ring, swatch, ring.Sampled, top: true, middle);
        DrawHalf(canvas, ring, swatch, ring.Current, top: false, middle);

        using var pale = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = PaleWidth,
            Color = new SKColor(255, 255, 255, 200),
        };
        using var dark = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = DarkWidth,
            Color = new SKColor(0, 0, 0, 220),
        };

        // The outer edge, against the drawing.
        canvas.DrawCircle(ring.X, ring.Y, OuterRadius, pale);
        canvas.DrawCircle(ring.X, ring.Y, OuterRadius, dark);

        // The hole's rim, against the swatches. Each radius is derived from the
        // stroke that will be drawn on it, so both land strictly outside
        // HoleRadius however wide the strokes get — a halo centred on the rim
        // would tint the pixel the artist is looking at, which is the one thing
        // this must not do. Stacked rather than overlapped, dark first: it is
        // the pale one that has to survive against a dark swatch, so it is the
        // one that gets its full width.
        canvas.DrawCircle(ring.X, ring.Y, HoleRadius + RimGap + DarkWidth / 2f, dark);
        canvas.DrawCircle(ring.X, ring.Y, HoleRadius + RimGap + DarkWidth + PaleWidth / 2f, pale);
    }

    /// <summary>Outline weights, and the clearance the rim keeps off the hole.</summary>
    /// <remarks>
    /// Slimmer than the guides and the transform gizmo use, because these are
    /// drawn <em>on</em> the thing they outline rather than beside it: every
    /// pixel of rim is a pixel of swatch not shown, and at
    /// <see cref="BandWidth"/> the pair already spends about a quarter of the
    /// band. Heavy enough to survive an arbitrary drawing behind it and no
    /// heavier — <c>EachSwatchKeepsARealRunOfColour</c> is what keeps the two
    /// sides of that trade from being tuned independently until neither works.
    /// </remarks>
    private const float PaleWidth = 2.2f;
    private const float DarkWidth = 1.1f;
    private const float RimGap = 0.2f;

    private static void DrawHalf(
        SKCanvas canvas, PickRing ring, SKPaint swatch, SKColor color, bool top, float middle)
    {
        var span = OuterRadius + 4f;
        canvas.Save();
        canvas.ClipRect(top
            ? new SKRect(ring.X - span, ring.Y - span, ring.X + span, ring.Y - SeamHalfHeight)
            : new SKRect(ring.X - span, ring.Y + SeamHalfHeight, ring.X + span, ring.Y + span));
        // Opaque, whatever arrived: a swatch is a statement about a colour, and
        // a translucent one would be a statement about the colour blended with
        // whatever happens to be behind the pointer.
        swatch.Color = color.WithAlpha(255);
        canvas.DrawCircle(ring.X, ring.Y, middle, swatch);
        canvas.Restore();
    }
}
