using Avalonia;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Part of <see cref="CanvasControl"/>: everything the pointer draws for itself.
/// </summary>
/// <remarks>
/// <para>
/// Two gizmos and the rule about which one is shown. The brush's size ring says
/// how wide the next mark will be; the eyedropper's ring
/// (<see cref="PickRing"/>) says what colour the next click will take and what
/// it would replace. They are alternatives rather than layers — the picker has
/// no width to preview, so a size ring under a pick ring would be a gizmo
/// describing a tool that is not in hand.
/// </para>
/// <para>
/// Split out under the monolith ratchet, which is doing exactly what it was
/// built for: <c>CanvasControl.cs</c> is the third-riskiest file in the
/// repository and new work belongs in a partial rather than on the end of it.
/// The line this draws is the honest one — the pointer's own appearance is a
/// concern, not a leftover, and the tip-outline cache that was already there
/// came with it because it exists solely to feed the size ring.
/// </para>
/// </remarks>
public sealed partial class CanvasControl
{
    /// <summary>
    /// The colour the eyedropper would take at the pointer, or null when it
    /// would take none — the top half of <see cref="PickRing"/>.
    /// </summary>
    /// <remarks>
    /// A hex string rather than a colour, because that is what the view model
    /// holds and what the click will assign. Converting once, here, is one place
    /// for a round trip to go wrong instead of two.
    /// </remarks>
    public static readonly StyledProperty<string?> PickSampleHexProperty =
        AvaloniaProperty.Register<CanvasControl, string?>(nameof(PickSampleHex));

    public string? PickSampleHex
    {
        get => GetValue(PickSampleHexProperty);
        set => SetValue(PickSampleHexProperty, value);
    }

    /// <summary>The colour in hand — the bottom half of <see cref="PickRing"/>.</summary>
    public static readonly StyledProperty<string?> PickCurrentHexProperty =
        AvaloniaProperty.Register<CanvasControl, string?>(nameof(PickCurrentHex));

    public string? PickCurrentHex
    {
        get => GetValue(PickCurrentHexProperty);
        set => SetValue(PickCurrentHexProperty, value);
    }

    /// <summary>
    /// The eyedropper's ring as it should be drawn this frame, or null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Gated on the pointer <em>intent</em> rather than on the active tool, so
    /// the eyedropper borrowed by holding Ctrl gets the ring too — and gated on
    /// the intent rather than on the sample resolving, so the size ring does not
    /// come back for the moment the pointer spends off the paper. Whether there
    /// is a colour to show is <see cref="PickRing.For"/>'s decision, which is
    /// where it can be tested.
    /// </para>
    /// <para>
    /// Black is the fallback for a missing colour in hand, not a signal: nothing
    /// binds it to null in practice, and inventing a "no colour" state for a
    /// swatch that always has one would be a case with no way to reach it.
    /// </para>
    /// </remarks>
    private PickRing? PickRingNow() => PickRing.For(
        PointerIntent,
        _hoverPoint is { } p ? ((float)p.X, (float)p.Y) : null,
        ParseHex(PickSampleHex),
        ParseHex(PickCurrentHex) ?? SKColors.Black);

    /// <summary>A bound hex colour as Skia sees it, or null when there is none.</summary>
    /// <remarks>
    /// The forgiving parser on purpose. This runs on a value that arrives from a
    /// binding and is drawn immediately, so a malformed string has to mean "no
    /// ring" — <c>BrushEngine.ParseColor</c> throws, and a throw here would be a
    /// half-typed hex code taking the canvas down.
    /// </remarks>
    private static SKColor? ParseHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        if (Services.ColorSpace.HexToRgb(hex) is not { } rgb) return null;
        // Rounded, not truncated: the parser hands back 0..1, and 128/255 comes
        // back as 127.999… — a swatch one value off the colour the click sets is
        // the exact drift this preview exists not to have.
        static byte Channel(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return new SKColor(Channel(rgb.R), Channel(rgb.G), Channel(rgb.B));
    }

    /// <summary>
    /// Brush cursor in view space (radius already view-scaled).
    /// </summary>
    /// <remarks>
    /// <b>B74.</b> <see cref="BrushCursor.Outline"/> is the tip's silhouette in
    /// unit space, already traced and cached by <c>BrushTipOutline</c>, or null
    /// for a brush with no tip — where <see cref="BrushCursor.Roundness"/> and
    /// <see cref="BrushCursor.AngleDeg"/> describe the ellipse the engine's round
    /// dab actually is.
    /// </remarks>
    private readonly record struct BrushCursor(
        float X, float Y, float Radius,
        float Roundness = 1f,
        float AngleDeg = 0f,
        SKPath? Outline = null,
        CursorBadge Badge = CursorBadge.None);

