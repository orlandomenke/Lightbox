using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
/// A docker slot collapses to its header and hands its height to the others,
/// so a crowded side keeps every group's name in view (Q277).
/// </summary>
[Collection("BrushState")]
public class DockerCollapseTests(ITestOutputHelper output) : BrushStateIsolated
{
    // ---- the record ----------------------------------------------------------

    [Fact]
    public void CollapsingASlotCollapsesEveryTabInIt()
    {
        var layout = DockLayout.Default();

        layout.SetCollapsed(DockPanelId.Palette, true);

        // A slot is one thing on screen, so it collapses as one: switching
        // tabs must not open it back up by reading another member's flag.
        Assert.All(layout.SlotOf(DockPanelId.Color), id => Assert.True(layout.IsCollapsed(id), $"{id} is not collapsed"));
        Assert.False(layout.IsCollapsed(DockPanelId.Layers));
    }

    [Fact]
    public void BringingATabForwardInACollapsedSlotOpensItBackUp()
    {
        // Clicking a tab is asking to see that panel; a header with nothing
        // under it would answer the click with nothing.
        var layout = DockLayout.Default();
        layout.SetCollapsed(DockPanelId.Color, true);
        Assert.True(layout.IsCollapsed(DockPanelId.Color));

        layout.Activate(DockPanelId.Palette);

        Assert.False(layout.IsCollapsed(DockPanelId.Color));
    }

    [Fact]
    public void OpeningAPanelIntoACollapsedGroupOpensTheGroup()
    {
        var layout = DockLayout.Default();
        layout.SetCollapsed(DockPanelId.Color, true);
        Assert.True(layout.IsCollapsed(DockPanelId.Color));

        layout.OpenInGroup(DockPanelId.History, DockPanelId.Color);

        Assert.Contains(DockPanelId.History, layout.SlotOf(DockPanelId.Color));
        Assert.False(layout.IsCollapsed(DockPanelId.History));
        Assert.False(layout.IsCollapsed(DockPanelId.Color));
    }

    [Fact]
    public void APanelDockedIntoASlotOfItsOwnArrivesOpen()
    {
        var layout = DockLayout.Default();
        layout.SetCollapsed(DockPanelId.Color, true);

        layout.Dock(DockPanelId.Palette, DockSide.Left, 0);

        Assert.False(layout.IsCollapsed(DockPanelId.Palette));
        Assert.True(layout.IsCollapsed(DockPanelId.Color), "the group it left stays as the artist set it");
    }

    [Fact]
    public void OnlyASideStripCollapses()
    {
        // The bottom strip lays its panels side by side: a collapsed one would
        // be a header standing in a full-height column of nothing.
        var layout = DockLayout.Default();

        Assert.True(layout.CanCollapse(DockPanelId.Layers));
        Assert.False(layout.CanCollapse(DockPanelId.Timeline));
        layout.SetCollapsed(DockPanelId.Timeline, true);
        Assert.False(layout.IsCollapsed(DockPanelId.Timeline));

        layout.Float(DockPanelId.Reference, 100, 100, 320, 400);
        Assert.False(layout.CanCollapse(DockPanelId.Reference));
    }

    [Fact]
    public void AnUncollapsedLayoutWritesNoCollapseKey()
    {
        // Optional means absent: a workspace nobody collapsed anything in must
        // serialize exactly as it did before the field existed.
        // The placements only: the canvas bars already write a "Collapsed" of
        // their own, so the whole file cannot answer this.
        var layout = DockLayout.Default();
        Assert.DoesNotContain("\"Collapsed\"", PlacementsOf(layout.Serialize()));

        layout.SetCollapsed(DockPanelId.Color, true);
        var json = layout.Serialize();
        Assert.Contains("\"Collapsed\"", PlacementsOf(json));

        var back = DockLayout.Deserialize(json);
        Assert.True(back.IsCollapsed(DockPanelId.Color));
        Assert.True(layout.Clone().IsCollapsed(DockPanelId.Color), "a clone that drops it loses it on the next workspace switch");
    }

    private static string PlacementsOf(string json) =>
        System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("Placements").GetRawText();

    [Fact]
    public void APanelThatLeavesACollapsedGroupForAWindowOrForClosedCarriesNoFlag()
    {
        // Invisible either way, but a stale true would be written into the
        // workspace file, where "absent unless used" is the promise.
        var layout = DockLayout.Default();
        layout.SetCollapsed(DockPanelId.Color, true);

        layout.Float(DockPanelId.Palette, 100, 100, 320, 400);
        layout.Hide(DockPanelId.Gradient);

        Assert.False(layout.Place(DockPanelId.Palette).Collapsed);
        Assert.False(layout.Place(DockPanelId.Gradient).Collapsed);
        Assert.True(layout.IsCollapsed(DockPanelId.Color), "the group they left stays as it was");
    }

