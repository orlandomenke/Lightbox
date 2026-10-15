using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Right-click a docker's header for every panel, and pick one to open it as a
/// tab right there (Q276).
/// </summary>
/// <remarks>
/// Three layers, tested separately for the reason the drag tests are: the
/// bookkeeping (<see cref="DockLayout.OpenInGroup"/>), what the menu offers
/// (<see cref="DockHeaderMenu"/>), and the real chrome that opens it. Green
/// bookkeeping says nothing about whether a right-click ever reaches it, which
/// is the shape B183 had.
/// </remarks>
[Collection("BrushState")]
public class DockerHeaderMenuTests(ITestOutputHelper output) : BrushStateIsolated
{
    // ---- the bookkeeping -----------------------------------------------------

    [Fact]
    public void AClosedPanelOpensAsATabInTheGroupItWasPickedFrom()
    {
        var layout = DockLayout.Default();
        Assert.False(layout.IsVisible(DockPanelId.History));

        layout.OpenInGroup(DockPanelId.History, DockPanelId.Layers);

        Assert.Equal([DockPanelId.Layers, DockPanelId.History], layout.SlotOf(DockPanelId.Layers));
        Assert.Equal(DockPanelId.History, layout.ActiveOf(layout.SlotOf(DockPanelId.Layers)));
    }

    [Fact]
    public void APanelOpenElsewhereMovesHereAndItsOldGroupClosesUp()
    {
        var layout = DockLayout.Default();

        layout.OpenInGroup(DockPanelId.Palette, DockPanelId.Layers);

        Assert.Equal([DockPanelId.Layers, DockPanelId.Palette], layout.SlotOf(DockPanelId.Layers));
        Assert.Equal(
            [DockPanelId.Color, DockPanelId.Gradient, DockPanelId.Channels],
            layout.SlotOf(DockPanelId.Color));
        // Never open twice: it is in one slot, not copied into a second.
        Assert.Single(layout.SlotsIn(DockSide.Right), s => s.Contains(DockPanelId.Palette));
    }

    [Fact]
    public void ALoneSlotMovedInLeavesNoHoleInTheStrip()
    {
        var layout = DockLayout.Default();
        var before = layout.SlotsIn(DockSide.Right).Count;

        layout.OpenInGroup(DockPanelId.Navigator, DockPanelId.Layers);

        var slots = layout.SlotsIn(DockSide.Right);
        Assert.Equal(before - 1, slots.Count);
        Assert.Equal(
            Enumerable.Range(0, slots.Count),
            slots.Select(s => layout.Place(s[0]).Order));
    }

    [Fact]
    public void PickingAMemberOfThisGroupBringsItForward()
    {
        var layout = DockLayout.Default();
        var slot = layout.SlotOf(DockPanelId.Color);

        layout.OpenInGroup(DockPanelId.Palette, DockPanelId.Color);

        Assert.Equal(slot, layout.SlotOf(DockPanelId.Color));
        Assert.Equal(DockPanelId.Palette, layout.ActiveOf(layout.SlotOf(DockPanelId.Color)));
    }

    [Fact]
    public void AFloatingPanelComesInFromItsWindow()
    {
        var layout = DockLayout.Default();
        layout.Float(DockPanelId.Reference, 100, 100, 320, 400);

        layout.OpenInGroup(DockPanelId.Reference, DockPanelId.Layers);

        Assert.Equal(DockSide.Right, layout.SideOf(DockPanelId.Reference));
        Assert.Contains(DockPanelId.Reference, layout.SlotOf(DockPanelId.Layers));
    }

    [Fact]
    public void TheTimelineDoesNotLeaveTheBottom()
    {
        var layout = DockLayout.Default();

        layout.OpenInGroup(DockPanelId.Timeline, DockPanelId.Layers);

        Assert.Equal(DockSide.Bottom, layout.SideOf(DockPanelId.Timeline));
        Assert.DoesNotContain(DockPanelId.Timeline, layout.SlotOf(DockPanelId.Layers));
    }

    /// <summary>
    /// Opening one closed panel must not rewrite where the <em>other</em>
    /// closed panels remember they were.
    /// </summary>
    /// <remarks>
    /// <see cref="DockLayout.JoinGroup"/> renumbered the slots of whatever side
    /// the joiner came from, and for a closed panel that "side" is Hidden: every
    /// closed panel's remembered strip position was closed up as if they were a
    /// strip. A panel closed from the third slot then reopened in the second.
    /// Show already went through JoinGroup for a family reopen; the menu makes
    /// it the ordinary route, so the fault is fixed with it.
    /// </remarks>
    [Fact]
    public void OpeningAClosedPanelLeavesOtherClosedPanelsWhereTheyWere()
    {
        var layout = DockLayout.Default();
        var layersOrder = layout.Place(DockPanelId.Layers).Order;
        layout.Hide(DockPanelId.Layers);

        layout.OpenInGroup(DockPanelId.History, DockPanelId.Color);

        output.WriteLine($"Layers closed at slot {layersOrder}, remembers {layout.Place(DockPanelId.Layers).Order}");
        Assert.Equal(layersOrder, layout.Place(DockPanelId.Layers).Order);
    }

