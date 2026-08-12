using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Tests;

/// <summary>
/// A character-sheet view can be taped onto the canvas as a see-through
/// reference (Q69): flattened into a <see cref="ReferenceStrip"/>, pinned to
/// every frame, opacity riding the existing reference slider — and live,
/// re-flattened the moment the sheet is edited.
/// </summary>
[Collection("BrushState")]
public class SheetViewOnCanvasTests : BrushStateIsolated
{
    private static (MainViewModel Vm, ReferenceView View, ReferenceSheet Sheet) VmWithAnInkedView()
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

        // Ink the view through the real pipeline: AddReferenceView opened it
        // in a tab, so drawing lands in the view's own layer stack.
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(300, 200, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (vm, view, sheet);
    }

    [AvaloniaFact]
    public void TapingAViewMakesAPinnedLinkedStrip()
    {
        var (vm, view, _) = VmWithAnInkedView();

        var strip = vm.ToggleViewOnCanvas(view);

        Assert.NotNull(strip);
        Assert.Equal(view.Id, strip!.SheetViewId);
        Assert.True(strip.Pinned, "a taped-up sheet shows on every frame");
        Assert.NotEmpty(strip.Png);
        Assert.True(vm.IsViewOnCanvas(view));
    }

    [AvaloniaFact]
    public void TogglingAgainTakesItDown()
    {
        var (vm, view, _) = VmWithAnInkedView();
        vm.ToggleViewOnCanvas(view);

        var second = vm.ToggleViewOnCanvas(view);

        Assert.Null(second);
        Assert.False(vm.IsViewOnCanvas(view));
    }

    [AvaloniaFact]
    public void APinnedStripAnswersEveryFrame()
    {
        var strip = new ReferenceStrip
        {
            Pinned = true,
            Cells = [new ReferenceCell { Width = 96, Height = 54 }],
        };

        // No slots assigned at all — the pin is what makes it visible, on
        // frames far past anything a slot row would cover.
        Assert.NotNull(strip.CellAt(0));
        Assert.NotNull(strip.CellAt(500));
        Assert.Null(new ReferenceStrip { Cells = [new ReferenceCell()] }.CellAt(500));
    }

    [AvaloniaFact]
    public void EditingTheSheetReflattensTheTapedCopy()
    {
        var (vm, view, _) = VmWithAnInkedView();
        var strip = vm.ToggleViewOnCanvas(view)!;
        var before = strip.Png;

        // Draw on the sheet again — the tab is still open and active.
        vm.BeginStroke(400, 300, 1);
        vm.MoveStroke(600, 400, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotEqual(before, strip.Png);
    }

    [AvaloniaFact]
    public void AnUnrelatedEditDoesNotChurnTheStrip()
    {
        var (vm, view, _) = VmWithAnInkedView();
        var strip = vm.ToggleViewOnCanvas(view)!;
        var before = strip.Png;

        // An edit that does not touch the sheet: rename it. The re-flatten
        // runs and must conclude nothing changed — same bytes, same object.
        vm.MarkDocumentEditedForTests();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(before, strip.Png);
    }

    [AvaloniaFact]
    public void ADeletedViewLeavesTheLastPictureStanding()
    {
        var (vm, view, sheet) = VmWithAnInkedView();
        var strip = vm.ToggleViewOnCanvas(view)!;
        var before = strip.Png;

        sheet.Views.Clear();
        vm.MarkDocumentEditedForTests();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Degrades like a missing video file: the pixels stand.
        Assert.Equal(before, strip.Png);
    }

    [AvaloniaFact]
    public void TheLinkAndThePinWriteNoKeysWhenUnused()
    {
        // The serialize-and-look rule: a document whose references never came
        // from a sheet must not carry the new keys.
        var doc = new Doc();
        doc.Scene.References = [new ReferenceStrip { Png = "AA==" }];

        var json = DocJson.Serialize(doc);

        Assert.DoesNotContain("\"sheetViewId\"", json);
    }
}
