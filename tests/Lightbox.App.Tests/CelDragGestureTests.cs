using Avalonia;
using Lightbox.App.Input;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// B8: the timeline's context submenu flickers and will not stay open under a
/// pen.
/// </summary>
/// <remarks>
/// <para>
/// Recorded as "cause: not investigated" and guessed to be a spurious leave
/// event. It was not. A pen right-click is a press-and-hold, so the press
/// armed the cel drag, the hold opened the context menu, and moving towards
/// "Insert frame" crossed the six-pixel threshold and started a drag — which
/// seized the pointer and shut the menu. A mouse right-click never arms the
/// gesture, which is exactly why the report said a mouse was fine, and the
/// detail that pins the diagnosis.
/// </para>
/// <para>
/// Testable at all because the decision moved out of the window's code-behind
/// into <see cref="CelDragGesture"/>. The handlers around it are still only
/// reachable through synthetic input, which is unreliable here; what is
/// asserted below is the rule those handlers now enforce.
/// </para>
/// </remarks>
public class CelDragGestureTests
{
    private static FrameCell Cel(int index = 0) => new(index);

    private static CelDragGesture Armed(FrameCell cell)
    {
        var g = new CelDragGesture();
        g.Press(cell, new Point(100, 20), leftButton: true, keyed: true);
        return g;
    }

    [Fact]
    public void OpeningAContextMenuCancelsThePendingDrag()
    {
        // B8. The press that opened the menu must not also be the press that
        // starts a drag.
        var cel = Cel();
        var gesture = Armed(cel);

        gesture.Cancel();

        Assert.Null(gesture.Candidate);
        Assert.False(gesture.ShouldStart(cel, new Point(160, 20), leftButton: true),
            "moving to the submenu started a drag and stole the pointer");
    }

    [Fact]
    public void WithoutTheMenuAPullStillDragsTheCel()
    {
        // The guard must not have cost the feature it guards. Dragging a cel
        // along its row is how you retime by hand.
        var cel = Cel();
        var gesture = Armed(cel);

        Assert.True(gesture.ShouldStart(cel, new Point(160, 20), leftButton: true));
        Assert.True(gesture.Dragging);
    }

    [Fact]
    public void LettingGoDisarmsTheGesture()
    {
        // The arming press used to be cleared only by a move that found the
        // button up, so lifting without moving left it armed and a much later
        // movement could set it off.
        var cel = Cel();
        var gesture = Armed(cel);

        gesture.Cancel();

        Assert.False(gesture.ShouldStart(cel, new Point(400, 20), leftButton: true));
    }

    [Fact]
    public void AWobbleUnderTheThresholdIsAClickNotADrag()
    {
        // A pen tip moves a little while you press. Without this a click on a
        // cel would sometimes retime the animation instead of selecting it.
        var cel = Cel();
        var gesture = Armed(cel);

        Assert.False(gesture.ShouldStart(cel, new Point(104, 24), leftButton: true));
        Assert.False(gesture.Dragging);
        Assert.NotNull(gesture.Candidate);   // still armed — a real pull can follow
    }

    [Fact]
    public void AMoveWithTheButtonUpDisarmsRatherThanWaits()
    {
        var cel = Cel();
        var gesture = Armed(cel);

        Assert.False(gesture.ShouldStart(cel, new Point(160, 20), leftButton: false));

        Assert.Null(gesture.Candidate);
    }

    [Fact]
    public void AnEmptySlotIsNotArmedAtAll()
    {
        // There is nothing to pick up, and arming leaves a candidate that can
        // only ever fire on the wrong thing.
        var gesture = new CelDragGesture();

        gesture.Press(Cel(), new Point(100, 20), leftButton: true, keyed: false);

        Assert.Null(gesture.Candidate);
    }

    [Fact]
    public void ARightClickAloneNeverArmsIt()
    {
        // What a mouse does, and why a mouse never had this bug.
        var gesture = new CelDragGesture();

        gesture.Press(Cel(), new Point(100, 20), leftButton: false, keyed: true);

        Assert.Null(gesture.Candidate);
    }

    [Fact]
    public void MovingOverADifferentCelDoesNotStartTheArmedOne()
    {
        var armed = Cel();
        var gesture = Armed(armed);

        Assert.False(gesture.ShouldStart(Cel(1), new Point(160, 20), leftButton: true));
    }

    [Fact]
    public void ADragAlreadyRunningIgnoresACancel()
    {
        // Once the drag owns the pointer, a stray context-request or release
        // must not pull the rug out from under it mid-gesture.
        var cel = Cel();
        var gesture = Armed(cel);
        Assert.True(gesture.ShouldStart(cel, new Point(160, 20), leftButton: true));

        gesture.Cancel();

        Assert.True(gesture.Dragging);
        gesture.Finished();
        Assert.False(gesture.Dragging);
        Assert.Null(gesture.Candidate);
    }
}
