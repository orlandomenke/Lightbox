using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Raster;

namespace Lightbox.App.Tests;

/// <summary>
/// Pillar 3, step four: putting a symbol down, moving it, and letting go of
/// the link.
/// </summary>
/// <remarks>
/// Driven at the view model, because that is where the operations live — the
/// browser in S5 is a way of calling them. Each one is a single undo step, and
/// each test says so, because "it worked but took three undos to take back" is
/// the failure an artist notices.
/// </remarks>
public class SymbolPlacingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-place-{Guid.NewGuid():N}.lbproj");

    public void Dispose()
    {
        SymbolRegistry.Clear();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static Stroke Bar(double x, double y, string? swatchId = null) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#c02040",
        SwatchId = swatchId,
        Points = [new StrokePoint(x, y, 1), new StrokePoint(x + 40, y, 1)],
        Brush = new BrushSettings { Size = 10, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
    };

    /// <summary>A symbol whose ink runs from (0, 0) to about (40, 0) in its own space.</summary>
    private static Symbol Sword(string? swatchId = null) => new()
    {
        Name = "Sword",
        PivotX = 0,
        PivotY = 0,
        Frames = [new PaintedFrame { Strokes = [Bar(0, 0, swatchId)] }],
    };

    private MainViewModel WithSymbol(out Symbol symbol)
    {
        var vm = new MainViewModel(null);
        var project = ProjectIo.Create("Knight", _root);
        symbol = Sword();
        project.Symbols[symbol.Id] = symbol;
        vm.ProjectDocker.Project = project;
        vm.RefreshProjectResources();
        return vm;
    }

    private static PaintedFrame FrameOf(MainViewModel vm) =>
        (PaintedFrame)vm.Doc.Scene.Layers[^1].Cels[0].Frame!;

    // ---- putting one down -----------------------------------------------------

    [AvaloniaFact]
    public void PlacingASymbolPutsItOnTheCel()
    {
        var vm = WithSymbol(out var sword);

        var placement = vm.PlaceSymbol(sword.Id, 100, 60);

        Assert.NotNull(placement);
        Assert.Equal(sword.Id, Assert.Single(FrameOf(vm).Placements!).SymbolId);
        Assert.Equal(100, placement!.X);
    }

    [AvaloniaFact]
    public void APlacementRecordsWhatItWasPlacedAgainst()
    {
        var vm = WithSymbol(out var sword);
        sword.Version = 7;

        var placement = vm.PlaceSymbol(sword.Id, 100, 60);

        Assert.Equal(7, placement!.SeenVersion);
    }

    [AvaloniaFact]
    public void PlacingIsOneUndoStep()
    {
        var vm = WithSymbol(out var sword);
        vm.PlaceSymbol(sword.Id, 100, 60);

        vm.UndoCommand.Execute(null);

        // Absent, not empty: undoing back past the first placement has to leave
        // the cel serializing as one that never placed anything.
        Assert.Null(FrameOf(vm).Placements);
    }

    [AvaloniaFact]
    public void PlacingAnUnknownSymbolDoesNothingAndSaysSo()
    {
        var vm = WithSymbol(out _);

        var placement = vm.PlaceSymbol("sym_gone", 10, 10);

        Assert.Null(placement);
        Assert.Null(FrameOf(vm).Placements);
        Assert.Contains("not in this project", vm.AiStatus);
    }

    [AvaloniaFact]
    public void ALockedLayerRefusesAPlacement()
    {
        var vm = WithSymbol(out var sword);
        vm.ActiveLayerForIpc.Locked = true;

        Assert.Null(vm.PlaceSymbol(sword.Id, 100, 60));
    }

    [AvaloniaFact]
    public void RemovingAPlacementLeavesTheSymbolAlone()
    {
        var vm = WithSymbol(out var sword);
        var placement = vm.PlaceSymbol(sword.Id, 100, 60)!;

        Assert.True(vm.RemovePlacement(placement));

        Assert.Null(FrameOf(vm).Placements);
        Assert.Same(sword, SymbolRegistry.Resolve(sword.Id));
    }

    [AvaloniaFact]
    public void RemovingIsOneUndoStepAndKeepsTheOrder()
    {
        // Undo has to put it back where it was, or the drawing order changes
        // and what covers what silently swaps.
        var vm = WithSymbol(out var sword);
        var first = vm.PlaceSymbol(sword.Id, 20, 20)!;
        var second = vm.PlaceSymbol(sword.Id, 80, 20)!;
        vm.RemovePlacement(first);

        vm.UndoCommand.Execute(null);

        Assert.Equal([first.Id, second.Id], FrameOf(vm).Placements!.Select(p => p.Id));
    }

    // ---- picking one up --------------------------------------------------------

    [AvaloniaFact]
    public void APlacementCanBeFoundUnderThePointer()
    {
        var vm = WithSymbol(out var sword);
        var placement = vm.PlaceSymbol(sword.Id, 100, 60)!;

        Assert.Equal(placement.Id, vm.PlacementAt(110, 60)?.Id);
    }

    [AvaloniaFact]
    public void EmptyCanvasUnderThePointerFindsNothing()
    {
        var vm = WithSymbol(out var sword);
        vm.PlaceSymbol(sword.Id, 100, 60);

        Assert.Null(vm.PlacementAt(400, 400));
    }

    [AvaloniaFact]
    public void TheTopmostPlacementWins()
    {
        // They draw in record order, so the one you can see is the one you
        // meant.
        var vm = WithSymbol(out var sword);
        vm.PlaceSymbol(sword.Id, 100, 60);
        var above = vm.PlaceSymbol(sword.Id, 105, 60)!;

        Assert.Equal(above.Id, vm.PlacementAt(110, 60)?.Id);
    }

    // ---- moving one -------------------------------------------------------------

    [AvaloniaFact]
    public void TheMoveToolGrabsAPlacementBeforeTheDrawing()
    {
        var vm = WithSymbol(out var sword);
        vm.PlaceSymbol(sword.Id, 100, 60);

        Assert.True(vm.BeginMove(110, 60, wholeLayer: false));

        Assert.True(vm.PlacementMoveActive);
        Assert.False(vm.TransformActive);
    }

    [AvaloniaFact]
    public void TheMoveToolStillMovesTheDrawingAwayFromAPlacement()
    {
        var vm = WithSymbol(out var sword);
        FrameOf(vm).Strokes.Add(Bar(300, 300));
        vm.PlaceSymbol(sword.Id, 100, 60);

        Assert.True(vm.BeginMove(310, 300, wholeLayer: false));

        Assert.False(vm.PlacementMoveActive);
        Assert.True(vm.TransformActive);
    }

    [AvaloniaFact]
    public void DraggingAPlacementMovesIt()
    {
        var vm = WithSymbol(out var sword);
        var placement = vm.PlaceSymbol(sword.Id, 100, 60)!;

        vm.BeginMove(110, 60, wholeLayer: false);
        vm.UpdateMove(150, 90, axisLock: false);
        vm.EndMove();

        Assert.Equal(140, placement.X);
        Assert.Equal(90, placement.Y);
    }

    [AvaloniaFact]
    public void ShiftHoldsThePlacementToOneAxis()
    {
        var vm = WithSymbol(out var sword);
        var placement = vm.PlaceSymbol(sword.Id, 100, 60)!;

        vm.BeginMove(110, 60, wholeLayer: false);
        vm.UpdateMove(150, 70, axisLock: true);
        vm.EndMove();

        Assert.Equal(140, placement.X);
        Assert.Equal(60, placement.Y);
    }

    [AvaloniaFact]
    public void MovingAPlacementIsOneUndoStep()
    {
        var vm = WithSymbol(out var sword);
        vm.PlaceSymbol(sword.Id, 100, 60);

        vm.BeginMove(110, 60, wholeLayer: false);
        vm.UpdateMove(130, 60, axisLock: false);
        vm.UpdateMove(150, 90, axisLock: false);
        vm.EndMove();
        vm.UndoCommand.Execute(null);

        var placement = Assert.Single(FrameOf(vm).Placements!);
        Assert.Equal(100, placement.X);
        Assert.Equal(60, placement.Y);
    }

    [AvaloniaFact]
    public void MovingAPlacementDoesNotTouchTheSymbol()
    {
        // The whole point. Two placements of one sword, and dragging one must
        // not drag the other.
        var vm = WithSymbol(out var sword);
        var first = vm.PlaceSymbol(sword.Id, 40, 40)!;
        var second = vm.PlaceSymbol(sword.Id, 200, 40)!;
        var before = ((PaintedFrame)sword.Frames[0]).Strokes[0].Points[0].X;

        vm.BeginMove(50, 40, wholeLayer: false);
        vm.UpdateMove(90, 40, axisLock: false);
        vm.EndMove();

        Assert.Equal(80, first.X);
        Assert.Equal(200, second.X);
        Assert.Equal(before, ((PaintedFrame)sword.Frames[0]).Strokes[0].Points[0].X);
    }

    [AvaloniaFact]
    public void AClickThatWentNowhereIsNotAnEdit()
    {
        var vm = WithSymbol(out var sword);
        vm.PlaceSymbol(sword.Id, 100, 60);
        var steps = UndoDepth(vm);

        vm.BeginMove(110, 60, wholeLayer: false);
        vm.EndMove();

        Assert.Equal(steps, UndoDepth(vm));
    }

    [AvaloniaFact]
    public void CancellingAMovePutsItBack()
    {
        var vm = WithSymbol(out var sword);
        var placement = vm.PlaceSymbol(sword.Id, 100, 60)!;

        vm.BeginMove(110, 60, wholeLayer: false);
        vm.UpdateMove(150, 90, axisLock: false);
        vm.CancelMove();

        Assert.Equal(100, placement.X);
        Assert.Equal(60, placement.Y);
        Assert.False(vm.PlacementMoveActive);
    }

    // ---- letting go of the link --------------------------------------------------

    [AvaloniaFact]
    public void BreakingTheLinkLeavesOrdinaryStrokes()
    {
        var vm = WithSymbol(out var sword);
        var placement = vm.PlaceSymbol(sword.Id, 100, 60)!;

        Assert.True(vm.BreakLink(placement));

        var frame = FrameOf(vm);
        Assert.Null(frame.Placements);
        Assert.Single(frame.Strokes);
        // Placed at (100, 60) with the pivot at the origin, so that is where
        // the baked stroke starts.
        Assert.Equal(100, frame.Strokes[0].Points[0].X);
        Assert.Equal(60, frame.Strokes[0].Points[0].Y);
    }

    [AvaloniaFact]
    public void BrokenStrokesKeepTheirSwatch()
    {
        // The link to the palette is a different link from the link to the
        // symbol, and breaking one must not break the other.
        var vm = new MainViewModel(null);
        var project = ProjectIo.Create("Knight", _root);
        var swatch = new Swatch { Color = "#20c040" };
        project.Palettes.Add(new Palette { Swatches = [swatch] });
        var sword = Sword(swatch.Id);
        project.Symbols[sword.Id] = sword;
        vm.ProjectDocker.Project = project;
        vm.RefreshProjectResources();
        var placement = vm.PlaceSymbol(sword.Id, 100, 60)!;

        vm.BreakLink(placement);

        Assert.Equal(swatch.Id, FrameOf(vm).Strokes[0].SwatchId);
    }

    [AvaloniaFact]
    public void BreakingTheLinkIsOneUndoStep()
    {
        var vm = WithSymbol(out var sword);
        var placement = vm.PlaceSymbol(sword.Id, 100, 60)!;

        vm.BreakLink(placement);
        vm.UndoCommand.Execute(null);

        var frame = FrameOf(vm);
        Assert.Empty(frame.Strokes);
        Assert.Equal(placement.Id, Assert.Single(frame.Placements!).Id);
    }

    [AvaloniaFact]
    public void BreakingOneLinkLeavesOtherPlacementsLinked()
    {
        var vm = WithSymbol(out var sword);
        var first = vm.PlaceSymbol(sword.Id, 40, 40)!;
        vm.PlaceSymbol(sword.Id, 200, 40);

        vm.BreakLink(first);

        Assert.Single(FrameOf(vm).Placements!);
        Assert.Single(FrameOf(vm).Strokes);
    }

    [AvaloniaFact]
    public void AScaledPlacementBakesItsSizeIntoTheBrush()
    {
        // A sword at 2x that broke into strokes drawn with the original brush
        // width would come out as a big drawing in a thin line.
        var vm = WithSymbol(out var sword);
        var placement = vm.PlaceSymbol(sword.Id, 100, 60)!;
        placement.ScaleX = 2;
        placement.ScaleY = 2;
        var width = ((PaintedFrame)sword.Frames[0]).Strokes[0].Brush.Size;

        vm.BreakLink(placement);

        Assert.Equal(width * 2, FrameOf(vm).Strokes[0].Brush.Size);
        Assert.Equal(width, ((PaintedFrame)sword.Frames[0]).Strokes[0].Brush.Size);
    }

    private static int UndoDepth(MainViewModel vm)
    {
        var depth = 0;
        while (vm.UndoCommand.CanExecute(null) && depth < 50)
        {
            vm.UndoCommand.Execute(null);
            depth++;
        }
        return depth;
    }
}
