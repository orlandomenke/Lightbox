using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// A character-sheet view in a window of its own (Q69): a live viewer beside
/// the art — it follows the sheet as it is edited, and drawing on it still
/// happens in the tab.
/// </summary>
[Collection("BrushState")]
public class ReferenceViewWindowTests : BrushStateIsolated
{
    private static (MainViewModel Vm, ReferenceView View) VmWithAnInkedView()
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 12;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;

        var sheet = vm.AddReferenceSheet("Hero")!;
        vm.AddReferenceView(sheet);
        var view = sheet.Views[0];
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(300, 200, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (vm, view);
    }

    [AvaloniaFact]
    public void TheWindowShowsThePictureAndNamesItsSheet()
    {
        var (vm, view) = VmWithAnInkedView();

        var window = new ReferenceViewWindow(vm, view, "Hero");

        Assert.Contains("Hero", window.Title);
        Assert.Contains(view.Name, window.Title!);
        var image = Assert.IsType<Image>(window.Content);
        Assert.NotNull(image.Source);
        window.Close();
    }

    [AvaloniaFact]
    public void EditingTheSheetUpdatesTheWindow()
    {
        var (vm, view) = VmWithAnInkedView();
        var window = new ReferenceViewWindow(vm, view, "Hero");
        var image = (Image)window.Content!;
        var before = image.Source;

        vm.BeginStroke(400, 300, 1);
        vm.MoveStroke(600, 400, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotSame(before, image.Source);
        window.Close();
    }

    [AvaloniaFact]
    public void AnUnrelatedEditDoesNotChurnTheWindow()
    {
        var (vm, view) = VmWithAnInkedView();
        var window = new ReferenceViewWindow(vm, view, "Hero");
        var image = (Image)window.Content!;
        var before = image.Source;

        // The funnel fires, the picture did not change — the bitmap instance
        // must not be replaced, or every edit anywhere flickers the window.
        vm.MarkDocumentEditedForTests();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Same(before, image.Source);
        window.Close();
    }

    [AvaloniaFact]
    public void AClosedWindowStopsListening()
    {
        var (vm, view) = VmWithAnInkedView();
        var window = new ReferenceViewWindow(vm, view, "Hero");
        var image = (Image)window.Content!;
        window.Close();
        var after = image.Source;

        vm.BeginStroke(500, 300, 1);
        vm.MoveStroke(700, 300, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // The unsubscribe on Closed is what this pins: a window that keeps
        // rendering after it is gone is a leak wearing a feature's name.
        Assert.Same(after, image.Source);
    }
}
