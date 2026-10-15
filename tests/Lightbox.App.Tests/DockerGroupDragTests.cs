using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Drag a whole tab group by the empty header beside its tabs (Q278), the way
/// Photoshop moves a panel group by its title bar.
/// </summary>
/// <remarks>
/// The tab stays the grip for one panel (B155). The bare header was made inert
/// because it tore single panels out; Q278 gives it back as the grip for the
/// group, which is the one thing four tabs dragged one at a time cannot do.
/// </remarks>
[Collection("BrushState")]
public class DockerGroupDragTests(ITestOutputHelper output) : BrushStateIsolated
{
    // ---- the record ----------------------------------------------------------

    [Fact]
    public void DockingAGroupMovesEveryTabInItsOrderWithTheSameOneInFront()
    {
        var layout = DockLayout.Default();
        layout.Activate(DockPanelId.Palette);

        layout.DockGroup(DockPanelId.Palette, DockSide.Left, 0);

        Assert.Equal(
            [[DockPanelId.Color, DockPanelId.Palette, DockPanelId.Gradient, DockPanelId.Channels]],
            layout.SlotsIn(DockSide.Left));
        Assert.Equal(DockPanelId.Palette, layout.ActiveOf(layout.SlotOf(DockPanelId.Color)));
        Assert.DoesNotContain(DockPanelId.Color, layout.PanelsIn(DockSide.Right));
        var right = layout.SlotsIn(DockSide.Right);
        Assert.Equal(Enumerable.Range(0, right.Count), right.Select(s => layout.Place(s[0]).Order));
    }

    [Fact]
    public void AGroupMovedAlongItsOwnSideLandsAtTheIndexTheDropNamed()
    {
        // The index arrives as DockZones computes it: with the carried slot
        // already lifted out. Navigator, Work, Layers, Colour — the work group
        // dropped after Layers is index 2 once it is gone.
        var layout = DockLayout.Default();

        layout.DockGroup(DockPanelId.Sheets, DockSide.Right, 2);

        var fronts = layout.SlotsIn(DockSide.Right).Select(s => s[0]).ToList();
        Assert.Equal([DockPanelId.Navigator, DockPanelId.Layers, DockPanelId.Project, DockPanelId.Color], fronts);
    }

    [Fact]
    public void AGroupJoiningAnotherKeepsItsOrderAndItsFront()
    {
        var layout = DockLayout.Default();

        layout.JoinGroupWhole(DockPanelId.Color, DockPanelId.Layers, 1);

        Assert.Equal(
            [DockPanelId.Layers, DockPanelId.Color, DockPanelId.Palette, DockPanelId.Gradient, DockPanelId.Channels],
            layout.SlotOf(DockPanelId.Layers));
        Assert.Equal(DockPanelId.Color, layout.ActiveOf(layout.SlotOf(DockPanelId.Layers)));
    }

    [Fact]
    public void AGroupWithTheTimelineInItStaysWhereItIs()
    {
        var layout = DockLayout.Default();
        var before = layout.Serialize();

        layout.DockGroup(DockPanelId.Xsheet, DockSide.Right, 0);
        layout.JoinGroupWhole(DockPanelId.Xsheet, DockPanelId.Layers);

        Assert.Equal(before, layout.Serialize());
    }

    [Fact]
    public void AFullSideTakesAGroupAsTabsInsteadOfAFifthSlot()
    {
        var layout = DockLayout.Default();
        foreach (var (id, i) in new[] { DockPanelId.History, DockPanelId.Reference, DockPanelId.Symbols, DockPanelId.Scene }.Select((id, i) => (id, i)))
        {
            layout.Dock(id, DockSide.Left, i);
        }

        layout.DockGroup(DockPanelId.Color, DockSide.Left, 0);

        Assert.Equal(DockLayout.MaxSlotsPerSide, layout.SlotsIn(DockSide.Left).Count);
        var landed = layout.SlotOf(DockPanelId.Color);
        Assert.Equal(DockSide.Left, layout.SideOf(DockPanelId.Color));
        Assert.Contains(DockPanelId.Channels, landed);
        Assert.DoesNotContain(DockPanelId.Palette, layout.PanelsIn(DockSide.Right));
    }

