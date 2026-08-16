using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The marching ants follow the pixels they outline. During a transform drag
/// the moving pixels composite through the session's preview matrix, and the
/// ants must ride the same matrix — an outline frozen at the pre-drag
/// position while the drawing moves under it reads as the transform not
/// working, which is exactly how it was reported.
/// </summary>
public class SelectionAntsTests(ITestOutputHelper output)
{
    private static List<StrokePoint> Square(double x, double y, double side) =>
    [
        new(x, y, 1), new(x + side, y, 1), new(x + side, y + side, 1), new(x, y + side, 1),
    ];

    /// <summary>Regression: the ants were frozen mid-drag and jumped on release.</summary>
    [Fact]
    public void TheAntsRideThePreviewMatrixAndTheDragShapeDoesNot()
    {
        using var basePath = SelectionAnts.BasePath([Square(10, 10, 20)]);
        Assert.NotNull(basePath);

        var preview = SKMatrix.CreateTranslation(100, 50);
        var drag = Square(500, 500, 8);
        using var frame = SelectionAnts.FramePath(basePath, preview, drag);
        Assert.NotNull(frame);

        var bounds = frame!.Bounds;
        output.WriteLine($"frame bounds {bounds}, base bounds {basePath!.Bounds}");

        // The committed contour moved with the preview…
        Assert.Equal(110, bounds.Left, 1);
        Assert.Equal(60, bounds.Top, 1);
        // …the drag shape did not (it is the artist's hand, not the content)…
        Assert.Equal(508, bounds.Right, 1);
        Assert.Equal(508, bounds.Bottom, 1);
        // …and the cached base is untouched, because the next frame copies it again.
        Assert.Equal(10, basePath.Bounds.Left, 1);
        Assert.Equal(10, basePath.Bounds.Top, 1);
    }

    [Fact]
    public void NoPreviewDrawsTheBaseWhereItIs()
    {
        using var basePath = SelectionAnts.BasePath([Square(10, 10, 20)]);
        using var frame = SelectionAnts.FramePath(basePath, null, []);
        Assert.NotNull(frame);
        Assert.Equal(basePath!.Bounds, frame!.Bounds);
    }

    [Fact]
    public void NothingSelectedAndNothingDraggedIsNoPathAtAll()
    {
        Assert.Null(SelectionAnts.BasePath([]));
        Assert.Null(SelectionAnts.FramePath(null, SKMatrix.CreateTranslation(5, 5), []));
        Assert.Null(SelectionAnts.OpenPath([]));
    }

    [Fact]
    public void ADragShapeAloneStillGetsAPath()
    {
        using var frame = SelectionAnts.FramePath(null, null, Square(3, 4, 6));
        Assert.NotNull(frame);
        Assert.Equal(3, frame!.Bounds.Left, 1);
        Assert.Equal(10, frame.Bounds.Bottom, 1);
    }

    [Fact]
    public void ThePolygonTrailStaysOpen()
    {
        using var open = SelectionAnts.OpenPath([new(0, 0, 1), new(10, 0, 1), new(10, 10, 1)]);
        Assert.NotNull(open);
        // An open trail: last verb is a line, not a close.
        Assert.Equal(3, open!.PointCount);
    }
}

/// <summary>
/// The view model side of the same promise: every preview step announces its
/// matrix, and ending the session — commit or cancel — announces null, so the
/// canvas can never keep a stale matrix after the gesture.
/// </summary>
[Collection("BrushState")]
public class TransformPreviewEventTests : BrushStateIsolated
{
    private static MainViewModel PaintedVm()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.BrushSize = 16;
        vm.BeginStroke(200, 200, 1);
        vm.MoveStroke(300, 200, 1);
        vm.EndStroke();
        return vm;
    }

    [AvaloniaFact]
    public void EveryPreviewStepRaisesItsMatrixAndTheSessionEndRaisesNull()
    {
        var vm = PaintedVm();
        var raised = new List<SKMatrix?>();
        vm.TransformPreviewChanged += m => raised.Add(m);

        Assert.True(vm.BeginTransform());
        vm.PreviewTransform(SKMatrix.CreateTranslation(40, 0));
        vm.PreviewTransform(SKMatrix.CreateTranslation(80, 0));
        vm.CancelTransform();

        Assert.Equal(3, raised.Count);
        Assert.Equal(40, raised[0]!.Value.TransX, 1);
        Assert.Equal(80, raised[1]!.Value.TransX, 1);
        Assert.Null(raised[2]);
    }

    [AvaloniaFact]
    public void ACommitAlsoClearsThePreviewMatrix()
    {
        var vm = PaintedVm();
        SKMatrix? last = SKMatrix.CreateTranslation(1, 1);
        vm.TransformPreviewChanged += m => last = m;

        Assert.True(vm.BeginTransform());
        vm.PreviewTransform(SKMatrix.CreateTranslation(40, 0));
        vm.CommitTransformAffine(250, 200, 1, 1, 0, 40, 0);

        Assert.False(vm.TransformActive);
        Assert.Null(last);
    }
}
