using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;

namespace Lightbox.App.Tests;

/// <summary>
/// Picking several layers by their names on the X-sheet, under a real pointer.
/// </summary>
/// <remarks>
/// The layer docker had Ctrl/Shift selection and the X-sheet did not: its name
/// button activates one layer and drops the rest, so a selection built in the
/// docker was lost by the next click on the sheet. These click the button, as
/// the docker's tests do, because the view-model half was never the problem.
/// </remarks>
[Collection("BrushState")]
public class XsheetLayerSelectionTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static void Pump() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow { Width = 1400, Height = 1600 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Sheet", 400, 300, 12, 72, "#ffffff", false));
        while (vm.Doc.Scene.Layers.Count < 4) vm.AddPaintedLayerCommand.Execute(null);
        string[] names = ["a", "b", "c"];
        for (var i = 1; i < vm.Doc.Scene.Layers.Count; i++) vm.Doc.Scene.Layers[i].Name = names[i - 1];
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.Count - 1;
        // The track view is in front by default; the sheet is the other tab.
        vm.Workspace.Activate(Lightbox.App.Docking.DockPanelId.Xsheet);
        Pump();
        return (window, vm);
    }

    private static LayerRow RowOf(MainViewModel vm, string name) => vm.LayerRows.First(r => r.Layer.Name == name);

    private static Button NameButton(Window window, LayerRow row) =>
        window.GetVisualDescendants().OfType<Button>()
            .First(b => b.Classes.Contains("layerName") && ReferenceEquals(b.DataContext, row));

    private static void Click(Window window, Button button, RawInputModifiers mods = RawInputModifiers.None)
    {
        var at = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left, mods);
        window.MouseUp(at, MouseButton.Left, mods);
        Pump();
        window.MouseMove(new Point(700, 5));
        Pump();
    }

    [AvaloniaFact]
    public void CtrlClickOnANameAddsTheLayerAndKeepsTheOthers()
    {
        var (window, vm) = Open();

        Click(window, NameButton(window, RowOf(vm, "b")));
        Click(window, NameButton(window, RowOf(vm, "a")), RawInputModifiers.Control);

        output.WriteLine($"selected {vm.SelectedLayerCount}");
        Assert.Equal(2, vm.SelectedLayerCount);
        Assert.Contains(RowOf(vm, "a").Layer.Id, vm.SelectedLayerIds);
        Assert.Contains(RowOf(vm, "b").Layer.Id, vm.SelectedLayerIds);
        Assert.True(RowOf(vm, "a").IsSelected && RowOf(vm, "b").IsSelected, "the sheet's rows do not show the selection");
    }

    [AvaloniaFact]
    public void ShiftClickOnANameTakesTheRangeFromTheLastPick()
    {
        var (window, vm) = Open();

        Click(window, NameButton(window, RowOf(vm, "c")));
        Click(window, NameButton(window, RowOf(vm, "a")), RawInputModifiers.Shift);

        Assert.Equal(3, vm.SelectedLayerCount); // c, b, a
        Assert.DoesNotContain(vm.Doc.Scene.Layers[0].Id, vm.SelectedLayerIds);
    }

    [AvaloniaFact]
    public void APlainClickStillPicksOneLayerAlone()
    {
        var (window, vm) = Open();
        Click(window, NameButton(window, RowOf(vm, "c")));
        Click(window, NameButton(window, RowOf(vm, "a")), RawInputModifiers.Control);

        Click(window, NameButton(window, RowOf(vm, "b")));

        Assert.Equal(1, vm.SelectedLayerCount);
        Assert.Equal(RowOf(vm, "b").SceneIndex, vm.ActiveLayerIndex);
    }
}
