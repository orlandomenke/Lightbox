using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Rulers, and the gestures that make and move a guide with them.
/// </summary>
/// <remarks>
/// The point of the design is that nothing floats over the artwork: a guide is
/// pulled out of a ruler, moved by grabbing it, and thrown away by putting it
/// back. These tests are about that gesture surviving, and about the rulers
/// costing nothing when they are off.
/// </remarks>
[Collection("BrushState")]
public class RulerAndGuideEditTests : BrushStateIsolated
{
    private static (Views.MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new Views.MainWindow();
        window.Show();
        Pump(window);
        return (window, (MainViewModel)window.DataContext!);
    }

    /// <summary>
    /// Run queued work and settle the layout.
    /// </summary>
    /// <remarks>
    /// The layout pass matters here more than usual: showing the rulers insets
    /// the canvas, and every mapping in this file is computed from bounds. A
    /// strip that has not been arranged has a height of zero, which makes
    /// "released back on the ruler" impossible to be true.
    /// </remarks>
    private static void Pump(Layoutable? settle = null)
    {
        Dispatcher.UIThread.RunJobs();
        settle?.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static RulerStrip Strip(Views.MainWindow window, Orientation orientation) =>
        window.GetVisualDescendants().OfType<RulerStrip>().First(s => s.Orientation == orientation);

    private static Rendering.CanvasControl CanvasOf(Views.MainWindow window) =>
        window.GetVisualDescendants().OfType<Rendering.CanvasControl>().First();

    // ---- the tick scale ----------------------------------------------------------

    [Fact]
    public void TicksStayOnRoundNumbersAtEveryZoom()
    {
        // The 1-2-5 sequence. Numbers on a ruler are the only reason to have
        // numbers on a ruler, so they have to stay readable at any zoom.
        foreach (var scale in (double[])[0.05, 0.25, 1, 4, 32])
        {
            var step = RulerStrip.TickStep(scale);
            var decade = Math.Pow(10, Math.Floor(Math.Log10(step)));
            var mantissa = step / decade;
            Assert.True(
                Math.Abs(mantissa - 1) < 1e-6
                || Math.Abs(mantissa - 2) < 1e-6
                || Math.Abs(mantissa - 5) < 1e-6,
                $"scale {scale} gave a step of {step}");
        }
    }

    [Fact]
    public void ZoomingInGivesAFinerRuler()
    {
        Assert.True(RulerStrip.TickStep(8) < RulerStrip.TickStep(1));
        Assert.True(RulerStrip.TickStep(1) < RulerStrip.TickStep(0.1));
    }

    [Fact]
    public void AStripMapsBothWaysConsistently()
    {
        var strip = new RulerStrip { Orientation = Orientation.Horizontal, Mapping = (40, 2) };

        Assert.Equal(140, strip.StripAt(50), 6);
        Assert.Equal(50, strip.DocAt(new Point(140, 3)), 6);
    }

    [Fact]
    public void AMirroredViewGivesABackwardsRulerRatherThanNone()
    {
        // A negative scale is right, not a degenerate case: the ruler counts
        // the way the drawing reads.
        var strip = new RulerStrip { Orientation = Orientation.Horizontal, Mapping = (300, -2) };

        Assert.Equal(200, strip.StripAt(50), 6);
        Assert.Equal(50, strip.DocAt(new Point(200, 0)), 6);
    }

    // ---- absence ------------------------------------------------------------------

    [AvaloniaFact]
    public void TheRulersAreAbsentUntilAskedFor()
    {
        // Optional means absent, not disabled — and while they are off the
        // canvas keeps the whole of its cell.
        var (window, vm) = Open();

        Assert.False(vm.Workspace.RulersVisible);
        Assert.False(Strip(window, Orientation.Horizontal).IsVisible);
        Assert.Equal(default, CanvasOf(window).Margin);
    }

    [AvaloniaFact]
    public void TurningThemOnInsetsTheCanvasAndBackOffReturnsIt()
    {
        var (window, vm) = Open();

        vm.Workspace.RulersVisible = true;
        Pump(window);

        Assert.True(Strip(window, Orientation.Vertical).IsVisible);
        Assert.Equal(RulerStrip.Thickness, CanvasOf(window).Margin.Left, 3);
        Assert.Equal(RulerStrip.Thickness, CanvasOf(window).Margin.Top, 3);
        // The inset and the strips have to be the same number, or the strip's
        // own coordinates stop lining up with the canvas along its axis and
        // every reading is quietly off by the difference.
        Assert.Equal(RulerStrip.Thickness, Strip(window, Orientation.Horizontal).Bounds.Height, 3);
        Assert.Equal(RulerStrip.Thickness, Strip(window, Orientation.Vertical).Bounds.Width, 3);

        vm.Workspace.RulersVisible = false;
        Pump(window);

        Assert.Equal(default, CanvasOf(window).Margin);
    }

    // ---- pulling a guide out of a ruler ------------------------------------------

    [AvaloniaFact]
    public void DraggingOutOfTheTopRulerLeavesAHorizontalGuide()
    {
        // Photoshop's gesture, and the only way to place a guide by eye.
        var (window, vm) = Open();
        vm.Workspace.RulersVisible = true;
        Pump(window);

        var strip = Strip(window, Orientation.Horizontal);
        Pull(window, strip, new Point(120, 9), new Point(140, 200));

        var guide = Assert.Single(vm.Guides);
        Assert.Equal(GuideKind.Line, guide.Kind);
        Assert.Equal(0, guide.Angle, 3);
    }

    [AvaloniaFact]
    public void DraggingOutOfTheLeftRulerLeavesAVerticalGuide()
    {
        var (window, vm) = Open();
        vm.Workspace.RulersVisible = true;
        Pump(window);

        var strip = Strip(window, Orientation.Vertical);
        Pull(window, strip, new Point(9, 120), new Point(200, 140));

        Assert.Equal(90, Assert.Single(vm.Guides).Angle, 3);
    }

    [AvaloniaFact]
    public void AGuideLandsWhereThePointerWasOnTheOtherAxis()
    {
        // The axis that matters is the one across the ruler: a horizontal
        // guide's place is the pointer's height, not its distance along the
        // top edge. Getting this the wrong way round is the obvious bug and
        // would still look plausible on screen.
        var (window, vm) = Open();
        vm.Workspace.RulersVisible = true;
        Pump(window);

        var strip = Strip(window, Orientation.Horizontal);
        var canvas = CanvasOf(window);
        var drop = new Point(140, 200);
        Pull(window, strip, new Point(120, 9), drop);

        var (_, expected) = canvas.ViewToDoc(strip.TranslatePoint(drop, canvas)!.Value);
        Assert.Equal(expected, Assert.Single(vm.Guides).Y, 3);
    }

    [AvaloniaFact]
    public void LettingGoBackOnTheRulerThrowsTheGuideAway()
    {
        // The undo for a drag you did not mean to start, and the way a guide
        // is deleted in every application that has rulers.
        var (window, vm) = Open();
        vm.Workspace.RulersVisible = true;
        Pump(window);

        var strip = Strip(window, Orientation.Horizontal);
        strip.RaiseEvent(Press(window, strip, new Point(120, 9)));
        strip.RaiseEvent(Move(window, strip, new Point(130, 180)));
        strip.RaiseEvent(Release(window, strip, new Point(130, 8)));
        Pump(window);

        Assert.False(vm.HasGuides);
        Assert.Null(CanvasOf(window).DraftGuide);
    }

    [AvaloniaFact]
    public void TheDraftIsDrawnWhileTheGuideIsBeingPlaced()
    {
        // A guide you cannot see until you let go is one you place twice.
        var (window, vm) = Open();
        vm.Workspace.RulersVisible = true;
        Pump(window);

        var strip = Strip(window, Orientation.Horizontal);
        var canvas = CanvasOf(window);
        strip.RaiseEvent(Press(window, strip, new Point(120, 9)));
        strip.RaiseEvent(Move(window, strip, new Point(130, 180)));

        Assert.NotNull(canvas.DraftGuide);
        Assert.Equal([0d], canvas.DraftGuide!.Value.Angles);

        strip.RaiseEvent(Release(window, strip, new Point(130, 180)));
        Assert.Null(canvas.DraftGuide);
    }

    // ---- moving a guide on the canvas ---------------------------------------------

    [AvaloniaFact]
    public void AGuideIsOnlyGrabbableWithTheMoveTool()
    {
        // Grabbing a guide and drawing along one are the same gesture in the
        // same place, so something has to say which was meant. The rulers used
        // to; the Move tool does now, and it works with the rulers down.
        var (window, vm) = Open();
        var canvas = CanvasOf(window);
        vm.AddGuide(GuideKind.Line, 0, 100);
        Pump(window);

        Assert.False(canvas.GuideDragEnabled);

        vm.ActiveTool = ToolId.Move;
        Pump(window);
        Assert.True(canvas.GuideDragEnabled);

        // And the rulers no longer have anything to do with it.
        vm.Workspace.RulersVisible = true;
        Pump(window);
        Assert.True(canvas.GuideDragEnabled);
        vm.ActiveTool = ToolId.Brush;
        Pump(window);
        Assert.False(canvas.GuideDragEnabled);
    }

    [AvaloniaFact]
    public void LockingThemStopsTheGrabWithoutHidingAnything()
    {
        var (window, vm) = Open();
        var canvas = CanvasOf(window);
        vm.AddGuide(GuideKind.Line, 0, 100);
        vm.ActiveTool = ToolId.Move;
        Pump(window);

        vm.Workspace.GuidesLocked = true;
        Pump(window);

        Assert.False(canvas.GuideDragEnabled);
        Assert.NotNull(canvas.Guides);
    }

    [AvaloniaFact]
    public void HidingGuidesTakesThemOffTheCanvasAndOutOfReach()
    {
        var (window, vm) = Open();
        var canvas = CanvasOf(window);
        vm.AddGuide(GuideKind.Line, 0, 100);
        vm.ActiveTool = ToolId.Move;
        Pump(window);

        vm.Workspace.GuidesVisible = false;
        Pump(window);

        Assert.Null(canvas.Guides);
        Assert.False(canvas.GuideDragEnabled);
    }

    [AvaloniaFact]
    public void AHiddenGuideStillConstrainsTheStroke()
    {
        // Hiding the rig to look at the drawing under it is something an
        // artist does constantly; having the next line quietly stop obeying it
        // is a very confusing way to lose a stroke.
        var (_, vm) = Open();
        vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 20);
        vm.Workspace.GuidesVisible = false;

        vm.BeginStroke(23, 17, 1);
        vm.EndStroke();

        var stroke = ((PaintedFrame)vm.PaintLayer().Cels[0].Frame!).Strokes[^1];
        Assert.Equal(20, stroke.Points[0].X, 1);
    }

