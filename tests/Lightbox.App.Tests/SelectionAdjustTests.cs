using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Grow and Shrink, on every side.
/// </summary>
/// <remarks>
/// Both bugs here were one-sided, which is the hard kind to see: a selection
/// that shrinks on three sides out of four still shrinks, and you blame your
/// own eyes for a while.
/// </remarks>
[Collection("BrushState")]
public sealed class SelectionAdjustTests : BrushStateIsolated
{
    private static MainViewModel Vm()
    {
        var vm = new MainViewModel(null);
        vm.SelectionAdjustPx = 10;
        return vm;
    }

    private readonly record struct Edges(double Left, double Right, double Top, double Bottom);

    private static Edges Box(MainViewModel vm)
    {
        var pts = vm.SelectionContoursForTests.SelectMany(c => c).ToList();
        Assert.NotEmpty(pts);
        return new Edges(
            pts.Min(p => p.X), pts.Max(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.Y));
    }

    private static List<StrokePoint> Ellipse(double cx, double cy, double rx, double ry)
    {
        var pts = new List<StrokePoint>();
        for (var i = 0; i < 48; i++)
        {
            var t = i / 48.0 * Math.PI * 2;
            pts.Add(new StrokePoint(cx + rx * Math.Cos(t), cy + ry * Math.Sin(t), 1));
        }
        return pts;
    }

    private static void MovedIn(double expected, double before, double after, string side) =>
        Assert.True(
            Math.Abs((after - before) - expected) <= 1.5,
            $"{side} moved {after - before:0.#}, expected about {expected}");

    // ---- B20: a selection touching the canvas edge --------------------------------

    [AvaloniaFact]
    public void ShrinkingTheWholeCanvasPullsInFromAllFourEdges()
    {
        // It used to do nothing at all. Erosion is a dilation of the
        // complement, and the complement of "everything" is empty — so with
        // the world outside the bitmap treated as unselected too, there was
        // nothing anywhere to grow inward from.
        var vm = Vm();
        vm.SelectAllCommand.Execute(null);
        var before = Box(vm);

        vm.ShrinkSelectionCommand.Execute(null);
        var after = Box(vm);

        MovedIn(10, before.Left, after.Left, "left");
        MovedIn(-10, before.Right, after.Right, "right");
        MovedIn(10, before.Top, after.Top, "top");
        MovedIn(-10, before.Bottom, after.Bottom, "bottom");
    }

    [AvaloniaFact]
    public void AnEdgeTouchingSelectionShrinksOnTheEdgeItTouches()
    {
        var vm = Vm();
        vm.ApplySelectionShape(
        [
            new(0, 100, 1), new(300, 100, 1), new(300, 260, 1), new(0, 260, 1),
        ], false, false);
        var before = Box(vm);

        vm.ShrinkSelectionCommand.Execute(null);
        var after = Box(vm);

        MovedIn(10, before.Left, after.Left, "left (the one on the canvas edge)");
        MovedIn(-10, before.Right, after.Right, "right");
    }

    // ---- B21: the round trip through contours --------------------------------------

    [AvaloniaFact]
    public void ShrinkAndGrowLeaveTheSelectionWhereItWas()
    {
        // The contour tracer walks pixel centres, so the polygon it returns
        // bisects the boundary ring instead of enclosing it. Filling that back
        // dropped the top and left rings every time, and five shrink-and-grow
        // cycles walked a circle ten pixels into its own corner.
        var vm = Vm();
        vm.ApplySelectionShape(Ellipse(300, 250, 120, 100), false, false);
        var before = Box(vm);

        for (var i = 0; i < 5; i++)
        {
            vm.ShrinkSelectionCommand.Execute(null);
            vm.GrowSelectionCommand.Execute(null);
        }

        Assert.Equal(before, Box(vm));
    }

    [AvaloniaFact]
    public void ACircleShrinksByTheSameAmountOnEverySide()
    {
        var vm = Vm();
        vm.ApplySelectionShape(Ellipse(300, 250, 120, 100), false, false);
        var before = Box(vm);

        vm.ShrinkSelectionCommand.Execute(null);
        var after = Box(vm);

        MovedIn(10, before.Left, after.Left, "left");
        MovedIn(-10, before.Right, after.Right, "right");
        MovedIn(10, before.Top, after.Top, "top");
        MovedIn(-10, before.Bottom, after.Bottom, "bottom");
    }

    [AvaloniaFact]
    public void GrowingPushesOutOnEverySideToo()
    {
        var vm = Vm();
        vm.ApplySelectionShape(Ellipse(400, 270, 120, 100), false, false);
        var before = Box(vm);

        vm.GrowSelectionCommand.Execute(null);
        var after = Box(vm);

        MovedIn(-10, before.Left, after.Left, "left");
        MovedIn(10, before.Right, after.Right, "right");
        MovedIn(-10, before.Top, after.Top, "top");
        MovedIn(10, before.Bottom, after.Bottom, "bottom");
    }

    [AvaloniaFact]
    public void ShrinkingPastNothingLeavesNoSelectionRatherThanAnInsideOutOne()
    {
        var vm = Vm();
        vm.ApplySelectionShape(Ellipse(300, 250, 12, 12), false, false);
        vm.SelectionAdjustPx = 40;

        vm.ShrinkSelectionCommand.Execute(null);

        Assert.False(vm.HasSelection);
    }
}
