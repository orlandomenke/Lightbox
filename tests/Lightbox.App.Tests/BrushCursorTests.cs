using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;

namespace Lightbox.App.Tests;

/// <summary>
/// The ring has one job: show the thickness the stroke will actually be. A
/// ring that disagrees with the mark is worse than no ring, because the
/// artist aims with it.
/// </summary>
/// <remarks>
/// In the <c>BrushState</c> collection because it sets brush parameters, and
/// those live in a process-wide store: running beside a test that assumes
/// defaults hands it this one’s brush. It opted out until a CI run caught
/// it — the live-preview pixel check went red on a loaded machine and was
/// green on every local run, which is what this collection exists to stop.
/// </remarks>
[Collection("BrushState")]
public class BrushCursorTests : BrushStateIsolated
{
    [AvaloniaFact]
    public void WithAMouse_TheRingIsTheFullBrushWidth()
    {
        var vm = new MainViewModel(null) { BrushSize = 80 };
        // A mouse reports full pressure; nothing should shrink the ring.
        vm.SetCursorPressure(1, penDown: true);
        Assert.Equal(80, vm.BrushCursorDiameter, 3);
    }

    [AvaloniaFact]
    public void HoveringShowsTheMaximum_EvenAfterALightStroke()
    {
        var vm = new MainViewModel(null) { BrushSize = 60 };
        vm.BrushPressureEnabled = true;
        vm.BrushPressureSizeGamma = 1;

        vm.SetCursorPressure(0.25, penDown: true);
        Assert.True(vm.BrushCursorDiameter < 60, "the ring should shrink under light pressure");

        vm.SetCursorPressure(0.25, penDown: false); // pen lifted, hovering
        Assert.Equal(60, vm.BrushCursorDiameter, 3);
    }

    [AvaloniaFact]
    public void TheRingMatchesTheRadiusTheEngineWillStamp()
    {
        var vm = new MainViewModel(null) { BrushSize = 100 };
        vm.BrushPressureEnabled = true;
        vm.BrushPressureSizeGamma = 1.6; // a non-linear curve is where drift would show

        foreach (var pressure in new[] { 0.15, 0.4, 0.75, 1.0 })
        {
            vm.SetCursorPressure(pressure, penDown: true);
            var settings = new BrushSettings
            {
                Size = vm.BrushSize,
                PressureEnabled = vm.BrushPressureEnabled,
                PressureSizeGamma = vm.BrushPressureSizeGamma,
            };
            var expected = BrushEngine.RadiusAt(settings, pressure) * 2;
            Assert.Equal(expected, vm.BrushCursorDiameter, 3);
        }
    }

    [AvaloniaFact]
    public void TurningTrackingOff_PinsTheRingToFullSize()
    {
        var vm = new MainViewModel(null) { BrushSize = 44 };
        vm.BrushPressureEnabled = true;
        vm.BrushPressureSizeGamma = 1;

        vm.SetCursorPressure(0.2, penDown: true);
        Assert.True(vm.BrushCursorDiameter < 44);

        vm.CursorTracksPressure = false;
        Assert.Equal(44, vm.BrushCursorDiameter, 3);
    }

    [AvaloniaFact]
    public void WhenPressureIsDisabledForTheBrush_TheRingIgnoresIt()
    {
        // The brush's own master pressure switch must win: if pressure does
        // not affect the stroke, it must not affect the ring either.
        var vm = new MainViewModel(null) { BrushSize = 50 };
        vm.BrushPressureEnabled = false;

        vm.SetCursorPressure(0.1, penDown: true);
        Assert.Equal(50, vm.BrushCursorDiameter, 3);
    }
}
