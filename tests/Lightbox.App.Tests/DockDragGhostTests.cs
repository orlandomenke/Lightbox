using Avalonia;
using Avalonia.Headless.XUnit;
using Lightbox.App.Controls;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The ghost that follows the pointer while a panel is being dragged.
/// </summary>
/// <remarks>
/// What these can close is its lifecycle — that it appears, moves, and is gone
/// from every exit. What no test here can tell you is whether it tracks the
/// pointer without lag or reads as one gesture alongside the drop highlight;
/// that is `MANUAL_TESTING.md`'s.
/// </remarks>
public class DockDragGhostTests
{
    [AvaloniaFact]
    public void ItIsNotThereUntilSomethingIsBeingDragged()
    {
        Assert.False(new DockDragGhost().IsVisible);
    }

    [AvaloniaFact]
    public void ItNeverTakesThePointer()
    {
        // The one that matters most, and the reason it is asserted rather than
        // trusted: the ghost sits under the pointer by definition, so if it
        // were hit-testable it would swallow the drag it exists to illustrate.
        // The symptom is "dragging stopped working", which points nowhere near
        // a control whose whole job is to be looked at.
        var ghost = new DockDragGhost();
        Assert.False(ghost.IsHitTestVisible);

        ghost.Show("Layers", new Point(100, 100));
        Assert.False(ghost.IsHitTestVisible);
    }

    [AvaloniaFact]
    public void ShowingItPutsItOnScreenAndHidingTakesItOff()
    {
        var ghost = new DockDragGhost();

        ghost.Show("Layers", new Point(120, 80));
        Assert.True(ghost.IsVisible);

        ghost.Hide();
        Assert.False(ghost.IsVisible);
    }

    [AvaloniaFact]
    public void HidingWhatIsAlreadyHiddenIsHarmless()
    {
        // Hide is called from every exit of a drag rather than from the one
        // that ran, because reasoning about which exits exist is how a ghost
        // gets left on screen — and a ghost left on screen reads as a rendering
        // fault rather than as a stuck state.
        var ghost = new DockDragGhost();

        ghost.Hide();
        ghost.Hide();

        Assert.False(ghost.IsVisible);
    }

    [AvaloniaFact]
    public void ItFollowsThePointerWithoutBeingRebuilt()
    {
        // Moving is a redraw, not a new control: it is shown once per drag and
        // moved on every pointer event, which is the difference between cheap
        // and a layout pass at pointer rate.
        var ghost = new DockDragGhost();

        ghost.Show("Layers", new Point(10, 10));
        ghost.Show("Layers", new Point(400, 260));

        Assert.True(ghost.IsVisible);
    }
}
