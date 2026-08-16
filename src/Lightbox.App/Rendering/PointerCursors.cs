using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Lightbox.App.Rendering;

/// <summary>
/// The platform cursor for each pointer intent — the last step of the cursor
/// pipeline, and the one that used to live as a private switch inside
/// <see cref="CanvasControl"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>B175.</b> <see cref="CanvasCursor.Armed"/> already told the picker, the
/// fill and the pen apart — three distinct kinds — and the control's private
/// mapping collapsed all three onto one <c>Cross</c>, which nothing could catch
/// because a private static in a control is out of every test's reach. The
/// mapping lives here now, public, so <c>CanvasCursorTests</c> can hold the
/// property the enum exists for: a kind that is distinct upstream stays
/// distinct on screen.
/// </para>
/// <para>
/// <b>The picker and the fill wear their own icons</b>, drawn from the same
/// registered geometry the tool rail shows (<see cref="IconSet"/>) — the
/// roadmap item "Lightbox draws its own icons" reaching the one surface it had
/// not. The precision point stays a crosshair at the hotspot; the glyph sits
/// beside it as a badge, which is how a cursor says <em>what</em> without
/// costing <em>where</em>.
/// </para>
/// <para>
/// Cursors are cached: a pointer move asks for one per event, and building a
/// bitmap cursor per event would be work in a per-event path for a value that
/// never changes.
/// </para>
/// </remarks>
public static class PointerCursors
{
    /// <summary>The cursor for an intent.</summary>
    /// <remarks>
    /// <b>Paint maps to <c>None</c> on purpose:</b> the brush cursor is drawn by
    /// the render op at the brush's real size and shape, and showing an arrow as
    /// well would put two pointers on the canvas.
    /// </remarks>
    public static Cursor For(CanvasCursorKind intent) => intent switch
    {
        CanvasCursorKind.Paint => Hidden,
        CanvasCursorKind.Forbidden => Forbidden,
        CanvasCursorKind.Pick => _pick ??= Badged(IconSet.Picker),
        CanvasCursorKind.Fill => _fill ??= Badged(IconSet.Fill),
        CanvasCursorKind.Precise => Precise,
        CanvasCursorKind.Move => Move,
        CanvasCursorKind.PickRecords => Records,
        CanvasCursorKind.Rotate => Rotate,
        _ => Default,
    };

    /// <summary>
    /// This drag turns something. <c>StandardCursorType</c> has no rotate, and
    /// the four-way move is the one thing it must not be mistaken for — the
    /// whole point is telling those two apart — so the nearest honest stock
    /// cursor is the one that says "this handle turns".
    /// </summary>
    public static readonly Cursor Rotate = new(StandardCursorType.Hand);

    /// <summary>Nothing drawn — the brush ring is the pointer.</summary>
    public static readonly Cursor Hidden = new(StandardCursorType.None);

    public static readonly Cursor Forbidden = new(StandardCursorType.No);

    /// <summary>Placing points: the pen, the shapes, the selections.</summary>
    public static readonly Cursor Precise = new(StandardCursorType.Cross);

    public static readonly Cursor Move = new(StandardCursorType.SizeAll);

    /// <summary>The two arrows: picking records is pointing at things.</summary>
    public static readonly Cursor Records = new(StandardCursorType.Arrow);

    public static readonly Cursor Default = new(StandardCursorType.Arrow);

    private static Cursor? _pick;
    private static Cursor? _fill;

    // ---- the angled cursors (B228) ---------------------------------------------------

