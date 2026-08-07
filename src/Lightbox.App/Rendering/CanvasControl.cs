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
public sealed class CanvasControl : Control
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
        ];
        AffectsRender<CanvasControl>(RepaintOnChange);
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
        string Id, int Kind, float X, float Y, float Spacing, IReadOnlyList<double> Angles);

    /// <summary>
    /// The guides to draw, or null for none — in which case nothing
    /// guide-related is drawn at all.
    /// </summary>
    public IReadOnlyList<GuideLine>? Guides
    {
        get => _guides;
        set
        {
            _guides = value;
            InvalidateVisual();
        }
    }

    private IReadOnlyList<GuideLine>? _guides;

    /// <summary>
    /// The rig marks to draw, or null for none — in which case no rig furniture
    /// is drawn at all.
    /// </summary>
    /// <remarks>
    /// <b>B58.</b> The one thing that was missing: <c>MainViewModel.RigMarks</c>
    /// existed, was resolved through holds, and nothing ever asked for it. A plain
    /// property with <c>InvalidateVisual</c> in the setter rather than a
    /// <c>StyledProperty</c>, following <see cref="Guides"/> — the list is pushed
    /// from the window when the view model says it changed, not bound, because it
    /// is a flattened snapshot rather than a value an artist edits.
    /// <para>
    /// Absent rather than empty when the mode is off: <c>RigMarks</c> returns an
    /// empty list when <c>RigEditMode</c> is false, so nothing here needs to know
    /// about the mode at all.
    /// </para>
    /// </remarks>
    public IReadOnlyList<RigMark>? RigMarks
    {
        get => _rigMarks;
        set
        {
            _rigMarks = value;
            InvalidateVisual();
        }
    }

    private IReadOnlyList<RigMark>? _rigMarks;

    /// <summary>
    /// Whether a press should edit the rig instead of drawing.
    /// </summary>
    /// <remarks>
    /// A mode, for the reason <c>MainViewModel.RigEditMode</c> gives: Shift, Ctrl
    /// and Alt are already spoken for on the canvas, and a fourth meaning for one
    /// of them is a chord nobody finds and everybody triggers by accident.
    /// </remarks>
    public bool RigEditMode
    {
        get => _rigEditMode;
        set
        {
            _rigEditMode = value;
            InvalidateVisual();
        }
    }

    private bool _rigEditMode;

    /// <summary>A press landed on the canvas in rig edit mode, in document space.</summary>
    /// <remarks>
    /// Void, like every other event here, so the window owns the decision. The
    /// control then learns the answer through <see cref="BeginRigDrag"/> — the
    /// alternative was a <c>Func</c> returning a hit, which would put the view
    /// model's shape into the control's signature.
    /// </remarks>
    public event Action<double, double, double>? RigPressed;

    /// <summary>A rig drag finished: the mark, the corner, and the total delta.</summary>
    /// <remarks>
    /// On release with the whole delta, not per pointer move, because
    /// <c>MainViewModel.DragRig</c> is one editor step per call — a long drag
    /// reported per event would be a hundred undo entries.
    /// </remarks>
    public event Action<string, RigCorner, double, double>? RigDragged;

    /// <summary>An empty-canvas press in rig edit mode, for whatever the window adds there.</summary>
    public event Action<double, double>? RigEmptyPressed;

    private string? _rigDragId;
    private RigCorner _rigDragCorner;
    private (double X, double Y) _rigDragStart;

    /// <summary>
    /// Start dragging a mark. Called by the window once it knows what was hit.
    /// </summary>
    public void BeginRigDrag(string id, RigCorner corner)
    {
        _rigDragId = id;
        _rigDragCorner = corner;
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
    /// A guide being pulled out of a ruler, not yet part of the document.
    /// </summary>
    /// <remarks>
    /// Chrome, not data: it exists for the length of one drag and is never
    /// written anywhere. Drawing it is the whole point of the gesture — a
    /// guide you cannot see until you let go is one you place twice.
    /// </remarks>
    public GuideLine? DraftGuide
    {
        get => _draftGuide;
        set
        {
            _draftGuide = value;
            InvalidateVisual();
        }
    }

    private GuideLine? _draftGuide;

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

    /// <summary>A guide was dragged, by a delta in document pixels.</summary>
    public event Action<string, double, double>? GuideMoved;

    /// <summary>A guide drag finished, so the move can be closed off.</summary>
    public event Action? GuideDragEnded;

    private string? _guideDrag;

    private (double X, double Y) _guideDragLast;

    /// <summary>How close, in screen pixels, counts as being on a guide.</summary>
    private const double GuideGrabPixels = 6;

    /// <summary>
    /// The guide under a view-space point, or null.
    /// </summary>
    /// <remarks>
    /// A line is grabbed anywhere along it; everything else — grids,
    /// isometric axes, vanishing points — is grabbed at its anchor. Letting a
    /// grid be grabbed on any of its lines would mean a grid covers the whole
    /// canvas in grab targets and nothing else could ever be picked up.
    /// </remarks>
    private GuideLine? GuideAt(Point view)
    {
        if (_guides is not { Count: > 0 } guides) return null;
        var scale = FitScale() * _zoom;
        if (scale <= 0) return null;
        var reach = GuideGrabPixels / scale;
        var (x, y) = ViewToDoc(view);

        GuideLine? best = null;
        var bestDistance = reach;
        foreach (var guide in guides)
        {
            double distance;
            if (guide.Kind == (int)GuideKindLine && guide.Angles.Count > 0)
            {
                var radians = guide.Angles[0] * Math.PI / 180;
                distance = Math.Abs(
                    -Math.Sin(radians) * (x - guide.X) + Math.Cos(radians) * (y - guide.Y));
            }
            else
            {
                distance = Math.Sqrt(
                    (x - guide.X) * (x - guide.X) + (y - guide.Y) * (y - guide.Y));
            }
            if (distance > bestDistance) continue;
            bestDistance = distance;
            best = guide;
        }
        return best;
    }

    private const int GuideKindLine = 0;

    /// <summary>The cursor the drawing normally uses: none, so the gizmo is it.</summary>
    private static readonly Cursor DrawingCursor = new(StandardCursorType.None);

    private static readonly Cursor GuideCursor = new(StandardCursorType.SizeAll);

    private bool _overGuide;

    private void UpdateGuideHoverCursor(Point view)
    {
        var over = GuideDragEnabled && GuideAt(view) is not null;
        if (over == _overGuide) return;
        _overGuide = over;
        Cursor = over ? GuideCursor : DrawingCursor;
    }

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
            snapshot.Image.Dispose();
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
            _retired.Dequeue().Image.Dispose();
        }
        // Hard cap, but never at the cost of freeing something in flight: an
        // image the render thread has not finished with must survive however
        // long the queue gets. Anything at or above `rendered` may still be
        // on the compositor's canvas right now.
        while (_retired.Count > RetiredHardCap
               && _retired.Peek() is { } spare
               && spare.Seq < rendered)
        {
            _retired.Dequeue().Image.Dispose();
        }
        InvalidateVisual();
        // InvalidateVisual alone is not enough: when input goes quiet right
        // after a publish (mouse released and held still), the dispatcher may
        // never wake to paint it — the stroke only appeared on the NEXT event.
        // An animation-frame request forces a compositor frame regardless.
        if (!_framePending && TopLevel.GetTopLevel(this) is { } top)
        {
            _framePending = true;
            top.RequestAnimationFrame(_ =>
            {
                _framePending = false;
                InvalidateVisual();
            });
        }
        return true;
    }

    private bool _framePending;

    public override void Render(DrawingContext context)
    {
        if (Environment.GetEnvironmentVariable("LIGHTBOX_TRACE") is not null)
            Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss.fff} Render (snapshot={_snapshot is not null})");
        var snapshot = _snapshot;
        if (snapshot is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        BrushCursor? cursor = null;
        if (_hoverPoint is { } p)
        {
            var radius = (float)Math.Max(1.0, BrushCursorSize / 2 * FitScale() * _zoom);
            cursor = new BrushCursor(
                (float)p.X, (float)p.Y, radius,
                (float)Math.Clamp(BrushCursorRoundness, 0.05, 1.0),
                (float)BrushCursorAngle,
                TipOutlinePath(BrushCursorTipId));
        }
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
            ReferenceBoxes, _newBox, Guides, _draftGuide, WithRigPreview(RigMarks),
            _selectionManager, _getPlacementsForSelection, _presented, gpuWork));
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
    /// It was added twice for the unbounded canvas and measured wrong both
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
    /// Map a view-space point to document space (exposed for tests). Never
    /// throws: a degenerate matrix (zero-sized layout) falls back to the raw
    /// point, and non-finite results are pinned to the origin.
    /// </summary>
    public (double X, double Y) ViewToDoc(Point p)
    {
        if (!ViewMatrix().TryInvert(out var inverse)) return (p.X, p.Y);
        var doc = p.Transform(inverse);
        if (!double.IsFinite(doc.X) || !double.IsFinite(doc.Y)) return (0, 0);
        return (doc.X, doc.Y);
    }

    /// <summary>Map a document point to view space.</summary>
    public (double X, double Y) DocToView(double x, double y)
    {
        var p = new Point(x, y).Transform(ViewMatrix());
        return (p.X, p.Y);
    }

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

    private static double PressureOf(PointerPoint pp)
    {
        // A mouse has no pressure axis: it paints at 100% so the stroke
        // matches the cursor gizmo exactly. Only a real pen (which reports
        // meaningful values via Windows Ink) modulates pressure.
        if (pp.Pointer.Type != PointerType.Pen) return 1.0;
        var raw = pp.Properties.Pressure;
        return raw <= 0 ? 1.0 : Math.Clamp(raw, 0.0, 1.0);
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
                case CanvasToolMode.Select:
                    if (_selectionManager is not null)
                    {
                        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

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
                    }
                    e.Handled = true;
                    return;
            }

            e.Pointer.Capture(this);
            _painting = true;
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
            _panning = false;
            ReportInputError("press", ex);
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hoverPoint = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverPoint = null;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        try
        {
            _hoverPoint = e.GetPosition(this);
            // The brush cursor must follow the pointer no matter what state
            // we're in — repaints coalesce, so this is cheap.
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
                GuideMoved?.Invoke(dragging, gx - _guideDragLast.X, gy - _guideDragLast.Y);
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
            GuideDragEnded?.Invoke();
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
        _painting = false;
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

    /// <summary>
    /// Brush cursor in view space (radius already view-scaled).
    /// </summary>
    /// <remarks>
    /// <b>B74.</b> <see cref="Outline"/> is the tip's silhouette in unit space,
    /// already traced and cached by <c>BrushTipOutline</c>, or null for a brush
    /// with no tip — where <see cref="Roundness"/> and <see cref="AngleDeg"/>
    /// describe the ellipse the engine's round dab actually is.
    /// </remarks>
    private readonly record struct BrushCursor(
        float X, float Y, float Radius,
        float Roundness = 1f,
        float AngleDeg = 0f,
        SKPath? Outline = null);

    /// <summary>Pulled-string gizmo, all in document space: dead zone around the cursor, string, anchor.</summary>
    private readonly record struct LazyGizmo(
        float AnchorX, float AnchorY, float CursorX, float CursorY, float Radius, float BrushRadius);

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
        (_snapshot is null || _snapshot.Image.Handle != IntPtr.Zero)
        && _retired.All(r => r.Image.Handle != IntPtr.Zero);

    /// <summary>How many frames are queued behind the one on screen. Tests only.</summary>
    internal int RetiredCount => _retired.Count;

    private void NoteRendered(long seq)
    {
        long current;
        do
        {
            current = Interlocked.Read(ref _lastRenderedSeq);
            if (seq <= current) return;
        }
        while (Interlocked.CompareExchange(ref _lastRenderedSeq, seq, current) != current);
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
        ViewModels.SelectionManager? selectionManager = null,
        Func<IReadOnlyList<Core.Documents.SymbolPlacement>?>? getPlacementsForSelection = null,
        PresentedFrame? presented = null,
        Action<GRContext?>? gpuWork = null) : ICustomDrawOperation
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
            var artwork = presented is null || !DurableFrameEnabled
                ? snapshot.Image
                : presented.Present(lease.GrContext, snapshot.Image, snapshot.ChangedInImage, snapshot.Seq);

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
                snapshot.DocViewport);
            DrawCameraFrame(canvas);
            DrawGradientAxis(canvas);
            // B58. Over the artwork and over the guides, under the selection ants:
            // a rig is furniture you aim with, and the reason guides sit over the
            // art (an opaque background layer) applies to sockets just as much.
            RigOverlayPainter.Paint(canvas, rigMarks, view.Scale);
            RigOverlayPainter.PaintLabels(canvas, rigMarks, view.Scale);
            DrawAnts(canvas);
            DrawLazyGizmo(canvas);
            DrawTransformGizmo(canvas);
            DrawReferenceBoxes(canvas);
            DrawObjectSelections(canvas);
            canvas.Restore();

            if (cursor is { } c) DrawBrushCursor(canvas, c);
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
            new(g.Kind, g.X, g.Y, g.Spacing, g.Angles);

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

        private void DrawObjectSelections(SKCanvas canvas)
        {
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

            using var edge = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.6f / scale,
                Color = new SKColor(0xff, 0xd0, 0x40, 240),
            };
            canvas.DrawPath(frame, edge);
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
