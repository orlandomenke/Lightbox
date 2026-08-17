using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Ctrl on the artwork inside a marquee takes hold of it (Q104).
/// </summary>
/// <remarks>
/// <para>
/// The interesting half is not the move — that is the ordinary transform
/// session, guarded where it lives — but the <em>displacement</em>: Ctrl is the
/// held eyedropper everywhere else on this canvas, and these say exactly where
/// the boundary is, because a modifier that quietly means two things in two
/// places is only defensible if the boundary is sharp.
/// </para>
/// <para>
/// Drivable with no window, like <c>StrokeSelectionTests</c>, and for its
/// reason: the view model owns both refusals, so the canvas asks and acts on the
/// answer rather than deciding in an event handler nothing can reach.
/// </para>
/// </remarks>
public class SelectionCtrlMoveTests
{
    private static Stroke Line(double x0, double y0, double x1, double y1) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#000000",
        Points = [new StrokePoint(x0, y0, 1), new StrokePoint(x1, y1, 1)],
        Brush = new BrushSettings { Size = 10, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
    };

    private static MainViewModel WithStrokes(params Stroke[] strokes)
    {
        var vm = new MainViewModel(null);
        var frame = vm.Doc.Scene.Layers[^1].Cels[0].Frame;
        Assert.NotNull(frame);
        frame.Strokes.AddRange(strokes);
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.Count - 1;
        return vm;
    }

    /// <summary>A rectangular marquee, through the same call the canvas makes.</summary>
    private static void Marquee(MainViewModel vm, double x0, double y0, double x1, double y1) =>
        vm.ApplySelectionShape(
            [
                new StrokePoint(x0, y0, 1), new StrokePoint(x1, y0, 1),
                new StrokePoint(x1, y1, 1), new StrokePoint(x0, y1, 1),
            ],
            add: false, subtract: false);

    private static Stroke StrokeOf(MainViewModel vm, string id) =>
        ((Frame)vm.PaintLayer().Cels[0].Frame!).Strokes.First(s => s.Id == id);

    // ---- where the boundary is -------------------------------------------------------

    /// <summary>Inside the marquee, Ctrl is a move rather than an eyedropper.</summary>
    [AvaloniaFact]
    public void CtrlInsideAMarqueeMeansMoveRatherThanPick()
    {
        var target = new CanvasTarget(SelectionToMove: true, OutsideSelection: false);

        Assert.Equal(
            ToolId.Move, CanvasCursor.Effective(ToolId.Brush, KeyModifiers.Control, target));
        Assert.Equal(
            CanvasCursorKind.Move, CanvasCursor.For(ToolId.Brush, target, KeyModifiers.Control));
    }

    /// <summary>
    /// Outside it, Ctrl still fetches a colour — which is nearly all of the
    /// canvas, and the whole reason the displacement is acceptable.
    /// </summary>
    [AvaloniaFact]
    public void CtrlOutsideTheMarqueeStillPicksAColour()
    {
        var target = new CanvasTarget(SelectionToMove: true, OutsideSelection: true);

        Assert.Equal(
            ToolId.Picker, CanvasCursor.Effective(ToolId.Brush, KeyModifiers.Control, target));
    }

    /// <summary>
    /// With no selection at all nothing changes, which is the state almost every
    /// document is in almost all of the time.
    /// </summary>
    /// <remarks>
    /// Asserted against <c>default</c> on purpose: a record struct's
    /// primary-constructor defaults do not run for a zero-initialised value, so
    /// this is the case where a field named the wrong way round would silently
    /// hand every Ctrl on the canvas to the move.
    /// </remarks>
    [AvaloniaFact]
    public void WithNoSelectionCtrlIsTheEyedropperExactlyAsBefore()
    {
        Assert.Equal(
            ToolId.Picker, CanvasCursor.Effective(ToolId.Brush, KeyModifiers.Control, default));
        Assert.Equal(
            ToolId.Picker,
            CanvasCursor.Effective(ToolId.Brush, KeyModifiers.Control, new CanvasTarget()));
    }