    [AvaloniaFact]
    public void AGuideIsMovedByGrabbingItOnTheCanvas()
    {
        // The whole design in one gesture: you pick the line up where it is,
        // and there is nothing floating over the drawing to click instead.
        var (window, vm) = Open();
        vm.ActiveTool = ToolId.Move;
        var guide = vm.AddGuide(GuideKind.Line, 0, 200, angle: 0);
        Pump(window);

        var canvas = CanvasOf(window);
        var (_, top) = canvas.DocToView(0, 200);
        var (_, lower) = canvas.DocToView(0, 260);
        canvas.RaiseEvent(Press(window, canvas, new Point(300, top)));
        canvas.RaiseEvent(Move(window, canvas, new Point(300, lower)));
        canvas.RaiseEvent(Release(window, canvas, new Point(300, lower)));
        Pump(window);

        Assert.Equal(260, guide.Y, 1);
        // And the whole of it is one step, not one per pointer event.
        vm.UndoCommand.Execute(null);
        Assert.Equal(200, guide.Y, 1);
    }

    [AvaloniaFact]
    public void AGrabMissesWithADrawingToolInHand()
    {
        // Which is what leaves the same gesture free to draw along the guide.
        var (window, vm) = Open();
        var guide = vm.AddGuide(GuideKind.Line, 0, 200, angle: 0);
        Pump(window);

        var canvas = CanvasOf(window);
        var (_, top) = canvas.DocToView(0, 200);
        var (_, lower) = canvas.DocToView(0, 260);
        canvas.RaiseEvent(Press(window, canvas, new Point(300, top)));
        canvas.RaiseEvent(Move(window, canvas, new Point(300, lower)));
        canvas.RaiseEvent(Release(window, canvas, new Point(300, lower)));
        Pump(window);

        Assert.Equal(200, guide.Y, 1);
    }

