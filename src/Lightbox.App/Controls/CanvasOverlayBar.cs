using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Lightbox.App.Docking;

namespace Lightbox.App.Controls;

/// <summary>
/// A small bar of controls floating on the canvas: a grip, the controls, a
/// collapse toggle and a close button.
/// </summary>
/// <remarks>
/// <para>
/// Not a docker, deliberately. A docker takes width away from the drawing;
/// these are three or four buttons you reach for constantly and read at a
/// glance, and giving them a strip of sidebar would be a bad trade. They can
/// be dragged to any edge of the canvas and they follow it: on a left or right
/// edge the whole bar turns a quarter turn, so its length runs along the edge
/// rather than sticking out into the drawing.
/// </para>
/// <para>
/// The control owns the drag gesture and the two toggles; it does not own
/// <em>where it is</em>. It reports a drop in canvas coordinates and the host
/// decides which edge that means, so the geometry stays in
/// <see cref="CanvasOverlayLayout"/> where it can be tested without a window.
/// </para>
/// </remarks>
public class CanvasOverlayBar : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<CanvasOverlayBar, string?>(nameof(Title));

    public static readonly StyledProperty<OverlayId> OverlayIdProperty =
        AvaloniaProperty.Register<CanvasOverlayBar, OverlayId>(nameof(OverlayId));

    public static readonly StyledProperty<bool> CollapsedProperty =
        AvaloniaProperty.Register<CanvasOverlayBar, bool>(nameof(Collapsed));

    /// <summary>
    /// True when the bar has been turned a quarter turn for a side edge. The
    /// template rotates the grip glyph back so it still reads as a grip.
    /// </summary>
    public static readonly StyledProperty<bool> IsVerticalProperty =
        AvaloniaProperty.Register<CanvasOverlayBar, bool>(nameof(IsVertical));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Which bar this is. The layout refers to bars by id, never by control.</summary>
    public OverlayId OverlayId
    {
        get => GetValue(OverlayIdProperty);
        set => SetValue(OverlayIdProperty, value);
    }

    /// <summary>Rolled up to its grip — out of the way, and still where you left it.</summary>
    public bool Collapsed
    {
        get => GetValue(CollapsedProperty);
        set => SetValue(CollapsedProperty, value);
    }

    public bool IsVertical
    {
        get => GetValue(IsVerticalProperty);
        set => SetValue(IsVerticalProperty, value);
    }

    /// <summary>Dragged to this point, in the coordinates of <see cref="DragHost"/>.</summary>
    public event Action<CanvasOverlayBar, Point>? Dropped;

    /// <summary>Live position during a drag, so the host can preview the edge.</summary>
    public event Action<CanvasOverlayBar, Point>? Dragging;

    public event Action<CanvasOverlayBar>? CloseRequested;

    /// <summary>
    /// What the drag position is measured against — the canvas host. Set by
    /// the window; without it the bar reports its own coordinates, which move
    /// with it and make a drag chase itself.
    /// </summary>
    public Visual? DragHost { get; set; }

    private bool _dragging;

    protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<Control>("PART_Grip") is { } grip)
        {
            grip.PointerPressed += OnGripPressed;
            grip.PointerMoved += OnGripMoved;
            grip.PointerReleased += OnGripReleased;
        }
        if (e.NameScope.Find<Button>("PART_Collapse") is { } collapse)
        {
            collapse.Click += (_, _) => Collapsed = !Collapsed;
        }
        if (e.NameScope.Find<Button>("PART_Close") is { } close)
        {
            close.Click += (_, _) => CloseRequested?.Invoke(this);
        }
    }

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragging = true;
        e.Pointer.Capture((IInputElement?)sender);
        e.Handled = true;
    }

    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging || DragHost is null) return;
        Dragging?.Invoke(this, e.GetPosition(DragHost));
        e.Handled = true;
    }

    private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        if (DragHost is not null) Dropped?.Invoke(this, e.GetPosition(DragHost));
        e.Handled = true;
    }
}
