using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// Holding a modifier borrows a tool; letting go gives the old one back.
/// </summary>
/// <remarks>
/// <para>
/// The properties that carry the weight: <b>the tool comes back</b> (a
/// momentary tool that strands you is worse than none), <b>a borrow is not a
/// decision</b> (it must not finish a pen path or drop a line selection on the
/// way past), and <b>it survives a lost key-up</b>, because alt-tabbing away
/// mid-hold is ordinary and a stuck modifier is the failure people report.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class BorrowedToolTests(ITestOutputHelper output) : BrushStateIsolated
{
    [AvaloniaFact]
    public void HoldingControlBorrowsTheEyedropperAndReleasingGivesTheToolBack()
    {
        var vm = VmLayers.PaperVm();
        vm.ActiveTool = ToolId.Brush;

        vm.ApplyHeldModifiers(KeyModifiers.Control);
        output.WriteLine($"holding Ctrl -> {vm.ActiveTool}");
        Assert.Equal(ToolId.Picker, vm.ActiveTool);
        Assert.True(vm.IsBorrowingTool);

        vm.ApplyHeldModifiers(KeyModifiers.None);
        Assert.Equal(ToolId.Brush, vm.ActiveTool);
        Assert.False(vm.IsBorrowingTool);
    }

    /// <summary>Key repeat fires continuously, so the same held state must be a no-op.</summary>
    [AvaloniaFact]
    public void HoldingIsIdempotent()
    {
        var vm = VmLayers.PaperVm();
        vm.ActiveTool = ToolId.Eraser;

        for (var i = 0; i < 10; i++) vm.ApplyHeldModifiers(KeyModifiers.Control);
        Assert.Equal(ToolId.Picker, vm.ActiveTool);

        vm.ApplyHeldModifiers(KeyModifiers.None);
        Assert.Equal(ToolId.Eraser, vm.ActiveTool);
    }

    /// <summary>
    /// Ctrl+Shift is a different gesture from Ctrl. Treating it as Ctrl would
    /// fire the eyedropper in the middle of a constrained drag.
    /// </summary>
    [AvaloniaFact]
    public void AModifierCombinationIsNotTheModifierOnItsOwn()
    {
        var vm = VmLayers.PaperVm();
        vm.ActiveTool = ToolId.Brush;

        vm.ApplyHeldModifiers(KeyModifiers.Control | KeyModifiers.Shift);

        Assert.Equal(ToolId.Brush, vm.ActiveTool);
        Assert.False(vm.IsBorrowingTool);
    }

    /// <summary>
    /// <b>A borrow is not a decision.</b> Leaving a tool deliberately finishes a
    /// pen path, ends isolation and drops the line selection. Holding Ctrl is
    /// not leaving.
    /// </summary>
    [AvaloniaFact]
    public void BorrowingDoesNotThrowAwayWhatLeavingAToolWould()
    {
        var vm = VmLayers.PaperVm();
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.Count - 1;
        vm.ActiveTool = ToolId.Brush;

        // A line to select, and the arrow's selection is what a tool change drops.
        vm.SmoothStrokes = false;
        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(80, 80, 1);
        vm.EndStroke();
        vm.ActiveTool = ToolId.Arrow;
        Assert.True(vm.PickStrokeAt(50, 50, tolerance: 6));
        Assert.True(vm.HasStrokeSelection);

        // The arrow is not borrowable — modal state is exactly why — so the
        // selection survives simply because nothing happens.
        vm.ApplyHeldModifiers(KeyModifiers.Control);
        Assert.Equal(ToolId.Arrow, vm.ActiveTool);
        Assert.True(vm.HasStrokeSelection);
    }

    /// <summary>
    /// A tool with modal state in flight cannot be borrowed FROM: holding Ctrl
    /// mid-path would otherwise commit the path still being drawn.
    /// </summary>
    [AvaloniaFact]
    public void ToolsWithWorkInFlightAreNotBorrowedFrom()
    {
        var vm = VmLayers.PaperVm();
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.Count - 1;
        vm.ActiveTool = ToolId.Pen;
        vm.PenPress(100, 100, tolerance: 6);
        vm.PenRelease();
        vm.PenPress(200, 140, tolerance: 6);
        vm.PenRelease();
        Assert.True(vm.PenActive);

        vm.ApplyHeldModifiers(KeyModifiers.Control);

        output.WriteLine($"pen + Ctrl -> {vm.ActiveTool}, path still open: {vm.PenActive}");
        Assert.Equal(ToolId.Pen, vm.ActiveTool);
        Assert.True(vm.PenActive);
    }

    /// <summary>
    /// Move keeps Ctrl for "the whole layer" while dragging, and a modifier
    /// cannot mean two things on one tool.
    /// </summary>
    [AvaloniaFact]
    public void MoveKeepsControlForItself()
    {
        var vm = VmLayers.PaperVm();
        vm.ActiveTool = ToolId.Move;

        vm.ApplyHeldModifiers(KeyModifiers.Control);

        Assert.Equal(ToolId.Move, vm.ActiveTool);
    }

    /// <summary>
    /// Alt-tab away mid-hold and the key-up never arrives. A stuck modifier is
    /// the failure that makes a momentary tool worse than none, so the window
    /// deactivating clears it the same way a release does.
    /// </summary>
    [AvaloniaFact]
    public void LosingTheKeyUpDoesNotStrandTheBorrowedTool()
    {
        var vm = VmLayers.PaperVm();
        vm.ActiveTool = ToolId.Fill;
        vm.ApplyHeldModifiers(KeyModifiers.Control);
        Assert.Equal(ToolId.Picker, vm.ActiveTool);

        // What Deactivated does.
        vm.ApplyHeldModifiers(KeyModifiers.None);

        Assert.Equal(ToolId.Fill, vm.ActiveTool);
    }

    [AvaloniaFact]
    public void EveryBorrowableToolComesBack()
    {
        foreach (var tool in new[] { ToolId.Brush, ToolId.Eraser, ToolId.Fill, ToolId.Shape, ToolId.Gradient })
        {
            var vm = VmLayers.PaperVm();
            vm.ActiveTool = tool;
            vm.ApplyHeldModifiers(KeyModifiers.Control);
            Assert.Equal(ToolId.Picker, vm.ActiveTool);
            vm.ApplyHeldModifiers(KeyModifiers.None);
            Assert.Equal(tool, vm.ActiveTool);
        }
    }
}
