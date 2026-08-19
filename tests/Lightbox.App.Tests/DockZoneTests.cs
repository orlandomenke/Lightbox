using Lightbox.App.Docking;

namespace Lightbox.App.Tests;

/// <summary>
/// "If I let go here, what happens." Pure arithmetic, so the cases that are
/// tedious to reach by hand — every edge, empty versus occupied, before versus
/// after each neighbour, a panel dragged out of the strip it is over — are
/// cheap to cover.
/// </summary>
public class DockZoneTests
{
    private static readonly DockRect Content = new(0, 0, 1200, 800);

    private static DropTarget? At(double x, double y, DockPanelId dragged, params PanelSlot[] slots) =>
        DockZones.Resolve(x, y, Content, slots, dragged, DockLayout.Default());

    [Fact]
    public void NearAnEmptyEdgeTheWholeAreaIsOffered()
    {
        // Nothing is there to shift, so the highlight has to stand in for the
        // strip itself — at the size the panel will actually open at.
        var drop = At(8, 400, DockPanelId.Palette);

        Assert.NotNull(drop);
        Assert.Equal(DockSide.Left, drop!.Value.Side);
        Assert.Equal(0, drop.Value.Index);
        Assert.True(drop.Value.OpensArea);
        Assert.Equal(new DockRect(0, 0, 320, 800), drop.Value.Preview);
    }

    [Theory]
    [InlineData(8, 400, DockSide.Left)]
    [InlineData(1194, 400, DockSide.Right)]
    [InlineData(600, 4, DockSide.Top)]
    [InlineData(600, 796, DockSide.Bottom)]
    public void EachEdgeIsReachable(double x, double y, DockSide expected)
    {
        Assert.Equal(expected, At(x, y, DockPanelId.Palette)!.Value.Side);
    }

    [Fact]
    public void TheMiddleOfTheCanvasIsNotADropTarget()
    {
        // Otherwise crossing the canvas keeps lighting areas up, and a drag
        // that meant to float ends up docked.
        Assert.Null(At(600, 400, DockPanelId.Palette));
    }

    [Fact]
    public void TheTimelineCannotBeDropped()
    {
        Assert.Null(At(8, 400, DockPanelId.Timeline));
    }

    // ---- dropping into a strip that already has panels ----------------------

    private static readonly PanelSlot Top = new(DockPanelId.Layers, DockSide.Right, 0, new(900, 0, 300, 400));
    private static readonly PanelSlot Bot = new(DockPanelId.Color, DockSide.Right, 1, new(900, 400, 300, 400));

    [Fact]
    public void TheTopSliverOfAPanelInsertsAboveIt()
    {
        // The halves are gone — the owner's call is that a panel's body is
        // one target, joining its tabs. Inserting into the stack lives in a
        // slim sliver at each boundary.
        var drop = At(1000, 410, DockPanelId.Palette, Top, Bot);

        Assert.Equal(DockSide.Right, drop!.Value.Side);
        Assert.Equal(1, drop.Value.Index);
        Assert.False(drop.Value.OpensArea);
        Assert.Null(drop.Value.IntoGroupOf);
    }

    [Fact]
    public void TheBottomSliverOfAPanelInsertsBelowIt()
    {
        Assert.Equal(2, At(1000, 790, DockPanelId.Palette, Top, Bot)!.Value.Index);
    }

    [Fact]
    public void ThePreviewIsABandAtTheBoundarySoTheNeighbourVisiblyMakesRoom()
    {
        // A highlight floating over the panel does not tell you what you are
        // about to get; a band the neighbour has to move for does.
        var drop = At(1000, 410, DockPanelId.Palette, Top, Bot);

        var preview = drop!.Value.Preview;
        Assert.Equal(400, preview.Y);            // the top edge of the panel below
        Assert.Equal(300, preview.Width);        // the strip's width
        Assert.Equal(200, preview.Height);       // half the neighbour, capped
    }

    [Fact]
    public void DroppingAPanelBackWhereItAlreadyIsChangesNothing()
    {
        // Removing a panel shifts everything after it up by one, so without
        // correcting for that a panel walks forward one place on every
        // no-op drag. Its own top sliver is the no-op spot now — its body
        // offers nothing, because a panel cannot join its own group.
        var drop = At(1000, 10, DockPanelId.Layers, Top, Bot);

        Assert.Equal(0, drop!.Value.Index);
    }

    [Fact]
    public void DraggingTheOnlyPanelOfAStripOverItselfOffersNothing()
    {
        var only = new PanelSlot(DockPanelId.Palette, DockSide.Left, 0, new(0, 0, 300, 800));

        Assert.Null(At(150, 400, DockPanelId.Palette, only));
    }

