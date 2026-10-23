using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Shift holds a drag to one axis on the Transform gizmo's body and on the Move
/// tool, driven through real pointer events on the canvas.
/// </summary>
/// <remarks>
/// <para>
/// <b>B499.</b> Shift reached the gizmo on every move, but only the corner
/// scale read it — as "uniform". Dragging the body ignored it, so the one
/// transform an artist most often wants straight was the one that would not
/// hold still. The brush, the Move tool, the gradient and the object drags all
/// lock; the gizmo was the exception nobody had written down.
/// </para>
/// <para>
/// <b>The Move tool half is here for the routing, not the arithmetic.</b>
/// <c>MoveToolAndModifierTests</c> already proves <c>UpdateMove</c> zeroes the
/// short axis when told to. Nothing proved the canvas tells it — the shape of
/// bug <c>TransformGizmoInputTests</c> exists for (B208): both halves exact,
/// the wire between them untested.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class TransformGizmoAxisLockTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static readonly Pointer Mouse = new(1, PointerType.Mouse, true);

    private static (MainWindow Window, CanvasControl Canvas, MainViewModel Vm) Open()
    {
        var window = new MainWindow { Width = 1200, Height = 900 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        var canvas = window.GetVisualDescendants().OfType<CanvasControl>().First();
        return (window, canvas, vm);
    }

    private static Point Root(Window window, Visual target, Point local) =>
        target.TranslatePoint(local, window) ?? local;

    private static PointerPressedEventArgs Press(Window w, Control t, Point at, KeyModifiers held) =>
        new(t, Mouse, w, Root(w, t, at), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            held);

    private static PointerEventArgs Move(Window w, Control t, Point at, KeyModifiers held) =>
        new(InputElement.PointerMovedEvent, t, Mouse, w, Root(w, t, at), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other),
            held);

    private static PointerReleasedEventArgs Release(Window w, Control t, Point at, KeyModifiers held) =>
        new(t, Mouse, w, Root(w, t, at), 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            held, MouseButton.Left);

    private static void Send(Window w, Key key, KeyModifiers held, bool down) =>
        w.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = down ? InputElement.KeyDownEvent : InputElement.KeyUpEvent,
            Key = key,
            KeyModifiers = held,
        });

    /// <summary>Ctrl+T with the release, for <c>TransformGizmoInputTests</c>' reason (B208).</summary>
    private static void PressCtrlT(Window w)
    {
        Send(w, Key.LeftCtrl, KeyModifiers.Control, down: true);
        Send(w, Key.T, KeyModifiers.Control, down: true);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Send(w, Key.LeftCtrl, KeyModifiers.None, down: false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static void Paint(MainViewModel vm)
    {
        vm.SmoothStrokes = false;
        vm.BrushSize = 30;
        vm.BeginStroke(200, 200, 1);
        vm.MoveStroke(600, 400, 1);
        vm.EndStroke();
    }

    /// <summary>
    /// A point inside the gizmo's box, in view pixels — a press there grabs the body.
    /// </summary>
    /// <remarks>
    /// Halfway from the centre to the first corner, not the centre itself: the
    /// pivot sits at the centre and takes that press first.
    /// </remarks>
    private static Point InsideTheBox(CanvasControl canvas)
    {
        var src = canvas.TransformQuadResult.Src;
        var cx = (src[0] + src[2] + src[4] + src[6]) / 4;
        var cy = (src[1] + src[3] + src[5] + src[7]) / 4;
        var (vx, vy) = canvas.DocToView((cx + src[0]) / 2, (cy + src[1]) / 2);
        return new Point(vx, vy);
    }

    /// <summary>Press on the box, move by <paramref name="by"/> view pixels in two steps, release.</summary>
    /// <remarks>
    /// Two steps rather than one so the second move is the one read: a lock
    /// that only held on the first event would pass a single-move test.
    /// </remarks>
    private static void DragTheBody(
        Window window, CanvasControl canvas, Point by, KeyModifiers held,
        KeyModifiers? secondHeld = null)
    {
        var at = InsideTheBox(canvas);
        canvas.RaiseEvent(Press(window, canvas, at, held));
        canvas.RaiseEvent(Move(window, canvas, at + by * 0.5, held));
        canvas.RaiseEvent(Move(window, canvas, at + by, secondHeld ?? held));
    }

    private (MainWindow Window, CanvasControl Canvas) OpenWithGizmo()
    {
        var (window, canvas, vm) = Open();
        Paint(vm);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        PressCtrlT(window);
        Assert.True(canvas.TransformSessionActive, "the session did not start");
        return (window, canvas);
    }

    [AvaloniaFact]
    public void WithoutShiftAGizmoMoveFollowsThePointerOnBothAxes()
    {
        var (window, canvas) = OpenWithGizmo();

        DragTheBody(window, canvas, new Point(80, 20), KeyModifiers.None);

        var r = canvas.TransformAffineResult;
        output.WriteLine($"unlocked: offset ({r.Dx:F3},{r.Dy:F3})");
        Assert.True(r.Dx > 1, $"the body drag did not move — Dx {r.Dx:F3}");
        Assert.True(r.Dy > 0.1, $"the control moved on one axis only — Dy {r.Dy:F3}");
    }

    [AvaloniaFact]
    public void ShiftHoldsAGizmoMoveToTheHorizontal()
    {
        var (window, canvas) = OpenWithGizmo();

        DragTheBody(window, canvas, new Point(80, 20), KeyModifiers.Shift);

        var r = canvas.TransformAffineResult;
        output.WriteLine($"shift, mostly sideways: offset ({r.Dx:F3},{r.Dy:F3})");
        Assert.True(r.Dx > 1, $"the body drag did not move — Dx {r.Dx:F3}");
        Assert.Equal(0, r.Dy, 6);
    }

    [AvaloniaFact]
    public void ShiftHoldsAGizmoMoveToTheVertical()
    {
        var (window, canvas) = OpenWithGizmo();

        DragTheBody(window, canvas, new Point(15, -70), KeyModifiers.Shift);

        var r = canvas.TransformAffineResult;
        output.WriteLine($"shift, mostly upward: offset ({r.Dx:F3},{r.Dy:F3})");
        Assert.True(r.Dy < -1, $"the body drag did not move — Dy {r.Dy:F3}");
        Assert.Equal(0, r.Dx, 6);
    }

    /// <summary>
    /// Shift is read per move, the way the brush and the object drags read it:
    /// letting go mid-drag hands the box back to the pointer.
    /// </summary>
    [AvaloniaFact]
    public void ReleasingShiftMidGizmoMoveHandsItBackToThePointer()
    {
        var (window, canvas) = OpenWithGizmo();

        DragTheBody(window, canvas, new Point(80, 20), KeyModifiers.Shift, secondHeld: KeyModifiers.None);

        var r = canvas.TransformAffineResult;
        output.WriteLine($"shift released: offset ({r.Dx:F3},{r.Dy:F3})");
        Assert.True(r.Dy > 0.1, $"the lock outlived the key — Dy {r.Dy:F3}");
    }

    /// <summary>
    /// The perspective box moves its four corners rather than an offset — a
    /// second branch, so it gets its own proof rather than the affine one's.
    /// </summary>
    [AvaloniaFact]
    public void ShiftHoldsAPerspectiveBoxMoveToOneAxis()
    {
        var (window, canvas) = OpenWithGizmo();
        canvas.TransformPerspective = true;

        DragTheBody(window, canvas, new Point(80, 20), KeyModifiers.Shift);

        var (src, dst) = canvas.TransformQuadResult;
        output.WriteLine($"perspective, shift: corner 0 moved ({dst[0] - src[0]:F3},{dst[1] - src[1]:F3})");
        Assert.True(canvas.TransformIsPerspectiveResult);
        for (var i = 0; i < 4; i++)
        {
            Assert.True(dst[i * 2] - src[i * 2] > 1, $"corner {i} did not move sideways");
            Assert.Equal(0, dst[i * 2 + 1] - src[i * 2 + 1], 6);
        }
    }

    private static List<(double X, double Y)> PointsOf(MainViewModel vm) =>
        vm.PaintedCel().Strokes[^1].Points.Select(p => ((double)p.X, (double)p.Y)).ToList();

    /// <summary>The Move tool's lock, from the pointer rather than from <c>UpdateMove</c>.</summary>
    [AvaloniaTheory]
    [InlineData(80, 20, true)]
    [InlineData(15, -70, false)]
    public void ShiftHoldsAMoveToolDragToOneAxis(double byX, double byY, bool horizontal)
    {
        var (window, canvas, vm) = Open();
        Paint(vm);
        vm.ActiveTool = ToolId.Move;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(CanvasControl.CanvasToolMode.Move, canvas.ToolMode);
        var before = PointsOf(vm);

        var (vx, vy) = canvas.DocToView(400, 300);
        var at = new Point(vx, vy);
        var by = new Point(byX, byY);
        canvas.RaiseEvent(Press(window, canvas, at, KeyModifiers.Shift));
        canvas.RaiseEvent(Move(window, canvas, at + by * 0.5, KeyModifiers.Shift));
        canvas.RaiseEvent(Move(window, canvas, at + by, KeyModifiers.Shift));
        canvas.RaiseEvent(Release(window, canvas, at + by, KeyModifiers.Shift));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var after = PointsOf(vm);
        var dx = after[0].X - before[0].X;
        var dy = after[0].Y - before[0].Y;
        output.WriteLine($"move tool, shift: first point moved ({dx:F3},{dy:F3})");
        if (horizontal)
        {
            Assert.True(Math.Abs(dx) > 1, $"the drawing did not move — dx {dx:F3}");
            Assert.Equal(0, dy, 3);
        }
        else
        {
            Assert.True(Math.Abs(dy) > 1, $"the drawing did not move — dy {dy:F3}");
            Assert.Equal(0, dx, 3);
        }
    }
}
