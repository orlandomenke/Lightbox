using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// B381 and B382 through the view model: the Transform tool, the marquee and
/// the pen all act on the posed picture the canvas shows, and what they write
/// into the rest-space record renders back to exactly that.
/// </summary>
/// <remarks>
/// The rig in every test: one bone from (100,150) along +x, posed to point
/// straight down. A rest stroke along the bone from (110,150) to (150,150)
/// therefore stands on screen at x=100 from y=160 to y=200.
/// </remarks>
[Collection("BrushState")]
public class PosedTransformTests : BrushStateIsolated
{
    private static MainViewModel Posed(out string bone, out Stroke stroke)
    {
        var vm = new MainViewModel(null)
        {
            SmoothStrokes = false,
            ColorHex = "#000000",
            BrushSize = 8,
            BrushHardness = 1,
            BrushOpacity = 1,
            BrushFlow = 1,
            BrushWetEdge = 0,
            BrushGranulation = 0,
            BrushScatter = 0,
        };
        vm.NewDocument(new NewDocumentSettings("Rig", 400, 300, 12, 72, "#ffffff", false));
        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 150, 160, 150);
        bone = vm.SelectedBoneId!;
        vm.SetLayerBone(bone);
        stroke = new Stroke { Points = [new StrokePoint(110, 150, 1), new StrokePoint(150, 150, 1)] };
        PaintFrame(vm).Strokes.Add(stroke);
        vm.PosingMode = true;
        vm.PoseBoneTo(bone, 100, 250);
        vm.PosingMode = false;
        vm.ArmatureEditMode = false;
        vm.MarkDocumentEditedForTests();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return vm;
    }

    private static Frame PaintFrame(MainViewModel vm) =>
        ExposureSheet.ExposedFrame(vm.Doc.Scene.Layers.First(l => !l.IsBackground), vm.CurrentFrameIndex)!;

    /// <summary>Where the render puts a stroke of the paint frame at the playhead.</summary>
    private static (StrokePoint First, StrokePoint Last) Shown(MainViewModel vm, string strokeId)
    {
        var frame = PaintFrame(vm);
        var posed = Skinning.PoseFrameForRender(vm.Doc, frame, vm.CurrentFrameIndex, RigIndex.For(vm.Doc));
        var shown = posed.Strokes.Single(s => s.Id == strokeId);
        return (shown.Points[0], shown.Points[^1]);
    }

    private static SKBitmap Pixels(RenderSnapshot snapshot)
    {
        var bmp = SKBitmap.FromImage(snapshot.Image);
        Assert.NotNull(bmp);
        return bmp!;
    }

    [AvaloniaFact]
    public void TheRigStandsWhereTheTestsSayItDoes()
    {
        var vm = Posed(out _, out var stroke);
        var (first, last) = Shown(vm, stroke.Id);
        Assert.Equal(100, first.X, 6);
        Assert.Equal(160, first.Y, 6);
        Assert.Equal(100, last.X, 6);
        Assert.Equal(200, last.Y, 6);
    }

    /// <summary>B381, the commit: the posed picture moves by the gizmo's translation and nothing else.</summary>
    [AvaloniaFact]
    public void CommittingATranslationMovesThePosedPictureByTheTranslation()
    {
        var vm = Posed(out _, out var stroke);

        Assert.True(vm.BeginTransform(), vm.AiStatus);
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 30, 10);

        var (first, last) = Shown(vm, stroke.Id);
        Assert.Equal(130, first.X, 6);
        Assert.Equal(170, first.Y, 6);
        Assert.Equal(130, last.X, 6);
        Assert.Equal(210, last.Y, 6);
        // The record took the translation turned back through the bone: a
        // posed (+30,+10) on a bone turned 90° is a rest (+10,−30).
        var record = PaintFrame(vm).Strokes.Single(s => s.Id == stroke.Id);
        Assert.Equal(120, record.Points[0].X, 6);
        Assert.Equal(120, record.Points[0].Y, 6);
    }

    /// <summary>B381, the marquee: a box drawn over the posed ink finds it, and finds it whole.</summary>
    [AvaloniaFact]
    public void AMarqueeOverThePosedInkCatchesTheWholeStroke()
    {
        var vm = Posed(out _, out var stroke);
        // Round the posed line (x=100, y 160..200) and clear of the rest one
        // (y=150, x 110..150), brush reach included.
        vm.ApplySelectionShape(
            [new(85, 156, 1), new(115, 156, 1), new(115, 206, 1), new(85, 206, 1)],
            add: false, subtract: false);

        Assert.True(vm.BeginTransform(), vm.AiStatus);
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 50, 0);

        // One stroke, not a stayed half and a travelled half.
        var record = Assert.Single(PaintFrame(vm).Strokes);
        Assert.Equal(stroke.Id, record.Id);
        var (first, last) = Shown(vm, stroke.Id);
        Assert.Equal(150, first.X, 6);
        Assert.Equal(160, first.Y, 6);
        Assert.Equal(150, last.X, 6);
        Assert.Equal(200, last.Y, 6);
    }

    /// <summary>B381, the preview: what slides under the gizmo is the posed drawing, not the rest one.</summary>
    [AvaloniaFact]
    public void TheLivePreviewDragsThePosedDrawing()
    {
        var vm = Posed(out _, out _);
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        Assert.True(vm.BeginTransform(), vm.AiStatus);
        vm.PreviewTransform(SKMatrix.CreateTranslation(60, 0));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(latest);
        using var bmp = Pixels(latest!);
        // The posed line, carried 60 right: ink at (160, 180).
        Assert.True(bmp.GetPixel(160, 180).Red < 100,
            $"the preview did not show the posed drawing under the drag (red {bmp.GetPixel(160, 180).Red})");
        // The rest line carried 60 right would be at (190, 150): paper there.
        Assert.True(bmp.GetPixel(190, 150).Red > 200,
            $"the preview dragged the rest pose instead (red {bmp.GetPixel(190, 150).Red})");
        vm.CancelTransform();
    }

    /// <summary>
    /// The sensitivity review's story on B382: a frame posed by per-stroke
    /// weights on an UNRIGGED layer is not carried back to rest, but its
    /// render is rebuilt rather than appended to — so an eraser's probe must
    /// not be opened on a bitmap that rebuild is about to dispose, and the
    /// eraser must still be recorded.
    /// </summary>
    [AvaloniaFact]
    public void AnEraserOverAnAutoBoundStrokeOnAnUnriggedLayerIsRecorded()
    {
        var vm = new MainViewModel(null)
        {
            SmoothStrokes = false, ColorHex = "#000000", BrushSize = 8, BrushHardness = 1,
            BrushOpacity = 1, BrushFlow = 1, BrushWetEdge = 0, BrushGranulation = 0, BrushScatter = 0,
        };
        vm.NewDocument(new NewDocumentSettings("Rig", 400, 300, 12, 72, "#ffffff", false));
        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 150, 160, 150);
        var bone = vm.SelectedBoneId!;
        var stroke = new Stroke { Points = [new StrokePoint(110, 150, 1), new StrokePoint(150, 150, 1)] };
        PaintFrame(vm).Strokes.Add(stroke);
        vm.Selection.SelectStroke(stroke.Id);
        vm.AssignSelectedStrokesToBone();          // per-stroke weights; the layer itself stays unrigged
        vm.Selection.ClearAllSelections();
        vm.PosingMode = true;
        vm.PoseBoneTo(bone, 100, 250);
        vm.PosingMode = false;
        vm.ArmatureEditMode = false;
        vm.MarkDocumentEditedForTests();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Null(vm.Doc.Scene.Layers.First(l => !l.IsBackground).BoneId);

        vm.ActiveTool = ToolId.Eraser;
        vm.BeginStroke(100, 160, 1);
        vm.MoveStroke(100, 200, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, PaintFrame(vm).Strokes.Count);
    }

    /// <summary>
    /// B382: the mark stays where the pen put it. Before the fix the stroke was
    /// committed as rest geometry and swung by the pose on the next render, so
    /// it appeared under the pen and jumped away on release.
    /// </summary>
    [AvaloniaFact]
    public void AStrokeDrawnOnThePosedLayerStaysWhereThePenWas()
    {
        var vm = Posed(out _, out var original);
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        vm.BeginStroke(200, 100, 1);
        vm.MoveStroke(260, 100, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var drawn = PaintFrame(vm).Strokes.Single(s => s.Id != original.Id);
        var (first, last) = Shown(vm, drawn.Id);
        Assert.Equal(200, first.X, 6);
        Assert.Equal(100, first.Y, 6);
        Assert.Equal(260, last.X, 6);
        Assert.Equal(100, last.Y, 6);
        // The record holds rest geometry — somewhere else — and no weights of
        // its own: the layer's binding is read, never written.
        Assert.NotEqual(200, drawn.Points[0].X, 6);
        Assert.Null(drawn.Weights);

        Assert.NotNull(latest);
        using var bmp = Pixels(latest!);
        Assert.True(bmp.GetPixel(230, 100).Red < 100,
            $"the committed stroke is not on screen where it was drawn (red {bmp.GetPixel(230, 100).Red})");
    }
}
