using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// What chrome drawn over the artwork looks like: one colour, one line, one
/// set of sizes, and the pens that draw them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists (Q249).</b> An inventory on 2026-10-12 found thirty-two
/// overlays on the canvas drawing themselves from about ten blues, twelve
/// ambers, eight handle sizes and grab distances from five pixels to ten, with
/// no shared constant among them — each painter had picked what looked right
/// beside nothing. The owner's ruling was one style. This is where it is
/// written down, and a painter that takes its colour from here cannot drift.
/// </para>
/// <para>
/// <b>The rules, as decided:</b>
/// </para>
/// <list type="bullet">
/// <item>
/// <b>One accent means "this is chrome you can edit"</b> — the palette's
/// <c>AccentCyan</c>, which the design already calls the chosen-state accent.
/// Colours that carry a meaning of their own keep it: onion red and blue, the
/// heat map, the camera's orange, the brush ring's adaptive ink.
/// </item>
/// <item>
/// <b>Every line is cased.</b> The accent over a wider dark pass, so it reads
/// on white, on black and on artwork that is exactly the accent's colour. An
/// overlay that vanishes against half the drawings is one you cannot trust.
/// </item>
/// <item>
/// <b>Squares grab, dots steer.</b> Anything you move or resize is a square
/// grip — except a smooth point, which stays round so a curve can be told from
/// a corner without clicking. What only steers a curve is a small solid dot on
/// a stem, clearly smaller than a grip, so a handle is never mistaken for a
/// point.
/// </item>
/// <item>
/// <b>Selected is filled, unselected is open</b> — white inside an accent
/// outline.
/// </item>
/// <item>
/// <b>Every size is screen pixels.</b> The painters run inside the document
/// transform, so each size here is divided by the view scale once, in
/// <see cref="Pens"/>.
/// </item>
/// </list>
/// <para>
/// <b>A literal rather than a resource lookup</b>, because these are drawn on
/// the render thread where the theme dictionary cannot be read.
/// <c>TheOverlayAccentIsThePalettesCyan</c> holds the two together.
/// </para>
/// </remarks>
public static class OverlayStyle
{
    /// <summary>The colour of editable chrome. The palette's <c>AccentCyan</c>.</summary>
    public static readonly SKColor Accent = new(0x00, 0xD1, 0xBC);

    /// <summary>
    /// The style's one second colour: a mark that is a reference point rather
    /// than the thing being edited — a pivot, a warp band, a guide's anchor.
    /// The palette's <c>AccentAmber</c> (Q252), in place of the dozen ambers
    /// those marks had each picked.
    /// </summary>
    public static readonly SKColor Reference = new(0xFF, 0xB0, 0x20);

    /// <summary>The camera's colour. The palette's <c>CameraTrack</c>.</summary>
    public static readonly SKColor CameraOrange = new(0xFF, 0x9F, 0x45);

    /// <summary>
    /// A guide being drawn against, at rest: one faint pass, no casing. Thirty
    /// of them must not read as thirty selections (Q252); a guide takes the
    /// full style only once the Move tool has hold of it.
    /// </summary>
    public static readonly SKColor QuietLine = new(0x50, 0x96, 0xF0, 110);

    /// <summary>
    /// A guide's own marks at rest — its vanishing point, its post — in the
    /// reference colour, turned down to sit with <see cref="QuietLine"/>.
    /// </summary>
    public static readonly SKColor QuietMark = new(0xFF, 0xB0, 0x20, 150);

    /// <summary>
    /// The outline of a drawn line the arrow has picked or is over: one pixel,
    /// uncased, so the ink shows either side of it. The owner's ruling on the
    /// cased line over a picked stroke — <em>"make the outline thinner so the
    /// ink shows"</em> — with its cost named: it can be lost on artwork of the
    /// accent's own colour.
    /// </summary>
    public const float OutlineWidth = 1f;

    /// <summary>What a mark is, which decides its colour.</summary>
    public enum Role
    {
        /// <summary>Chrome you can edit: the accent.</summary>
        Edit,

        /// <summary>A reference point, not the thing itself: amber.</summary>
        Reference,

        /// <summary>
        /// The camera's frame and its handles. The camera keeps the orange it
        /// has on the timeline (Q249: a colour that carries a meaning keeps
        /// it) and takes everything else — the casing, the grips — from here.
        /// </summary>
        Camera,
    }

