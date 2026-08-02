using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.App.Tests;

/// <remarks>
/// In the <c>BrushState</c> collection because it sets brush parameters, and
/// those live in a process-wide store: running beside a test that assumes
/// defaults hands it this one’s brush. It opted out until a CI run caught
/// it — the live-preview pixel check went red on a loaded machine and was
/// green on every local run, which is what this collection exists to stop.
/// </remarks>
[Collection("BrushState")]
public class TransformToolTests : BrushStateIsolated
{
    /// <summary>
    /// Bare — one drawing layer, no paper. Transform scopes are addressed by
    /// layer and row index, and "every layer" assertions would otherwise have
    /// to reason about the locked paper, which is not what they are checking.
    /// </summary>
    private static MainViewModel PaintedVm()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 16;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        vm.BrushWetEdge = 0;
        vm.BrushGranulation = 0;
        vm.BrushScatter = 0;
        vm.BeginStroke(200, 200, 1);
        vm.MoveStroke(300, 200, 1);
        vm.EndStroke();
        return vm;
    }

    private static PaintedFrame ActiveFrame(MainViewModel vm) =>
        (PaintedFrame)vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[0].Frame!;

    [AvaloniaFact]
    public void BeginTransform_ReportsTheStrokeBounds_AndCommitMovesThePoints()
    {
        var vm = PaintedVm();
        (double MinX, double MinY, double MaxX, double MaxY)? bounds = null;
        vm.TransformBegun += (a, b, c, d) => bounds = (a, b, c, d);

        Assert.True(vm.BeginTransform());
        Assert.True(vm.TransformActive);
        Assert.NotNull(bounds);
        Assert.InRange(bounds!.Value.MinX, 190, 210);
        Assert.InRange(bounds.Value.MaxX, 290, 310);

        var sizeBefore = ActiveFrame(vm).Strokes[0].Brush.Size;
        vm.CommitTransformAffine(250, 200, 2, 2, 0, 10, 0); // 2× around the middle + shift right
        Assert.False(vm.TransformActive);

        var stroke = ActiveFrame(vm).Strokes[0];
        Assert.Equal(160, stroke.Points[0].X, 3); // 250 + (200-250)*2 + 10
        Assert.Equal(360, stroke.Points[^1].X, 3);
        Assert.Equal(sizeBefore * 2, stroke.Brush.Size, 3); // line weight follows

        vm.UndoCommand.Execute(null);
        Assert.Equal(200, ActiveFrame(vm).Strokes[0].Points[0].X, 3); // one undo step restores
    }

    [AvaloniaFact]
    public void MirrorCommit_FlipsAroundThePivot_WithoutMoving()
    {
        var vm = PaintedVm();
        Assert.True(vm.BeginTransform());
        vm.CommitTransformAffine(250, 200, -1, 1, 0, 0, 0); // mirror horizontally around x=250

        var stroke = ActiveFrame(vm).Strokes[0];
        Assert.Equal(300, stroke.Points[0].X, 3); // 200 -> 300
        Assert.Equal(200, stroke.Points[^1].X, 3); // 300 -> 200
        Assert.Equal(200, stroke.Points[0].Y, 3); // vertical position untouched
        Assert.Equal(16, stroke.Brush.Size, 1); // |−1×1| = 1: no size change
    }

    [AvaloniaFact]
    public void PerspectiveCommit_MapsTheCornersExactly()
    {
        var vm = PaintedVm();
        Assert.True(vm.BeginTransform());

        double[] src = [0, 0, 100, 0, 100, 100, 0, 100];
        double[] dst = [10, 5, 90, 0, 100, 100, 0, 95];
        var map = TransformOps.Perspective(src, dst);
        for (var i = 0; i < 4; i++)
        {
            var (x, y) = map(src[i * 2], src[i * 2 + 1]);
            Assert.Equal(dst[i * 2], x, 6);
            Assert.Equal(dst[i * 2 + 1], y, 6);
        }

        vm.CommitTransformPerspective(src, dst);
        Assert.False(vm.TransformActive); // commit ends the session
    }

    [AvaloniaFact]
    public void DegeneratePerspective_IsRefused_AndTheSessionSurvives()
    {
        var vm = PaintedVm();
        Assert.True(vm.BeginTransform());
        double[] src = [0, 0, 100, 0, 100, 100, 0, 100];
        double[] collapsed = [50, 50, 50, 50, 50, 50, 50, 50];
        vm.CommitTransformPerspective(src, collapsed);
        Assert.True(vm.TransformActive); // refused, still editable
        Assert.Contains("corner", vm.AiStatus, StringComparison.OrdinalIgnoreCase);
        vm.CancelTransform();
    }

    [AvaloniaFact]
    public void CelRangeScope_TransformsEachDistinctDrawingOnce()
    {
        var vm = PaintedVm();
        vm.AddFrameCommand.Execute(null); // duplicate exposure structure: frame 1
        vm.ClearCelAt(vm.LayerRows[0].Cells.First(c => c.Index == 1)); // frame 1 = hold of frame 0

        var cell0 = vm.LayerRows[^1].Cells.First(c => c.Index == 0);
        vm.RangeSelectTo(cell0);
        vm.RangeSelectTo(vm.LayerRows[^1].Cells.First(c => c.Index == 1));

        vm.TransformScope = TransformScope.CelRange;
        Assert.True(vm.BeginTransform());
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 50, 0);

        // The hold shares the frame: had it been transformed twice, the shift
        // would be 100 instead of 50.
        Assert.Equal(250, ActiveFrame(vm).Strokes[0].Points[0].X, 3);
    }

    [AvaloniaFact]
    public void EntireAnimationScope_MovesEveryLayer()
    {
        var vm = PaintedVm();
        vm.AddPaintedLayerCommand.Execute(null); // second layer, becomes active
        vm.BeginStroke(100, 100, 1);
        vm.EndStroke();

        vm.TransformScope = TransformScope.EntireAnimation;
        Assert.True(vm.BeginTransform());
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 0, 25);

        foreach (var layer in vm.Doc.Scene.Layers)
        {
            var frame = (PaintedFrame)layer.Cels[0].Frame!;
            Assert.All(frame.Strokes, s => Assert.True(s.Points[0].Y is 225 or 125));
        }
    }

    [AvaloniaFact]
    public void SelectionRegion_LimitsTheTransform_ToStrokesInsideIt()
    {
        var vm = PaintedVm(); // stroke A: y=200, x=200..300
        vm.BeginStroke(200, 400, 1); // stroke B: far below
        vm.MoveStroke(300, 400, 1);
        vm.EndStroke();

        // Select only around stroke A, then transform "everything" in scope.
        vm.ApplySelectionShape(
            [new(150, 150, 1), new(350, 150, 1), new(350, 250, 1), new(150, 250, 1)],
            add: false, subtract: false);
        Assert.True(vm.BeginTransform());
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 0, -50);

        var strokes = ActiveFrame(vm).Strokes;
        Assert.Equal(150, strokes[0].Points[0].Y, 3); // A moved up
        Assert.Equal(400, strokes[1].Points[0].Y, 3); // B untouched
    }

    [AvaloniaFact]
    public void EmptyScope_RefusesToStart()
    {
        var vm = new MainViewModel(null); // nothing drawn
        Assert.False(vm.BeginTransform());
        Assert.False(vm.TransformActive);
        Assert.Contains("Nothing to transform", vm.AiStatus);
    }
}
