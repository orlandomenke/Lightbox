using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// While a transform is open, Ctrl+Z steps back through the session's own
/// tweaks; applying ends that history, so the commit is one document step; and
/// moving to another frame, layer or document applies the session first.
/// </summary>
/// <remarks>
/// <para>
/// Before this, Ctrl+Z mid-session went straight to the document stack — it
/// took back the stroke drawn before the transform while the box and its
/// preview stayed up over a drawing that had changed underneath them. And a
/// scrub left the session open over the old frame, so the next Enter wrote a
/// transform to a drawing the artist was no longer looking at.
/// </para>
/// <para>
/// Driven the long way round, through real keys into <c>MainWindow</c>, for the
/// reason <c>TransformGizmoInputTests</c> gives: the gizmo's history lives in
/// the canvas and the routing lives in the view model, and only the window
/// joins them.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class TransformSessionUndoTests(ITestOutputHelper output) : BrushStateIsolated
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

    private static void DragTheCorner(Window w, CanvasControl canvas, double by)
    {
        var src = canvas.TransformQuadResult.Src;
        var r = canvas.TransformAffineResult;
        // The corner where the box is drawn now, not where it started.
        var x = r.PivotX + (src[4] - r.PivotX) * r.ScaleX + r.Dx;
        var y = r.PivotY + (src[5] - r.PivotY) * r.ScaleY + r.Dy;
        var (vx, vy) = canvas.DocToView(x, y);
        var at = new Point(vx, vy);
        var to = at + new Point(by, by);
        canvas.RaiseEvent(new PointerPressedEventArgs(canvas, Mouse, w, Root(w, canvas, at), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None));
        canvas.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, canvas, Mouse, w,
            Root(w, canvas, to), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other),
            KeyModifiers.None));
        canvas.RaiseEvent(new PointerReleasedEventArgs(canvas, Mouse, w, Root(w, canvas, to), 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None, MouseButton.Left));
    }

    private static void Send(Window w, Key key, KeyModifiers held, bool down) =>
        w.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = down ? InputElement.KeyDownEvent : InputElement.KeyUpEvent,
            Key = key,
            KeyModifiers = held,
        });

    /// <summary>A Ctrl chord as a keyboard delivers it, the release included.</summary>
    private static void Ctrl(Window w, Key key)
    {
        Send(w, Key.LeftCtrl, KeyModifiers.Control, down: true);
        Send(w, key, KeyModifiers.Control, down: true);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Send(w, Key.LeftCtrl, KeyModifiers.None, down: false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static void Press(Window w, Key key)
    {
        Send(w, key, KeyModifiers.None, down: true);
        Send(w, key, KeyModifiers.None, down: false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static void Paint(MainViewModel vm)
    {
        vm.SmoothStrokes = false;
        vm.BrushSize = 30;
        vm.BeginStroke(200, 200, 1);
        vm.MoveStroke(600, 400, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static List<StrokePoint> PointsOf(Frame frame) =>
        frame.Strokes.SelectMany(s => s.Points).ToList();

    [AvaloniaFact]
    public void CtrlZMidSessionStepsBackTheBoxAndNeverTheDocument()
    {
        var (window, canvas, vm) = Open();
        Paint(vm);
        Paint(vm); // a second stroke: the document has a step Ctrl+Z could wrongly take
        var steps = vm.RecordedStepCount;
        var strokes = vm.PaintedCel().Strokes.Count;

        Ctrl(window, Key.T);
        DragTheCorner(window, canvas, 40);
        var afterFirst = canvas.TransformAffineResult.ScaleX;
        DragTheCorner(window, canvas, 40);
        var afterSecond = canvas.TransformAffineResult.ScaleX;
        output.WriteLine($"scale after drags: {afterFirst:F3}, {afterSecond:F3}; depth {canvas.TransformUndoDepth}");
        Assert.True(afterSecond > afterFirst + 0.01, "the second drag did nothing; the test cannot tell steps apart");
        Assert.Equal(2, canvas.TransformUndoDepth);

        Ctrl(window, Key.Z);
        Assert.Equal(afterFirst, canvas.TransformAffineResult.ScaleX, 9);
        Ctrl(window, Key.Z);
        Assert.Equal(1.0, canvas.TransformAffineResult.ScaleX, 9);
        // An empty session still does not fall through to the document.
        Ctrl(window, Key.Z);

        output.WriteLine($"steps {steps} -> {vm.RecordedStepCount}, strokes {strokes} -> {vm.PaintedCel().Strokes.Count}");
        Assert.True(vm.TransformActive, "Ctrl+Z ended the session");
        Assert.Equal(steps, vm.RecordedStepCount);
        Assert.Equal(strokes, vm.PaintedCel().Strokes.Count);

        Ctrl(window, Key.Y);
        Assert.Equal(afterFirst, canvas.TransformAffineResult.ScaleX, 9);
    }

    /// <summary>A new tweak after an undo is a new branch: what was undone cannot be redone.</summary>
    [AvaloniaFact]
    public void ANewTweakAfterUndoDropsTheRedo()
    {
        var (window, canvas, vm) = Open();
        Paint(vm);
        Ctrl(window, Key.T);
        DragTheCorner(window, canvas, 40);
        Ctrl(window, Key.Z);
        Assert.Equal(1, canvas.TransformRedoDepth);

        canvas.MirrorTransformGizmo(horizontal: true);

        Assert.Equal(0, canvas.TransformRedoDepth);
        Assert.Equal(1, canvas.TransformUndoDepth);
        Assert.True(canvas.UndoTransformStep(), "a mirror is a step of its own");
        Assert.Equal(1.0, canvas.TransformAffineResult.ScaleX, 9);
    }

    /// <summary>
    /// A press that moves nothing is not a step — otherwise Ctrl+Z would spend
    /// presses taking back clicks that changed nothing.
    /// </summary>
    [AvaloniaFact]
    public void APressThatMovesNothingIsNotAStep()
    {
        var (window, canvas, vm) = Open();
        Paint(vm);
        Ctrl(window, Key.T);
        DragTheCorner(window, canvas, 0);
        Assert.Equal(0, canvas.TransformUndoDepth);
    }

    /// <summary>
    /// After Enter, the session and its history are gone, and one Ctrl+Z puts the
    /// drawing back as it was before the transform began — however many tweaks it took.
    /// </summary>
    [AvaloniaFact]
    public void AfterApplyOneCtrlZRestoresTheDrawingFromBeforeTheTransform()
    {
        var (window, canvas, vm) = Open();
        Paint(vm);
        var before = PointsOf(vm.PaintedCel());
        var steps = vm.RecordedStepCount;

        Ctrl(window, Key.T);
        DragTheCorner(window, canvas, 40);
        canvas.MirrorTransformGizmo(horizontal: true);
        DragTheCorner(window, canvas, 30);
        Press(window, Key.Enter);

        Assert.False(vm.TransformActive);
        Assert.Equal(steps + 1, vm.RecordedStepCount);
        Assert.NotEqual(before, PointsOf(vm.PaintedCel()));

        Ctrl(window, Key.Z);

        Assert.Equal(steps, vm.RecordedStepCount);
        Assert.Equal(before, PointsOf(vm.PaintedCel()));
    }

    /// <summary>Changing frame is Enter: the session applies to the frame it was on, then ends.</summary>
    [AvaloniaFact]
    public void ChangingFrameAppliesTheSessionToTheFrameItWasOn()
    {
        var (window, canvas, vm) = Open();
        Paint(vm);
        vm.AddFrameCommand.Execute(null);
        vm.CurrentFrameIndex = 0;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var before = PointsOf(vm.PaintedCel(0));
        var steps = vm.RecordedStepCount;

        Ctrl(window, Key.T);
        DragTheCorner(window, canvas, 40);
        vm.CurrentFrameIndex = 1;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        output.WriteLine($"active {vm.TransformActive}, gizmo {canvas.TransformSessionActive}, steps {steps} -> {vm.RecordedStepCount}");
        Assert.False(vm.TransformActive);
        Assert.False(canvas.TransformSessionActive);
        Assert.Equal(1, vm.CurrentFrameIndex);
        Assert.Equal(steps + 1, vm.RecordedStepCount);
        Assert.NotEqual(before, PointsOf(vm.PaintedCel(0)));

        // And the step it left is the ordinary one: Ctrl+Z from the new frame
        // puts the old frame back.
        Ctrl(window, Key.Z);
        Assert.Equal(before, PointsOf(vm.PaintedCel(0)));
    }

    [AvaloniaFact]
    public void ChangingLayerAppliesTheSession()
    {
        var (window, canvas, vm) = Open();
        Paint(vm);
        var drawingLayer = vm.ActiveLayerIndex;
        var cel = vm.PaintedCel();
        var before = PointsOf(cel);
        var steps = vm.RecordedStepCount;

        Ctrl(window, Key.T);
        DragTheCorner(window, canvas, 40);
        vm.ActiveLayerIndex = drawingLayer == 0 ? 1 : 0;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(vm.TransformActive);
        Assert.False(canvas.TransformSessionActive);
        Assert.Equal(steps + 1, vm.RecordedStepCount);
        Assert.NotEqual(before, PointsOf(cel));
    }

    /// <summary>
    /// Changing frame with the box untouched is Enter on an untouched box —
    /// the session ends and nothing is recorded.
    /// </summary>
    [AvaloniaFact]
    public void ChangingFrameWithAnUntouchedBoxRecordsNothing()
    {
        var (window, canvas, vm) = Open();
        Paint(vm);
        vm.AddFrameCommand.Execute(null);
        vm.CurrentFrameIndex = 0;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var steps = vm.RecordedStepCount;

        Ctrl(window, Key.T);
        vm.CurrentFrameIndex = 1;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(vm.TransformActive);
        Assert.Equal(steps, vm.RecordedStepCount);
    }

    // ---- the adversarial pass: ways leaving could apply to the wrong place ----

    /// <summary>
    /// Switching tab applies to the document being left, and marks <em>that</em>
    /// tab edited — not the one being arrived at.
    /// </summary>
    /// <remarks>
    /// The confirm first ran from the Changed half of <c>ActiveTab</c>, where the
    /// tab had already moved: the edit landed in the old document and its dirty
    /// mark on the new tab.
    /// </remarks>
    [AvaloniaFact]
    public void SwitchingTabAppliesToTheDocumentLeftAndMarksThatTab()
    {
        var (window, canvas, vm) = Open();
        vm.NewDocument(new NewDocumentSettings("A", 960, 540, 12, 72, "#ffffff", true));
        vm.NewDocument(new NewDocumentSettings("B", 960, 540, 12, 72, "#ffffff", true));
        var (a, b) = (vm.Tabs[^2], vm.Tabs[^1]);
        vm.ActiveTab = a;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Paint(vm);
        var cel = vm.PaintedCel();
        var before = PointsOf(cel);
        a.MarkSaved();
        b.MarkSaved();

        Ctrl(window, Key.T);
        DragTheCorner(window, canvas, 40);
        // Which tab is active while the edit lands is what the dirty marking,
        // a reference sheet's view sync and a symbol's sync all read. An
        // ordinary tab's dirty badge comes from its editor's revision and
        // cannot tell the two orders apart, so this asks the question directly.
        DocumentTab? activeAtApply = null;
        var apply = vm.ConfirmTransform!;
        vm.ConfirmTransform = () => { activeAtApply = vm.ActiveTab; apply(); };
        vm.ActiveTab = b;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        output.WriteLine($"active {vm.TransformActive}, A dirty {a.IsDirty}, B dirty {b.IsDirty}");
        Assert.False(vm.TransformActive);
        Assert.Same(a, activeAtApply);
        Assert.NotEqual(before, PointsOf(cel));
        Assert.True(a.IsDirty, "the document the transform was applied to is not marked edited");
        Assert.False(b.IsDirty, "the tab arrived at was marked edited by a change it never had");
    }

    /// <summary>
    /// Deleting a layer moves the index as a consequence. The session's frames
    /// went with the layer, so it is dropped rather than applied to them.
    /// </summary>
    [AvaloniaFact]
    public void DeletingTheLayerUnderASessionDropsItRatherThanApplying()
    {
        var (window, canvas, vm) = Open();
        Paint(vm);
        vm.AddPaintedLayerCommand.Execute(null);
        Paint(vm);
        var top = vm.PaintLayer();

        Ctrl(window, Key.T);
        DragTheCorner(window, canvas, 40);
        var steps = vm.RecordedStepCount;
        vm.DeleteLayer(top);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        output.WriteLine($"active {vm.TransformActive}, steps {steps} -> {vm.RecordedStepCount}");
        Assert.False(vm.TransformActive);
        Assert.False(canvas.TransformSessionActive);
        Assert.Equal(steps + 1, vm.RecordedStepCount); // the delete, and no phantom transform
    }

    /// <summary>
    /// A Move-tool drag has no gizmo, so leaving mid-drag must not apply the
    /// box the last Ctrl+T session left behind in the gizmo's fields.
    /// </summary>
    [AvaloniaFact]
    public void LeavingAGizmolessSessionDoesNotReplayTheLastBox()
    {
        var (window, canvas, vm) = Open();
        Paint(vm);
        vm.AddFrameCommand.Execute(null);
        vm.CurrentFrameIndex = 0;
        Ctrl(window, Key.T);
        canvas.MirrorTransformGizmo(horizontal: true);
        Press(window, Key.Enter);
        var after = PointsOf(vm.PaintedCel(0));
        var steps = vm.RecordedStepCount;

        Assert.True(vm.BeginTransform(gizmo: false));
        vm.CurrentFrameIndex = 1;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(vm.TransformActive);
        Assert.Equal(steps, vm.RecordedStepCount);
        Assert.Equal(after, PointsOf(vm.PaintedCel(0)));
    }

    /// <summary>
    /// A drag the pointer cannot finish — capture lost, window deactivated —
    /// becomes a step where it stands, so the next tweak is a step of its own.
    /// </summary>
    [AvaloniaFact]
    public void ADragCutOffByCaptureLossIsItsOwnStep()
    {
        var (window, canvas, vm) = Open();
        Paint(vm);
        Ctrl(window, Key.T);
        var src = canvas.TransformQuadResult.Src;
        var (vx, vy) = canvas.DocToView(src[4], src[5]);
        var at = new Point(vx, vy);
        canvas.RaiseEvent(new PointerPressedEventArgs(canvas, Mouse, window, Root(window, canvas, at), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None));
        canvas.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, canvas, Mouse, window,
            Root(window, canvas, at + new Point(40, 40)), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other),
            KeyModifiers.None));
        canvas.CancelPointerGestures(); // no release ever arrives

        canvas.MirrorTransformGizmo(horizontal: true);

        Assert.Equal(2, canvas.TransformUndoDepth);
    }
}