    /// <summary>
    /// A double-headed arrow lying along <paramref name="degrees"/>, for a
    /// handle that resizes along that direction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Drawn at the real angle rather than snapped to one of the platform's
    /// eight (Q100, the owner's call against the recommendation).</b> The stock
    /// set — <c>SizeWestEast</c>, <c>SizeNorthSouth</c> and the four corner
    /// cursors — covers 45° steps, which is exact for an unrotated box and wrong
    /// by up to 22.5° for any other. Drawing it means a box turned 30° gets a
    /// cursor at 30°.
    /// </para>
    /// <para>
    /// <b>What that costs, recorded because it was the argument against.</b>
    /// These are our bitmaps on every platform, so they do not inherit the
    /// system cursor theme, they do not follow a user's cursor-size accessibility
    /// setting, and the hotspot is ours to keep correct. The mitigations are the
    /// ones <see cref="Badged"/> already established: the same halo-under-dark
    /// double pass so the arrow survives any drawing beneath it, the same
    /// hotspot as every other canvas cursor, and the same catch-and-fall-back,
    /// because a pointer is the one thing that must never take the window down.
    /// </para>
    /// <para>
    /// <b>Cached per whole degree, and symmetric.</b> A double arrow at 200° is
    /// the arrow at 20°, so the cache is keyed mod 180 — at most 180 entries of
    /// 32×32, and a rotation drag re-uses one the moment the angle repeats.
    /// Rendering a bitmap per pointer move is exactly the per-event cost
    /// invariant 6 rules out.
    /// </para>
    /// </remarks>
    public static Cursor ResizeAt(double degrees)
    {
        var key = ((int)Math.Round(degrees) % 180 + 180) % 180;
        if (_resize.TryGetValue(key, out var cached)) return cached;
        var made = DrawDoubleArrow(key) ?? Move;
        _resize[key] = made;
        return made;
    }

    private static readonly Dictionary<int, Cursor> _resize = [];

    /// <summary>
    /// The turn cursor: an arc with a head, for the gizmo's outside-the-box
    /// rotate and the bone tool's pose drag.
    /// </summary>
    /// <remarks>
    /// <b>Drawn because there is no stock one.</b> <c>StandardCursorType</c> has
    /// no rotate at all, and <c>Hand</c> stood in for it — which reads as "grab
    /// and pan" on every platform where a hand means exactly that. The one thing
    /// this must not be mistaken for is the four-way move, and an arc is not
    /// mistakable for one.
    /// </remarks>
    public static Cursor Turn => _turn ??= DrawTurn() ?? Rotate;

    private static Cursor? _turn;

    /// <summary>Dragging the canvas itself: the hand that is holding it.</summary>
    /// <remarks>
    /// Stock, unlike the two above, because every platform has a hand and this
    /// is the one gesture where "grab and pan" is precisely the meaning.
    /// </remarks>
    public static readonly Cursor Grab = new(StandardCursorType.Hand);

    /// <summary>
    /// Two arrowheads on a shaft through the hotspot, at <paramref name="degrees"/>.
    /// </summary>
    /// <remarks>
    /// Null rather than a throw when there is no render surface — headless runs
    /// have no application to render into, and the caller falls back to a stock
    /// cursor rather than losing the pointer.
    /// </remarks>
    private static Cursor? DrawDoubleArrow(double degrees)
    {
        try
        {
            var bitmap = new RenderTargetBitmap(
                new PixelSize(CursorSize, CursorSize), new Vector(96, 96));
            using (var ctx = bitmap.CreateDrawingContext())
            {
                var radians = degrees * Math.PI / 180;
                var dx = Math.Cos(radians);
                var dy = Math.Sin(radians);
                var centre = new Point(HotspotX, HotspotY);

                // Sized to the hotspot's quadrant rather than the bitmap, so the
                // arrow stays centred on the point that acts.
                const double reach = 7.0;
                const double head = 3.2;
                var a = new Point(centre.X - dx * reach, centre.Y - dy * reach);
                var b = new Point(centre.X + dx * reach, centre.Y + dy * reach);

                // Halo first and wider, then the dark line over it — the
                // marching-ants reason: a cursor that vanishes against half the
                // drawings is a cursor that cannot be trusted.
                foreach (var pen in new[]
                {
                    new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), 3.5),
                    new Pen(Brushes.Black, 1.4),
                })
                {
                    ctx.DrawLine(pen, a, b);
                    Arrowhead(ctx, pen, a, -dx, -dy, head);
                    Arrowhead(ctx, pen, b, dx, dy, head);
                }
            }
            return new Cursor(bitmap, new PixelPoint(HotspotX, HotspotY));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Two short strokes back from a tip, making a chevron.</summary>
    private static void Arrowhead(
        DrawingContext ctx, Pen pen, Point tip, double dx, double dy, double size)
    {
        // The shaft direction turned ±140°, which is the angle that reads as an
        // arrow rather than as a T or a cross at cursor size.
        foreach (var turn in new[] { 2.44, -2.44 })
        {
            var cos = Math.Cos(turn);
            var sin = Math.Sin(turn);
            ctx.DrawLine(pen, tip, new Point(
                tip.X + (dx * cos - dy * sin) * size,
                tip.Y + (dx * sin + dy * cos) * size));
        }
    }

