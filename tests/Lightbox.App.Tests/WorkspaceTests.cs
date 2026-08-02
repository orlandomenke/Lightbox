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
