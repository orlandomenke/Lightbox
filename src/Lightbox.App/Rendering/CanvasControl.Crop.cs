using Avalonia.Input;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Part of <see cref="CanvasControl"/> — the crop tool's frame: where it is,
/// what the pointer does to it, and how near counts as touching a handle.
/// </summary>
/// <remarks>
/// <para>
/// Beside <c>CanvasControl.cs</c> rather than in it, for the reason that file's
/// ratchet gives. What is left in the budgeted file is one case in each of the
/// three pointer switches, which is the registration and has nowhere else to
/// live.
/// </para>
/// <para>
/// <b>No arithmetic here either.</b> This converts pointer positions and raises
/// events; <c>CropSession</c> decides what a drag does to the rectangle and the
/// view model applies it. The canvas draws what it is told, which is what lets
/// the whole gesture be tested without a window.
/// </para>
/// </remarks>
public partial class CanvasControl
{
    private bool _cropDragging;

    /// <summary>
    /// The crop frame in document coordinates, or null when the tool is not in
    /// hand. Set by the window whenever the session changes.
    /// </summary>
    public SKRect? CropFrameRect
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            InvalidateVisual();
        }
    }

    /// <summary>The pointer took hold of the frame, in document coordinates.</summary>
    public event Action<double, double, double>? CropPressed;

    /// <summary>The pointer dragged it; the bool is Shift, the constraint key everywhere here.</summary>
    public event Action<double, double, bool>? CropDragged;

    /// <summary>The pointer let go.</summary>
    public event Action? CropReleased;

    /// <summary>
    /// How near an edge counts as being on it, in document units.
    /// </summary>
    /// <remarks>
    /// Derived from the zoom so the grab area is a constant size on screen. A
    /// fixed document tolerance would be ungrabbable zoomed out — a two-pixel
    /// target at 25% — and at 800% would swallow a small frame entirely, so
    /// every press inside it would resize rather than move.
    /// </remarks>
    private double CropTolerance => 7.0 / Math.Max(0.05, FitScale() * _zoom);

    /// <summary>The frame in surface coordinates, which is the space the draw op works in.</summary>
    /// <remarks>
    /// The canvas transform ends at the paper's own corner, so everything drawn
    /// inside it is offset by the document origin — which is zero until a crop
    /// or a left-anchored resize moves it, and is exactly the case a second crop
    /// exercises. Converted here, once, rather than in the painter, which should
    /// not have to know that two coordinate spaces exist.
    /// </remarks>
    private SKRect? CropSurfaceRect()
    {
        if (CropFrameRect is not { } r) return null;
        var origin = DocumentOrigin;
        return new SKRect(
            r.Left - origin.X, r.Top - origin.Y,
            r.Right - origin.X, r.Bottom - origin.Y);
    }

    private void CropPress(PointerPressedEventArgs e, double x, double y)
    {
        e.Pointer.Capture(this);
        _cropDragging = true;
        CropPressed?.Invoke(x, y, CropTolerance);
        e.Handled = true;
    }

    private void CropMove(PointerEventArgs e)
    {
        var (x, y) = ViewToDoc(e.GetPosition(this));
        CropDragged?.Invoke(x, y, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        e.Handled = true;
    }

    private void CropRelease(PointerReleasedEventArgs e)
    {
        _cropDragging = false;
        e.Pointer.Capture(null);
        CropReleased?.Invoke();
        e.Handled = true;
    }
}
