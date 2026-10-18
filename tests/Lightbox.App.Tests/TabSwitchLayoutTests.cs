using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Switching tabs leaves the dockers where they are (B489).
/// </summary>
/// <remarks>
/// <para>
/// After B484 the view-model half of a switch to a 31-layer document was
/// ~85 ms and the rest was layout: <c>UpdateLayout</c> 200–450 ms and the
/// jobs after it 50–160 ms. Diffing the window's visuals across one switch
/// found the cause. Every switch raised <c>ShowTimeline</c>, the window
/// answered it with <c>ApplyDockLayout</c>, and that detached and reattached
/// every docker. The Layers docker came back with a new container for every
/// row (1,457 new visuals at 31 layers), and each docker's tab strip was
/// rebuilt, even though the timeline was shown on both sides of the switch.
/// </para>
/// <para>
/// <b>Counted, not timed</b>, for B353's reason: a container is either the
/// one that was there or a new one, and that cannot flake on a busy box.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class TabSwitchLayoutTests : BrushStateIsolated
{
    private const int Layers = 8;

    private static void Pump()
    {
        for (var i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static (MainWindow Window, MainViewModel Vm, DocumentTab A, DocumentTab B) TwoDocuments()
    {
        var window = new MainWindow { Width = 960, Height = 540 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        DocumentTab Open(string prefix)
        {
            vm.OpenDocumentTab(DocumentFactory.CreateDoc(96, 96, paperColor: Scene.DefaultBackgroundColor), null);
            while (vm.Doc.Scene.Layers.Count < Layers) vm.AddPaintedLayerCommand.Execute(null);
            for (var i = 0; i < Layers; i++) vm.Doc.Scene.Layers[i].Name = $"{prefix}{i}";
            Pump();
            window.UpdateLayout();
            return vm.ActiveTab!;
        }
        var a = Open("Alpha");
        var b = Open("Bravo");
        return (window, vm, a, b);
    }

    private static void SwitchTo(MainWindow window, MainViewModel vm, DocumentTab tab)
    {
        vm.ActiveTab = tab;
        window.UpdateLayout();
        Pump();
        window.UpdateLayout();
    }

    /// <summary>The containers an items control shows, by identity.</summary>
    private static HashSet<Control> Containers(ItemsControl list) =>
        new(list.GetRealizedContainers(), ReferenceEqualityComparer.Instance);

    [AvaloniaFact]
    public void ALayerRowIsNotRebuiltBySwitchingTabs()
    {
        var (window, vm, a, b) = TwoDocuments();
        var list = window.FindControl<ItemsControl>("LayerList")!;
        SwitchTo(window, vm, a);
        var before = Containers(list);

        SwitchTo(window, vm, b);

        var after = Containers(list);
        var rebuilt = after.Count(c => !before.Contains(c));
        Assert.True(before.Count >= Layers, $"the Layers docker showed {before.Count} rows; expected at least {Layers}");
        // Two documents with the same number of layers reuse every row view
        // model by position, so nothing the list shows is new. Before the fix
        // all of them were: {Layers} of {Layers}.
        Assert.True(rebuilt == 0, $"switching tabs rebuilt {rebuilt} of {after.Count} layer rows");
        // Kept is only right if the kept rows now say the arriving document:
        // the names on screen are the second document's, not the first's.
        var shown = list.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToHashSet();
        Assert.Contains("Bravo3", shown);
        Assert.DoesNotContain("Alpha3", shown);
    }

    [AvaloniaFact]
    public void ADockerTabStripIsNotRebuiltBySwitchingTabs()
    {
        var (window, vm, a, b) = TwoDocuments();
        SwitchTo(window, vm, a);
        var strips = window.GetVisualDescendants().OfType<ListBox>().Where(l => l.Name == "PART_Tabs").ToList();
        var before = strips.SelectMany(Containers).ToHashSet(ReferenceEqualityComparer.Instance);

        SwitchTo(window, vm, b);

        var after = window.GetVisualDescendants().OfType<ListBox>().Where(l => l.Name == "PART_Tabs")
            .SelectMany(Containers).ToList();
        var rebuilt = after.Count(c => !before.Contains(c));
        Assert.True(before.Count > 0, "no docker tab strip was found");
        // The timeline is shown on both sides of the switch, so the dock layout
        // has nothing to change and nothing to rebuild.
        Assert.True(rebuilt == 0, $"switching tabs rebuilt {rebuilt} of {after.Count} docker tabs");
    }

    [AvaloniaFact]
    public void HidingTheTimelineStillTakesItOutOfTheDock()
    {
        // The other half of the contract: when the answer does change, the
        // dock still follows it.
        var (window, vm, a, _) = TwoDocuments();
        var timeline = window.FindControl<Control>("TimelineDocker")!;
        SwitchTo(window, vm, a);
        Assert.True(timeline.IsEffectivelyVisible, "the timeline should be docked on a document tab");

        vm.TimelineVisible = false;
        Pump();
        window.UpdateLayout();
        Assert.False(timeline.IsEffectivelyVisible, "hiding the timeline should take it out of the dock");

        vm.TimelineVisible = true;
        Pump();
        window.UpdateLayout();
        Assert.True(timeline.IsEffectivelyVisible, "showing the timeline again should put it back");
    }
}
