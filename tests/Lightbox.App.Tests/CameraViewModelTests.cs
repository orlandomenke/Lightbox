using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// The camera as the artist meets it. The thread running through all of it is
/// optionality: until someone asks for a camera there is nothing to see, and
/// removing one puts the document back exactly as it was.
/// </summary>
public class CameraViewModelTests
{
    private static MainViewModel Vm()
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings("Shot", 400, 300, 12, 72, "#ffffff", false));
        return vm;
    }

    [AvaloniaFact]
    public void ANewDocumentHasNoCameraAndNoOverlay()
    {
        var vm = Vm();
        Assert.False(vm.HasCamera);
        Assert.Null(vm.Doc.Scene.Camera);
        Assert.Null(vm.CameraFrameCorners);
    }

    [AvaloniaFact]
    public void AddingACameraFramesTheWholeCanvasAtOneToOne()
    {
        // A camera should start by changing nothing: the first thing the
        // artist sees is what they already had.
        var vm = Vm();
        vm.AddCameraCommand.Execute(null);

        Assert.True(vm.HasCamera);
        Assert.Equal(400, vm.CameraOutputWidth);
        Assert.Equal(300, vm.CameraOutputHeight);
        Assert.Equal(1.0, vm.CameraZoom);
        Assert.Equal(200, vm.CameraX);
        Assert.Equal(150, vm.CameraY);

        var corners = Assert.IsType<SkiaSharp.SKPoint[]>(vm.CameraFrameCorners);
        Assert.Equal(4, corners.Length);
        Assert.Equal(0, corners[0].X, 3);
        Assert.Equal(0, corners[0].Y, 3);
        Assert.Equal(400, corners[2].X, 3);
        Assert.Equal(300, corners[2].Y, 3);
    }

    [AvaloniaFact]
    public void AddingACameraTwiceIsHarmless()
    {
        var vm = Vm();
        vm.AddCameraCommand.Execute(null);
        vm.CameraZoom = 3;
        vm.AddCameraCommand.Execute(null);

        Assert.Equal(3, vm.CameraZoom);
    }

    [AvaloniaFact]
    public void RemovingTheCameraTakesTheOverlayWithIt()
    {
        var vm = Vm();
        vm.AddCameraCommand.Execute(null);
        vm.SetCameraKeyCommand.Execute(null);
        Assert.NotNull(vm.CameraFrameCorners);

        vm.RemoveCameraCommand.Execute(null);
        Assert.False(vm.HasCamera);
        Assert.Null(vm.Doc.Scene.Camera);
        Assert.Null(vm.CameraFrameCorners);
    }

    [AvaloniaFact]
    public void EditingAFramingKeysItAtThePlayhead()
    {
        // A framing you cannot see keyed is a framing you lose by scrubbing
        // away from it, so editing a field writes the key.
        var vm = Vm();
        vm.AddCameraCommand.Execute(null);
        vm.CurrentFrameIndex = 0;
        vm.CameraZoom = 2.5;

        Assert.True(vm.IsOnCameraKey);
        Assert.Equal(2.5, CameraOps.KeyAt(vm.Doc.Scene.Camera, 0)!.Zoom);
    }

    [AvaloniaFact]
    public void ScrubbingBetweenKeysShowsTheInterpolatedFraming()
    {
        var vm = Vm();
        vm.AddFrameCommand.Execute(null);
        vm.AddFrameCommand.Execute(null);
        vm.AddCameraCommand.Execute(null);

        vm.CurrentFrameIndex = 0;
        vm.CameraX = 100;
        vm.CurrentFrameIndex = 2;
        vm.CameraX = 300;

        vm.CurrentFrameIndex = 1;
        Assert.Equal(200, vm.CameraX, 3);
        // And the overlay follows the playhead, not just the fields.
        Assert.Equal(0, vm.CameraFrameCorners![0].X, 3); // 200 - 400/2
    }

    [AvaloniaFact]
    public void ClearingAKeyLeavesTheOthers()
    {
        var vm = Vm();
        vm.AddFrameCommand.Execute(null);
        vm.AddCameraCommand.Execute(null);

        vm.CurrentFrameIndex = 0;
        vm.CameraX = 100;
        vm.CurrentFrameIndex = 1;
        vm.CameraX = 300;

        vm.ClearCameraKeyCommand.Execute(null);
        Assert.False(vm.IsOnCameraKey);
        Assert.Equal([0], vm.CameraKeyFrames);
        // With only the frame-0 key left, frame 1 holds it.
        Assert.Equal(100, vm.CameraX, 3);
    }

    [AvaloniaFact]
    public void TheRulerLearnsWhichFramesCarryAKey()
    {
        var vm = Vm();
        vm.AddFrameCommand.Execute(null);
        vm.AddFrameCommand.Execute(null);
        vm.AddCameraCommand.Execute(null);

        vm.CurrentFrameIndex = 2;
        vm.SetCameraKeyCommand.Execute(null);
        vm.CurrentFrameIndex = 0;
        vm.SetCameraKeyCommand.Execute(null);

        Assert.Equal([0, 2], vm.CameraKeyFrames);
    }

    [AvaloniaFact]
    public void ViewingThroughTheCameraDropsTheOverlay()
    {
        // Looking through the camera makes the frame the viewport, so an
        // overlay would just outline the window.
        var vm = Vm();
        vm.AddCameraCommand.Execute(null);
        Assert.NotNull(vm.CameraFrameCorners);

        vm.ViewThroughCamera = true;
        Assert.Null(vm.CameraFrameCorners);

        vm.ViewThroughCamera = false;
        Assert.NotNull(vm.CameraFrameCorners);
    }

    [AvaloniaFact]
    public void ViewingThroughTheCameraDoesNotTouchTheDocument()
    {
        // Invariant 5's cousin: the camera is authored, but looking through it
        // is a view operation and must not change a stroke.
        var vm = Vm();
        vm.BeginStroke(50, 50, 1);
        vm.MoveStroke(200, 150, 1);
        vm.EndStroke();

        var before = Lightbox.Core.Serialization.DocJson.Serialize(vm.Doc);
        vm.AddCameraCommand.Execute(null);
        vm.CameraZoom = 2;
        vm.ViewThroughCamera = true;
        vm.ViewThroughCamera = false;
        vm.RemoveCameraCommand.Execute(null);

        Assert.Equal(before, Lightbox.Core.Serialization.DocJson.Serialize(vm.Doc));
    }

    [AvaloniaFact]
    public void PaintingStillWorksWhileLookingThroughTheCamera()
    {
        var vm = Vm();
        vm.AddCameraCommand.Execute(null);
        vm.ViewThroughCamera = true;

        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(180, 140, 1);
        vm.EndStroke();

        var painted = vm.Doc.Scene.Layers
            .Where(l => !l.IsBackground)
            .SelectMany(l => l.Cels)
            .Select(c => c.Frame)
            .OfType<Frame>()
            .Sum(f => f.Strokes.Count);
        Assert.True(painted > 0, "the stroke did not reach the record");
    }

    [AvaloniaFact]
    public void ACameraWithNoKeysStillFramesSomething()
    {
        // Adding a camera and immediately scrubbing must not produce a null
        // framing or a collapsed overlay.
        var vm = Vm();
        vm.AddFrameCommand.Execute(null);
        vm.AddCameraCommand.Execute(null);
        vm.CurrentFrameIndex = 1;

        Assert.Empty(vm.CameraKeyFrames);
        Assert.Equal(200, vm.CameraX);
        Assert.NotNull(vm.CameraFrameCorners);
    }
}
