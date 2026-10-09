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
}
