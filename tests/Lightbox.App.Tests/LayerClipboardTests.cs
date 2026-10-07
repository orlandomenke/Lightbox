using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Copying and pasting whole layers, from the layer docker and the X-sheet, by
/// key and by right-click.
/// </summary>
/// <remarks>
/// Ctrl+C/V already mean lines and cels. These tests hold the routing: a layer
/// surface having focus makes them mean layers, and a cel having focus leaves
/// them exactly what they were.
/// </remarks>
[Collection("BrushState")]
public class LayerClipboardTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static void Pump() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    /// <summary>Paper plus a, b, c (bottom first); a carries one stroke.</summary>
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow { Width = 1400, Height = 1600 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Clip", 400, 300, 12, 72, "#ffffff", false));
        while (vm.Doc.Scene.Layers.Count < 4) vm.AddPaintedLayerCommand.Execute(null);
        string[] names = ["a", "b", "c"];
        for (var i = 1; i < vm.Doc.Scene.Layers.Count; i++) vm.Doc.Scene.Layers[i].Name = names[i - 1];
        vm.Doc.Scene.Layers[1].Cels[0].Frame!.Strokes.Add(
            new Stroke { Points = [new StrokePoint(10, 10, 1), new StrokePoint(60, 60, 1)] });
        vm.Workspace.Activate(Lightbox.App.Docking.DockPanelId.Xsheet);
        Pump();
        return (window, vm);
    }

    private static LayerRow RowOf(MainViewModel vm, string name) => vm.LayerRows.First(r => r.Layer.Name == name);

    private static string Order(MainViewModel vm) => string.Join(",", vm.Doc.Scene.Layers.Select(l => l.Name));

    private static Button NameButton(Window window, LayerRow row) =>
        window.GetVisualDescendants().OfType<Button>()
            .First(b => b.Classes.Contains("layerName") && ReferenceEquals(b.DataContext, row));

    private static Border DockerRow(Window window, LayerRow row) =>
        window.GetVisualDescendants().OfType<Border>()
            .First(b => b.Classes.Contains("layerRow") && b.Focusable && ReferenceEquals(b.DataContext, row));

    private static void Press(Window window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.Control);
        Pump();
    }

    // ---- the layer docker -----------------------------------------------------

    [AvaloniaFact]
    public void CtrlCThenCtrlVOnADockerRowPastesACopyAboveTheActiveLayer()
    {
        var (window, vm) = Open();
        vm.SelectLayer(RowOf(vm, "a"), toggle: false, range: false);
        DockerRow(window, RowOf(vm, "a")).Focus();
        Pump();

        Press(window, PhysicalKey.C);
        Press(window, PhysicalKey.V);

        output.WriteLine(Order(vm));
        Assert.Equal("Background,a,a copy,b,c", Order(vm));
        var pasted = vm.Doc.Scene.Layers[2];
        var stroke = Assert.Single(pasted.Cels[0].Frame!.Strokes);
        Assert.NotEqual(vm.Doc.Scene.Layers[1].Cels[0].Frame!.Strokes[0].Id, stroke.Id);
        Assert.Equal(pasted.Id, vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Id);
        Assert.False(vm.HasCelClipboard, "the key also filled the cel clipboard");
    }

    [AvaloniaFact]
    public void ThePasteIsOneUndoStep()
    {
        var (window, vm) = Open();
        var before = Order(vm);
        vm.SelectLayer(RowOf(vm, "a"), toggle: false, range: false);
        DockerRow(window, RowOf(vm, "a")).Focus();
        Pump();
        Press(window, PhysicalKey.C);
        Press(window, PhysicalKey.V);

        vm.UndoCommand.Execute(null);
        Pump();

        Assert.Equal(before, Order(vm));
    }

    [AvaloniaFact]
    public void ASelectionOfSeveralLayersCopiesAndPastesAsOneKeepingItsOrder()
    {
        var (window, vm) = Open();
        vm.SelectLayer(RowOf(vm, "a"), toggle: false, range: false);
        vm.SelectLayer(RowOf(vm, "b"), toggle: true, range: false);
        DockerRow(window, RowOf(vm, "b")).Focus();
        Pump();

        Press(window, PhysicalKey.C);
        Press(window, PhysicalKey.V);

        // Above the active layer, which is the topmost of the two picked.
        Assert.Equal("Background,a,b,a copy,b copy,c", Order(vm));
        Assert.Equal(2, vm.SelectedLayerCount);
        Assert.All(vm.SelectedLayers, l => Assert.EndsWith("copy", l.Name));
    }

    [AvaloniaFact]
    public void PastingTwiceGivesTwoIndependentLayers()
    {
        var (_, vm) = Open();
        vm.SelectLayer(RowOf(vm, "a"), toggle: false, range: false);
        Assert.True(vm.CopyLayers());

        Assert.True(vm.PasteLayers());
        Assert.True(vm.PasteLayers());

        var ids = vm.Doc.Scene.Layers.Select(l => l.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Contains("a copy", vm.Doc.Scene.Layers.Select(l => l.Name));
        Assert.Contains("a copy 2", vm.Doc.Scene.Layers.Select(l => l.Name));
    }

    [AvaloniaFact]
    public void ARightClickMenuCopyOnASelectedRowTakesTheWholeSelection()
    {
        var (_, vm) = Open();
        vm.SelectLayer(RowOf(vm, "a"), toggle: false, range: false);
        vm.SelectLayer(RowOf(vm, "c"), toggle: true, range: false);

        Assert.True(vm.CopyLayers(RowOf(vm, "a").Layer));
        Assert.True(vm.PasteLayers());

        Assert.Equal(2, vm.Doc.Scene.Layers.Count(l => l.Name.EndsWith("copy")));
    }

    [AvaloniaFact]
    public void ARightClickMenuCopyOnAnUnselectedRowTakesOnlyThatRow()
    {
        var (_, vm) = Open();
        vm.SelectLayer(RowOf(vm, "a"), toggle: false, range: false);
        vm.SelectLayer(RowOf(vm, "b"), toggle: true, range: false);

        Assert.True(vm.CopyLayers(RowOf(vm, "c").Layer));
        // What the menu's Paste does: the row it was opened on becomes the place.
        vm.ActivateLayerCommand.Execute(RowOf(vm, "c"));
        Assert.True(vm.PasteLayers());

        Assert.Equal("Background,a,b,c,c copy", Order(vm));
    }

    [AvaloniaFact]
    public void ALayerCopiedInOneDocumentIsRefusedInAnother()
    {
        var (_, vm) = Open();
        vm.SelectLayer(RowOf(vm, "a"), toggle: false, range: false);
        Assert.True(vm.CopyLayers());

        vm.NewDocument(new NewDocumentSettings("Other", 400, 300, 12, 72, "#ffffff", false));
        var before = vm.Doc.Scene.Layers.Count;

        Assert.False(vm.PasteLayers());
        Assert.Equal(before, vm.Doc.Scene.Layers.Count);
        Assert.Contains("another document", vm.AiStatus);
    }

    // ---- the X-sheet ------------------------------------------------------------

    [AvaloniaFact]
    public void CtrlCThenCtrlVOnAnXsheetLayerNamePastesALayer()
    {
        var (window, vm) = Open();
        vm.SelectLayer(RowOf(vm, "a"), toggle: false, range: false);
        NameButton(window, RowOf(vm, "a")).Focus();
        Pump();

        Press(window, PhysicalKey.C);
        Press(window, PhysicalKey.V);

        Assert.Equal("Background,a,a copy,b,c", Order(vm));
    }

    [AvaloniaFact]
    public void CtrlCOnACelIsStillTheCelsAndNeverALayer()
    {
        var (window, vm) = Open();
        vm.SelectLayer(RowOf(vm, "a"), toggle: false, range: false);
        var cel = window.GetVisualDescendants().OfType<Button>()
            .First(b => b.Classes.Contains("cel") && b.DataContext is FrameCell { LayerIndex: 1, Index: 0 });
        cel.Focus();
        Pump();

        Press(window, PhysicalKey.C);

        Assert.True(vm.HasCelClipboard);
        Assert.False(vm.HasLayerClipboard);
    }
}
