using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// The ramp editor's operations. The control draws and reports; every edit it
/// reports lands here, which is what keeps dragging a stop inside the undo
/// history rather than beside it.
/// </summary>
public class GradientRampTests
{
    private static (MainViewModel Vm, GradientDockerViewModel Gradients) WithGradient()
    {
        var vm = new MainViewModel(null) { ActiveTool = ToolId.Gradient };
        vm.GradientDocker.AddGradientCommand.Execute(null);
        return (vm, vm.GradientDocker);
    }

    // ---- colour stops -----------------------------------------------------------

    [AvaloniaFact]
    public void ClickingTheColourTrackAddsAStopWhereYouClicked()
    {
        var (_, gradients) = WithGradient();

        gradients.AddStopAt(alphaTrack: false, 0.25);

        var stop = gradients.SelectedGradient!.Stops.Single(s => Math.Abs(s.Position - 0.25) < 1e-9);
        Assert.Equal(3, gradients.SelectedGradient!.Stops.Count);
        // It takes the colour that was already there, so placing a stop is not
        // itself a visible edit. A quarter along a black-to-white ramp is
        // #898989, not #404040: the interpolation is in linear light, which is
        // why this app's gradients do not go muddy through the middle.
        Assert.Equal("#898989", stop.Color);
    }

    [AvaloniaFact]
    public void DraggingAColourStopMovesIt()
    {
        var (_, gradients) = WithGradient();
        var stops = gradients.SelectedGradient!.Stops;

        gradients.MoveStop(alphaTrack: false, 1, 0.6);

        Assert.Equal(0.6, stops[1].Position, 3);
    }

    [AvaloniaFact]
    public void TheLastTwoColourStopsCannotBeRemoved()
    {
        // One stop is not a ramp, and the tool would start painting a flat
        // colour with no way back except undo.
        var (_, gradients) = WithGradient();

        gradients.RemoveStopAt(alphaTrack: false, 0);

        Assert.Equal(2, gradients.SelectedGradient!.Stops.Count);
    }

    [AvaloniaFact]
    public void AddingAColourStopIsUndoable()
    {
        var (vm, gradients) = WithGradient();

        gradients.AddStopAt(alphaTrack: false, 0.5);
        Assert.Equal(3, gradients.SelectedGradient!.Stops.Count);

        vm.UndoCommand.Execute(null);
        Assert.Equal(2, vm.Doc.Gradients.Values.Single().Stops.Count);
    }

    // ---- the opacity track ------------------------------------------------------

    [AvaloniaFact]
    public void AGradientHasNoAlphaTrackUntilYouAddOne()
    {
        var (_, gradients) = WithGradient();

        Assert.False(gradients.SelectedGradient!.HasAlphaTrack);
    }

    [AvaloniaFact]
    public void TheFirstOpacityStopSeedsBothEndsSoItDoesNotFadeEverything()
    {
        // A single stop holds its value everywhere, which would turn "make
        // this bit fade" into "fade the whole thing".
        var (_, gradients) = WithGradient();

        gradients.AddStopAt(alphaTrack: true, 0.5);

        var track = gradients.SelectedGradient!.AlphaStops!;
        Assert.Equal(3, track.Count);
        Assert.Contains(track, s => s.Position == 0);
        Assert.Contains(track, s => s.Position == 1);
        // And nothing changed visually: it took the opacity already there.
        Assert.All(track, s => Assert.Equal(1, s.Alpha, 3));
    }

    [AvaloniaFact]
    public void EditingTheSelectedOpacityShowsInTheRamp()
    {
        var (_, gradients) = WithGradient();
        gradients.AddStopAt(alphaTrack: true, 0.5);

        gradients.SelectedAlpha = 0;

        Assert.Equal(0, GradientOps.Sample(gradients.SelectedGradient!, 0.5).A);
        // The ends are untouched, which is the point of a stop.
        Assert.Equal(255, GradientOps.Sample(gradients.SelectedGradient!, 0).A);
    }

    [AvaloniaFact]
    public void RemovingDownToOneOpacityStopDropsTheTrackEntirely()
    {
        // Back to the colour stops' own alpha, which is where a gradient
        // starts — rather than leaving one stop quietly flattening everything.
        var (_, gradients) = WithGradient();
        gradients.AddStopAt(alphaTrack: true, 0.5);
        Assert.Equal(3, gradients.SelectedGradient!.AlphaStops!.Count);

        gradients.RemoveStopAt(alphaTrack: true, 2);
        gradients.RemoveStopAt(alphaTrack: true, 1);

        Assert.False(gradients.SelectedGradient!.HasAlphaTrack);
    }

    [AvaloniaFact]
    public void OpacityAndColourStopsAreSelectedIndependently()
    {
        // Selecting one has to deselect the other, or the editor would show
        // two panels claiming to be "the selected stop".
        var (_, gradients) = WithGradient();
        gradients.AddStopAt(alphaTrack: true, 0.5);
        Assert.True(gradients.HasAlphaStop);
        Assert.False(gradients.HasStop);

        gradients.Select(alphaTrack: false, 0);
        Assert.False(gradients.HasAlphaStop);
        Assert.True(gradients.HasStop);
    }

    [AvaloniaFact]
    public void AnOpacityHoleShowsOnTheCanvas()
    {
        // End to end: the track reaches the pixels, through the same stroke
        // record as everything else.
        var vm = new MainViewModel(null) { ActiveTool = ToolId.Gradient };
        vm.GradientDocker.AddGradientCommand.Execute(null);
        vm.GradientDocker.AddStopAt(alphaTrack: true, 0.5);
        vm.GradientDocker.SelectedAlpha = 0;

        var w = vm.Doc.Scene.Width;
        vm.BeginGradient(0, 10);
        vm.MoveGradient(w / 2.0, 10);
        vm.EndGradient(w, 10);

        var strokes = ((Frame)vm.PaintLayer().Cels[0].Frame!).Strokes;
        using var bmp = Lightbox.Raster.FrameRasterizer.Rasterize(
            strokes, vm.Doc.Scene.Width, vm.Doc.Scene.Height);

        Assert.Equal(0, bmp.GetPixel(w / 2, 10).Alpha);
        Assert.True(bmp.GetPixel(4, 10).Alpha > 200);
    }
}
