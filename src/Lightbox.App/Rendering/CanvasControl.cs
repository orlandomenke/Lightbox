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
    /// <summary>Current tool size in document units — drives the brush-shape cursor.</summary>
    public static readonly StyledProperty<double> BrushCursorSizeProperty =
        AvaloniaProperty.Register<CanvasControl, double>(nameof(BrushCursorSize), 6.0);

    public double BrushCursorSize
    {
        get => GetValue(BrushCursorSizeProperty);
        set => SetValue(BrushCursorSizeProperty, value);
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

    // Shift+drag brush sizing (paint modes only — Shift modifies selections elsewhere).
    private bool _resizingBrush;
    private Avalonia.Point _resizeStart;
    private double _resizeStartSize;

    /// <summary>Shift+drag on the canvas asks for a new brush size (document pixels).</summary>
    public event Action<double>? BrushResizeRequested;

    /// <summary>Alt was held when this stroke began, so it erases with the current brush.</summary>
    private bool _erasingThisStroke;

    /// <summary>True while an Alt-held stroke is in progress (drives the cursor).</summary>
    public bool IsTemporaryEraser => _painting && _erasingThisStroke;

    /// <summary>Update (or clear) the pulled-string anchor — where paint actually lands.</summary>
    public void SetLazyAnchor(double? x, double? y)
    {
        _lazyAnchor = x is { } ax && y is { } ay ? (ax, ay) : null;
        InvalidateVisual();
    }

    private RenderSnapshot? _snapshot;
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
    // %TEMP%/lightbox-canvas.log instead of killing the input or render loop.

    private static readonly object DiagLock = new();
    private static readonly HashSet<string> DiagLogged = [];

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

    private static void RecordBackend(ISkiaSharpApiLease lease)
    {
        if (GraphicsBackend != "unknown") return;
        GraphicsBackend = lease.GrContext is null ? "CPU (software)" : "GPU";
    }

    internal static void LogDiag(string context, Exception ex)
    {
        try
        {
            lock (DiagLock)
            {
                if (!DiagLogged.Add(context)) return;
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "lightbox-canvas.log"),
                    $"{DateTime.Now:O} [{context}] {ex}{Environment.NewLine}");
            }
        }
        catch
        {
            // diagnostics must never break drawing
        }
    }

    private void ReportInputError(string context, Exception ex)
    {
        LogDiag(context, ex);
        CanvasError?.Invoke($"Canvas {context} error: {ex.Message} — details in %TEMP%\\lightbox-canvas.log");
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

    public double ZoomPercent => _zoom * 100;

    public bool IsMirrored => _mirrored;

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
    /// Stroke begun: document x, y, pressure, and whether Alt was held — an
    /// Alt stroke erases with the current brush rather than switching tools.
    /// </summary>
    public event Action<double, double, double, bool>? PaintStarted;

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
    }

    public static readonly StyledProperty<CanvasToolMode> ToolModeProperty =
        AvaloniaProperty.Register<CanvasControl, CanvasToolMode>(nameof(ToolMode));

    public CanvasToolMode ToolMode
    {
        get => GetValue(ToolModeProperty);
        set => SetValue(ToolModeProperty, value);
    }

    /// <summary>Fill tool click at a document position.</summary>
    public event Action<double, double>? FillClicked;

    /// <summary>Magic-wand click at a document position (Shift=add, Alt=subtract).</summary>
    public event Action<double, double, bool, bool>? WandClicked;

    /// <summary>Eyedropper click at a document position.</summary>
    public event Action<double, double>? PickClicked;

    // ---- transform gizmo (Ctrl+T session) --------------------------------------
    // The gizmo owns the interactive state (pivot, scale, angle, offset or a
    // free quad in perspective mode); the VM owns the document side. The
    // window mediates begin/commit/cancel.

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
            snapshot.Image.Dispose();
            return false;
        }

        var old = _snapshot;
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
            cursor = new BrushCursor((float)p.X, (float)p.Y, radius);
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

        context.Custom(new DrawOp(
            new Rect(Bounds.Size), snapshot, view, cursor, ants, openPath, _antsPhase, lazy, txGizmo,
            NoteRendered, ReportFrameTime, CameraFrame));
    }

    // ---- view <-> document transform ---------------------------------------

    private double FitScale()
    {
        var snapshot = _snapshot;
        if (snapshot is null || Bounds.Width <= 0 || Bounds.Height <= 0) return 1;
        return Math.Min(Bounds.Width / snapshot.DocWidth, Bounds.Height / snapshot.DocHeight);
    }

    /// <summary>Document → view matrix: center, mirror, scale, rotate, place.</summary>
    private Matrix ViewMatrix()
    {
        var snapshot = _snapshot;
        if (snapshot is null) return Matrix.Identity;
        var s = FitScale() * _zoom;
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
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ReportDisplayScale();
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

            if (ToolMode == CanvasToolMode.Paint && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                // Shift+drag resizes the brush around the anchored cursor.
                _resizingBrush = true;
                _resizeStart = pp.Position;
                _resizeStartSize = BrushCursorSize;
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
                    FillClicked?.Invoke(x, y);
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
                    _dragShape.Clear();
                    _dragShape.Add(new Core.Documents.StrokePoint(x, y, 1));
                    e.Handled = true;
                    return;
                case CanvasToolMode.SelectRect:
                case CanvasToolMode.SelectEllipse:
                    e.Pointer.Capture(this);
                    _dragAnchor = (x, y);
                    _dragShape.Clear();
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
            ReportInputDiagnostic(e.Pointer.Type, pp.Properties.Pressure);
            ReportCursorPressure(PressureOf(pp), penDown: true);
            PaintStarted?.Invoke(x, y, PressureOf(pp), _erasingThisStroke);
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
            if (_resizingBrush)
            {
                // Horizontal drag = size, converted to document pixels; the
                // cursor stays anchored so the growing ring reads clearly.
                _hoverPoint = _resizeStart;
                var deltaView = e.GetPosition(this).X - _resizeStart.X;
                var deltaDoc = deltaView / Math.Max(0.01, FitScale() * _zoom);
                BrushResizeRequested?.Invoke(Math.Clamp(_resizeStartSize + deltaDoc, 1, 500));
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            _hoverPoint = e.GetPosition(this);
            // The brush cursor must follow the pointer no matter what state
            // we're in — repaints coalesce, so this is cheap.
            InvalidateVisual();

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
            if (_dragAnchor is { } anchor && ToolMode is CanvasToolMode.SelectRect or CanvasToolMode.SelectEllipse)
            {
                var (dx, dy) = ViewToDoc(e.GetPosition(this));
                _dragShape.Clear();
                _dragShape.AddRange(ShapeBetween(anchor, (dx, dy), ToolMode == CanvasToolMode.SelectEllipse));
                e.Handled = true;
                return;
            }

            if (!_painting) return;
            // Coalesced high-frequency samples, not just the latest position —
            // delivered as one batch per event.
            var points = e.GetIntermediatePoints(this);
            var samples = new List<ViewModels.MainViewModel.PointerSample>(points.Count);
            foreach (var pp in points)
            {
                var (x, y) = ViewToDoc(pp.Position);
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
        if (_txActive && _txDrag != TxDrag.None)
        {
            _txDrag = TxDrag.None;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }
        if (_resizingBrush)
        {
            _resizingBrush = false;
            e.Pointer.Capture(null);
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
        if (_dragShape.Count > 0 || _dragAnchor is not null)
        {
            var shape = _dragShape.ToList();
            _dragShape.Clear();
            _dragAnchor = null;
            e.Pointer.Capture(null);
            if (shape.Count >= 3)
            {
                SelectionShapeDrawn?.Invoke(
                    shape,
                    e.KeyModifiers.HasFlag(KeyModifiers.Shift),
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

    /// <summary>Brush cursor in view space (radius already view-scaled).</summary>
    private readonly record struct BrushCursor(float X, float Y, float Radius);

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
        Action<double>? onFrameTime = null, SKPoint[]? cameraFrame = null) : ICustomDrawOperation
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
            DrawTransparencyCheckerboard(canvas, view);
            using (var paint = new SKPaint { IsAntialias = true })
            {
                canvas.DrawImage(
                    snapshot.Image,
                    new SKRect(0, 0, view.DocW, view.DocH),
                    new SKSamplingOptions(SKFilterMode.Linear),
                    paint);
            }
            DrawCameraFrame(canvas);
            DrawAnts(canvas);
            DrawLazyGizmo(canvas);
            DrawTransformGizmo(canvas);
            canvas.Restore();

            if (cursor is { } c) DrawBrushCursor(canvas, c);
            canvas.Restore();
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
        /// The tool cursor: today the brush footprint is always a circle, so a
        /// dark/light double ring keeps it visible on any background. New brush
        /// shapes plug in here.
        /// </summary>
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
            canvas.DrawCircle(c.X, c.Y, c.Radius, dark);
            canvas.DrawCircle(c.X, c.Y, Math.Max(0.5f, c.Radius - 1.2f), light);
        }
    }
}
