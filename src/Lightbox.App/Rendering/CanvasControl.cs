using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
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

    private bool _painting;
    private bool _panning;
    private Point _panLast;

    public void UpdateSnapshot(RenderSnapshot snapshot)
    {
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
    }

    public override void Render(DrawingContext context)
    {
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
        context.Custom(new DrawOp(new Rect(Bounds.Size), snapshot, view, cursor));
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
            e.Pointer.Capture(this);
            _painting = true;
            var (x, y) = ViewToDoc(pp.Position);
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
            if (samples.Count > 0) PaintMoved?.Invoke(samples);
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
        if (_panning)
        {
            _panning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }
        if (!_painting) return;
        _painting = false;
        e.Pointer.Capture(null);
        PaintEnded?.Invoke();
        e.Handled = true;
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

    /// <summary>
    /// Decomposed view transform for the render thread — primitive canvas ops
    /// only (translate/rotate/scale), no matrix API edge cases.
    /// </summary>
    private readonly record struct ViewState(
        float DocW, float DocH, float Scale, float RotationDeg, bool Mirrored, float CenterX, float CenterY);

    private sealed class DrawOp(Rect bounds, RenderSnapshot snapshot, ViewState view, BrushCursor? cursor) : ICustomDrawOperation
    {
        public Rect Bounds { get; } = bounds;

        public bool HitTest(Point p) => Bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
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
            canvas.Restore();

            if (cursor is { } c) DrawBrushCursor(canvas, c);
            canvas.Restore();
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
