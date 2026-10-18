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

    /// <summary>
    /// B491. The canvas and the export draw from the registry, not from
    /// <see cref="ReferenceStrip.Png"/>, so a re-flatten that only updates the
    /// record leaves the first picture on screen until the document reopens.
    /// </summary>
    [AvaloniaFact]
    public void EditingTheSheetChangesWhatTheCanvasDraws()
    {
        var (vm, view, _) = VmWithAnInkedView();
        var strip = vm.ToggleViewOnCanvas(view)!;
        var before = SheetPixels(AnimationTab(vm).Doc.Scene, strip.Id);

        vm.BeginStroke(400, 300, 1);
        vm.MoveStroke(600, 400, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var after = SheetPixels(AnimationTab(vm).Doc.Scene, strip.Id);
        var fromRecord = Lightbox.Raster.PngCodec.Decode(strip.Png);
        Assert.NotEqual(before, after);
        Assert.Equal(fromRecord.Bytes, after);
    }

    private static byte[] SheetPixels(Scene scene, string id)
    {
        var sheet = Lightbox.Raster.ReferenceStripRegistry.Resolve(scene, id);
        Assert.NotNull(sheet);
        return sheet!.Bytes;
    }

    private static DocumentTab AnimationTab(MainViewModel vm) =>
        vm.Tabs.First(t => t.Kind == DocumentTabKind.Animation);

    private static void InkTheOpenView(MainViewModel vm)
    {
        vm.BeginStroke(400, 300, 1);
        vm.MoveStroke(600, 400, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// B490. Close a document with an unsaved edit to a taped sheet and open
    /// the file again: the canvas must draw what the file holds. The load path
    /// skipped any id the registry still held, so the edit nobody saved stayed
    /// on screen while the record said otherwise.
    /// </summary>
    [AvaloniaFact]
    public void ReopeningAnUnsavedEditShowsTheSavedReference()
    {
        var (vm, view, _) = VmWithAnInkedView();
        var strip = vm.ToggleViewOnCanvas(view)!;
        var owner = AnimationTab(vm);
        var onDisk = DocJson.Clone(owner.Doc);
        var saved = Lightbox.Raster.PngCodec.Decode(onDisk.Scene.References![0].Png).Bytes;

        InkTheOpenView(vm);
        Assert.NotEqual(saved, SheetPixels(owner.Doc.Scene, strip.Id));
        vm.CloseTab(owner);

        vm.OpenDocumentTab(onDisk, "/projects/hero.lightbox.json");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(saved, SheetPixels(vm.Doc.Scene, strip.Id));
    }

    /// <summary>
    /// B490. A copy carries the original's strip ids. Editing the copy's sheet
    /// must change what the copy draws and leave the original as it was.
    /// </summary>
    [AvaloniaFact]
    public void ACopysTapedSheetDoesNotDrawOnTheOriginal()
    {
        var (vm, view, _) = VmWithAnInkedView();
        var strip = vm.ToggleViewOnCanvas(view)!;
        var original = AnimationTab(vm);
        var before = SheetPixels(original.Doc.Scene, strip.Id);

        vm.OpenDocumentTab(DocJson.Clone(original.Doc), null);
        var copy = vm.ActiveTab!;
        Assert.NotSame(original, copy);
        vm.OpenReferenceView(copy.Doc.ReferenceSheets[0].Views[0]);
        InkTheOpenView(vm);

        Assert.NotEqual(before, SheetPixels(copy.Doc.Scene, strip.Id));
        Assert.Equal(before, SheetPixels(original.Doc.Scene, strip.Id));
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

    /// <summary>
    /// B325. A stroke on the animation canvas cannot change a character sheet,
    /// so it must not pay to find that out.
    /// </summary>
    /// <remarks>
    /// The re-flatten used to render, PNG-encode and base64 the view and only
    /// then compare the bytes, so the whole cost landed on every edit anywhere:
    /// measured at 7.5 ms per stroke with nothing taped up, 26.6 with one view
    /// and 44.5 with two. Counted rather than timed — what the fix is about is
    /// whether the work happens, and a millisecond assertion would only measure
    /// the machine it ran on.
    /// </remarks>
    [AvaloniaFact]
    public void DrawingOnTheCanvasDoesNotReflattenATapedView()
    {
        var (vm, view, _) = VmWithAnInkedView();
        vm.ToggleViewOnCanvas(view);

        // Back to the animation tab, which is where the artist draws against
        // the reference they have just taped up.
        vm.ActiveTab = vm.Tabs[0];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var flattens = vm.LinkedStripFlattens;

        for (var i = 0; i < 5; i++)
        {
            vm.BeginStroke(100 + i, 100, 1);
            vm.MoveStroke(300 + i, 200, 1);
            vm.EndStroke();
        }
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(flattens, vm.LinkedStripFlattens);
    }

    /// <summary>
    /// B325's other half: the gate must not swallow a change the artist made.
    /// </summary>
    [AvaloniaFact]
    public void DrawingOnTheSheetStillReflattensIt()
    {
        var (vm, view, _) = VmWithAnInkedView();
        vm.ToggleViewOnCanvas(view);
        var flattens = vm.LinkedStripFlattens;

        vm.BeginStroke(400, 300, 1);
        vm.MoveStroke(600, 400, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(
            vm.LinkedStripFlattens > flattens,
            "a stroke on the sheet is exactly the edit the taped copy has to follow");
    }

    /// <summary>
    /// Hiding a layer changes the picture without adding a stroke, and the
    /// layer opacity slider changes it without an undo step at all — so the
    /// gate cannot be the editor's revision alone.
    /// </summary>
    [AvaloniaFact]
    public void HidingALayerOnTheSheetReflattensIt()
    {
        var (vm, view, _) = VmWithAnInkedView();
        vm.ToggleViewOnCanvas(view);
        var flattens = vm.LinkedStripFlattens;

        view.Layers[0].Visible = false;
        vm.MarkDocumentEditedForTests();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(vm.LinkedStripFlattens > flattens, "the view no longer looks the same");
    }

    /// <summary>
    /// An undo of a structural edit on the owning document restores a cloned
    /// <c>ReferenceSheets</c>, so the view behind a taped strip becomes a
    /// different object — which is the one way an edit made on an animation tab
    /// reaches a sheet, and what the document-identity gate is keyed on.
    /// </summary>
    /// <remarks>
    /// A layer add rather than a stroke, because the two undo differently and
    /// the difference is the gate's whole argument. A stroke is a
    /// <c>DeltaStep</c>, whose rollback mutates the document in place and
    /// returns the same object; it cannot touch a sheet and correctly does not
    /// reopen the gate. A layer add is a snapshot, and its rollback *swaps* the
    /// object.
    /// </remarks>
    [AvaloniaFact]
    public void AnUndoOnTheOwningDocumentReflattensTheTapedView()
    {
        var (vm, view, _) = VmWithAnInkedView();
        vm.ToggleViewOnCanvas(view);

        vm.ActiveTab = vm.Tabs[0];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        // Something to undo that is not the taping itself — undoing that would
        // take the strip off the canvas and leave nothing to re-flatten.
        vm.AddPaintedLayerCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var flattens = vm.LinkedStripFlattens;

        vm.UndoCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(
            vm.LinkedStripFlattens > flattens,
            "undo swaps the document object, and the sheets inside it with it");
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
