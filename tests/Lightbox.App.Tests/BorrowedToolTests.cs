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

    // ---- held letter keys (B176) -------------------------------------------------
    // The same borrow, triggered by a key: press borrows now, and the release
    // splits tap from hold by duration — a tap is the switch the key always
    // performed, a hold is a scrub that comes back.

    /// <summary>A view model whose momentary clock the test moves by hand.</summary>
    private static (MainViewModel Vm, Action<long> Advance) Clocked()
    {
        var vm = VmLayers.PaperVm();
        long now = 0;
        vm.MomentaryClock = () => now;
        return (vm, ms => now += ms);
    }

    [AvaloniaFact]
    public void HoldingEErasesAndReleasesBackToThePreviousTool()
    {
        var (vm, advance) = Clocked();
        vm.ActiveTool = ToolId.Brush;

        vm.BeginMomentaryTool(ToolId.Eraser);
        Assert.Equal(ToolId.Eraser, vm.ActiveTool);
        Assert.True(vm.IsHoldingMomentaryTool);

        advance(MainViewModel.MomentaryTapMilliseconds + 200);
        vm.EndMomentaryTool(ToolId.Eraser);

        output.WriteLine($"released after a hold -> {vm.ActiveTool}");
        Assert.Equal(ToolId.Brush, vm.ActiveTool);
        Assert.False(vm.IsHoldingMomentaryTool);
    }

    [AvaloniaFact]
    public void HoldingIPicksAndReleasesBackToThePreviousTool()
    {
        var (vm, advance) = Clocked();
        vm.ActiveTool = ToolId.Brush;

        vm.BeginMomentaryTool(ToolId.Picker);
        Assert.Equal(ToolId.Picker, vm.ActiveTool);

        advance(MainViewModel.MomentaryTapMilliseconds + 200);
        vm.EndMomentaryTool(ToolId.Picker);

        Assert.Equal(ToolId.Brush, vm.ActiveTool);
    }

    /// <summary>
    /// A tap is not a hold: press and release inside the threshold latches the
    /// tool, which is what the key did before any of this existed.
    /// </summary>
    [AvaloniaFact]
    public void ATapLatchesTheToolRatherThanBouncingBack()
    {
        var (vm, advance) = Clocked();
        vm.ActiveTool = ToolId.Brush;

        vm.BeginMomentaryTool(ToolId.Eraser);
        advance(MainViewModel.MomentaryTapMilliseconds - 200);
        vm.EndMomentaryTool(ToolId.Eraser);

        Assert.Equal(ToolId.Eraser, vm.ActiveTool);
        Assert.False(vm.IsHoldingMomentaryTool);
    }

    /// <summary>Key repeat re-fires the press for as long as the key is down.</summary>
    [AvaloniaFact]
    public void KeyRepeatDoesNotDisturbTheHold()
    {
        var (vm, advance) = Clocked();
        vm.ActiveTool = ToolId.Brush;

        vm.BeginMomentaryTool(ToolId.Eraser);
        advance(MainViewModel.MomentaryTapMilliseconds + 100);
        // Repeats arrive after the threshold; they must not restart the clock
        // or re-borrow from the eraser itself.
        for (var i = 0; i < 10; i++) vm.BeginMomentaryTool(ToolId.Eraser);
        vm.EndMomentaryTool(ToolId.Eraser);

        Assert.Equal(ToolId.Brush, vm.ActiveTool);
    }

    /// <summary>
    /// <b>Which shortcuts are momentary is data in the registry</b>, so the
    /// Configure window can see and rebind them — and this holds the property
    /// for every registered one, so the next momentary tool added is guarded
    /// the day it is registered.
    /// </summary>
    [AvaloniaFact]
    public void ARegisteredMomentaryShortcutRestoresTheToolItInterrupted()
    {
        var map = new Lightbox.App.Services.ShortcutMap();
        var momentary = map.Definitions.Where(d => d.MomentaryTool is not null).ToList();

        output.WriteLine($"momentary: {string.Join(", ", momentary.Select(d => d.Id))}");
        Assert.Contains(momentary, d => d.Id == "tool.eraser");
        Assert.Contains(momentary, d => d.Id == "canvas.pickColor");

        foreach (var definition in momentary)
        {
            var (vm, advance) = Clocked();
            vm.ActiveTool = ToolId.Brush;

            vm.BeginMomentaryTool(definition.MomentaryTool!.Value);
            Assert.Equal(definition.MomentaryTool, vm.ActiveTool);

            advance(MainViewModel.MomentaryTapMilliseconds + 100);
            vm.EndMomentaryTool(definition.MomentaryTool!.Value);
            Assert.Equal(ToolId.Brush, vm.ActiveTool);
        }
    }

    /// <summary>
    /// The window deactivating mid-hold never delivers the key-up; restoring is
    /// what keeps a held key from becoming a stuck mode.
    /// </summary>
    [AvaloniaFact]
    public void LosingTheWindowMidHoldRestoresTheTool()
    {
        var (vm, _) = Clocked();
        vm.ActiveTool = ToolId.Brush;
        vm.BeginMomentaryTool(ToolId.Eraser);

        // What Deactivated does.
        vm.CancelMomentaryTool();

        Assert.Equal(ToolId.Brush, vm.ActiveTool);
        Assert.False(vm.IsHoldingMomentaryTool);
    }
}
