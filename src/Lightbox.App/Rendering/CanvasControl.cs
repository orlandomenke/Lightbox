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
/// The drawing surface. Displays the composited scene (fit-to-view, centered)
/// via a Skia lease draw operation, and translates pointer input into
/// document-space paint events.
///
/// Threading: the draw op runs on Avalonia's render thread and reads only the
/// immutable <see cref="RenderSnapshot"/>. Old snapshots are retired through a
/// small ring buffer so an in-flight render never touches a disposed image.
/// </summary>
public sealed class CanvasControl : Control
{
    private RenderSnapshot? _snapshot;
    private readonly Queue<SKImage> _retired = new();
    private const int RetiredKeep = 8;

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
        context.Custom(new DrawOp(new Rect(Bounds.Size), snapshot));
    }

    // ---- view <-> document transform ---------------------------------------

    private (double Scale, double OffsetX, double OffsetY) Transform()
    {
        var snapshot = _snapshot;
        if (snapshot is null || Bounds.Width <= 0 || Bounds.Height <= 0) return (1, 0, 0);
        var scale = Math.Min(Bounds.Width / snapshot.DocWidth, Bounds.Height / snapshot.DocHeight);
        var ox = (Bounds.Width - snapshot.DocWidth * scale) / 2;
        var oy = (Bounds.Height - snapshot.DocHeight * scale) / 2;
        return (scale, ox, oy);
    }

    private (double X, double Y) ToDoc(Point p)
    {
        var (scale, ox, oy) = Transform();
        return ((p.X - ox) / scale, (p.Y - oy) / scale);
    }

    private static double PressureOf(PointerPoint pp)
    {
        // Mice report 0 or 0.5 depending on backend; treat "no real pressure"
        // as the neutral 0.5 so mouse and pen share one code path.
        var raw = pp.Properties.Pressure;
        return raw <= 0 ? 0.5 : Math.Clamp(raw, 0.0, 1.0);
    }

    // ---- pointer input ------------------------------------------------------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pp = e.GetCurrentPoint(this);
        if (!pp.Properties.IsLeftButtonPressed) return;
        e.Pointer.Capture(this);
        _painting = true;
        var (x, y) = ToDoc(pp.Position);
        PaintStarted?.Invoke(x, y, PressureOf(pp));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_painting) return;
        // Coalesced high-frequency samples, not just the latest position —
        // delivered as one batch per event.
        var points = e.GetIntermediatePoints(this);
        var samples = new List<ViewModels.MainViewModel.PointerSample>(points.Count);
        foreach (var pp in points)
        {
            var (x, y) = ToDoc(pp.Position);
            samples.Add(new ViewModels.MainViewModel.PointerSample(x, y, PressureOf(pp)));
        }
        if (samples.Count > 0) PaintMoved?.Invoke(samples);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_painting) return;
        _painting = false;
        e.Pointer.Capture(null);
        PaintEnded?.Invoke();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!_painting) return;
        _painting = false;
        PaintEnded?.Invoke();
    }

    // ---- render-thread blit -------------------------------------------------

    private sealed class DrawOp(Rect bounds, RenderSnapshot snapshot) : ICustomDrawOperation
    {
        public Rect Bounds { get; } = bounds;

        public bool HitTest(Point p) => Bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;

            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, (float)Bounds.Width, (float)Bounds.Height));
            canvas.Clear(new SKColor(0x2b, 0x2b, 0x2b));

            var scale = Math.Min(Bounds.Width / snapshot.DocWidth, Bounds.Height / snapshot.DocHeight);
            var ox = (Bounds.Width - snapshot.DocWidth * scale) / 2;
            var oy = (Bounds.Height - snapshot.DocHeight * scale) / 2;
            var dest = new SKRect(
                (float)ox,
                (float)oy,
                (float)(ox + snapshot.DocWidth * scale),
                (float)(oy + snapshot.DocHeight * scale));

            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawImage(snapshot.Image, dest, new SKSamplingOptions(SKFilterMode.Linear), paint);
            canvas.Restore();
        }
    }
}
