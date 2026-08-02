using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;

namespace Lightbox.App.Tests;

/// <summary>
/// The workspace as the window actually builds it. The arithmetic is covered
/// by <see cref="DockLayoutTests"/> and <see cref="DockZoneTests"/> without a
/// window; what is left to check here is the part only a real window can get
/// wrong — that a panel ends up in the strip the layout named, that closing
/// one parks it instead of destroying it, and that an emptied edge collapses.
/// </summary>
[Collection("BrushState")]
public sealed class WorkspaceTests : BrushStateIsolated
{
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, (MainViewModel)window.DataContext!);
    }

    private static DockStrip Strip(MainWindow w, DockSide side) => side switch
    {
        DockSide.Left => w.FindControl<DockStrip>("LeftStrip")!,
        DockSide.Right => w.FindControl<DockStrip>("RightStrip")!,
        DockSide.Top => w.FindControl<DockStrip>("TopStrip")!,
        _ => w.FindControl<DockStrip>("BottomStrip")!,
    };

    private static List<DockPanelId> Shown(MainWindow w, DockSide side) =>
        Strip(w, side).Children.OfType<Docker>().Select(d => d.PanelId).ToList();

    private static Panel Pool(MainWindow w) => w.FindControl<Panel>("PanelPool")!;

    [AvaloniaFact]
    public void PanelsLandInTheStripTheLayoutNames()
    {
        var (w, _) = Open();

        Assert.Equal(
            [DockPanelId.Layers, DockPanelId.Color, DockPanelId.Sheets],
            Shown(w, DockSide.Right));   // Project is absent: no project yet
        Assert.Equal([DockPanelId.Timeline], Shown(w, DockSide.Bottom));
        Assert.Empty(Shown(w, DockSide.Left));
    }

    [AvaloniaFact]
    public void MovingAPanelMovesTheControl()
    {
        var (w, vm) = Open();

        vm.Workspace.Dock(DockPanelId.Color, DockSide.Left, 0);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal([DockPanelId.Color], Shown(w, DockSide.Left));
        Assert.DoesNotContain(DockPanelId.Color, Shown(w, DockSide.Right));
    }

    [AvaloniaFact]
    public void AnEmptyEdgeCollapsesAndAFilledOneOpens()
    {
        // "Optional means absent, not disabled": an area with nothing in it
        // takes no width and shows no splitter.
        var (w, vm) = Open();
        var host = w.FindControl<ScrollViewer>("LeftHost")!;
        var splitter = w.FindControl<GridSplitter>("LeftSplitter")!;
        Assert.False(host.IsVisible);
        Assert.False(splitter.IsVisible);

        vm.Workspace.Dock(DockPanelId.Color, DockSide.Left, 0);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(host.IsVisible);
        Assert.True(splitter.IsVisible);

        vm.Workspace.SetVisible(DockPanelId.Color, false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(host.IsVisible);
        Assert.False(splitter.IsVisible);
    }

    [AvaloniaFact]
    public void ClosingAPanelParksItRatherThanDestroyingIt()
    {
        // Which is what makes closing and reopening a no-op rather than a
        // reset: the same control comes back, scroll position and all.
        var (w, vm) = Open();
        var color = Strip(w, DockSide.Right).Children.OfType<Docker>()
            .First(d => d.PanelId == DockPanelId.Color);

        vm.Workspace.SetVisible(DockPanelId.Color, false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains(color, Pool(w).Children);

        vm.Workspace.SetVisible(DockPanelId.Color, true);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Same(color, Strip(w, DockSide.Right).Children.OfType<Docker>()
            .First(d => d.PanelId == DockPanelId.Color));
    }

    [AvaloniaFact]
    public void TheHeaderSwitcherTradesTwoPanelsPlaces()
    {
        // Blender's rule: no panel is ever open twice, so choosing the palette
        // from the colour header sends the colour panel where the palette was.
        var (w, vm) = Open();
        Assert.DoesNotContain(DockPanelId.Palette, Shown(w, DockSide.Right));

        vm.Workspace.Swap(DockPanelId.Color, DockPanelId.Palette);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Contains(DockPanelId.Palette, Shown(w, DockSide.Right));
        Assert.DoesNotContain(DockPanelId.Color, Shown(w, DockSide.Right));
        Assert.Single(Pool(w).Children.OfType<Docker>(), d => d.PanelId == DockPanelId.Color);
    }

    [AvaloniaFact]
    public void EveryPanelExceptTheTimelineOffersASwitcher()
    {
        var (w, _) = Open();
        var pool = Pool(w).Children.OfType<Docker>()
            .Concat(Strip(w, DockSide.Right).Children.OfType<Docker>())
            .Concat(Strip(w, DockSide.Bottom).Children.OfType<Docker>())
            .ToList();

        foreach (var panel in pool)
        {
            if (panel.PanelId == DockPanelId.Timeline)
            {
                Assert.False(panel.ShowSwitcher);
                continue;
            }
            Assert.NotNull(panel.SwitchTargets);
            Assert.DoesNotContain(panel.SwitchTargets!, t => t.Id == panel.PanelId);
        }
    }

    [AvaloniaFact]
    public void TheProjectPanelAppearsAsSoonAsThereIsAProject()
    {
        // It was staying hidden: HasProject is a forwarding property with no
        // notification of its own, and adopting a project is not an edit, so
        // the docker's change callback never fired for it.
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-ws-{Guid.NewGuid():N}.lbproj");
        try
        {
            var (w, vm) = Open();
            Assert.DoesNotContain(DockPanelId.Project, Shown(w, DockSide.Right));

            vm.NewProject(root, "Knight");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Contains(DockPanelId.Project, Shown(w, DockSide.Right));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void TheCanvasGetsTheRoomLeftOverByTheStrips()
    {
        // It did not. The canvas host was left in the left strip's grid column
        // when the docking rework renumbered them — an Auto column, empty by
        // default, so it collapsed to the width of the zoom bar floating on
        // top of it. The canvas had no width, so it drew nothing and took no
        // pointer events, and the app opened on what looked like a dead
        // renderer.
        var (w, _) = Open();
        var canvas = w.FindControl<Panel>("CanvasHost")!;
        var left = w.FindControl<ScrollViewer>("LeftHost")!;
        var right = w.FindControl<ScrollViewer>("RightHost")!;

        Assert.NotEqual(Grid.GetColumn(left), Grid.GetColumn(canvas));
        Assert.NotEqual(Grid.GetColumn(right), Grid.GetColumn(canvas));
        // The starred column: whatever the strips leave, and never less than
        // its declared minimum.
        Assert.True(canvas.Bounds.Width >= 240,
            $"the canvas needs the leftover room, got {canvas.Bounds.Width}");
        Assert.True(canvas.Bounds.Height >= 100,
            $"the canvas needs the leftover height, got {canvas.Bounds.Height}");
    }

    [AvaloniaFact]
    public void TheProjectRowMenuActuallyDoesSomethingWhenClicked()
    {
        // The failure this guards is silent. A flyout's items live in a popup
        // rather than in the window's tree, so a `$parent[Window]` binding —
        // the pattern the ＋ menu uses — resolves to nothing here and leaves
        // every item looking correct and doing nothing. Raising the real Click
        // is the only way to tell the difference.
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-menu-{Guid.NewGuid():N}.lbproj");
        try
        {
            var (w, vm) = Open();
            vm.NewProject(root, "Knight");
            vm.Save();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var list = w.FindControl<ListBox>("ProjectRows")!;
            var host = Assert.IsAssignableFrom<Control>(list.ContainerFromIndex(0))
                .GetVisualDescendants().OfType<DockPanel>()
                .First(p => p.ContextFlyout is MenuFlyout);
            var flyout = (MenuFlyout)host.ContextFlyout!;
            flyout.ShowAt(host);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var items = flyout.Items.OfType<MenuItem>().ToList();
            flyout.Hide();

            Assert.Equal(
                ["Open", "Open with default app…", "Show in file manager", "Copy path",
                 "Duplicate", "Rename…", "Remove from project"],
                items.Select(i => i.Header?.ToString()).ToList());

            vm.ProjectDocker.Selected = vm.ProjectDocker.Rows.First(r => r.Animation is not null);
            Click(items, "Copy path");
            Assert.EndsWith(".lightbox.json", vm.ProjectDocker.CopiedPath);

            Click(items, "Duplicate");
            Assert.Equal(2, vm.ProjectDocker.Project!.Characters.First().Animations.Count);

            Click(items, "Rename…");
            Assert.True(vm.ProjectDocker.Selected!.IsRenaming);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void TheNewMenuActuallyMakesThings()
    {
        // Same failure mode as the row menu, in the header. It used to build
        // its items from NewItemKinds with a `$parent[Window]` command binding,
        // which a popup cannot resolve.
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-new-{Guid.NewGuid():N}.lbproj");
        try
        {
            var (w, vm) = Open();
            vm.NewProject(root, "Knight");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var button = w.GetVisualDescendants().OfType<Button>()
                .First(b => b.Flyout is MenuFlyout && Equals(b.Content, "＋ New ▾"));
            var flyout = (MenuFlyout)button.Flyout!;
            flyout.ShowAt(button);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var items = flyout.Items.OfType<MenuItem>().ToList();
            flyout.Hide();

            Assert.Equal(["Animation", "Character", "Scene", "Shot", "Document"],
                items.Select(i => i.Header?.ToString()).ToList());

            Click(items, "Animation");
            Assert.Equal(2, vm.ProjectDocker.Project!.Characters.First().Animations.Count);

            Click(items, "Character");
            Assert.Equal(2, vm.ProjectDocker.Project.Characters.Count());

            Click(items, "Document");
            Assert.Single(vm.ProjectDocker.Project.Manifest.Documents);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void Click(IEnumerable<MenuItem> items, string header) =>
        items.First(i => Equals(i.Header, header))
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

    [AvaloniaFact]
    public void TheReferencePanelIsAbsentUntilItIsAskedFor()
    {
        // Same rule as the palette and the gradient: an animation reference is
        // something you set up deliberately, and empty it is sidebar height
        // the layers could be using.
        var (w, vm) = Open();
        Assert.DoesNotContain(DockPanelId.Reference, Shown(w, DockSide.Right));
        Assert.False(vm.ReferenceDockerVisible);

        vm.ToggleReferenceDockerCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Contains(DockPanelId.Reference, Shown(w, DockSide.Right));
    }

    [AvaloniaFact]
    public void ACappedStripIsNoWiderThanItsPanelsCanUse()
    {
        // A sidebar of fixed-size controls widened past its cap is just
        // whitespace. A panel with real use for the room removes the ceiling.
        var (w, vm) = Open();
        var work = w.FindControl<Grid>("WorkArea")!;
        var right = work.ColumnDefinitions[6];

        vm.Workspace.SetVisible(DockPanelId.Layers, false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(320, right.MaxWidth);   // Color and Sheets both cap at 320

        vm.Workspace.SetVisible(DockPanelId.Layers, true);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(double.PositiveInfinity, right.MaxWidth);
    }
}