    [AvaloniaFact]
    public void AWholeDragOfAGuideIsOneUndoStep()
    {
        // Fifty pointer events must not become fifty undo steps, or undoing a
        // drag becomes a job rather than a keystroke.
        var (_, vm) = Open();
        var guide = vm.AddGuide(GuideKind.Line, 0, 100);

        vm.DragGuide(guide, 0, 5);
        vm.DragGuide(guide, 0, 5);
        vm.DragGuide(guide, 0, 5);
        vm.EndGuideDrag(guide);
        Assert.Equal(115, guide.Y, 3);

        vm.UndoCommand.Execute(null);

        Assert.Equal(100, guide.Y, 3);
    }

    [AvaloniaFact]
    public void ALockedGuideDoesNotBudge()
    {
        var (_, vm) = Open();
        var guide = vm.AddGuide(GuideKind.Line, 0, 100);
        guide.Locked = true;

        vm.DragGuide(guide, 0, 40);
        vm.EndGuideDrag(guide);

        Assert.Equal(100, guide.Y, 3);
    }

    // ---- what the rulers show ------------------------------------------------------

    [AvaloniaFact]
    public void EachStraightGuideIsMarkedOnTheRulerItCrosses()
    {
        // A horizontal guide crosses the LEFT ruler. Marking it on the top one
        // would look fine on a square canvas and be wrong everywhere else.
        var (window, vm) = Open();
        vm.Workspace.RulersVisible = true;
        vm.AddGuide(GuideKind.Line, 10, 120);              // horizontal
        vm.AddGuide(GuideKind.Line, 250, 10, angle: 90);   // vertical
        Pump(window);

        Assert.Equal([120d], Strip(window, Orientation.Vertical).Marks);
        Assert.Equal([250d], Strip(window, Orientation.Horizontal).Marks);
    }

