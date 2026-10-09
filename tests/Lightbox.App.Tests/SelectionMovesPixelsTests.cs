using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// B433: a selection moves the pixels it holds, on a layer that is pixels — an
/// imported image, a PSD layer, a merge that had to bake.
/// </summary>
/// <remarks>
/// <para>
/// The owner's report (Q233): with a selection up, switching layer and pressing
/// Ctrl+T or dragging with Move "does not work". Measured: a marquee over a
/// layer whose drawing is a pixel baseline refused with <i>"Nothing to
/// transform in this scope"</i>, because a transform under a selection moved
/// strokes only and left the baseline where it was — the box round the
/// drawing counted the baseline only when there was no selection, so with one
/// there was nothing to box.
/// </para>
/// <para>
/// The rule now is the one Q233 settled for strokes, applied to pixels: what is
/// inside the selection moves, what is outside stays, and the cut is at the
/// selection's edge.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class SelectionMovesPixelsTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static readonly SKColor Red = new(255, 0, 0);

    /// <summary>
    /// A document with one layer holding two red squares as imported pixels:
    /// one at (100, 100)–(140, 140), inside the lasso to come, and one at
    /// (300, 100)–(340, 140), outside it.
    /// </summary>
    private static (MainViewModel Vm, int Layer) Imported()
    {
        var vm = VmLayers.BareVm();
        var layers = vm.Doc.Scene.Layers;
        var layer = Enumerable.Range(0, layers.Count).Last(i => layers[i].Cels.Count > 0);
        using var bmp = new SKBitmap(new SKImageInfo(vm.Doc.Scene.Width, vm.Doc.Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = Red, IsAntialias = false };
            canvas.DrawRect(SKRect.Create(100, 100, 40, 40), paint);
            canvas.DrawRect(SKRect.Create(300, 100, 40, 40), paint);
        }
        layers[layer].Cels[0].Frame = new Frame { PngBase64 = PngCodec.Encode(bmp) };
        vm.ActiveLayerIndex = layer;
        vm.ActiveTool = ToolId.Select;
        return (vm, layer);
    }

    private static void LassoTheLeftSquare(MainViewModel vm) =>
        vm.ApplySelectionShape([new(80, 80, 1), new(160, 80, 1), new(160, 160, 1), new(80, 160, 1)], false, false);

    private static SKColor At(MainViewModel vm, int layer, int x, int y)
    {
        var scene = vm.Doc.Scene;
        using var bmp = FrameRasterizer.Materialize((Frame)scene.Layers[layer].Cels[0].Frame!, scene.Width, scene.Height);
        return bmp.GetPixel(x, y);
    }

    private static bool IsRed(SKColor c) => c.Alpha > 200 && c.Red > 200 && c.Green < 60;

    // ---- the report ---------------------------------------------------------------

    [AvaloniaFact]
    public void ALassoOverImportedPixelsMovesWhatIsInsideAndLeavesTheRest()
    {
        var (vm, layer) = Imported();
        LassoTheLeftSquare(vm);

        var began = vm.BeginTransform();
        output.WriteLine($"Ctrl+T over pixels: {began}; {vm.AiStatus}");
        Assert.True(began, vm.AiStatus);
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 0, 200);

        output.WriteLine($"old spot {At(vm, layer, 120, 120)}, new spot {At(vm, layer, 120, 320)}, other square {At(vm, layer, 320, 120)}");
        Assert.True(IsRed(At(vm, layer, 120, 320)), "the selected pixels did not arrive");
        Assert.Equal(0, At(vm, layer, 120, 120).Alpha);           // and left nothing behind
        Assert.True(IsRed(At(vm, layer, 320, 120)), "pixels outside the selection moved");
        Assert.Equal(0, At(vm, layer, 320, 320).Alpha);
    }

    /// <summary>The Move tool's drag is the same session as Ctrl+T and has to agree.</summary>
    [AvaloniaFact]
    public void AMoveDragInsideTheLassoTakesThePixelsToo()
    {
        var (vm, layer) = Imported();
        LassoTheLeftSquare(vm);

        Assert.True(vm.BeginSelectionMove(120, 120), vm.AiStatus);
        vm.UpdateMove(120, 320, axisLock: false);
        vm.EndMove();

        Assert.True(IsRed(At(vm, layer, 120, 320)), "the drag did not carry the pixels");
        Assert.Equal(0, At(vm, layer, 120, 120).Alpha);
        Assert.True(IsRed(At(vm, layer, 320, 120)));
    }

    /// <summary>One undo puts every pixel back where it was.</summary>
    [AvaloniaFact]
    public void UndoPutsThePixelsBack()
    {
        var (vm, layer) = Imported();
        var before = ((Frame)vm.Doc.Scene.Layers[layer].Cels[0].Frame!).PngBase64;
        LassoTheLeftSquare(vm);
        Assert.True(vm.BeginTransform(), vm.AiStatus);
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 0, 200);
        // Moved first — an undo of a move that never happened restores nothing
        // and passes.
        Assert.True(IsRed(At(vm, layer, 120, 320)), "the move did not happen, so undo proves nothing");

        vm.UndoCommand.Execute(null);

        Assert.True(IsRed(At(vm, layer, 120, 120)), "undo did not bring the pixels back");
        Assert.Equal(0, At(vm, layer, 120, 320).Alpha);
        Assert.Equal(before, ((Frame)vm.Doc.Scene.Layers[layer].Cels[0].Frame!).PngBase64);
    }

    /// <summary>
    /// A layer that is pixels and strokes both: the selection takes both, and
    /// the strokes' half of it is what it always was.
    /// </summary>
    [AvaloniaFact]
    public void PixelsAndStrokesOnOneLayerMoveTogether()
    {
        var (vm, layer) = Imported();
        vm.SmoothStrokes = false;
        vm.ActiveTool = ToolId.Brush;
        vm.ColorHex = "#0000ff";
        vm.BrushSize = 8;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        vm.BeginStroke(100, 150, 1);
        vm.MoveStroke(120, 150, 1);
        vm.MoveStroke(140, 150, 1);
        vm.EndStroke();
        vm.ActiveTool = ToolId.Select;
        LassoTheLeftSquare(vm);

        Assert.True(vm.BeginTransform(), vm.AiStatus);
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 0, 200);

        Assert.True(IsRed(At(vm, layer, 120, 320)));
        var line = At(vm, layer, 120, 350);
        output.WriteLine($"the blue line after the move: {line}");
        Assert.True(line.Blue > 200 && line.Alpha > 200, $"the stroke did not move with the pixels: {line}");
    }

    /// <summary>
    /// What the drag shows is what the commit keeps: while the transform is
    /// open and dragged, the canvas shows the pixels at the new spot and the
    /// old spot empty.
    /// </summary>
    [AvaloniaFact]
    public void ThePreviewShowsThePixelsWhereTheyAreGoing()
    {
        var (vm, _) = Imported();
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        LassoTheLeftSquare(vm);
        Assert.True(vm.BeginTransform(), vm.AiStatus);

        vm.PreviewTransform(SKMatrix.CreateTranslation(0, 200));
        vm.PublishSnapshot();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.NotNull(latest);
        using var shown = SKBitmap.FromImage(latest!.Image);

        output.WriteLine($"preview: old spot {shown.GetPixel(120, 120)}, new spot {shown.GetPixel(120, 320)}, other {shown.GetPixel(320, 120)}");
        Assert.True(IsRed(shown.GetPixel(120, 320)), "the preview does not show the pixels moving");
        Assert.False(IsRed(shown.GetPixel(120, 120)), "the preview still shows them at the old spot");
        Assert.True(IsRed(shown.GetPixel(320, 120)));
    }

    /// <summary>
    /// A merge that had to bake (the upper layer at half opacity) leaves a
    /// layer that is pixels; a lasso over it moves what it holds.
    /// </summary>
    [AvaloniaFact]
    public void ALassoOverABakedMergeMovesWhatIsInside()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        vm.BrushSize = 20;
        while (vm.Doc.Scene.Layers.Count(l => l.Cels.Count > 0) < 2) vm.AddPaintedLayerCommand.Execute(null);
        var layers = vm.Doc.Scene.Layers;
        var painted = Enumerable.Range(0, layers.Count).Where(i => layers[i].Cels.Count > 0).ToList();
        int lower = painted[^2], upper = painted[^1];
        vm.ActiveLayerIndex = lower;
        vm.ColorHex = "#ff0000";
        vm.BeginStroke(100, 120, 1);
        vm.MoveStroke(140, 120, 1);
        vm.EndStroke();
        vm.ActiveLayerIndex = upper;
        vm.ColorHex = "#ff0000";
        vm.BeginStroke(300, 120, 1);
        vm.MoveStroke(340, 120, 1);
        vm.EndStroke();
        layers[upper].Opacity = 0.5;
        Assert.True(vm.MergeWouldBake(layers[upper]));
        vm.MergeLayerDown(layers[upper]);
        var merged = vm.ActiveLayerIndex;
        Assert.True(((Frame)layers[merged].Cels[0].Frame!).HasBaseline);

        vm.ActiveTool = ToolId.Select;
        LassoTheLeftSquare(vm);
        Assert.True(vm.BeginTransform(), vm.AiStatus);
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 0, 200);

        output.WriteLine($"merged layer: old {At(vm, merged, 120, 120)}, new {At(vm, merged, 120, 320)}");
        Assert.True(At(vm, merged, 120, 320).Alpha > 200, "the baked pixels did not move");
        Assert.Equal(0, At(vm, merged, 120, 120).Alpha);
    }

    // ---- from the adversarial review ----------------------------------------------

    /// <summary>
    /// On paper grown on its left and top the baseline is laid out in stroke
    /// coordinates and the selection on the paper's pixels; the cut has to put
    /// the two in one space, or it takes the pixels the grown distance away
    /// from the lasso.
    /// </summary>
    [AvaloniaFact]
    public void OnGrownPaperTheLassoTakesThePixelsUnderIt()
    {
        var (vm, layer) = Imported();
        var baseline = ((Frame)vm.Doc.Scene.Layers[layer].Cels[0].Frame!).PngBase64;
        var choice = new ResizeDialogViewModel(vm.Doc.Scene, ResizeMode.Canvas) { Anchor = ResizeAnchor.BottomRight };
        choice.Width = vm.Doc.Scene.Width + 100;
        choice.Height = vm.Doc.Scene.Height + 50;
        Assert.True(vm.ApplyResize(choice));
        Assert.Equal((-100, -50), (vm.Doc.Scene.Left, vm.Doc.Scene.Top));
        // The pixels stay where they were in stroke coordinates.
        Assert.Equal(baseline, ((Frame)vm.Doc.Scene.Layers[layer].Cels[0].Frame!).PngBase64);
        vm.ActiveTool = ToolId.Select;
        LassoTheLeftSquare(vm);   // in stroke coordinates, round the left square

        Assert.True(vm.BeginTransform(), vm.AiStatus);
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 0, 200);

        output.WriteLine($"grown: old {AtStroke(vm, layer, 120, 120)}, new {AtStroke(vm, layer, 120, 320)}, other {AtStroke(vm, layer, 320, 120)}");
        Assert.True(IsRed(AtStroke(vm, layer, 120, 320)), "the pixels under the lasso did not arrive");
        Assert.Equal(0, AtStroke(vm, layer, 120, 120).Alpha);
        Assert.True(IsRed(AtStroke(vm, layer, 320, 120)), "pixels outside the lasso moved");
    }

    /// <summary>
    /// A diagonal lasso edge across solid paint, and a move too small to see:
    /// the paint along the edge must stay solid. Cutting both halves with an
    /// antialiased edge and laying one over the other left a translucent line
    /// there — a quarter of the alpha gone where the edge crossed a pixel half.
    /// </summary>
    [AvaloniaFact]
    public void ACutAcrossSolidPaintLeavesNoSeam()
    {
        var (vm, layer) = Imported();
        // A triangle whose hypotenuse runs diagonally through the left square.
        vm.ApplySelectionShape([new(80, 80, 1), new(160, 80, 1), new(80, 160, 1)], false, false);
        Assert.True(vm.BeginTransform(), vm.AiStatus);
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 0.001, 0.001);

        var scene = vm.Doc.Scene;
        using var after = FrameRasterizer.Materialize((Frame)scene.Layers[layer].Cels[0].Frame!, scene.Width, scene.Height);
        var faintest = 255;
        for (var y = 102; y < 138; y++)
        {
            for (var x = 102; x < 138; x++) faintest = Math.Min(faintest, after.GetPixel(x, y).Alpha);
        }
        output.WriteLine($"faintest pixel inside the square after a near-zero move: {faintest}");
        Assert.True(faintest > 245, $"a seam along the cut: alpha {faintest}");
    }

    /// <summary>
    /// The selection nudged while the transform is open: pixels follow the
    /// selection the strokes follow — the one on screen at apply — rather
    /// than a copy taken when Ctrl+T was pressed.
    /// </summary>
    [AvaloniaFact]
    public void PixelsFollowASelectionNudgedDuringTheTransform()
    {
        var (vm, layer) = Imported();
        // Round the right square, then nudged onto the left one.
        vm.ApplySelectionShape([new(280, 80, 1), new(360, 80, 1), new(360, 160, 1), new(280, 160, 1)], false, false);
        Assert.True(vm.BeginTransform(), vm.AiStatus);
        vm.NudgeSelection(-200, 0);
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 0, 200);

        output.WriteLine($"left {At(vm, layer, 120, 120)}->{At(vm, layer, 120, 320)}, right {At(vm, layer, 320, 120)}->{At(vm, layer, 320, 320)}");
        Assert.True(IsRed(At(vm, layer, 120, 320)), "the pixels under the nudged selection did not move");
        Assert.True(IsRed(At(vm, layer, 320, 120)), "the pixels under the old selection moved");
    }

    /// <summary>The layer's pixels at a stroke coordinate, wherever the paper's corner is.</summary>
    private static SKColor AtStroke(MainViewModel vm, int layer, int x, int y)
    {
        var scene = vm.Doc.Scene;
        using var bmp = FrameRasterizer.Materialize(
            (Frame)scene.Layers[layer].Cels[0].Frame!, scene.Width, scene.Height,
            origin: new SKPointI(scene.Left, scene.Top));
        return bmp.GetPixel(x - scene.Left, y - scene.Top);
    }
}
