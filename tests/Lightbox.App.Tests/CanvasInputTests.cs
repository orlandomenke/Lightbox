using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Full input pipeline through real pointer events: window → CanvasControl →
/// view transform → VM. Guards the "can still draw after using view tools"
/// contract.
/// </summary>
public class CanvasInputTests
{
    private static (Window Window, CanvasControl Canvas, MainViewModel Vm) NewRig()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        var canvas = new CanvasControl();
        var window = new Window { Width = 800, Height = 600, Content = canvas };
        vm.SnapshotChanged += s => canvas.UpdateSnapshot(s);
        canvas.PaintStarted += vm.BeginStroke;
        canvas.PaintMoved += vm.MoveStrokeBatch;
        canvas.PaintEnded += vm.EndStroke;
        window.Show();
        vm.PublishSnapshot();
        return (window, canvas, vm);
    }

    private static List<Lightbox.Core.Documents.Stroke> Strokes(MainViewModel vm) =>
        (vm.PaintedCel()).Strokes;

    [AvaloniaFact]
    public void MouseDrag_PaintsAStroke()
    {
        var (window, _, vm) = NewRig();

        window.MouseDown(new Point(400, 300), MouseButton.Left);
        window.MouseMove(new Point(430, 300));
        window.MouseUp(new Point(430, 300), MouseButton.Left);

        Assert.Single(Strokes(vm));
        window.Close();
    }

    [AvaloniaFact]
    public void MouseDrag_AfterWheelZoom_StillPaints()
    {
        var (window, _, vm) = NewRig();

        window.MouseWheel(new Point(400, 300), new Vector(0, 2)); // zoom in
        window.MouseDown(new Point(400, 300), MouseButton.Left);
        window.MouseMove(new Point(430, 300));
        window.MouseUp(new Point(430, 300), MouseButton.Left);

        Assert.Single(Strokes(vm));
        window.Close();
    }

    [AvaloniaFact]
    public void MouseDrag_AfterMiddleButtonPan_StillPaints()
    {
        var (window, _, vm) = NewRig();

        window.MouseDown(new Point(200, 200), MouseButton.Middle);
        window.MouseMove(new Point(260, 240));
        window.MouseUp(new Point(260, 240), MouseButton.Middle);

        window.MouseDown(new Point(400, 300), MouseButton.Left);
        window.MouseMove(new Point(430, 300));
        window.MouseUp(new Point(430, 300), MouseButton.Left);

        Assert.Single(Strokes(vm));
        window.Close();
    }

    [AvaloniaFact]
    public void MouseDrag_AfterMirrorRotateZoom_StillPaints_AtCorrectDocPoint()
    {
        var (window, canvas, vm) = NewRig();

        canvas.ToggleMirror();
        canvas.RotateBy(30);
        window.MouseWheel(new Point(300, 200), new Vector(0, 1));
        window.MouseWheel(new Point(300, 200), new Vector(0, -1), RawInputModifiers.Shift); // rotate back a bit

        window.MouseDown(new Point(400, 300), MouseButton.Left);
        window.MouseMove(new Point(420, 310));
        window.MouseUp(new Point(420, 310), MouseButton.Left);

        var stroke = Assert.Single(Strokes(vm));
        // The stroke's points must be inside the 960×540 document, i.e. the
        // inverse mapping produced sane document coordinates.
        Assert.All(stroke.Points, p =>
        {
            Assert.InRange(p.X, -100, vm.Doc.Scene.Width + 100);
            Assert.InRange(p.Y, -100, vm.Doc.Scene.Height + 100);
        });
        window.Close();
    }
}
