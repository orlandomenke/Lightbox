using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.Tests;

/// <summary>
/// A plain click on another layer's cell selects that cell and moves the
/// playhead, and leaves the layer being drawn on alone (Q224).
/// </summary>
/// <remarks>
/// <para>
/// It used to do three things at once — go to the frame, pick the cell, and
/// switch the layer you draw on — and the third is the one an artist reading
/// timing across layers did not ask for: one stray click in another row and the
/// next mark lands on the wrong layer.
/// </para>
/// <para>
/// <b>The cost of separating them is that "the cell that is picked" and "the
/// layer being drawn on" can now be different layers</b>, which was never
/// possible before. So most of this file is about the second half of the
/// answer: every timeline verb acts on the cell that is picked — the one
/// highlighted — and only drawing stays with the layer. A Ctrl+C that copied a
/// different cell from the one showing as selected would be the bug this
/// change is most likely to cause.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class XsheetClickKeepsLayerTests(Xunit.ITestOutputHelper output) : BrushStateIsolated
{
    private const int Frames = 6;

    private static void Pump() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    private static FrameCell Cell(MainViewModel vm, int sceneLayer, int index) =>
        vm.LayerRows.First(r => r.SceneIndex == sceneLayer).Cells.First(c => c.Index == index);

    private static Stroke Mark(string colour) => new()
    {
        Tool = ToolKind.Fill,
        Color = colour,
        Brush = new BrushSettings { Opacity = 1 },
        Points = [new(1, 1, 1), new(9, 1, 1), new(9, 9, 1), new(1, 9, 1)],
    };

    /// <summary>
    /// Paper and three drawing layers, six frames, every drawing layer keyed on
    /// every frame with one mark in its own colour. Ends on the top layer.
    /// </summary>
    private static MainViewModel Sheet()
    {
        var vm = VmLayers.PaperVm();
        vm.AddPaintedLayerCommand.Execute(null);
        vm.AddPaintedLayerCommand.Execute(null);
        for (var i = 1; i < Frames; i++) vm.AddFrameCommand.Execute(null);
        string[] colours = ["", "#ff0000", "#00ff00", "#0000ff"];
        for (var layer = 1; layer <= 3; layer++)
        {
            for (var i = 0; i < Frames; i++)
            {
                vm.InsertFrameAt(Cell(vm, layer, i), FrameRole.Key);
                ExposureSheet.FrameAtExactIndex(vm.Doc.Scene.Layers[layer], i)!.Strokes.Add(Mark(colours[layer]));
            }
        }
        vm.ClearCelRange();
        vm.ActiveLayerIndex = Top;
        vm.CurrentFrameIndex = 0;
        return vm;
    }

    private const int Bottom = 1, Middle = 2, Top = 3;

    private static int Marks(MainViewModel vm, int layer, int index) =>
        ExposureSheet.FrameAtExactIndex(vm.Doc.Scene.Layers[layer], index)?.Strokes.Count ?? -1;

    private static string? ColourAt(MainViewModel vm, int layer, int index) =>
        ExposureSheet.FrameAtExactIndex(vm.Doc.Scene.Layers[layer], index)?.Strokes.FirstOrDefault()?.Color;

    // ---- the click -------------------------------------------------------------

    [AvaloniaFact]
    public void APlainClickOnAnotherLayersCellKeepsTheLayerYouDrawOn()
    {
        var vm = Sheet();

        vm.SelectFrameCommand.Execute(Cell(vm, Bottom, 3));

        Assert.Equal(Top, vm.ActiveLayerIndex);
        Assert.Equal(3, vm.CurrentFrameIndex);
        Assert.Equal([(Bottom, 3)], vm.CelSelection.ToArray());
        Assert.True(Cell(vm, Bottom, 3).IsSelected);
    }

    [AvaloniaFact]
    public void TheClickedCellReplacesWhateverWasSelected()
    {
        var vm = Sheet();
        vm.ToggleCelSelection(Cell(vm, Middle, 0));
        vm.ToggleCelSelection(Cell(vm, Middle, 1));

        vm.SelectFrameCommand.Execute(Cell(vm, Bottom, 4));

        Assert.Equal([(Bottom, 4)], vm.CelSelection.ToArray());
    }

    [AvaloniaFact]
    public void AClickInYourOwnLayersRowIsWhatItAlwaysWas()
    {
        // Nothing to keep apart there: the cell picked and the layer drawn on
        // are the same layer, so the playhead's cel is the pick, as before.
        var vm = Sheet();
        vm.ToggleCelSelection(Cell(vm, Middle, 1));

        vm.SelectFrameCommand.Execute(Cell(vm, Top, 2));

        Assert.Equal(Top, vm.ActiveLayerIndex);
        Assert.Equal(2, vm.CurrentFrameIndex);
        Assert.Empty(vm.CelSelection);
    }

    [AvaloniaFact]
    public void ShiftClickRangesFromTheCellThatWasClicked()
    {
        var vm = Sheet();
        vm.SelectFrameCommand.Execute(Cell(vm, Bottom, 1));

        vm.RangeSelectTo(Cell(vm, Middle, 3));

        Assert.Equal(Top, vm.ActiveLayerIndex);
        Assert.Equal(6, vm.CelSelection.Count);
        Assert.All(vm.CelSelection, c => Assert.True(c.Layer is Bottom or Middle && c.Index is >= 1 and <= 3));
    }

    [AvaloniaFact]
    public void AHatchedCellOnAnotherLayerMovesThePlayheadAndPicksNothing()
    {
        // Past the end there is no cel to pick (Q103); standing there is still
        // allowed, and still does not change the layer.
        var vm = Sheet();
        var hatched = vm.LayerRows.First(r => r.SceneIndex == Bottom).Cells.First(c => c.IsVirtual);

        vm.SelectFrameCommand.Execute(hatched);

        Assert.Equal(Top, vm.ActiveLayerIndex);
        Assert.Equal(hatched.Index, vm.CurrentFrameIndex);
        Assert.Empty(vm.CelSelection);
    }

    [AvaloniaFact]
    public void DoubleClickingACellGoesThereAndSwitchesToItsLayer()
    {
        var vm = Sheet();
        vm.ToggleCelSelection(Cell(vm, Middle, 1));

        vm.ActivateCel(Cell(vm, Bottom, 4));

        Assert.Equal(Bottom, vm.ActiveLayerIndex);
        Assert.Equal(4, vm.CurrentFrameIndex);
        Assert.Empty(vm.CelSelection);
    }

    // ---- drawing stays with the layer -------------------------------------------

    [AvaloniaFact]
    public void TheNextMarkLandsOnTheLayerYouWereOnNotTheOneYouClicked()
    {
        // The reason for the whole change.
        var vm = Sheet();
        vm.SelectFrameCommand.Execute(Cell(vm, Bottom, 2));

        vm.BeginStroke(40, 40, 1);
        vm.MoveStroke(80, 60, 1);
        vm.EndStroke();
        Pump();

        output.WriteLine($"marks at frame 2 — top {Marks(vm, Top, 2)}, bottom {Marks(vm, Bottom, 2)}");
        Assert.Equal(2, Marks(vm, Top, 2));
        Assert.Equal(1, Marks(vm, Bottom, 2));
    }

    // ---- every verb acts on the cell that is picked ----------------------------

    [AvaloniaFact]
    public void DeleteTakesTheClickedCell()
    {
        var vm = Sheet();
        vm.SelectFrameCommand.Execute(Cell(vm, Bottom, 2));

        vm.ClearCelAtPlayhead();

        Assert.Equal(1, Marks(vm, Top, 2));
        Assert.NotEqual(1, Marks(vm, Bottom, 2));
    }

    [AvaloniaFact]
    public void CopyTakesTheClickedCell()
    {
        var vm = Sheet();
        vm.SelectFrameCommand.Execute(Cell(vm, Bottom, 2));

        vm.CopyCurrentCel();
        // Paste it somewhere on the layer being drawn on, to see what was copied.
        vm.SelectFrameCommand.Execute(Cell(vm, Top, 5));
        vm.PasteCurrentCel();

        output.WriteLine($"pasted onto the top layer at 5: {ColourAt(vm, Top, 5)}");
        Assert.Equal("#ff0000", ColourAt(vm, Top, 5));
    }

    [AvaloniaFact]
    public void CutTakesTheClickedCell()
    {
        var vm = Sheet();
        vm.SelectFrameCommand.Execute(Cell(vm, Middle, 3));

        vm.CutCurrentCel();

        Assert.Equal("#0000ff", ColourAt(vm, Top, 3));
        Assert.NotEqual("#00ff00", ColourAt(vm, Middle, 3));
    }

    [AvaloniaFact]
    public void PasteLandsOnTheClickedCell()
    {
        var vm = Sheet();
        vm.SelectFrameCommand.Execute(Cell(vm, Top, 0));
        vm.CopyCurrentCel();

        vm.SelectFrameCommand.Execute(Cell(vm, Bottom, 4));
        vm.PasteCurrentCel();

        Assert.Equal("#0000ff", ColourAt(vm, Bottom, 4));
        Assert.Equal("#0000ff", ColourAt(vm, Top, 4));
        Assert.Equal(Top, vm.ActiveLayerIndex);
    }

    [AvaloniaFact]
    public void ExtendingAnExposureExtendsTheClickedCellsLayer()
    {
        var vm = Sheet();
        var before = (Bottom: vm.Doc.Scene.Layers[Bottom].Cels.Count, Top: vm.Doc.Scene.Layers[Top].Cels.Count);
        vm.SelectFrameCommand.Execute(Cell(vm, Bottom, 2));

        vm.ExtendExposureAtPlayhead();

        var after = (Bottom: vm.Doc.Scene.Layers[Bottom].Cels.Count, Top: vm.Doc.Scene.Layers[Top].Cels.Count);
        output.WriteLine($"cels before {before}, after {after}");
        Assert.Equal(before.Bottom + 1, after.Bottom);
        Assert.Equal(before.Top, after.Top);
    }

    [AvaloniaFact]
    public void ReducingAnExposureReducesTheClickedCellsLayer()
    {
        var vm = Sheet();
        vm.SelectFrameCommand.Execute(Cell(vm, Bottom, 2));
        vm.ExtendExposureAtPlayhead();
        var before = (Bottom: vm.Doc.Scene.Layers[Bottom].Cels.Count, Top: vm.Doc.Scene.Layers[Top].Cels.Count);
        vm.SelectFrameCommand.Execute(Cell(vm, Bottom, 2));

        vm.ReduceExposureAtPlayhead();

        Assert.Equal(before.Bottom - 1, vm.Doc.Scene.Layers[Bottom].Cels.Count);
        Assert.Equal(before.Top, vm.Doc.Scene.Layers[Top].Cels.Count);
    }

    [AvaloniaFact]
    public void MarkingABreakdownMarksTheClickedCell()
    {
        var vm = Sheet();
        vm.SelectFrameCommand.Execute(Cell(vm, Bottom, 2));

        vm.InsertFrameAtPlayhead(FrameRole.Breakdown);

        Assert.Equal(FrameRole.Breakdown, ExposureSheet.FrameAtExactIndex(vm.Doc.Scene.Layers[Bottom], 2)!.Role);
        Assert.Equal(FrameRole.Key, ExposureSheet.FrameAtExactIndex(vm.Doc.Scene.Layers[Top], 2)!.Role);
    }

    [AvaloniaFact]
    public void WithNothingPickedEveryVerbStillMeansThePlayheadOnYourLayer()
    {
        // The other direction: none of the verbs above may have stopped
        // working the way they always did when no cell is picked.
        var vm = Sheet();
        vm.CurrentFrameIndex = 1;

        vm.InsertFrameAtPlayhead(FrameRole.Breakdown);
        vm.CopyCurrentCel();
        vm.CurrentFrameIndex = 5;
        vm.PasteCurrentCel();

        Assert.Equal(FrameRole.Breakdown, ExposureSheet.FrameAtExactIndex(vm.Doc.Scene.Layers[Top], 1)!.Role);
        Assert.Equal("#0000ff", ColourAt(vm, Top, 5));
        Assert.Equal(1, Marks(vm, Bottom, 5));
    }

    // ---- the real gestures -----------------------------------------------------

    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow { Width = 1400, Height = 1600 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Sheet", 400, 300, 12, 72, "#ffffff", false));
        while (vm.Doc.Scene.Layers.Count < 4) vm.AddPaintedLayerCommand.Execute(null);
        for (var i = 1; i < Frames; i++) vm.AddFrameCommand.Execute(null);
        vm.ActiveLayerIndex = Top;
        vm.Workspace.Activate(Lightbox.App.Docking.DockPanelId.Xsheet);
        Pump();
        return (window, vm);
    }

    private static Point CentreOfCell(MainWindow window, FrameCell cell)
    {
        var button = window.GetVisualDescendants().OfType<Button>().First(b => ReferenceEquals(b.DataContext, cell));
        return button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
    }

    [AvaloniaFact]
    public void ARealClickInTheSheetKeepsTheLayer()
    {
        var (window, vm) = Open();
        var at = CentreOfCell(window, Cell(vm, Bottom, 2));

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Pump();

        Assert.Equal(Top, vm.ActiveLayerIndex);
        Assert.Equal(2, vm.CurrentFrameIndex);
        Assert.Equal([(Bottom, 2)], vm.CelSelection.ToArray());
    }

    [AvaloniaFact]
    public void ARealDoubleClickInTheSheetSwitchesLayer()
    {
        var (window, vm) = Open();
        var at = CentreOfCell(window, Cell(vm, Bottom, 2));

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Pump();

        Assert.Equal(Bottom, vm.ActiveLayerIndex);
        Assert.Equal(2, vm.CurrentFrameIndex);
        Assert.Empty(vm.CelSelection);
    }
}
