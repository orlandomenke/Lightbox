using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lightbox.App.Rendering;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// What the pointer says a press would do, over every grabbable thing on the
/// canvas (B241).
/// </summary>
/// <remarks>
/// <para>
/// Against <c>CanvasCursorNow</c> — the decision — rather than against the
/// <c>Cursor</c> property, because building a bitmap cursor needs a render
/// surface a headless run has not got and would land every one of these on
/// <c>PointerCursors</c>' catch-and-fall-back. The rendering half is one switch
/// with nothing in it to get wrong; the choice is where the mistakes live.
/// </para>
/// <para>
/// Angles are asserted in screen degrees mod 180, which is what
/// <see cref="CanvasCursorChoice.ResizeAt"/> normalises to: a double-headed
/// arrow has no front, so 225° and 45° are the same cursor and a test that
/// distinguished them would be pinning the caller's arithmetic rather than the
/// pointer.
/// </para>
/// </remarks>
public class CanvasCursorChoiceTests
{
    private static (Window Window, CanvasControl Canvas) NewRig()
    {
        // A real snapshot, because the view matrix falls back to the identity
        // without one — and every angle here is measured through that matrix,
        // so a rig with no document would pass the rotation test by not
        // rotating anything.
        var vm = new Lightbox.App.ViewModels.MainViewModel(null);
        var canvas = new CanvasControl();
        var window = new Window { Width = 800, Height = 600, Content = canvas };
        vm.SnapshotChanged += s => canvas.UpdateSnapshot(s);
        window.Show();
        vm.PublishSnapshot();
        return (window, canvas);
    }

    /// <summary>The view point over a document point, so a test can aim at a handle.</summary>
    private static Point At(CanvasControl canvas, double x, double y)
    {
        var (vx, vy) = canvas.DocToView(x, y);
        return new Point(vx, vy);
    }

    /// <summary>A resize along a screen direction, to the nearest hundredth of a degree.</summary>
    /// <remarks>
    /// Compared loosely rather than by the record's own equality: every angle
    /// here comes out of a matrix and two trigonometric calls, so an exact
    /// comparison pins the arithmetic rather than the direction. A cursor is
    /// drawn to a whole degree, so a hundredth is already far finer than
    /// anything that can be seen.
    /// </remarks>
    private static void AssertResize(double degrees, CanvasCursorChoice choice)
    {
        Assert.Equal(CanvasCursorKind.Resize, choice.Kind);
        Assert.Equal(degrees, choice.Angle, 2);
    }

    private static CanvasCursorChoice Over(
        CanvasControl canvas, double x, double y, KeyModifiers modifiers = KeyModifiers.None) =>
        canvas.CanvasCursorNow(At(canvas, x, y), modifiers);

    // ---- the transform gizmo: six gestures in one box --------------------------------

    [AvaloniaFact]
    public void ACornerHandleScalesAlongItsOwnDiagonal()
    {
        var (window, canvas) = NewRig();
        canvas.BeginTransformGizmo(100, 100, 300, 300);

        // Top-left of a square box: the diagonal to the bottom-right is 45° on
        // screen, and it is the direction the handle actually drags.
        AssertResize(45, Over(canvas, 100, 100));
        // The opposite corner drags along the same line, so it is the same arrow.
        AssertResize(45, Over(canvas, 300, 300));
        // The other diagonal is the other arrow.
        AssertResize(135, Over(canvas, 300, 100));

        window.Close();
    }

    [AvaloniaFact]
    public void AnEdgeHandleScalesAcrossItselfRatherThanAlongItself()
    {
        var (window, canvas) = NewRig();
        canvas.BeginTransformGizmo(100, 100, 300, 300);

        // The top edge is horizontal and grows the box vertically. Pointing the
        // arrow along the edge is the version that looks right in a screenshot.
        AssertResize(90, Over(canvas, 200, 100));
        // The right edge is vertical and grows the box horizontally.
        AssertResize(0, Over(canvas, 300, 200));

        window.Close();
    }

    [AvaloniaFact]
    public void InsideTheBoxMovesAndOutsideItTurns()
    {
        var (window, canvas) = NewRig();
        canvas.BeginTransformGizmo(100, 100, 300, 300);

        // The pivot starts at the centre and is a place rather than a direction.
        Assert.Equal(CanvasCursorKind.Move, Over(canvas, 200, 200).Kind);
        Assert.Equal(CanvasCursorKind.Move, Over(canvas, 150, 250).Kind);
        Assert.Equal(CanvasCursorKind.Rotate, Over(canvas, 20, 20).Kind);

        window.Close();
    }

