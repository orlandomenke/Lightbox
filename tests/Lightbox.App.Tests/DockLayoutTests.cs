using Lightbox.App.Docking;

namespace Lightbox.App.Tests;

/// <summary>
/// The workspace's bookkeeping. Docking goes wrong in the accounting long
/// before it goes wrong on screen — an order with a gap in it, an area that
/// stays open with nothing in it, a swap that loses a panel — so the model is
/// tested on its own, without a window.
/// </summary>
public class DockLayoutTests
{
    [Fact]
    public void TheDefaultLayoutOpensTheSidebarPanelsAndATimeline()
    {
        // Absent until asked for: the palette and the gradient editor are
        // things an artist sets up deliberately, and empty they are sidebar
        // height the layers could use.
        var layout = DockLayout.Default();

        Assert.Equal(
            [DockPanelId.Project, DockPanelId.Layers, DockPanelId.Color, DockPanelId.Sheets],
            layout.PanelsIn(DockSide.Right));
        Assert.Equal([DockPanelId.Timeline], layout.PanelsIn(DockSide.Bottom));
        Assert.True(layout.IsEmpty(DockSide.Left));
        Assert.True(layout.IsEmpty(DockSide.Top));
        Assert.False(layout.IsVisible(DockPanelId.Palette));
    }

    [Fact]
    public void DockingIntoAStripPutsThePanelAtTheAskedForPosition()
    {
        var layout = DockLayout.Default();
        layout.Dock(DockPanelId.Palette, DockSide.Right, 1);

        Assert.Equal(
            [DockPanelId.Project, DockPanelId.Palette, DockPanelId.Layers,
             DockPanelId.Color, DockPanelId.Sheets],
            layout.PanelsIn(DockSide.Right));
    }

    [Fact]
    public void OrdersAreAlwaysContiguousFromZero()
    {
        // Everything else relies on this: the drop resolver reads a panel's
        // Order as its index, so one gap turns every later drop into an
        // off-by-one.
        var layout = DockLayout.Default();
        layout.Dock(DockPanelId.Palette, DockSide.Right, 5);
        layout.Dock(DockPanelId.Layers, DockSide.Left, 0);
        layout.Hide(DockPanelId.Project);

        foreach (var side in (DockSide[])[DockSide.Left, DockSide.Right, DockSide.Top, DockSide.Bottom])
        {
            var panels = layout.PanelsIn(side);
            Assert.Equal(
                Enumerable.Range(0, panels.Count),
                panels.Select(p => layout.Place(p).Order));
        }
    }

    [Fact]
    public void MovingTheLastPanelOutOfAnAreaEmptiesIt()
    {
        // Which is what makes the area collapse — an empty gutter is worse
        // than no gutter.
        var layout = DockLayout.Default();
        Assert.False(layout.IsEmpty(DockSide.Bottom));

        layout.Dock(DockPanelId.Timeline, DockSide.Right, 0);

        Assert.True(layout.IsEmpty(DockSide.Bottom));
    }

    [Fact]
    public void HidingAPanelKeepsWhereItWasSoShowingItPutsItBack()
    {
        var layout = DockLayout.Default();
        layout.Dock(DockPanelId.Palette, DockSide.Left, 0);
        layout.Hide(DockPanelId.Palette);
        Assert.True(layout.IsEmpty(DockSide.Left));

        layout.Show(DockPanelId.Palette);

        Assert.Equal(DockSide.Left, layout.SideOf(DockPanelId.Palette));
    }

    [Fact]
    public void SwappingExchangesTwoPanelsPositions()
    {
        // The header's panel switcher. No panel is ever open twice, so
        // choosing one from another's header has to send that one where the
        // chosen panel came from.
        var layout = DockLayout.Default();
        layout.Dock(DockPanelId.Palette, DockSide.Left, 0);

        layout.Swap(DockPanelId.Color, DockPanelId.Palette);

        Assert.Equal(DockSide.Left, layout.SideOf(DockPanelId.Color));
        Assert.Equal(DockSide.Right, layout.SideOf(DockPanelId.Palette));
        Assert.Equal(2, layout.Place(DockPanelId.Palette).Order);   // where Color was
        Assert.Contains(DockPanelId.Palette, layout.PanelsIn(DockSide.Right));
        Assert.DoesNotContain(DockPanelId.Color, layout.PanelsIn(DockSide.Right));
    }

    [Fact]
    public void SwappingWithAHiddenPanelOpensItAndClosesTheOther()
    {
        var layout = DockLayout.Default();
        Assert.False(layout.IsVisible(DockPanelId.Gradient));

        layout.Swap(DockPanelId.Color, DockPanelId.Gradient);

        Assert.Equal(DockSide.Right, layout.SideOf(DockPanelId.Gradient));
        Assert.False(layout.IsVisible(DockPanelId.Color));
    }

    [Fact]
    public void ASidebarIsCappedByItsPanelsButAnUncappedPanelRemovesTheCeiling()
    {
        // What makes a sidebar a sidebar: sized for the controls in it, not
        // for the largest thing that ever landed there. Except that the
        // layer stack and the project tree have real use for the space.
        var layout = new DockLayout();
        layout.Dock(DockPanelId.Color, DockSide.Right, 0);
        Assert.Equal(320, layout.CapFor(DockSide.Right));

        layout.Dock(DockPanelId.Layers, DockSide.Right, 1);
        Assert.Null(layout.CapFor(DockSide.Right));
    }

    [Fact]
    public void TheTimelineIsNotDraggable()
    {
        // Its row is as long as the animation, so a 300px sidebar is a
        // placement that cannot work. Better to not offer it.
        Assert.False(DockPanels.Of(DockPanelId.Timeline).Movable);
        Assert.True(DockPanels.Of(DockPanelId.Palette).Movable);
    }

    [Fact]
    public void ALayoutRoundTripsThroughJson()
    {
        var layout = DockLayout.Default();
        layout.Dock(DockPanelId.Palette, DockSide.Left, 0);
        layout.Float(DockPanelId.Gradient, 40, 60, 300, 380);
        layout.AreaExtents[DockSide.Left] = 260;

        var back = DockLayout.Deserialize(layout.Serialize());

        Assert.Equal([DockPanelId.Palette], back.PanelsIn(DockSide.Left));
        Assert.Equal(DockSide.Floating, back.SideOf(DockPanelId.Gradient));
        Assert.Equal(300, back.Place(DockPanelId.Gradient).FloatWidth);
        Assert.Equal(260, back.AreaExtents[DockSide.Left]);
    }

    [Fact]
    public void ACorruptLayoutFallsBackRatherThanThrowing()
    {
        // A workspace file is a convenience. A broken one must not be the
        // reason the app will not open.
        Assert.Equal(
            DockLayout.Default().PanelsIn(DockSide.Right),
            DockLayout.Deserialize("{ not json at all").PanelsIn(DockSide.Right));
    }
}
