using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.Docking;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Switching between a document with few layers and one with many builds only
/// the Layers docker rows that come into view.
/// </summary>
/// <remarks>
/// <para>
/// What was left of a tab switch once the dock stopped being rebuilt (B489):
/// with the Layers docker hidden, a switch to a 31-layer document took 62–116
/// ms; shown, ~380 ms. <c>LayerList</c> was an <c>ItemsControl</c> over a plain
/// <c>StackPanel</c>, which destroys the container of every item removed — so
/// going down to a small document threw twenty rows away and coming back built
/// them again, ~15 ms each.
/// </para>
/// <para>
/// The list virtualizes now, so a row off screen is never built. Recycling the
/// rows that are was tried and does not work in Avalonia 12: a pooled container
/// is detached, and <c>ContentPresenter</c> forgets its recycling template on
/// re-attach, so every reused container rebuilt its row anyway (17 of 20).
/// </para>
/// <para>
/// Counted, not timed: how many row controls a warm switch creates is exact.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class LayerRowsOnASwitchTests : BrushStateIsolated
{
    private static void Pump()
    {
        for (var i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static void Open(MainViewModel vm, string title, int layers)
    {
        vm.NewDocument(new NewDocumentSettings(title, 320, 240, 12, 72, Scene.DefaultBackgroundColor, false));
        for (var l = 1; l < layers; l++) vm.AddPaintedLayerCommand.Execute(null);
    }

    /// <summary>The Layers docker's row controls currently in the window.</summary>
    private static HashSet<Border> Rows(MainWindow window)
    {
        var list = window.FindControl<ItemsControl>("LayerList")!;
        return list.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("layerRow") || b.Classes.Contains("groupRow"))
            .ToHashSet();
    }

    private static void Settle(MainWindow window)
    {
        Pump();
        window.UpdateLayout();
        Pump();
    }

    [AvaloniaFact]
    public void AWarmSwitchBuildsOnlyTheRowsThatComeIntoView()
    {
        var window = new MainWindow { Width = 1400, Height = 900 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        vm.Workspace.SetVisible(DockPanelId.Layers, true);
        vm.Workspace.Activate(DockPanelId.Layers);
        Open(vm, "Few", 2);
        var few = vm.ActiveTab!;
        Open(vm, "Many", 25);
        var many = vm.ActiveTab!;
        Settle(window);
        Assert.NotEmpty(Rows(window));

        // Once round, so both documents have been shown.
        vm.ActiveTab = few;
        Settle(window);
        vm.ActiveTab = many;
        Settle(window);

        var seen = new HashSet<Border>(Rows(window));
        vm.ActiveTab = few;
        Settle(window);
        var atFew = Rows(window).Count;
        seen.UnionWith(Rows(window));
        vm.ActiveTab = many;
        Settle(window);
        var shown = Rows(window).Count;
        var built = Rows(window).Count(r => !seen.Contains(r));

        // Off screen is not built: the docker cannot show 26 rows at this size.
        Assert.True(shown < vm.LayerPanelItems.Count,
            $"all {shown} rows were built for a docker that shows fewer — the list is not virtualizing");
        // And what is built is no more than came into view.
        Assert.True(built <= shown - atFew,
            $"coming back built {built} row(s) where only {shown - atFew} came into view");
    }

    [AvaloniaFact]
    public void EveryLayerStillHasARowYouCanReach()
    {
        // Rows that are reused must still show the right layer, and a row off
        // the bottom of the docker must still be reachable by scrolling.
        var window = new MainWindow { Width = 1400, Height = 900 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        vm.Workspace.SetVisible(DockPanelId.Layers, true);
        vm.Workspace.Activate(DockPanelId.Layers);
        Open(vm, "Few", 2);
        var few = vm.ActiveTab!;
        Open(vm, "Many", 25);
        var many = vm.ActiveTab!;
        Settle(window);
        vm.ActiveTab = few;
        Settle(window);
        vm.ActiveTab = many;
        Settle(window);

        var shown = Rows(window).Select(r => r.DataContext).OfType<LayerRow>().Select(r => r.Layer).ToList();
        Assert.All(shown, layer => Assert.Contains(layer, many.Doc.Scene.Layers));

        var list = window.FindControl<ItemsControl>("LayerList")!;
        var last = vm.LayerPanelItems.Count - 1;
        list.ScrollIntoView(last);
        Settle(window);
        Assert.NotNull(list.ContainerFromIndex(last));
    }
}
