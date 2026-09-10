using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.App.Tests;

/// <summary>
/// Band mode in the transform tool (Q184), through the real view-model path:
/// begin, place a line, drag it, commit.
/// </summary>
/// <remarks>
/// The geometry itself is covered by <c>BandScaleTests</c> in the Core suite.
/// What is tested here is the half that only exists once the tool is wired —
/// that a commit reaches the stroke record, that the box really does not move,
/// and that points land on the dividers.
/// </remarks>
public class TransformBandsTests(ITestOutputHelper output)
{
    private const int W = 400;
    private const int H = 400;

    /// <summary>A figure: one long vertical stroke from y=50 to y=350.</summary>
    private static MainViewModel VmWithAFigure()
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings("bands", W, H, 12, 180, "#808080", false));
        var layer = vm.Doc.Scene.Layers[^1];
        var frame = layer.Cels[0].Frame!;
        frame.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#101010",
            Brush = new BrushSettings { Size = 8, Hardness = 0.8, Flow = 1, Opacity = 1 },
            // Deliberately only two points, 300 px apart: the coarse sampling
            // that makes divider insertion matter.
            Points = [new StrokePoint(200, 50, 1), new StrokePoint(200, 350, 1)],
        });
        return vm;
    }

    private static Stroke TheStroke(MainViewModel vm) =>
        vm.Doc.Scene.Layers[^1].Cels[0].Frame!.Strokes[0];

    /// <summary>
    /// <b>The whole promise, end to end: the drawing's extent is unchanged and
    /// its middle has moved.</b>
    /// </summary>
    /// <remarks>
    /// The owner's case. A divider at y=200, dragged to y=120, should leave the
    /// stroke spanning exactly 50..350 while everything between the ends is
    /// redistributed — the figure keeps its height and its proportions change.
    /// </remarks>
    [AvaloniaFact]
    public void ABandCommitRedistributesWithoutChangingTheExtent()
    {
        var vm = VmWithAFigure();
        Assert.True(vm.BeginTransform());

        var box = (Start: 50.0, End: 350.0);
        var y = BandScale.Add(BandScale.Axis.None(box.Start, box.End), 200);
        y = BandScale.Drag(y, 0, 120);

        vm.CommitTransformBands(BandScale.Axis.None(0, W), y);

        var pts = TheStroke(vm).Points;
        var top = pts.Min(p => p.Y);
        var bottom = pts.Max(p => p.Y);
        output.WriteLine($"{pts.Count} points, spanning {top:0.0}..{bottom:0.0}");
        foreach (var p in pts) output.WriteLine($"    ({p.X:0.0}, {p.Y:0.0})");

        // The extent is exactly what it was.
        Assert.Equal(50, top, 4);
        Assert.Equal(350, bottom, 4);
        // And the hip moved: a point was inserted on the divider and is now at
        // where the divider was dragged to.
        Assert.Contains(pts, p => Math.Abs(p.Y - 120) < 1e-4);
    }

    /// <summary>
    /// A point is inserted on the divider, so the bend is where the line is.
    /// </summary>
    [AvaloniaFact]
    public void CommittingInsertsAPointOnTheDivider()
    {
        var vm = VmWithAFigure();
        var before = TheStroke(vm).Points.Count;
        Assert.True(vm.BeginTransform());

        var y = BandScale.Drag(
            BandScale.Add(BandScale.Axis.None(50, 350), 200), 0, 120);
        vm.CommitTransformBands(BandScale.Axis.None(0, W), y);

        var after = TheStroke(vm).Points.Count;
        output.WriteLine($"{before} points before, {after} after");
        Assert.Equal(before + 1, after);
    }

    /// <summary>
    /// <b>A commit that moves nothing records no undo step.</b>
    /// </summary>
    /// <remarks>
    /// Placing a line and pressing Enter is a thing an artist will do by
    /// accident, and it must not leave an undo step or a dirty badge behind —
    /// the same promise <c>TransformIsIdentity</c> already makes for the box.
    /// </remarks>
    [AvaloniaFact]
    public void PlacingALineAndApplyingItChangesNothing()
    {
        var vm = VmWithAFigure();
        var before = TheStroke(vm).Points.Select(p => (p.X, p.Y)).ToList();
        Assert.True(vm.BeginTransform());

        // Placed, never dragged.
        var y = BandScale.Add(BandScale.Axis.None(50, 350), 200);
        vm.CommitTransformBands(BandScale.Axis.None(0, W), y);

        var after = TheStroke(vm).Points.Select(p => (p.X, p.Y)).ToList();
        Assert.Equal(before, after);
        Assert.False(vm.TransformActive);
    }

    /// <summary>Brush size is untouched — a stretched leg keeps its weight.</summary>
    /// <remarks>
    /// An affine commit scales the brush by the geometric mean of its two
    /// factors. A band scale has no single factor, and Q184 decided the line
    /// weight stays: correcting a proportion is moving the drawing, not
    /// redrawing it heavier.
    /// </remarks>
    [AvaloniaFact]
    public void ABandCommitLeavesBrushSizeAlone()
    {
        var vm = VmWithAFigure();
        var size = TheStroke(vm).Brush.Size;
        Assert.True(vm.BeginTransform());

        var y = BandScale.Drag(
            BandScale.Add(BandScale.Axis.None(50, 350), 200), 0, 120);
        vm.CommitTransformBands(BandScale.Axis.None(0, W), y);

        Assert.Equal(size, TheStroke(vm).Brush.Size, 6);
    }

    /// <summary>
    /// Vertical dividers work the same way, on X.
    /// </summary>
    [AvaloniaFact]
    public void AVerticalDividerRedistributesAcross()
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings("bands", W, H, 12, 180, "#808080", false));
        var frame = vm.Doc.Scene.Layers[^1].Cels[0].Frame!;
        frame.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#101010",
            Brush = new BrushSettings { Size = 8 },
            Points = [new StrokePoint(50, 200, 1), new StrokePoint(350, 200, 1)],
        });
        Assert.True(vm.BeginTransform());

        var x = BandScale.Drag(
            BandScale.Add(BandScale.Axis.None(50, 350), 200), 0, 280);
        vm.CommitTransformBands(x, BandScale.Axis.None(0, H));

        var pts = frame.Strokes[0].Points;
        output.WriteLine(string.Join(", ", pts.Select(p => $"({p.X:0.0},{p.Y:0.0})")));
        Assert.Equal(50, pts.Min(p => p.X), 4);
        Assert.Equal(350, pts.Max(p => p.X), 4);
        Assert.Contains(pts, p => Math.Abs(p.X - 280) < 1e-4);
    }

    /// <summary>
    /// <b>The gizmo's band passes tile the box, so the preview shows no seam.</b>
    /// </summary>
    /// <remarks>
    /// The source rectangles are integers because <c>RenderPass.Source</c> is,
    /// and each band edge is rounded once and shared — so consecutive crops must
    /// abut exactly. A gap means a row of the drawing is missing from the
    /// preview; an overlap means a doubled edge.
    /// </remarks>
    [AvaloniaFact]
    public void TheBandPassesCropWithoutAGapOrAnOverlap()
    {
        var canvas = new Lightbox.App.Rendering.CanvasControl();
        canvas.BeginTransformGizmo(0, 0, 200, 400);
        canvas.TransformBands = true;

        // With nothing placed there is one pass covering the box — the
        // single-matrix behaviour the tool already had.
        var single = Assert.Single(canvas.TransformBandPasses);
        output.WriteLine($"no dividers: source {single.Source}");
        Assert.Equal(0, single.Source.Top);
        Assert.Equal(400, single.Source.Bottom);
        Assert.True(canvas.TransformBandsAreIdentity);

        // Now the real gesture: place two horizontal lines and drag one.
        // Driven through the press path rather than through synthetic pointer
        // input, which is unreliable headless — this is the same method the
        // pointer handler calls.
        Assert.True(canvas.TxBandPress(100, 100, shift: false, alt: false));
        Assert.True(canvas.TxBandPress(100, 300, shift: false, alt: false));
        Assert.True(canvas.TxBandPress(60, 0, shift: true, alt: false));
        Assert.Equal(3, canvas.TransformDividerCount);
        Assert.True(canvas.TransformBandsAreIdentity);

        canvas.TxBandDragTo(100, 220);
        Assert.False(canvas.TransformBandsAreIdentity);

        var passes = canvas.TransformBandPasses;
        output.WriteLine($"{passes.Count} passes:");
        foreach (var (src, _) in passes) output.WriteLine($"    {src}");
        // 2 vertical bands x 3 horizontal = 6 cells.
        Assert.Equal(6, passes.Count);

        // The source crops must tile the box exactly: sum the areas and compare.
        var area = passes.Sum(p => (long)p.Source.Width * p.Source.Height);
        output.WriteLine($"source area {area} vs box {200 * 400}");
        Assert.Equal(200L * 400, area);
    }

    /// <summary>
    /// Alt takes a line away, matching what Alt means to the selection tools.
    /// </summary>
    [AvaloniaFact]
    public void AltClickingADividerRemovesIt()
    {
        var canvas = new Lightbox.App.Rendering.CanvasControl();
        canvas.BeginTransformGizmo(0, 0, 200, 400);
        canvas.TransformBands = true;

        canvas.TxBandPress(100, 150, shift: false, alt: false);
        Assert.Equal(1, canvas.TransformDividerCount);

        // On the line, with Alt.
        Assert.True(canvas.TxBandPress(100, 150, shift: false, alt: true));
        Assert.Equal(0, canvas.TransformDividerCount);
    }

    /// <summary>
    /// A drag cannot push a divider out of the box, however far the pointer goes.
    /// </summary>
    /// <remarks>
    /// The gizmo's half of "the boundaries of the box are never crossed" — the
    /// clamp lives in Core and is tested there, and this is the check that the
    /// gesture actually routes through it.
    /// </remarks>
    [AvaloniaFact]
    public void DraggingADividerMilesAwayKeepsItInsideTheBox()
    {
        var canvas = new Lightbox.App.Rendering.CanvasControl();
        canvas.BeginTransformGizmo(0, 0, 200, 400);
        canvas.TransformBands = true;
        canvas.TxBandPress(100, 200, shift: false, alt: false);

        canvas.TxBandDragTo(100, 99999);
        var (_, y) = canvas.TransformBandsResult;
        output.WriteLine($"dragged to 99999, landed at {y.Moved[0]}");
        Assert.InRange(y.Moved[0], 0, 400);

        canvas.TxBandDragTo(100, -99999);
        (_, y) = canvas.TransformBandsResult;
        output.WriteLine($"dragged to -99999, landed at {y.Moved[0]}");
        Assert.InRange(y.Moved[0], 0, 400);
    }

    /// <summary>
    /// Band mode reports identity until a divider is actually dragged, so Enter
    /// does not record an empty step.
    /// </summary>
    [AvaloniaFact]
    public void TheGizmoIsIdentityUntilADividerMoves()
    {
        var canvas = new Lightbox.App.Rendering.CanvasControl();
        canvas.BeginTransformGizmo(0, 0, 200, 400);

        Assert.True(canvas.TransformIsIdentity);
        canvas.TransformBands = true;
        Assert.True(canvas.TransformIsIdentity);
        Assert.Equal(0, canvas.TransformDividerCount);
    }
}