    /// <summary>An arc with a head on it: this drag turns something.</summary>
    private static Cursor? DrawTurn()
    {
        try
        {
            var bitmap = new RenderTargetBitmap(
                new PixelSize(CursorSize, CursorSize), new Vector(96, 96));
            using (var ctx = bitmap.CreateDrawingContext())
            {
                var centre = new Point(HotspotX, HotspotY);
                const double r = 6.0;

                var arc = new StreamGeometry();
                using (var g = arc.Open())
                {
                    // Three-quarters of a turn, so the gap says which way round
                    // it goes; a full ring would read as a target.
                    var from = new Point(centre.X + r, centre.Y);
                    g.BeginFigure(from, false);
                    g.ArcTo(
                        new Point(centre.X, centre.Y - r),
                        new Size(r, r), 0, false, SweepDirection.CounterClockwise);
                    g.ArcTo(
                        new Point(centre.X, centre.Y + r),
                        new Size(r, r), 0, false, SweepDirection.CounterClockwise);
                    g.EndFigure(false);
                }

                foreach (var pen in new[]
                {
                    new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), 3.5),
                    new Pen(Brushes.Black, 1.4),
                })
                {
                    ctx.DrawGeometry(null, pen, arc);
                    // The head sits at the arc's end, pointing the way it sweeps.
                    Arrowhead(ctx, pen, new Point(centre.X, centre.Y + r), -1, 0, 3.2);
                }
            }
            return new Cursor(bitmap, new PixelPoint(HotspotX, HotspotY));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Where the badged cursors act, in cursor pixels.</summary>
    /// <remarks>
    /// Public so the tests can assert the hotspot is the crosshair and not the
    /// glyph — a cursor whose visible tip and acting point disagree is worse
    /// than a plain cross.
    /// </remarks>
    public const int HotspotX = 8;
    public const int HotspotY = 8;

    private const int CursorSize = 32;

    /// <summary>
    /// A crosshair at the hotspot with the tool's registered icon beside it.
    /// </summary>
    /// <remarks>
    /// Every mark is drawn twice — a pale wide pass under the dark one — for the
    /// marching-ants reason: a cursor that vanishes against half the drawings is
    /// a cursor that cannot be trusted. Falls back to the plain crosshair when
    /// the geometry cannot be resolved or rendered (no application, headless
    /// platform without a render surface), because a pointer is the one thing
    /// that must never take the window down.
    /// </remarks>
    private static Cursor Badged(string iconName)
    {
        if (IconSet.Resolve(iconName) is not { } glyph) return Precise;

        try
        {
            var bitmap = new RenderTargetBitmap(
                new PixelSize(CursorSize, CursorSize), new Vector(96, 96));
            using (var ctx = bitmap.CreateDrawingContext())
            {
                var halo = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 3);
                var line = new Pen(Brushes.Black, 1.25);

                // The precision half: a crosshair centred on the hotspot.
                foreach (var pen in new[] { halo, line })
                {
                    ctx.DrawLine(pen, new Point(HotspotX, 1), new Point(HotspotX, 15));
                    ctx.DrawLine(pen, new Point(1, HotspotY), new Point(15, HotspotY));
                }

                // The identity half: the tool's own glyph, badge-sized, clear of
                // the crosshair so neither obscures the other.
                var bounds = glyph.Bounds;
                if (bounds.Width > 0 && bounds.Height > 0)
                {
                    const double badge = 15.0;
                    var scale = badge / Math.Max(bounds.Width, bounds.Height);
                    var transform =
                        Matrix.CreateTranslation(-bounds.X, -bounds.Y)
                        * Matrix.CreateScale(scale, scale)
                        * Matrix.CreateTranslation(CursorSize - badge - 1, CursorSize - badge - 1);
                    using (ctx.PushTransform(transform))
                    {
                        // Pen widths are in glyph space once the transform is
                        // pushed, so they are pre-divided to land at the same
                        // screen weight as the crosshair.
                        ctx.DrawGeometry(
                            null, new Pen(halo.Brush, halo.Thickness / scale), glyph);
                        ctx.DrawGeometry(
                            null, new Pen(line.Brush, line.Thickness / scale), glyph);
                    }
                }
            }
            return new Cursor(bitmap, new PixelPoint(HotspotX, HotspotY));
        }
        catch
        {
            return Precise;
        }
    }
}