    [Fact]
    public void AGapBelowAShortStackAppendsToIt()
    {
        var single = new PanelSlot(DockPanelId.Layers, DockSide.Right, 0, new(900, 0, 300, 200));

        // In the empty part of the right strip, near the bottom edge.
        var drop = At(1000, 790, DockPanelId.Palette, single);

        Assert.Equal(DockSide.Bottom, drop!.Value.Side);   // the bottom edge wins here
        // …but level with the strip and away from any edge, it appends.
        var inside = At(1194, 400, DockPanelId.Palette, single);
        Assert.Equal(DockSide.Right, inside!.Value.Side);
        Assert.Equal(1, inside.Value.Index);
        Assert.False(inside.Value.OpensArea);
    }

    [Fact]
    public void ATopStripSplitsLeftToRightRatherThanTopToBottom()
    {
        // A horizontal strip's axis is horizontal. Reusing the vertical rule
        // would put every drop at index 0.
        var a = new PanelSlot(DockPanelId.Palette, DockSide.Top, 0, new(0, 0, 600, 200));
        var b = new PanelSlot(DockPanelId.Gradient, DockSide.Top, 1, new(600, 0, 600, 200));

        Assert.Equal(1, At(590, 100, DockPanelId.Color, a, b)!.Value.Index);
        Assert.Equal(1, At(610, 100, DockPanelId.Color, a, b)!.Value.Index);
        Assert.Equal(2, At(1190, 100, DockPanelId.Color, a, b)!.Value.Index);
    }

    [Fact]
    public void DroppingOnAHeaderTabsIntoThatSlot()
    {
        // The header sits inside the upper half, so it has to be tested first
        // or it is unreachable: aiming at a header would insert the panel above
        // the very slot whose tabs you were aiming for.
        var content = new DockRect(0, 0, 1000, 800);
        var slots = new List<PanelSlot>
        {
            new(DockPanelId.Layers, DockSide.Right, 0, new DockRect(700, 0, 300, 400), 22),
            new(DockPanelId.Color, DockSide.Right, 1, new DockRect(700, 400, 300, 400), 22),
        };

        var drop = DockZones.Resolve(820, 8, content, slots, DockPanelId.Palette, DockLayout.Default());

        Assert.NotNull(drop);
        Assert.Equal(DockPanelId.Layers, drop!.Value.IntoGroupOf);
        // The highlight is the WHOLE panel, per the owner: full width and
        // full height, so the offer reads as becoming part of it.
        Assert.Equal(400, drop.Value.Preview.Height);
    }

    [Fact]
    public void DroppingOnTheBodyJoinsThatSlot()
    {
        // A reversal, and the owner's own words are the spec: "the top one
        // should be the entire width (correct already) and height (incorrect)
        // of the docker", with the lower half-target removed. The body is one
        // target and it means join; inserting lives in the boundary slivers.
        var content = new DockRect(0, 0, 1000, 800);
        var slots = new List<PanelSlot>
        {
            new(DockPanelId.Layers, DockSide.Right, 0, new DockRect(700, 0, 300, 400), 22),
        };

        var drop = DockZones.Resolve(820, 300, content, slots, DockPanelId.Palette, DockLayout.Default());

        Assert.NotNull(drop);
        Assert.Equal(DockPanelId.Layers, drop!.Value.IntoGroupOf);
        Assert.Equal(new DockRect(700, 0, 300, 400), drop.Value.Preview);
    }

    [Fact]
    public void APanelCannotBeTabbedIntoItself()
    {
        var content = new DockRect(0, 0, 1000, 800);
        var slots = new List<PanelSlot>
        {
            new(DockPanelId.Layers, DockSide.Right, 0, new DockRect(700, 0, 300, 400), 22),
            new(DockPanelId.Color, DockSide.Right, 1, new DockRect(700, 400, 300, 400), 22),
        };

        var drop = DockZones.Resolve(820, 8, content, slots, DockPanelId.Layers, DockLayout.Default());

        Assert.Null(drop?.IntoGroupOf);
    }

    // ---- aiming at a position in a tab strip --------------------------------

    private static readonly DockRect Big = new(0, 0, 1000, 800);

    /// <summary>
    /// A slot whose header carries three 60px tabs, measured as the window
    /// would measure them: Layers at x=700, Color at 760, Palette at 820.
    /// </summary>
    private static PanelSlot Tabbed(DockPanelId active, int order = 0, double top = 0) =>
        new(active, DockSide.Right, order, new DockRect(700, top, 300, 400), 22,
            [
                new PanelTab(DockPanelId.Layers, new DockRect(700, top + 4, 60, 18)),
                new PanelTab(DockPanelId.Color, new DockRect(760, top + 4, 60, 18)),
                new PanelTab(DockPanelId.Palette, new DockRect(820, top + 4, 60, 18)),
            ]);

