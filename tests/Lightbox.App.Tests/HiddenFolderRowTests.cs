using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lightbox.App.ViewModels;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Hiding a folder hides everything in it, and the docker says so on every row
/// it reaches: each layer and folder beneath is dimmed and its eye drawn shut.
/// </summary>
/// <remarks>
/// Only the hidden folder's own header used to change. The layers inside went
/// on looking visible — open eye, full strength — while drawing nothing, so the
/// docker and the canvas disagreed about every one of them.
/// </remarks>
[Collection("BrushState")]
public class HiddenFolderRowTests : BrushStateIsolated
{
    private static LayerRow Row(MainViewModel vm, string name) =>
        vm.LayerRows.Single(r => r.Layer.Name == name);

    private static GroupRow HeaderOf(MainViewModel vm, string folder) =>
        vm.LayerPanelItems.OfType<GroupRow>().Single(g => g.Group.Name == folder);

    /// <summary>Bottom to top: paper, a, [Outer: b, c, [Inner: d, e]], f.</summary>
    private static MainViewModel Nested(MainViewModel? into = null)
    {
        var vm = into ?? VmLayers.PaperVm();
        while (vm.Doc.Scene.Layers.Count < 7) vm.AddPaintedLayerCommand.Execute(null);
        string[] names = ["a", "b", "c", "d", "e", "f"];
        for (var i = 1; i < 7; i++) vm.LayerRows.Single(r => r.SceneIndex == i).Name = names[i - 1];
        vm.SelectLayer(Row(vm, "b"), toggle: false, range: false);
        vm.SelectLayer(Row(vm, "c"), toggle: true, range: false);
        vm.GroupLayersCommand.Execute(null);
        vm.SelectLayer(Row(vm, "d"), toggle: false, range: false);
        vm.SelectLayer(Row(vm, "e"), toggle: true, range: false);
        vm.GroupLayersCommand.Execute(null);
        var headers = vm.LayerPanelItems.OfType<GroupRow>().ToList();
        var inner = headers.Single(h => h.Group.Id == Row(vm, "d").Layer.GroupId);
        var outer = headers.Single(h => h != inner);
        outer.Name = "Outer";
        inner.Name = "Inner";
        vm.DropOnLayerPanel(inner, outer, LayerDropHint.Into);
        Assert.Equal(outer.Group.Id, inner.Group.ParentId);
        return vm;
    }

    [AvaloniaFact]
    public void HidingAFolderDimsEveryRowBeneathIt_AtAnyDepth()
    {
        var vm = Nested();

        HeaderOf(vm, "Outer").Visible = false;

        foreach (var name in new[] { "b", "c", "d", "e" })
        {
            Assert.True(Row(vm, name).HiddenByFolder, $"{name} is not marked hidden by its folder");
            Assert.True(Row(vm, name).IsDimmed, $"{name} is not dimmed");
        }
        Assert.True(HeaderOf(vm, "Inner").HiddenByFolder);
        Assert.True(HeaderOf(vm, "Inner").IsDimmed);
        // The folder that was hidden is dimmed for its own reason, not a parent's.
        Assert.True(HeaderOf(vm, "Outer").IsDimmed);
        Assert.False(HeaderOf(vm, "Outer").HiddenByFolder);
    }

    [AvaloniaFact]
    public void RowsOutsideTheFolderAreLeftAlone()
    {
        var vm = Nested();

        HeaderOf(vm, "Outer").Visible = false;

        foreach (var name in new[] { "a", "f" })
        {
            Assert.False(Row(vm, name).HiddenByFolder);
            Assert.False(Row(vm, name).IsDimmed);
        }
    }

    /// <summary>The docker reports the folder's reach; it does not rewrite each layer's own switch.</summary>
    [AvaloniaFact]
    public void TheLayersOwnVisibilityIsNotTouched_SoShowingTheFolderPutsEverythingBack()
    {
        var vm = Nested();
        Row(vm, "c").Visible = false; // hidden in its own right, before the folder is

        HeaderOf(vm, "Outer").Visible = false;
        Assert.True(Row(vm, "b").Layer.Visible);
        Assert.True(HeaderOf(vm, "Inner").Group.Visible);

        HeaderOf(vm, "Outer").Visible = true;
        Assert.False(Row(vm, "b").IsDimmed);
        Assert.False(Row(vm, "d").IsDimmed);
        Assert.False(HeaderOf(vm, "Inner").IsDimmed);
        Assert.True(Row(vm, "c").IsDimmed); // still hidden for its own reason
        Assert.False(Row(vm, "c").HiddenByFolder);
    }

    [AvaloniaFact]
    public void HidingAnInnerFolderReachesOnlyWhatIsInsideIt()
    {
        var vm = Nested();

        HeaderOf(vm, "Inner").Visible = false;

        Assert.True(Row(vm, "d").IsDimmed);
        Assert.True(Row(vm, "e").IsDimmed);
        Assert.False(Row(vm, "b").IsDimmed);
        Assert.False(HeaderOf(vm, "Outer").IsDimmed);
    }

    /// <summary>
    /// As drawn: the row carries the dimming class, and the eye is the closed
    /// drawing even though the layer's own switch is still on.
    /// </summary>
    [AvaloniaFact]
    public void TheRowIsDrawnDimmed_AndItsEyeIsDrawnShut()
    {
        var window = new Views.MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        try
        {
            Nested(vm);
            HeaderOf(vm, "Outer").Visible = false;
            Dispatcher.UIThread.RunJobs();

            var list = window.GetVisualDescendants().OfType<ItemsControl>().Single(c => c.Name == "LayerList");
            Assert.True(window.TryFindResource("IconEyeClosed", window.ActualThemeVariant, out var closed));
            Assert.True(window.TryFindResource("IconEyeOpen", window.ActualThemeVariant, out var open));

            (Border Row, ToggleButton Eye) Drawn(object item)
            {
                var container = list.ContainerFromItem(item)!;
                var row = container.GetVisualDescendants().OfType<Border>()
                    .First(b => b.Classes.Contains("layerRow") || b.Classes.Contains("groupRow"));
                var eye = row.GetVisualDescendants().OfType<ToggleButton>().First(t => t.Classes.Contains("eye"));
                return (row, eye);
            }

            foreach (var item in new object[] { Row(vm, "b"), Row(vm, "d"), HeaderOf(vm, "Inner") })
            {
                var (row, eye) = Drawn(item);
                Assert.Contains("hiddenLayer", row.Classes);
                Assert.True(eye.IsChecked, "the row's own switch was turned off");
                Assert.Same(closed, eye.GetVisualDescendants().OfType<Path>().Single().Data);
            }

            var (looseRow, looseEye) = Drawn(Row(vm, "f"));
            Assert.DoesNotContain("hiddenLayer", looseRow.Classes);
            Assert.Same(open, looseEye.GetVisualDescendants().OfType<Path>().Single().Data);
        }
        finally
        {
            window.Close();
        }
    }
}
