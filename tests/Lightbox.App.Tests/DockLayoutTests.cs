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
        // The bottom is the timeline family (Q58): three views over one set
        // of records, tabbed, the track view in front.
        Assert.Equal(
            [DockPanelId.Timeline, DockPanelId.Xsheet, DockPanelId.GraphEditor],
            layout.PanelsIn(DockSide.Bottom));
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
        // Order as its SLOT index, so one gap turns every later drop into an
        // off-by-one. Panels tabbed into one slot share that slot's order —
        // the timeline family does, by default — so contiguity is a property
        // of slots, not of panels.
        var layout = DockLayout.Default();
        layout.Dock(DockPanelId.Palette, DockSide.Right, 5);
        layout.Dock(DockPanelId.Layers, DockSide.Left, 0);
        layout.Hide(DockPanelId.Project);

        foreach (var side in (DockSide[])[DockSide.Left, DockSide.Right, DockSide.Top, DockSide.Bottom])
        {
            var slots = layout.SlotsIn(side);
            for (var i = 0; i < slots.Count; i++)
            {
                foreach (var panel in slots[i])
                {
                    Assert.Equal(i, layout.Place(panel).Order);
                }
            }
        }
    }

    [Fact]
    public void MovingTheLastPanelOutOfAnAreaEmptiesIt()
    {
        // Which is what makes the area collapse — an empty gutter is worse
        // than no gutter. A panel alone on a side is the honest case, now
        // that the bottom starts as a family of three.
        var layout = DockLayout.Default();
        layout.Dock(DockPanelId.Palette, DockSide.Left, 0);
        Assert.False(layout.IsEmpty(DockSide.Left));

        layout.Dock(DockPanelId.Palette, DockSide.Right, 0);

        Assert.True(layout.IsEmpty(DockSide.Left));
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
    public void PanelsSharingASlotAreOneGroup()
    {
        // Swap used to live here: the switcher's "this slot shows that panel
        // instead, and the displaced one goes where it came from". With tabs a
        // slot shows several panels at once, so trading places is no longer
        // what choosing another panel means, and the operation went with it.
        var layout = DockLayout.Default();

        layout.JoinGroup(DockPanelId.Palette, DockPanelId.Color);

        var slot = layout.SlotOf(DockPanelId.Color);
        Assert.Contains(DockPanelId.Palette, slot);
        Assert.Contains(DockPanelId.Color, slot);
        Assert.Equal(
            layout.Place(DockPanelId.Color).Order,
            layout.Place(DockPanelId.Palette).Order);
    }

    [Fact]
    public void TheOneYouJustDroppedIsTheOneShowing()
    {
        // Landing behind the panel it was dropped on would read as the drop
        // having failed.
        var layout = DockLayout.Default();
        layout.JoinGroup(DockPanelId.Palette, DockPanelId.Color);

        Assert.Equal(DockPanelId.Palette, layout.ActiveOf(layout.SlotOf(DockPanelId.Color)));
        Assert.False(layout.Place(DockPanelId.Color).TabActive);
    }

    [Fact]
    public void ExactlyOneTabShowsPerSlotHoweverTheLayoutArrived()
    {
        // A file can arrive with none of a slot's members marked active, or
        // with two. Rendering nothing is a worse answer than rendering the
        // wrong tab, so the active one is derived rather than trusted.
        var layout = DockLayout.Default();
        layout.JoinGroup(DockPanelId.Palette, DockPanelId.Color);
        layout.Place(DockPanelId.Palette).TabActive = false;
        layout.Place(DockPanelId.Color).TabActive = false;

        var slot = layout.SlotOf(DockPanelId.Color);
        Assert.Contains(layout.ActiveOf(slot), slot);
    }

    [Fact]
    public void APanelIsStillInExactlyOnePlace()
    {
        // The invariant the whole model rests on, restated for groups. It is
        // enforced by Placements being keyed on the panel id rather than by any
        // guard, and grouping must not have introduced a second home.
        var layout = DockLayout.Default();
        layout.JoinGroup(DockPanelId.Palette, DockPanelId.Color);
        layout.Dock(DockPanelId.Palette, DockSide.Left, 0);

        var everywhere = Enum.GetValues<DockSide>()
            .SelectMany(side => layout.PanelsIn(side))
            .Where(id => id == DockPanelId.Palette)
            .ToList();
        Assert.Single(everywhere);
        Assert.DoesNotContain(DockPanelId.Palette, layout.SlotOf(DockPanelId.Color));
    }

    [Fact]
    public void AGroupThatLosesItsLastMemberStopsExisting()
    {
        // Nothing declares a group, so nothing has to clean one up: drag the
        // last member out and the concept evaporates with it.
        var layout = DockLayout.Default();
        layout.JoinGroup(DockPanelId.Palette, DockPanelId.Color);
        var slots = layout.SlotsIn(DockSide.Right).Count;

        layout.Dock(DockPanelId.Palette, DockSide.Left, 0);

        Assert.Single(layout.SlotOf(DockPanelId.Color));
        Assert.Equal(slots, layout.SlotsIn(DockSide.Right).Count);
    }

    [Fact]
    public void HidingAPanelLeavesItsGroupIntact()
    {
        var layout = DockLayout.Default();
        layout.JoinGroup(DockPanelId.Palette, DockPanelId.Color);

        layout.Hide(DockPanelId.Palette);

        Assert.False(layout.IsVisible(DockPanelId.Palette));
        Assert.Contains(DockPanelId.Color, layout.SlotOf(DockPanelId.Color));
        Assert.True(layout.Place(DockPanelId.Color).TabActive);
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

    [Fact]
    public void CloningALayoutKeepsTheRulersAndGuideFlags()
    {
        // Clone is on the path of both applying a workspace and saving one, so
        // a field it forgets is a setting that silently resets every time you
        // switch workspace. Nothing throws — the flags just go back to their
        // defaults, which reads as the application forgetting rather than as a
        // bug with a place to look.
        var layout = DockLayout.Default();
        layout.Rulers = true;
        layout.GuidesVisible = false;
        layout.GuidesLocked = true;

        var copy = layout.Clone();

        Assert.True(copy.Rulers);
        Assert.False(copy.GuidesVisible);
        Assert.True(copy.GuidesLocked);
    }

    [Fact]
    public void ACloneSharesNothingWithTheOriginal()
    {
        // The other half of what Clone is for. Applying a workspace hands out a
        // copy precisely so that rearranging panels does not edit the saved
        // layout under it.
        var layout = DockLayout.Default();
        var copy = layout.Clone();

        copy.Dock(DockPanelId.Color, DockSide.Left, 0);
        copy.Rulers = !layout.Rulers;

        Assert.NotEqual(copy.Place(DockPanelId.Color).Side, layout.Place(DockPanelId.Color).Side);
        Assert.NotEqual(copy.Rulers, layout.Rulers);
    }

    [Fact]
    public void ALayoutSavedBeforeTabsExistedStillLoads()
    {
        // The compatibility claim, asserted against real old JSON rather than a
        // freshly built object. Every other round-trip test here serialises
        // something this build made and reads it back, which proves the format
        // is self-consistent and says nothing about the format that shipped.
        //
        // There is no version field and no migration step anywhere, and
        // WorkspaceStore.Deserialize only recovers from JsonException — so a
        // semantically-old file does not fail, it loads as garbage. This is the
        // test that would notice.
        const string before = """
        {
          "Placements": {
            "Project": { "Side": "Right", "HomeSide": "Right", "Order": 0, "Extent": 200,
                         "FloatX": 0, "FloatY": 0, "FloatWidth": 320, "FloatHeight": 400 },
            "Layers":  { "Side": "Right", "HomeSide": "Right", "Order": 1, "Extent": 260,
                         "FloatX": 0, "FloatY": 0, "FloatWidth": 320, "FloatHeight": 400 },
            "Color":   { "Side": "Right", "HomeSide": "Right", "Order": 2, "Extent": 300,
                         "FloatX": 0, "FloatY": 0, "FloatWidth": 320, "FloatHeight": 400 },
            "Palette": { "Side": "Hidden", "HomeSide": "Hidden", "Order": 0, "Extent": 220,
                         "FloatX": 0, "FloatY": 0, "FloatWidth": 320, "FloatHeight": 400 }
          },
          "AreaExtents": { "Right": 300, "Bottom": 280 }
        }
        """;

        var layout = DockLayout.Deserialize(before);

        // Unique orders mean a slot each, which is exactly what the file meant
        // back when a slot could only hold one panel. Nothing to convert.
        var slots = layout.SlotsIn(DockSide.Right);
        Assert.Equal(3, slots.Count);
        Assert.All(slots, slot => Assert.Single(slot));

        // And every one of them shows, because TabActive defaults to true — a
        // file written before the field existed has none of them set.
        Assert.All(slots, slot => Assert.True(layout.Place(slot[0]).TabActive));

        Assert.Equal([DockPanelId.Project, DockPanelId.Layers, DockPanelId.Color],
                     layout.PanelsIn(DockSide.Right));
        Assert.False(layout.IsVisible(DockPanelId.Palette));
        Assert.Equal(300, layout.AreaExtents[DockSide.Right]);
    }

    [Fact]
    public void SizingASlotSizesEveryTabInIt()
    {
        // Extent is stored per panel, which was right when a panel was a slot.
        // With tabs the members have to agree, or switching tab resizes the
        // slot to whatever height that panel was last given on its own — the
        // layout twitching every time you look at another tab.
        var layout = DockLayout.Default();
        layout.JoinGroup(DockPanelId.Palette, DockPanelId.Color);

        layout.SetExtent(DockPanelId.Color, 333);

        Assert.Equal(333, layout.Place(DockPanelId.Color).Extent);
        Assert.Equal(333, layout.Place(DockPanelId.Palette).Extent);
    }

    [Fact]
    public void AStripIsSizedByTheTabsShowingNotTheOnesHidden()
    {
        // An uncapped panel removes the ceiling for a whole strip. Counting
        // hidden tabs would let a tucked-away Layers tab widen the sidebar for
        // a Colour panel that wants to stay narrow — a strip sized by something
        // nobody can see.
        var layout = DockLayout.Default();
        layout.Dock(DockPanelId.Color, DockSide.Left, 0);
        Assert.NotNull(layout.CapFor(DockSide.Left));   // Color is capped

        layout.JoinGroup(DockPanelId.Layers, DockPanelId.Color);   // Layers is not
        Assert.Null(layout.CapFor(DockSide.Left));                 // and it is showing

        layout.Activate(DockPanelId.Color);                        // now it is not
        Assert.NotNull(layout.CapFor(DockSide.Left));
    }
}
