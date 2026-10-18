using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Switching tabs leaves the dockers where they are (B493).
/// </summary>
/// <remarks>
/// <para>
/// Every switch raised <c>ShowTimeline</c>, and the window answered it by
/// rebuilding the whole dock layout — every docker on screen taken out of its
/// strip and put back — whether or not the value had changed. Between two
/// drawings it never does. Taking a docker out of the tree throws its layout
/// away, so the next pass measured the entire window from nothing.
/// </para>
/// <para>
/// <b>Measured on the lab fixtures</b> (11 and 31 layers, a real window at
/// 1600×1000, the fastest of six switches each way, two runs): to the
/// 11-layer tab 495–694 ms before, 80–90 ms after; to the 31-layer tab
/// 784–993 ms before, 341–392 ms after. What is left going up is twenty layer
/// rows being built from nothing (B492), not the dockers.
/// </para>
/// <para>
/// <b>Counted, not timed</b>, for B353's reason: a docker leaving the tree is
/// exact, where a duration measures this box.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class TabSwitchLayoutTests : BrushStateIsolated
{
    private static void Pump()
    {
        for (var i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs();
    }

    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow { Width = 1400, Height = 900 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        // The window starts with no document; a test needs one to switch from.
        vm.OpenDocumentTab(Drawing(), null);
        Pump();
        return (window, vm);
    }

    private static Doc Drawing() =>
        DocumentFactory.CreateDoc(96, 96, paperColor: Scene.DefaultBackgroundColor);

    [AvaloniaFact]
    public void SwitchingBetweenDrawingsLeavesEveryDockerInPlace()
    {
        var (window, vm) = Open();
        var first = vm.Tabs[0];
        vm.OpenDocumentTab(Drawing(), null);
        var second = vm.ActiveTab!;
        Pump();
        window.UpdateLayout();

        var dockers = window.GetVisualDescendants().OfType<Docker>().Where(d => d.IsEffectivelyVisible).ToList();
        var detached = 0;
        foreach (var docker in dockers) docker.DetachedFromVisualTree += (_, _) => detached++;

        for (var i = 0; i < 4; i++)
        {
            vm.ActiveTab = i % 2 == 0 ? first : second;
            Pump();
            window.UpdateLayout();
        }

        Assert.True(dockers.Count > 3, $"expected the default workspace to show several dockers; found {dockers.Count}");
        // Before the fix: every docker on screen left the tree on every switch.
        Assert.Equal(0, detached);
    }

    [AvaloniaFact]
    public void TheTimelineStillLeavesForAReferenceTabAndComesBack()
    {
        // The rebuild had one job on a switch, and it must survive: the
        // timeline means nothing on a reference tab.
        var (window, vm) = Open();
        var timeline = window.FindControl<Docker>("TimelineDocker")!;
        var drawing = vm.Tabs[0];
        Assert.True(timeline.IsEffectivelyVisible, "the default workspace should show the timeline");

        vm.AddReferenceSheet();
        vm.AddReferenceView(vm.ReferenceSheetsView[0]);
        Pump();
        Assert.Equal(DocumentTabKind.Reference, vm.ActiveTab!.Kind);
        Assert.False(timeline.IsEffectivelyVisible, "a reference tab has no timeline");

        vm.ActiveTab = drawing;
        Pump();
        Assert.True(timeline.IsEffectivelyVisible, "back on the drawing, the timeline returns");
    }

    [AvaloniaFact]
    public void TurningTheTimelineOffAndOnStillMovesTheDock()
    {
        // The other road to the same gate: TimelineVisible feeds ShowTimeline,
        // and a switch that skips the rebuild must not make this one skip it.
        var (window, vm) = Open();
        var timeline = window.FindControl<Docker>("TimelineDocker")!;
        Assert.True(timeline.IsEffectivelyVisible, "the default workspace should show the timeline");

        vm.TimelineVisible = false;
        Pump();
        Assert.False(timeline.IsEffectivelyVisible, "the timeline was turned off");

        vm.TimelineVisible = true;
        Pump();
        Assert.True(timeline.IsEffectivelyVisible, "the timeline was turned back on");
    }
}
