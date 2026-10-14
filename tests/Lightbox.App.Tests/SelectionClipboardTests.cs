using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// B474: what Ctrl+C, Ctrl+X and Ctrl+V take and where they put it.
/// </summary>
/// <remarks>
/// <para>
/// The owner's rule, as stated: <b>with a selection up, copy and cut take only
/// what was selected; with none, they take the whole content of the layer; and
/// a paste always lands as a new layer directly above the active one.</b> The
/// timeline's right-click Copy and Paste are the one exception — they are the
/// cel, by name.
/// </para>
/// <para>
/// The report was a merged layer, whose drawing is pixels rather than strokes.
/// The selection copy looked only at strokes, found none, and fell through to
/// copying the whole cel — and Ctrl+V then pasted that cel over the active one.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class SelectionClipboardTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel Fresh()
    {
        StrokeClipboard.Clear();
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 30;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        vm.AntiAliasing = false;
        return vm;
    }

    private static void Drag(MainViewModel vm, params (double X, double Y)[] points)
    {
        vm.BeginStroke(points[0].X, points[0].Y, 1);
        for (var i = 1; i < points.Length; i++) vm.MoveStroke(points[i].X, points[i].Y, 1);
        vm.EndStroke();
    }

    private static void Box(MainViewModel vm, double x0, double y0, double x1, double y1) =>
        vm.ApplySelectionShape(
            [new(x0, y0, 1), new(x1, y0, 1), new(x1, y1, 1), new(x0, y1, 1)],
            add: false, subtract: false);

    /// <summary>The active drawing made of pixels only — what a merge leaves.</summary>
    private static void PixelsOnly(MainViewModel vm)
    {
        using var pixels = new SKBitmap(new SKImageInfo(
            vm.Doc.Scene.Width, vm.Doc.Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(pixels)) canvas.Clear(SKColors.Red);
        // A new drawing rather than an edit of the old one, so no render of it
        // is cached from before it had pixels.
        vm.PaintLayer().Cels[vm.CurrentFrameIndex].Frame = new Frame { PngBase64 = PngCodec.Encode(pixels) };
    }

    private static byte BaselineAlpha(Frame frame, int x, int y)
    {
        if (frame.PngBase64 is not { Length: > 0 } png) return 0;
        using var bitmap = PngCodec.Decode(png);
        return bitmap.GetPixel(x, y).Alpha;
    }

    private static bool IsRed(SKColor c) => c.Alpha > 200 && c.Red > 200 && c.Green < 60;

    private static Frame DrawingOn(Layer layer, int cel = 0) => layer.Cels[cel].Frame!;

    // ---- a selection: only what is inside it -----------------------------------

    [AvaloniaFact]
    public void ASelectionOverPixelsCopiesOnlyThePixelsInsideIt_OntoANewLayerAbove()
    {
        var vm = Fresh();
        PixelsOnly(vm);
        var source = vm.PaintLayer();
        var layers = vm.Doc.Scene.Layers.Count;
        var active = vm.ActiveLayerIndex;

        Box(vm, 100, 100, 200, 200);
        vm.EditCopy();
        vm.EditPaste();

        Assert.Equal(layers + 1, vm.Doc.Scene.Layers.Count);
        Assert.Equal(active + 1, vm.ActiveLayerIndex);
        var pasted = DrawingOn(vm.Doc.Scene.Layers[active + 1]);
        output.WriteLine($"inside {BaselineAlpha(pasted, 150, 150)}, outside {BaselineAlpha(pasted, 50, 50)} / {BaselineAlpha(pasted, 400, 300)}");
        Assert.Equal(255, BaselineAlpha(pasted, 150, 150));
        Assert.Equal(0, BaselineAlpha(pasted, 50, 50));
        Assert.Equal(0, BaselineAlpha(pasted, 400, 300));
        // The source is untouched, and is still the same layer below.
        Assert.Same(source, vm.Doc.Scene.Layers[active]);
        Assert.Equal(255, BaselineAlpha(DrawingOn(source), 50, 50));
    }

    [AvaloniaFact]
    public void CuttingASelectionOverPixelsClearsOnlyInsideIt()
    {
        var vm = Fresh();
        PixelsOnly(vm);

        Box(vm, 100, 100, 200, 200);
        var layer = vm.PaintLayer();
        vm.EditCut();
        var source = DrawingOn(layer);

        // The cut is a clear-region stroke over the pixels, so read the drawing
        // as it renders: the baseline, then its strokes on top.
        using var rendered = PngCodec.Decode(source.PngBase64!);
        foreach (var stroke in source.Strokes) FrameRasterizer.Append(rendered, stroke);
        var inside = rendered.GetPixel(150, 150);
        var outside = rendered.GetPixel(50, 50);
        output.WriteLine($"inside {inside}, outside {outside}");
        Assert.False(IsRed(inside), "the pixels inside the selection were not cut");
        Assert.True(IsRed(outside), "the pixels outside the selection went too");

        vm.EditPaste();
        Assert.Equal(255, BaselineAlpha(DrawingOn(vm.PaintLayer()), 150, 150));
    }

    [AvaloniaFact]
    public void ASelectionOverStrokesAndPixelsTakesBoth()
    {
        var vm = Fresh();
        PixelsOnly(vm);
        Drag(vm, (120, 150), (180, 150));
        var active = vm.ActiveLayerIndex;

        Box(vm, 100, 100, 200, 200);
        vm.EditCopy();
        vm.EditPaste();

        var pasted = DrawingOn(vm.Doc.Scene.Layers[active + 1]);
        Assert.Single(pasted.Strokes);
        Assert.Equal(255, BaselineAlpha(pasted, 150, 120));
    }

    [AvaloniaFact]
    public void ASelectionOverADrawingMergedIntoAGroupCopiesWhatItHolds()
    {
        // The other shape a merge leaves (Q265): strokes kept with the erasers
        // that reach them, inside a group. Still strokes, so still copyable.
        var vm = Fresh();
        Drag(vm, (40, 40), (160, 40));
        vm.AddPaintedLayerCommand.Execute(null);
        Drag(vm, (40, 120), (160, 120));
        vm.PaintStrokes().Add(new Stroke
        {
            Tool = ToolKind.Eraser,
            Points = [new StrokePoint(100, 20, 1), new StrokePoint(100, 180, 1)],
            Brush = new BrushSettings { Size = 20, Hardness = 1 },
        });
        vm.MergeLayerDown(null, Lightbox.Raster.EraserMerge.Group);
        Assert.Contains(vm.PaintStrokes(), st => st.Group is not null);
        var active = vm.ActiveLayerIndex;

        Box(vm, 20, 100, 180, 140);
        vm.EditCopy();
        vm.EditPaste();

        Assert.Equal(active + 1, vm.ActiveLayerIndex);
        Assert.NotEmpty(DrawingOn(vm.PaintLayer()).Strokes);
    }

    [AvaloniaFact]
    public void ASelectionHoldingNothingCopiesNothing_RatherThanTheWholeCel()
    {
        var vm = Fresh();
        Drag(vm, (60, 60), (240, 60));

        Box(vm, 500, 300, 600, 400);
        vm.EditCopy();

        Assert.False(vm.HasCelClipboard, "an empty selection copied the cel");
        Assert.False(vm.HasLayerClipboard, "an empty selection copied the layer");
        Assert.False(vm.HasStrokeClipboard);
    }

    [AvaloniaFact]
    public void CopyingOnAHoldReadsTheHeldDrawingWithoutKeyingIt()
    {
        // A copy only reads: on a hold it takes what the hold shows, and must
        // not key a copy of it the way a mark there would (adversary, B474).
        var vm = Fresh();
        Drag(vm, (60, 60), (240, 60));
        var layer = vm.PaintLayer();
        // Three frames, 1 and 2 holding the drawing on frame 0.
        vm.Doc.Scene.FrameCount = 3;
        foreach (var l in vm.Doc.Scene.Layers)
        {
            while (l.Cels.Count < 3) l.Cels.Add(new Cel());
        }
        vm.CurrentFrameIndex = 2;
        Assert.Null(layer.Cels[2].Frame);

        Box(vm, 20, 20, 280, 120);
        vm.EditCopy();

        Assert.True(vm.HasStrokeClipboard);
        Assert.Null(layer.Cels[2].Frame);
    }

    [AvaloniaFact]
    public void CuttingASelectionHoldingNothingLeavesTheDrawingAlone()
    {
        var vm = Fresh();
        Drag(vm, (60, 60), (240, 60));

        Box(vm, 500, 300, 600, 400);
        vm.EditCut();

        Assert.Single(vm.PaintStrokes());
        Assert.NotNull(vm.PaintLayer().Cels[0].Frame);
    }

    // ---- no selection: the whole layer ---------------------------------------------

    [AvaloniaFact]
    public void WithNothingSelectedCopyTakesTheWholeLayer_AndPasteAddsItAbove()
    {
        var vm = Fresh();
        Drag(vm, (60, 60), (240, 60));
        vm.CurrentFrameIndex = 2;
        vm.InsertFrameAtPlayhead(FrameRole.Key);
        Drag(vm, (60, 200), (240, 200));
        var source = vm.PaintLayer();
        var active = vm.ActiveLayerIndex;
        var layers = vm.Doc.Scene.Layers.Count;

        vm.EditCopy();
        vm.EditPaste();

        Assert.Equal(layers + 1, vm.Doc.Scene.Layers.Count);
        Assert.Same(source, vm.Doc.Scene.Layers[active]);
        var pasted = vm.Doc.Scene.Layers[active + 1];
        Assert.Equal(pasted.Id, vm.PaintLayer().Id);
        Assert.Single(DrawingOn(pasted, 0).Strokes);
        Assert.Single(DrawingOn(pasted, 2).Strokes);
        Assert.Single(DrawingOn(source, 0).Strokes);
    }

    [AvaloniaFact]
    public void WithNothingSelectedCutEmptiesTheLayerButKeepsIt()
    {
        var vm = Fresh();
        Drag(vm, (60, 60), (240, 60));
        var source = vm.PaintLayer();
        var layers = vm.Doc.Scene.Layers.Count;

        vm.EditCut();

        Assert.Equal(layers, vm.Doc.Scene.Layers.Count);
        Assert.Contains(source, vm.Doc.Scene.Layers);
        Assert.All(source.Cels, c => Assert.True(c.Frame is null || c.Frame.Strokes.Count == 0));

        vm.EditPaste();
        Assert.Equal(layers + 1, vm.Doc.Scene.Layers.Count);
        Assert.Single(DrawingOn(vm.PaintLayer()).Strokes);
    }

    [AvaloniaFact]
    public void TheWholeLayerCutIsOneUndoStep()
    {
        var vm = Fresh();
        Drag(vm, (60, 60), (240, 60));

        vm.EditCut();
        vm.UndoCommand.Execute(null);

        Assert.Single(vm.PaintStrokes());
    }

    // ---- paste: always a new layer above -----------------------------------------

    [AvaloniaFact]
    public void ACelCopiedFromTheTimelineMenuPastesByKeyAsANewLayerAbove()
    {
        var vm = Fresh();
        Drag(vm, (60, 60), (240, 60));
        var source = vm.PaintLayer();
        var active = vm.ActiveLayerIndex;
        var layers = vm.Doc.Scene.Layers.Count;
        var cell = vm.LayerRows.First(r => r.SceneIndex == active).Cells[0];

        vm.CopyCel(cell);  // the right-click Copy
        vm.EditPaste();    // Ctrl+V

        Assert.Equal(layers + 1, vm.Doc.Scene.Layers.Count);
        Assert.Same(source, vm.Doc.Scene.Layers[active]);
        Assert.Single(DrawingOn(source).Strokes);
        Assert.Single(DrawingOn(vm.Doc.Scene.Layers[active + 1]).Strokes);
    }

    [AvaloniaFact]
    public void TheTimelineMenusPasteStillMeansTheCel()
    {
        var vm = Fresh();
        Drag(vm, (60, 60), (240, 60));
        var active = vm.ActiveLayerIndex;
        var layers = vm.Doc.Scene.Layers.Count;
        var cells = vm.LayerRows.First(r => r.SceneIndex == active).Cells;

        vm.CopyCel(cells[0]);
        vm.PasteCel(cells[3]);

        Assert.Equal(layers, vm.Doc.Scene.Layers.Count);
        Assert.Single(DrawingOn(vm.Doc.Scene.Layers[active], 3).Strokes);
    }

    [AvaloniaFact]
    public void APasteLandsInTheActiveLayersFolder_DirectlyAboveIt()
    {
        var vm = Fresh();
        vm.AddPaintedLayerCommand.Execute(null);
        vm.AddPaintedLayerCommand.Execute(null);
        // Folder around the top two, then back to the lower of them.
        var lower = vm.Doc.Scene.Layers[^2];
        var upper = vm.Doc.Scene.Layers[^1];
        vm.SelectLayer(vm.LayerRows.First(r => r.Layer == lower), toggle: false, range: false);
        vm.SelectLayer(vm.LayerRows.First(r => r.Layer == upper), toggle: true, range: false);
        vm.GroupLayersCommand.Execute(null);
        vm.SelectLayer(vm.LayerRows.First(r => r.Layer == lower), toggle: false, range: false);
        Drag(vm, (60, 60), (240, 60));
        Assert.Same(lower, vm.PaintLayer());

        Box(vm, 20, 20, 280, 120);
        vm.EditCopy();
        vm.EditPaste();

        var pasted = vm.PaintLayer();
        output.WriteLine(string.Join(",", vm.Doc.Scene.Layers.Select(l => $"{l.Name}[{l.GroupId}]")));
        Assert.NotSame(lower, pasted);
        Assert.Equal(lower.GroupId, pasted.GroupId);
        Assert.Equal(vm.Doc.Scene.Layers.IndexOf(lower) + 1, vm.Doc.Scene.Layers.IndexOf(pasted));
    }

    [AvaloniaFact]
    public void PasteTakesWhicheverWasCopiedLast()
    {
        var vm = Fresh();
        Drag(vm, (60, 60), (240, 60));
        Drag(vm, (60, 200), (240, 200));
        var active = vm.ActiveLayerIndex;

        Box(vm, 20, 20, 280, 120);
        vm.EditCopy();          // one line
        vm.DeselectCommand.Execute(null);
        vm.EditCopy();          // the whole layer, two lines
        vm.EditPaste();

        Assert.Equal(2, DrawingOn(vm.Doc.Scene.Layers[active + 1]).Strokes.Count);
    }
}
