using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;

namespace Lightbox.App.Tests;

/// <summary>
/// The Crop tool as wired: the frame belongs to the tool, applying it goes
/// through the same door the menu commands use, and the tool is reachable.
/// </summary>
/// <remarks>
/// <c>CropSessionTests</c> covers the drag arithmetic on its own. This is the
/// part that can only go wrong once the pieces are joined — a frame that
/// outlives its tool, an Apply that quietly does nothing, a tool nobody can
/// find.
/// </remarks>
[Collection("BrushState")]
public sealed class CropToolTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel Vm()
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        return vm;
    }

    /// <summary>Drag the frame in to a rectangle, the way the canvas would.</summary>
    private static void DragFrame(MainViewModel vm, double x1, double y1, double x2, double y2)
    {
        var frame = vm.CropFrame!;
        frame.Press(x1, y1, 6);
        frame.Move(x2, y2);
        frame.Release();
        vm.NotifyCropFrame();
    }

    /// <summary>
    /// The frame is the tool: picking Crop opens one, and picking anything else
    /// takes it away.
    /// </summary>
    /// <remarks>
    /// A frame that outlived its tool would be a rectangle drawn over the
    /// artwork that nothing can act on — the same one-line rule the pen's
    /// parking, the node overlay and the rig all follow.
    /// </remarks>
    [AvaloniaFact]
    public void TheFrameArrivesWithTheToolAndLeavesWithIt()
    {
        var vm = Vm();
        Assert.Null(vm.CropFrame);

        vm.SelectToolCommand.Execute(ToolId.Crop);
        Assert.NotNull(vm.CropFrame);
        output.WriteLine($"opened on {vm.CropFrame!.Width} × {vm.CropFrame.Height}");
        // On the whole page, so the handles are there to pull in immediately.
        Assert.Equal(960, vm.CropFrame.Width);
        Assert.Equal(540, vm.CropFrame.Height);
        Assert.False(vm.CanApplyCropFrame);

        vm.SelectToolCommand.Execute(ToolId.Brush);
        Assert.Null(vm.CropFrame);
    }

    [AvaloniaFact]
    public void ApplyingTheFrameCropsThePaperAndReopensOnIt()
    {
        var vm = Vm();
        vm.SelectToolCommand.Execute(ToolId.Crop);
        DragFrame(vm, 100, 60, 500, 360);
        Assert.True(vm.CanApplyCropFrame);
        Assert.Equal("400 × 300", vm.CropFrameLabel);

        Assert.True(vm.ApplyCropFrame());

        var scene = vm.Doc.Scene;
        output.WriteLine($"{scene.Width} × {scene.Height} at ({scene.Left}, {scene.Top})");
        Assert.Equal(400, scene.Width);
        Assert.Equal(300, scene.Height);
        Assert.Equal(100, scene.Left);
        Assert.Equal(60, scene.Top);

        // Re-opened on the new page, because the tool is still in hand: an
        // artist who wants a little more off should find handles there.
        Assert.NotNull(vm.CropFrame);
        Assert.Equal(100, vm.CropFrame!.Left);
        Assert.Equal(400, vm.CropFrame.Width);
        Assert.False(vm.CanApplyCropFrame);
    }

    /// <summary>
    /// A second crop works, which is the case that catches surface coordinates
    /// standing in for document ones.
    /// </summary>
    /// <remarks>
    /// The first crop is where every origin is still zero and both readings
    /// agree. The second is where they part company, and it is the one an
    /// artist reaches within seconds of finding the tool.
    /// </remarks>
    [AvaloniaFact]
    public void ASecondCropIsRelativeToTheFirst()
    {
        var vm = Vm();
        vm.SelectToolCommand.Execute(ToolId.Crop);
        DragFrame(vm, 100, 60, 500, 360);
        Assert.True(vm.ApplyCropFrame());

        // The frame reopened on a page that starts at (100, 60), so the handles
        // are there and not at the origin.
        Assert.Equal(CropHandle.TopLeft, vm.CropFrame!.HandleAt(102, 62, 6));

        DragFrame(vm, 200, 100, 400, 300);
        Assert.True(vm.ApplyCropFrame());

        var scene = vm.Doc.Scene;
        output.WriteLine($"{scene.Width} × {scene.Height} at ({scene.Left}, {scene.Top})");
        Assert.Equal(200, scene.Width);
        Assert.Equal(200, scene.Height);
        Assert.Equal(200, scene.Left);
        Assert.Equal(100, scene.Top);
    }

    [AvaloniaFact]
    public void ApplyingAnUndraggedFrameRefusesAndSaysWhy()
    {
        var vm = Vm();
        vm.SelectToolCommand.Execute(ToolId.Crop);

        Assert.False(vm.CanApplyCropFrame);
        Assert.Equal("Drag a frame", vm.CropFrameLabel);
        Assert.False(vm.ApplyCropFrame());

        output.WriteLine(vm.AiStatus);
        Assert.Equal(960, vm.Doc.Scene.Width);
        Assert.Contains("still the whole page", vm.AiStatus);
    }

    [AvaloniaFact]
    public void CancellingPutsTheFrameBackRoundThePageWithoutPuttingTheToolAway()
    {
        var vm = Vm();
        vm.SelectToolCommand.Execute(ToolId.Crop);
        DragFrame(vm, 100, 60, 500, 360);
        Assert.True(vm.CanApplyCropFrame);

        vm.CancelCropFrame();

        output.WriteLine($"{vm.CropFrameLabel}, tool={vm.ActiveTool}");
        Assert.False(vm.CanApplyCropFrame);
        // Still the crop tool: leaving the tool is picking another one, and one
        // key doing both would make "undo my drag" and "I am done" the same
        // gesture.
        Assert.Equal(ToolId.Crop, vm.ActiveTool);
        Assert.NotNull(vm.CropFrame);
    }

    /// <summary>Cropping through the tool is one undo step, like every other crop.</summary>
    [AvaloniaFact]
    public void TheCropIsOneUndoStepAndUndoPutsThePaperBack()
    {
        var vm = Vm();
        vm.SelectToolCommand.Execute(ToolId.Crop);
        DragFrame(vm, 100, 60, 500, 360);
        Assert.True(vm.ApplyCropFrame());

        vm.UndoCommand.Execute(null);

        output.WriteLine($"after undo: {vm.Doc.Scene.Width} × {vm.Doc.Scene.Height}");
        Assert.Equal(960, vm.Doc.Scene.Width);
        Assert.Equal(0, vm.Doc.Scene.Left);
    }

    // ---- reachable ------------------------------------------------------------------

    /// <summary>
    /// The tool is registered everywhere something enumerates tools.
    /// </summary>
    /// <remarks>
    /// The standing rule: anything with a registry has that registry because
    /// something else enumerates it. A tool wired only to a rail button works
    /// perfectly and is invisible to the shortcut editor, so an artist cannot
    /// rebind it and cannot find it by searching.
    /// </remarks>
    [AvaloniaFact]
    public void TheToolIsInTheRailTheShortcutMapAndTheLabels()
    {
        var map = new ShortcutMap { StorePathOverride = Path.GetTempFileName() };
        var crop = map.Definitions.SingleOrDefault(d => d.Id == "tool.crop");
        Assert.NotNull(crop);
        output.WriteLine($"{crop!.Id}: {crop.Name} on {crop.GestureText}");
        Assert.Equal("C", crop.GestureText);

        var vm = Vm();
        vm.SelectToolCommand.Execute(ToolId.Crop);
        Assert.True(vm.IsCropTool);
        // In the label table rather than left to the enum's ToString fallback,
        // per B221 — the fallback exists so a tool added tomorrow shows
        // something, not so a shipped tool can skip the table.
        Assert.Equal("Crop", vm.ActiveToolLabel);
        Assert.NotNull(vm.ActiveToolIcon);

        var window = new MainWindow();
        var button = window.GetLogicalDescendants().OfType<ToggleButton>()
            .FirstOrDefault(b => b.CommandParameter is ToolId.Crop);
        Assert.True(button is not null, "the Crop tool has no button in the rail");
    }
}
