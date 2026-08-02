using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// A locked layer refuses everything that changes pixels or geometry, and
/// nothing else. The hidden-layer precedent only guarded paint, fill and AI
/// draw by hand — transform, cel edits, deletion and the external writers
/// were all reachable, so each of those gets a test here.
/// </summary>
public class LayerLockTests
{
    private static (MainViewModel Vm, Layer Layer) Locked()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        var layer = vm.PaintLayer();
        // Paint something first, so "nothing changed" is a real observation
        // rather than the trivial case of an empty layer.
        vm.BeginStroke(10, 10, 1);
        vm.MoveStroke(50, 50, 1);
        vm.EndStroke();
        layer.Locked = true;
        return (vm, layer);
    }

    private static int StrokeCount(Layer layer) =>
        ((PaintedFrame)layer.Cels[0].Frame!).Strokes.Count;

    [AvaloniaFact]
    public void PaintingIsRefused_WithAReasonThatNamesTheLayer()
    {
        var (vm, layer) = Locked();
        var before = StrokeCount(layer);

        vm.BeginStroke(60, 60, 1);
        vm.MoveStroke(90, 90, 1);
        vm.EndStroke();

        Assert.Equal(before, StrokeCount(layer));
        Assert.Contains("locked", vm.AiStatus);
        Assert.Contains(layer.Name, vm.AiStatus);
    }

    [AvaloniaFact]
    public void FillIsRefused()
    {
        var (vm, layer) = Locked();
        var before = StrokeCount(layer);
        vm.ActiveTool = ToolId.Fill;
        vm.FillAt(30, 30);
        Assert.Equal(before, StrokeCount(layer));
        Assert.Contains("locked", vm.AiStatus);
    }

    [AvaloniaFact]
    public void TransformIsRefused()
    {
        var (vm, _) = Locked();
        Assert.False(vm.BeginTransform());
        Assert.Contains("locked", vm.AiStatus);
    }

    [AvaloniaFact]
    public void DeletingTheLayerIsRefused()
    {
        var (vm, layer) = Locked();
        var before = vm.Doc.Scene.Layers.Count;
        vm.DeleteLayer(layer);
        Assert.Equal(before, vm.Doc.Scene.Layers.Count);
        Assert.Contains("locked", vm.AiStatus);
    }

    [AvaloniaFact]
    public void ExternalWritersAreRefused()
    {
        // An MCP or IPC client must not be able to paint on a layer the
        // artist locked; these paths had no guard of any kind before.
        var (vm, layer) = Locked();
        var stroke = new Stroke
        {
            Tool = ToolKind.Brush, Color = "#00ff00",
            Points = [new StrokePoint(5, 5, 1), new StrokePoint(20, 20, 1)],
        };
        Assert.Equal(0, vm.AppendExternalStrokes(layer.Id, 0, [stroke]));
        Assert.Equal(0, vm.InsertExternalInbetweens(layer.Id, 0, [[stroke]]));
    }

    [AvaloniaFact]
    public void VisibilityOpacityAndBlendModeStayAvailable()
    {
        // Locking is about editing, not about hiding. A locked layer must
        // still be usable as reference.
        var (vm, layer) = Locked();
        vm.SetLayerVisible(layer, false);
        Assert.False(layer.Visible);
        vm.SetLayerVisible(layer, true);

        layer.Opacity = 0.5;
        layer.BlendMode = LayerBlendMode.Multiply;
        Assert.Equal(0.5, layer.Opacity);
        Assert.Equal(LayerBlendMode.Multiply, layer.BlendMode);
    }

    [AvaloniaFact]
    public void ALockedFolderLocksItsMembers_AndSaysSo()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        var layer = vm.PaintLayer();
        var group = new LayerGroup { Name = "Backgrounds" };
        vm.Doc.Scene.LayerGroups.Add(group);
        layer.GroupId = group.Id;
        group.Locked = true;

        var before = StrokeCount(layer);
        vm.BeginStroke(10, 10, 1);
        vm.MoveStroke(40, 40, 1);
        vm.EndStroke();

        Assert.Equal(before, StrokeCount(layer));
        // The folder is the reason, so the folder is what the message names.
        Assert.Contains("Backgrounds", vm.AiStatus);
        Assert.Contains("locked", vm.AiStatus);
    }

    [AvaloniaFact]
    public void LockingIsUndoable()
    {
        var vm = new MainViewModel(null);
        var layer = vm.PaintLayer();
        vm.SetLayerLocked(layer, true);
        Assert.True(layer.Locked);

        vm.UndoCommand.Execute(null);
        Assert.False(vm.PaintLayer().Locked);
    }

    [AvaloniaFact]
    public void AlphaLockIsRecordedOnTheStroke_NotReadBackFromTheLayer()
    {
        // Invariant 4: flipping the layer's alpha lock afterwards must not
        // repaint work already committed.
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        var layer = vm.PaintLayer();
        layer.AlphaLocked = true;

        vm.BeginStroke(10, 10, 1);
        vm.MoveStroke(40, 40, 1);
        vm.EndStroke();

        var stroke = ((PaintedFrame)layer.Cels[0].Frame!).Strokes[^1];
        Assert.True(stroke.AlphaLocked);

        layer.AlphaLocked = false;
        Assert.True(stroke.AlphaLocked, "the committed stroke must keep its own restriction");
    }
}
