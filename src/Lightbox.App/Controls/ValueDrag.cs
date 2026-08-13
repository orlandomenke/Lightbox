using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Lightbox.App.Controls;

/// <summary>
/// Drag a numeric field sideways to change its value — the scrub every other
/// tool of this kind has.
/// </summary>
/// <remarks>
/// Attached rather than a subclass so it applies to every
/// <see cref="NumericUpDown"/> in the app from one style setter, including the
/// dozens already written into the brush pages.
///
/// The interaction has to share the field with typing, which is what the
/// threshold is for: a press that never moves is a click, and the caret lands
/// where it always did. Only once the pointer has travelled far enough to
/// clearly mean it does the field become a slider, and from that point the
/// press is consumed so no caret appears when you let go.
///
/// The step comes from the field's own <see cref="NumericUpDown.Increment"/>,
/// so a 0–1 opacity and a 0–2000 brush size both feel right without either
/// being told about the other. Shift is the fine pass; Ctrl the coarse one.
/// </remarks>
public static class ValueDrag
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<NumericUpDown, bool>(
            "Enabled", typeof(ValueDrag), defaultValue: false);

    public static bool GetEnabled(NumericUpDown field) => field.GetValue(EnabledProperty);

    public static void SetEnabled(NumericUpDown field, bool value) => field.SetValue(EnabledProperty, value);

    /// <summary>Pixels of travel before a press stops being a click.</summary>
    private const double Threshold = 4;

    /// <summary>Pixels per increment at the default speed.</summary>
    private const double PixelsPerStep = 4;

    private sealed class Session
    {
        public Point Origin;
        public decimal Start;
        public bool Scrubbing;

        /// <summary>Whether the field already had focus when the press landed.</summary>
        public bool WasFocused;
    }

    private static readonly Dictionary<NumericUpDown, Session> Active = [];

    static ValueDrag()
    {
        EnabledProperty.Changed.AddClassHandler<NumericUpDown>((field, e) =>
        {
            if (e.NewValue is true) Attach(field);
            else Detach(field);
        });
    }

    private static void Attach(NumericUpDown field)
    {
        field.Cursor = new Cursor(StandardCursorType.SizeWestEast);
        field.AddHandler(InputElement.PointerPressedEvent, OnPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        field.AddHandler(InputElement.PointerMovedEvent, OnMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        field.AddHandler(InputElement.PointerReleasedEvent, OnReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        field.DetachedFromVisualTree += (_, _) => Active.Remove(field);
    }

    private static void Detach(NumericUpDown field)
    {
        field.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
        field.RemoveHandler(InputElement.PointerMovedEvent, OnMoved);
        field.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
        Active.Remove(field);
    }

    private static void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not NumericUpDown field) return;
        if (!e.GetCurrentPoint(field).Properties.IsLeftButtonPressed) return;
        Active[field] = new Session
        {
            Origin = e.GetPosition(field),
            Start = field.Value ?? 0,
            // Read before the press moves focus — this handler tunnels ahead
            // of the focus change, so it still sees the world as it was.
            WasFocused = field.IsKeyboardFocusWithin,
        };
    }

    private static void OnMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not NumericUpDown field) return;
        if (!Active.TryGetValue(field, out var session)) return;

        var dx = e.GetPosition(field).X - session.Origin.X;
        if (!session.Scrubbing)
        {
            if (Math.Abs(dx) < Threshold) return;
            session.Scrubbing = true;
            // Take the pointer so the drag survives leaving the field, which it
            // will: the useful range of a size field is far wider than 64px.
            e.Pointer.Capture(field);
        }

        var step = field.Increment == 0 ? 1m : field.Increment;
        var speed = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 0.1
            : e.KeyModifiers.HasFlag(KeyModifiers.Control) ? 10.0
            : 1.0;
        var moved = (decimal)(dx / PixelsPerStep * speed) * step;

        var value = session.Start + moved;
        if (field.Minimum != field.Maximum) value = Math.Clamp(value, field.Minimum, field.Maximum);
        field.Value = value;
        e.Handled = true;
    }

    private static void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not NumericUpDown field) return;
        if (!Active.Remove(field, out var session)) return;
        if (!session.Scrubbing)
        {
            // A click into an unfocused field selects the whole value: the
            // overwhelmingly common intent is to type a new number, and a
            // caret parked mid-digits makes that a three-step edit. Posted so
            // it lands after the click's own caret placement rather than
            // under it. A click inside a field that already has focus keeps
            // its caret — that one is somebody editing digits.
            if (!session.WasFocused && field.FindDescendantOfType<TextBox>() is { } text)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(text.SelectAll);
            }
            return;
        }
        e.Pointer.Capture(null);
        // Swallow the release so the field does not also focus and select —
        // a drag that ends with the text highlighted looks like a mistake.
        e.Handled = true;
    }
}