    private static DropTarget? OnHeader(double x, DockPanelId dragged, params PanelSlot[] slots) =>
        DockZones.Resolve(x, 8, Big, slots, dragged, DockLayout.Default());

    [Theory]
    // Before the first tab's midpoint, and after it: the gap the pointer is
    // nearest, not the tab it is over.
    [InlineData(705, 0)]
    [InlineData(735, 1)]
    [InlineData(795, 2)]
    [InlineData(855, 3)]
    // Past the last tab, in the empty part of the header.
    [InlineData(950, 3)]
    public void AHeaderDropChoosesThePositionBetweenTwoTabs(double x, int expected)
    {
        var drop = OnHeader(x, DockPanelId.Gradient, Tabbed(DockPanelId.Layers));

        Assert.Equal(DockPanelId.Layers, drop!.Value.IntoGroupOf);
        Assert.Equal(expected, drop.Value.TabIndex);
    }

    [Fact]
    public void ATabDroppedInItsOwnHeaderMovesToThatPosition()
    {
        // The gesture the whole thing exists for. Everywhere else a panel over
        // its own slot is no target at all — over its own tabs it means
        // something ordinary and different: put this one down further along.
        var drop = OnHeader(855, DockPanelId.Layers, Tabbed(DockPanelId.Layers));

        Assert.NotNull(drop);
        Assert.Equal(DockPanelId.Layers, drop!.Value.IntoGroupOf);
        // Two, not three: the strip it is landing in is the strip with this tab
        // already lifted out of it.
        Assert.Equal(2, drop.Value.TabIndex);
    }

    [Fact]
    public void ATabDroppedWhereItAlreadyIsStaysWhereItIs()
    {
        // And it must still be a TARGET. Resolving to null here would mean the
        // release found nothing under the pointer, and a drag that let go over
        // its own name would float the panel.
        var drop = OnHeader(715, DockPanelId.Layers, Tabbed(DockPanelId.Layers));

        Assert.NotNull(drop);
        Assert.Equal(0, drop!.Value.TabIndex);
    }

    [Fact]
    public void TheCaretMarksTheGapUnderThePointerRatherThanTheCorrectedIndex()
    {
        // Two indices that are not the same number: the caret is drawn between
        // the tabs as they stand, the index describes the strip with the
        // dragged tab lifted out. Drawing the corrected one would put the mark
        // a whole tab away from where the artist is aiming.
        var drop = OnHeader(855, DockPanelId.Layers, Tabbed(DockPanelId.Layers));

        Assert.Equal(2, drop!.Value.TabIndex);
        var caret = drop.Value.Caret;
        Assert.NotNull(caret);
        // Past the last tab, so the mark sits on its right edge — 880, not 820.
        Assert.Equal(880, caret!.Value.X + DockZones.CaretWidth / 2, 3);
        Assert.Equal(18, caret.Value.Height);
    }

    [Fact]
    public void TheCaretSitsOnTheBoundaryItNames()
    {
        var drop = OnHeader(795, DockPanelId.Gradient, Tabbed(DockPanelId.Layers));

        // Between Color and Palette: the left edge of Palette's tab.
        Assert.Equal(820, drop!.Value.Caret!.Value.X + DockZones.CaretWidth / 2, 3);
    }

    [Fact]
    public void RearrangingIsOfferedEvenWhenTheSlotIsTheOnlyOneOnItsSide()
    {
        // The bottom edge is one slot holding the whole timeline family, so the
        // "nowhere to go" shortcut would take the X-sheet's tab out of reach of
        // the only rearrangement it has.
        var only = new PanelSlot(
            DockPanelId.Xsheet, DockSide.Bottom, 0, new DockRect(0, 600, 1000, 200), 22,
            [
                new PanelTab(DockPanelId.Timeline, new DockRect(0, 604, 60, 18)),
                new PanelTab(DockPanelId.Xsheet, new DockRect(60, 604, 60, 18)),
            ]);

        var drop = DockZones.Resolve(10, 608, Big, [only], DockPanelId.Xsheet, DockLayout.Default());

        Assert.Equal(DockPanelId.Xsheet, drop!.Value.IntoGroupOf);
        Assert.Equal(0, drop.Value.TabIndex);
    }

