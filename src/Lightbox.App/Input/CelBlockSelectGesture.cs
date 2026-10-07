using Avalonia;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Input;

/// <summary>
/// The press-then-move decision behind dragging a block of cels into the
/// selection (Q207) — a plain drag across the X-sheet.
/// </summary>
/// <remarks>
/// <para>
/// <b>A plain drag selects; Alt+drag moves a cel.</b> The owner's call. It
/// swapped what a plain drag meant: before, it picked a drawing up and carried
/// it along its row, and a block could only be built a click at a time. The
/// move kept its gesture under Alt, so <see cref="CelDragGesture"/> is armed
/// only when Alt is held and this one only when it is not — the two can never
/// both claim a press.
/// </para>
/// <para>
/// The same threshold as the cel drag, for the same reason: a pen tip wobbles
/// during a click, and a click must stay a click (it moves the playhead and
/// clears the selection). Releasing, or a context menu opening, disarms it.
/// </para>
/// </remarks>
public sealed class CelBlockSelectGesture
{
    /// <summary>The cel the press landed on, while a press is pending or a drag is under way.</summary>
    public FrameCell? From { get; private set; }

    /// <summary>The press has travelled far enough to be a drag.</summary>
    public bool Selecting { get; private set; }

    private Point _from;
    private FrameCell? _last;

    /// <summary>
    /// Whether the block needs rebuilding for a move over <paramref name="over"/>:
    /// true once per cel the pointer reaches.
    /// </summary>
    /// <remarks>
    /// A move arrives per pointer event and most of them stay on one cel.
    /// Rebuilding the selection walks every cel on the sheet and tells the
    /// inspector, so doing it only when the corner changes keeps a sweep across
    /// a long scene from paying that per event.
    /// </remarks>
    public bool CornerMovedTo(FrameCell over)
    {
        if (ReferenceEquals(over, _last)) return false;
        _last = over;
        return true;
    }

    /// <summary>Arm on a plain left press. A hatched cel has no cel to start a block from.</summary>
    public void Press(FrameCell cell, Point at, bool leftButton)
    {
        if (!leftButton || cell.IsVirtual)
        {
            Cancel();
            return;
        }
        From = cell;
        _from = at;
        _last = null;
        Selecting = false;
    }

    /// <summary>
    /// Whether this move should update the block.
    /// </summary>
    /// <param name="started">True on the one move that turned the press into a drag.</param>
    /// <remarks>
    /// A move arriving with the button up means the press ended somewhere this
    /// gesture never saw, so it disarms rather than waiting.
    /// </remarks>
    public bool Moved(Point at, bool leftButton, out bool started)
    {
        started = false;
        if (From is null) return false;
        if (!leftButton)
        {
            Cancel();
            return false;
        }
        if (!Selecting)
        {
            var delta = at - _from;
            if (Math.Abs(delta.X) < CelDragGesture.ThresholdPx && Math.Abs(delta.Y) < CelDragGesture.ThresholdPx)
            {
                return false;
            }
            Selecting = true;
            started = true;
        }
        return true;
    }

    /// <summary>Disarm — a release, a context menu, anything that ends the press.</summary>
    public void Cancel()
    {
        From = null;
        _last = null;
        Selecting = false;
    }
}