    /// <summary>
    /// How much a hovered line thickens. Hover grows and thickens in the same
    /// colours (Q252): bigger says "a press takes this one", and it cannot be
    /// read as selected, which is filled.
    /// </summary>
    public const float HoverLineGain = 1f;

    /// <summary>How strongly a dragged-out area is washed with the accent.</summary>
    public const byte AreaAlpha = 24;

    /// <summary>How much a hovered grip grows on each side: two pixels across.</summary>
    public const float HoverGripGain = 1f;

    /// <summary>What goes under and around the accent so it reads on any artwork.</summary>
    /// <remarks>
    /// Not a palette role, deliberately: it is drawn over the artist's picture
    /// rather than over the application's surfaces, so it does not follow the
    /// theme. The dark the symmetry axis and the camera's handles already used.
    /// </remarks>
    public static readonly SKColor Casing = new(0x26, 0x29, 0x2F, 200);

    /// <summary>
    /// The inside of an unselected grip. White, not a text or surface token:
    /// it has to read as empty on any artwork, in either theme.
    /// </summary>
    public static readonly SKColor Open = SKColors.White;

    public const float LineWidth = 1.5f;

    /// <summary>
    /// The line and a pixel of casing on each side. Any wider and the casing
    /// is most of what is seen: at 1.25 inside 3.75 the line read as grey-teal
    /// rather than as the accent (the ui-critic, on the first render).
    /// </summary>
    public const float CasingWidth = 3.5f;

    /// <summary>
    /// Half the width of a grip's body: nine pixels across, and about twelve
    /// drawn once its outline and rim are on.
    /// </summary>
    public const float GripHalf = 4.5f;

    public const float GripOutline = 1.5f;

    /// <summary>
    /// A steering dot — a curve handle. About a quarter of a drawn grip's
    /// area, which is what keeps it from being read as a point.
    /// </summary>
    public const float DotRadius = 2.5f;

    /// <summary>
    /// How far out the "next click lands here" ring sits, in grip half-widths:
    /// clear of the grip's own rim with a gap the eye can see between them.
    /// </summary>
    public const float RingReach = 2.2f;

    public const float StemWidth = 1f;

    /// <summary>How near a press has to land to have meant a grip.</summary>
    public const double GrabPixels = 10.0;

    /// <summary>
    /// The pens for one draw at one view scale. Made per frame and disposed
    /// with it, like every other paint a draw operation uses.
    /// </summary>
    public sealed class Pens : IDisposable
    {
        private readonly float _scale;
        private readonly SKPaint _line, _casing, _stem, _stemCasing, _fill, _open, _outline, _rim;

        public Pens(float scale)
        {
            _scale = Math.Max(0.01f, scale);
            _line = Stroke(Accent, LineWidth);
            _casing = Stroke(Casing, CasingWidth);
            _stem = Stroke(Accent, StemWidth);
            _stemCasing = Stroke(Casing, StemWidth + 2f);
            _outline = Stroke(Accent, GripOutline);
            _rim = Stroke(Casing, 1f);
            _fill = new SKPaint { Color = Accent, Style = SKPaintStyle.Fill, IsAntialias = true };
            _open = new SKPaint { Color = Open, Style = SKPaintStyle.Fill, IsAntialias = true };
        }

        private SKPaint Stroke(SKColor color, float width) => new()
        {
            Color = color,
            StrokeWidth = width / _scale,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
        };

        private static SKColor ColourOf(Role role) => role switch
        {
            Role.Reference => Reference,
            Role.Camera => CameraOrange,
            _ => Accent,
        };

        /// <summary>A line: the casing, then the role's colour over it.</summary>
        /// <param name="dashed">
        /// A line that is a copy of another rather than the thing itself — the
        /// spokes of a rotational symmetry beside its mirror line. The casing
        /// stays whole, so the dashes are legible on anything.
        /// </param>
        public void Line(
            SKCanvas canvas, SKPath path, bool hover = false, Role role = Role.Edit, bool dashed = false)
        {
            var gain = hover ? HoverLineGain / _scale : 0f;
            _casing.StrokeWidth = CasingWidth / _scale + gain;
            _line.StrokeWidth = LineWidth / _scale + gain;
            _line.Color = ColourOf(role);
            canvas.DrawPath(path, _casing);
            if (dashed)
            {
                using var dash = SKPathEffect.CreateDash([6f / _scale, 5f / _scale], 0);
                _line.PathEffect = dash;
                canvas.DrawPath(path, _line);
                _line.PathEffect = null;
                return;
            }
            canvas.DrawPath(path, _line);
        }

