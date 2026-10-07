using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Ctrl+T with several layers picked in the layers docker transforms the drawing
/// at this frame on every one of them, together — as Krita does. Not
/// animation-aware: the same frame on each layer, nothing across time.
/// </summary>
[Collection("BrushState")]
public class TransformSelectedLayersTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static LayerRow Row(MainViewModel vm, int sceneIndex) =>
        vm.LayerRows.First(r => r.SceneIndex == sceneIndex);

    private static void Line(MainViewModel vm, double y)
    {
        vm.BeginStroke(100, y, 1);
        vm.MoveStroke(250, y + 10, 1);
        vm.MoveStroke(400, y, 1);
        vm.EndStroke();
    }

    /// <summary>Paper plus three drawing layers (scene 1, 2, 3), a line on each at frame 0.</summary>
    private static MainViewModel ThreeDrawnLayers()
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        Line(vm, 100);
        vm.AddPaintedLayerCommand.Execute(null);
        Line(vm, 200);
        vm.AddPaintedLayerCommand.Execute(null);
        Line(vm, 300);
        Assert.Equal(4, vm.Doc.Scene.Layers.Count);
        return vm;
    }

    private static double FirstX(Layer layer, int cel = 0) =>
        ((Frame)layer.Cels[cel].Frame!).Strokes[0].Points[0].X;

    [AvaloniaFact]
    public void TwoPickedLayersMoveTogether_AndTheThirdStays()
    {
        var vm = ThreeDrawnLayers();
        vm.SelectLayer(Row(vm, 3), toggle: false, range: false);
        vm.SelectLayer(Row(vm, 2), toggle: true, range: false);
        var layers = vm.Doc.Scene.Layers;
        var before = (FirstX(layers[1]), FirstX(layers[2]), FirstX(layers[3]));
        var steps = vm.RecordedStepCount;

        Assert.True(vm.BeginTransform());
        output.WriteLine($"subject: {vm.TransformSubject}");
        Assert.Equal("2 layers", vm.TransformSubject);
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 50, 0);

        Assert.Equal(before.Item1, FirstX(layers[1]), 3);          // not picked
        Assert.Equal(before.Item2 + 50, FirstX(layers[2]), 3);
        Assert.Equal(before.Item3 + 50, FirstX(layers[3]), 3);
        Assert.Equal(steps + 1, vm.RecordedStepCount);

        vm.UndoCommand.Execute(null);
        layers = vm.Doc.Scene.Layers;
        Assert.Equal(before.Item2, FirstX(layers[2]), 3);
        Assert.Equal(before.Item3, FirstX(layers[3]), 3);
    }

    [AvaloniaFact]
    public void AHiddenPickStaysAndTheStatusLineSaysSo()
    {
        var vm = ThreeDrawnLayers();
        // Picked last is active, and the active layer must be one you can see.
        vm.SelectLayer(Row(vm, 2), toggle: false, range: false);
        vm.SelectLayer(Row(vm, 3), toggle: true, range: false);
        vm.Doc.Scene.Layers[2].Visible = false;
        var layers = vm.Doc.Scene.Layers;
        var before = FirstX(layers[2]);

        Assert.True(vm.BeginTransform());
        output.WriteLine(vm.AiStatus);
        Assert.Contains("1 picked layer is hidden or locked", vm.AiStatus);
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 50, 0);

        Assert.Equal(before, FirstX(layers[2]), 3);
    }

    /// <summary>
    /// A picked layer standing on a hold gets a drawing of its own at this frame,
    /// as the active layer always did — otherwise the transform would move the
    /// earlier drawing it borrows, and every frame that shows it.
    /// </summary>
    [AvaloniaFact]
    public void AHeldCelOnAPickedLayerIsKeyedAndTheHeldDrawingStays()
    {
        var vm = ThreeDrawnLayers();
        vm.AddFrameCommand.Execute(null);              // frame 1, keyed on the top layer
        Line(vm, 320);                                  // top layer draws at frame 1
        var layers = vm.Doc.Scene.Layers;
        var middle = layers[2];
        while (middle.Cels.Count > 1) middle.Cels.RemoveAt(middle.Cels.Count - 1);
        middle.Cels.Add(new Cel());                     // frame 1 on the middle layer: a hold of frame 0
        vm.CurrentFrameIndex = 1;
        // Picked last is active: the top layer, which has a drawing of its own here.
        vm.SelectLayer(Row(vm, 2), toggle: false, range: false);
        vm.SelectLayer(Row(vm, 3), toggle: true, range: false);
        Assert.Equal(3, vm.ActiveLayerIndex);
        var heldX = FirstX(middle, 0);

        Assert.True(vm.BeginTransform());
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 50, 0);

        layers = vm.Doc.Scene.Layers;
        middle = layers[2];
        output.WriteLine($"middle cels: {string.Join(" ", middle.Cels.Select(c => c.Frame is null ? "h" : "K"))}");
        Assert.Equal(heldX, FirstX(middle, 0), 3);       // the borrowed drawing did not move
        Assert.Equal(heldX + 50, FirstX(middle, 1), 3);  // the copy at this frame did
    }
}