    /// <summary>Without Ctrl a marquee changes nothing about the tool in hand.</summary>
    [AvaloniaFact]
    public void AMarqueeAloneDoesNotBorrowTheTool()
    {
        var target = new CanvasTarget(SelectionToMove: true);

        Assert.Equal(ToolId.Brush, CanvasCursor.Effective(ToolId.Brush, KeyModifiers.None, target));
        Assert.Equal(ToolId.Brush, CanvasCursor.Effective(ToolId.Brush, KeyModifiers.Shift, target));
        Assert.Equal(ToolId.Brush, CanvasCursor.Effective(ToolId.Brush, KeyModifiers.Alt, target));
    }

    /// <summary>
    /// The move outranks a refusal the picker would also have lifted, and for a
    /// different reason: dragging a selection about does not put paint anywhere.
    /// </summary>
    [AvaloniaFact]
    public void MovingIsNotRefusedOnALockedLayer()
    {
        var target = new CanvasTarget(LayerLocked: true, SelectionToMove: true);

        Assert.Equal(
            CanvasCursorKind.Move, CanvasCursor.For(ToolId.Brush, target, KeyModifiers.Control));
        Assert.Null(CanvasCursor.Refusal(ToolId.Brush, target, KeyModifiers.Control));
    }

    // ---- the pointer says so before the press ----------------------------------------

    /// <summary>
    /// The cursor changes under a still hand, which is what makes the modifier
    /// discoverable rather than a trap.
    /// </summary>
    [AvaloniaFact]
    public void ThePointerSaysMoveWhileCtrlIsHeldOverTheSelection()
    {
        var vm = WithStrokes(Line(100, 100, 300, 100));
        Marquee(vm, 50, 50, 350, 150);

        vm.UpdatePointerContext(200, 100, KeyModifiers.None, 1);
        Assert.Equal(CanvasCursorKind.Paint, vm.PointerIntent);

        vm.UpdatePointerContext(200, 100, KeyModifiers.Control, 1);
        Assert.Equal(CanvasCursorKind.Move, vm.PointerIntent);

        // A hand's width to the right, outside the marquee, and it is the
        // eyedropper again — the same keys, a different place.
        vm.UpdatePointerContext(500, 400, KeyModifiers.Control, 1);
        Assert.Equal(CanvasCursorKind.Pick, vm.PointerIntent);
    }

    /// <summary>
    /// And with no marquee up, holding Ctrl anywhere is the eyedropper — the
    /// view-model half of the same fact, which is where the flag is actually
    /// set from.
    /// </summary>
    [AvaloniaFact]
    public void WithNoMarqueeTheHeldCtrlIsStillTheEyedropper()
    {
        var vm = WithStrokes(Line(100, 100, 300, 100));

        vm.UpdatePointerContext(200, 100, KeyModifiers.Control, 1);
        Assert.Equal(CanvasCursorKind.Pick, vm.PointerIntent);
    }

    // ---- the gesture -----------------------------------------------------------------

    /// <summary>A press inside the marquee opens a session; one outside does not.</summary>
    [AvaloniaFact]
    public void OnlyAPressInsideTheMarqueeTakesHold()
    {
        var vm = WithStrokes(Line(100, 100, 300, 100));
        Marquee(vm, 50, 50, 350, 150);

        Assert.False(vm.BeginSelectionMove(500, 400));
        Assert.False(vm.TransformActive);

        Assert.True(vm.BeginSelectionMove(200, 100));
        Assert.True(vm.TransformActive);
        Assert.Equal("the selection", vm.TransformSubject);

        vm.CancelMove();
    }

    /// <summary>With no marquee up there is nothing to take hold of.</summary>
    [AvaloniaFact]
    public void WithNoMarqueeThereIsNothingToMove()
    {
        var vm = WithStrokes(Line(100, 100, 300, 100));

        Assert.False(vm.BeginSelectionMove(200, 100));
        Assert.False(vm.TransformActive);
    }

