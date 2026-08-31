using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Raster;

namespace Lightbox.App.Tests;

/// <summary>
/// Q173: making a symbol out of part of a drawing.
/// </summary>
/// <remarks>
/// The gesture an artist reaches for mid-drawing — the sword is already drawn,
/// inside a bigger picture, and it should have been a symbol. The load-bearing
/// assertions here are the two the decision turned on: what the drawing keeps
/// at the edge, and whether the symbol carries the clip its strokes reference.
/// </remarks>
public class SymbolFromSelectionTests : IDisposable
{
    private readonly string _root = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), $"lightbox-sel-{Guid.NewGuid():N}.lbproj");

    public void Dispose()
    {
        SymbolRegistry.Clear();
        if (System.IO.Directory.Exists(_root)) System.IO.Directory.Delete(_root, recursive: true);
    }

    private static Stroke Bar(double y, double x1 = 20, double x2 = 120) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#c02040",
        Points = [new StrokePoint(x1, y, 1), new StrokePoint(x2, y, 1)],
        Brush = new BrushSettings { Size = 8, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
    };

    private MainViewModel Painted(params Stroke[] strokes)
    {
        var vm = VmLayers.PaperVm();
        vm.ProjectDocker.Project = ProjectIo.Create("Knight", _root);
        vm.RefreshProjectResources();
        var frame = (Frame)vm.Doc.Scene.Layers[^1].Cels[0].Frame!;
        frame.Strokes.AddRange(strokes);
        return vm;
    }

    private static Frame FrameOf(MainViewModel vm) =>
        (Frame)vm.Doc.Scene.Layers[^1].Cels[0].Frame!;

    /// <summary>A rectangular selection, as the marquee would make one.</summary>
    private static void Rect(MainViewModel vm, double x, double y, double w, double h) =>
        vm.ApplySelectionShape(
            [
                new StrokePoint(x, y, 1), new StrokePoint(x + w, y, 1),
                new StrokePoint(x + w, y + h, 1), new StrokePoint(x, y + h, 1),
            ],
            add: false, subtract: false);

    // ---- picked lines ------------------------------------------------------------

    [AvaloniaFact]
    public void PickedLinesBecomeASymbolAndLeaveTheRestAlone()
    {
        var sword = Bar(40);
        var keep = Bar(120);
        var vm = Painted(sword, keep);
        vm.Selection.SelectStrokes([sword.Id]);

        var symbol = vm.MakeSymbolFromSelection("Sword");

        Assert.NotNull(symbol);
        Assert.Single(symbol!.AllFrames.First().Strokes);
        var frame = FrameOf(vm);
        // The picked line is gone and the other is untouched.
        Assert.DoesNotContain(frame.Strokes, k => k.Id == sword.Id);
        Assert.Contains(frame.Strokes, k => k.Id == keep.Id);
        Assert.NotNull(frame.Placements);
        Assert.Single(frame.Placements!);
    }

    [AvaloniaFact]
    public void TheSymbolOwnsItsStrokesRatherThanSharingTheDrawings()
    {
        // Picked lines come back as the drawing's own objects, so a capture that
        // did not clone would leave the symbol and the drawing holding one
        // stroke between them — and an edit to either would move the other.
        var sword = Bar(40);
        var vm = Painted(sword);
        vm.Selection.SelectStrokes([sword.Id]);

        var symbol = vm.MakeSymbolFromSelection("Sword")!;

        Assert.NotSame(sword, symbol.AllFrames.First().Strokes[0]);
    }

    [AvaloniaFact]
    public void MakingASymbolFromASelectionIsOneUndoStep()
    {
        var sword = Bar(40);
        var vm = Painted(sword, Bar(120));
        vm.Selection.SelectStrokes([sword.Id]);
        vm.MakeSymbolFromSelection("Sword");

        vm.UndoCommand.Execute(null);

        var frame = FrameOf(vm);
        Assert.Null(frame.Placements);
        Assert.Contains(frame.Strokes, k => k.Id == sword.Id);
    }

    // ---- a boxed region ----------------------------------------------------------

    /// <summary>
    /// A marquee capture carries the clip its strokes reference.
    /// </summary>
    /// <remarks>
    /// <b>Q173, and the reason the decision was not free.</b> Clip regions live
    /// in <c>Doc.ClipRegions</c> and reach the renderer from the *active
    /// document*; a symbol is placed into documents it was not made in. Without
    /// this the sword would resolve its clip against whatever happened to be
    /// open — the wrong shape, or nothing.
    /// </remarks>
    [AvaloniaFact]
    public void AMarqueeCaptureCarriesTheClipItsStrokesReference()
    {
        var vm = Painted(Bar(40, x1: 10, x2: 190));
        Rect(vm, 60, 20, 60, 40);

        var symbol = vm.MakeSymbolFromSelection("Sword")!;

        var clipped = symbol.AllFrames.First().Strokes;
        Assert.NotEmpty(clipped);
        Assert.All(clipped, k => Assert.NotNull(k.ClipId));
        Assert.True(symbol.HasClipRegions, "the symbol carries no regions for the clips its strokes name");
        foreach (var k in clipped)
        {
            Assert.True(
                symbol.ClipRegions!.ContainsKey(k.ClipId!),
                $"stroke names clip {k.ClipId} and the symbol does not carry it");
        }
    }

    [AvaloniaFact]
    public void ALineCrossingTheEdgeKeepsThePartOutsideTheBox()
    {
        // The existing answer, not a new one: a boxed region leaves a carve
        // behind so the outside survives, where picking a line removes it whole.
        var across = Bar(40, x1: 10, x2: 190);
        var vm = Painted(across);
        Rect(vm, 60, 20, 60, 40);

        vm.MakeSymbolFromSelection("Sword");

        var frame = FrameOf(vm);
        // The original line is still there; a ClearRegion stroke carves the box.
        Assert.Contains(frame.Strokes, k => k.Id == across.Id);
        Assert.Contains(frame.Strokes, k => k.Tool == ToolKind.ClearRegion);
    }

    [AvaloniaFact]
    public void ASymbolMadeFromWholeLinesCarriesNoRegions()
    {
        // Absent unless used: picking lines clips nothing, so nothing is carried
        // and the file grows no key.
        var sword = Bar(40);
        var vm = Painted(sword);
        vm.Selection.SelectStrokes([sword.Id]);

        var symbol = vm.MakeSymbolFromSelection("Sword")!;

        Assert.False(symbol.HasClipRegions);
        Assert.Null(symbol.ClipRegions);
    }

    [AvaloniaFact]
    public void NothingSelectedMakesNoSymbol()
    {
        var vm = Painted(Bar(40));

        Assert.Null(vm.MakeSymbolFromSelection("Sword"));
        Assert.Null(FrameOf(vm).Placements);
    }
}