    [Fact]
    public void TheBottomEdgeOfATabIsStillTheTabStrip()
    {
        // Measured, not supposed: a realised tab is 24px tall from y=4 under a
        // header that measures 27, so its last row of pixels is outside the
        // band. Letting go there meant "join, last" instead of the position
        // under the pointer, on the row an artist aims at when they aim at the
        // bottom of a word.
        var overhanging = new PanelSlot(
            DockPanelId.Layers, DockSide.Right, 0, new DockRect(700, 0, 300, 400), 27,
            [
                new PanelTab(DockPanelId.Layers, new DockRect(700, 4, 60, 24)),
                new PanelTab(DockPanelId.Color, new DockRect(760, 4, 60, 24)),
            ]);

        var drop = DockZones.Resolve(
            765, 27.5, Big, [overhanging], DockPanelId.Gradient, DockLayout.Default());

        Assert.Equal(DockPanelId.Layers, drop!.Value.IntoGroupOf);
        Assert.Equal(1, drop.Value.TabIndex);
    }

    [Fact]
    public void ATabBehindAnotherIsStillRearrangedRatherThanRejoined()
    {
        // A press selects the tab it lands on and the layout hears about it a
        // dispatcher turn later, so a quick pull can resolve against a slot
        // whose active panel is still the previous one. What decides is whether
        // the dragged panel is IN this group, not whether it is its face.
        var drop = OnHeader(715, DockPanelId.Palette, Tabbed(DockPanelId.Layers));

        Assert.NotNull(drop);
        // The header alone: nothing about where the panel sits is changing.
        Assert.Equal(new DockRect(700, 0, 300, 22), drop!.Value.Preview);
        Assert.Equal(0, drop.Value.TabIndex);
    }

    [Fact]
    public void ALoneTabOverItsOwnHeaderIsStillNoTarget()
    {
        // Nothing to sit beside, so the drop means what it always meant: let go
        // over nothing and the panel floats.
        var lone = new PanelSlot(
            DockPanelId.Layers, DockSide.Right, 0, new DockRect(700, 0, 300, 400), 22,
            [new PanelTab(DockPanelId.Layers, new DockRect(700, 4, 60, 18))]);

        Assert.Null(DockZones.Resolve(730, 8, Big, [lone], DockPanelId.Layers, DockLayout.Default()));
    }

    [Fact]
    public void ADropOnTheBodyNamesNoPositionAtAll()
    {
        // A gesture that never went near the strip cannot mean a place in it,
        // and last is the only honest reading.
        var drop = DockZones.Resolve(
            820, 300, Big, [Tabbed(DockPanelId.Layers)], DockPanelId.Gradient, DockLayout.Default());

        Assert.Equal(DockPanelId.Layers, drop!.Value.IntoGroupOf);
        Assert.Null(drop.Value.TabIndex);
        Assert.Null(drop.Value.Caret);
    }

    [Fact]
    public void AnUnmeasuredHeaderStillJoinsTheGroupItAlwaysDid()
    {
        // No tab rectangles — the containers were not realised — so the header
        // cannot say where in itself anything goes. It must not stop meaning
        // "join this group", which is what it meant before positions existed.
        var slot = new PanelSlot(DockPanelId.Layers, DockSide.Right, 0, new DockRect(700, 0, 300, 400), 22);

        var drop = DockZones.Resolve(820, 8, Big, [slot], DockPanelId.Gradient, DockLayout.Default());

        Assert.Equal(DockPanelId.Layers, drop!.Value.IntoGroupOf);
        Assert.Null(drop.Value.TabIndex);
    }

    [Fact]
    public void AHeaderDropOnAnotherSlotStillWashesTheWholePanel()
    {
        // The position is new; what the highlight says is not. The wash names
        // the panel being joined and the caret names the place in its strip —
        // one drop, two facts, and neither replaces the other.
        var drop = OnHeader(795, DockPanelId.Gradient, Tabbed(DockPanelId.Layers));

        Assert.Equal(new DockRect(700, 0, 300, 400), drop!.Value.Preview);
        Assert.NotNull(drop.Value.Caret);
    }

    [Fact]
    public void RearrangingInsideOneHeaderWashesOnlyTheHeader()
    {
        // Nothing about where the panel sits is changing, and a wash over the
        // whole body would say it was.
        var drop = OnHeader(855, DockPanelId.Layers, Tabbed(DockPanelId.Layers));

        Assert.Equal(new DockRect(700, 0, 300, 22), drop!.Value.Preview);
    }

    [Fact]
    public void AHeaderlessSlotOffersNoTabTarget()
    {
        // HeaderHeight of zero means the caller could not measure one. Offering
        // a tab drop against a band of no height would be a target you cannot
        // aim at, which is worse than not offering it.
        var content = new DockRect(0, 0, 1000, 800);
        var slots = new List<PanelSlot>
        {
            new(DockPanelId.Layers, DockSide.Right, 0, new DockRect(700, 0, 300, 400)),
        };

        var drop = DockZones.Resolve(820, 2, content, slots, DockPanelId.Palette, DockLayout.Default());

        Assert.Null(drop?.IntoGroupOf);
    }
}
