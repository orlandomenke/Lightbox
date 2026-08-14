using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// The drawing surface. Displays the composited scene through a view
/// transform — fit-to-view base plus user zoom / rotation / mirror / pan —
/// and translates pointer input back into document space through the same
/// (inverted) transform.
///
/// The view transform is strictly a drawing aid: it lives only here, never
/// touches the document, the stroke record, or anything the inbetweeners
/// see. Wheel zooms (Shift+wheel rotates) around the cursor; middle-drag
/// pans; everything resets with <see cref="ResetView"/>.
///
/// Threading: the draw op runs on Avalonia's render thread and reads only the
/// immutable <see cref="RenderSnapshot"/>. Old snapshots are retired through a
/// small ring buffer so an in-flight render never touches a disposed image.
/// </summary>
public sealed partial class CanvasControl : Control
{
    /// <summary>
    /// Repaint when a property that is only read at draw time changes.
    /// </summary>
    /// <remarks>
    /// <b>B72.</b> <see cref="BrushCursorSize"/> is read inside
    /// <see cref="Render"/> and nothing invalidated when it changed, so the ring
    /// kept its old size until the pointer moved and invalidated for its own
    /// reasons. The view model was right, the binding was right, and the canvas
    /// was showing a stale answer to the one question the gizmo exists to answer.
    /// <para>
    /// A <c>StyledProperty</c> does not imply a repaint — that is what
    /// <c>AffectsRender</c> is for, and its absence is invisible in every test
    /// that moves a pointer before looking. <see cref="LazyRadius"/> was in the
    /// same position and is listed for the same reason rather than because anybody
    /// reported it.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// A named list rather than an inline argument list, because it is the thing
    /// that goes wrong: a new property read inside <see cref="Render"/> and not
    /// added here is invisible to every test that moves a pointer before looking,
    /// which is every test. <c>CanvasCursorTests</c> asserts the membership, and
    /// what that guards is the registration rather than Avalonia's own plumbing —
    /// worth saying, because the two are easy to conflate.
    /// </remarks>
    /// <remarks>
    /// Filled in the static constructor rather than here: static field
    /// initializers run in declaration order, and the properties this names are
    /// declared below — so an initializer would capture five nulls and
    /// <c>AffectsRender</c> would register nothing at all. It compiled.
    /// </remarks>
    public static readonly AvaloniaProperty[] RepaintOnChange;

    static CanvasControl()
    {
        RepaintOnChange =
        [
            BrushCursorSizeProperty,
            BrushCursorTipIdProperty,
            BrushCursorRoundnessProperty,
            BrushCursorAngleProperty,
            LazyRadiusProperty,
            SoloProperty,
            PickSampleHexProperty,
            PickCurrentHexProperty,
        ];
        AffectsRender<CanvasControl>(RepaintOnChange);

        // The intent decides the platform cursor, and nothing else does — a
        // second writer of Cursor is how the pointer ends up disagreeing with
        // what the tool will actually do.
        PointerIntentProperty.Changed.AddClassHandler<CanvasControl, CanvasCursorKind>(
            (control, e) =>
            {
                if (control._overGuide) return;
                control.Cursor = PointerCursors.For(e.NewValue.GetValueOrDefault());
            });
    }

    /// <summary>Current tool size in document units — drives the brush-shape cursor.</summary>
    public static readonly StyledProperty<double> BrushCursorSizeProperty =
        AvaloniaProperty.Register<CanvasControl, double>(nameof(BrushCursorSize), 6.0);

    public double BrushCursorSize
    {
        get => GetValue(BrushCursorSizeProperty);
        set => SetValue(BrushCursorSizeProperty, value);
    }

    /// <summary>
    /// The tip the cursor should outline, or null for the round dab.
    /// </summary>
    /// <remarks>
    /// <b>B74.</b> An id rather than a shape: the outline is traced from the tip
    /// bitmap the engine actually stamps
    /// (<see cref="Lightbox.Raster.BrushTipOutline"/>), so the preview cannot
    /// disagree with the mark. Passing a shape down from the view model would be a
    /// second description of the same thing.
    /// </remarks>
    public static readonly StyledProperty<string?> BrushCursorTipIdProperty =
        AvaloniaProperty.Register<CanvasControl, string?>(nameof(BrushCursorTipId));

    public string? BrushCursorTipId
    {
        get => GetValue(BrushCursorTipIdProperty);
        set => SetValue(BrushCursorTipIdProperty, value);
    }

    /// <summary>How flat the dab is: 1 round, below that an ellipse.</summary>
    /// <remarks>
    /// The nominal value from the brush, not <c>RoundnessAt</c>'s per-dab result.
    /// That one is jittered from the dab's position by <c>Hash01</c>, and a cursor
    /// that flickered as the pointer moved would be reporting the jitter rather
    /// than the brush.
    /// </remarks>
    public static readonly StyledProperty<double> BrushCursorRoundnessProperty =
        AvaloniaProperty.Register<CanvasControl, double>(nameof(BrushCursorRoundness), 1.0);

    public double BrushCursorRoundness
    {
        get => GetValue(BrushCursorRoundnessProperty);
        set => SetValue(BrushCursorRoundnessProperty, value);
    }

    /// <summary>The dab's angle in degrees, so a chisel is previewed at its angle.</summary>
    public static readonly StyledProperty<double> BrushCursorAngleProperty =
        AvaloniaProperty.Register<CanvasControl, double>(nameof(BrushCursorAngle));

    public double BrushCursorAngle
    {
        get => GetValue(BrushCursorAngleProperty);
        set => SetValue(BrushCursorAngleProperty, value);
    }

    /// <summary>Pulled-string dead-zone radius in document pixels (0 = gizmo hidden).</summary>
    public static readonly StyledProperty<double> LazyRadiusProperty =
        AvaloniaProperty.Register<CanvasControl, double>(nameof(LazyRadius));

    public double LazyRadius
    {
        get => GetValue(LazyRadiusProperty);
        set => SetValue(LazyRadiusProperty, value);
    }

    // The smoothed brush anchor while a live-smoothing stroke is active (doc space).
    private (double X, double Y)? _lazyAnchor;

    /// <summary>Selection manager for object selection feedback.</summary>
    private ViewModels.SelectionManager? _selectionManager;

    /// <summary>Callback to get current placements for selection rendering.</summary>
    private Func<IReadOnlyList<Core.Documents.SymbolPlacement>?>? _getPlacementsForSelection;

    /// <summary>Set the selection manager for rendering object selection feedback.</summary>
    public void SetSelectionManager(ViewModels.SelectionManager selectionManager)
    {
        _selectionManager = selectionManager;
    }

    /// <summary>Set callback to provide placements for selection rendering.</summary>
    public void SetPlacementProvider(Func<IReadOnlyList<Core.Documents.SymbolPlacement>?>? provider)
    {
        _getPlacementsForSelection = provider;
    }

    /// <summary>Alt was held when this stroke began, so it erases with the current brush.</summary>
    private bool _erasingThisStroke;

    /// <summary>Shift was held at the press: join to the previous stroke's end.</summary>
    private bool _straightFromLast;

    /// <summary>Where the stroke in progress began, for the axis lock.</summary>
    private (double X, double Y) _paintAnchor;

    /// <summary>Shift is down mid-drag: hold the stroke to one axis.</summary>
    private bool _axisLockedStroke;

    /// <summary>
    /// A point held to whichever axis the drag has gone furthest along.
    /// </summary>
    /// <remarks>
    /// Measured from the anchor every time rather than accumulated, so
    /// crossing from the horizontal run to the vertical one snaps cleanly
    /// instead of leaving the stroke wherever the changeover happened to fall.
    /// </remarks>
    private static (double X, double Y) AxisLocked((double X, double Y) anchor, double x, double y) =>
        Math.Abs(x - anchor.X) >= Math.Abs(y - anchor.Y)
            ? (x, anchor.Y)
            : (anchor.X, y);

    /// <summary>True while an Alt-held stroke is in progress (drives the cursor).</summary>
    public bool IsTemporaryEraser => _painting && _erasingThisStroke;

    /// <summary>Update (or clear) the pulled-string anchor — where paint actually lands.</summary>
    public void SetLazyAnchor(double? x, double? y)
    {
        _lazyAnchor = x is { } ax && y is { } ay ? (ax, ay) : null;
        InvalidateVisual();
    }

    private RenderSnapshot? _snapshot;

    /// <summary>
    /// The durable frame the compositor actually draws (B122). Owned here because
    /// it has to outlive individual draw operations — that is the whole point of
    /// it — and handed to each <see cref="DrawOp"/> rather than reached back into.
    /// </summary>
    private readonly PresentedFrame _presented = new();

    /// <summary>
    /// Layer rasters kept resident on the GPU (B125 stage 5), so a frame showing
    /// the same drawing as the last one is not uploaded again.
    /// </summary>
    /// <remarks>
    /// Owned here because it is filled and read inside the draw op, on the render
    /// thread, where the lease's context lives. Nothing on the UI thread may touch
    /// it — freeing a GPU resource from the wrong thread is B130 in another hat.
    /// </remarks>
    private readonly LayerTextureCache _textures = new();



    /// <summary>Resident-texture counters, for the render report. Tests only.</summary>
    internal (int Hits, int Misses, long Bytes) TextureResidency =>
        (_textures.Hits, _textures.Misses, _textures.ResidentBytes);

    /// <summary>
    /// Whether the durable presentation frame (B122) is in the paint path at all.
    /// </summary>
    /// <remarks>
    /// Off until B130's use-after-free is fixed with the retirement this class
    /// already implements for <see cref="RenderSnapshot"/>. Read once and cached,
    /// because this is consulted inside the render loop.
    /// </remarks>
    internal static bool DurableFrameEnabled { get; } =
        Environment.GetEnvironmentVariable("LIGHTBOX_DURABLE_FRAME") == "1";

    /// <summary>Whether the durable frame is on the GPU, for the status strip.</summary>
    internal bool PresentedFrameIsOnGpu => _presented.IsGpuBacked;

    /// <summary>Pixels the last present had to copy. Tests and telemetry.</summary>
    internal long LastPresentedPatchPixels => _presented.LastPatchedPixels;

    /// <summary>
    /// What the durable frame has done this session, for the render report.
    /// </summary>
    internal (long Presents, long Full, long Free, long Patched, long IfAlwaysFull, bool OnGpu, bool GpuFailed)
        PresentedFrameTotals => (
            _presented.Presents,
            _presented.FullPresents,
            _presented.FreePresents,
            _presented.TotalPatchedPixels,
            _presented.TotalPixelsIfAlwaysFull,
            _presented.IsGpuBacked,
            _presented.GpuSurfaceRequestFailed);

    /// <summary>
    /// How long published frames waited to be drawn, for the render report.
    /// </summary>
    /// <remarks>
    /// B150's other half. The clock's own stats say whether the tick arrived on
    /// time; this says whether the frame it asked for reached the screen — and a
    /// report carrying only the first reads clean on a machine where the second
    /// is the problem.
    /// </remarks>
    internal PresentLatency.Stats PresentWait => _presentWait.Snapshot;

    private readonly PresentLatency _presentWait = new();

    /// <summary>
    /// Run something that needs the compositor's Skia context, on the render
    /// thread, at the next frame.
    /// </summary>
    /// <remarks>
    /// The context only exists inside a lease, and a lease is only granted inside
    /// a draw operation — so an upload probe cannot simply be called from a menu
    /// handler. This queues the work and forces a frame; the callback runs once
    /// and is cleared, and it receives null when the canvas is on software.
    /// </remarks>
    public void RunWithGpuContext(Action<GRContext?> work)
    {
        _pendingGpuWork = work;
        InvalidateVisual();
    }

    private Action<GRContext?>? _pendingGpuWork;
    private readonly Queue<RenderSnapshot> _retired = new();

    /// <summary>Snapshots kept past the current one, even after they've been rendered.</summary>
    private const int RetiredKeep = 1;

    /// <summary>
    /// Backstop when render completions never report back — a hidden window,
    /// or headless, where nothing ever draws and the queue would grow forever.
    ///
    /// It may only ever free a snapshot the render thread has finished with.
    /// It used to free the oldest unconditionally once the queue passed this
    /// mark, which was safe only for as long as publishes never outran
    /// renders. When live wet-media previews started publishing an extra frame
    /// per pass, they did outrun them, and the compositor drew an image that
    /// had just been freed underneath it: an access violation inside
    /// <c>sk_canvas_draw_image_rect</c>, plus flickering frames on the way
    /// there. Bounded memory is not worth a crash — if everything queued is
    /// still in flight, the queue is allowed to grow.
    /// </summary>
    private const int RetiredHardCap = 4;

    /// <summary>Highest snapshot sequence the render thread has finished drawing.</summary>
    private long _lastRenderedSeq;

    /// <summary>Pointer position in view space while it hovers the canvas.</summary>
    private Point? _hoverPoint;

    // ---- view-only transform state -------------------------------------------
    private double _zoom = 1;
    private double _rotationDeg;
    private bool _mirrored;
    private Vector _pan;

    private const double MinZoom = 0.05;
    private const double MaxZoom = 32;

    /// <summary>Raised whenever zoom/rotation/mirror/pan change (for status UI).</summary>
    public event Action? ViewChanged;

    /// <summary>
    /// Visible document rectangle changed (B82: viewport culling).
    /// Raised when zoom, pan, rotation, or bounds change, with the rectangle
    /// of document space visible at the current view state. Null means the
    /// whole document is visible. Consumed by MainViewModel.SetViewport to
    /// enable the compositor to cull work to what the view shows.
    /// </summary>
    public event Action<SKRectI?>? ViewportChanged;

    /// <summary>
    /// Document pixels per screen pixel that the canvas can actually show,
    /// so compositing need not produce more detail than is displayable.
    /// A 4K document in a 1600 px window is presented at about 0.42 —
    /// rescaling that every frame costs ~29 ms, and it is pure waste.
    /// </summary>
    public event Action<double>? DisplayScaleChanged;

    /// <summary>How long the render thread took on its last frames (milliseconds).</summary>
    public event Action<double>? FrameRendered;

    /// <summary>
    /// Pressure to draw the brush ring at, and whether the pen is down.
    /// Raised on hover and while painting so the ring can show the thickness
    /// the stroke is actually laying down rather than the brush's maximum.
    /// </summary>
    public event Action<double, bool>? CursorPressureChanged;

    private double _reportedCursorPressure = -1;
    private bool _reportedPenDown;

    private void ReportCursorPressure(double pressure, bool penDown)
    {
        if (CursorPressureChanged is null) return;
        // Quantised: a tablet reports pressure on every event and the ring
        // only needs to move when it would visibly change.
        var stepped = Math.Round(Math.Clamp(pressure, 0, 1) * 50) / 50.0;
        if (Math.Abs(stepped - _reportedCursorPressure) < 0.001 && penDown == _reportedPenDown) return;
        _reportedCursorPressure = stepped;
        _reportedPenDown = penDown;
        CursorPressureChanged.Invoke(stepped, penDown);
    }

    private double _reportedScale = -1;

    private void ReportDisplayScale()
    {
        var snapshot = _snapshot;
        if (snapshot is null || Bounds.Width <= 0) return;
        // Quantised to eighths: a continuous zoom would otherwise reallocate
        // the compose buffers on every wheel notch.
        var raw = Math.Clamp(FitScale() * _zoom, 0.125, 1.0);
        var stepped = Math.Clamp(Math.Ceiling(raw * 8) / 8.0, 0.125, 1.0);
        if (Math.Abs(stepped - _reportedScale) < 0.001) return;
        _reportedScale = stepped;
        DisplayScaleChanged?.Invoke(stepped);
    }

    /// <summary>Raised (on the UI thread) when an input handler fails, so the error is visible.</summary>
    public event Action<string>? CanvasError;

    // ---- diagnostics ----------------------------------------------------------
    // Drawing must survive anything; failures are logged once per context to
    // the diagnostics folder instead of killing the input or render loop.
    // The de-duplication and the file both live in DiagnosticLog now, so this
    // breadcrumb lands where a crash report does rather than in %TEMP%.

    /// <summary>
    /// Whether Avalonia handed the canvas a GPU-backed Skia context, and so
    /// whether the frame the artist sees is presented by the GPU at all.
    ///
    /// Every "should this be on the GPU" question starts here and cannot be
    /// answered from a headless container: on Windows the default backend is
    /// ANGLE/D3D11 and this is expected to read "GPU", but a machine that fell
    /// back to software rendering has a completely different cost profile and
    /// no amount of GPU work would help it. Reported in the info strip so it
    /// is a fact rather than an assumption.
    /// </summary>
    public static string GraphicsBackend { get; private set; } = "unknown";

    /// <summary>
    /// True once a frame has been presented without a GPU context, null while
    /// nothing has been drawn yet.
    /// </summary>
    /// <remarks>
    /// Worth a separate flag from the label because something has to act on
    /// it. A machine on the software rasteriser is not a machine with a
    /// slightly slower canvas — presenting the frame becomes the dominant
    /// cost, and the setting that decides how many pixels get presented is the
    /// only lever that helps.
    /// </remarks>
    public static bool? SoftwareRendering { get; private set; }

    /// <summary>Raised the first time the backend is known.</summary>
    public static event Action? BackendDetected;

    /// <summary>
    /// The context's texture limit, or null when there is no context. Reported
    /// rather than merely used: at 4K with display scaling the presentation
    /// surface approaches this, and exceeding it is what makes a GPU surface fail
    /// to allocate and fall back to CPU without saying so.
    /// </summary>
    public static int? MaxTextureSize { get; private set; }

    private static void RecordBackend(ISkiaSharpApiLease lease)
    {
        if (GraphicsBackend != "unknown") return;
        var software = lease.GrContext is null;
        GraphicsBackend = software ? "CPU (software)" : "GPU";
        SoftwareRendering = software;
        MaxTextureSize = lease.GrContext?.MaxTextureSize;
        BackendDetected?.Invoke();
    }

    /// <summary>Test seam: pretend the backend came back as software, or as a GPU.</summary>
    internal static void ForceBackendForTests(bool? software)
    {
        SoftwareRendering = software;
        GraphicsBackend = software switch
        {
            true => "CPU (software)",
            false => "GPU",
            null => "unknown",
        };
        if (software is not null) BackendDetected?.Invoke();
    }

    /// <summary>
    /// Record a survivable canvas failure. Kept here so the nine call sites
    /// read the same as they always did; the writing itself moved.
    /// </summary>
    internal static void LogDiag(string context, Exception ex) =>
        Lightbox.App.Services.DiagnosticLog.WriteOnce(context, ex);

    private void ReportInputError(string context, Exception ex)
    {
        LogDiag(context, ex);
        CanvasError?.Invoke(
            $"Canvas {context} error: {ex.Message} — details in {Lightbox.App.Services.DiagnosticLog.Directory}");
    }

    /// <summary>
    /// The camera frame's corners in document coordinates, or null for no
    /// camera — in which case nothing camera-related is drawn at all. Set from
    /// the view model whenever the playhead or the camera changes.
    /// </summary>
    public SKPoint[]? CameraFrame
    {
        get => _cameraFrame;
        set
        {
            _cameraFrame = value;
            InvalidateVisual();
        }
    }

    private SKPoint[]? _cameraFrame;

    /// <summary>The camera gizmo's drags, as document-space deltas per move.
    /// The window maps them onto the framing at the playhead, which keys it —
    /// adjusting the camera IS keying it, same as the numeric fields.</summary>
    /// <summary>
    /// The pointer moved over the document: stroke coordinates and the keys
    /// held, so the view model can answer the two questions about a place that
    /// only a place can answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Raised on hover only — while a gesture is in progress the answer is
    /// already decided and re-asking it would cost a bitmap read per pointer
    /// event during the one operation that can least afford it (invariant 6).
    /// </para>
    /// <para>
    /// Coalesced by position: a pen reports far more moves than it crosses
    /// pixels, and every duplicate would be a wasted read of the same pixel.
    /// </para>
    /// </remarks>
    public event Action<double, double, KeyModifiers>? PointerHovered;

    private (int X, int Y, KeyModifiers Mods)? _lastHoverReport;

    private void ReportHover(Point view, KeyModifiers modifiers)
    {
        if (PointerHovered is null) return;
        var (x, y) = ViewToDoc(view);
        if (!double.IsFinite(x) || !double.IsFinite(y)) return;

        var key = ((int)Math.Round(x), (int)Math.Round(y), modifiers);
        if (_lastHoverReport == key) return;
        _lastHoverReport = key;
        PointerHovered.Invoke(x, y, modifiers);
    }

    public event Action<double, double>? CameraPanned;

    /// <summary>Multiplicative zoom change from a corner drag.</summary>
    public event Action<double>? CameraZoomedBy;

    /// <summary>Roll change in degrees from the rotate handle.</summary>
    public event Action<double>? CameraRotatedBy;

    private enum CameraDrag { None, Pan, Zoom, Rotate }

    private CameraDrag _cameraDrag;
    private Point _cameraLast;

    /// <summary>Centre of the camera frame, in document coordinates.</summary>
    private static SKPoint FrameCentre(SKPoint[] corners) => new(
        (corners[0].X + corners[1].X + corners[2].X + corners[3].X) / 4,
        (corners[0].Y + corners[1].Y + corners[2].Y + corners[3].Y) / 4);

    /// <summary>Midpoint of the frame's top edge — the pan grip.</summary>
    private static SKPoint PanGrip(SKPoint[] corners) => new(
        (corners[0].X + corners[1].X) / 2, (corners[0].Y + corners[1].Y) / 2);