    [AvaloniaFact]
    public void ARotatedViewSwingsTheHandleCursorsWithIt()
    {
        var (window, canvas) = NewRig();
        canvas.BeginTransformGizmo(100, 100, 300, 300);
        var upright = Over(canvas, 200, 100);

        canvas.RotateBy(30);
        var turned = Over(canvas, 200, 100);

        // Invariant 5: the view transform never touches the record, so the
        // handle is where it always was and the *screen* direction it drags
        // along is what moved. Measuring in document space would leave the
        // arrow pointing 30° off the thing it describes.
        Assert.Equal(90d, upright.Angle, 3);
        Assert.Equal(120d, turned.Angle, 3);

        window.Close();
    }

    [AvaloniaFact]
    public void APerspectiveCornerIsNotAScale()
    {
        var (window, canvas) = NewRig();
        canvas.BeginTransformGizmo(100, 100, 300, 300);
        canvas.TransformPerspective = true;

        // A free corner goes wherever it is put — there is no axis to point
        // along, which is the whole difference from a scale handle.
        Assert.Equal(CanvasCursorKind.Move, Over(canvas, 100, 100).Kind);

        window.Close();
    }

    [AvaloniaFact]
    public void TheCursorHoldsForTheWholeDragRatherThanFollowingThePointer()
    {
        var (window, canvas) = NewRig();
        canvas.ToolMode = CanvasControl.CanvasToolMode.Transform;
        canvas.BeginTransformGizmo(100, 100, 300, 300);

        window.MouseDown(At(canvas, 100, 100), MouseButton.Left);

        // Mid-drag the pointer is well inside the box, where a hover test would
        // say "move". The gesture in flight is a scale and the pointer must go
        // on saying so — the owner's rule, and the reason the live gestures are
        // asked before the hit test rather than after it.
        Assert.Equal(CanvasCursorKind.Resize, Over(canvas, 210, 205).Kind);

        window.MouseUp(At(canvas, 210, 205), MouseButton.Left);
        window.Close();
    }

    // ---- guides ----------------------------------------------------------------------

    [AvaloniaFact]
    public void AHeightScalesTopResizesItAndItsPostMovesIt()
    {
        var (window, canvas) = NewRig();
        canvas.GuideDragEnabled = true;
        canvas.Guides =
        [
            // Kind 4 is a height scale: eight rungs of 20 document pixels,
            // standing on (200, 300), so its top rung is at y = 140.
            new CanvasControl.GuideLine("h", 4, 200, 300, 20, [], Divisions: 8),
        ];

        // The one guide grab that is not a move, and it wore the move cursor
        // like the rest — so "this character is this tall" was indistinguishable
        // from sliding the chart sideways.
        Assert.Equal(CanvasCursorKind.Resize, Over(canvas, 200, 140).Kind);
        Assert.Equal(CanvasCursorKind.Move, Over(canvas, 200, 240).Kind);

        window.Close();
    }

    [AvaloniaFact]
    public void GuidesSayNothingWhenTheyCannotBePickedUp()
    {
        var (window, canvas) = NewRig();
        canvas.PointerIntent = CanvasCursorKind.Paint;
        canvas.Guides =
        [
            new CanvasControl.GuideLine("h", 4, 200, 300, 20, [], Divisions: 8),
        ];

        // With a brush in hand the rig is scenery: the emphasis is off and so is
        // the promise. A cursor offering a grab the press would refuse is worse
        // than none, because it is the pointer that lied.
        Assert.Equal(CanvasCursorKind.Paint, Over(canvas, 200, 140).Kind);

        window.Close();
    }

    // ---- the camera ------------------------------------------------------------------

    [AvaloniaFact]
    public void TheCameraFramesThreeGripsReadAsThreeDifferentGestures()
    {
        var (window, canvas) = NewRig();
        canvas.CameraFrame =
        [
            new SKPoint(200, 150), new SKPoint(600, 150),
            new SKPoint(600, 450), new SKPoint(200, 450),
        ];

        // The pan grip is the top edge's midpoint; zoom is any corner; the
        // rotate handle is pushed out beyond the pan grip.
        Assert.Equal(CanvasCursorKind.Move, Over(canvas, 400, 150).Kind);
        Assert.Equal(CanvasCursorKind.Resize, Over(canvas, 200, 150).Kind);

        var scale = (float)canvas.ViewScale;
        var handle = CanvasControl.RotateHandle(canvas.CameraFrame, 26f / scale);
        Assert.Equal(CanvasCursorKind.Rotate, Over(canvas, handle.X, handle.Y).Kind);

        window.Close();
    }

