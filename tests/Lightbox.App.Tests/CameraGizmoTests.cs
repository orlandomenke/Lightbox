using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The camera gizmo on the canvas: the handle geometry the pointer hits, and
/// the view-model drags that key the framing at the playhead.
/// </summary>
public class CameraGizmoGeometryTests
{
    // AvaloniaFact even though the helpers are pure statics: CanvasControl's
    // type initializer makes a Cursor, which needs the headless platform up.
    [AvaloniaFact]
    public void TheRotateHandleSitsOutsideTheTopEdge()
    {
        // An axis-aligned frame: the pan grip is the top-edge midpoint, and
        // the rotate handle is pushed straight up, away from the centre.
        SKPoint[] corners = [new(10, 10), new(110, 10), new(110, 90), new(10, 90)];

        var handle = CanvasControl.RotateHandle(corners, 26f);

        Assert.Equal(60f, handle.X, 3);
        Assert.Equal(-16f, handle.Y, 3);
    }

    [AvaloniaFact]
    public void TheRotateHandleFollowsTheFrameRoll()
    {
        // The same frame rolled 90°: the top edge now faces right, so the
        // handle pushes right — it rides the frame, not the screen.
        SKPoint[] corners = [new(100, 0), new(100, 100), new(0, 100), new(0, 0)];

        var handle = CanvasControl.RotateHandle(corners, 26f);

        Assert.Equal(126f, handle.X, 3);
        Assert.Equal(50f, handle.Y, 3);
    }
}

[Collection("BrushState")]
public sealed class CameraGizmoTests : BrushStateIsolated
{
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, (MainViewModel)window.DataContext!);
    }

    [AvaloniaFact]
    public void AGizmoNudgeMovesTheFramingAndKeysIt()
    {
        var (_, vm) = Open();
        vm.AddCameraCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var x0 = vm.CameraX;
        var y0 = vm.CameraY;

        vm.NudgeCamera(12, -7);

        Assert.Equal(x0 + 12, vm.CameraX, 3);
        Assert.Equal(y0 - 7, vm.CameraY, 3);
        // Adjusting IS keying: the drag writes a key at the playhead, the
        // same bargain the numeric fields make.
        Assert.Contains(vm.CurrentFrameIndex, vm.CameraKeyFrames);
    }

    [AvaloniaFact]
    public void AZoomDragMultipliesAndClamps()
    {
        var (_, vm) = Open();
        vm.AddCameraCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.ZoomCameraBy(2.0);
        Assert.Equal(2.0, vm.CameraZoom, 3);

        vm.ZoomCameraBy(1e9);
        Assert.Equal(64.0, vm.CameraZoom, 3);

        // A degenerate factor is ignored, never written.
        vm.ZoomCameraBy(0);
        vm.ZoomCameraBy(double.NaN);
        Assert.Equal(64.0, vm.CameraZoom, 3);
    }

    [AvaloniaFact]
    public void ARotateDragAccumulatesDegrees()
    {
        var (_, vm) = Open();
        vm.AddCameraCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.RotateCameraBy(15);
        vm.RotateCameraBy(-40);

        Assert.Equal(-25.0, vm.CameraRotationDeg, 3);
    }
}
