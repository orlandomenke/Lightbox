using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Clicking a tab must not take the strip apart underneath the click.
/// </summary>
/// <remarks>
/// <para>
/// <b>B135.</b> Reported from a shipped build: tab two panels together, switch
/// to one, switch back, and the application dies. Reproduced with Layers +
/// Palette and again with Colour + Reference sheets, so it is the mechanism
/// rather than any particular pair.
/// </para>
/// <para>
/// The path is a re-entrancy: a click raises <c>SelectionChanged</c> on the
/// header's ListBox, which the docker turns into <c>TabPicked</c>, which the
/// window turns into <c>Workspace.Activate</c> — and that raises <c>Changed</c>,
/// which runs <c>ApplyDockLayout</c>, which detaches and re-adds the very
/// <see cref="Docker"/> whose ListBox is still dispatching the event.
/// </para>
/// <para>
/// These drive the ListBox rather than the view model, because setting
/// <c>SelectedItem</c> is what a click does and calling <c>Activate</c>
/// directly is what every existing test does — which is exactly why the suite
/// was green while the application crashed.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class TabSwitchCrashTests : BrushStateIsolated
{
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, (MainViewModel)window.DataContext!);
    }

    private static ListBox Strip(MainWindow w) =>
        w.FindControl<DockStrip>("RightStrip")!.Children.OfType<Docker>()
            .First(d => d.Tabs?.Count() > 1)
            .GetVisualDescendants().OfType<ListBox>().First();

    /// <summary>Click a tab the way the pointer does: through the ListBox.</summary>
    private static void ClickTab(MainWindow w, DockPanelId id)
    {
        var strip = Strip(w);
        var item = strip.Items.OfType<DockPanelInfo>().First(t => t.Id == id);
        strip.SelectedItem = item;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void SwitchingBackAndForthBetweenTabsDoesNotCrash()
    {
        var (w, vm) = Open();

        vm.Workspace.JoinGroup(DockPanelId.Palette, DockPanelId.Layers);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // The joiner shows, because the artist just dragged it there. So the
        // first click is a real switch and the second is the one the report
        // says dies.
        ClickTab(w, DockPanelId.Layers);
        Assert.Equal(DockPanelId.Layers, vm.Workspace.Layout.ActiveOf(
            vm.Workspace.Layout.SlotOf(DockPanelId.Layers)));

        ClickTab(w, DockPanelId.Palette);
        Assert.Equal(DockPanelId.Palette, vm.Workspace.Layout.ActiveOf(
            vm.Workspace.Layout.SlotOf(DockPanelId.Palette)));

        // And keeps working, because a crash on the third would be the same
        // defect with a different count.
        ClickTab(w, DockPanelId.Layers);
        ClickTab(w, DockPanelId.Palette);
    }

    [AvaloniaFact]
    public void TheOtherPairInTheReportBehavesTheSameWay()
    {
        // Colour + Reference sheets, named in the report. Different panels,
        // different content, same mechanism — worth pinning both so a fix that
        // happens to suit one pair cannot pass.
        var (w, vm) = Open();

        vm.Workspace.JoinGroup(DockPanelId.Sheets, DockPanelId.Color);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        ClickTab(w, DockPanelId.Color);
        ClickTab(w, DockPanelId.Sheets);
        ClickTab(w, DockPanelId.Color);
    }

    [AvaloniaFact]
    public void TheHighlightedTabIsTheDockerThatIsShowing()
    {
        // B138. Reported after the crash fix: "the active docker is visible
        // but the tab is in rest while the inactive has the highlight". The
        // crash tests above assert the strip's CONTENT; this asserts the
        // strip's LIGHTS — the ListBox selection the artist actually reads.
        var (w, vm) = Open();

        vm.Workspace.JoinGroup(DockPanelId.Palette, DockPanelId.Layers);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        foreach (var target in new[]
        {
            DockPanelId.Layers, DockPanelId.Palette,
            DockPanelId.Layers, DockPanelId.Palette,
        })
        {
            ClickTab(w, target);

            var docker = w.FindControl<DockStrip>("RightStrip")!.Children.OfType<Docker>()
                .First(d => d.Tabs?.Count() > 1);
            var strip = docker.GetVisualDescendants().OfType<ListBox>().First();
            Assert.Equal(target, docker.PanelId);
            var lit = Assert.IsType<DockPanelInfo>(strip.SelectedItem);
            Assert.True(lit.Id == target,
                $"the strip shows {docker.PanelId} but the highlight sits on {lit.Id}");
        }
    }

    [AvaloniaFact]
    public void TheDockerShowingAfterASwitchIsTheOneInTheStrip()
    {
        // The crash is the headline, but a fix that survives by not rebuilding
        // would leave the wrong panel on screen. This is the other half.
        var (w, vm) = Open();

        vm.Workspace.JoinGroup(DockPanelId.Palette, DockPanelId.Layers);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        ClickTab(w, DockPanelId.Layers);

        var shown = w.FindControl<DockStrip>("RightStrip")!.Children.OfType<Docker>().ToList();
        Assert.Contains(shown, d => d.PanelId == DockPanelId.Layers);
        Assert.DoesNotContain(shown, d => d.PanelId == DockPanelId.Palette);
    }
}