    // ---- what the menu offers ------------------------------------------------

    [Fact]
    public void TheMenuListsEveryPanelAndSaysWhereEachOneIs()
    {
        var layout = DockLayout.Default();

        var items = DockHeaderMenu.Panels(layout, DockPanelId.Layers, _ => true);

        foreach (var i in items) output.WriteLine($"{i.Title}: {i.State} {i.Where}");
        Assert.Equal(HeaderMenuPanelState.InThisGroup, items.Single(i => i.Id == DockPanelId.Layers).State);
        var palette = items.Single(i => i.Id == DockPanelId.Palette);
        Assert.Equal(HeaderMenuPanelState.Elsewhere, palette.State);
        Assert.False(string.IsNullOrEmpty(palette.Where), "a panel open elsewhere has to say where, or moving it is a surprise");
        Assert.Equal(HeaderMenuPanelState.Closed, items.Single(i => i.Id == DockPanelId.History).State);
        // Grouped by what picking does (here, elsewhere, closed) and in the
        // registry's order inside each group, so it reads the same from every header.
        Assert.Equal(items.Select(i => i.State).Order(), items.Select(i => i.State));
        foreach (var state in Enum.GetValues<HeaderMenuPanelState>())
        {
            Assert.Equal(
                DockPanels.All.Select(p => p.Id).Where(id => items.Any(i => i.Id == id && i.State == state)),
                items.Where(i => i.State == state).Select(i => i.Id));
        }
    }

    [Fact]
    public void ThePanelsThatCannotComeHereAreNotOffered()
    {
        var layout = DockLayout.Default();

        var items = DockHeaderMenu.Panels(layout, DockPanelId.Layers, id => id != DockPanelId.Project);

        Assert.DoesNotContain(items, i => i.Id == DockPanelId.Timeline);
        Assert.DoesNotContain(items, i => i.Id == DockPanelId.Project);
        // And not by offering nothing at all.
        Assert.Contains(items, i => i.Id == DockPanelId.Palette);
    }

    [Fact]
    public void OnTheTimelinesOwnHeaderTheTimelineIsListedAndTicked()
    {
        var layout = DockLayout.Default();

        var items = DockHeaderMenu.Panels(layout, DockPanelId.Xsheet, _ => true);

        Assert.Equal(HeaderMenuPanelState.InThisGroup, items.Single(i => i.Id == DockPanelId.Timeline).State);
    }

    [Fact]
    public void AFloatingPanelsHeaderOffersNoPanels()
    {
        // A float holds one panel (Q278 left floating groups unbuilt), so
        // "open as a tab here" has nowhere to put the tab.
        var layout = DockLayout.Default();
        layout.Float(DockPanelId.Reference, 100, 100, 320, 400);

        Assert.Empty(DockHeaderMenu.Panels(layout, DockPanelId.Reference, _ => true));
    }