    // ---- where it can be dropped ---------------------------------------------

    private static readonly DockRect Content = new(0, 0, 1000, 800);

    [Fact]
    public void OverItsOwnHeaderACarriedGroupHasNowhereToGo()
    {
        // A tab over its own header is a rearrangement; a group over its own
        // header is the group where it already is.
        var layout = DockLayout.Default();
        var slots = new List<PanelSlot>
        {
            new(DockPanelId.Color, DockSide.Right, 0, new DockRect(700, 0, 300, 400), 30,
                [new PanelTab(DockPanelId.Color, new DockRect(700, 4, 60, 26)),
                 new PanelTab(DockPanelId.Palette, new DockRect(760, 4, 60, 26))]),
            new(DockPanelId.Layers, DockSide.Right, 1, new DockRect(700, 400, 300, 400), 30,
                [new PanelTab(DockPanelId.Layers, new DockRect(700, 404, 60, 26))]),
        };

        Assert.Null(DockZones.Resolve(790, 15, Content, slots, DockPanelId.Color, layout, wholeGroup: true));
        Assert.NotNull(DockZones.Resolve(790, 15, Content, slots, DockPanelId.Color, layout));

        var onLayers = DockZones.Resolve(720, 415, Content, slots, DockPanelId.Color, layout, wholeGroup: true);
        Assert.Equal(DockPanelId.Layers, onLayers?.IntoGroupOf);
    }

    // ---- the grip -------------------------------------------------------------