    /// <summary>
    /// The +/− the pointer wears (<see cref="CursorBadge"/>): pushed from the
    /// view model like <see cref="PointerIntent"/>, worn by the crosshair for
    /// the select family and by the brush ring for the armed weight brush.
    /// </summary>
    public static readonly StyledProperty<CursorBadge> PointerBadgeProperty =
        AvaloniaProperty.Register<CanvasControl, CursorBadge>(nameof(PointerBadge));

    public CursorBadge PointerBadge
    {
        get => GetValue(PointerBadgeProperty);
        set => SetValue(PointerBadgeProperty, value);
    }

    // ---- the departure that is not one (B126) -----------------------------------

    /// <summary>When the pointer last left, or null while it is here.</summary>
    private DateTime? _leftAt;

    private Avalonia.Threading.DispatcherTimer? _leaveTimer;

    /// <summary>
    /// A departure only counts once it has lasted this long.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The pointer leaves and comes back 39 times a second and never
    /// actually goes anywhere.</b> Three traces from the reporter's Huion say
    /// so precisely: every one of 663, then 2,763, then 1,448 exits was
    /// followed immediately by a <em>different device</em> entering, none by
    /// the same one, at a median gap of <b>0.5 ms</b>. It is Windows Ink's
    /// phantom mouse trading the canvas with the pen. Tearing the hover state
    /// down on each exit is what makes the brush ring strobe and hands the
    /// window's arrow back in between.
    /// </para>
    /// <para>
    /// <b>Sized against the measurement rather than picked.</b> The churn's p90
    /// gap is 0.8 ms; the longest genuine departure in the same traces was 16
    /// seconds. Fifty milliseconds sits far clear of the real thing while
    /// swallowing all of the false. Nobody perceives the ring outstaying the
    /// pointer by a twentieth of a second; everybody perceives it strobing.
    /// </para>
    /// <para>
    /// <b>What this does not fix, said plainly.</b> Only the canvas's own hover
    /// state is debounced. The submenu thrash that freezes the application
    /// (B255) happens inside Avalonia's <c>MenuItem</c> code, which no handler
    /// here is on the path of — that is what <c>OverlayPopups</c> is for. The
    /// echo itself is Windows Ink's and cannot be suppressed from in here at
    /// all; <c>PenEchoFilter</c> tried and is the reason that is now known.
    /// </para>
    /// </remarks>
    private const double LeaveGraceMs = 50;

    /// <summary>True once the pointer has been away long enough to mean it.</summary>
    internal bool HoverIsStale =>
        _leftAt is { } left && (DateTime.UtcNow - left).TotalMilliseconds >= LeaveGraceMs;

    /// <summary>Note a departure and arm the settle.</summary>
    /// <remarks>
    /// One timer for the control's life, restarted per exit rather than one
    /// timer per exit — at 39 departures a second the latter would be work
    /// proportional to the churn, on the thread the churn is already starving.
    /// </remarks>
    private void BeginLeave()
    {
        _leftAt = DateTime.UtcNow;
        try
        {
            _leaveTimer ??= new Avalonia.Threading.DispatcherTimer(
                TimeSpan.FromMilliseconds(LeaveGraceMs),
                Avalonia.Threading.DispatcherPriority.Background,
                (_, _) => SettleHover());
            _leaveTimer.Stop();
            _leaveTimer.Start();
        }
        catch
        {
            // No dispatcher (a headless construction): SettleHover is still
            // reached by the next pointer event, which is enough for a test.
        }
    }

    /// <summary>The pointer is back, so the departure never happened.</summary>
    private void CancelLeave()
    {
        _leftAt = null;
        try { _leaveTimer?.Stop(); }
        catch { /* nothing to stop */ }
    }

    /// <summary>
    /// Drop the hover state if the departure has lasted. Returns whether it did.
    /// </summary>
    internal bool SettleHover()
    {
        if (!HoverIsStale) return false;

        CancelLeave();
        _hoverPoint = null;
        // Nothing to re-ask about once the pointer is gone, so a later tool
        // change falls back to the tool's own cursor rather than answering
        // about wherever the pointer happened to leave.
        _cursorAt = null;
        InvalidateVisual();
        return true;
    }