    // ---- through the real chrome ---------------------------------------------

    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, (MainViewModel)window.DataContext!);
    }

    private static Docker Docked(MainWindow w, DockPanelId id) =>
        w.FindControl<DockStrip>("RightStrip")!.Children.OfType<Docker>().First(d => d.PanelId == id);

    private static MenuItem ItemFor(ContextMenu menu, string startsWith) =>
        menu.Items.OfType<MenuItem>().First(m => m.Header is string s && s.StartsWith(startsWith, StringComparison.Ordinal));

    [AvaloniaFact]
    public void RightClickingAHeaderAsksForTheMenuNamingTheTabUnderThePointer()
    {
        var (w, _) = Open();
        var color = Docked(w, DockPanelId.Color);
        DockPanelId? asked = null;
        color.HeaderMenuRequested += (_, subject) => asked = subject;

        // Raised on the Palette tab itself, which is not the one in front: the
        // menu's Close and Float are about the tab that was clicked.
        var paletteTab = color.GetVisualDescendants().OfType<ListBoxItem>()
            .First(i => i.DataContext is DockPanelInfo { Id: DockPanelId.Palette });
        paletteTab.RaiseEvent(new ContextRequestedEventArgs());

        Assert.Equal(DockPanelId.Palette, asked);
    }

    /// <summary>
    /// A real right button, through the window's input pipeline, on a tab.
    /// </summary>
    /// <remarks>
    /// The two tests around this one raise the context request by hand, and a
    /// hand-raised event skips the step that matters most: the control under
    /// the pointer turning a right-button release into that request. The list
    /// box also takes a press on a tab for itself (B183 was that, for the left
    /// button), so the only way to know the menu is reachable is to press.
    /// </remarks>
    [AvaloniaFact]
    public void ARealRightClickOnATabReachesTheMenu()
    {
        var (w, vm) = Open();
        // With nothing open the empty-state cover sits over the whole window,
        // dockers included, and a click lands on it instead.
        vm.NewDocument(new NewDocumentSettings("Menu", 400, 300, 12, 72, "#ffffff", false));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var color = Docked(w, DockPanelId.Color);
        DockPanelId? asked = null;
        color.HeaderMenuRequested += (_, subject) => asked = subject;

        var tab = color.TabRects().First(t => t.Id == DockPanelId.Palette).Bounds;
        var toWindow = ((Avalonia.Visual)color).TransformToVisual(w)!.Value;
        var at = new Avalonia.Point(tab.X + tab.Width / 2, tab.Y + tab.Height / 2).Transform(toWindow);
        output.WriteLine($"Palette tab at {at} in the window");

        Avalonia.Headless.HeadlessWindowExtensions.MouseDown(w, at, MouseButton.Right);
        Avalonia.Headless.HeadlessWindowExtensions.MouseUp(w, at, MouseButton.Right);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(DockPanelId.Palette, asked);
        // And the press did not switch tabs on the way. It used to: the list
        // box selected Palette on the press, the slot swapped dockers under the
        // pointer, and the release — the context request — came from the strip.
        Assert.Equal(DockPanelId.Color, vm.Workspace.Layout.ActiveOf(vm.Workspace.Layout.SlotOf(DockPanelId.Color)));
    }

    [AvaloniaFact]
    public void RightClickingTheEmptyHeaderNamesThePanelInFront()
    {
        var (w, _) = Open();
        var color = Docked(w, DockPanelId.Color);
        DockPanelId? asked = null;
        color.HeaderMenuRequested += (_, subject) => asked = subject;

        var header = color.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Header");
        header.RaiseEvent(new ContextRequestedEventArgs());

        Assert.Equal(DockPanelId.Color, asked);
    }

    [AvaloniaFact]
    public void PickingAClosedPanelOpensItAsATabInThatHeader()
    {
        var (w, vm) = Open();
        var layers = Docked(w, DockPanelId.Layers);

        var menu = w.HeaderMenuFor(layers, DockPanelId.Layers);
        ItemFor(menu, "Undo history").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal([DockPanelId.Layers, DockPanelId.History], vm.Workspace.Layout.SlotOf(DockPanelId.Layers));
        Assert.True(vm.Workspace.HistoryPanelVisible, "the View menu's tick has to follow");
        var shown = Docked(w, DockPanelId.History);
        Assert.Equal(
            [DockPanelId.Layers, DockPanelId.History],
            shown.Tabs!.Select(t => t.Id));
    }

    [AvaloniaFact]
    public void TheMenuTicksThisGroupAndOffersCloseAndFloatForTheClickedTab()
    {
        var (w, _) = Open();
        var color = Docked(w, DockPanelId.Color);

        var menu = w.HeaderMenuFor(color, DockPanelId.Palette);

        foreach (var m in menu.Items.OfType<MenuItem>()) output.WriteLine($"{m.Header} checked={m.IsChecked}");
        // Ticked means open, as in View > Dockers; the suffix says where.
        Assert.Equal("Palette — here", ItemFor(menu, "Palette").Header);
        Assert.True(ItemFor(menu, "Palette").IsChecked);
        Assert.Equal("Layers — right", ItemFor(menu, "Layers").Header);
        Assert.True(ItemFor(menu, "Layers").IsChecked);
        Assert.Equal("Undo history", ItemFor(menu, "Undo history").Header);
        Assert.False(ItemFor(menu, "Undo history").IsChecked);
        // A rule between the groups (here | elsewhere | closed), then the verbs.
        Assert.Equal(3, menu.Items.OfType<Separator>().Count());
        Assert.Contains(menu.Items.OfType<MenuItem>(), m => m.Header is "Close Palette");
        Assert.Contains(menu.Items.OfType<MenuItem>(), m => m.Header is "Float Palette");
    }

    [AvaloniaFact]
    public void CloseInTheMenuClosesTheTabThatWasClicked()
    {
        var (w, vm) = Open();
        var color = Docked(w, DockPanelId.Color);

        var menu = w.HeaderMenuFor(color, DockPanelId.Palette);
        ItemFor(menu, "Close Palette").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(vm.Workspace.PaletteDockerVisible);
        Assert.True(vm.Workspace.ColorDockerVisible, "only the clicked tab closes, not the group");
    }
}
