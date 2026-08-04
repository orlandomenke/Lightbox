using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;

namespace Lightbox.App.Tests;

/// <summary>
/// Tearing a panel out into its own window, and putting it back.
/// </summary>
/// <remarks>
/// <para>
/// <c>FloatingPanelWindow</c> was the one control in the docking system with no tests, and
/// the codemap said so. What it was hiding was a crash on the way back in: docking a
/// floating panel threw, and the layout pass that threw had <em>already</em> reparented the
/// panel, so the state left behind was neither floating nor docked.
/// </para>
/// <para>
/// The round trip is the test, not the tear-out. Anything can move a control into a new
/// window; the hard part is that the panel is the same instance throughout — that is what
/// keeps its scroll position and any half-typed value — so every step has to agree about
/// who currently owns it.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class FloatingPanelTests : BrushStateIsolated
{
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, (MainViewModel)window.DataContext!);
    }

    private static void Pump()
    {
        for (var i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The panel control for an id, wherever it currently lives.</summary>
    private static Docker Panel(MainWindow window, DockPanelId id) =>
        window.GetVisualDescendants().OfType<Docker>().FirstOrDefault(d => d.PanelId == id)
        ?? window.FloatingWindowsForTests
            .Select(w => w.Content).OfType<Docker>().First(d => d.PanelId == id);

    private const DockPanelId Subject = DockPanelId.Layers;

    [AvaloniaFact]
    public void APanelCanBeTornOutAndDockedAgainWithoutCrashing()
    {
        // The reported crash, as a round trip. It threw inside the layout pass, which is
        // why the panel was left in neither place: ApplyDockLayout detaches every panel it
        // is about to restrip, so by the time CloseFloating asked the window for its
        // content there was none, and the cast to Docker went through null.
        var (_, vm) = Open();

        vm.Workspace.Float(Subject, 120, 120, 320, 400);
        Pump();

        vm.Workspace.Dock(Subject, DockSide.Right, 0);
        Pump();

        Assert.Equal(DockSide.Right, vm.Workspace.Layout.SideOf(Subject));
    }

    [AvaloniaFact]
    public void ADockedPanelIsInTheStripRatherThanParkedInThePool()
    {
        // The second bug in the same line, and the one a crash fix alone would leave: even
        // without the throw, CloseFloating parked whatever the window handed back — so a
        // panel that had just been docked was pulled straight out of the strip it was
        // dropped into and stored in the pool, present in the layout and invisible.
        var (window, vm) = Open();

        vm.Workspace.Float(Subject, 140, 140, 320, 400);
        Pump();
        vm.Workspace.Dock(Subject, DockSide.Right, 0);
        Pump();

        var panel = Panel(window, Subject);
        var pool = window.FindControl<Panel>("PanelPool");
        Assert.NotNull(pool);
        Assert.DoesNotContain(panel, pool!.Children);
        Assert.True(panel.IsVisible, "the docked panel is not visible");
    }

    [AvaloniaFact]
    public void TheFloatingWindowIsGoneOnceThePanelIsDockedAgain()
    {
        var (window, vm) = Open();

        vm.Workspace.Float(Subject, 160, 160, 320, 400);
        Pump();
        Assert.Contains(window.FloatingWindowsForTests, w => w.PanelId == Subject);

        vm.Workspace.Dock(Subject, DockSide.Left, 0);
        Pump();

        Assert.DoesNotContain(window.FloatingWindowsForTests, w => w.PanelId == Subject);
    }

    [AvaloniaFact]
    public void ItIsTheSamePanelInstanceAllTheWayRound()
    {
        // The reason the panels live in a pool and the layout only names them. A rebuilt
        // panel would lose a scroll position and any half-typed value, and the artist would
        // read that as the app forgetting what they were doing.
        var (window, vm) = Open();

        var before = Panel(window, Subject);
        vm.Workspace.Float(Subject, 180, 180, 320, 400);
        Pump();
        var floating = Panel(window, Subject);
        vm.Workspace.Dock(Subject, DockSide.Right, 0);
        Pump();
        var after = Panel(window, Subject);

        Assert.Same(before, floating);
        Assert.Same(before, after);
    }

    [AvaloniaFact]
    public void FloatingAndDockingRepeatedlyStaysStable()
    {
        // Once is a crash; three times is the state machine. Each trip leaves the window
        // dictionary, the pool and the strip to agree, and a leak in any of them shows up
        // as a stale window or a panel in two places.
        var (window, vm) = Open();

        for (var i = 0; i < 3; i++)
        {
            vm.Workspace.Float(Subject, 100 + i * 20, 100, 320, 400);
            Pump();
            vm.Workspace.Dock(Subject, i % 2 == 0 ? DockSide.Right : DockSide.Left, 0);
            Pump();
        }

        Assert.DoesNotContain(window.FloatingWindowsForTests, w => w.PanelId == Subject);
        var pool = window.FindControl<Panel>("PanelPool");
        Assert.DoesNotContain(Panel(window, Subject), pool!.Children);
    }

    [AvaloniaFact]
    public void ClosingTheFloatingWindowParksThePanelRatherThanLosingIt()
    {
        // The other way out of a floating window, and the case where parking IS correct —
        // which is exactly why the two had to be told apart rather than sharing one line.
        var (window, vm) = Open();

        vm.Workspace.Float(Subject, 200, 200, 320, 400);
        Pump();
        var floater = window.FloatingWindowsForTests.First(w => w.PanelId == Subject);
        var panel = (Docker)floater.Content!;

        floater.Close();
        Pump();

        var pool = window.FindControl<Panel>("PanelPool");
        Assert.Contains(panel, pool!.Children);
        Assert.False(vm.Workspace.Layout.SideOf(Subject) == DockSide.Floating);
    }
}
