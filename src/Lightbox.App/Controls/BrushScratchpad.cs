using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Controls;

/// <summary>
/// The brush editor's scratchpad (Q211): a small piece of paper to try the
/// brush on while its settings are changing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not part of any document.</b> What is drawn here is never recorded,
/// never undone and never saved; it exists so a setting can be judged without
/// leaving a mark in the work. Clearing it is the only way to remove a stroke.
/// </para>
/// <para>
/// Drawn by the real engine at the brush's true size
/// (<see cref="BrushPreviewRenderer.RenderStrokes"/>), and redrawn whole when a
/// setting changes, so the strokes already on the pad show the brush as it is
/// now. The pad is a few hundred pixels across, so a full redraw is the
/// simplest correct answer and costs little.
/// </para>
/// </remarks>
public sealed class BrushScratchpad : Control
{
    private readonly List<List<StrokePoint>> _strokes = [];
    private List<StrokePoint>? _live;
    private WriteableBitmap? _picture;

    /// <summary>The settings to paint with, read each time the pad redraws.</summary>
    public Func<BrushSettings>? Settings { get; set; }

    /// <summary>The colour to paint in, read each time the pad redraws.</summary>
    public Func<string>? Colour { get; set; }

    public BrushScratchpad()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    /// <summary>How many strokes are on the pad, the live one included. For tests.</summary>
    internal int StrokeCount => _strokes.Count + (_live is null ? 0 : 1);

    /// <summary>Wipe the pad.</summary>
    public void Clear()
    {
        _strokes.Clear();
        _live = null;
        Redraw();
    }

    /// <summary>Redraw every stroke with the brush as it is now.</summary>
    public void Redraw()
    {
        var w = (int)Math.Max(8, Bounds.Width);
        var h = (int)Math.Max(8, Bounds.Height);
        if (Settings is null || Bounds.Width <= 0) return;

        var all = _live is null ? _strokes : _strokes.Append(_live);
        using var rendered = BrushPreviewRenderer.RenderStrokes(
            Settings(), all.Select(s => (IReadOnlyList<StrokePoint>)s).ToList(), Colour?.Invoke() ?? "#1a1a1a", w, h);

        if (_picture is null || _picture.PixelSize.Width != w || _picture.PixelSize.Height != h)
        {
            _picture?.Dispose();
            _picture = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Premul);
        }
        using (var target = _picture.Lock())
        {
            var bytes = rendered.Bytes;
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, target.Address, Math.Min(bytes.Length, target.RowBytes * h));
        }
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Redraw();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _live = [Point(e)];
        e.Pointer.Capture(this);
        e.Handled = true;
        Redraw();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_live is null) return;
        _live.Add(Point(e));
        Redraw();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_live is null) return;
        _strokes.Add(_live);
        _live = null;
        e.Pointer.Capture(null);
        Redraw();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_live is null) return;
        _strokes.Add(_live);
        _live = null;
    }

    private StrokePoint Point(PointerEventArgs e)
    {
        var p = e.GetCurrentPoint(this);
        // The canvas's rule (CanvasControl.PressureOf): only a pen modulates
        // pressure; a mouse or touch paints at 100%, and so does a pen that
        // reports zero. The pad has no history to promote from, so a pen's
        // zero is full here too.
        var raw = p.Properties.Pressure;
        var pressure = p.Pointer.Type == PointerType.Pen && raw > 0 ? Math.Clamp(raw, 0, 1) : 1.0;
        return new StrokePoint(p.Position.X, p.Position.Y, pressure);
    }

    public override void Render(DrawingContext context)
    {
        if (_picture is { } picture) context.DrawImage(picture, new Rect(Bounds.Size));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _picture?.Dispose();
        _picture = null;
    }
}