    // ---- on screen -----------------------------------------------------------

    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        // With nothing open the empty-state cover sits over the dockers.
        vm.NewDocument(new NewDocumentSettings("Collapse", 400, 300, 12, 72, "#ffffff", false));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static Docker Docked(MainWindow w, DockPanelId id) =>
        w.FindControl<DockStrip>("RightStrip")!.Children.OfType<Docker>().First(d => d.PanelId == id);

    /// <summary>A point on the tab's label, just inside its left edge.</summary>
    /// <remarks>
    /// Not its centre: in a narrow strip the last tab runs on under the header's
    /// buttons, and its centre was the collapse chevron — measured, when a
    /// "tab" click kept opening a slot it was meant to bring a tab forward in.
    /// </remarks>
    private static Point TabCentre(MainWindow w, Docker docker, DockPanelId tab)
    {
        var r = docker.TabRects().First(t => t.Id == tab).Bounds;
        return new Point(r.X + Math.Min(12, r.Width / 2), r.Y + r.Height / 2).Transform(((Visual)docker).TransformToVisual(w)!.Value);
    }

    private static void Click(MainWindow w, Point at)
    {
        w.MouseDown(at, MouseButton.Left);
        w.MouseUp(at, MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void ACollapsedSlotGivesItsHeightToTheOthers()
    {
        var (w, vm) = Open();
        var layersBefore = Docked(w, DockPanelId.Layers).Bounds.Height;
        var colorBefore = Docked(w, DockPanelId.Color).Bounds.Height;

        vm.Workspace.SetCollapsed(DockPanelId.Color, true);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var color = Docked(w, DockPanelId.Color);
        var layers = Docked(w, DockPanelId.Layers);
        output.WriteLine($"Color {colorBefore:F0} -> {color.Bounds.Height:F0} (header {color.HeaderHeight:F0}); Layers {layersBefore:F0} -> {layers.Bounds.Height:F0}");
        Assert.True(color.IsCollapsed);
        // Its header and nothing else: a little slack for the frame's padding,
        // nowhere near the ChromeFloor a squeezed open docker keeps.
        Assert.True(color.Bounds.Height < color.HeaderHeight + 20,
            $"a collapsed slot kept {color.Bounds.Height:F0}px against a {color.HeaderHeight:F0}px header");
        Assert.True(layers.Bounds.Height > layersBefore + (colorBefore - color.Bounds.Height) / 4,
            "the height the collapsed slot gave up went nowhere");
    }

    [AvaloniaFact]
    public void ADoubleClickedTabCollapsesItsSlotAndASecondOneOpensIt()
    {
        var (w, vm) = Open();
        var at = TabCentre(w, Docked(w, DockPanelId.Color), DockPanelId.Color);

        // The collapse itself re-lays the strip between the two pairs, which
        // costs wall clock in a headless run — see the test below.
        Docker.DoubleClickTimeForTests = TimeSpan.FromSeconds(30);
        try
        {
            Click(w, at);
            Click(w, at);
            Assert.True(vm.Workspace.Layout.IsCollapsed(DockPanelId.Color), "a double-click on the front tab did not collapse its slot");

            at = TabCentre(w, Docked(w, DockPanelId.Color), DockPanelId.Color);
            Click(w, at);
            Click(w, at);
            Assert.False(vm.Workspace.Layout.IsCollapsed(DockPanelId.Color), "a second double-click did not open it again");

            // And a single click is only a click: no pair, no change.
            Click(w, at);
            Assert.False(vm.Workspace.Layout.IsCollapsed(DockPanelId.Color), "one click after a pair collapsed the slot");
        }
        finally
        {
            Docker.DoubleClickTimeForTests = null;
        }
    }

    [AvaloniaFact]
    public void ADoubleClickOnATabBehindTheFrontOneCollapsesWithThatTabInFront()
    {
        // The first click of the pair brings the tab forward, which swaps the
        // docker the slot shows, so the second lands on a different control —
        // the same swap that kept a right-click from ever opening the menu.
        var (w, vm) = Open();
        var at = TabCentre(w, Docked(w, DockPanelId.Color), DockPanelId.Palette);

        // Headless presses carry the wall clock at processing time, and the
        // first click's re-layout sits between them (783 ms, measured, in a
        // loaded run). What this guards is the pair surviving the swap, not
        // the system's double-click time.
        Docker.DoubleClickTimeForTests = TimeSpan.FromSeconds(30);
        try
        {
            Click(w, at);
            Click(w, at);
        }
        finally
        {
            Docker.DoubleClickTimeForTests = null;
        }

        var layout = vm.Workspace.Layout;
        Assert.Equal(DockPanelId.Palette, layout.ActiveOf(layout.SlotOf(DockPanelId.Color)));
        Assert.True(layout.IsCollapsed(DockPanelId.Color));
    }

    [AvaloniaFact]
    public void ADoubleClickOnATabBehindTheFrontOfACollapsedSlotOpensIt()
    {
        // The first click brings the tab forward, and bringing a tab forward
        // opens its slot; a plain toggle on the second click then shut it
        // again. A double-click means "the other state from the one I saw",
        // so it is judged against the slot as it was at the first click.
        //
        // A group at the top of the strip: a slot's header stays under the
        // pointer only when nothing above it changes size, and the default
        // groups lower down slide away as they open. Short titles too, so the
        // tab behind is not under the header's buttons in a narrow strip.
        var (w, vm) = Open();
        vm.Workspace.OpenInGroup(DockPanelId.Palette, DockPanelId.Navigator);
        vm.Workspace.Activate(DockPanelId.Navigator);
        vm.Workspace.SetCollapsed(DockPanelId.Navigator, true);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var at = TabCentre(w, Docked(w, DockPanelId.Navigator), DockPanelId.Palette);

        Docker.DoubleClickTimeForTests = TimeSpan.FromSeconds(30);
        try
        {
            Click(w, at);
            Click(w, at);
        }
        finally
        {
            Docker.DoubleClickTimeForTests = null;
        }

        var layout = vm.Workspace.Layout;
        Assert.Equal(DockPanelId.Palette, layout.ActiveOf(layout.SlotOf(DockPanelId.Navigator)));
        Assert.False(layout.IsCollapsed(DockPanelId.Navigator), "the slot ended collapsed after a double-click on a collapsed slot");
    }

    [AvaloniaFact]
    public void ASplitterDragWhileASlotIsCollapsedLeavesItsShareForWhenItOpens()
    {
        // Extents are star weights. Captured while a neighbour is rolled up,
        // the open panels measure larger than their share, and written back as
        // they are they would out-weigh the collapsed one's older extent — it
        // would reopen smaller than it closed, a little more each time.
        var (w, vm) = Open();
        var strip = w.FindControl<DockStrip>("RightStrip")!;
        double Share(DockPanelId id) => Docked(w, id).Bounds.Height / strip.Bounds.Height;
        var before = Share(DockPanelId.Layers);

        vm.Workspace.SetCollapsed(DockPanelId.Layers, true);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        // A drag between two open panels, let go where it started: nothing the
        // artist did should change what Layers comes back at.
        var splitter = strip.Children.OfType<GridSplitter>().First();
        splitter.RaiseEvent(new Avalonia.Input.VectorEventArgs { RoutedEvent = Avalonia.Controls.Primitives.Thumb.DragCompletedEvent });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.Workspace.SetCollapsed(DockPanelId.Layers, false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var after = Share(DockPanelId.Layers);
        output.WriteLine($"Layers share {before:P1} before, {after:P1} after");
        Assert.InRange(after, before - 0.02, before + 0.02);
    }

    [AvaloniaFact]
    public void TheChevronCollapsesAndOpensTheSlot()
    {
        var (w, vm) = Open();
        Button Chevron() => Docked(w, DockPanelId.Color).GetVisualDescendants().OfType<Button>().First(b => b.Name == "PART_Collapse");

        Chevron().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(vm.Workspace.Layout.IsCollapsed(DockPanelId.Color));

        Chevron().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(vm.Workspace.Layout.IsCollapsed(DockPanelId.Color));
    }

    [AvaloniaFact]
    public void TheBottomStripOffersNoChevron()
    {
        var (w, _) = Open();
        var timeline = w.FindControl<Docker>("TimelineDocker")!;
        Assert.False(timeline.CanCollapse);
        Assert.True(Docked(w, DockPanelId.Layers).CanCollapse, "and not by offering it nowhere");
    }

    [AvaloniaFact]
    public void BringingForwardTheTabAlreadyInFrontOpensACollapsedSlot()
    {
        // The toolbar gear "brings Tool options forward". When it is already
        // the front tab that used to be a no-op, which on a collapsed slot
        // would leave the panel the artist asked for shut.
        var (w, vm) = Open();
        vm.Workspace.Activate(DockPanelId.ToolOptions);
        vm.Workspace.SetCollapsed(DockPanelId.ToolOptions, true);
        Assert.True(vm.Workspace.Layout.IsCollapsed(DockPanelId.ToolOptions));

        vm.Workspace.Activate(DockPanelId.ToolOptions);

        Assert.False(vm.Workspace.Layout.IsCollapsed(DockPanelId.ToolOptions));
    }

    [AvaloniaFact]
    public void TheHeaderMenuCollapsesTheGroupOnASideAndNotOnTheBottom()
    {
        var (w, vm) = Open();
        var menu = w.HeaderMenuFor(Docked(w, DockPanelId.Color), DockPanelId.Color);
        var collapse = menu.Items.OfType<MenuItem>().Single(m => m.Header is "Collapse group");

        collapse.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(vm.Workspace.Layout.IsCollapsed(DockPanelId.Color));

        menu = w.HeaderMenuFor(Docked(w, DockPanelId.Color), DockPanelId.Color);
        Assert.Contains(menu.Items.OfType<MenuItem>(), m => m.Header is "Expand group");

        var bottom = w.HeaderMenuFor(w.FindControl<Docker>("TimelineDocker")!, DockPanelId.Timeline);
        Assert.DoesNotContain(bottom.Items.OfType<MenuItem>(), m => m.Header is "Collapse group" or "Expand group");
    }
}
