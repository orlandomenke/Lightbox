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
/// Ctrl+T puts a handled box round the drawing, and a press on a handle has to reach
/// it. These drive real pointer and key events through the window.
/// </summary>
/// <remarks>
/// <para>
/// <b>B208 is why this file exists, and it is the shape of bug the suite could not
/// see.</b> Both halves worked: <c>TransformPreviewTests</c> drives the view model and
/// the pixels move, and the gizmo's own arithmetic is exact. What nothing exercised was
/// the <i>routing</i> — whether a press on the canvas reaches the gizmo at all — and
/// that is decided by one field the tool bar owns.
/// </para>
/// <para>
/// So these go the long way round on purpose: a real key event through
/// <c>MainWindow</c>, including the modifier release nobody thinks about, and a real
/// pointer press at a handle's actual screen position. Anything that starts by calling
/// <c>BeginTransform()</c> passes with the bug still in place.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class TransformGizmoInputTests(ITestOutputHelper output) : BrushStateIsolated
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

    private static PointerPressedEventArgs Press(Window w, Control t, Point at) =>
        new(t, Mouse, w, Root(w, t, at), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None);

    private static PointerEventArgs Move(Window w, Control t, Point at) =>
        new(InputElement.PointerMovedEvent, t, Mouse, w, Root(w, t, at), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other),
            KeyModifiers.None);

    private static PointerReleasedEventArgs Release(Window w, Control t, Point at) =>
        new(t, Mouse, w, Root(w, t, at), 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None, MouseButton.Left);

    private static void Send(Window w, Key key, KeyModifiers held, bool down) =>
        w.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = down ? InputElement.KeyDownEvent : InputElement.KeyUpEvent,
            Key = key,
            KeyModifiers = held,
        });

    /// <summary>Ctrl+T as a keyboard actually delivers it: Ctrl down, T, Ctrl up.</summary>
    /// <remarks>
    /// The release is the whole point. Dropping it makes every test here pass against
    /// the bug, which is exactly how the bug survived.
    /// </remarks>
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

    /// <summary>Drag the bottom-right corner handle out by 60 view pixels.</summary>
    private static void DragTheCorner(Window window, CanvasControl canvas)
    {
        var src = canvas.TransformQuadResult.Src;
        var (vx, vy) = canvas.DocToView(src[4], src[5]);
        var at = new Point(vx, vy);
        canvas.RaiseEvent(Press(window, canvas, at));
        canvas.RaiseEvent(Move(window, canvas, at + new Point(60, 60)));
        canvas.RaiseEvent(Release(window, canvas, at + new Point(60, 60)));
    }

    /// <summary><b>B208.</b> The whole gesture, from the keyboard, with the brush in hand.</summary>
    /// <remarks>
    /// Releasing Ctrl gave the brush back and put the canvas into paint mode with the
    /// gizmo still on screen, so the handle did nothing — and the press went to the
    /// brush instead, laying a stroke across the drawing the artist was transforming.
    /// The stroke assertion is the half that makes this a P1 rather than an annoyance.
    /// </remarks>
    [AvaloniaFact]
    public void AfterCtrlTTheHandlesTransformRatherThanPaint()
    {
        var (window, canvas, vm) = Open();
        Paint(vm);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(ToolId.Brush, vm.ActiveTool);
        var strokes = vm.PaintedCel().Strokes.Count;

        PressCtrlT(window);

        output.WriteLine($"after Ctrl+T: txActive {canvas.TransformSessionActive}, "
                         + $"tool {vm.ActiveTool}, toolMode {canvas.ToolMode}");
        Assert.True(canvas.TransformSessionActive, "the session did not start");
        Assert.Equal(CanvasControl.CanvasToolMode.Transform, canvas.ToolMode);

        DragTheCorner(window, canvas);

        var r = canvas.TransformAffineResult;
        output.WriteLine($"after the drag: scale ({r.ScaleX:F3},{r.ScaleY:F3}), "
                         + $"strokes {strokes} -> {vm.PaintedCel().Strokes.Count}");
        Assert.True(r.ScaleX > 1.01, $"the corner handle did nothing — ScaleX is {r.ScaleX:F3}");
        Assert.Equal(strokes, vm.PaintedCel().Strokes.Count);
    }

    /// <summary>
    /// The same gesture from a tool that cannot be borrowed from — which is why the bug
    /// read as intermittent rather than as broken.
    /// </summary>
    [AvaloniaFact]
    public void CtrlTFromANonBorrowableToolWorksTheSameWay()
    {
        var (window, canvas, vm) = Open();
        Paint(vm);
        vm.ActiveTool = ToolId.Arrow;   // BorrowedFor returns null for this one
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        PressCtrlT(window);
        Assert.True(canvas.TransformSessionActive);
        DragTheCorner(window, canvas);

        var r = canvas.TransformAffineResult;
        output.WriteLine($"from the arrow: scale ({r.ScaleX:F3},{r.ScaleY:F3})");
        Assert.True(r.ScaleX > 1.01, "this path always worked and must keep working");
    }

    /// <summary>
    /// Deliberately choosing a tool ends the session — and ends it cleanly, canvas
    /// handed back, rather than leaving a gizmo nothing can drive.
    /// </summary>
    /// <remarks>
    /// The owner's call: reaching for a tool means you are done transforming. It
    /// discards rather than applies, because the preview was never an edit and there is
    /// no undo step to recover a commit nobody asked for. Enter is still the only thing
    /// that writes.
    /// </remarks>
    [AvaloniaFact]
    public void ChoosingAToolMidSessionCancelsTheTransform()
    {
        var (window, canvas, vm) = Open();
        Paint(vm);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        PressCtrlT(window);
        var strokes = vm.PaintedCel().Strokes.Count;
        var undos = vm.UndoDepth;

        vm.ActiveTool = ToolId.Eraser;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        output.WriteLine($"tool {vm.ActiveTool}, txActive {canvas.TransformSessionActive}, "
                         + $"toolMode {canvas.ToolMode}, undo {undos} -> {vm.UndoDepth}");
        Assert.False(vm.TransformActive);
        Assert.False(canvas.TransformSessionActive);
        Assert.Equal(CanvasControl.CanvasToolMode.Paint, canvas.ToolMode);   // the eraser paints
        // Discarded, not committed: no edit, no undo step, and the record untouched.
        Assert.Equal(undos, vm.UndoDepth);
        Assert.Equal(strokes, vm.PaintedCel().Strokes.Count);
    }

    /// <summary>
    /// The borrowed tool is not a choice, so it must not cancel — which is the
    /// distinction the whole fix rests on.
    /// </summary>
    /// <remarks>
    /// Ctrl+T <i>is</i> a tool change followed by a tool change: Ctrl borrows the
    /// eyedropper on the way in and gives the brush back on the way out. If a borrow
    /// counted as choosing, the shortcut would now cancel the session it just started —
    /// the same bug wearing the fix's clothes.
    /// </remarks>
    [AvaloniaFact]
    public void ABorrowedToolIsNotAChoiceAndDoesNotCancel()
    {
        var (window, canvas, vm) = Open();
        Paint(vm);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        PressCtrlT(window);   // borrows the picker, starts the session, gives the brush back

        output.WriteLine($"after the whole gesture: txActive {canvas.TransformSessionActive}, "
                         + $"tool {vm.ActiveTool}, toolMode {canvas.ToolMode}");
        Assert.True(vm.TransformActive, "the borrow's restore cancelled the session it started");
        Assert.Equal(CanvasControl.CanvasToolMode.Transform, canvas.ToolMode);

        // And a plain Ctrl hold mid-session still borrows without ending anything.
        Send(window, Key.LeftCtrl, KeyModifiers.Control, down: true);
        Send(window, Key.LeftCtrl, KeyModifiers.None, down: false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(vm.TransformActive);
        DragTheCorner(window, canvas);
        Assert.True(canvas.TransformAffineResult.ScaleX > 1.01);
    }

    /// <summary>
    /// Ending the session hands the canvas back, or the guard would be a trap of its
    /// own — a transform that once ran would leave the artist unable to draw.
    /// </summary>
    [AvaloniaFact]
    public void EndingTheSessionGivesTheCanvasBackToTheTool()
    {
        var (window, canvas, vm) = Open();
        Paint(vm);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        PressCtrlT(window);
        Assert.Equal(CanvasControl.CanvasToolMode.Transform, canvas.ToolMode);

        vm.CancelTransform();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        output.WriteLine($"after Escape: txActive {canvas.TransformSessionActive}, "
                         + $"tool {vm.ActiveTool}, toolMode {canvas.ToolMode}");
        Assert.False(canvas.TransformSessionActive);
        Assert.Equal(CanvasControl.CanvasToolMode.Paint, canvas.ToolMode);
    }

    /// <summary>
    /// The Move tool opens a session with no gizmo, and must keep following the tool —
    /// the reason the guard reads the canvas's flag rather than the view model's.
    /// </summary>
    [AvaloniaFact]
    public void AGizmolessSessionDoesNotFreezeTheCanvasMode()
    {
        var (_, canvas, vm) = Open();
        Paint(vm);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(vm.BeginTransform(gizmo: false));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(canvas.TransformSessionActive, "a gizmoless session must not claim the canvas");

        vm.ActiveTool = ToolId.Eraser;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        output.WriteLine($"gizmoless session, tool {vm.ActiveTool}, toolMode {canvas.ToolMode}");
        Assert.Equal(CanvasControl.CanvasToolMode.Paint, canvas.ToolMode);
    }
}