    private static (Window Window, Docker Docker) Shown(params DockPanelId[] tabs)
    {
        var docker = new Docker { PanelId = tabs[0], Title = DockPanels.Of(tabs[0]).Title };
        var window = new Window { Content = docker, Width = 500, Height = 300 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        docker.ShowTabs(tabs.Select(DockPanels.Of).ToList(), tabs[0]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, docker);
    }

    /// <summary>A point in the header between the last tab and the buttons.</summary>
    private static Point BareHeaderPoint(Docker docker, Visual relativeTo)
    {
        var tabsEnd = docker.TabRects().Max(t => t.Bounds.Right);
        var header = docker.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Header");
        var buttons = header.GetVisualDescendants().OfType<StackPanel>().First(s => s.Orientation == Avalonia.Layout.Orientation.Horizontal && s.Children.OfType<Button>().Any());
        var buttonsStart = buttons.TransformToVisual(docker)!.Value.Transform(default).X;
        var y = docker.TabRects()[0].Bounds.Y + docker.TabRects()[0].Bounds.Height / 2;
        Assert.True(buttonsStart - tabsEnd > 8, $"no bare header to press: tabs end {tabsEnd:F0}, buttons start {buttonsStart:F0}");
        return new Point((tabsEnd + buttonsStart) / 2, y).Transform(docker.TransformToVisual(relativeTo)!.Value);
    }

    [AvaloniaFact]
    public void APressAndPullOnTheBareHeaderDragsTheGroupAndNotOnePanel()
    {
        var (window, docker) = Shown(DockPanelId.Navigator, DockPanelId.Palette);
        var panel = false;
        var group = false;
        docker.PanelDragStarted += (_, _) => panel = true;
        docker.GroupDragStarted += (_, _) => group = true;

        var at = BareHeaderPoint(docker, window);
        window.MouseDown(at, MouseButton.Left);
        window.MouseMove(new Point(at.X - 40, at.Y + 40), RawInputModifiers.LeftMouseButton);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(group, "a pull on the bare header did not pick the group up");
        Assert.False(panel, "the bare header picked up one panel, which is what B155 removed");
    }

    [AvaloniaFact]
    public void ATabIsStillTheGripForOnePanel()
    {
        var (window, docker) = Shown(DockPanelId.Navigator, DockPanelId.Palette);
        var panel = false;
        var group = false;
        docker.PanelDragStarted += (_, _) => panel = true;
        docker.GroupDragStarted += (_, _) => group = true;

        var tab = docker.TabRects()[1].Bounds;
        var at = new Point(tab.X + 10, tab.Y + tab.Height / 2).Transform(docker.TransformToVisual(window)!.Value);
        window.MouseDown(at, MouseButton.Left);
        window.MouseMove(new Point(at.X - 40, at.Y + 40), RawInputModifiers.LeftMouseButton);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(panel);
        Assert.False(group);
    }

    [AvaloniaFact]
    public void APressThatLandsOnAButtonDisarmsAnEarlierPressWhoseReleaseWentMissing()
    {
        // A release can go missing (the tab strip is rebuilt under the pointer,
        // and capture goes with it). The next press, on a button, used to leave
        // the old one armed, and a pull afterwards started a drag nobody began.
        var (window, docker) = Shown(DockPanelId.Navigator, DockPanelId.Palette);
        var started = false;
        docker.GroupDragStarted += (_, _) => started = true;
        docker.PanelDragStarted += (_, _) => started = true;
        var header = docker.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Header");
        var button = docker.GetVisualDescendants().OfType<Button>().First(b => b.Name == "PART_Float");

        docker.HeaderPressed(PressOn(header, docker, window));
        docker.HeaderPressed(PressOn(button, docker, window));
        window.MouseMove(new Point(200, 200), RawInputModifiers.LeftMouseButton);
        docker.HeaderMoved(MoveTo(docker, window, new Point(200, 200)));

        Assert.False(started);
    }

    private static PointerPressedEventArgs PressOn(Visual source, Docker docker, Window root) =>
        new(source, new Pointer(1, PointerType.Mouse, true), root, new Point(10, 10), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None);

    private static PointerEventArgs MoveTo(Docker docker, Window root, Point at) =>
        new(InputElement.PointerMovedEvent, docker, new Pointer(1, PointerType.Mouse, true), root, at, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other),
            KeyModifiers.None);

    [AvaloniaFact]
    public void ADoubleClickOnTheBareHeaderDoesNotCollapse()
    {
        // Collapse is a double-click on a tab (Q277); the bare header is a grip
        // now, and two quick presses on a grip are not a request to roll up.
        var (window, docker) = Shown(DockPanelId.Navigator, DockPanelId.Palette);
        docker.CanCollapse = true;
        var asked = false;
        docker.CollapseRequested += (_, _) => asked = true;

        var at = BareHeaderPoint(docker, window);
        Docker.DoubleClickTimeForTests = TimeSpan.FromSeconds(30);
        try
        {
            for (var i = 0; i < 2; i++)
            {
                window.MouseDown(at, MouseButton.Left);
                window.MouseUp(at, MouseButton.Left);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            Docker.DoubleClickTimeForTests = null;
        }

        Assert.False(asked);
    }

    // ---- end to end, through the window ----------------------------------------

    /// <summary>The middle of a docker's ⠿ grip.</summary>
    /// <remarks>
    /// The grip rather than the bare header: at the real sidebar width the
    /// tabs fill the header (Q281: tabs end at 234 px, buttons start at 196),
    /// which is the whole reason the grip exists.
    /// </remarks>
    private static Point GripPoint(Docker docker, Visual relativeTo)
    {
        var grip = docker.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_GroupGrip");
        Assert.True(grip.IsEffectivelyVisible, "the group grip is not on screen");
        return new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2).Transform(grip.TransformToVisual(relativeTo)!.Value);
    }