    /// <summary>The rotate handle: pushed out from the pan grip, away from the centre.</summary>
    internal static SKPoint RotateHandle(SKPoint[] corners, float offset)
    {
        var grip = PanGrip(corners);
        var centre = FrameCentre(corners);
        var dx = grip.X - centre.X;
        var dy = grip.Y - centre.Y;
        var len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-3f) return grip;
        return new SKPoint(grip.X + dx / len * offset, grip.Y + dy / len * offset);
    }

    private CameraDrag CameraHandleAt(double x, double y, float scale)
    {
        if (_cameraFrame is not { Length: 4 } corners) return CameraDrag.None;
        var r = 9f / Math.Max(0.01f, scale);
        bool Near(SKPoint p) => Math.Abs(p.X - x) <= r && Math.Abs(p.Y - y) <= r;

        if (Near(RotateHandle(corners, 26f / Math.Max(0.01f, scale)))) return CameraDrag.Rotate;
        if (Near(PanGrip(corners))) return CameraDrag.Pan;
        foreach (var corner in corners)
        {
            if (Near(corner)) return CameraDrag.Zoom;
        }
        return CameraDrag.None;
    }

    /// <summary>
    /// One editable box on a reference sheet, in document coordinates.
    /// </summary>
    /// <remarks>
    /// A snapshot, not a live view of the cell. The renderer runs on another
    /// thread and must never read a document object that the UI thread may be
    /// halfway through editing.
    /// </remarks>
    public readonly record struct ReferenceBox(
        float X, float Y, float W, float H, float PivotX, float PivotY, bool Selected);

    /// <summary>
    /// The reference grid's gizmos, or null when the mode is off — in which
    /// case nothing grid-related is drawn at all.
    /// </summary>
    public IReadOnlyList<ReferenceBox>? ReferenceBoxes
    {
        get => _referenceBoxes;
        set
        {
            _referenceBoxes = value;
            InvalidateVisual();
        }
    }

    private IReadOnlyList<ReferenceBox>? _referenceBoxes;

    /// <summary>
    /// One guide, flattened for the renderer.
    /// </summary>
    /// <remarks>
    /// A snapshot rather than the <c>Guide</c> itself: the renderer runs on
    /// another thread and must never read a document object the UI thread may
    /// be halfway through editing. Same reason the reference boxes are copied.
    /// </remarks>
    public readonly record struct GuideLine(
        string Id, int Kind, float X, float Y, float Spacing, IReadOnlyList<double> Angles,
        string? Label = null, int Divisions = 0);

    // The Guides, DraftGuide and BalanceDots overlay properties live in
    // CanvasControl.Guides.cs with the rest of the guide chrome.

    /// <summary>
    /// Show one channel of the artwork as grayscale, or all of them as normal.
    /// View-only: the record is untouched, only the draw changes.
    /// </summary>
    public static readonly StyledProperty<ChannelSolo> SoloProperty =
        AvaloniaProperty.Register<CanvasControl, ChannelSolo>(nameof(Solo));

    public ChannelSolo Solo
    {
        get => GetValue(SoloProperty);
        set => SetValue(SoloProperty, value);
    }


    /// <summary>
    /// The rig marks a drag is currently carrying, and how far.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Chrome, not data — the same bargain <see cref="DraftGuide"/> makes, and for
    /// the same reason: the record must not be touched per pointer event, but a mark
    /// that does not move until you let go is one you place twice. <c>DragRig</c>'s
    /// own remark already said live feedback "is the overlay's own business and
    /// touches nothing"; this is the overlay finally doing it (B112).
    /// </para>
    /// <para>
    /// One field covers every rig drag, single or group, because
    /// <see cref="RigOverlay.Drag"/> is what decides what a delta means: a corner
    /// resizes and <see cref="RigCorner.None"/> translates, so the preview is the
    /// same arithmetic the commit will use rather than a second guess at it.
    /// </para>
    /// </remarks>
    private (HashSet<string> Ids, RigCorner Corner, double Dx, double Dy)? _rigPreview;

    internal void PreviewRig(IEnumerable<string> ids, RigCorner corner, double dx, double dy)
    {
        _rigPreview = (new HashSet<string>(ids, StringComparer.Ordinal), corner, dx, dy);
        InvalidateVisual();
    }

    /// <summary>
    /// Preview where a group drag has reached, measured from where it was picked up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Absolute rather than a sum of the per-event increments, which is the same
    /// choice the content move makes and for a reason B109 paid for: the pointer
    /// handler advances its own anchor every event, so anything that adds up what it
    /// reports is one rounding away from drifting off the pointer.
    /// </para>
    /// <para>
    /// A seam rather than four lines in the pointer handler, so the arithmetic is
    /// reachable from a test — the wiring above it is two calls, and this is the part
    /// that can be wrong while still compiling.
    /// </para>
    /// </remarks>
    /// <summary>Note where a group drag was picked up, for the preview to measure from.</summary>
    internal void BeginRigGroupPreview(double x, double y, bool shapes)
    {
        if (shapes) _shapeMoveStart = (x, y);
        else _anchorMoveStart = (x, y);
    }

    internal void TrackRigGroupPreview(double mx, double my, bool shapes)
    {
        if (_selectionManager is not { } selection) return;
        var start = shapes ? _shapeMoveStart : _anchorMoveStart;
        PreviewRig(
            shapes ? selection.SelectedShapeIds : selection.SelectedAnchorIds,
            RigCorner.None, mx - start.X, my - start.Y);
    }

    /// <summary>Drop the preview, because the record now holds the move.</summary>
    internal void ClearRigPreview()
    {
        if (_rigPreview is null) return;
        _rigPreview = null;
        InvalidateVisual();
    }

    /// <summary>The marks as they should be drawn while a drag is in flight.</summary>
    /// <remarks>
    /// Returns the list unchanged when nothing is being dragged, so the overlay pays
    /// nothing for a preview that does not exist. Internal rather than private so a
    /// test can read what the overlay would draw: synthetic pointer input through
    /// Xvfb is unreliable here, and a dropped click looks exactly like a bug.
    /// </remarks>
    internal IReadOnlyList<RigMark>? WithRigPreview(IReadOnlyList<RigMark>? marks)
    {
        if (_rigPreview is not { } preview || marks is null || marks.Count == 0) return marks;

        var shown = new List<RigMark>(marks.Count);
        foreach (var mark in marks)
        {
            shown.Add(mark.Id is { } id && preview.Ids.Contains(id)
                ? RigOverlay.Drag(mark, preview.Corner, preview.Dx, preview.Dy)
                : mark);
        }
        return shown;
    }

    /// <summary>
    /// Whether a guide under the pointer can be picked up and moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off unless the rulers are showing, and that is the design rather than
    /// an implementation detail. Grabbing a guide and drawing along one are
    /// the same gesture in the same place, so something has to say which was
    /// meant. Photoshop's answer is the Move tool; this app has no move tool,
    /// so the rulers are the switch: turn them on and you are arranging the
    /// rig, turn them off and the rig is scenery you draw over. It is one
    /// toggle instead of a modifier nobody discovers, and it is why the
    /// guide controls only appear on the shortcut bar when the rulers do.
    /// </para>
    /// <para>
    /// The cursor is the only affordance. No handles float over the artwork —
    /// an overlay of little buttons sits between the artist and the drawing,
    /// and it is the thing this design exists to avoid.
    /// </para>
    /// </remarks>
    public bool GuideDragEnabled { get; set; }

    /// <summary>The Move tool picked the drawing up. <c>wholeLayer</c> is Ctrl.</summary>
    public event Action<double, double, bool>? ContentMoveStarted;

    /// <summary>Absolute position, plus Shift for the axis lock.</summary>
    public event Action<double, double, bool>? ContentMoveUpdated;

    public event Action? ContentMoveEnded;

    public event Action? ContentMoveCancelled;

    private bool _movingContent;

    private bool _movingGuides;
    private (double X, double Y) _guideMoveLast;

    private bool _movingRefBoxes;
    private (double X, double Y) _refBoxMoveLast;

    private bool _movingAnchors;
    private (double X, double Y) _anchorMoveLast;
    private (double X, double Y) _anchorMoveStart;

    private bool _movingShapes;
    private (double X, double Y) _shapeMoveLast;
    private (double X, double Y) _shapeMoveStart;

    /// <summary>Guides move started via Selection in Move mode.</summary>
    public event Action? GuidesMovedStarted;

    /// <summary>Guides moved by a delta in document pixels.</summary>
    public event Action<double, double>? GuidesMoved;

    /// <summary>Guides move finished.</summary>
    public event Action? GuidesMovedEnded;

    /// <summary>Reference boxes move started via Selection in Move mode.</summary>
    public event Action? RefBoxesMoveStarted;

    /// <summary>Reference boxes moved by a delta in document pixels.</summary>
    public event Action<double, double>? RefBoxesMoved;

    /// <summary>Reference boxes move finished.</summary>
    public event Action? RefBoxesMovedEnded;

    /// <summary>Anchors move started via Selection in Move mode.</summary>
    public event Action? AnchorsMoveStarted;

    /// <summary>Anchors moved by a delta in document pixels.</summary>
    public event Action<double, double>? AnchorsMoved;

    /// <summary>Anchors move finished.</summary>
    public event Action? AnchorsMovedEnded;

    /// <summary>Collision shapes move started via Selection in Move mode.</summary>
    public event Action? ShapesMoveStarted;

    /// <summary>Collision shapes moved by a delta in document pixels.</summary>
    public event Action<double, double>? ShapesMoved;

    /// <summary>Collision shapes move finished.</summary>
    public event Action? ShapesMovedEnded;

    // The guide-grab section — picking a guide up, move versus height-scale
    // resize, and the hover cursor — lives in CanvasControl.Guides.cs.

    /// <summary>
    /// What the pointer is currently saying the tool will do — decided by
    /// <see cref="CanvasCursor"/> and set from the view model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A property rather than a decision made here, so the cursor and whatever
    /// else reports the same fact cannot disagree. The control's job is the last
    /// step only: turning a name into a platform cursor.
    /// </para>
    /// <para>
    /// Platform cursors for now, which is what the roadmap allows — the mechanism
    /// is the part that was missing, and custom artwork can replace this mapping
    /// without anything above it changing.
    /// </para>
    /// </remarks>
    public static readonly StyledProperty<CanvasCursorKind> PointerIntentProperty =
        AvaloniaProperty.Register<CanvasControl, CanvasCursorKind>(nameof(PointerIntent));

    public CanvasCursorKind PointerIntent
    {
        get => GetValue(PointerIntentProperty);
        set => SetValue(PointerIntentProperty, value);
    }

    // The two colours the eyedropper's ring compares are properties too, and
    // they live in CanvasControl.Pointer.cs with the gizmo that reads them.

    // The intent-to-cursor mapping lives in PointerCursors now (B175): as a
    // private switch here it collapsed Pick, Fill and Precise onto one cross,
    // and being private is why no test could say so.

    /// <summary>The box being drawn by hand right now, in document coordinates.</summary>
    private SKRect? _newBox;

    public double ZoomPercent => _zoom * 100;

    public bool IsMirrored => _mirrored;

    /// <summary>
    /// The whole view transform as one value, so a document can be left framed
    /// the way the artist framed it.
    /// </summary>
    /// <remarks>
    /// <b>B67.</b> Zoom, rotation, mirror and pan were four private fields on a
    /// control that outlives every document it shows, so framing a face at 400%
    /// in one drawing opened the next one at 400% too. Reading and writing them
    /// as a group is what makes "put this down, pick that up" a single
    /// assignment rather than four that can drift apart.
    ///
    /// Still view-only, and this is the property that could quietly stop being
    /// so: the setter touches nothing but these four fields and a repaint.
    /// </remarks>
    public ViewModels.CanvasViewState Framing
    {
        get => new(_zoom, _rotationDeg, _mirrored, _pan.X, _pan.Y);
        set
        {
            _zoom = Math.Clamp(value.Zoom, MinZoom, MaxZoom);
            _rotationDeg = value.RotationDeg;
            _mirrored = value.Mirrored;
            _pan = new Vector(value.PanX, value.PanY);
            ViewUpdated();
        }
    }

    public CanvasControl()
    {
        // The brush cursor drawn by the render op replaces the OS cursor.
        Cursor = new Cursor(StandardCursorType.None);
        // Take keyboard focus on click so arrow-key nudging isn't swallowed
        // by whatever control (menu, slider) held focus before.
        Focusable = true;
    }

    /// <summary>Begin a stroke at a document-space position (pressure 0..1).</summary>
    /// <summary>
    /// Stroke begun: document x, y, pressure, whether Alt was held — an Alt
    /// stroke erases with the current brush rather than switching tools — and
    /// whether Shift was, which joins this stroke to the last one's end.
    /// </summary>
    public event Action<double, double, double, bool, bool>? PaintStarted;

    /// <summary>
    /// Extend the live stroke with ALL coalesced samples of one pointer event
    /// (document space) — batched so the consumer repaints once per event,
    /// not once per sample.
    /// </summary>
    public event Action<IReadOnlyList<ViewModels.MainViewModel.PointerSample>>? PaintMoved;

    public event Action? PaintEnded;

    // ---- tool-aware input ------------------------------------------------------

    public enum CanvasToolMode
    {
        Paint,
        Fill,
        SelectFreehand,
        SelectPolygon,
        SelectRect,
        SelectEllipse,
        SelectWand,
        Pick,
        Transform,
        Gradient,
        Shape,
        Move,
        Select,

        /// <summary>
        /// The Bone tool: presses select bones, drags create or move them, and
        /// in posing mode a drag rotates and keys at the playhead. Always in
        /// the palette — the first drag on an unrigged document creates the
        /// armature, which is how the capability stays reachable while the
        /// record stays absent (Q81).
        /// </summary>
        Bone,

        /// <summary>
        /// The white arrow: nodes and handles on the one isolated stroke.
        /// </summary>
        /// <remarks>
        /// Distinct from <see cref="Select"/> rather than a flag on it, because
        /// what a press means is entirely different — a node, not a stroke — and
        /// folding the two would make one gesture mean two things depending on
        /// state, which is the ambiguity the vector design exists to avoid.
        /// </remarks>
        PathEdit,

        /// <summary>
        /// The pen: every press places a node rather than starting a mark.
        /// </summary>
        /// <remarks>
        /// A press and a drag mean one gesture here — place a node, then curve
        /// it — so the paint path's begin/move/end cannot be reused with a flag.
        /// The tool that looks most like the brush is the one that shares least
        /// with it.
        /// </remarks>
        Pen,

        /// <summary>
        /// The width tool: a press grabs the line, a drag changes its weight.
        /// </summary>
        /// <remarks>
        /// Shares the isolation session with <see cref="PathEdit"/> and not its
        /// gesture: what a press means here is a position along the line rather
        /// than a node or a handle, so folding the two would put the ambiguity
        /// back that separate tools exist to remove.
        /// </remarks>
        Width,
    }

    public static readonly StyledProperty<CanvasToolMode> ToolModeProperty =
        AvaloniaProperty.Register<CanvasControl, CanvasToolMode>(nameof(ToolMode));

    public CanvasToolMode ToolMode
    {
        get => GetValue(ToolModeProperty);
        set => SetValue(ToolModeProperty, value);
    }

    /// <summary>
    /// Dragging on the canvas lines the imported reference up instead of
    /// drawing.
    /// </summary>
    /// <remarks>
    /// A mode rather than a modifier chord. Shift, Ctrl and Alt are all
    /// already spoken for on the canvas — brush resize, held eyedropper, held
    /// eraser — and a fourth meaning for one of them would be a chord nobody
    /// finds and everybody triggers by accident. This is switched on from the
    /// Reference panel, where you already are when you want it.
    /// </remarks>
    public static readonly StyledProperty<bool> ReferenceAlignModeProperty =
        AvaloniaProperty.Register<CanvasControl, bool>(nameof(ReferenceAlignMode));

    public bool ReferenceAlignMode
    {
        get => GetValue(ReferenceAlignModeProperty);
        set => SetValue(ReferenceAlignModeProperty, value);
    }

    /// <summary>
    /// The reference was dragged by this much in document pixels. The flag is
    /// Shift: move the whole sheet rather than this one frame.
    /// </summary>
    public event Action<double, double, bool>? ReferenceDragged;

    private bool _aligningReference;

    // ---- the reference grid gesture -------------------------------------------

    /// <summary>What a press in grid-edit mode turned out to be.</summary>
    private enum GridGesture
    {
        None,
        Move,
        Resize,
        Draw,
    }

    private GridGesture _gridGesture;
    private int _gridIndex = -1;
    private bool _gridLeft, _gridTop;
    private Point _gridLast;
    private Point _gridStart;

    /// <summary>A box was picked, or the selection was cleared with -1.</summary>
    public event Action<int>? ReferenceBoxPicked;

    /// <summary>Move box <c>index</c> by a document-space delta.</summary>
    public event Action<int, double, double>? ReferenceBoxMoved;

    /// <summary>Resize box <c>index</c> from the corner named by the two flags.</summary>
    public event Action<int, bool, bool, double, double>? ReferenceBoxResized;

    /// <summary>A box drawn by hand, in document coordinates.</summary>
    public event Action<double, double, double, double>? ReferenceBoxDrawn;

    /// <summary>
    /// Decide what a press in grid-edit mode means: a corner grabs a resize,
    /// the inside of a box grabs a move, and empty canvas draws a new one.
    /// </summary>
    /// <remarks>
    /// Corners are tested before insides, and the selected box before the
    /// rest. A corner sits inside its own box, so the other order makes the
    /// handles unreachable — they would every one of them be a move.
    /// </remarks>
    private void BeginGridGesture(double x, double y)
    {
        _gridStart = new Point(x, y);
        _gridLast = _gridStart;
        var boxes = ReferenceBoxes ?? [];
        var reach = 6 / Math.Max(0.01, _zoom * FitScale());

        for (var i = boxes.Count - 1; i >= 0; i--)
        {
            if (!boxes[i].Selected) continue;
            if (CornerOf(boxes[i], x, y, reach) is not { } corner) continue;
            (_gridGesture, _gridIndex, _gridLeft, _gridTop) = (GridGesture.Resize, i, corner.Left, corner.Top);
            return;
        }

        for (var i = boxes.Count - 1; i >= 0; i--)
        {
            var b = boxes[i];
            if (x < b.X || x > b.X + b.W || y < b.Y || y > b.Y + b.H) continue;
            _gridGesture = GridGesture.Move;
            _gridIndex = i;
            ReferenceBoxPicked?.Invoke(i);
            return;
        }

        _gridGesture = GridGesture.Draw;
        _gridIndex = -1;
        ReferenceBoxPicked?.Invoke(-1);
    }

    private static (bool Left, bool Top)? CornerOf(ReferenceBox box, double x, double y, double reach)
    {
        foreach (var (cx, cy, left, top) in ((double, double, bool, bool)[])
            [
                (box.X, box.Y, true, true),
                (box.X + box.W, box.Y, false, true),
                (box.X, box.Y + box.H, true, false),
                (box.X + box.W, box.Y + box.H, false, false),
            ])
        {
            if (Math.Abs(x - cx) <= reach && Math.Abs(y - cy) <= reach) return (left, top);
        }
        return null;
    }
    private Point _alignLast;

    /// <summary>Fill tool click at a document position.</summary>
    public event Action<double, double, bool>? FillClicked;

    /// <summary>Magic-wand click at a document position (Shift=add, Alt=subtract).</summary>
    public event Action<double, double, bool, bool>? WandClicked;

    /// <summary>Eyedropper click at a document position.</summary>
    public event Action<double, double>? PickClicked;

    /// <summary>Gradient tool: the drag that defines the ramp's axis.</summary>
    public event Action<double, double>? GradientDragStarted;

    public event Action<double, double, bool>? GradientDragMoved;

    public event Action<double, double, bool>? GradientDragEnded;

    /// <summary>
    /// The black arrow pressed at a point: <c>(x, y, tolerance, shift, alt)</c>,
    /// answering whether a line was actually there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A delegate rather than an event because the canvas needs the answer back.
    /// Same shape as <c>_getPlacementsForSelection</c>, which asks the view model
    /// a question for the same reason.
    /// </para>
    /// <para>
    /// The tolerance travels with the call because only the canvas knows the
    /// zoom and only the view model knows the strokes. A fixed document-space
    /// number would make a thin line unhittable zoomed out and grab the wrong
    /// one zoomed in.
    /// </para>
    /// </remarks>
    private Func<double, double, double, bool, bool, bool>? _pickLine;

    /// <summary>Hand the canvas the question it asks on a black-arrow press.</summary>
    public void SetLinePicker(Func<double, double, double, bool, bool, bool>? pick) =>
        _pickLine = pick;

    // ---- reshaping one line (phase 2) ----------------------------------------

    /// <summary>
    /// Double-clicked a line with the black arrow: go into it. Returns whether
    /// something was there.
    /// </summary>
    /// <remarks>
    /// A delegate that answers rather than an event that does not, for the same
    /// reason the line picker is one: the canvas has to know whether the press
    /// became an isolation or is still a selection.
    /// </remarks>
    private Func<double, double, double, bool>? _enterPathEdit;

    public void SetPathEditEntry(Func<double, double, double, bool>? enter) =>
        _enterPathEdit = enter;

    /// <summary>Grab a node or handle. Returns what was taken, or a miss.</summary>
    private Func<double, double, double, bool, ViewModels.PathHit>? _grabPathPart;

    /// <summary>Drag it — live, view-only, no document write.</summary>
    private Action<ViewModels.PathHit, double, double, double, double, bool>? _dragPathPart;

    /// <summary>Let go: one undo step for the whole drag.</summary>
    private Action? _commitPathEdit;

    /// <summary>
    /// Preview what a click would reach into. Returns whether the answer moved.
    /// </summary>
    private Func<double, double, double, bool>? _hoverPathPart;

    public void SetPathEditHandlers(
        Func<double, double, double, bool, ViewModels.PathHit>? grab,
        Action<ViewModels.PathHit, double, double, double, double, bool>? drag,
        Action? commit,
        Func<double, double, double, bool>? hover = null)
    {
        _grabPathPart = grab;
        _dragPathPart = drag;
        _commitPathEdit = commit;
        _hoverPathPart = hover;
    }

    private ViewModels.PathHit _pathGrab = ViewModels.PathHit.Miss;
    private (double X, double Y)? _pathDragLast;

    /// <summary>Press: place a node, or close the path. Returns whether it took.</summary>
    private Func<double, double, double, bool, bool, bool>? _penPress;

    /// <summary>Drag after that press: curve the node just placed.</summary>
    private Action<double, double, bool, bool>? _penDrag;

    /// <summary>Release: the node keeps its curve.</summary>
    private Action? _penRelease;

    /// <summary>
    /// Move with no button down: the segment not placed yet. Carries the grab
    /// tolerance so the view model can preview a click that would close the
    /// path with the same distance the press will use.
    /// </summary>
    private Action<double, double, double, bool>? _penHover;

    public void SetPenHandlers(
        Func<double, double, double, bool, bool, bool>? press,
        Action<double, double, bool, bool>? drag,
        Action? release,
        Action<double, double, double, bool>? hover)
    {
        _penPress = press;
        _penDrag = drag;
        _penRelease = release;
        _penHover = hover;
    }

    private bool _penShaping;

    /// <summary>Press: take hold of the line. Returns where along it, or -1.</summary>
    private Func<double, double, double, double>? _grabWidth;

    /// <summary>Drag: the weight follows how far the pointer is off the line.</summary>
    private Action<double, double, double>? _dragWidth;

    /// <summary>Release: one undo step for the whole drag.</summary>
    private Action? _endWidth;

    public void SetWidthHandlers(
        Func<double, double, double, double>? grab,
        Action<double, double, double>? drag,
        Action? end)
    {
        _grabWidth = grab;
        _dragWidth = drag;
        _endWidth = end;
    }

    /// <summary>Where along the line the width drag has hold, or -1.</summary>
    private double _widthGrab = -1;

    /// <summary>
    /// The path being drawn, in document space, as a polyline.
    /// </summary>
    /// <remarks>
    /// <b>Chrome, not paint.</b> The shape tool stamps its preview with the real
    /// brush, which is right for one drag; a pen session lasts as long as it
    /// takes to place a dozen nodes, and re-stamping the whole path into the
    /// full-canvas scratch on every pointer move is what invariant 6 forbids.
    /// A traced line costs a path per redraw and says everything the artist
    /// needs while placing — the brush arrives at the commit.
    /// </remarks>
    private IReadOnlyList<Core.Documents.StrokePoint>? _penPreview;

    public void SetPenPreview(IReadOnlyList<Core.Documents.StrokePoint>? points)
    {
        _penPreview = points is { Count: > 1 } ? points : null;
        InvalidateVisual();
    }

    /// <summary>
    /// The isolated line's current shape, retraced live while a node drags.
    /// </summary>
    /// <remarks>
    /// The raster stroke does not re-render until the drag commits (invariant
    /// 6 — a per-move re-render repaints the frame from its strokes hundreds
    /// of times a drag), so without this the nodes move and the line they
    /// describe stays put until release. Same trace the pen draws, on its own
    /// channel: the pen's path now survives a tool switch, so the two can be
    /// alive at once and must not clobber each other.
    /// </remarks>
    private IReadOnlyList<Core.Documents.StrokePoint>? _pathTrace;

    public void SetPathTrace(IReadOnlyList<Core.Documents.StrokePoint>? points)
    {
        _pathTrace = points is { Count: > 1 } ? points : null;
        InvalidateVisual();
    }

    /// <summary>
    /// The nodes to draw, in document space, and which are selected.
    /// </summary>
    /// <remarks>
    /// Handed over on change rather than polled, like the selected-line
    /// outlines: rebuilding this per pointer move would be work proportional to
    /// the path inside a move handler. It is small — a fitted line is a handful
    /// of nodes — but the rule is the rule, and B147 is what happens when a
    /// canvas's private copy stops being refreshed.
    /// </remarks>
    private IReadOnlyList<PathNodeGlyph>? _pathNodes;

    /// <summary>One node as the overlay needs it: where it is and what it is.</summary>
    /// <param name="CloseHint">
    /// The pen's closing indicator: a ring around the first node while a click
    /// would join the path back to it.
    /// </param>
    public readonly record struct PathNodeGlyph(
        double X, double Y,
        double InX, double InY,
        double OutX, double OutY,
        bool Corner, bool Selected, bool CloseHint = false);

    public void SetPathNodes(IReadOnlyList<PathNodeGlyph>? nodes)
    {
        _pathNodes = nodes is { Count: > 0 } ? nodes : null;
        InvalidateVisual();
    }

    /// <summary>
    /// A marquee dragged with the arrow: the rect in document space, and
    /// whether Shift was held at the press to add rather than replace.
    /// </summary>
    public event Action<SKRect, bool>? LinesMarqueed;

    private (double X, double Y)? _lineMarqueeFrom;
    private (double X, double Y) _lineMarqueeTo;
    private bool _lineMarqueeAdds;

    /// <summary>
    /// Below this, a drag was a click with a shaky hand.
    /// </summary>
    /// <remarks>
    /// Screen pixels, so it means the same thing at every zoom. Without it, a
    /// click that moved two pixels would select a two-pixel box's worth of
    /// nothing and silently drop whatever the click had just picked.
    /// </remarks>
    private const double MarqueeMinimumPixels = 3.0;

    /// <summary>
    /// How near a click has to land, in screen pixels, before it counts.
    /// </summary>
    /// <remarks>
    /// The same 10 the transform gizmo's handles use. One number for "close
    /// enough to have meant it" rather than two that drift apart.
    /// </remarks>
    private const double GrabPixels = 10.0;

    /// <summary>The marquee in flight, or null when there is not one.</summary>
    private SKRect? LineMarqueeRect()
    {
        if (_lineMarqueeFrom is not { } from) return null;
        var to = _lineMarqueeTo;
        return SKRect.Create(
            (float)Math.Min(from.X, to.X), (float)Math.Min(from.Y, to.Y),
            (float)Math.Abs(to.X - from.X), (float)Math.Abs(to.Y - from.Y));
    }

    /// <summary>Screen pixels as document units at the current zoom.</summary>
    private double DocTolerance(double screenPixels) =>
        screenPixels / Math.Max(0.01, FitScale() * _zoom);

    /// <summary>
    /// A shape drag. The modifiers travel with the move and the end because
    /// they are read live — Shift held halfway through a rectangle squares it
    /// there and then, which is the only behaviour anybody expects.
    /// </summary>
    public event Action<double, double>? ShapeDragStarted;

    public event Action<double, double, bool, bool>? ShapeDragMoved;

    public event Action<double, double, bool, bool>? ShapeDragEnded;

    /// <summary>Capture was lost mid-drag: abandon the ramp rather than commit it.</summary>
    public event Action? GradientDragCancelled;

    /// <summary>The axis being dragged, in document coordinates, or null when idle.</summary>
    private (double X, double Y)? _gradientFrom, _gradientTo;

    /// <summary>
    /// Show the axis while the VM renders the ramp. View-only chrome, like the
    /// transform gizmo: it is drawn over the composite and never reaches the
    /// document.
    /// </summary>
    public void SetGradientAxis((double X, double Y)? from, (double X, double Y)? to)
    {
        _gradientFrom = from;
        _gradientTo = to;
        InvalidateVisual();
    }

    private (SKPoint From, SKPoint To)? GradientAxisPoints() =>
        _gradientFrom is { } a && _gradientTo is { } b
            ? (new SKPoint((float)a.X, (float)a.Y), new SKPoint((float)b.X, (float)b.Y))
            : null;

    // ---- transform gizmo (Ctrl+T session) --------------------------------------
    // The gizmo owns the interactive state (pivot, scale, angle, offset or a
    // free quad in perspective mode); the VM owns the document side. The
    // window mediates begin/commit/cancel.

    private bool _gradientDragging;

    private bool _shapeDragging;

    private bool _txActive;
    private double _txMinX, _txMinY, _txMaxX, _txMaxY;
    private double _txPivotX, _txPivotY;               // draggable, doc space
    private double _txScaleX = 1, _txScaleY = 1, _txAngle, _txDx, _txDy;
    private bool _txPerspective;
    private readonly double[] _txQuad = new double[8]; // dst corners in perspective mode

    private enum TxDrag { None, Move, ScaleCorner, ScaleEdge, Rotate, Pivot, Quad }

    private TxDrag _txDrag;
    private int _txHandle;
    private (double X, double Y) _txDragStart;
    private (double ScaleX, double ScaleY, double Angle, double Dx, double Dy) _txStart;

    /// <summary>Right-click during a transform: show the options menu at this view position.</summary>
    public event Action<Point>? TransformMenuRequested;

    /// <summary>The gizmo changed (for live numeric readouts).</summary>
    public event Action? TransformGizmoChanged;

    public bool TransformSessionActive => _txActive;

    /// <summary>Four-corner free drag instead of the affine box.</summary>
    public bool TransformPerspective
    {
        get => _txPerspective;
        set
        {
            if (_txPerspective == value) return;
            if (value) SeedQuadFromCorners();
            _txPerspective = value;
            InvalidateVisual();
        }
    }

    public void BeginTransformGizmo(double minX, double minY, double maxX, double maxY)
    {
        _txActive = true;
        _txMinX = minX; _txMinY = minY; _txMaxX = maxX; _txMaxY = maxY;
        _txPivotX = (minX + maxX) / 2;
        _txPivotY = (minY + maxY) / 2;
        _txScaleX = 1; _txScaleY = 1; _txAngle = 0; _txDx = 0; _txDy = 0;
        _txPerspective = false;
        _txDrag = TxDrag.None;
        SeedQuadFromCorners();
        InvalidateVisual();
    }

    public void EndTransformGizmo()
    {
        _txActive = false;
        _txDrag = TxDrag.None;
        InvalidateVisual();
    }

    /// <summary>Flip in place around the (draggable) pivot — no translation.</summary>
    public void MirrorTransformGizmo(bool horizontal)
    {
        if (!_txActive) return;
        if (_txPerspective)
        {
            for (var i = 0; i < 4; i++)
            {
                if (horizontal) _txQuad[i * 2] = 2 * _txPivotX - _txQuad[i * 2];
                else _txQuad[i * 2 + 1] = 2 * _txPivotY - _txQuad[i * 2 + 1];
            }
        }
        else if (horizontal)
        {
            _txScaleX = -_txScaleX;
        }
        else
        {
            _txScaleY = -_txScaleY;
        }
        TransformGizmoChanged?.Invoke();
        InvalidateVisual();
    }

    /// <summary>Back to identity (bounds, pivot and mode stay).</summary>
    public void ResetTransformGizmo()
    {
        _txScaleX = 1; _txScaleY = 1; _txAngle = 0; _txDx = 0; _txDy = 0;
        SeedQuadFromCorners();
        TransformGizmoChanged?.Invoke();
        InvalidateVisual();
    }

    public bool TransformIsPerspectiveResult => _txPerspective;

    public (double PivotX, double PivotY, double ScaleX, double ScaleY, double Angle, double Dx, double Dy) TransformAffineResult =>
        (_txPivotX, _txPivotY, _txScaleX, _txScaleY, _txAngle, _txDx, _txDy);

    public (double[] Src, double[] Dst) TransformQuadResult =>
        ([_txMinX, _txMinY, _txMaxX, _txMinY, _txMaxX, _txMaxY, _txMinX, _txMaxY], (double[])_txQuad.Clone());

    /// <summary>
    /// The gizmo's current shape as a document-space matrix, for the live
    /// preview. Identity while the gizmo is untouched.
    /// </summary>
    /// <remarks>
    /// The same composition <c>MainViewModel.CommitTransformAffine</c> builds
    /// for the baseline resample, and the same homography the perspective
    /// commit solves — so what the preview shows and what apply produces come
    /// from one definition of the transform, not two that can drift.
    /// </remarks>
    public SKMatrix TransformMatrix
    {
        get
        {
            if (!_txActive) return SKMatrix.Identity;
            if (_txPerspective)
            {
                var (src, dst) = TransformQuadResult;
                double[] h;
                try
                {
                    h = Lightbox.Core.Geometry.TransformOps.PerspectiveCoefficients(src, dst);
                }
                catch (InvalidOperationException)
                {
                    // A collapsed quad has no preview; apply refuses it too.
                    return SKMatrix.Identity;
                }
                return new SKMatrix(
                    (float)h[0], (float)h[1], (float)h[2],
                    (float)h[3], (float)h[4], (float)h[5],
                    (float)h[6], (float)h[7], 1f);
            }
            var m = SKMatrix.CreateTranslation((float)-_txPivotX, (float)-_txPivotY);
            m = m.PostConcat(SKMatrix.CreateScale((float)_txScaleX, (float)_txScaleY));
            m = m.PostConcat(SKMatrix.CreateRotation((float)_txAngle));
            m = m.PostConcat(SKMatrix.CreateTranslation(
                (float)(_txPivotX + _txDx), (float)(_txPivotY + _txDy)));
            return m;
        }
    }

    /// <summary>True when the gizmo is still identity (nothing to commit).</summary>
    public bool TransformIsIdentity =>
        !_txPerspective
        && Math.Abs(_txScaleX - 1) < 1e-9 && Math.Abs(_txScaleY - 1) < 1e-9
        && Math.Abs(_txAngle) < 1e-9 && Math.Abs(_txDx) < 1e-9 && Math.Abs(_txDy) < 1e-9;

    private (double X, double Y) TxMap(double x, double y)
    {
        var cos = Math.Cos(_txAngle);
        var sin = Math.Sin(_txAngle);
        var dx = (x - _txPivotX) * _txScaleX;
        var dy = (y - _txPivotY) * _txScaleY;
        return (_txPivotX + dx * cos - dy * sin + _txDx,
                _txPivotY + dx * sin + dy * cos + _txDy);
    }

    /// <summary>Current corner positions (affine-mapped bounds, or the free quad).</summary>
    private (double X, double Y)[] TxCorners()
    {
        if (_txPerspective)
        {
            return
            [
                (_txQuad[0], _txQuad[1]), (_txQuad[2], _txQuad[3]),
                (_txQuad[4], _txQuad[5]), (_txQuad[6], _txQuad[7]),
            ];
        }
        return
        [
            TxMap(_txMinX, _txMinY), TxMap(_txMaxX, _txMinY),
            TxMap(_txMaxX, _txMaxY), TxMap(_txMinX, _txMaxY),
        ];
    }

    private void SeedQuadFromCorners()
    {
        var corners = TxCorners();
        for (var i = 0; i < 4; i++)
        {
            _txQuad[i * 2] = corners[i].X;
            _txQuad[i * 2 + 1] = corners[i].Y;
        }
    }

    /// <summary>Where the pivot sits after the current transform (it rides the offset).</summary>
    private (double X, double Y) TxPivotNow() => (_txPivotX + _txDx, _txPivotY + _txDy);

    private static double TxDist(double x0, double y0, double x1, double y1)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool TxPointInQuad(double x, double y, (double X, double Y)[] q)
    {
        // Winding test — works for the rotated box and any convex-ish quad.
        var inside = false;
        for (int i = 0, j = 3; i < 4; j = i++)
        {
            if (q[i].Y > y != q[j].Y > y
                && x < (q[j].X - q[i].X) * (y - q[i].Y) / (q[j].Y - q[i].Y) + q[i].X)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    /// <summary>What a press at a document point grabs, Krita-style: pivot,
    /// corner/edge handles, inside = move, outside = rotate.</summary>
    private (TxDrag Kind, int Handle) TxHitTest(double x, double y)
    {
        var tol = 10.0 / Math.Max(0.01, FitScale() * _zoom);
        var pivot = TxPivotNow();
        if (TxDist(x, y, pivot.X, pivot.Y) <= tol * 1.2) return (TxDrag.Pivot, 0);

        var corners = TxCorners();
        for (var i = 0; i < 4; i++)
        {
            if (TxDist(x, y, corners[i].X, corners[i].Y) <= tol)
            {
                return (_txPerspective ? TxDrag.Quad : TxDrag.ScaleCorner, i);
            }
        }
        if (!_txPerspective)
        {
            for (var i = 0; i < 4; i++)
            {
                var a = corners[i];
                var b = corners[(i + 1) % 4];
                if (TxDist(x, y, (a.X + b.X) / 2, (a.Y + b.Y) / 2) <= tol)
                {
                    return (TxDrag.ScaleEdge, i);
                }
            }
        }
        return TxPointInQuad(x, y, corners) ? (TxDrag.Move, 0) : (TxDrag.Rotate, 0);
    }

    /// <summary>Cursor position relative to the pivot, un-rotated into the box's local frame.</summary>
    private (double X, double Y) TxLocal(double x, double y)
    {
        var pivot = TxPivotNow();
        var dx = x - pivot.X;
        var dy = y - pivot.Y;
        var cos = Math.Cos(-_txAngle);
        var sin = Math.Sin(-_txAngle);
        return (dx * cos - dy * sin, dx * sin + dy * cos);
    }

    private void TxDragTo(double x, double y, bool uniform)
    {
        switch (_txDrag)
        {
            case TxDrag.Move:
                if (_txPerspective)
                {
                    // Quad mode: shift every corner from its press-time position.
                    var ddx = x - _txDragStart.X;
                    var ddy = y - _txDragStart.Y;
                    for (var i = 0; i < 4; i++)
                    {
                        _txQuad[i * 2] = _txStartQuad[i * 2] + ddx;
                        _txQuad[i * 2 + 1] = _txStartQuad[i * 2 + 1] + ddy;
                    }
                }
                else
                {
                    _txDx = _txStart.Dx + (x - _txDragStart.X);
                    _txDy = _txStart.Dy + (y - _txDragStart.Y);
                }
                break;
            case TxDrag.Pivot:
                if (_txPerspective)
                {
                    _txPivotX = x;
                    _txPivotY = y;
                }
                else
                {
                    // Re-anchor without letting the drawing jump: the new
                    // source pivot is the drop point pulled back through the
                    // current map; the offset re-derives so A(p) is unchanged.
                    var local = TxLocal(x, y);
                    var sx = Math.Abs(_txScaleX) < 1e-6 ? 1e-6 * Math.Sign(_txScaleX == 0 ? 1 : _txScaleX) : _txScaleX;
                    var sy = Math.Abs(_txScaleY) < 1e-6 ? 1e-6 * Math.Sign(_txScaleY == 0 ? 1 : _txScaleY) : _txScaleY;
                    var srcX = _txPivotX + local.X / sx;
                    var srcY = _txPivotY + local.Y / sy;
                    _txPivotX = srcX;
                    _txPivotY = srcY;
                    _txDx = x - srcX;
                    _txDy = y - srcY;
                }
                break;
            case TxDrag.ScaleCorner:
            case TxDrag.ScaleEdge:
            {
                var q0 = TxLocalAtStart();
                var q = TxLocal(x, y);
                if (_txDrag == TxDrag.ScaleCorner && uniform)
                {
                    var d0 = Math.Max(1e-6, Math.Sqrt(q0.X * q0.X + q0.Y * q0.Y));
                    var f = Math.Sqrt(q.X * q.X + q.Y * q.Y) / d0;
                    _txScaleX = _txStart.ScaleX * f;
                    _txScaleY = _txStart.ScaleY * f;
                }
                else
                {
                    var scaleX = Math.Abs(q0.X) < 1e-6 ? _txStart.ScaleX : _txStart.ScaleX * (q.X / q0.X);
                    var scaleY = Math.Abs(q0.Y) < 1e-6 ? _txStart.ScaleY : _txStart.ScaleY * (q.Y / q0.Y);
                    if (_txDrag == TxDrag.ScaleCorner)
                    {
                        _txScaleX = scaleX;
                        _txScaleY = scaleY;
                    }
                    else if (_txHandle % 2 == 0)
                    {
                        _txScaleY = scaleY; // top/bottom edges scale vertically
                    }
                    else
                    {
                        _txScaleX = scaleX; // left/right edges scale horizontally
                    }
                }
                break;
            }
            case TxDrag.Rotate:
            {
                var pivot = TxPivotNow();
                var a0 = Math.Atan2(_txDragStart.Y - pivot.Y, _txDragStart.X - pivot.X);
                var a1 = Math.Atan2(y - pivot.Y, x - pivot.X);
                _txAngle = _txStart.Angle + (a1 - a0);
                break;
            }
            case TxDrag.Quad:
                _txQuad[_txHandle * 2] = x;
                _txQuad[_txHandle * 2 + 1] = y;
                break;
        }
        TransformGizmoChanged?.Invoke();
        InvalidateVisual();
    }

    private readonly double[] _txStartQuad = new double[8];

    private (double X, double Y) TxLocalAtStart()
    {
        // Local coordinates of the press point under the transform state
        // captured at press time (scale hasn't been applied to it yet).
        var pivot = (_txPivotX + _txStart.Dx, _txPivotY + _txStart.Dy);
        var dx = _txDragStart.X - pivot.Item1;
        var dy = _txDragStart.Y - pivot.Item2;
        var cos = Math.Cos(-_txStart.Angle);
        var sin = Math.Sin(-_txStart.Angle);
        return (dx * cos - dy * sin, dx * sin + dy * cos);
    }

    /// <summary>A closed freehand/rect/ellipse selection shape (doc space; Shift=add, Alt=subtract).</summary>
    public event Action<List<Core.Documents.StrokePoint>, bool, bool>? SelectionShapeDrawn;

    public event Action<double, double>? PolygonVertexAdded;

    public event Action<bool, bool>? PolygonCompleted;

    // Selection overlay state (marching ants) — pushed by the window.
    private IReadOnlyList<List<Core.Documents.StrokePoint>> _selectionContours = [];
    private IReadOnlyList<Core.Documents.StrokePoint> _polygonInProgress = [];
    private float _antsPhase;
    private bool _antsAnimating;

    public void SetSelectionOverlay(
        IReadOnlyList<List<Core.Documents.StrokePoint>> contours,
        IReadOnlyList<Core.Documents.StrokePoint> polygonInProgress)
    {
        _selectionContours = contours;
        _polygonInProgress = polygonInProgress;
        InvalidateVisual();
        StartAntsIfNeeded();
    }

    private void StartAntsIfNeeded()
    {
        if (_antsAnimating || (_selectionContours.Count == 0 && _polygonInProgress.Count == 0)) return;
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        _antsAnimating = true;
        top.RequestAnimationFrame(OnAntsFrame);
    }

    private void OnAntsFrame(TimeSpan _)
    {
        _antsAnimating = false;
        if (_selectionContours.Count == 0 && _polygonInProgress.Count == 0) return;
        _antsPhase = (_antsPhase + 0.35f) % 8f;
        InvalidateVisual();
        StartAntsIfNeeded();
    }

    // in-progress drag shape (doc space)
    private readonly List<Core.Documents.StrokePoint> _dragShape = [];
    private (double X, double Y)? _dragAnchor;

    /// <summary>
    /// Whether Shift was already down when a marquee started.
    /// </summary>
    /// <remarks>
    /// Photoshop's disambiguation, and it is the only one that works: Shift
    /// means <i>add to the selection</i> when it is held before the press, and
    /// <i>make it square</i> when it comes down during the drag. Both are real
    /// uses of the same key on the same tool, and the moment it is pressed is
    /// the only thing that tells them apart. Recorded at press because by the
    /// release the two are indistinguishable again.
    /// </remarks>
    private bool _marqueeAdds;

    private bool _painting;

    /// <summary>
    /// Which pointer owns the stroke in flight. Refocusing the window with a
    /// pen makes Windows synthesise mouse activity at the mouse's stale
    /// position, and those events arrive interleaved with the pen's — without
    /// this, each one painted a full-pressure dab wherever the mouse was last
    /// left, which read as lines shooting across the canvas (B185).
    /// </summary>
    private int _paintPointerId = -1;

    private bool _panning;
    private Point _panLast;

    /// <summary>
    /// Take a published frame. Returns false when the frame was dropped
    /// because the compositor is behind — see the back-pressure below.
    /// </summary>
    public bool UpdateSnapshot(RenderSnapshot snapshot)
    {
        if (Environment.GetEnvironmentVariable("LIGHTBOX_TRACE") is not null)
            Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss.fff} UpdateSnapshot");
        var rendered = Interlocked.Read(ref _lastRenderedSeq);

        // Back-pressure. If the queue is full of frames the compositor has not
        // finished with, the publisher is ahead of the renderer and there is
        // nothing safe to free. Drop the INCOMING frame instead — it has never
        // been handed to the render thread, so disposing it cannot race, and
        // the canvas simply keeps showing the frame it already has until the
        // next publish. Freeing an old one here instead is what crashed:
        // the compositor was mid-draw on it.
        if (_retired.Count > RetiredHardCap && _retired.Peek() is { } head && head.Seq >= rendered)
        {
            // B122: this frame's change never reaches the durable presentation
            // surface, so it has to be owed or those pixels stay stale.
            _presented.Skipped(snapshot.ChangedInImage);
            snapshot.Dispose();
            return false;
        }

        var old = _snapshot;

        // B122, the second way a change goes missing, and the subtler one. A
        // snapshot replaced before it was ever rendered was never patched in
        // either — and the incoming snapshot's own region cannot be relied on to
        // cover it, because ComposeRing's clip describes what THAT buffer owed
        // rather than what the presentation surface is missing.
        if (old is not null && old.Seq > rendered) _presented.Skipped(old.ChangedInImage);

        // A change in which document rectangle the image covers re-bases every
        // pixel, so a patch computed against the old framing would be nonsense.
        // Cheap insurance in the one place where being wrong shows stale art.
        if (old is not null && old.DocViewport != snapshot.DocViewport) _presented.ForceFull();

        _snapshot = snapshot;
        if (old is null) ReportDisplayScale(); // first frame: the scale is now knowable
        if (old is not null) _retired.Enqueue(old);
        // Free images the render thread is provably done with: renders are
        // sequential, so once a newer snapshot has been drawn, no earlier one
        // can still be in flight. Holding them longer is not just memory —
        // the compositor's back-buffer would have to copy-on-write around
        // them on every publish (~375 ms at 4K).
        while (_retired.Count > RetiredKeep
               && _retired.Peek() is { } stale
               && stale.Seq < rendered)
        {
            _retired.Dequeue().Dispose();
        }
        // Hard cap, but never at the cost of freeing something in flight: an
        // image the render thread has not finished with must survive however
        // long the queue gets. Anything at or above `rendered` may still be
        // on the compositor's canvas right now.
        while (_retired.Count > RetiredHardCap
               && _retired.Peek() is { } spare
               && spare.Seq < rendered)
        {
            _retired.Dequeue().Dispose();
        }
        InvalidateVisual();
        // InvalidateVisual alone is not enough: when input goes quiet right
        // after a publish (mouse released and held still), the dispatcher may
        // never wake to paint it — the stroke only appeared on the NEXT event.
        // An animation-frame request forces a compositor frame regardless.
        _presentWait.Published(snapshot.Seq);
        StrokeToScreen.Shared.Published(snapshot.Seq);

        if (_keepPresenting) PumpPresentLoop();
        else RequestOneFrame();
        return true;
    }

    private void RequestOneFrame()
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        _frameRequests++;
        top.RequestAnimationFrame(_ =>
        {
            _frameCallbacks++;
            InvalidateVisual();
        });
    }

    /// <summary>
    /// While true, keep a compositor frame permanently on request — the state
    /// playback needs and could not have.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B164, and the reported symptom is a direct consequence: playback is
    /// smooth only while the pointer moves over the canvas.</b> A frame was asked
    /// for once per publish, so during playback the compositor ran <em>at the
    /// scene's frame rate and in lock-step with it</em> — twelve times a second,
    /// each tick triggered by the publish it was meant to show. Nothing was
    /// running between them, so every wobble in when a publish landed went
    /// straight to the screen with nothing to absorb it.
    /// </para>
    /// <para>
    /// <b>Moving the pointer fixed it by accident.</b> Pointer motion invalidates
    /// the canvas at pointer rate, which keeps the compositor ticking at display
    /// rate; a published frame is then picked up within a vsync instead of
    /// waiting for the next publish to drag the compositor awake.
    /// </para>
    /// <para>
    /// <b>The owner's own experiment is what makes this the answer rather than
    /// another theory, because it is the one fact every earlier hypothesis got
    /// wrong.</b> Mashing Ctrl, mashing Space and switching tools quickly all
    /// smooth playback; <em>holding a key down does not</em>. Held keys repeat at
    /// about 30 a second, so events are arriving in quantity — what they do not do
    /// is change anything visible. Everything in the working list repaints some
    /// control; everything in the failing list does not. "Input wakes the loop"
    /// cannot survive that pair, and "the compositor is only ticking when
    /// something invalidates" explains both halves and the docker case with it.
    /// </para>
    /// <para>
    /// So while the transport is running, the loop re-arms itself from inside its
    /// own callback and the compositor ticks at display rate the way it does under
    /// the pointer. <see cref="_presentLoopRunning"/> keeps exactly one request in
    /// flight, so a publish arriving mid-loop joins it rather than doubling it.
    /// When it stops, the last callback simply does not re-arm — there is no timer
    /// to cancel and nothing spins while the artist is drawing.
    /// </para>
    /// </remarks>
    public bool KeepPresenting
    {
        get => _keepPresenting;
        set
        {
            if (_keepPresenting == value) return;
            _keepPresenting = value;
            if (value) PumpPresentLoop();
        }
    }

    private bool _keepPresenting;

    /// <summary>One request in flight at a time. See <see cref="KeepPresenting"/>.</summary>
    private bool _presentLoopRunning;

    private void PumpPresentLoop()
    {
        if (_presentLoopRunning || !_keepPresenting) return;
        if (TopLevel.GetTopLevel(this) is not { } top) return;

        _presentLoopRunning = true;
        _frameRequests++;
        top.RequestAnimationFrame(_ =>
        {
            _frameCallbacks++;
            _presentLoopRunning = false;
            InvalidateVisual();
            // Re-arm from inside the callback: that is what makes it a loop
            // rather than a single wake, and it stops the moment playback does.
            PumpPresentLoop();
        });
    }

    /// <summary>
    /// Animation frames asked for, and callbacks that came back (B153).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>These two numbers decide between the only two explanations left</b> for
    /// playback riding on pointer movement. If they track each other, the
    /// compositor is ticking and the wake is arriving, so the fault is upstream.
    /// If requests climb and callbacks do not, the compositor is genuinely
    /// asleep when input is quiet, and no amount of asking politely will wake it
    /// — which is a different fix and worth knowing before building one.
    /// </para>
    /// <para>
    /// <b>The latch this replaced was a real defect on its own.</b> A
    /// <c>_framePending</c> flag was set before the request and cleared only
    /// inside the callback, so a <em>single</em> callback that never arrived
    /// left it true for the rest of the session and every later publish skipped
    /// asking. One dropped frame disabled the mitigation permanently, and moving
    /// the pointer hid it by waking the loop another way — which is exactly the
    /// symptom reported. Requesting unconditionally costs one one-shot callback
    /// per publish, a dozen or two a second, and cannot latch.
    /// </para>
    /// </remarks>
    internal (long Requested, long Delivered) AnimationFrames => (_frameRequests, _frameCallbacks);

    internal void ResetAnimationFrameCounters()
    {
        _frameRequests = 0;
        _frameCallbacks = 0;
    }

    private long _frameRequests;
    private long _frameCallbacks;

    public override void Render(DrawingContext context)
    {
        if (Environment.GetEnvironmentVariable("LIGHTBOX_TRACE") is not null)
            Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Render (snapshot={_snapshot is not null})");
        var snapshot = _snapshot;
        if (snapshot is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        // Both pointer gizmos, and which one is shown, live in CanvasControl.Pointer.cs.
        BrushCursor? cursor = null;
        if (_hoverPoint is { } p && PointerIntent != CanvasCursorKind.Pick)
        {
            var radius = (float)Math.Max(1.0, BrushCursorSize / 2 * FitScale() * _zoom);
            cursor = new BrushCursor(
                (float)p.X, (float)p.Y, radius,
                (float)Math.Clamp(BrushCursorRoundness, 0.05, 1.0),
                (float)BrushCursorAngle,
                TipOutlinePath(BrushCursorTipId));
        }
        var pickRing = PickRingNow();

        var view = new ViewState(
            snapshot.DocWidth,
            snapshot.DocHeight,
            (float)(FitScale() * _zoom),
            (float)_rotationDeg,
            _mirrored,
            (float)(Bounds.Width / 2 + _pan.X),
            (float)(Bounds.Height / 2 + _pan.Y));

        // Selection overlay paths (doc space; the op transforms them with the view).
        SKPath? ants = null;
        if (_selectionContours.Count > 0 || _dragShape.Count >= 3)
        {
            var contours = new List<IReadOnlyList<Core.Documents.StrokePoint>>(_selectionContours);
            if (_dragShape.Count >= 3) contours.Add(_dragShape.ToList());
            ants = BrushEngine.PathFromContours(contours);
        }
        SKPath? openPath = null;
        if (_polygonInProgress.Count >= 2)
        {
            openPath = new SKPath();
            openPath.MoveTo((float)_polygonInProgress[0].X, (float)_polygonInProgress[0].Y);
            for (var i = 1; i < _polygonInProgress.Count; i++)
            {
                openPath.LineTo((float)_polygonInProgress[i].X, (float)_polygonInProgress[i].Y);
            }
        }

        LazyGizmo? lazy = null;
        if (LazyRadius > 0 && _hoverPoint is { } lp)
        {
            var (cx, cy) = ViewToDoc(lp);
            var (ax, ay) = _lazyAnchor ?? (cx, cy);
            lazy = new LazyGizmo((float)ax, (float)ay, (float)cx, (float)cy,
                (float)LazyRadius, (float)Math.Max(0.75, BrushCursorSize / 2));
        }

        TxGizmoData? txGizmo = null;
        if (_txActive)
        {
            var c = TxCorners();
            var pivot = TxPivotNow();
            txGizmo = new TxGizmoData(
                new SKPoint((float)c[0].X, (float)c[0].Y),
                new SKPoint((float)c[1].X, (float)c[1].Y),
                new SKPoint((float)c[2].X, (float)c[2].Y),
                new SKPoint((float)c[3].X, (float)c[3].Y),
                new SKPoint((float)pivot.X, (float)pivot.Y),
                _txPerspective);
        }

        // Take the queued context work, if any, and hand it over exactly once.
        var gpuWork = _pendingGpuWork;
        _pendingGpuWork = null;

        context.Custom(new DrawOp(
            new Rect(Bounds.Size), snapshot, view, cursor, ants, openPath, _antsPhase, lazy, txGizmo,
            NoteRendered, ReportFrameTime, CameraFrame, GradientAxisPoints(),
            ReferenceBoxes, _newBox, Guides, _draftGuide, WithRigPreview(RigMarks), _balanceDots,
            _selectionManager, _getPlacementsForSelection, _presented, gpuWork,
            _selectedLines, LineMarqueeRect(), LineDragOffset(), _pathNodes, _penPreview,
            _pathTrace, GpuComposite.ResidencyDisabled ? null : _textures, Solo, pickRing,
            BoneChromes, HeatPoints));
    }

    // The tip outline cache and TipOutlinePath moved to CanvasControl.Pointer.cs,
    // where the rest of what the pointer draws for itself now lives.

    // ---- view <-> document transform ---------------------------------------

    private double FitScale()
    {
        var snapshot = _snapshot;
        if (snapshot is null || Bounds.Width <= 0 || Bounds.Height <= 0) return 1;
        return Math.Min(Bounds.Width / snapshot.DocWidth, Bounds.Height / snapshot.DocHeight);
    }

    /// <summary>Document → view matrix: center, mirror, scale, rotate, place.</summary>
    /// <remarks>
    /// <para>
    /// <b>This matrix may never read <see cref="RenderSnapshot.DocViewport"/>,
    /// and the reason is a cycle rather than a preference.</b>
    /// <see cref="ReportViewport"/> computes the visible rectangle by inverting
    /// this matrix, and the view model hands that rectangle back on the next
    /// publish. So a viewport term here is fed by its own output: every publish
    /// shifts the matrix that produced the viewport, which shifts the next
    /// viewport, and the point under the pointer walks away from the mark.
    /// </para>
    /// <para>
    /// It was added twice for the tiled canvas work and measured wrong both
    /// times. <c>CursorAlignmentTests</c> holds the numbers: a document-space
    /// offset added to this screen-space translation put the cursor
    /// <b>108 document units</b> from its mark at 100%, <b>1152</b> at 50%, and
    /// walked the anchor <b>217 units</b> over ten zoom round trips. The whole
    /// suite stayed green because every earlier view test published
    /// <c>DocViewport = null</c>.
    /// </para>
    /// <para>
    /// Culling is a <em>rendering</em> decision and belongs on the rendering
    /// side of the line: the published image knows which document rectangle it
    /// covers, and <see cref="GuidePainter.PaintDocument"/> draws it into that
    /// rectangle. The pointer mapping stays a pure function of the document, so
    /// it cannot depend on how the compositor chose to do its work — which is
    /// invariant 5 (<i>the view transform is view-only</i>) read from the other
    /// direction.
    /// </para>
    /// </remarks>
    private Matrix ViewMatrix()
    {
        var snapshot = _snapshot;
        if (snapshot is null) return Matrix.Identity;
        var s = FitScale() * _zoom;

        // Document size, zoom, pan, rotation, mirror — and NOTHING derived from
        // the snapshot's viewport. See the remarks: putting the viewport in here
        // is a feedback loop, because the viewport is computed FROM this matrix.
        return Matrix.CreateTranslation(-snapshot.DocWidth / 2.0, -snapshot.DocHeight / 2.0)
               * Matrix.CreateScale(_mirrored ? -s : s, s)
               * Matrix.CreateRotation(_rotationDeg * Math.PI / 180)
               * Matrix.CreateTranslation(Bounds.Width / 2 + _pan.X, Bounds.Height / 2 + _pan.Y);
    }

    /// <summary>
    /// Where the paper's top-left sits in stroke coordinates — <c>Scene.Left</c>
    /// and <c>Scene.Top</c>, non-zero only once the canvas has been grown or
    /// cropped on that side.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The view matrix works in surface pixels and always will.</b> It is
    /// built from <c>DocWidth</c>/<c>DocHeight</c>, which are the size of the
    /// composited bitmap — that bitmap starts at its own (0,0) whatever the
    /// document rectangle is called. So the origin does not belong in the
    /// matrix; it belongs in the two conversions either side of it, which is
    /// where it is.
    /// </para>
    /// <para>
    /// Putting it here rather than on <c>RenderSnapshot</c> keeps it out of the
    /// render path entirely, which is right: nothing about <em>drawing</em> the
    /// composited bitmap changes when the paper is renamed. Only the question
    /// "which stroke coordinate is under the pointer" does.
    /// </para>
    /// </remarks>
    public static readonly StyledProperty<PixelPoint> DocumentOriginProperty =
        AvaloniaProperty.Register<CanvasControl, PixelPoint>(nameof(DocumentOrigin));

    public PixelPoint DocumentOrigin
    {
        get => GetValue(DocumentOriginProperty);
        set => SetValue(DocumentOriginProperty, value);
    }

    /// <summary>
    /// Map a view-space point to <b>stroke</b> coordinates (exposed for tests).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Never throws: a degenerate matrix (zero-sized layout) falls back to the
    /// raw point, and non-finite results are pinned to the document's top-left.
    /// </para>
    /// <para>
    /// <b>Stroke coordinates, not surface pixels</b>, and that is what makes the
    /// resize work reach the tools for nothing. Every caller that picks a line,
    /// drags a guide, places a symbol or starts a stroke wants the space the
    /// record is written in — so adding the origin here fixes all of them at
    /// once. The few callers that index a bitmap instead subtract it again, and
    /// they are the exceptions rather than the rule.
    /// </para>
    /// </remarks>
    public (double X, double Y) ViewToDoc(Point p)
    {
        var origin = DocumentOrigin;
        if (!ViewMatrix().TryInvert(out var inverse)) return (p.X + origin.X, p.Y + origin.Y);
        var doc = p.Transform(inverse);
        if (!double.IsFinite(doc.X) || !double.IsFinite(doc.Y)) return (origin.X, origin.Y);
        return (doc.X + origin.X, doc.Y + origin.Y);
    }

    /// <summary>Map a stroke coordinate to view space.</summary>
    public (double X, double Y) DocToView(double x, double y)
    {
        var origin = DocumentOrigin;
        var p = new Point(x - origin.X, y - origin.Y).Transform(ViewMatrix());
        return (p.X, p.Y);
    }

    /// <summary>A stroke coordinate as a pixel in the composited bitmap.</summary>
    /// <remarks>
    /// For the handful of callers that index pixels rather than reasoning about
    /// the record: flood fill, the wand, and the colour picker. Everything else
    /// should stay in stroke coordinates and never call this.
    /// </remarks>
    public (double X, double Y) DocToSurface(double x, double y) =>
        (x - DocumentOrigin.X, y - DocumentOrigin.Y);

    /// <summary>Find the placement at document coordinates, or null if none hit.</summary>
    private Core.Documents.SymbolPlacement? PickPlacementAt(double x, double y)
    {
        if (_getPlacementsForSelection is null) return null;
        var placements = _getPlacementsForSelection();
        if (placements is null || placements.Count == 0) return null;

        const double hitRadius = 20;  // Document units; same as half the selection box
        foreach (var placement in placements)
        {
            var dx = x - placement.X;
            var dy = y - placement.Y;
            var distSq = dx * dx + dy * dy;
            if (distSq <= hitRadius * hitRadius)
                return placement;
        }
        return null;
    }

    private int PickGuideAt(double x, double y)
    {
        if (_guides is null || _guides.Count == 0) return -1;

        const double hitRadius = 5;  // Document units for click tolerance on a line
        foreach (var (index, guide) in _guides.Select((g, i) => (i, g)))
        {
            // For a line guide, calculate perpendicular distance from point to line
            if (guide.Angles.Count > 0)
            {
                var angle = guide.Angles[0];  // Use the primary angle for distance calc
                var radians = angle * Math.PI / 180;
                var cos = Math.Cos(radians);
                var sin = Math.Sin(radians);

                // Distance from point (x,y) to line through (guide.X, guide.Y) at angle
                var dx = x - guide.X;
                var dy = y - guide.Y;
                var perpDist = Math.Abs(dx * (-sin) + dy * cos);

                if (perpDist <= hitRadius)
                    return index;
            }
            else
            {
                // Vanishing point: simple distance to point
                var dx = x - guide.X;
                var dy = y - guide.Y;
                var distSq = dx * dx + dy * dy;
                if (distSq <= hitRadius * hitRadius)
                    return index;
            }
        }
        return -1;
    }

    private int PickRefBoxAt(double x, double y)
    {
        if (_referenceBoxes is null || _referenceBoxes.Count == 0) return -1;

        foreach (var (index, box) in _referenceBoxes.Select((b, i) => (i, b)))
        {
            if (x >= box.X && x <= box.X + box.W &&
                y >= box.Y && y <= box.Y + box.H)
                return index;
        }
        return -1;
    }

    private string? PickAnchorAt(double x, double y)
    {
        if (_rigMarks is null || _rigMarks.Count == 0) return null;

        const double hitRadius = 10;  // Document units for click tolerance on anchor point
        foreach (var mark in _rigMarks)
        {
            if (mark.Kind != RigMarkKind.Anchor) continue;
            var dx = x - mark.X;
            var dy = y - mark.Y;
            var distSq = dx * dx + dy * dy;
            if (distSq <= hitRadius * hitRadius)
                return mark.Id;
        }
        return null;
    }

    private string? PickShapeAt(double x, double y)
    {
        if (_rigMarks is null || _rigMarks.Count == 0) return null;

        foreach (var mark in _rigMarks)
        {
            if (mark.Kind != RigMarkKind.Shape) continue;
            if (x >= mark.X && x <= mark.X + mark.W &&
                y >= mark.Y && y <= mark.Y + mark.H)
                return mark.Id;
        }
        return null;
    }

    /// <summary>
    /// Where document zero lands along one screen axis, and how many screen
    /// pixels a document pixel covers along it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What a ruler needs, and all it needs: two numbers instead of a matrix,
    /// so the strip has no idea a renderer exists.
    /// </para>
    /// <para>
    /// A mirrored view gives a negative scale, which is right — the ruler
    /// then reads the way the drawing does. A <i>rotated</i> view does not
    /// decompose into two axes at all, and the projection here shrinks
    /// towards zero as the canvas turns; the strip stops drawing rather than
    /// print numbers that mean nothing. Rulers are for square work, and
    /// rotating the view is for drawing an arc comfortably; the two are not
    /// used at the same time.
    /// </para>
    /// </remarks>
    public (double Origin, double Scale) AxisMapping(bool horizontal)
    {
        var matrix = ViewMatrix();
        var zero = new Point(0, 0).Transform(matrix);
        var unit = (horizontal ? new Point(1, 0) : new Point(0, 1)).Transform(matrix);
        var origin = horizontal ? zero.X : zero.Y;
        return (origin, (horizontal ? unit.X : unit.Y) - origin);
    }

    /// <summary>
    /// The pen has produced a real pressure value at least once. Zero from a
    /// pen means two different things and this is what tells them apart: a pen
    /// whose driver never exposes pressure reads zero forever and should paint
    /// at 100% like a mouse, while a pen that normally reports pressure reads
    /// zero only in a gap in the stream — the first packets after the window
    /// is refocused — and promoting those to 100% stamps full-size dabs the
    /// artist never asked for (B185).
    /// </summary>
    /// <remarks>
    /// Cleared again by a whole pen stroke that never reports pressure — see
    /// the release path. Without that, one session with a pressure-reporting
    /// pen would pin a second, pressure-less pen near zero for every stroke
    /// it makes; with it, such a pen pays one faint stroke and is back to
    /// painting at 100% from its next.
    /// </remarks>
    private bool _penHasReportedPressure;

    /// <summary>The stroke in flight has seen a real (non-zero) pressure value.</summary>
    private bool _strokeSawRealPressure;

    /// <summary>The stroke in flight was begun by a pen rather than a mouse.</summary>
    private bool _strokeWasPen;

    private double PressureOf(PointerPoint pp)
    {
        // A mouse has no pressure axis: it paints at 100% so the stroke
        // matches the cursor gizmo exactly. Only a real pen (which reports
        // meaningful values via Windows Ink) modulates pressure.
        if (pp.Pointer.Type != PointerType.Pen) return 1.0;
        var raw = pp.Properties.Pressure;
        if (raw > 0)
        {
            _penHasReportedPressure = true;
            _strokeSawRealPressure = true;
            return Math.Clamp(raw, 0.0, 1.0);
        }
        return _penHasReportedPressure ? 0.0 : 1.0;
    }

    /// <summary>
    /// Called as a stroke ends: a pen stroke that produced no real pressure
    /// from press to release is a pen that is not reporting it, so the
    /// promotion of zero to 100% comes back for the next stroke.
    /// </summary>
    private void NoteStrokePressureHistory()
    {
        if (_strokeWasPen && !_strokeSawRealPressure) _penHasReportedPressure = false;
    }

    /// <summary>
    /// What the OS is actually delivering, for the Pen-pressure settings
    /// page: lets a tablet user see instantly whether real pressure arrives
    /// (it only does when the driver exposes the pen to Windows Ink).
    /// </summary>
    public event Action<string>? InputDiagnostic;

    private string? _lastDiagnostic;

    private void ReportInputDiagnostic(PointerType type, float rawPressure)
    {
        if (InputDiagnostic is null) return;
        var text = type == PointerType.Pen
            ? $"Pen detected — pressure {rawPressure:0.00}"
            : $"{type} input — no pressure axis (paints at 100%)";
        if (text == _lastDiagnostic) return;
        _lastDiagnostic = text;
        InputDiagnostic.Invoke(text);
    }

    // ---- view tools -----------------------------------------------------------

    private void ViewUpdated()
    {
        ViewChanged?.Invoke();
        ReportDisplayScale();
        ReportViewport();
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ReportDisplayScale();
        ReportViewport();
    }

    /// <summary>
    /// Compute and report the visible document rectangle (B82).
    /// Transforms the canvas bounds from screen space to document space,
    /// accounting for zoom, pan, rotation, and fit scale.
    /// </summary>
    private void ReportViewport()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0 || ViewportChanged is null) return;

        // Transform the four corners of the canvas bounds from view space to document space.
        // The inverse view matrix converts screen pixels to document coordinates.
        var invView = ViewMatrix().Invert();
        var corners = new[]
        {
            new Point(0, 0),
            new Point(Bounds.Width, 0),
            new Point(Bounds.Width, Bounds.Height),
            new Point(0, Bounds.Height),
        };

        // Find bounding box of transformed corners in document space.
        var docCorners = corners.Select(c => c.Transform(invView)).ToList();
        var minX = docCorners.Min(p => p.X);
        var minY = docCorners.Min(p => p.Y);
        var maxX = docCorners.Max(p => p.X);
        var maxY = docCorners.Max(p => p.Y);

        // Convert to SKRectI (integer rectangle in document space).
        var viewport = new SKRectI(
            (int)Math.Floor(minX),
            (int)Math.Floor(minY),
            (int)Math.Ceiling(maxX),
            (int)Math.Ceiling(maxY));

        ViewportChanged?.Invoke(viewport);
    }

    /// <summary>Zoom by a factor keeping the given view point fixed.</summary>
    public void ZoomAt(Point anchor, double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0) return;
        var (x, y) = ViewToDoc(anchor);
        _zoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        ReanchorAt(anchor, new Point(x, y));
    }

    /// <summary>Rotate by degrees keeping the given view point fixed.</summary>
    public void RotateAt(Point anchor, double degrees)
    {
        var (x, y) = ViewToDoc(anchor);
        _rotationDeg = (_rotationDeg + degrees) % 360;
        ReanchorAt(anchor, new Point(x, y));
    }

    /// <summary>Mirror the view horizontally, keeping the view center fixed.</summary>
    public void ToggleMirror()
    {
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var (x, y) = ViewToDoc(center);
        _mirrored = !_mirrored;
        ReanchorAt(center, new Point(x, y));
    }

    public void ZoomIn() => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), 1.25);

    public void ZoomOut() => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), 1 / 1.25);

    public void RotateBy(double degrees) => RotateAt(new Point(Bounds.Width / 2, Bounds.Height / 2), degrees);

    public void ResetView()
    {
        _zoom = 1;
        _rotationDeg = 0;
        _mirrored = false;
        _pan = default;
        ViewUpdated();
    }

    /// <summary>Adjust pan so <paramref name="docPoint"/> maps back onto <paramref name="viewPoint"/>.</summary>
    private void ReanchorAt(Point viewPoint, Point docPoint)
    {
        var mapped = docPoint.Transform(ViewMatrix());
        var delta = viewPoint - mapped;
        if (double.IsFinite(delta.X) && double.IsFinite(delta.Y)) _pan += delta;
        ViewUpdated();
    }

    // ---- pointer input ------------------------------------------------------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        try
        {
            var pp = e.GetCurrentPoint(this);
            var kind = pp.Properties.PointerUpdateKind;

            // Edge-triggered: only THIS press being the middle button starts a
            // pan (a stale "middle is down" flag must never eat left clicks).
            if (kind == PointerUpdateKind.MiddleButtonPressed)
            {
                if (_painting) return;
                _panning = true;
                _panLast = pp.Position;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            if (_panning) return;

            // Right-click during a transform session: the options menu.
            if (_txActive && kind == PointerUpdateKind.RightButtonPressed)
            {
                TransformMenuRequested?.Invoke(pp.Position);
                e.Handled = true;
                return;
            }

            if (kind != PointerUpdateKind.LeftButtonPressed && !pp.Properties.IsLeftButtonPressed) return;

            var (x, y) = ViewToDoc(pp.Position);

            // Aligning the reference comes before every tool: while the mode is
            // on, the canvas is a place to push a photograph around, not a
            // place to draw. Half-drawing during alignment would be a mark you
            // then have to find and undo.
            if (ReferenceAlignMode)
            {
                _aligningReference = true;
                _alignLast = new Point(x, y);
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            // B58. Rig editing comes next, for the reason alignment comes first:
            // while the mode is on the canvas is a place to place sockets and
            // hitboxes, not a place to draw. Half a stroke laid down while
            // reaching for an anchor is a mark to find and undo.
            if (RigEditMode)
            {
                _rigDragId = null;
                _rigDragCorner = RigCorner.None;
                _rigDragStart = (x, y);
                // The window answers with BeginRigDrag if the press hit something.
                RigPressed?.Invoke(x, y, FitScale() * _zoom);
                if (_rigDragId is null) RigEmptyPressed?.Invoke(x, y);
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            // The camera gizmo's handles are deliberately small targets, so a
            // press on one is a decision — everywhere else on the canvas still
            // paints, camera or no camera.
            if (_cameraFrame is { Length: 4 } &&
                CameraHandleAt(x, y, (float)(FitScale() * _zoom)) is var handle and not CameraDrag.None)
            {
                _cameraDrag = handle;
                _cameraLast = new Point(x, y);
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            if (ReferenceBoxes is not null)
            {
                BeginGridGesture(x, y);
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            if (_txActive && ToolMode == CanvasToolMode.Transform)
            {
                (_txDrag, _txHandle) = TxHitTest(x, y);
                _txDragStart = (x, y);
                _txStart = (_txScaleX, _txScaleY, _txAngle, _txDx, _txDy);
                Array.Copy(_txQuad, _txStartQuad, 8);
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            // A guide under the pointer is picked up before any tool sees the
            // press. Only ever true while the rulers are showing, which is
            // what keeps this out of the way of drawing — see GuideDragEnabled.
            if (GuideDragEnabled && GuideAt(pp.Position) is { } grabbed)
            {
                _guideDrag = grabbed.Id;
                _guideResizing = GrabsHeightScaleTop(grabbed, pp.Position);
                _guideDragLast = (x, y);
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            // Ctrl is a held eyedropper while painting or filling: the colour
            // you want is almost always already on the canvas, and reaching
            // for a tool to fetch it breaks the stroke you were about to make.
            if (ToolMode is CanvasToolMode.Paint or CanvasToolMode.Fill
                && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                PickClicked?.Invoke(x, y);
                e.Handled = true;
                return;
            }

            switch (ToolMode)
            {
                case CanvasToolMode.Fill:
                    FillClicked?.Invoke(x, y, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                    e.Handled = true;
                    return;
                case CanvasToolMode.Bone:
                    _boneDragId = null;
                    _boneDragGrab = BoneGrab.None;
                    _boneGestureStart = (x, y);
                    _boneGestureActive = true;
                    // The window answers with BeginBoneDrag if the press hit a bone.
                    BonePressed?.Invoke(x, y, FitScale() * _zoom);
                    e.Pointer.Capture(this);
                    e.Handled = true;
                    return;
                case CanvasToolMode.SelectWand:
                    WandClicked?.Invoke(x, y,
                        e.KeyModifiers.HasFlag(KeyModifiers.Shift),
                        e.KeyModifiers.HasFlag(KeyModifiers.Alt));
                    e.Handled = true;
                    return;
                case CanvasToolMode.Pick:
                    PickClicked?.Invoke(x, y);
                    e.Handled = true;
                    return;
                case CanvasToolMode.SelectPolygon:
                    if (e.ClickCount >= 2)
                    {
                        PolygonCompleted?.Invoke(
                            e.KeyModifiers.HasFlag(KeyModifiers.Shift),
                            e.KeyModifiers.HasFlag(KeyModifiers.Alt));
                    }
                    else
                    {
                        PolygonVertexAdded?.Invoke(x, y);
                    }
                    e.Handled = true;
                    return;
                case CanvasToolMode.SelectFreehand:
                    e.Pointer.Capture(this);
                    _marqueeAdds = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                    _dragShape.Clear();
                    _dragShape.Add(new Core.Documents.StrokePoint(x, y, 1));
                    e.Handled = true;
                    return;
                case CanvasToolMode.SelectRect:
                case CanvasToolMode.SelectEllipse:
                    e.Pointer.Capture(this);
                    _dragAnchor = (x, y);
                    _marqueeAdds = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                    _dragShape.Clear();
                    e.Handled = true;
                    return;
                case CanvasToolMode.Gradient:
                    e.Pointer.Capture(this);
                    _gradientDragging = true;
                    GradientDragStarted?.Invoke(x, y);
                    e.Handled = true;
                    return;
                case CanvasToolMode.Shape:
                    e.Pointer.Capture(this);
                    _shapeDragging = true;
                    ShapeDragStarted?.Invoke(x, y);
                    e.Handled = true;
                    return;
                case CanvasToolMode.Move:
                    // A guide under the pointer was already taken above; this is
                    // the drawing itself, or what is selected.
                    //
                    // Selected placements are deliberately not tested here.
                    // `BeginMove` asks the view model, which is the side that
                    // knows both what is selected and what is under the grab,
                    // and which owns the one path a placement moves along —
                    // deciding it here is what grew the second one (B109).
                    // Placements still win over guides and boxes, because the
                    // fall-through leads straight to that path.
                    e.Pointer.Capture(this);
                    var movingSelection = _selectionManager?.SelectedPlacementIds.Count is null or 0;
                    // The same gate the single-guide grab uses at the top of
                    // this handler: locking guides means "pin them where they
                    // are", and a selection must not be the way round it.
                    if (movingSelection && GuideDragEnabled && _selectionManager?.SelectedGuideIndices.Count > 0)
                    {
                        _movingGuides = true;
                        _guideMoveLast = (x, y);
                        GuidesMovedStarted?.Invoke();
                    }
                    else if (movingSelection && _selectionManager?.SelectedRefBoxIndices.Count > 0)
                    {
                        _movingRefBoxes = true;
                        _refBoxMoveLast = (x, y);
                        RefBoxesMoveStarted?.Invoke();
                    }
                    else if (movingSelection && _selectionManager?.SelectedAnchorIds.Count > 0)
                    {
                        _movingAnchors = true;
                        _anchorMoveLast = (x, y);
                        BeginRigGroupPreview(x, y, shapes: false);
                        AnchorsMoveStarted?.Invoke();
                    }
                    else if (movingSelection && _selectionManager?.SelectedShapeIds.Count > 0)
                    {
                        _movingShapes = true;
                        _shapeMoveLast = (x, y);
                        BeginRigGroupPreview(x, y, shapes: true);
                        ShapesMoveStarted?.Invoke();
                    }
                    else
                    {
                        _movingContent = true;
                        ContentMoveStarted?.Invoke(x, y, e.KeyModifiers.HasFlag(KeyModifiers.Control));
                    }
                    e.Handled = true;
                    return;
                case CanvasToolMode.Width:
                    e.Pointer.Capture(this);
                    _widthGrab = _grabWidth?.Invoke(x, y, DocTolerance(GrabPixels)) ?? -1;
                    e.Handled = true;
                    return;
                case CanvasToolMode.Pen:
                    // Captured whether or not the press took, so a press over a
                    // locked layer cannot leave the pointer half-grabbed.
                    e.Pointer.Capture(this);
                    _penShaping = _penPress?.Invoke(
                        x, y, DocTolerance(GrabPixels),
                        e.KeyModifiers.HasFlag(KeyModifiers.Shift),
                        e.KeyModifiers.HasFlag(KeyModifiers.Alt)) ?? false;
                    e.Handled = true;
                    return;
                case CanvasToolMode.PathEdit:
                    e.Pointer.Capture(this);
                    // B172. Entering isolation and grabbing a part are one
                    // question here — "what does a click with the white arrow
                    // mean" — and the answer lives in the view model, like every
                    // other decision this control delegates. It used to be able
                    // to grab only, so the tool was inert until the *black*
                    // arrow had opened the line first.
                    _pathGrab = _grabPathPart?.Invoke(
                        x, y, DocTolerance(GrabPixels),
                        e.KeyModifiers.HasFlag(KeyModifiers.Shift)) ?? ViewModels.PathHit.Miss;
                    _pathDragLast = _pathGrab.IsHit ? (x, y) : null;
                    e.Handled = true;
                    return;
                case CanvasToolMode.Select:
                    if (_selectionManager is not null)
                    {
                        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

                        // Double-click goes into the line rather than selecting
                        // it again. Illustrator's gesture, and safe for the
                        // reason Q53 gives: reaching into geometry is never
                        // something a single click can do by accident.
                        if (e.ClickCount >= 2 && _enterPathEdit is not null
                            && _enterPathEdit(x, y, DocTolerance(GrabPixels)))
                        {
                            e.Handled = true;
                            return;
                        }

                        // Try placements first
                        if (_getPlacementsForSelection is not null)
                        {
                            var placement = PickPlacementAt(x, y);
                            if (placement is not null)
                            {
                                _selectionManager.SelectPlacementWithModifiers(placement.Id, shift, alt);
                                e.Handled = true;
                                return;
                            }
                        }

                        // Then try guides
                        var guideIndex = PickGuideAt(x, y);
                        if (guideIndex >= 0)
                        {
                            _selectionManager.SelectGuideWithModifiers(guideIndex, shift, alt);
                            e.Handled = true;
                            return;
                        }

                        // Then try reference boxes
                        var boxIndex = PickRefBoxAt(x, y);
                        if (boxIndex >= 0)
                        {
                            _selectionManager.SelectRefBoxWithModifiers(boxIndex, shift, alt);
                            e.Handled = true;
                            return;
                        }

                        // Then try anchors
                        var anchorId = PickAnchorAt(x, y);
                        if (anchorId is not null)
                        {
                            _selectionManager.SelectAnchorWithModifiers(anchorId, shift, alt);
                            e.Handled = true;
                            return;
                        }

                        // Then try collision shapes
                        var shapeId = PickShapeAt(x, y);
                        if (shapeId is not null)
                        {
                            _selectionManager.SelectShapeWithModifiers(shapeId, shift, alt);
                            e.Handled = true;
                            return;
                        }

                        // Artwork last, and the order is the point: placements,
                        // guides, reference boxes, anchors and collision shapes
                        // are all chrome sitting *on top of* the drawing. A
                        // guide crossing a line has to win, or the guide becomes
                        // unclickable wherever an artist has drawn — and the
                        // drawing is the thing that is everywhere.
                        if (_pickLine?.Invoke(x, y, DocTolerance(GrabPixels), shift, alt) == true)
                        {
                            // Something is selected and the pointer is down on
                            // it, so this press may become a move. Capture now:
                            // deciding later would mean missing the moves that
                            // happened while we were making up our mind.
                            e.Pointer.Capture(this);
                            _lineDragFrom = (x, y);
                            _lineDragTo = (x, y);
                        }
                        else
                        {
                            // Nothing under the cursor, so the press is the
                            // start of a marquee rather than a click that
                            // missed. Illustrator's rule, and the reason the
                            // pick is a delegate that answers rather than an
                            // event that does not: the canvas has to know.
                            e.Pointer.Capture(this);
                            _lineMarqueeFrom = (x, y);
                            _lineMarqueeTo = (x, y);
                            _lineMarqueeAdds = shift;
                        }
                    }
                    e.Handled = true;
                    return;
            }

            // A second device pressing mid-stroke is never the artist — it is
            // the synthesized mouse click a window activation delivers while
            // the pen is already down. Restarting the stroke from its position
            // would begin the mark wherever the mouse was last left.
            if (_painting) return;

            e.Pointer.Capture(this);
            _painting = true;
            _paintPointerId = e.Pointer.Id;
            _strokeWasPen = e.Pointer.Type == PointerType.Pen;
            _strokeSawRealPressure = false;
            // Alt turns the brush in your hand into an eraser without
            // swapping tools, so it keeps its size, shape and dynamics. That
            // is different from E, which switches to the dedicated eraser and
            // its own settings.
            _erasingThisStroke = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            // Shift at the press is Photoshop's straight segment: the stroke
            // runs from wherever the last one ended to here, and the drag that
            // follows carries on from that point. It is how a hard edge or a
            // long straight is drawn without a ruler.
            _straightFromLast = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            _paintAnchor = (x, y);
            ReportInputDiagnostic(e.Pointer.Type, pp.Properties.Pressure);
            ReportCursorPressure(PressureOf(pp), penDown: true);
            PaintStarted?.Invoke(x, y, PressureOf(pp), _erasingThisStroke, _straightFromLast);
            e.Handled = true;
        }
        catch (Exception ex)
        {
            _painting = false;
            _paintPointerId = -1;
            _panning = false;
            ReportInputError("press", ex);
        }
    }

    // B126. How many times the pointer has crossed this control's boundary.
    //
    // A diagnostic rather than state: the reported pen flicker is the OS pointer
    // appearing over our ring during hover and never while drawing, and drawing
    // is exactly where Pointer.Capture pins pointer-over. If these counters climb
    // while a pen hovers *without moving*, then Avalonia is churning enter/exit
    // and the flicker is ours to fix; if they stay flat, nothing is churning and
    // Windows is simply painting over us, which is an upstream problem. The
    // question has been argued three times and measured none, so it is measured
    // here — on the only hardware that can answer it.
    private int _enterCount;
    private int _exitCount;

    /// <summary>Boundary crossings so far. Tests and the pen diagnostic.</summary>
    internal (int Entered, int Exited) HoverCrossings => (_enterCount, _exitCount);

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _enterCount++;
        ReportHoverChurn(e);
        _hoverPoint = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _exitCount++;
        ReportHoverChurn(e);
        _hoverPoint = null;
        InvalidateVisual();
    }

    /// <summary>
    /// Put the crossing counts on the status strip, beside the pressure reading.
    /// </summary>
    /// <remarks>
    /// Deliberately not gated behind the diagnostics toggle: somebody chasing a
    /// flicker will not know to turn anything on first, and this costs a string
    /// only when a boundary is actually crossed — which, if the flicker is what I
    /// think it is, is the very thing being counted.
    /// </remarks>
    private void ReportHoverChurn(PointerEventArgs e)
    {
        if (InputDiagnostic is null) return;
        var device = e.Pointer.Type;
        InputDiagnostic.Invoke(
            $"{device} hover — entered {_enterCount}, exited {_exitCount}"
            + (_exitCount > 0 && _enterCount > 1
                ? "  (climbing while still = the cursor is being re-evaluated, B126)"
                : string.Empty));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        try
        {
            _hoverPoint = e.GetPosition(this);
            // Only while nothing is being dragged: mid-gesture the question is
            // already answered, and asking it again would put a bitmap read in
            // the pointer path of the one operation that cannot afford it.
            if (!_painting && !_panning) ReportHover(_hoverPoint.Value, e.KeyModifiers);
            // The brush cursor must follow the pointer no matter what state
            // we're in — repaints coalesce, so this is cheap.
            //
            // It is also, as far as anything can tell, the whole reason playback
            // looks smooth while the pointer moves HERE and nowhere else: this
            // is the only InvalidateVisual in the application that a docker
            // cannot cause. Counted so the report can say whether a frame's wait
            // to be drawn depends on it — see InputPulse.
            InputPulse.OnCanvas();
            InvalidateVisual();

            if (_movingGuides)
            {
                var (mx, my) = ViewToDoc(e.GetPosition(this));
                var dx = mx - _guideMoveLast.X;
                var dy = my - _guideMoveLast.Y;
                GuidesMoved?.Invoke(dx, dy);
                _guideMoveLast = (mx, my);
                return;
            }

            if (_cameraDrag != CameraDrag.None)
            {
                var (cx, cy) = ViewToDoc(e.GetPosition(this));
                if (_cameraFrame is { Length: 4 } frame)
                {
                    var centre = FrameCentre(frame);
                    switch (_cameraDrag)
                    {
                        case CameraDrag.Pan:
                            // Incremental doc-space deltas, like the reference
                            // nudge — the window keys the framing per event.
                            CameraPanned?.Invoke(cx - _cameraLast.X, cy - _cameraLast.Y);
                            break;
                        case CameraDrag.Zoom:
                        {
                            // Corner out grows the frame, which is seeing MORE:
                            // the factor is old-distance over new-distance.
                            var d0 = Math.Sqrt(
                                (_cameraLast.X - centre.X) * (_cameraLast.X - centre.X)
                                + (_cameraLast.Y - centre.Y) * (_cameraLast.Y - centre.Y));
                            var d1 = Math.Sqrt(
                                (cx - centre.X) * (cx - centre.X) + (cy - centre.Y) * (cy - centre.Y));
                            if (d0 > 1e-3 && d1 > 1e-3) CameraZoomedBy?.Invoke(d0 / d1);
                            break;
                        }
                        case CameraDrag.Rotate:
                        {
                            var a0 = Math.Atan2(_cameraLast.Y - centre.Y, _cameraLast.X - centre.X);
                            var a1 = Math.Atan2(cy - centre.Y, cx - centre.X);
                            var delta = (a1 - a0) * 180.0 / Math.PI;
                            if (delta > 180) delta -= 360;
                            if (delta < -180) delta += 360;
                            // The frame sits at MINUS the camera's roll in doc
                            // space, so turning the frame by δ is rolling by −δ.
                            CameraRotatedBy?.Invoke(-delta);
                            break;
                        }
                    }
                }
                _cameraLast = new Point(cx, cy);
                e.Handled = true;
                return;
            }

            if (_movingRefBoxes)
            {
                var (mx, my) = ViewToDoc(e.GetPosition(this));
                var dx = mx - _refBoxMoveLast.X;
                var dy = my - _refBoxMoveLast.Y;
                RefBoxesMoved?.Invoke(dx, dy);
                _refBoxMoveLast = (mx, my);
                e.Handled = true;
                return;
            }

            if (_movingAnchors)
            {
                var (mx, my) = ViewToDoc(e.GetPosition(this));
                var dx = mx - _anchorMoveLast.X;
                var dy = my - _anchorMoveLast.Y;
                AnchorsMoved?.Invoke(dx, dy);
                _anchorMoveLast = (mx, my);
                TrackRigGroupPreview(mx, my, shapes: false);
                e.Handled = true;
                return;
            }

            if (_movingShapes)
            {
                var (mx, my) = ViewToDoc(e.GetPosition(this));
                var dx = mx - _shapeMoveLast.X;
                var dy = my - _shapeMoveLast.Y;
                ShapesMoved?.Invoke(dx, dy);
                _shapeMoveLast = (mx, my);
                TrackRigGroupPreview(mx, my, shapes: true);
                e.Handled = true;
                return;
            }

            // A single mark being dragged. It has never previewed either — B112 is
            // both paths, not just the group one — and it is the same mechanism.
            if (RigEditMode && _rigDragId is { } draggedMark)
            {
                var (mx, my) = ViewToDoc(e.GetPosition(this));
                PreviewRig(
                    [draggedMark], _rigDragCorner,
                    mx - _rigDragStart.X, my - _rigDragStart.Y);
                e.Handled = true;
                return;
            }

            if (_movingContent)
            {
                var (mx, my) = ViewToDoc(e.GetPosition(this));
                // Absolute rather than incremental: the axis lock has to be
                // measured from where the drag started, and a sum of clamped
                // deltas drifts off the axis it is supposed to be holding.
                ContentMoveUpdated?.Invoke(mx, my, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                e.Handled = true;
                return;
            }

            if (_guideDrag is { } dragging)
            {
                var (gx, gy) = ViewToDoc(e.GetPosition(this));
                // Incremental, like the reference nudge: an absolute drag
                // would leave one enormous step in the history.
                if (_guideResizing) GuideResized?.Invoke(dragging, gy - _guideDragLast.Y);
                else GuideMoved?.Invoke(dragging, gx - _guideDragLast.X, gy - _guideDragLast.Y);
                _guideDragLast = (gx, gy);
                e.Handled = true;
                return;
            }

            // The cursor is the whole affordance — the pointer changing shape
            // over a guide is what tells you it can be picked up, instead of a
            // row of buttons floating over the drawing.
            UpdateGuideHoverCursor(e.GetPosition(this));

            if (_aligningReference)
            {
                var (ax, ay) = ViewToDoc(e.GetPosition(this));
                // Incremental, not "start plus total": the reference is nudged
                // through an undoable delta each event, and an absolute drag
                // would leave one enormous step in the history.
                ReferenceDragged?.Invoke(
                    ax - _alignLast.X, ay - _alignLast.Y, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                _alignLast = new Point(ax, ay);
                e.Handled = true;
                return;
            }

            if (_gridGesture != GridGesture.None)
            {
                var (gx, gy) = ViewToDoc(e.GetPosition(this));
                switch (_gridGesture)
                {
                    case GridGesture.Move:
                        // Incremental, like the sheet nudge: an absolute drag
                        // would leave one enormous step in the history.
                        ReferenceBoxMoved?.Invoke(_gridIndex, gx - _gridLast.X, gy - _gridLast.Y);
                        break;
                    case GridGesture.Resize:
                        ReferenceBoxResized?.Invoke(
                            _gridIndex, _gridLeft, _gridTop, gx - _gridLast.X, gy - _gridLast.Y);
                        break;
                    case GridGesture.Draw:
                        _newBox = SKRect.Create(
                            (float)Math.Min(_gridStart.X, gx), (float)Math.Min(_gridStart.Y, gy),
                            (float)Math.Abs(gx - _gridStart.X), (float)Math.Abs(gy - _gridStart.Y));
                        break;
                }
                _gridLast = new Point(gx, gy);
                e.Handled = true;
                return;
            }

            if (_txActive && _txDrag != TxDrag.None)
            {
                var (tx, ty) = ViewToDoc(e.GetPosition(this));
                TxDragTo(tx, ty, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                e.Handled = true;
                return;
            }

            if (_panning)
            {
                var pos = e.GetPosition(this);
                _pan += pos - _panLast;
                _panLast = pos;
                ViewUpdated();
                e.Handled = true;
                return;
            }

            // changing a line's weight?
            if (ToolMode == CanvasToolMode.Width && _widthGrab >= 0)
            {
                var (wx, wy) = ViewToDoc(e.GetPosition(this));
                _dragWidth?.Invoke(_widthGrab, wx, wy);
                e.Handled = true;
                return;
            }

            // drawing a path?
            if (ToolMode == CanvasToolMode.Pen)
            {
                var (px, py) = ViewToDoc(e.GetPosition(this));
                var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                if (_penShaping)
                {
                    _penDrag?.Invoke(px, py, shift, e.KeyModifiers.HasFlag(KeyModifiers.Alt));
                }
                else
                {
                    // The rubber band, which is the whole reason a pen is
                    // predictable: you see the curve the next click will make
                    // before you commit to it — including the click that would
                    // close the path, which is why the tolerance travels too.
                    _penHover?.Invoke(px, py, DocTolerance(GrabPixels), shift);
                }
                e.Handled = true;
                return;
            }

            // Not dragging anything, white arrow in hand: preview the line
            // under the pointer. The view model answers whether the preview
            // actually moved, so an unchanged hover costs a hit test and no
            // repaint — a hover fires on every pointer event, and this is a
            // per-event path.
            if (ToolMode == CanvasToolMode.PathEdit && !_pathGrab.IsHit)
            {
                var (hx, hy) = ViewToDoc(e.GetPosition(this));
                _hoverPathPart?.Invoke(hx, hy, DocTolerance(GrabPixels));
                // Deliberately not handled: a preview is not an interaction,
                // and swallowing the move here would stop everything below it
                // that also wants to know where the pointer is.
            }

            // reshaping a node or a handle?
            if (_pathGrab.IsHit && _pathDragLast is { } pathFrom)
            {
                var (px, py) = ViewToDoc(e.GetPosition(this));
                _pathDragLast = (px, py);
                _dragPathPart?.Invoke(
                    _pathGrab, px, py, px - pathFrom.X, py - pathFrom.Y,
                    // Alt breaks a smooth node's handle pair, Illustrator's
                    // modifier, so the muscle memory transfers.
                    e.KeyModifiers.HasFlag(KeyModifiers.Alt));
                e.Handled = true;
                return;
            }

            // selection shape in progress?
            if (_dragShape.Count > 0 && ToolMode == CanvasToolMode.SelectFreehand)
            {
                var (dx, dy) = ViewToDoc(e.GetPosition(this));
                _dragShape.Add(new Core.Documents.StrokePoint(dx, dy, 1));
                e.Handled = true;
                return;
            }
            if (_gradientDragging)
            {
                var (gx, gy) = ViewToDoc(e.GetPosition(this));
                GradientDragMoved?.Invoke(gx, gy, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                e.Handled = true;
                return;
            }
            if (_shapeDragging)
            {
                var (sx, sy) = ViewToDoc(e.GetPosition(this));
                ShapeDragMoved?.Invoke(
                    sx, sy,
                    e.KeyModifiers.HasFlag(KeyModifiers.Alt),
                    e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                e.Handled = true;
                return;
            }
            if (_lineDragFrom is not null)
            {
                _lineDragTo = ViewToDoc(e.GetPosition(this));
                // Only chrome moves here, so this is a repaint of the overlay
                // rather than a re-render of the frame. See DrawSelectedLines.
                InvalidateVisual();
                e.Handled = true;
                return;
            }
            if (_lineMarqueeFrom is not null)
            {
                _lineMarqueeTo = ViewToDoc(e.GetPosition(this));
                InvalidateVisual();
                e.Handled = true;
                return;
            }
            if (_dragAnchor is { } anchor && ToolMode is CanvasToolMode.SelectRect or CanvasToolMode.SelectEllipse)
            {
                var (dx, dy) = ViewToDoc(e.GetPosition(this));
                // Square it only when Shift arrived after the press — held
                // from the start it means "add to the selection" instead.
                if (!_marqueeAdds && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    (dx, dy) = Squared(anchor, dx, dy);
                }
                _dragShape.Clear();
                _dragShape.AddRange(ShapeBetween(anchor, (dx, dy), ToolMode == CanvasToolMode.SelectEllipse));
                e.Handled = true;
                return;
            }

            if (!_painting) return;
            // Only the pointer that began the stroke may extend it: the mouse
            // moving (or being moved by the OS on a refocus) while the pen is
            // down must not drag the mark to wherever the mouse happens to be.
            if (e.Pointer.Id != _paintPointerId) return;
            // Shift during a brush drag holds it to one axis, the way it does
            // in Photoshop. Applied here rather than in the view model because
            // it is a property of the gesture, not of the mark: the record
            // stores the points the artist actually asked for.
            _axisLockedStroke = e.KeyModifiers.HasFlag(KeyModifiers.Shift) && !_straightFromLast;
            // Coalesced high-frequency samples, not just the latest position —
            // delivered as one batch per event.
            var points = e.GetIntermediatePoints(this);
            var samples = new List<ViewModels.MainViewModel.PointerSample>(points.Count);
            foreach (var pp in points)
            {
                // Coalesced history can reach back past the press into hover
                // samples — positions from before the stroke, with no contact
                // and no pressure. After a refocus that history is where the
                // pen last was, which may be nowhere near where it came down.
                if (!pp.Properties.IsLeftButtonPressed) continue;
                var (x, y) = ViewToDoc(pp.Position);
                if (_axisLockedStroke) (x, y) = AxisLocked(_paintAnchor, x, y);
                samples.Add(new ViewModels.MainViewModel.PointerSample(x, y, PressureOf(pp)));
                ReportCursorPressure(PressureOf(pp), penDown: true);
            }
            if (samples.Count > 0)
            {
                ReportInputDiagnostic(e.Pointer.Type, points[^1].Properties.Pressure);
                PaintMoved?.Invoke(samples);
            }
            e.Handled = true;
        }
        catch (Exception ex)
        {
            ReportInputError("move", ex);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_movingGuides)
        {
            _movingGuides = false;
            e.Pointer.Capture(null);
            GuidesMovedEnded?.Invoke();
            e.Handled = true;
            return;
        }
        if (_movingRefBoxes)
        {
            _movingRefBoxes = false;
            e.Pointer.Capture(null);
            RefBoxesMovedEnded?.Invoke();
            e.Handled = true;
            return;
        }
        if (_cameraDrag != CameraDrag.None)
        {
            _cameraDrag = CameraDrag.None;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }
        if (_movingAnchors)
        {
            _movingAnchors = false;
            e.Pointer.Capture(null);
            // Cleared before the commit, so the record's new position is what the
            // next paint reads. Clearing afterwards would double the move for one
            // frame and read as an overshoot.
            ClearRigPreview();
            AnchorsMovedEnded?.Invoke();
            e.Handled = true;
            return;
        }
        if (_movingShapes)
        {
            _movingShapes = false;
            e.Pointer.Capture(null);
            ClearRigPreview();
            ShapesMovedEnded?.Invoke();
            e.Handled = true;
            return;
        }
        if (_movingContent)
        {
            _movingContent = false;
            e.Pointer.Capture(null);
            ContentMoveEnded?.Invoke();
            e.Handled = true;
            return;
        }
        if (_guideDrag is not null)
        {
            _guideDrag = null;
            e.Pointer.Capture(null);
            if (_guideResizing)
            {
                _guideResizing = false;
                GuideResizeEnded?.Invoke();
            }
            else
            {
                GuideDragEnded?.Invoke();
            }
            e.Handled = true;
            return;
        }
        if (_txActive && _txDrag != TxDrag.None)
        {
            _txDrag = TxDrag.None;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }
        if (_aligningReference)
        {
            _aligningReference = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }
        // A bone gesture ends the same way a rig drag does: on release, with the
        // endpoints, one editor step. The window decides what the gesture meant —
        // create, joint move, re-aim or pose — from the grab and the mode.
        if (_boneGestureActive)
        {
            var (bx, by) = ViewToDoc(e.GetPosition(this));
            _boneGestureActive = false;
            var boneId = _boneDragId;
            var boneGrab = _boneDragGrab;
            _boneDragId = null;
            e.Pointer.Capture(null);
            BoneGestureEnded?.Invoke(boneId, boneGrab, _boneGestureStart.X, _boneGestureStart.Y, bx, by);
            e.Handled = true;
            return;
        }
        // B58. On release with the total delta, because DragRig is one editor step
        // per call — reporting per pointer move would be a hundred undo entries for
        // one drag of a hitbox.
        if (RigEditMode && _rigDragId is { } dragged)
        {
            var (ux, uy) = ViewToDoc(e.GetPosition(this));
            _rigDragId = null;
            e.Pointer.Capture(null);
            ClearRigPreview();
            RigDragged?.Invoke(dragged, _rigDragCorner, ux - _rigDragStart.X, uy - _rigDragStart.Y);
            e.Handled = true;
            return;
        }
        if (_gridGesture != GridGesture.None)
        {
            if (_gridGesture == GridGesture.Draw && _newBox is { } drawn && drawn.Width > 2 && drawn.Height > 2)
            {
                ReferenceBoxDrawn?.Invoke(drawn.Left, drawn.Top, drawn.Width, drawn.Height);
            }
            _newBox = null;
            _gridGesture = GridGesture.None;
            _gridIndex = -1;
            e.Pointer.Capture(null);
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (_panning)
        {
            _panning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }
        if (_gradientDragging)
        {
            _gradientDragging = false;
            e.Pointer.Capture(null);
            var (gx, gy) = ViewToDoc(e.GetPosition(this));
            GradientDragEnded?.Invoke(gx, gy, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
            return;
        }
        if (_shapeDragging)
        {
            _shapeDragging = false;
            e.Pointer.Capture(null);
            var (sx, sy) = ViewToDoc(e.GetPosition(this));
            ShapeDragEnded?.Invoke(
                sx, sy,
                e.KeyModifiers.HasFlag(KeyModifiers.Alt),
                e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
            return;
        }
        if (ToolMode == CanvasToolMode.Width)
        {
            _widthGrab = -1;
            e.Pointer.Capture(null);
            _endWidth?.Invoke();
            e.Handled = true;
            return;
        }
        if (ToolMode == CanvasToolMode.Pen)
        {
            // Unconditional, not guarded on whether the press took: a press that
            // was refused still captured the pointer, and a capture nothing
            // releases is a canvas that has stopped responding.
            _penShaping = false;
            e.Pointer.Capture(null);
            _penRelease?.Invoke();
            e.Handled = true;
            return;
        }
        if (_pathGrab.IsHit)
        {
            // One undo step for the whole drag, however many pointer moves it
            // took — the transform session's rule, and the reason the live edit
            // never touched the document.
            _pathGrab = ViewModels.PathHit.Miss;
            _pathDragLast = null;
            e.Pointer.Capture(null);
            _commitPathEdit?.Invoke();
            e.Handled = true;
            return;
        }
        if (_lineDragFrom is { } dragFrom)
        {
            _lineDragFrom = null;
            e.Pointer.Capture(null);
            var landed = ViewToDoc(e.GetPosition(this));
            var dragScale = Math.Max(0.01, FitScale() * _zoom);
            var travelled = Math.Max(
                Math.Abs(landed.X - dragFrom.X), Math.Abs(landed.Y - dragFrom.Y)) * dragScale;
            // A press that went nowhere is the click that selected the line, and
            // committing it would put an identity move in the history for every
            // selection an artist makes.
            if (travelled >= LineDragMinimumPixels)
            {
                SelectedLinesDragged?.Invoke(landed.X - dragFrom.X, landed.Y - dragFrom.Y);
            }
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (_lineMarqueeFrom is { } from)
        {
            _lineMarqueeFrom = null;
            e.Pointer.Capture(null);
            var to = ViewToDoc(e.GetPosition(this));
            var scale = Math.Max(0.01, FitScale() * _zoom);
            var moved = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y)) * scale;
            if (moved >= MarqueeMinimumPixels)
            {
                LinesMarqueed?.Invoke(
                    SKRect.Create(
                        (float)Math.Min(from.X, to.X), (float)Math.Min(from.Y, to.Y),
                        (float)Math.Abs(to.X - from.X), (float)Math.Abs(to.Y - from.Y)),
                    // The flag from the press, not from now — the same reason
                    // the pixel marquee reads its own: Shift arriving mid-drag
                    // means something else, and reading it live would union by
                    // accident.
                    _lineMarqueeAdds);
            }
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (_dragShape.Count > 0 || _dragAnchor is not null)
        {
            var shape = _dragShape.ToList();
            _dragShape.Clear();
            _dragAnchor = null;
            e.Pointer.Capture(null);
            if (shape.Count >= 3)
            {
                // The union flag is the one from the press, not from now: a
                // Shift pressed mid-drag was squaring the marquee, and reading
                // the live modifier here would silently union as well.
                SelectionShapeDrawn?.Invoke(
                    shape,
                    _marqueeAdds,
                    e.KeyModifiers.HasFlag(KeyModifiers.Alt));
            }
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (!_painting) return;
        // The stroke ends when the pointer that made it lifts — a release from
        // any other device mid-stroke (the refocus mouse-click again) would cut
        // the mark short under a pen that is still down.
        if (e.Pointer.Id != _paintPointerId) return;
        _painting = false;
        _paintPointerId = -1;
        NoteStrokePressureHistory();
        e.Pointer.Capture(null);
        ReportCursorPressure(1, penDown: false); // back to showing the maximum on hover
        PaintEnded?.Invoke();
        e.Handled = true;
    }

    /// <summary>
    /// The far corner, pulled onto the diagonal so the box is square.
    /// </summary>
    /// <remarks>
    /// The larger of the two reaches wins, so the shape follows the hand
    /// rather than shrinking to whichever axis moved least.
    /// </remarks>
    private static (double X, double Y) Squared((double X, double Y) anchor, double x, double y)
    {
        var reach = Math.Max(Math.Abs(x - anchor.X), Math.Abs(y - anchor.Y));
        return (
            anchor.X + (x < anchor.X ? -reach : reach),
            anchor.Y + (y < anchor.Y ? -reach : reach));
    }

    /// <summary>Rectangle corners or a 48-segment ellipse between two drag points.</summary>
    private static IEnumerable<Core.Documents.StrokePoint> ShapeBetween(
        (double X, double Y) a, (double X, double Y) b, bool ellipse)
    {
        if (!ellipse)
        {
            yield return new(a.X, a.Y, 1);
            yield return new(b.X, a.Y, 1);
            yield return new(b.X, b.Y, 1);
            yield return new(a.X, b.Y, 1);
            yield break;
        }
        var cx = (a.X + b.X) / 2;
        var cy = (a.Y + b.Y) / 2;
        var rx = Math.Abs(b.X - a.X) / 2;
        var ry = Math.Abs(b.Y - a.Y) / 2;
        for (var i = 0; i < 48; i++)
        {
            var t = i / 48.0 * Math.PI * 2;
            yield return new(cx + rx * Math.Cos(t), cy + ry * Math.Sin(t), 1);
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        CancelPointerGestures();
    }

    /// <summary>
    /// Abandon whatever gesture is mid-flight, exactly as losing pointer
    /// capture does. The window calls this on Deactivated: a release delivered
    /// to another application is one this control will never see, and a stroke
    /// still marked in-flight would resume from its stale anchor the moment
    /// the window came back (B185).
    /// </summary>
    public void CancelPointerGestures()
    {
        _panning = false;
        if (_movingGuides)
        {
            _movingGuides = false;
            // Guide move cancellation
            return;
        }
        if (_movingRefBoxes)
        {
            _movingRefBoxes = false;
            // Reference box move cancellation
            return;
        }
        if (_lineDragFrom is not null)
        {
            // Abandon, and the selection stays. Nothing was written, so there is
            // nothing to roll back — the outline simply snaps home.
            _lineDragFrom = null;
            InvalidateVisual();
            return;
        }
        if (_lineMarqueeFrom is not null)
        {
            // Abandon. Losing capture is not a decision the artist made, and a
            // half-dragged box is not a selection they asked for.
            _lineMarqueeFrom = null;
            InvalidateVisual();
            return;
        }
        if (_movingContent)
        {
            // Abandon rather than commit, for the gradient's reason: losing
            // capture is not a decision the artist made.
            _movingContent = false;
            ContentMoveCancelled?.Invoke();
            return;
        }
        if (_gradientDragging)
        {
            // Abandon rather than commit: losing capture is not a decision the
            // artist made, and a half-dragged ramp is not what they wanted.
            _gradientDragging = false;
            GradientDragCancelled?.Invoke();
            return;
        }
        if (!_painting) return;
        _painting = false;
        _paintPointerId = -1;
        NoteStrokePressureHistory();
        ReportCursorPressure(1, penDown: false); // back to showing the maximum on hover
        PaintEnded?.Invoke();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        try
        {
            var pos = e.GetPosition(this);
            // Windows converts Shift+wheel into a horizontal delta — accept both axes.
            var notch = e.Delta.Y != 0 ? e.Delta.Y : -e.Delta.X;
            if (notch == 0) return;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                RotateAt(pos, notch * 10);
            }
            else
            {
                ZoomAt(pos, Math.Pow(1.15, notch));
            }
            e.Handled = true;
        }
        catch (Exception ex)
        {
            ReportInputError("wheel", ex);
        }
    }

    // ---- render-thread blit -------------------------------------------------

    // The BrushCursor record moved to CanvasControl.Pointer.cs.

    /// <summary>Pulled-string gizmo, all in document space: dead zone around the cursor, string, anchor.</summary>
    private readonly record struct LazyGizmo(
        float AnchorX, float AnchorY, float CursorX, float CursorY, float Radius, float BrushRadius);

    /// <summary>
    /// One selected line, traced so the artist can see which line they picked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A snapshot in document space, not a live view of the stroke.</b> The
    /// same rule as <see cref="ReferenceBox"/> and for the same reason: the
    /// render runs off the UI thread, so anything it reads has to be immutable
    /// by the time it gets there.
    /// </para>
    /// <para>
    /// <b>The line itself rather than a box round it.</b> A bounding box round a
    /// long diagonal covers half the frame and says almost nothing; tracing the
    /// path says <i>that</i> line. The box arrives with the transform handles,
    /// where it is the thing you grab rather than decoration.
    /// </para>
    /// </remarks>
    private readonly record struct SelectedLine(SKPoint[] Points, bool Closed);

    /// <summary>Where a drag of the selected lines began, in document space.</summary>
    /// <remarks>
    /// <b>Any successful pick arms a drag.</b> That is Illustrator's black arrow:
    /// press selects, and carrying on without lifting moves what you just
    /// selected — one gesture, no modifier to remember. A press that does not
    /// move is a click and commits nothing, so the arming costs an artist who is
    /// only selecting exactly nothing.
    /// </remarks>
    private (double X, double Y)? _lineDragFrom;

    private (double X, double Y) _lineDragTo;

    /// <summary>
    /// How far a press has to travel before it is a move rather than a click.
    /// </summary>
    /// <remarks>
    /// In screen pixels, so it does not get harder to click cleanly as you zoom
    /// in. Without it, a two-pixel wobble on a click would put a one-pixel move
    /// in the undo history for every selection an artist makes.
    /// </remarks>
    private const double LineDragMinimumPixels = 3.0;

    /// <summary>
    /// A drag of the selected lines finished — the offset, in document units.
    /// </summary>
    public event Action<double, double>? SelectedLinesDragged;

    /// <summary>The live drag offset, for the outline to follow while dragging.</summary>
    private SKPoint LineDragOffset() => _lineDragFrom is { } from
        ? new SKPoint((float)(_lineDragTo.X - from.X), (float)(_lineDragTo.Y - from.Y))
        : default;

    private IReadOnlyList<SelectedLine>? _selectedLines;

    /// <summary>
    /// Hand the canvas the outlines of whatever is selected, or null for none.
    /// </summary>
    /// <remarks>
    /// Called when the selection changes — user-paced — never per pointer move.
    /// Rebuilding these on a move would be work proportional to the drawing in a
    /// move handler, which is what invariant 6 exists to forbid.
    /// </remarks>
    public void SetSelectedLines(IReadOnlyList<(IReadOnlyList<Core.Documents.StrokePoint> Points, bool Closed)>? lines)
    {
        if (lines is null || lines.Count == 0)
        {
            if (_selectedLines is null) return;
            _selectedLines = null;
            InvalidateVisual();
            return;
        }

        var built = new List<SelectedLine>(lines.Count);
        foreach (var (points, closed) in lines)
        {
            if (points.Count < 2) continue;
            var pts = new SKPoint[points.Count];
            for (var i = 0; i < points.Count; i++)
            {
                pts[i] = new SKPoint((float)points[i].X, (float)points[i].Y);
            }
            built.Add(new SelectedLine(pts, closed));
        }
        _selectedLines = built;
        InvalidateVisual();
    }

    /// <summary>Transform gizmo, all in document space: the transformed quad, pivot, and mode.</summary>
    private readonly record struct TxGizmoData(
        SKPoint C0, SKPoint C1, SKPoint C2, SKPoint C3, SKPoint Pivot, bool Perspective);

    /// <summary>
    /// Decomposed view transform for the render thread — primitive canvas ops
    /// only (translate/rotate/scale), no matrix API edge cases.
    /// </summary>
    private readonly record struct ViewState(
        float DocW, float DocH, float Scale, float RotationDeg, bool Mirrored, float CenterX, float CenterY);

    /// <summary>Called from the render thread once a snapshot has been drawn.</summary>
    /// <summary>
    /// Report a completed render without a compositor. Tests only — it is the
    /// only way to exercise the branch where snapshots CAN be released, since
    /// nothing draws in a headless run.
    /// </summary>
    internal void NoteRenderedForTest(long seq) => NoteRendered(seq);

    /// <summary>
    /// Whether every image the canvas is still holding — the one on screen and
    /// everything retired behind it — is alive. The crash was this going
    /// false: the compositor drew a snapshot the canvas had already freed.
    /// Tests only.
    /// </summary>
    internal bool HeldImagesAlive =>
        // A null image is a snapshot whose composite has not been performed yet
        // (B125 stage 3b) — nothing has been freed, so nothing is dangling.
        (_snapshot is null || Alive(_snapshot)) && _retired.All(Alive);

    /// <summary>
    /// Whether a held snapshot is safe to draw. A snapshot with no image is safe
    /// when it has not been composed yet (B125 stage 3b) and unsafe when it was
    /// freed — null alone cannot tell those apart, which is why
    /// <see cref="RenderSnapshot.IsDisposed"/> exists.
    /// </summary>
    private static bool Alive(RenderSnapshot s) =>
        !s.IsDisposed && (s.Image is not { } img || img.Handle != IntPtr.Zero);

    /// <summary>How many frames are queued behind the one on screen. Tests only.</summary>
    internal int RetiredCount => _retired.Count;

    /// <summary>
    /// A snapshot the canvas had not shown before has been drawn — its seq,
    /// delivered on the UI thread.
    /// </summary>
    /// <remarks>
    /// The publisher's back-pressure signal (B189): the view model defers the
    /// next coalesced publish until the last one has actually been drawn, and
    /// this is how it learns that happened. Posted at <c>Input</c> priority,
    /// not <c>Background</c> like <see cref="FrameRendered"/>: the moment this
    /// signal matters most is mid-stroke, which is exactly when continuous
    /// pointer input starves <c>Background</c> — a deferred publish waiting on
    /// a starved notification would be the lag this exists to remove.
    /// </remarks>
    public event Action<long>? SnapshotPresented;

    private void NoteRendered(long seq)
    {
        // Before the early return below, which is about keeping the high-water
        // mark monotonic. A frame that arrived out of order was still drawn, and
        // dropping it here would flatter the average by counting only the
        // frames that behaved.
        _presentWait.Rendered(seq);
        StrokeToScreen.Shared.Rendered(seq);

        long current;
        do
        {
            current = Interlocked.Read(ref _lastRenderedSeq);
            if (seq <= current) return;
        }
        while (Interlocked.CompareExchange(ref _lastRenderedSeq, seq, current) != current);

        // Only when the high-water mark moved: a cursor repaint re-draws the
        // same snapshot many times a second, and the publisher only cares that
        // a NEW frame reached the screen. The deferral race is safe by
        // ordering — a publish can only be deferred while its draw's
        // notification has not been processed yet, so the post that releases
        // it is always already queued.
        if (SnapshotPresented is null) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => SnapshotPresented?.Invoke(seq),
            Avalonia.Threading.DispatcherPriority.Input);
    }

    /// <summary>Frame times arrive from the render thread; marshal to the UI thread to publish them.</summary>
    private void ReportFrameTime(double milliseconds)
    {
        if (FrameRendered is null) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => FrameRendered?.Invoke(milliseconds),
            Avalonia.Threading.DispatcherPriority.Background);
    }

    private sealed class DrawOp(
        Rect bounds, RenderSnapshot snapshot, ViewState view, BrushCursor? cursor,
        SKPath? ants, SKPath? antsOpen, float antsPhase, LazyGizmo? lazy = null,
        TxGizmoData? txGizmo = null, Action<long>? onRendered = null,
        Action<double>? onFrameTime = null, SKPoint[]? cameraFrame = null,
        (SKPoint From, SKPoint To)? gradientAxis = null,
        IReadOnlyList<ReferenceBox>? referenceBoxes = null,
        SKRect? newBox = null,
        IReadOnlyList<GuideLine>? guides = null,
        GuideLine? draftGuide = null,
        IReadOnlyList<RigMark>? rigMarks = null,
        IReadOnlyList<BalanceDot>? balanceDots = null,
        ViewModels.SelectionManager? selectionManager = null,
        Func<IReadOnlyList<Core.Documents.SymbolPlacement>?>? getPlacementsForSelection = null,
        PresentedFrame? presented = null,
        Action<GRContext?>? gpuWork = null,
        IReadOnlyList<SelectedLine>? selectedLines = null,
        SKRect? lineMarquee = null,
        SKPoint lineDrag = default,
        IReadOnlyList<PathNodeGlyph>? pathNodes = null,
        IReadOnlyList<Core.Documents.StrokePoint>? penPreview = null,
        IReadOnlyList<Core.Documents.StrokePoint>? pathTrace = null,
        LayerTextureCache? textures = null,
        ChannelSolo solo = ChannelSolo.None,
        PickRing? pickRing = null,
        IReadOnlyList<BoneChrome>? bones = null,
        IReadOnlyList<HeatPoint>? heat = null) : ICustomDrawOperation
    {
        public Rect Bounds { get; } = bounds;

        public bool HitTest(Point p) => Bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
            ants?.Dispose();
            antsOpen?.Dispose();
        }

        public void Render(ImmediateDrawingContext context)
        {
            // The compositor dies with the first unhandled exception here and
            // the whole window freezes — never let that happen.
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                RenderCore(context);
                onRendered?.Invoke(snapshot.Seq);
                onFrameTime?.Invoke(
                    (System.Diagnostics.Stopwatch.GetTimestamp() - started)
                    * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            }
            catch (Exception ex)
            {
                LogDiag("render", ex);
            }
        }

        private void RenderCore(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            RecordBackend(lease);

            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, (float)Bounds.Width, (float)Bounds.Height));
            canvas.Clear(new SKColor(0x2b, 0x2b, 0x2b));

            canvas.Save();
            canvas.Translate(view.CenterX, view.CenterY);
            canvas.RotateDegrees(view.RotationDeg);
            canvas.Scale(view.Mirrored ? -view.Scale : view.Scale, view.Scale);
            canvas.Translate(-view.DocW / 2f, -view.DocH / 2f);
            // B122: draw the durable frame rather than this publish's image. The
            // frame is patched with only what changed, so on a GPU-backed lease the
            // upload is the dab instead of the canvas — and the frame's own
            // snapshot is already a texture, making this a GPU-to-GPU blit.
            //
            // A null `presented` is the honest fallback for any caller that has not
            // been given one (tests constructing a DrawOp directly): draw the
            // publish's image, exactly as before.
            // B130: the durable frame is OFF by default, and this is a retreat
            // rather than a tidy-up.
            //
            // `PresentedFrame.PresentCore` disposed the previous snapshot on every
            // present, reasoning that renders are sequential so nothing could still
            // be drawing it. That is wrong, and wrong in the exact way this file
            // already documents a few hundred lines up: the compositor may still
            // hold the image it was handed, and freeing it under Skia is an access
            // violation inside `sk_canvas_draw_image_rect` — a NATIVE crash, so the
            // crash reporter's three managed channels never see it and the log is
            // empty. Reported as "Lightbox dies after the splash screen as soon as
            // I touch anything, and there is no crash report".
            //
            // The retirement machinery in this class exists for precisely that
            // hazard and B122 did not use it. Until it does, the safe path is the
            // one that shipped for years: draw the publish's own image, whose
            // lifetime `_retired` already manages. `LIGHTBOX_DURABLE_FRAME=1` opts
            // back in for measuring the fix, and it is deliberately an environment
            // variable rather than a setting — nobody should find this by accident.
            // B125 stage 3b: the composite may not have happened yet. Materialise
            // here — inside the draw op, on the render thread — because this is
            // where the lease's GRContext is, and handing it over is the only way
            // a composite can be GPU-backed at all (stage 4). A snapshot that
            // already carries an image returns it unchanged, which is every route
            // except the culled one.
            // B179: free the retired frames' render targets HERE, with the
            // lease's context current, before composing the next one — the
            // retirement queue runs on the UI thread, where a GPU release only
            // parks the resource. Before Materialise on purpose, so the memory
            // this frame is about to ask for was just given back.
            GpuImageReaper.Drain(lease.GrContext);
            var composed = snapshot.Materialise(lease.GrContext, textures);
            // B179: Skia's own GPU resource cache is the one large pool the
            // memory section could not see, and it is only askable from here —
            // the context lives in the lease and nowhere else. Sampled rather
            // than subscribed: one call per draw is nothing beside the composite
            // above it, and the report needs a recent number rather than a
            // running one.
            SkiaMemory.Sample(lease.GrContext);
            var artwork = presented is null || !DurableFrameEnabled
                ? composed
                : presented.Present(lease.GrContext, composed, snapshot.ChangedInImage, snapshot.Seq);

            // Queued work that needs the context (the render report's upload
            // probe). After the frame's own drawing, and wrapped, because a
            // diagnostic must never be able to take the compositor down — the
            // catch in Render would survive it, but this frame would be lost.
            if (gpuWork is not null)
            {
                try { gpuWork(lease.GrContext); } catch { /* a report is not worth a frame */ }
            }

            // Checkerboard, artwork, guides — one call, because splitting them
            // apart is how B17 happened. See GuidePainter.PaintDocument.
            GuidePainter.PaintDocument(
                canvas,
                artwork,
                view.DocW,
                view.DocH,
                view.Scale,
                c => DrawTransparencyCheckerboard(c, view),
                ToPainterLines(guides),
                draftGuide is { } d ? ToPainterLine(d) : null,
                snapshot.DocViewport,
                ChannelSoloFilters.For(solo));
            DrawCameraFrame(canvas);
            DrawGradientAxis(canvas);
            // B58. Over the artwork and over the guides, under the selection ants:
            // a rig is furniture you aim with, and the reason guides sit over the
            // art (an opaque background layer) applies to sockets just as much.
            RigOverlayPainter.Paint(canvas, rigMarks, view.Scale);
            RigOverlayPainter.PaintLabels(canvas, rigMarks, view.Scale);
            // The balance arc is furniture you judge against, same layer of
            // the sandwich as the rig.
            BalanceOverlayPainter.Paint(canvas, balanceDots, view.Scale);
            // Heat under the bones, both over the rig marks: the armature is
            // aimed with the same hand, and the heat is what a weight brush
            // corrects against.
            ArmatureOverlayPainter.PaintHeat(canvas, heat, view.Scale);
            ArmatureOverlayPainter.Paint(canvas, bones, view.Scale);
            DrawAnts(canvas);
            DrawLazyGizmo(canvas);
            DrawTransformGizmo(canvas);
            DrawReferenceBoxes(canvas);
            DrawObjectSelections(canvas);
            canvas.Restore();

            if (cursor is { } c) DrawBrushCursor(canvas, c);
            if (pickRing is { } ring) PickRing.Draw(canvas, ring);
            canvas.Restore();
        }

        /// <summary>
        /// Rulers, grids and vanishing points.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Over</b> the artwork. Under it was the first answer and it was
        /// wrong for a reason worth keeping written down: a new document opens
        /// with an opaque background layer, so "underneath the drawing" means
        /// underneath a sheet of white, and the guides could only ever be seen
        /// off the edge of the canvas. The paper analogy does not survive
        /// contact with an opaque bottom layer.
        /// </para>
        /// <para>
        /// The thing the analogy was protecting — not hiding the drawing — is
        /// paid for with alpha instead: every guide is drawn translucent, so
        /// the rig reads as a rig and the art stays legible through it.
        /// </para>
        /// <para>
        /// Every line is divided by the zoom so it stays a hairline on screen.
        /// A guide that scaled with the view would be invisible at 25% and a
        /// stripe at 800%, which is the opposite of what chrome is for. The
        /// grid also stops drawing when its cells fall under a few pixels —
        /// past that it is a grey wash, not a grid.
        /// </para>
        /// </remarks>
        private static IReadOnlyList<GuidePainter.Line>? ToPainterLines(
            IReadOnlyList<GuideLine>? lines) =>
            lines is null ? null : [.. lines.Select(ToPainterLine)];

        private static GuidePainter.Line ToPainterLine(GuideLine g) =>
            new(g.Kind, g.X, g.Y, g.Spacing, g.Angles, g.Label, g.Divisions);

        /// <summary>
        /// The reference grid, while it is being edited.
        /// </summary>
        /// <remarks>
        /// <para>
        /// View-only chrome, exactly like the camera frame and the transform
        /// gizmo: it never reaches a pixel of the document. Absent unless the
        /// mode is on, so an artist who never touches a reference sees none of
        /// it.
        /// </para>
        /// <para>
        /// Every line is divided by the zoom so the boxes stay one pixel wide
        /// on screen. A gizmo that scaled with the view would be a hairline at
        /// 25% and a slab at 800%, which is the opposite of what chrome is for.
        /// </para>
        /// </remarks>
        private void DrawReferenceBoxes(SKCanvas canvas)
        {
            if (referenceBoxes is not { Count: > 0 } boxes && newBox is null) return;
            var scale = Math.Max(0.01f, view.Scale);
            var thin = 1f / scale;
            var handle = 4f / scale;

            using var edge = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = thin,
                Color = new SKColor(90, 170, 255, 200),
                IsAntialias = true,
            };
            using var chosen = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = thin * 2,
                Color = new SKColor(255, 190, 60, 235),
                IsAntialias = true,
            };
            using var pivot = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = thin * 1.5f,
                Color = new SKColor(255, 90, 90, 235),
                IsAntialias = true,
            };
            using var grip = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = new SKColor(255, 255, 255, 220),
                IsAntialias = true,
            };

            foreach (var box in referenceBoxes ?? [])
            {
                var rect = SKRect.Create(box.X, box.Y, box.W, box.H);
                canvas.DrawRect(rect, box.Selected ? chosen : edge);
                if (box.Selected)
                {
                    // Corners only. Eight handles on a box this small is a row
                    // of overlapping targets, and the corners are the two
                    // gestures — origin and extent — that actually differ.
                    foreach (var corner in (SKPoint[])
                        [
                            new(rect.Left, rect.Top), new(rect.Right, rect.Top),
                            new(rect.Left, rect.Bottom), new(rect.Right, rect.Bottom),
                        ])
                    {
                        canvas.DrawRect(
                            SKRect.Create(corner.X - handle, corner.Y - handle, handle * 2, handle * 2),
                            grip);
                    }
                }
                // The registration mark, as a cross rather than a dot: a dot
                // has no centre you can see once it is over a drawing.
                var arm = 6f / scale;
                canvas.DrawLine(box.PivotX - arm, box.PivotY, box.PivotX + arm, box.PivotY, pivot);
                canvas.DrawLine(box.PivotX, box.PivotY - arm, box.PivotX, box.PivotY + arm, pivot);
            }

            if (newBox is { } drawing) canvas.DrawRect(drawing, chosen);
        }

        /// <summary>
        /// Trace every selected line, so the artist can see which they picked.
        /// </summary>
        /// <remarks>
        /// Two passes: a wide translucent halo under a thin opaque core. One
        /// stroke alone disappears against art of a similar colour — a black
        /// line highlighted in cyan on a cyan drawing is not highlighted — and
        /// the halo is what makes it read on anything. Both widths are divided
        /// by the zoom, so the marker stays the same thickness on screen at any
        /// magnification, like every other piece of chrome here.
        /// </remarks>
        /// <summary>
        /// The box being dragged with the arrow.
        /// </summary>
        /// <remarks>
        /// <b>Its own dashes rather than the marching ants.</b> The ants mean
        /// "this is an area, and paint will be confined to it" — the pixel
        /// selection. Borrowing them for an object selection would say the
        /// wrong thing with the app's own vocabulary, which is exactly the
        /// confusion Q48 separated these two tools to avoid. Cyan and static,
        /// matching the trace it is about to produce.
        /// </remarks>
        private void DrawLineMarquee(SKCanvas canvas)
        {
            if (lineMarquee is not { } rect) return;
            var scale = Math.Max(0.01f, view.Scale);
            using var paint = new SKPaint
            {
                Color = SKColors.Cyan,
                StrokeWidth = 1f / scale,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true,
                PathEffect = SKPathEffect.CreateDash([4f / scale, 4f / scale], 0),
            };
            using var fill = new SKPaint { Color = SKColors.Cyan.WithAlpha(20) };
            canvas.DrawRect(rect, fill);
            canvas.DrawRect(rect, paint);
        }

        private void DrawSelectedLines(SKCanvas canvas)
        {
            if (selectedLines is null || selectedLines.Count == 0) return;

            var scale = Math.Max(0.01f, view.Scale);
            // While a drag is in flight the outline is the only thing that moves.
            // Re-rendering the artwork every pointer move would repaint the whole
            // frame from its strokes — invariant 6's exact prohibition — so the
            // chrome shows where the lines are going and the pixels arrive on
            // release. Translating the canvas rather than the points keeps this
            // free of per-point work, and keeps stroke coordinates untouched
            // until the edit is actually committed.
            var dragging = lineDrag != default;
            if (dragging)
            {
                canvas.Save();
                canvas.Translate(lineDrag.X, lineDrag.Y);
            }
            using var halo = new SKPaint
            {
                Color = SKColors.Cyan.WithAlpha(70),
                StrokeWidth = 6f / scale,
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true,
            };
            using var core = new SKPaint
            {
                Color = SKColors.Cyan,
                StrokeWidth = 1.5f / scale,
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true,
            };

            foreach (var line in selectedLines)
            {
                if (line.Points.Length < 2) continue;
                using var path = new SKPath();
                path.MoveTo(line.Points[0]);
                for (var i = 1; i < line.Points.Length; i++) path.LineTo(line.Points[i]);
                if (line.Closed) path.Close();
                canvas.DrawPath(path, halo);
                canvas.DrawPath(path, core);
            }

            if (dragging) canvas.Restore();
        }

        /// <summary>
        /// The node overlay: handles behind, nodes in front, everything sized in
        /// screen pixels rather than document ones.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Every dimension is divided by the view scale</b>, which is what
        /// makes a node the same size to grab at 25% and at 800%. The overlay is
        /// drawn inside the document transform so its coordinates need no
        /// conversion; only its <em>thicknesses</em> do. Getting this backwards
        /// gives handles that vanish when you zoom out to see the shape — which
        /// is exactly when you need them.
        /// </para>
        /// <para>
        /// <b>Corners are squares and smooth nodes are circles</b>, which is the
        /// convention every vector tool shares and is worth matching rather than
        /// inventing: it is the only way to see, without clicking, whether a
        /// point will kink when you drag its handle.
        /// </para>
        /// <para>
        /// <b>Handles are drawn only for selected nodes</b>, matching the hit
        /// test exactly — an overlay that showed a handle nothing could grab, or
        /// hid one that could be, would make the tool feel unreliable in a way
        /// that is very hard to report.
        /// </para>
        /// </remarks>
        private void DrawPathNodes(SKCanvas canvas)
        {
            if (pathNodes is null || pathNodes.Count == 0) return;

            var scale = Math.Max(0.01f, view.Scale);
            var nodeRadius = 4f / scale;
            var handleRadius = 3f / scale;

            using var stem = new SKPaint
            {
                Color = SKColors.DeepSkyBlue.WithAlpha(200),
                StrokeWidth = 1f / scale,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true,
            };
            using var outline = new SKPaint
            {
                Color = SKColors.DeepSkyBlue,
                StrokeWidth = 1.5f / scale,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true,
            };
            using var hollow = new SKPaint
            {
                Color = SKColors.White,
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
            };
            using var filled = new SKPaint
            {
                Color = SKColors.DeepSkyBlue,
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
            };

            // Handles first, so a stem never draws over the node it belongs to.
            foreach (var n in pathNodes)
            {
                if (!n.Selected) continue;
                foreach (var (hx, hy) in new[] { (n.InX, n.InY), (n.OutX, n.OutY) })
                {
                    if (hx == 0 && hy == 0) continue;
                    var ax = (float)(n.X + hx);
                    var ay = (float)(n.Y + hy);
                    canvas.DrawLine((float)n.X, (float)n.Y, ax, ay, stem);
                    canvas.DrawCircle(ax, ay, handleRadius, hollow);
                    canvas.DrawCircle(ax, ay, handleRadius, outline);
                }
            }

            foreach (var n in pathNodes)
            {
                var x = (float)n.X;
                var y = (float)n.Y;
                var body = n.Selected ? filled : hollow;
                if (n.Corner)
                {
                    var box = new SKRect(x - nodeRadius, y - nodeRadius, x + nodeRadius, y + nodeRadius);
                    canvas.DrawRect(box, body);
                    canvas.DrawRect(box, outline);
                }
                else
                {
                    canvas.DrawCircle(x, y, nodeRadius, body);
                    canvas.DrawCircle(x, y, nodeRadius, outline);
                }

                // The pen's closing indicator: a ring, because the node itself
                // must stay legible under it — the ring says "the next click
                // lands here and joins up", the node keeps saying what it is.
                if (n.CloseHint)
                {
                    canvas.DrawCircle(x, y, nodeRadius * 2.2f, outline);
                }
            }
        }

        /// <summary>
        /// The path the pen has drawn so far, traced rather than painted.
        /// </summary>
        /// <remarks>
        /// <b>Deliberately not the brush.</b> Seeing the real mark would mean
        /// stamping the whole path into the scratch surface on every pointer
        /// move, for as long as the artist takes to place their nodes — the cost
        /// invariant 6 exists to refuse. What a pen actually needs shown is the
        /// <em>shape</em>, and the shape is what a traced line is.
        /// </remarks>
        private void DrawPenPreview(SKCanvas canvas)
        {
            // Two traces, one look: the pen's path in progress and the isolated
            // line being reshaped. Separate channels because both can be alive
            // at once — a parked pen path survives a tool switch into isolation.
            TraceLine(canvas, penPreview);
            TraceLine(canvas, pathTrace);
        }

        private void TraceLine(SKCanvas canvas, IReadOnlyList<Core.Documents.StrokePoint>? trace)
        {
            if (trace is not { Count: > 1 } points) return;

            var scale = Math.Max(0.01f, view.Scale);
            using var path = new SKPath();
            path.MoveTo((float)points[0].X, (float)points[0].Y);
            for (var i = 1; i < points.Count; i++)
            {
                path.LineTo((float)points[i].X, (float)points[i].Y);
            }

            // Two passes: a pale wide one so the line reads over dark artwork,
            // and the blue the nodes are drawn in over it. The same trick the
            // marching ants use, for the same reason — an overlay that vanishes
            // against half the drawings is an overlay you cannot trust.
            using var halo = new SKPaint
            {
                Color = SKColors.White.WithAlpha(160),
                StrokeWidth = 3f / scale,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true,
            };
            using var line = new SKPaint
            {
                Color = SKColors.DeepSkyBlue,
                StrokeWidth = 1.25f / scale,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true,
            };
            canvas.DrawPath(path, halo);
            canvas.DrawPath(path, line);
        }

        private void DrawObjectSelections(SKCanvas canvas)
        {
            DrawSelectedLines(canvas);
            DrawLineMarquee(canvas);
            DrawPenPreview(canvas);
            DrawPathNodes(canvas);
            if (selectionManager is null || !selectionManager.HasSelection) return;

            var scale = view.Scale;
            var reach = (view.DocW + view.DocH) * 2f;

            // Draw placement selections
            if (getPlacementsForSelection is not null)
            {
                var placements = getPlacementsForSelection();
                if (placements is not null && placements.Count > 0)
                {
                    foreach (var placementId in selectionManager.SelectedPlacementIds)
                    {
                        var placement = placements.FirstOrDefault(p => p.Id == placementId);
                        if (placement is null) continue;

                        var boxSize = 40f;
                        var left = (float)placement.X - boxSize / 2;
                        var top = (float)placement.Y - boxSize / 2;

                        using var paint = new SKPaint
                        {
                            Color = SKColors.Cyan,
                            StrokeWidth = (float)(2f / scale),
                            Style = SKPaintStyle.Stroke,
                            IsAntialias = true,
                        };

                        canvas.DrawRect(
                            SKRect.Create(left, top, boxSize, boxSize),
                            paint);
                    }
                }
            }

            // Draw guide selections
            if (guides is not null && guides.Count > 0)
            {
                foreach (var guideIndex in selectionManager.SelectedGuideIndices)
                {
                    if (guideIndex < 0 || guideIndex >= guides.Count) continue;
                    var guide = guides[guideIndex];

                    // Draw selected guides in yellow/gold for distinction
                    using var paint = new SKPaint
                    {
                        Color = new SKColor(255, 200, 0, 200),  // Gold
                        StrokeWidth = (float)(2f / scale),
                        Style = SKPaintStyle.Stroke,
                        IsAntialias = true,
                    };

                    if (guide.Angles.Count > 0)
                    {
                        var angle = guide.Angles[0];
                        var radians = angle * Math.PI / 180;
                        var dx = (float)Math.Cos(radians) * reach;
                        var dy = (float)Math.Sin(radians) * reach;
                        canvas.DrawLine(
                            guide.X - dx, guide.Y - dy,
                            guide.X + dx, guide.Y + dy,
                            paint);
                    }
                    else
                    {
                        // Vanishing point: draw crosshairs
                        var arm = 10f / scale;
                        canvas.DrawLine(guide.X - arm, guide.Y, guide.X + arm, guide.Y, paint);
                        canvas.DrawLine(guide.X, guide.Y - arm, guide.X, guide.Y + arm, paint);
                    }
                }
            }

            // Draw reference box selections
            if (referenceBoxes is not null && referenceBoxes.Count > 0)
            {
                foreach (var boxIndex in selectionManager.SelectedRefBoxIndices)
                {
                    if (boxIndex < 0 || boxIndex >= referenceBoxes.Count) continue;
                    var box = referenceBoxes[boxIndex];

                    using var paint = new SKPaint
                    {
                        Color = new SKColor(0, 255, 0, 200),  // Green
                        StrokeWidth = (float)(2f / scale),
                        Style = SKPaintStyle.Stroke,
                        IsAntialias = true,
                    };

                    canvas.DrawRect(
                        SKRect.Create(box.X, box.Y, box.W, box.H),
                        paint);
                }
            }

            // Draw anchor selections
            if (rigMarks is not null && rigMarks.Count > 0)
            {
                foreach (var anchorId in selectionManager.SelectedAnchorIds)
                {
                    var anchor = rigMarks.FirstOrDefault(m => m.Id == anchorId && m.Kind == RigMarkKind.Anchor);
                    if (anchor.Id is null) continue;

                    var hitRadius = 10f;
                    using var paint = new SKPaint
                    {
                        Color = new SKColor(255, 165, 0, 200),  // Orange
                        StrokeWidth = (float)(2f / scale),
                        Style = SKPaintStyle.Stroke,
                        IsAntialias = true,
                    };

                    canvas.DrawCircle((float)anchor.X, (float)anchor.Y, hitRadius, paint);
                }
            }

            // Draw collision shape selections
            if (rigMarks is not null && rigMarks.Count > 0)
            {
                foreach (var shapeId in selectionManager.SelectedShapeIds)
                {
                    var shape = rigMarks.FirstOrDefault(m => m.Id == shapeId && m.Kind == RigMarkKind.Shape);
                    if (shape.Id is null) continue;

                    using var paint = new SKPaint
                    {
                        Color = new SKColor(255, 0, 255, 200),  // Magenta
                        StrokeWidth = (float)(2f / scale),
                        Style = SKPaintStyle.Stroke,
                        IsAntialias = true,
                    };

                    canvas.DrawRect(
                        SKRect.Create((float)shape.X, (float)shape.Y, (float)shape.W, (float)shape.H),
                        paint);
                }
            }
        }

        /// <summary>
        /// The shot's framing, drawn over the world: the frame outlined, and
        /// everything outside it dimmed so the artist can see at a glance what
        /// the camera keeps. View-only chrome, exactly like the transform
        /// gizmo — it never reaches a pixel of the document.
        ///
        /// Absent unless the document has a camera. A sprite document shows no
        /// camera UI at all, which is what "optional" has to mean.
        /// </summary>
        private void DrawCameraFrame(SKCanvas canvas)
        {
            if (cameraFrame is not { Length: 4 } corners) return;
            var scale = Math.Max(0.01f, view.Scale);

            using var frame = new SKPath();
            frame.MoveTo(corners[0]);
            frame.LineTo(corners[1]);
            frame.LineTo(corners[2]);
            frame.LineTo(corners[3]);
            frame.Close();

            // Dim the world outside the frame: the document rect minus the
            // frame, even-odd. Generous bounds so the dimming still reads when
            // the camera is zoomed out past the artwork.
            var bounds = frame.Bounds;
            bounds.Union(new SKRect(0, 0, view.DocW, view.DocH));
            bounds.Inflate(bounds.Width + view.DocW, bounds.Height + view.DocH);

            using (var outside = new SKPath { FillType = SKPathFillType.EvenOdd })
            using (var dim = new SKPaint { Color = new SKColor(0, 0, 0, 110), IsAntialias = true })
            {
                outside.AddRect(bounds);
                outside.AddPath(frame);
                canvas.DrawPath(outside, dim);
            }

            // Camera orange — the same colour the timeline gives the camera
            // track, so "this is the camera" is one colour everywhere.
            using var edge = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.6f / scale,
                Color = new SKColor(0xff, 0x9f, 0x45, 240),
            };
            canvas.DrawPath(frame, edge);

            // The gizmo's handles: corner squares zoom, the top-edge grip
            // pans, and the circle pushed out from the grip rotates. Small
            // targets on purpose — everywhere else on the canvas still paints.
            using var fill = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = new SKColor(0xff, 0x9f, 0x45, 255),
            };
            using var casing = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.2f / scale,
                Color = new SKColor(0x26, 0x29, 0x2f, 220),
            };

            var half = 4f / scale;
            foreach (var corner in corners)
            {
                var box = new SKRect(corner.X - half, corner.Y - half, corner.X + half, corner.Y + half);
                canvas.DrawRect(box, fill);
                canvas.DrawRect(box, casing);
            }

            var grip = PanGrip(corners);
            var gripBox = new SKRect(grip.X - half, grip.Y - half, grip.X + half, grip.Y + half);
            canvas.DrawRect(gripBox, fill);
            canvas.DrawRect(gripBox, casing);

            var rotate = RotateHandle(corners, 26f / scale);
            canvas.DrawLine(grip, rotate, edge);
            canvas.DrawCircle(rotate, 4.5f / scale, fill);
            canvas.DrawCircle(rotate, 4.5f / scale, casing);
        }

        /// <summary>
        /// The gradient tool's axis while dragging: start, end and the line
        /// between them. The ramp itself is rendered by the view model into
        /// the live preview — this is only the handle, so the artist can see
        /// where the ends are on a ramp whose own edges are soft.
        /// </summary>
        private void DrawGradientAxis(SKCanvas canvas)
        {
            if (gradientAxis is not { } axis) return;
            var (from, to) = axis;
            var scale = Math.Max(0.01f, view.Scale);

            // Drawn twice: a dark casing under a light line, so it reads on
            // top of whatever the gradient itself is painting.
            using (var casing = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3.2f / scale,
                Color = new SKColor(0, 0, 0, 150),
            })
            {
                canvas.DrawLine(from, to, casing);
            }
            using var line = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.2f / scale,
                Color = new SKColor(0xff, 0xff, 0xff, 235),
            };
            canvas.DrawLine(from, to, line);
            canvas.DrawCircle(from, 4f / scale, line);
            canvas.DrawCircle(to, 4f / scale, line);
        }

        /// <summary>
        /// The Ctrl+T gizmo: the transformed quad with corner (and, in affine
        /// mode, edge) handles, plus the draggable pivot — cyan so it never
        /// reads as a selection or a brush cursor.
        /// </summary>
        private void DrawTransformGizmo(SKCanvas canvas)
        {
            if (txGizmo is not { } g) return;
            var scale = Math.Max(0.01f, view.Scale);
            var line = new SKColor(0x40, 0xc4, 0xd4, 235);

            using var outline = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.4f / scale,
                Color = line,
                PathEffect = SKPathEffect.CreateDash([6f / scale, 4f / scale], 0),
            };
            using var quad = new SKPath();
            quad.MoveTo(g.C0);
            quad.LineTo(g.C1);
            quad.LineTo(g.C2);
            quad.LineTo(g.C3);
            quad.Close();
            canvas.DrawPath(quad, outline);

            using var handleFill = new SKPaint { IsAntialias = true, Color = line };
            using var handleRim = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1f / scale,
                Color = new SKColor(10, 30, 34, 235),
            };
            var half = 4.5f / scale;
            SKPoint[] corners = [g.C0, g.C1, g.C2, g.C3];
            foreach (var c in corners)
            {
                var r = new SKRect(c.X - half, c.Y - half, c.X + half, c.Y + half);
                canvas.DrawRect(r, handleFill);
                canvas.DrawRect(r, handleRim);
            }
            if (!g.Perspective)
            {
                for (var i = 0; i < 4; i++)
                {
                    var a = corners[i];
                    var b = corners[(i + 1) % 4];
                    var mx = (a.X + b.X) / 2;
                    var my = (a.Y + b.Y) / 2;
                    canvas.DrawCircle(mx, my, half * 0.9f, handleFill);
                    canvas.DrawCircle(mx, my, half * 0.9f, handleRim);
                }
            }

            // Pivot: ring + crosshair, clearly grabbable.
            using var pivotPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.6f / scale,
                Color = new SKColor(0xe0, 0xa0, 0x30, 240),
            };
            var pr = 7f / scale;
            canvas.DrawCircle(g.Pivot, pr, pivotPaint);
            canvas.DrawLine(g.Pivot.X - pr * 1.6f, g.Pivot.Y, g.Pivot.X + pr * 1.6f, g.Pivot.Y, pivotPaint);
            canvas.DrawLine(g.Pivot.X, g.Pivot.Y - pr * 1.6f, g.Pivot.X, g.Pivot.Y + pr * 1.6f, pivotPaint);
        }

        /// <summary>
        /// The pulled-string ("lazy mouse") gizmo: an amber dead-zone ring
        /// around the cursor, the string, and a blue ring where paint lands —
        /// distinct from the white/black brush cursor.
        /// </summary>
        private void DrawLazyGizmo(SKCanvas canvas)
        {
            if (lazy is not { } g) return;
            var scale = Math.Max(0.01f, view.Scale);
            using var rope = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.2f / scale,
                Color = new SKColor(0xe0, 0xa0, 0x30, 170),
            };
            canvas.DrawCircle(g.CursorX, g.CursorY, g.Radius, rope);
            canvas.DrawLine(g.CursorX, g.CursorY, g.AnchorX, g.AnchorY, rope);
            using var anchor = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.6f / scale,
                Color = new SKColor(0x4a, 0x9d, 0xe0, 220),
            };
            canvas.DrawCircle(g.AnchorX, g.AnchorY, g.BrushRadius, anchor);
        }

        /// <summary>Marching ants for the selection + in-progress shapes (drawn in doc space).</summary>
        private void DrawAnts(SKCanvas canvas)
        {
            if (ants is null && antsOpen is null) return;
            var scale = Math.Max(0.01f, view.Scale);
            var dash = 4f / scale;
            using var black = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.4f / scale,
                Color = new SKColor(0, 0, 0, 230),
                PathEffect = SKPathEffect.CreateDash([dash, dash], (antsPhase + 4f) / scale),
            };
            using var white = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.4f / scale,
                Color = new SKColor(255, 255, 255, 230),
                PathEffect = SKPathEffect.CreateDash([dash, dash], antsPhase / scale),
            };
            if (ants is not null)
            {
                canvas.DrawPath(ants, black);
                canvas.DrawPath(ants, white);
            }
            if (antsOpen is not null)
            {
                canvas.DrawPath(antsOpen, black);
                canvas.DrawPath(antsOpen, white);
            }
        }

        /// <summary>
        /// The transparency checkerboard, behind the page. Drawn here rather
        /// than composited into the document, because it is a way of *seeing*
        /// the artwork, not part of it — the same reason zoom and rotation
        /// live on this side. It sits under everything, so it shows through
        /// wherever the composite has alpha, with or without a background
        /// layer.
        ///
        /// The squares are a fixed size on screen, so they stay legible as
        /// checkerboard at any zoom instead of turning into a grey haze when
        /// you zoom out or into giant tiles when you zoom in.
        /// </summary>
        private static void DrawTransparencyCheckerboard(SKCanvas canvas, ViewState view)
        {
            const float squareOnScreen = 8f;
            var square = squareOnScreen / Math.Max(0.01f, view.Scale);
            using var light = new SKPaint { Color = new SKColor(0x9a, 0x9a, 0x9a) };
            using var dark = new SKPaint { Color = new SKColor(0x77, 0x77, 0x77) };

            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, view.DocW, view.DocH));
            canvas.DrawRect(new SKRect(0, 0, view.DocW, view.DocH), light);
            var columns = (int)Math.Ceiling(view.DocW / square);
            var rows = (int)Math.Ceiling(view.DocH / square);
            for (var row = 0; row < rows; row++)
            {
                for (var col = row % 2; col < columns; col += 2)
                {
                    canvas.DrawRect(
                        new SKRect(col * square, row * square, (col + 1) * square, (row + 1) * square),
                        dark);
                }
            }
            canvas.Restore();
        }

        /// <summary>
        /// The tool cursor: the shape of the mark, outlined dark over light so it
        /// stays visible on any background.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>B74.</b> This drew two concentric circles whatever the brush was, and
        /// its own comment said so — "today the brush footprint is always a circle
        /// … new brush shapes plug in here". It had not been a circle for a long
        /// time: a tip, a roundness below 1 or an angle each make the dab something
        /// else, and the gizmo went on confidently reporting a circle.
        /// </para>
        /// <para>
        /// Three cases, in the order they cost anything: a traced tip outline, an
        /// ellipse when roundness is below 1, and the circle for the ordinary round
        /// dab — which is drawn as a circle rather than as a degenerate ellipse so
        /// the overwhelmingly common brush keeps exactly the two draw calls it had.
        /// </para>
        /// <para>
        /// <b>Outline only, and stroked rather than filled</b>, because the
        /// reporter was explicit and because a filled preview hides the artwork
        /// underneath at the moment an artist is deciding where to put a mark. The
        /// light ring is drawn inside the dark one by the stroke width, which is
        /// what makes the pair read as one edge instead of two.
        /// </para>
        /// </remarks>
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
        }
    }
}