    [AvaloniaFact]
    public void ARolledCameraZoomsAlongItsOwnDiagonal()
    {
        var (window, canvas) = NewRig();
        // A square frame rolled 45°: its corner-to-corner diagonal is vertical
        // on screen, so the zoom grip drags up and down rather than at 45°.
        canvas.CameraFrame =
        [
            new SKPoint(400, 100), new SKPoint(600, 300),
            new SKPoint(400, 500), new SKPoint(200, 300),
        ];

        AssertResize(90, Over(canvas, 400, 100));

        window.Close();
    }

    // ---- reference boxes and panning -------------------------------------------------

    [AvaloniaFact]
    public void OnlyASelectedReferenceBoxOffersItsCorners()
    {
        var (window, canvas) = NewRig();
        canvas.ReferenceBoxes =
        [
            new CanvasControl.ReferenceBox(100, 100, 200, 200, 0, 0, Selected: false),
        ];
        var unselected = Over(canvas, 100, 100).Kind;

        canvas.ReferenceBoxes =
        [
            new CanvasControl.ReferenceBox(100, 100, 200, 200, 0, 0, Selected: true),
        ];

        // The same order the press handler uses — an unselected box has no
        // handles, so promising one would be a resize the press reads as a move.
        Assert.NotEqual(CanvasCursorKind.Resize, unselected);
        AssertResize(45, Over(canvas, 100, 100));
        AssertResize(135, Over(canvas, 300, 100));

        window.Close();
    }

    [AvaloniaFact]
    public void DraggingTheCanvasItselfShowsTheHand()
    {
        var (window, canvas) = NewRig();
        canvas.PointerIntent = CanvasCursorKind.Paint;

        window.MouseDown(new Point(400, 300), MouseButton.Middle);

        // Panning is the one gesture where "grab and hold it" is precisely the
        // meaning, and it outranks the brush the artist still has in hand.
        Assert.Equal(CanvasCursorKind.Grab, canvas.CanvasCursorNow(
            new Point(400, 300), KeyModifiers.None).Kind);

        window.MouseUp(new Point(400, 300), MouseButton.Middle);
        window.Close();
    }

    // ---- the tool's own intent, and the held key --------------------------------------

    [AvaloniaFact]
    public void WithNothingGrabbableUnderItThePointerIsTheToolsOwn()
    {
        var (window, canvas) = NewRig();
        canvas.PointerIntent = CanvasCursorKind.Pick;

        Assert.Equal(new CanvasCursorChoice(CanvasCursorKind.Pick), Over(canvas, 400, 300));

        window.Close();
    }

    [AvaloniaFact]
    public void AKeyOrAToolChangeReDecidesTheCursorWithNoPointerEventToHangItOn()
    {
        var (window, canvas) = NewRig();
        canvas.PointerIntent = CanvasCursorKind.Move;
        canvas.BeginTransformGizmo(100, 100, 300, 300);
        window.MouseMove(At(canvas, 100, 100));

        // The hand is still, over a corner handle, and the tool changes under it
        // — Ctrl borrowing the eyedropper, or the toolbar being clicked. Without
        // the re-ask the new intent is written straight to the cursor and the
        // handle's own arrow is wiped, so the pointer promises a brush over a
        // grab the press will still read as a scale.
        canvas.PointerIntent = CanvasCursorKind.Paint;

        Assert.NotEqual(PointerCursors.For(CanvasCursorKind.Paint), canvas.Cursor);
        Assert.Equal(CanvasCursorKind.Resize, canvas.CanvasCursorNow(
            At(canvas, 100, 100), KeyModifiers.None).Kind);

        window.Close();
    }

    [AvaloniaFact]
    public void WithThePointerOffTheCanvasThereIsNothingToReAskAbout()
    {
        var (window, canvas) = NewRig();
        canvas.BeginTransformGizmo(100, 100, 300, 300);
        window.MouseMove(At(canvas, 100, 100));

        // The pointer leaves from a corner handle — the case that matters,
        // because the answer it left behind was not the tool's. Re-asking about
        // a place the pointer is no longer at would leave a scale arrow on
        // screen while the hand is somewhere else entirely.
        canvas.RaiseEvent(new PointerEventArgs(
            InputElement.PointerExitedEvent, canvas, new Pointer(0, PointerType.Mouse, true),
            null, default, 0, new PointerPointProperties(), KeyModifiers.None));
        canvas.PointerIntent = CanvasCursorKind.Pick;

        Assert.Equal(PointerCursors.For(CanvasCursorKind.Pick), canvas.Cursor);

        window.Close();
    }
}
