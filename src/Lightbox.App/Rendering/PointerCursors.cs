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