    /// <summary>
    /// The drag moves what the marquee holds, leaves what it does not, and is
    /// one undo step.
    /// </summary>
    /// <remarks>
    /// The second line is what stops this passing vacuously: a session with no
    /// filter would move the whole drawing and every other assertion here would
    /// still hold.
    /// </remarks>
    [AvaloniaFact]
    public void TheDragMovesWhatIsInsideAndLeavesWhatIsNot()
    {
        var inside = Line(100, 100, 300, 100);
        var outside = Line(100, 500, 300, 500);
        var vm = WithStrokes(inside, outside);
        Marquee(vm, 50, 50, 350, 150);
        var steps = vm.RecordedStepCount;

        Assert.True(vm.BeginSelectionMove(200, 100));
        vm.UpdateMove(240, 130, axisLock: false);
        vm.EndMove();

        Assert.Equal(140, StrokeOf(vm, inside.Id).Points[0].X, 1);
        Assert.Equal(130, StrokeOf(vm, inside.Id).Points[0].Y, 1);
        Assert.Equal(100, StrokeOf(vm, outside.Id).Points[0].X, 1);
        Assert.Equal(500, StrokeOf(vm, outside.Id).Points[0].Y, 1);
        Assert.Equal(steps + 1, vm.RecordedStepCount);

        vm.UndoCommand.Execute(null);

        // Re-read rather than reusing the reference: a transform commits through
        // a snapshot step, so undo replaces the whole tree and the captured
        // object is orphaned (B103).
        Assert.Equal(100, StrokeOf(vm, inside.Id).Points[0].X, 1);
    }

    /// <summary>Shift holds the move to one axis, as it does on every other drag here.</summary>
    [AvaloniaFact]
    public void ShiftHoldsTheMoveToOneAxis()
    {
        var line = Line(100, 100, 300, 100);
        var vm = WithStrokes(line);
        Marquee(vm, 50, 50, 350, 150);

        Assert.True(vm.BeginSelectionMove(200, 100));
        vm.UpdateMove(260, 110, axisLock: true);
        vm.EndMove();

        Assert.Equal(160, StrokeOf(vm, line.Id).Points[0].X, 1);
        Assert.Equal(100, StrokeOf(vm, line.Id).Points[0].Y, 1);
    }

    // ---- the press, on the canvas ----------------------------------------------------

    /// <summary>
    /// The press asks about the selection before it fetches a colour, so a
    /// brush in hand does not beat the marquee under the pointer.
    /// </summary>
    /// <remarks>
    /// The one thing the view model cannot guard: both branches live in the
    /// canvas's press handler and the whole feature is which of them is written
    /// first. Driven through a real pointer event because that is the only
    /// thing the order is visible to.
    /// </remarks>
    [AvaloniaFact]
    public void APressAsksAboutTheSelectionBeforeItFetchesAColour()
    {
        var canvas = new CanvasControl { ToolMode = CanvasControl.CanvasToolMode.Paint };
        var window = new Avalonia.Controls.Window { Width = 800, Height = 600, Content = canvas };
        var vm = new MainViewModel(null);
        vm.SnapshotChanged += s => canvas.UpdateSnapshot(s);
        window.Show();
        vm.PublishSnapshot();

        var asked = 0;
        var picked = 0;
        canvas.SetSelectionMoveEntry((_, _) => { asked++; return true; });
        canvas.PickClicked += (_, _) => picked++;

        window.MouseDown(
            new Avalonia.Point(400, 300), MouseButton.Left, RawInputModifiers.Control);

        Assert.Equal(1, asked);
        Assert.Equal(0, picked);

        window.MouseUp(new Avalonia.Point(400, 300), MouseButton.Left, RawInputModifiers.Control);
        window.Close();
    }
}
