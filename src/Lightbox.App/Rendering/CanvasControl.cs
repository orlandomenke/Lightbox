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

    /// <summary>Update (or clear) the pulled-string anchor — where paint actually lands.</summary>
    public void SetLazyAnchor(double? x, double? y)
    {
        _lazyAnchor = x is { } ax && y is { } ay ? (ax, ay) : null;
        InvalidateVisual();
    }

    private RenderSnapshot? _snapshot;
    private readonly Queue<SKImage> _retired = new();
    private const int RetiredKeep = 8;

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

    /// <summary>Raised (on the UI thread) when an input handler fails, so the error is visible.</summary>
    public event Action<string>? CanvasError;

    // ---- diagnostics ----------------------------------------------------------
    // Drawing must survive anything; failures are logged once per context to
    // %TEMP%/lightbox-canvas.log instead of killing the input or render loop.

    private static readonly object DiagLock = new();
    private static readonly HashSet<string> DiagLogged = [];

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
    public event Action<double, double, double>? PaintStarted;

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

    public void UpdateSnapshot(RenderSnapshot snapshot)
    {
        if (Environment.GetEnvironmentVariable("LIGHTBOX_TRACE") is not null)
            Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss.fff} UpdateSnapshot");
        var old = _snapshot;
        _snapshot = snapshot;
        if (old is not null)
        {
            _retired.Enqueue(old.Image);
            while (_retired.Count > RetiredKeep)
            {
                _retired.Dequeue().Dispose();
            }
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

        context.Custom(new DrawOp(new Rect(Bounds.Size), snapshot, view, cursor, ants, openPath, _antsPhase, lazy));
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
        // Mice report 0 or 0.5 depending on backend; treat "no real pressure"
        // as the neutral 0.5 so mouse and pen share one code path.
        var raw = pp.Properties.Pressure;
        return raw <= 0 ? 0.5 : Math.Clamp(raw, 0.0, 1.0);
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
            : $"{type} input — no pressure axis (constant {PressureOf0(rawPressure):0.00})";
        if (text == _lastDiagnostic) return;
        _lastDiagnostic = text;
        InputDiagnostic.Invoke(text);
    }

    private static double PressureOf0(float raw) => raw <= 0 ? 0.5 : Math.Clamp(raw, 0f, 1f);

    // ---- view tools -----------------------------------------------------------

    private void ViewUpdated()
    {
        ViewChanged?.Invoke();
        InvalidateVisual();
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
        Focus();
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
            if (kind != PointerUpdateKind.LeftButtonPressed && !pp.Properties.IsLeftButtonPressed) return;

            var (x, y) = ViewToDoc(pp.Position);

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
            ReportInputDiagnostic(e.Pointer.Type, pp.Properties.Pressure);
            PaintStarted?.Invoke(x, y, PressureOf(pp));
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

    /// <summary>
    /// Decomposed view transform for the render thread — primitive canvas ops
    /// only (translate/rotate/scale), no matrix API edge cases.
    /// </summary>
    private readonly record struct ViewState(
        float DocW, float DocH, float Scale, float RotationDeg, bool Mirrored, float CenterX, float CenterY);

    private sealed class DrawOp(
        Rect bounds, RenderSnapshot snapshot, ViewState view, BrushCursor? cursor,
        SKPath? ants, SKPath? antsOpen, float antsPhase, LazyGizmo? lazy = null) : ICustomDrawOperation
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
            try
            {
                RenderCore(context);
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

            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, (float)Bounds.Width, (float)Bounds.Height));
            canvas.Clear(new SKColor(0x2b, 0x2b, 0x2b));

            canvas.Save();
            canvas.Translate(view.CenterX, view.CenterY);
            canvas.RotateDegrees(view.RotationDeg);
            canvas.Scale(view.Mirrored ? -view.Scale : view.Scale, view.Scale);
            canvas.Translate(-view.DocW / 2f, -view.DocH / 2f);
            using (var paint = new SKPaint { IsAntialias = true })
            {
                canvas.DrawImage(
                    snapshot.Image,
                    new SKRect(0, 0, view.DocW, view.DocH),
                    new SKSamplingOptions(SKFilterMode.Linear),
                    paint);
            }
            DrawAnts(canvas);
            DrawLazyGizmo(canvas);
            canvas.Restore();

            if (cursor is { } c) DrawBrushCursor(canvas, c);
            canvas.Restore();
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