    [AvaloniaFact]
    public void AGridIsNotMarkedOnTheRulers()
    {
        // It crosses the top ruler everywhere; marking it is a solid bar where
        // the marks have to stay readable.
        var (window, vm) = Open();
        vm.Workspace.RulersVisible = true;
        vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 20);
        Pump(window);

        Assert.Empty(Strip(window, Orientation.Horizontal).Marks);
    }

    [AvaloniaFact]
    public void TheRulersTrackThePointerOverTheCanvas()
    {
        // Most of what a ruler is for: reading a tick to work out where you
        // are is slower than glancing at a line that is already there.
        var (window, vm) = Open();
        vm.Workspace.RulersVisible = true;
        Pump(window);

        var canvas = CanvasOf(window);
        canvas.RaiseEvent(Move(window, canvas, new Point(200, 150)));
        Pump(window);

        var (x, y) = canvas.ViewToDoc(new Point(200, 150));
        Assert.Equal(x, Strip(window, Orientation.Horizontal).Tracking!.Value, 3);
        Assert.Equal(y, Strip(window, Orientation.Vertical).Tracking!.Value, 3);
    }

    // ---- grid configuration -----------------------------------------------------------

    [AvaloniaFact]
    public void TheConfigureWindowListsTheGridsOnTheDocument()
    {
        var (_, vm) = Open();
        vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 24);
        vm.AddGuide(GuideKind.Line, 0, 100);   // not a grid, not listed

        Assert.Equal(24, Assert.Single(vm.GridGuides).Spacing, 3);
    }

    [AvaloniaFact]
    public void ChangingTheDefaultPitchDoesNotTouchAGridAlreadyPlaced()
    {
        // The point of keeping the preference and the guide apart: a setting
        // must never reach back into finished work.
        var (_, vm) = Open();
        var grid = vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 24);

        vm.GridSpacing = 64;

        Assert.Equal(24, grid.Spacing, 3);
    }

    [AvaloniaFact]
    public void EditingAPlacedGridIsUndoable()
    {
        var (_, vm) = Open();
        var grid = vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 24);

        vm.SetGridSpacing(grid, 40);
        Assert.Equal(40, grid.Spacing, 3);

        vm.UndoCommand.Execute(null);

        Assert.Equal(24, grid.Spacing, 3);
    }

    [AvaloniaFact]
    public void TurningAGridsSnappingOffLeavesTheStrokeAlone()
    {
        var (_, vm) = Open();
        var grid = vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 20);

        vm.SetGuideFlags(grid, visible: true, snaps: false);

        vm.BeginStroke(23, 17, 1);
        vm.EndStroke();
        var stroke = ((PaintedFrame)vm.PaintLayer().Cels[0].Frame!).Strokes[^1];
        Assert.Equal(23, stroke.Points[0].X, 1);
    }

    // ---- input plumbing -------------------------------------------------------------

    /// <remarks>
    /// Every position here is measured against the window, not the control the
    /// event is raised on: a synthetic <c>PointerEventArgs</c> carries its
    /// point in the visual root's frame, exactly as a real one does, and
    /// handing it a control-local point silently shifts every reading by that
    /// control's offset. The tests translate on the way in so each one can
    /// still say where it means in the coordinates of the thing it is poking.
    /// </remarks>
    private static void Pull(Window window, RulerStrip strip, Point from, Point to)
    {
        strip.RaiseEvent(Press(window, strip, from));
        strip.RaiseEvent(Move(window, strip, to));
        strip.RaiseEvent(Release(window, strip, to));
        Pump(window);
    }

    private static readonly Pointer TestPointer = new(1, PointerType.Mouse, true);

    private static Point Root(Window window, Visual target, Point local) =>
        target.TranslatePoint(local, window) ?? local;

    private static PointerPressedEventArgs Press(Window window, Control target, Point at) =>
        new(target, TestPointer, window, Root(window, target, at), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None);

    private static PointerEventArgs Move(Window window, Control target, Point at) =>
        new(InputElement.PointerMovedEvent, target, TestPointer, window, Root(window, target, at), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other),
            KeyModifiers.None);

    private static PointerReleasedEventArgs Release(Window window, Control target, Point at) =>
        new(target, TestPointer, window, Root(window, target, at), 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None, MouseButton.Left);
}
