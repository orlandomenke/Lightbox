using Avalonia.Input;

namespace Lightbox.App.Rendering;

/// <summary>
/// The pose guide on the canvas (Q274): the overlay pushed from the window,
/// and, while the guide is being edited, a press on a joint drags it.
/// </summary>
public partial class CanvasControl
{
    /// <summary>The pose guide to draw over the frame, or null: pushed from the window like <see cref="LineMatch"/>.</summary>
    public ViewModels.PoseGuideOverlay? PoseGuide
    {
        get => _poseGuide;
        set
        {
            if (ReferenceEquals(_poseGuide, value)) return;
            _poseGuide = value;
            InvalidateVisual();
        }
    }

    private ViewModels.PoseGuideOverlay? _poseGuide;

    /// <summary>
    /// Whether a press may take a joint of the guide. A mode, so drawing near a
    /// joint never drags it by accident.
    /// </summary>
    public bool PoseEditMode { get; set; }

    /// <summary>Asked on a press while editing: document x, y and the view scale; true when a joint was taken.</summary>
    public Func<double, double, double, bool>? PoseHit { get; set; }

    /// <summary>A taken joint follows the pointer, in document units.</summary>
    public event Action<double, double>? PoseDragged;

    /// <summary>A taken joint is let go, in document units.</summary>
    public event Action<double, double>? PoseReleased;

    private bool _poseDragging;

    /// <summary>A press while editing the guide: true when it took a joint.</summary>
    private bool PressPose(double x, double y, PointerPressedEventArgs e)
    {
        if (!PoseEditMode || PoseHit?.Invoke(x, y, FitScale() * _zoom) != true) return false;
        _poseDragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
        return true;
    }

    /// <summary>A move while a joint is taken: true when it was this one.</summary>
    private bool MovePose(PointerEventArgs e)
    {
        if (!_poseDragging) return false;
        var (x, y) = ViewToDoc(e.GetPosition(this));
        PoseDragged?.Invoke(x, y);
        e.Handled = true;
        return true;
    }

    /// <summary>The release of a taken joint: true when it was this one.</summary>
    private bool ReleasePose(PointerReleasedEventArgs e)
    {
        if (!_poseDragging) return false;
        _poseDragging = false;
        var (x, y) = ViewToDoc(e.GetPosition(this));
        e.Pointer.Capture(null);
        PoseReleased?.Invoke(x, y);
        e.Handled = true;
        return true;
    }
}
