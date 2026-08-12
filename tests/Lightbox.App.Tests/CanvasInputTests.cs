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

    // ---- B185: refocusing with a pen must not paint from stale positions ------
    //
    // Activating the window with a pen makes Windows synthesise mouse activity
    // at the mouse's stale position, interleaved with the pen's own events.
    // Each test drives one of the ways that used to reach the stroke: a second
    // device's move, press or release mid-stroke, hover history in a coalesced
    // batch, and a zero-pressure packet promoted to a full-size dab.

    private static readonly Pointer PenPointer = new(2, PointerType.Pen, true);
    private static readonly Pointer MousePointer = new(1, PointerType.Mouse, true);

    private static Point Root(Window window, Visual target, Point local) =>
        target.TranslatePoint(local, window) ?? local;

    private static PointerPressedEventArgs Press(
        Window window, Control target, Pointer pointer, Point at, float pressure = 0.5f) =>
        new(target, pointer, window, Root(window, target, at), 0,
            new PointerPointProperties(
                RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed,
                0, pressure, 0, 0), // twist, pressure, xTilt, yTilt
            KeyModifiers.None);

    private static PointerEventArgs Move(
        Window window, Control target, Pointer pointer, Point at,
        float pressure = 0.5f, bool contact = true) =>
        new(InputElement.PointerMovedEvent, target, pointer, window, Root(window, target, at), 0,
            new PointerPointProperties(
                contact ? RawInputModifiers.LeftMouseButton : RawInputModifiers.None,
                PointerUpdateKind.Other,
                0, pressure, 0, 0),
            KeyModifiers.None);

    private static PointerReleasedEventArgs Release(
        Window window, Control target, Pointer pointer, Point at) =>
        new(target, pointer, window, Root(window, target, at), 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None, MouseButton.Left);

    [AvaloniaFact]
    public void AStrayMouseMoveMidPenStrokeAddsNothing()
    {
        var (window, canvas, vm) = NewRig();
        var batches = 0;
        canvas.PaintMoved += _ => batches++;

        canvas.RaiseEvent(Press(window, canvas, PenPointer, new Point(400, 300)));
        // The stale mouse, 300 px away, moving with its button "down" — only
        // the pointer identity can tell it apart from the pen.
        canvas.RaiseEvent(Move(window, canvas, MousePointer, new Point(100, 100)));
        Assert.Equal(0, batches);

        canvas.RaiseEvent(Move(window, canvas, PenPointer, new Point(410, 300)));
        canvas.RaiseEvent(Release(window, canvas, PenPointer, new Point(410, 300)));

        Assert.Equal(1, batches);
        var stroke = Assert.Single(Strokes(vm));
        // Everything the stroke holds came from the pen's 10 px of travel; the
        // stale position would sit hundreds of document pixels away.
        var first = stroke.Points[0];
        Assert.All(stroke.Points, p =>
            Assert.True(Math.Abs(p.X - first.X) + Math.Abs(p.Y - first.Y) < 50,
                $"stray sample at ({p.X:F0}, {p.Y:F0}), stroke began at ({first.X:F0}, {first.Y:F0})"));
        window.Close();
    }

    [AvaloniaFact]
    public void AStrayMousePressMidPenStrokeDoesNotRestartIt()
    {
        var (window, canvas, _) = NewRig();
        var starts = 0;
        canvas.PaintStarted += (_, _, _, _, _) => starts++;

        canvas.RaiseEvent(Press(window, canvas, PenPointer, new Point(400, 300)));
        canvas.RaiseEvent(Press(window, canvas, MousePointer, new Point(100, 100)));

        Assert.Equal(1, starts);
        canvas.RaiseEvent(Release(window, canvas, PenPointer, new Point(400, 300)));
        window.Close();
    }

    [AvaloniaFact]
    public void AStrayMouseReleaseMidPenStrokeDoesNotEndIt()
    {
        var (window, canvas, vm) = NewRig();
        var ends = 0;
        canvas.PaintEnded += () => ends++;

        canvas.RaiseEvent(Press(window, canvas, PenPointer, new Point(400, 300)));
        canvas.RaiseEvent(Release(window, canvas, MousePointer, new Point(100, 100)));
        Assert.Equal(0, ends);

        // The pen is still down and still painting.
        canvas.RaiseEvent(Move(window, canvas, PenPointer, new Point(450, 300)));
        canvas.RaiseEvent(Release(window, canvas, PenPointer, new Point(450, 300)));

        Assert.Equal(1, ends);
        var stroke = Assert.Single(Strokes(vm));
        Assert.True(stroke.Points.Count >= 2, "the move after the stray release was dropped");
        window.Close();
    }

    [AvaloniaFact]
    public void AHoverSampleInACoalescedBatchIsNotPainted()
    {
        var (window, canvas, _) = NewRig();
        var batches = 0;
        canvas.PaintMoved += _ => batches++;

        canvas.RaiseEvent(Press(window, canvas, PenPointer, new Point(400, 300)));
        // A pen sample with no contact: the hover history a coalesced batch
        // can reach back into after a refocus. It must not become a dab.
        canvas.RaiseEvent(Move(window, canvas, PenPointer, new Point(150, 120), contact: false));
        Assert.Equal(0, batches);

        canvas.RaiseEvent(Move(window, canvas, PenPointer, new Point(410, 300)));
        Assert.Equal(1, batches);
        canvas.RaiseEvent(Release(window, canvas, PenPointer, new Point(410, 300)));
        window.Close();
    }

    [AvaloniaFact]
    public void APressureAwarePenNeverPromotesAZeroPacketToFullPressure()
    {
        var (window, canvas, _) = NewRig();
        var pressures = new List<double>();
        canvas.PaintMoved += samples => pressures.AddRange(samples.Select(s => s.Pressure));

        // This pen has proven it reports pressure...
        canvas.RaiseEvent(Press(window, canvas, PenPointer, new Point(400, 300), pressure: 0.6f));
        // ...so a zero packet is a gap in the stream, not a full-force dab.
        canvas.RaiseEvent(Move(window, canvas, PenPointer, new Point(410, 300), pressure: 0f));
        canvas.RaiseEvent(Release(window, canvas, PenPointer, new Point(410, 300)));

        var pressure = Assert.Single(pressures);
        Assert.Equal(0.0, pressure, 3);
        window.Close();
    }

    [AvaloniaFact]
    public void APenThatNeverReportsPressurePaintsAtFullLikeAMouse()
    {
        var (window, canvas, _) = NewRig();
        var startPressure = double.NaN;
        canvas.PaintStarted += (_, _, p, _, _) => startPressure = p;

        // A driver that hides the pen from the pressure API reads zero forever;
        // painting at 100% is the accommodation that keeps such pens usable.
        canvas.RaiseEvent(Press(window, canvas, PenPointer, new Point(400, 300), pressure: 0f));
        canvas.RaiseEvent(Release(window, canvas, PenPointer, new Point(400, 300)));

        Assert.Equal(1.0, startPressure, 3);
        window.Close();
    }

    [AvaloniaFact]
    public void APenWhoseWholeStrokeReportsNoPressureWinsBackFullSize()
    {
        var (window, canvas, _) = NewRig();
        var startPressures = new List<double>();
        canvas.PaintStarted += (_, _, p, _, _) => startPressures.Add(p);

        // A pressure-reporting pen makes the flag sticky...
        canvas.RaiseEvent(Press(window, canvas, PenPointer, new Point(400, 300), pressure: 0.6f));
        canvas.RaiseEvent(Release(window, canvas, PenPointer, new Point(400, 300)));

        // ...which must not condemn a second pen with no pressure axis to
        // near-invisible strokes for the rest of the session. Its first
        // stroke is faint — that is the price of telling it apart from a
        // refocus gap — and a whole stroke of zeros clears the flag, so its
        // next one is back at 100%.
        var pressureless = new Pointer(3, PointerType.Pen, true);
        canvas.RaiseEvent(Press(window, canvas, pressureless, new Point(200, 200), pressure: 0f));
        canvas.RaiseEvent(Release(window, canvas, pressureless, new Point(200, 200)));
        canvas.RaiseEvent(Press(window, canvas, pressureless, new Point(210, 200), pressure: 0f));
        canvas.RaiseEvent(Release(window, canvas, pressureless, new Point(210, 200)));

        Assert.Equal(0.6, startPressures[0], 3);
        Assert.Equal(0.0, startPressures[1], 3);
        Assert.Equal(1.0, startPressures[2], 3);
        window.Close();
    }

    [AvaloniaFact]
    public void DeactivationCancelsTheStrokeInFlight()
    {
        var (window, canvas, vm) = NewRig();
        var ends = 0;
        canvas.PaintEnded += () => ends++;

        canvas.RaiseEvent(Press(window, canvas, PenPointer, new Point(400, 300)));
        // What MainWindow calls on Deactivated: the release went to whichever
        // window took the focus, so the stroke must not stay in flight.
        canvas.CancelPointerGestures();
        Assert.Equal(1, ends);

        // A move arriving afterwards resumes nothing.
        var batches = 0;
        canvas.PaintMoved += _ => batches++;
        canvas.RaiseEvent(Move(window, canvas, PenPointer, new Point(200, 200)));
        Assert.Equal(0, batches);

        Assert.Single(Strokes(vm));
        window.Close();
    }
}
