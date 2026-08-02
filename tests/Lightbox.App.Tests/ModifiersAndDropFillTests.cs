using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Modifiers that change what the brush in your hand does, without making
/// you swap tools and swap back.
/// </summary>
/// <remarks>
/// In the <c>BrushState</c> collection because it sets brush parameters, and
/// those live in a process-wide store — running beside a test that assumes
/// defaults hands it this one’s brush.
/// </remarks>
[Collection("BrushState")]
public class TemporaryToolModifierTests : BrushStateIsolated
{
    private static List<Stroke> Strokes(MainViewModel vm) =>
        ((PaintedFrame)vm.Doc.Scene.Layers.First(l => !l.IsBackground).Cels[0].Frame!).Strokes;

    [AvaloniaFact]
    public void AltHeld_ErasesWithTheCurrentBrush_WithoutSwitchingTools()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false, BrushSize = 37 };
        vm.BeginStroke(10, 10, 1, eraseWithCurrentBrush: true);
        vm.MoveStroke(40, 40, 1);
        vm.EndStroke();

        var stroke = Strokes(vm)[^1];
        Assert.Equal(ToolKind.Eraser, stroke.Tool);
        // The point of Alt over E: it keeps the brush you were using.
        Assert.Equal(37, stroke.Brush.Size);
        Assert.False(vm.IsEraser, "Alt must not leave the eraser selected afterwards");
    }

    [AvaloniaFact]
    public void WithoutAlt_TheSameCallPaints()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.BeginStroke(10, 10, 1);
        vm.MoveStroke(40, 40, 1);
        vm.EndStroke();
        Assert.Equal(ToolKind.Brush, Strokes(vm)[^1].Tool);
    }

    [AvaloniaFact]
    public void TheEraserToolStillErases_EvenWithoutAlt()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false, IsEraser = true };
        vm.ActiveTool = ToolId.Eraser;
        vm.BeginStroke(10, 10, 1);
        vm.MoveStroke(40, 40, 1);
        vm.EndStroke();
        Assert.Equal(ToolKind.Eraser, Strokes(vm)[^1].Tool);
    }
}

/// <summary>
/// Dragging the colour swatch onto the canvas fills — the shortest path from
/// choosing a colour to seeing a shape in it.
/// </summary>
/// <remarks>
/// In the <c>BrushState</c> collection because it sets brush parameters, and
/// those live in a process-wide store — running beside a test that assumes
/// defaults hands it this one’s brush.
/// </remarks>
[Collection("BrushState")]
public class DropColorFillTests : BrushStateIsolated
{
    private static MainViewModel Painted()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.BrushSize = 60;
        vm.ColorHex = "#202020";
        vm.BeginStroke(30, 30, 1);
        vm.MoveStroke(120, 30, 1);
        vm.MoveStroke(120, 120, 1);
        vm.MoveStroke(30, 120, 1);
        vm.MoveStroke(30, 30, 1);
        vm.EndStroke();
        return vm;
    }

    private static List<Stroke> Strokes(MainViewModel vm) =>
        ((PaintedFrame)vm.Doc.Scene.Layers.First(l => !l.IsBackground).Cels[0].Frame!).Strokes;

    [AvaloniaFact]
    public void DroppingAColour_FillsAndAdoptsIt()
    {
        var vm = Painted();
        var before = Strokes(vm).Count;

        vm.DropColorAt("#cc4400", 75, 75); // inside the drawn box

        Assert.Equal("#cc4400", vm.ColorHex);
        Assert.Equal(before + 1, Strokes(vm).Count);

        // Fills land beneath the line art by default, so this is not simply
        // the last stroke in the record.
        var fill = Assert.Single(Strokes(vm), s => s.Tool == ToolKind.Fill);
        Assert.Equal("#cc4400", fill.Color);
    }

    [AvaloniaFact]
    public void ItWorksWhicheverToolIsSelected()
    {
        // The gesture is unambiguous; making it depend on the fill tool being
        // active would defeat the point of it.
        var vm = Painted();
        vm.ActiveTool = ToolId.Brush;
        var before = Strokes(vm).Count;

        vm.DropColorAt("#00aa66", 75, 75);
        Assert.Equal(before + 1, Strokes(vm).Count);
    }

    [AvaloniaFact]
    public void ALockedLayerStillRefusesIt()
    {
        var vm = Painted();
        vm.Doc.Scene.Layers.First(l => !l.IsBackground).Locked = true;
        var before = Strokes(vm).Count;

        vm.DropColorAt("#ff0000", 75, 75);

        Assert.Equal(before, Strokes(vm).Count);
        Assert.Contains("locked", vm.AiStatus);
    }
}
