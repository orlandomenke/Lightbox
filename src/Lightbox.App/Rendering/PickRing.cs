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
    /// <summary>The clean middle, in screen pixels: nothing is drawn inside it.</summary>
    /// <remarks>
    /// <b>Sized against the cursor rather than against the pixel.</b> The
    /// platform pointer draws a crosshair at the hotspot — that is what marks
    /// the exact pixel — and its arms reach 7px, so a hole any tighter than this
    /// is a hole with a crosshair in it and nothing else. This leaves a clear
    /// margin all the way round the crosshair for the artwork to show through,
    /// which is the whole reason the middle is empty.
    /// </remarks>
    public const float HoleRadius = 14f;

    /// <summary>Outer edge of the two swatches, in screen pixels.</summary>
    /// <remarks>
    /// The band between the two radii is what the eye actually compares, so it
    /// is the number worth being generous with: a hairline ring reads as
    /// decoration, and two colours a hairline apart cannot be told apart at all.
    /// </remarks>
    public const float OuterRadius = 32f;

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
        var band = OuterRadius - HoleRadius;
        var middle = (OuterRadius + HoleRadius) / 2f;

        using var swatch = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = band,
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
            StrokeWidth = 3f,
            Color = new SKColor(255, 255, 255, 200),
        };
        using var dark = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.4f,
            Color = new SKColor(0, 0, 0, 220),
        };

        // The outer edge, against the drawing.
        canvas.DrawCircle(ring.X, ring.Y, OuterRadius, pale);
        canvas.DrawCircle(ring.X, ring.Y, OuterRadius, dark);

        // The hole's rim, against the swatches — both passes strictly outside
        // HoleRadius so the middle stays exactly as the artist painted it.
        canvas.DrawCircle(ring.X, ring.Y, HoleRadius + 0.9f, dark);
        canvas.DrawCircle(ring.X, ring.Y, HoleRadius + 2.6f, pale);
    }

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
