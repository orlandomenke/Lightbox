using Avalonia;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Input;

/// <summary>
/// The press-then-move decision behind dragging a cel along its row.
/// </summary>
/// <remarks>
/// <para>
/// A few booleans and a threshold, pulled out of the window's code-behind
/// because they are a state machine and state machines are where the pointer
/// bugs live. B8 was one: with a pen, a right-click is a press-and-hold, so
/// the press armed the drag, the hold opened the context menu, and moving
/// towards "Insert frame" crossed the threshold and started a drag that seized
/// the pointer and shut the menu. A mouse right-click never arms it, which is
/// exactly why the report said a mouse was fine.
/// </para>
/// <para>
/// Two rules come out of that, and both are here rather than in a handler:
/// opening a context menu <b>cancels</b> the gesture, because a menu and a
/// drag are alternative readings of the same press and only one can win; and
/// releasing the pointer cancels it too, so an arming press can never outlive
/// the gesture that made it and go off under some later movement.
/// </para>
/// </remarks>
public sealed class CelDragGesture
{
    /// <summary>
    /// How far the pointer must travel before a press counts as a drag.
    /// </summary>
    /// <remarks>
    /// Big enough that the wobble of a pen tip during a click is not a drag,
    /// small enough that a deliberate pull feels immediate.
    /// </remarks>
    public const double ThresholdPx = 6;

    /// <summary>The cel a press has armed, or null when nothing is pending.</summary>
    public FrameCell? Candidate { get; private set; }

    /// <summary>A drag is under way; further moves are the drag's business, not ours.</summary>
    public bool Dragging { get; private set; }

    private Point _from;

    /// <summary>
    /// Arm the gesture on a press.
    /// </summary>
    /// <param name="keyed">
    /// Only a real drawing can be dragged. An empty or virtual slot has nothing
    /// to pick up, and arming on one leaves a candidate that never fires.
    /// </param>
    public void Press(FrameCell cell, Point at, bool leftButton, bool keyed)
    {
        if (!leftButton || !keyed)
        {
            Cancel();
            return;
        }
        Candidate = cell;
        _from = at;
    }

    /// <summary>
    /// Whether this move should begin the drag.
    /// </summary>
    /// <remarks>
    /// A move arriving with the button up means the press ended somewhere this
    /// gesture never saw, so it disarms rather than waiting.
    /// </remarks>
    public bool ShouldStart(FrameCell over, Point at, bool leftButton)
    {
        if (Dragging || Candidate is null || !ReferenceEquals(Candidate, over)) return false;
        if (!leftButton)
        {
            Cancel();
            return false;
        }
        var delta = at - _from;
        if (Math.Abs(delta.X) < ThresholdPx && Math.Abs(delta.Y) < ThresholdPx) return false;
        Dragging = true;
        return true;
    }

    /// <summary>The drag finished, however it finished.</summary>
    public void Finished()
    {
        Dragging = false;
        Candidate = null;
    }

    /// <summary>
    /// Disarm. A context menu opening, a pointer release, anything that means
    /// this press was not the start of a drag after all.
    /// </summary>
    public void Cancel()
    {
        if (Dragging) return; // a drag already under way owns the pointer
        Candidate = null;
    }
}