    [AvaloniaFact]
    public void EveryMovableGroupShowsAGripAndTheTimelinesDoesNot()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Group", 400, 300, 12, 72, "#ffffff", false));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        foreach (var docker in window.FindControl<DockStrip>("RightStrip")!.Children.OfType<Docker>())
        {
            var grip = docker.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_GroupGrip");
            Assert.True(grip.IsEffectivelyVisible, $"{docker.PanelId}'s group has no grip");
            Assert.Equal(Docker.Grip.Group, docker.GripAt(grip.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().First()));
        }
        var timeline = window.FindControl<Docker>("TimelineDocker")!;
        Assert.False(timeline.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_GroupGrip").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void AGroupHoldingTheTimelineShowsNoGripEvenWithAnotherPanelInFront()
    {
        // The grip is judged on the whole slot. With the timeline behind
        // another tab, or hidden because the document cannot use it, the
        // group still holds it and still cannot move.
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Group", 400, 300, 12, 72, "#ffffff", false));
        vm.Workspace.OpenInGroup(DockPanelId.Palette, DockPanelId.Timeline);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var palette = window.GetVisualDescendants().OfType<Docker>().First(d => d.PanelId == DockPanelId.Palette);
        Assert.False(palette.CanDragGroup);
    }

    [AvaloniaFact]
    public void AGroupDraggedByItsGripToTheLeftEdgeArrivesThereWhole()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Group", 400, 300, 12, 72, "#ffffff", false));
        vm.Workspace.OpenInGroup(DockPanelId.Palette, DockPanelId.Navigator);
        vm.Workspace.Activate(DockPanelId.Navigator);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var docker = window.FindControl<DockStrip>("RightStrip")!.Children.OfType<Docker>()
            .First(d => d.PanelId == DockPanelId.Navigator);
        var at = GripPoint(docker, window);
        var root = window.FindControl<Grid>("RootGrid")!;
        var leftEdge = new Point(12, root.Bounds.Height / 2).Transform(root.TransformToVisual(window)!.Value);
        output.WriteLine($"press {at}, release {leftEdge}");

        window.MouseDown(at, MouseButton.Left);
        window.MouseMove(new Point(at.X - 30, at.Y + 30), RawInputModifiers.LeftMouseButton);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.MouseMove(leftEdge, RawInputModifiers.LeftMouseButton);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.MouseUp(leftEdge, MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal([[DockPanelId.Navigator, DockPanelId.Palette]], vm.Workspace.Layout.SlotsIn(DockSide.Left));
        Assert.Equal(DockPanelId.Navigator, vm.Workspace.Layout.ActiveOf(vm.Workspace.Layout.SlotOf(DockPanelId.Navigator)));
    }

    [AvaloniaFact]
    public void AGroupLetGoOverTheCanvasStaysWhereItWas()
    {
        // A float holds one panel (Q278 left floating groups unbuilt), so there
        // is nothing for a group released over nothing to become.
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Group", 400, 300, 12, 72, "#ffffff", false));
        vm.Workspace.OpenInGroup(DockPanelId.Palette, DockPanelId.Navigator);
        vm.Workspace.Activate(DockPanelId.Navigator);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var before = vm.Workspace.Layout.Serialize();

        var docker = window.FindControl<DockStrip>("RightStrip")!.Children.OfType<Docker>()
            .First(d => d.PanelId == DockPanelId.Navigator);
        var at = GripPoint(docker, window);
        var root = window.FindControl<Grid>("RootGrid")!;
        var middle = new Point(root.Bounds.Width / 2, root.Bounds.Height / 2).Transform(root.TransformToVisual(window)!.Value);

        window.MouseDown(at, MouseButton.Left);
        window.MouseMove(new Point(at.X - 30, at.Y + 30), RawInputModifiers.LeftMouseButton);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.MouseMove(middle, RawInputModifiers.LeftMouseButton);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.MouseUp(middle, MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(before, vm.Workspace.Layout.Serialize());
        Assert.Empty(window.FloatingWindowsForTests);
    }
}
