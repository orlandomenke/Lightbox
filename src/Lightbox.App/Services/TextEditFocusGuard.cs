using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Lightbox.App.Services;

/// <summary>
/// Keeps a text field focused against pointer events that land inside it but
/// are routed somewhere else.
/// </summary>
/// <remarks>
/// <para>
/// <b>The report:</b> "double clicking with pen exits the text field
/// immediately … for all text fields". Every rename in the app is a label that
/// a double-click swaps for a text box — layers, folders, and the rest built
/// the same way — and with a pen the edit ended the moment it began.
/// </para>
/// <para>
/// <b>Pen alone does it; the tablet's phantom mouse only adds ways.</b>
/// Avalonia raises a double-tap on the second <em>press</em>, so the rename
/// starts while the pen is still down and captured to the label it pressed —
/// which has just been hidden. Avalonia focuses a mouse on press but a pen on
/// <em>release</em> (<c>FocusManager.CanPointerFocus</c>), and it starts from
/// the captured element: the hidden label. It walks up to the first focusable
/// ancestor, the docker row, and focuses that. The text box loses focus and
/// the rename commits and closes. A mouse focused on its press, before the
/// rename existed, which is why nobody with a mouse ever saw it. The Huion's
/// echo press (B255's phantom mouse) reaches the row by a second road — the
/// row's own press handler focuses it — and either one is enough.
/// </para>
/// <para>
/// <b>The rule: a press or release at a point inside the focused text box
/// never takes focus out of it.</b> Only events whose route has gone stale are
/// touched — the position is inside the box, the source is not. A click inside
/// the box is routed to the box and passes untouched; a click anywhere else
/// still ends the edit, which is how an artist leaves one. Marking the event
/// handled at the window, before the route reaches anything else, is what
/// keeps both Avalonia's focus walk and the row's own handler from running.
/// </para>
/// <para>
/// A class handler on every top level rather than a fix per field, for
/// <see cref="SubmenuCloseGrace"/>'s reason: it covers fields that do not exist
/// yet, in windows that do not exist yet, and there is nothing for the next
/// rename to forget.
/// </para>
/// </remarks>
public static class TextEditFocusGuard
{
    private static bool _installed;
    private static long _kept;

    /// <summary>How many misrouted events were kept from ending an edit, for diagnostics.</summary>
    internal static long Kept => Interlocked.Read(ref _kept);

    /// <summary>Guard every window in the process, once.</summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>(
            (top, e) => Guard(top, e), RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerReleasedEvent.AddClassHandler<TopLevel>(
            (top, e) => Guard(top, e), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>Where each pointer last went down, in its window's coordinates.</summary>
    /// <remarks>
    /// Keyed by pointer id: a pen and its phantom mouse are two pointers, and
    /// each one's release is judged by its own press. UI thread only.
    /// </remarks>
    private static readonly Dictionary<int, (TopLevel Top, Point At)> Presses = [];

    private static void Guard(TopLevel top, PointerEventArgs e)
    {
        if (e is PointerPressedEventArgs) Presses[e.Pointer.Id] = (top, e.GetPosition(top));
        var pressedAt = Presses.TryGetValue(e.Pointer.Id, out var p) && ReferenceEquals(p.Top, top) ? p.At : (Point?)null;
        if (e is PointerReleasedEventArgs) Presses.Remove(e.Pointer.Id);

        if (e.Handled || !ShouldKeep(top, e)) return;
        // A release is only kept when its press was inside the box too. A press
        // that began outside — on a slider beside the box, say — and was dragged
        // in before letting go belongs to whatever it pressed: swallowing its
        // release would leave that control pressed and holding the capture.
        if (e is PointerReleasedEventArgs && !PressWasInsideBox(top, pressedAt)) return;
        e.Handled = true;
        Interlocked.Increment(ref _kept);
    }

    private static bool PressWasInsideBox(TopLevel top, Point? pressedAt)
    {
        if (pressedAt is not { } at || FocusedBox(top) is not { } box) return false;
        return top.TranslatePoint(at, box) is { } local && new Rect(box.Bounds.Size).Contains(local);
    }

    private static TextBox? FocusedBox(TopLevel top)
    {
        if (top.FocusManager?.GetFocusedElement() is not Visual focused) return null;
        var box = focused as TextBox ?? focused.FindAncestorOfType<TextBox>();
        return box is { IsEffectivelyVisible: true } && box.Bounds.Width > 0 ? box : null;
    }

    /// <summary>
    /// Whether this event is inside the focused text box but routed elsewhere.
    /// </summary>
    internal static bool ShouldKeep(TopLevel top, PointerEventArgs e)
    {
        // A popup that is its own top level (a native one) is never the stale
        // route — it is somewhere else entirely.
        if (top is not Window) return false;
        // Nor is anything drawn over the box: Program turns on OverlayPopups,
        // so a menu, a dropdown or the box's own Cut/Copy/Paste menu opens in
        // the window's overlay layer, on top of the very box it belongs to. A
        // click on one of its items is inside the box's rectangle and routed
        // elsewhere — the exact shape of a stale route — and is the artist's.
        if (e.Source is Visual over && over.FindAncestorOfType<Avalonia.Controls.Primitives.OverlayLayer>(includeSelf: true) is not null)
        {
            return false;
        }
        if (FocusedBox(top) is not { } box) return false;
        if (e.Source is Visual source && (ReferenceEquals(source, box) || box.IsVisualAncestorOf(source)))
        {
            return false; // routed to the box itself: its own business
        }
        return new Rect(box.Bounds.Size).Contains(e.GetPosition(box));
    }
}