    /// <summary>
    /// The tip's outline as a unit-space path, built once per tip.
    /// </summary>
    /// <remarks>
    /// <b>B74.</b> <c>BrushTipOutline</c> caches the trace; this caches the
    /// <see cref="SKPath"/> built from it, because <c>Render</c> runs on every
    /// pointer move and building a few hundred-segment path per frame is the same
    /// mistake one level up. Keyed by tip id, and only ever holding one — the
    /// cursor shows one brush at a time, so a dictionary would be a cache with no
    /// second entry.
    /// </remarks>
    private string? _outlineTipId;
    private SKPath? _outlinePath;

    private sealed partial class DrawOp
    {
        /// <summary>The brush ring — the pointer itself while paint hides the platform cursor.</summary>
        private static void DrawBrushCursor(SKCanvas canvas, BrushCursor c)
        {
            using var dark = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.2f,
                Color = new SKColor(0, 0, 0, 200),
            };
            using var light = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.2f,
                Color = new SKColor(255, 255, 255, 200),
            };

            if (c.Outline is { } outline)
            {
                // Unit space to view space: the diameter is 2r, and the tip's own
                // aspect is already in the traced contour — so roundness flattens
                // it further rather than defining it, exactly as the engine
                // multiplies roundness onto whatever tip it is stamping.
                canvas.Save();
                canvas.Translate(c.X, c.Y);
                if (c.AngleDeg != 0) canvas.RotateDegrees(c.AngleDeg);
                canvas.Scale(c.Radius * 2, c.Radius * 2 * c.Roundness);
                // The stroke is scaled with the canvas, so undo it in the paint or
                // a big brush gets a fat ring and a small one gets none.
                dark.StrokeWidth = 1.2f / (c.Radius * 2);
                light.StrokeWidth = 1.2f / (c.Radius * 2);
                canvas.DrawPath(outline, dark);
                canvas.Restore();

                canvas.Save();
                canvas.Translate(c.X, c.Y);
                if (c.AngleDeg != 0) canvas.RotateDegrees(c.AngleDeg);
                var inner = Math.Max(0.5f, c.Radius - 1.2f);
                canvas.Scale(inner * 2, inner * 2 * c.Roundness);
                canvas.DrawPath(outline, light);
                canvas.Restore();
                return;
            }

            if (c.Roundness < 0.999f || c.AngleDeg != 0)
            {
                canvas.Save();
                canvas.Translate(c.X, c.Y);
                if (c.AngleDeg != 0) canvas.RotateDegrees(c.AngleDeg);
                canvas.DrawOval(new SKRect(-c.Radius, -c.Radius * c.Roundness, c.Radius, c.Radius * c.Roundness), dark);
                var r = Math.Max(0.5f, c.Radius - 1.2f);
                canvas.DrawOval(new SKRect(-r, -r * c.Roundness, r, r * c.Roundness), light);
                canvas.Restore();
                return;
            }

            canvas.DrawCircle(c.X, c.Y, c.Radius, dark);
            canvas.DrawCircle(c.X, c.Y, Math.Max(0.5f, c.Radius - 1.2f), light);

            // The +/− beside the ring, lower-right in screen pixels — the
            // weight brush's mode visible where the artist is looking.
            if (c.Badge is not CursorBadge.None)
            {
                var at = c.Radius * 0.7071f + 8f;
                CursorBadgePainter.Draw(canvas, c.X + at, c.Y + at, c.Badge);
            }
        }
    }

    private SKPath? TipOutlinePath(string? tipId)
    {
        if (string.IsNullOrEmpty(tipId))
        {
            _outlinePath?.Dispose();
            _outlinePath = null;
            _outlineTipId = null;
            return null;
        }
        if (string.Equals(tipId, _outlineTipId, StringComparison.Ordinal)) return _outlinePath;

        _outlinePath?.Dispose();
        _outlinePath = null;
        _outlineTipId = tipId;

        if (Lightbox.Raster.BrushTipOutline.Of(tipId) is not { Count: > 0 } contours) return null;

        var path = new SKPath();
        foreach (var contour in contours)
        {
            if (contour.Count < 3) continue;
            path.MoveTo((float)contour[0].X, (float)contour[0].Y);
            for (var i = 1; i < contour.Count; i++)
            {
                path.LineTo((float)contour[i].X, (float)contour[i].Y);
            }
            path.Close();
        }
        _outlinePath = path.IsEmpty ? null : path;
        if (_outlinePath is null) path.Dispose();
        return _outlinePath;
    }
}
