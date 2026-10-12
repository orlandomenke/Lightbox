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

    /// <summary>What goes under and around the accent so it reads on any artwork.</summary>
    public static readonly SKColor Casing = new(0x26, 0x29, 0x2F, 200);

    /// <summary>The inside of an unselected grip.</summary>
    public static readonly SKColor Open = SKColors.White;

    public const float LineWidth = 1.25f;

    /// <summary>The line and a little over a pixel of casing on each side.</summary>
    public const float CasingWidth = 3.75f;

    /// <summary>Half the width of a grip: nine pixels across.</summary>
    public const float GripHalf = 4.5f;

    public const float GripOutline = 1.5f;

    /// <summary>A steering dot — a curve handle. A third of a grip's area.</summary>
    public const float DotRadius = 2.5f;

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

        /// <summary>A line: the casing, then the accent over it.</summary>
        public void Line(SKCanvas canvas, SKPath path)
        {
            canvas.DrawPath(path, _casing);
            canvas.DrawPath(path, _line);
        }

        /// <summary>The thin line from a grip to the dot that steers it.</summary>
        public void Stem(SKCanvas canvas, float x0, float y0, float x1, float y1)
        {
            canvas.DrawLine(x0, y0, x1, y1, _stemCasing);
            canvas.DrawLine(x0, y0, x1, y1, _stem);
        }

        /// <summary>Something you take hold of: square, or round for a smooth point.</summary>
        public void Grip(SKCanvas canvas, float x, float y, bool round, bool selected)
        {
            var half = GripHalf / _scale;
            // The rim sits just outside the outline, so the grip keeps an edge
            // on a page of the accent's own colour.
            var rimHalf = half + (GripOutline / 2f + 0.5f) / _scale;
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
        public void Dot(SKCanvas canvas, float x, float y)
        {
            var radius = DotRadius / _scale;
            canvas.DrawCircle(x, y, radius, _fill);
            canvas.DrawCircle(x, y, radius + 0.5f / _scale, _rim);
        }

        /// <summary>A ring around a grip: "the next click lands here".</summary>
        public void Ring(SKCanvas canvas, float x, float y, float radius)
        {
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
