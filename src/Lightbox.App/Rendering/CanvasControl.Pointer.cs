using Avalonia;
using Avalonia.Input;
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
        CursorBadge Badge = CursorBadge.None,
        CursorInk Ink = CursorInk.Dark,
        double Contrast = CursorContrast.DefaultContrast,
        double Width = CursorContrast.DefaultWidth);

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

    /// <summary>
    /// Which ink the brush ring is drawn in — pushed from the view model, which
    /// is the only side that can see the artwork under the pointer.
    /// </summary>
    /// <remarks>
    /// The decision is <see cref="CursorContrast.Choose"/> and the sampling is
    /// the view model's; all that arrives here is the answer, so the control
    /// still reads no pixels of its own.
    /// </remarks>
    public static readonly StyledProperty<CursorInk> BrushCursorInkProperty =
        AvaloniaProperty.Register<CanvasControl, CursorInk>(nameof(BrushCursorInk));

    public CursorInk BrushCursorInk
    {
        get => GetValue(BrushCursorInkProperty);
        set => SetValue(BrushCursorInkProperty, value);
    }

    /// <summary>How far the ring's line stands off the artwork, 0 to 1.</summary>
    /// <remarks>
    /// A preference rather than a document value, and one of the two an artist
    /// can turn from <b>Configure ▸ Drawing</b>. It never reaches a pixel of the
    /// drawing, so unlike a brush setting it may be read at paint time
    /// (invariant 4 is about settings that change the mark).
    /// </remarks>
    public static readonly StyledProperty<double> BrushCursorContrastProperty =
        AvaloniaProperty.Register<CanvasControl, double>(
            nameof(BrushCursorContrast), CursorContrast.DefaultContrast);

    public double BrushCursorContrast
    {
        get => GetValue(BrushCursorContrastProperty);
        set => SetValue(BrushCursorContrastProperty, value);
    }

    /// <summary>The width of the ring's line, in screen pixels.</summary>
    /// <inheritdoc cref="BrushCursorContrastProperty" path="/remarks"/>
    public static readonly StyledProperty<double> BrushCursorWidthProperty =
        AvaloniaProperty.Register<CanvasControl, double>(
            nameof(BrushCursorWidth), CursorContrast.DefaultWidth);

    public double BrushCursorWidth
    {
        get => GetValue(BrushCursorWidthProperty);
        set => SetValue(BrushCursorWidthProperty, value);
    }

    // ---- the pen's other axes (tilt and speed) ----------------------------------

    /// <summary>The tilt reader and speed estimator for the stroke in flight.</summary>
    private readonly PenAxes _penAxes = new();

    /// <summary>Where the last sample was, for the speed estimate.</summary>
    private (double X, double Y, ulong T)? _lastAxisSample;

    /// <summary>
    /// The pen axes for a sample — always measured, never filtered here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Reading them is free; storing them is not.</b> Two properties off a
    /// pointer point and a running average cost nothing at 200 Hz, while
    /// writing them into every point of every document costs 113 bytes a point
    /// and 1.70× the saved file. So this reports what the pen said and
    /// <c>StrokeBuilder</c> — which holds the stroke's own brush — decides what
    /// is kept. Measuring unconditionally also means the diagnostics readout
    /// can answer "does my tablet report tilt at all" whatever brush is in
    /// hand, which is the question an artist actually arrives with.
    /// </para>
    /// <para>
    /// The timestamp is the <em>event's</em>, not the point's: coalesced
    /// intermediate points share one, which is why <see cref="PenAxes.SpeedFor"/>
    /// holds its estimate rather than dividing by zero when two samples arrive
    /// together. Verified against the shipped Avalonia assembly rather than
    /// assumed — per-point timestamps do not exist.
    /// </para>
    /// </remarks>
    private (double? TiltX, double? TiltY, double? Speed) AxesOf(PointerPoint pp, ulong timestamp)
    {
        var isPen = pp.Pointer.Type == PointerType.Pen;
        var (tx, ty) = _penAxes.TiltFor(isPen, pp.Properties.XTilt, pp.Properties.YTilt);

        double? speed = null;
        var at = pp.Position;
        if (_lastAxisSample is { } prev)
        {
            speed = _penAxes.SpeedFor(at.X - prev.X, at.Y - prev.Y, timestamp - (double)prev.T);
        }
        else
        {
            // The first sample of a stroke starts from rest rather than from a
            // guess: inventing a speed here would put a mark on the paper the
            // hand did not make.
            speed = 0;
        }
        _lastAxisSample = (at.X, at.Y, timestamp);
        return (tx, ty, speed);
    }

    private void ReportInputDiagnostic(PointerType type, float rawPressure) =>
        ReportInputDiagnostic(type, rawPressure, null, null, null);

    /// <summary>
    /// The live readout beside the pen settings, with the axes when they are
    /// being recorded.
    /// </summary>
    /// <remarks>
    /// <b>Tilt is the one worth showing.</b> Whether a tablet reports it at all
    /// is a fact about the driver that nothing else in the application can
    /// answer, and "my pen has tilt" is exactly the belief an artist arrives
    /// with and can be wrong about. Speed rides along because it is free once
    /// the line exists, and because a speed pinned at 1.00 says the reference
    /// needs tuning for that hand.
    /// </remarks>
    private void ReportInputDiagnostic(
        PointerType type, float rawPressure, double? tiltX, double? tiltY, double? speed)
    {
        if (InputDiagnostic is null) return;
        var axes = tiltX is { } tx && tiltY is { } ty
            ? $" · tilt {tx:0}/{ty:0}"
            : type == PointerType.Pen ? " · no tilt reported" : "";
        if (speed is { } sp) axes += $" · speed {sp:0.00}";
        var text = (type == PointerType.Pen
            ? $"Pen detected — pressure {rawPressure:0.00}{axes}"
            : $"{type} input — no pressure axis (paints at 100%)") + TracingSuffix();
        if (text == _lastDiagnostic) return;
        _lastDiagnostic = text;
        InputDiagnostic.Invoke(text);
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

    /// <summary>
    /// Whether the brush ring has a position to be drawn at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The strobe of B126 reduced to something countable: the ring going away
    /// and coming back is what the artist sees flickering. Watched by the replay
    /// harness across a whole capture rather than asserted at one moment,
    /// because the complaint was never about a single teardown — and watched
    /// through the hover point itself rather than through <see cref="SettleHover"/>
    /// so it stays true however the ring is torn down. A future change that
    /// dropped the hover point in <c>OnPointerExited</c> again would bypass the
    /// settle entirely, and a counter kept there would report nothing wrong.
    /// </para>
    /// <para>
    /// Here rather than beside the hover point it reads, because
    /// <c>CanvasControl.cs</c> is at its size ratchet and needing the room is
    /// explicitly not a reason to raise one.
    /// </para>
    /// </remarks>
    internal bool HasHoverRing => _hoverPoint is not null;

    /// <summary>Where "how long ago did the pointer leave" is read from.</summary>
    /// <remarks>
    /// <para>
    /// <b>A seam, and it exists because replaying a capture faithfully is
    /// otherwise impossible.</b> The churn this grace was built against has a
    /// median gap of <b>0.5 ms</b>. Raising one pointer event into a real canvas
    /// costs more than that, so a replay of a recorded trace can never keep up
    /// with the wall clock — and against <see cref="DateTime.UtcNow"/> every
    /// half-millisecond departure in the capture would age past the 50 ms grace
    /// while the harness was still delivering it. The replay would then report
    /// teardowns the reporter's machine never had, which is worse than no
    /// replay: a fixture that fails for a reason belonging to the harness.
    /// </para>
    /// <para>
    /// Per instance rather than static, so nothing about a test's clock can
    /// reach a canvas it did not construct. In the application it is never
    /// assigned, and the default is the clock it always read.
    /// </para>
    /// </remarks>
    internal Func<DateTime> HoverClock { get; set; } = () => DateTime.UtcNow;

    /// <summary>True once the pointer has been away long enough to mean it.</summary>
    internal bool HoverIsStale =>
        _leftAt is { } left && (HoverClock() - left).TotalMilliseconds >= LeaveGraceMs;

    /// <summary>Note a departure and arm the settle.</summary>
    /// <remarks>
    /// One timer for the control's life, restarted per exit rather than one
    /// timer per exit — at 39 departures a second the latter would be work
    /// proportional to the churn, on the thread the churn is already starving.
    /// </remarks>
    private void BeginLeave()
    {
        _leftAt = HoverClock();

        // Cleared at once, unlike the hover point, because the two answer
        // different questions and only one of them strobes. This is what a
        // *tool change* re-asks about, and B241's rule is that with the pointer
        // gone there is nothing to re-ask — the tool's own cursor stands. Held
        // through the grace, picking up the eyedropper while the pointer was
        // off the canvas would show whatever the pointer had last been over,
        // which `WithThePointerOffTheCanvasThereIsNothingToReAskAbout` exists
        // to forbid. The ring is the thing the echo strobes, and the ring is
        // the only thing debounced.
        _cursorAt = null;
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

    /// <summary>
    /// The pointer is back, so the departure never happened. Returns whether
    /// one was pending.
    /// </summary>
    private bool CancelLeave()
    {
        var wasAway = _leftAt is not null;
        _leftAt = null;
        try { _leaveTimer?.Stop(); }
        catch { /* nothing to stop */ }
        return wasAway;
    }

    /// <summary>
    /// Why enter and exit no longer repaint unconditionally.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Measured, on the machine this is for.</b> The sixth trace counts
    /// <b>6,090 enters and 6,091 exits in 63 seconds — 97 a second</b>, none of
    /// which is the artist's hand going anywhere. Each of those used to call
    /// <c>InvalidateVisual</c>, and on a 4K document a full canvas invalidation
    /// is not free.
    /// </para>
    /// <para>
    /// <b>It became redundant when the leave grace landed, which is the point.</b>
    /// Before it, an exit tore the hover state down and the ring genuinely had
    /// to be repainted. Now an exit changes nothing at all until
    /// <see cref="SettleHover"/> decides the departure was real — and that
    /// repaints. So the invalidate on exit repainted an identical canvas, and
    /// the one on enter repainted it back.
    /// </para>
    /// <para>
    /// <b>Stated as waste removal rather than as a fix.</b> The same trace shows
    /// 43 UI-thread stalls with <em>zero</em> popups, so whatever blocks the
    /// thread is not the popup churn and may not be this either. Removing work
    /// that provably changes no pixel is right on its own terms; whether it
    /// moves the stall count is for the next trace to say, not for this comment
    /// to claim.
    /// </para>
    /// </remarks>
    internal const string RepaintOnHoverChange =
        "enter/exit repaint only when the ring actually moves or returns";

    /// <summary>
    /// Drop the hover state if the departure has lasted. Returns whether it did.
    /// </summary>
    internal bool SettleHover()
    {
        if (!HoverIsStale) return false;

        CancelLeave();
        _hoverPoint = null;
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
    /// <remarks>
    /// <b>B348: the retired path is dropped, never disposed, and that is the
    /// whole fix.</b> This cache hands the live <see cref="SKPath"/> straight to
    /// a <c>DrawOp</c>, which Avalonia runs on the <em>render</em> thread some
    /// time after <c>Render</c> returned. Disposing it here — on the UI thread,
    /// the moment the tip id changed — freed the native path out from under a
    /// draw that was already queued with it, and <c>sk_canvas_draw_path</c>
    /// dereferenced the hole. An access violation is not a managed exception, so
    /// nothing was caught, nothing was logged, and the application simply went.
    ///
    /// Windows kept what the app could not: two records, 0xC0000005 at the same
    /// fault offset on two different builds, both naming
    /// <c>SkiaSharp.SkiaApi.sk_canvas_draw_path</c>. Reached by holding E to
    /// erase and letting go — the release puts the eraser's own size back and
    /// changes the cursor's tip, which is exactly this invalidation.
    ///
    /// Dropping the reference is correct where disposing is not: a queued
    /// DrawOp holds its own managed reference, so the path cannot be collected
    /// while a draw can still reach it, and it is finalised once none can. What
    /// that costs is a handful of small native objects waiting for the GC per
    /// session, because a tip changes on a deliberate action rather than per
    /// frame — which is the trade correctness asks for and B74's caching was
    /// never about.
    /// </remarks>
    private string? _outlineTipId;
    private SKPath? _outlinePath;

    private sealed partial class DrawOp
    {
        /// <summary>
        /// The brush ring — the pointer itself while paint hides the platform
        /// cursor. Drawn by <see cref="BrushRingPainter"/>, which is where it can
        /// be rendered to a bare surface and looked at by a test.
        /// </summary>
        private static void DrawBrushCursor(SKCanvas canvas, BrushCursor c) =>
            BrushRingPainter.Draw(
                canvas, c.X, c.Y, c.Radius, c.Roundness, c.AngleDeg, c.Outline, c.Ink, c.Badge,
                c.Contrast, c.Width);
    }

    internal SKPath? TipOutlinePath(string? tipId)
    {
        if (string.IsNullOrEmpty(tipId))
        {
            // Dropped, not disposed — see the field's remarks (B348).
            _outlinePath = null;
            _outlineTipId = null;
            return null;
        }
        if (string.Equals(tipId, _outlineTipId, StringComparison.Ordinal)) return _outlinePath;

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
        // This one may be disposed: it was never returned, so no draw can hold it.
        _outlinePath = path.IsEmpty ? null : path;
        if (_outlinePath is null) path.Dispose();
        return _outlinePath;
    }
}