        /// <summary>
        /// The outline of a drawn line: thin and uncased, so the line under it
        /// is still the thing you see. See <see cref="OutlineWidth"/>.
        /// </summary>
        public void Outline(SKCanvas canvas, SKPath path, bool hover = false, Role role = Role.Edit)
        {
            _line.StrokeWidth = (OutlineWidth + (hover ? HoverLineGain : 0f)) / _scale;
            _line.Color = ColourOf(role);
            canvas.DrawPath(path, _line);
        }

        /// <summary>
        /// A line to draw against rather than to edit: one faint pass. See
        /// <see cref="QuietLine"/>.
        /// </summary>
        public void Quiet(SKCanvas canvas, SKPath path)
        {
            _line.StrokeWidth = 1f / _scale;
            _line.Color = QuietLine;
            canvas.DrawPath(path, _line);
        }

        /// <summary>
        /// An area being dragged out — a marquee: a faint wash of the accent
        /// inside the styled line, so the box reads as a region and the artwork
        /// under it stays legible.
        /// </summary>
        public void Area(SKCanvas canvas, SKRect rect)
        {
            _fill.Color = Accent.WithAlpha(AreaAlpha);
            canvas.DrawRect(rect, _fill);
            using var path = new SKPath();
            path.AddRect(rect);
            Line(canvas, path);
        }

        /// <summary>The thin line from a grip to the dot that steers it.</summary>
        public void Stem(SKCanvas canvas, float x0, float y0, float x1, float y1)
        {
            canvas.DrawLine(x0, y0, x1, y1, _stemCasing);
            canvas.DrawLine(x0, y0, x1, y1, _stem);
        }

        /// <summary>Something you take hold of: square, or round for a smooth point.</summary>
        public void Grip(
            SKCanvas canvas, float x, float y, bool round, bool selected,
            bool hover = false, Role role = Role.Edit)
        {
            var half = (GripHalf + (hover ? HoverGripGain : 0f)) / _scale;
            // The rim sits just outside the outline, so the grip keeps an edge
            // on a page of the accent's own colour.
            var rimHalf = half + (GripOutline / 2f + 0.5f) / _scale;
            _fill.Color = _outline.Color = ColourOf(role);
            var body = selected ? _fill : _open;
            if (round)
            {
                canvas.DrawCircle(x, y, rimHalf, _rim);
                canvas.DrawCircle(x, y, half, body);
                canvas.DrawCircle(x, y, half, _outline);
            }
            else
            {
                canvas.DrawRect(new SKRect(x - rimHalf, y - rimHalf, x + rimHalf, y + rimHalf), _rim);
                var box = new SKRect(x - half, y - half, x + half, y + half);
                canvas.DrawRect(box, body);
                canvas.DrawRect(box, _outline);
            }
        }

        /// <summary>Something that steers and is not itself a place: a curve handle.</summary>
        public void Dot(SKCanvas canvas, float x, float y, Role role = Role.Edit)
        {
            var radius = DotRadius / _scale;
            _fill.Color = ColourOf(role);
            canvas.DrawCircle(x, y, radius, _fill);
            canvas.DrawCircle(x, y, radius + 0.5f / _scale, _rim);
        }

        /// <summary>A ring around a place: "the next click lands here", or a pivot.</summary>
        public void Ring(SKCanvas canvas, float x, float y, float radius, Role role = Role.Edit)
        {
            _casing.StrokeWidth = CasingWidth / _scale;
            _outline.Color = ColourOf(role);
            canvas.DrawCircle(x, y, radius / _scale, _casing);
            canvas.DrawCircle(x, y, radius / _scale, _outline);
        }

        public void Dispose()
        {
            _line.Dispose();
            _casing.Dispose();
            _stem.Dispose();
            _stemCasing.Dispose();
            _fill.Dispose();
            _open.Dispose();
            _outline.Dispose();
            _rim.Dispose();
        }
    }
}
