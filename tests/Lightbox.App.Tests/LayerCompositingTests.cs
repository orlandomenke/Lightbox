using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

public class BlendComposeTests
{
    private static SKBitmap Solid(byte r, byte g, byte b, int size = 8)
    {
        var bmp = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(r, g, b, 255));
        return bmp;
    }

    private static SKColor ComposeCenter(SKBlendMode blend, double opacity = 1)
    {
        using var bottom = Solid(128, 128, 128);
        using var top = Solid(128, 128, 128);
        using var image = SceneRenderer.Compose(8, 8,
        [
            new RenderPass(bottom, null, 1),
            new RenderPass(top, null, opacity, blend),
        ], SKColors.White);
        using var bmp = SKBitmap.FromImage(image);
        return bmp.GetPixel(4, 4);
    }

    [AvaloniaFact]
    public void Multiply_Darkens_And_Screen_Lightens()
    {
        var normal = ComposeCenter(SKBlendMode.SrcOver);
        var multiply = ComposeCenter(SKBlendMode.Multiply);
        var screen = ComposeCenter(SKBlendMode.Screen);

        Assert.Equal(128, normal.Red);
        Assert.True(multiply.Red < normal.Red, "multiply must darken (128·128/255 ≈ 64)");
        Assert.True(screen.Red > normal.Red, "screen must lighten (≈ 192)");
    }

    [AvaloniaFact]
    public void Opacity_StillApplies_UnderABlendMode()
    {
        var full = ComposeCenter(SKBlendMode.Multiply);
        var half = ComposeCenter(SKBlendMode.Multiply, 0.5);
        Assert.True(half.Red > full.Red, "a half-opacity multiply layer darkens less");
    }

    [AvaloniaFact]
    public void ToSkia_MapsNormalToSrcOver_AndCoversEveryMode()
    {
        Assert.Equal(SKBlendMode.SrcOver, SceneRenderer.ToSkia(LayerBlendMode.Normal));
        foreach (var mode in Enum.GetValues<LayerBlendMode>())
        {
            var skia = SceneRenderer.ToSkia(mode); // must not throw; Normal is the only SrcOver
            if (mode != LayerBlendMode.Normal) Assert.NotEqual(SKBlendMode.SrcOver, skia);
        }
    }
}

public class LayerPanelTests
{
    [AvaloniaFact]
    public void ActiveLayerOpacity_WritesThrough_AsPercent()
    {
        var vm = new MainViewModel(null);
        vm.ActiveLayerOpacity = 40;
        Assert.Equal(0.4, vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Opacity, 3);
        Assert.Equal(40, vm.ActiveLayerOpacity, 1);
    }

    [AvaloniaFact]
    public void ActiveLayerBlendMode_IsUndoable()
    {
        var vm = new MainViewModel(null);
        vm.ActiveLayerBlendMode = LayerBlendMode.Multiply;
        Assert.Equal(LayerBlendMode.Multiply, vm.Doc.Scene.Layers[vm.ActiveLayerIndex].BlendMode);

        vm.UndoCommand.Execute(null);
        Assert.Equal(LayerBlendMode.Normal, vm.Doc.Scene.Layers[vm.ActiveLayerIndex].BlendMode);
    }

    [AvaloniaFact]
    public void MoveLayer_FlipsCompositingOrder_AndFollowsTheActiveLayer()
    {
        var vm = new MainViewModel(null);
        vm.AddPaintedLayerCommand.Execute(null); // adds on top and activates it
        var topId = vm.Doc.Scene.Layers[2].Id;
        Assert.Equal(2, vm.ActiveLayerIndex);

        var row = vm.LayerRows.First(r => r.Layer.Id == topId);
        vm.MoveLayerDownCommand.Execute(row);

        Assert.Equal(topId, vm.Doc.Scene.Layers[1].Id); // swapped with the layer below
        Assert.Equal(1, vm.ActiveLayerIndex);           // still the active layer
    }

    [AvaloniaFact]
    public void LayerThumb_RendersTheExposedDrawing_AndFollowsThePlayhead()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        var row = vm.LayerRows[0];
        Assert.NotNull(row.Thumb); // frame 0 exists from the start

        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(300, 300, 1);
        vm.EndStroke();
        var frame0 = vm.PaintedCel();
        Assert.Equal(frame0.Id, row.ThumbFrameId);

        // a second, keyed frame: moving the playhead re-renders the thumb for it
        vm.AddFrameCommand.Execute(null);
        vm.CurrentFrameIndex = 1;
        var frame1 = vm.PaintedCel(1);
        Assert.Equal(frame1.Id, vm.LayerRows[0].ThumbFrameId);
    }
}

/// <remarks>
/// In the <c>BrushState</c> collection because it sets brush parameters, and
/// those live in a process-wide store — running beside a test that assumes
/// defaults hands it this one’s brush.
/// </remarks>
[Collection("BrushState")]
public class AlphaSelectAndWandTests : BrushStateIsolated
{
    private static MainViewModel DrawnVm()
    {
        // Pin the working brush: the store is shared across tests and a
        // persisted effect preset (granulation speckle) makes wand/alpha
        // tracing flaky.
        var vm = new MainViewModel(null)
        {
            SmoothStrokes = false,
            BrushSize = 16,
            BrushHardness = 1,
            BrushOpacity = 1,
            BrushFlow = 1,
            BrushWetEdge = 0,
            BrushGranulation = 0,
            BrushScatter = 0,
        };
        vm.BeginStroke(200, 200, 1);
        vm.MoveStroke(300, 200, 1);
        vm.EndStroke();
        return vm;
    }

    private static (double MinX, double MaxX, double MinY, double MaxY) Bounds(MainViewModel vm)
    {
        var pts = vm.SelectionContours.SelectMany(c => c).ToList();
        return (pts.Min(p => p.X), pts.Max(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.Y));
    }

    [AvaloniaFact]
    public void SelectLayerAlpha_SelectsOnlyThePaintedPixels()
    {
        var vm = DrawnVm();
        vm.SelectLayerAlpha(vm.LayerRows[0], add: false, subtract: false);

        Assert.True(vm.HasSelection);
        var (minX, maxX, minY, maxY) = Bounds(vm);
        Assert.InRange(minX, 150, 210);   // stroke spans x 200..300 (± brush radius)
        Assert.InRange(maxX, 290, 350);
        Assert.InRange(minY, 150, 210);
        Assert.InRange(maxY, 190, 250);
    }

    [AvaloniaFact]
    public void SelectLayerAlpha_OnAnEmptyLayer_IsANoOpWithAMessage()
    {
        var vm = new MainViewModel(null);
        vm.SelectLayerAlpha(vm.LayerRows[0], add: false, subtract: false);
        Assert.False(vm.HasSelection);
        Assert.Contains("nothing", vm.AiStatus, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void SelectLayerAlpha_SubtractCarvesOutOfAnExistingSelection()
    {
        var vm = DrawnVm();
        vm.SelectAllCommand.Execute(null);
        vm.SelectLayerAlpha(vm.LayerRows[0], add: false, subtract: true);

        Assert.True(vm.HasSelection); // the canvas minus the stroke
        // painting inside the carved-out (deselected) stroke area must be clipped away
        vm.ActiveTool = ToolId.Brush;
        vm.BeginStroke(250, 200, 1);
        vm.EndStroke();
        var strokes = (vm.PaintedCel()).Strokes;
        Assert.NotNull(strokes[^1].ClipId);
    }

    [AvaloniaFact]
    public void Wand_SelectsTheClickedColorRegion()
    {
        var vm = DrawnVm();
        vm.ActiveTool = ToolId.Select;
        vm.WandSelectAt(250, 200, add: false, subtract: false); // on the stroke

        Assert.True(vm.HasSelection);
        var (minX, maxX, _, _) = Bounds(vm);
        Assert.True(maxX - minX < 200, "wand on the stroke must not select the whole canvas");
    }

    [AvaloniaFact]
    public void Wand_OnEmptyCanvas_SelectsTheConnectedEmptiness()
    {
        var vm = DrawnVm();
        vm.ActiveTool = ToolId.Select;
        vm.WandSelectAt(20, 20, add: false, subtract: false); // far away from the stroke

        Assert.True(vm.HasSelection);
        var (minX, maxX, _, _) = Bounds(vm);
        Assert.True(maxX - minX > 800, "empty background is one big connected region");
    }

    [AvaloniaFact]
    public void FillInsideAWandSelection_StaysInside_AndRecordsTheClip()
    {
        var vm = DrawnVm();
        vm.ActiveTool = ToolId.Select;
        vm.WandSelectAt(250, 200, add: false, subtract: false); // select the stroke region

        vm.ActiveTool = ToolId.Fill;
        vm.FillAt(250, 200);

        var strokes = (vm.PaintedCel()).Strokes;
        var fill = strokes.First(s => s.Tool == ToolKind.Fill);
        Assert.NotNull(fill.ClipId);
        Assert.All(fill.Points, p => Assert.InRange(p.X, 140, 360)); // bounded by the selection
    }
}

public class CelClipboardTests
{
    /// <summary>Cel 0 of a scene layer; -1 (the default) means the layer being drawn on.</summary>
    private static Frame Frame0(MainViewModel vm, int layer = -1) =>
        (Frame)vm.Doc.Scene.Layers[layer < 0 ? vm.ActiveLayerIndex : layer].Cels[0].Frame!;

    /// <summary>A timeline cell; sceneLayer -1 (the default) means the layer being drawn on.</summary>
    private static FrameCell Cell(MainViewModel vm, int sceneLayer, int index) =>
        vm.LayerRows.First(r => r.SceneIndex == (sceneLayer < 0 ? vm.ActiveLayerIndex : sceneLayer))
            .Cells.First(c => c.Index == index);

    [AvaloniaFact]
    public void CopyPaste_DeepClonesWithFreshIds_AndExtendsTheTimeline()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.BeginStroke(50, 50, 1);
        vm.EndStroke();
        var srcId = Frame0(vm).Id;

        vm.CopyCel(Cell(vm, -1, 0));
        vm.PasteCel(Cell(vm, -1, 4)); // paste into the virtual tail

        var scene = vm.Doc.Scene;
        Assert.Equal(5, scene.FrameCount);
        var pasted = (Frame)vm.PaintLayer().Cels[4].Frame!;
        Assert.NotEqual(srcId, pasted.Id);
        Assert.Single(pasted.Strokes);
        Assert.NotSame(Frame0(vm).Strokes[0], pasted.Strokes[0]);
    }

    [AvaloniaFact]
    public void Cut_CopiesThenClears_SoTheCelBecomesAHold()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.BeginStroke(50, 50, 1);
        vm.EndStroke();

        vm.CutCel(Cell(vm, -1, 0));

        Assert.True(vm.HasCelClipboard);
        Assert.Null(vm.PaintLayer().Cels[0].Frame);
    }

    /// <remarks>
    /// Was <c>PasteAcrossKinds_ConvertsStrokes_ButRefusesBaselinePixelsOntoVector</c>,
    /// and it was right about the old behaviour: a cel carrying imported pixels
    /// could not be pasted onto a vector layer, and the paste was refused with a
    /// status mentioning the baseline. There are no kinds to paste across now, so
    /// the refusal has nothing left to protect — every layer holds what any frame
    /// holds. The test is kept pointed at the replacement rather than deleted,
    /// because "a paste that used to be refused now lands, pixels included" is
    /// exactly the behaviour change somebody would want to find a test for.
    /// </remarks>
    [AvaloniaFact]
    public void PasteOntoAnyLayer_CarriesStrokesAndBaselinePixelsAlike()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.BeginStroke(50, 50, 1);
        vm.EndStroke();
        vm.AddPaintedLayerCommand.Execute(null); // scene layer 1

        // Strokes only: lands, as it always did.
        vm.CopyCel(Cell(vm, 0, 0));
        vm.PasteCel(Cell(vm, 1, 0));
        Assert.Single(vm.Doc.Scene.Layers[1].Cels[0].Frame!.Strokes);

        // With imported pixels under them: lands too, and that is the change.
        // A real PNG, not three arbitrary bytes: the paste used to be refused, so
        // nothing ever decoded what the old test put here. Now it lands and gets
        // rendered, and the renderer throws on a baseline it cannot decode (B137).
        var baseline = RealPng();
        vm.Doc.Scene.Layers[0].Cels[0].Frame!.PngBase64 = baseline;
        vm.CopyCel(Cell(vm, 0, 0));
        vm.PasteCel(Cell(vm, 1, 0));

        var pasted = vm.Doc.Scene.Layers[1].Cels[0].Frame!;
        Assert.Equal(baseline, pasted.PngBase64);
        Assert.Single(pasted.Strokes);
        Assert.DoesNotContain("baseline", vm.AiStatus, StringComparison.OrdinalIgnoreCase);

        // A paste is a copy, not a share: the clone carries the content and its
        // own id, or editing one drawing would edit the other.
        Assert.NotEqual(vm.Doc.Scene.Layers[0].Cels[0].Frame!.Id, pasted.Id);
    }

    /// <summary>A decodable one-pixel PNG, for a baseline that will be rendered.</summary>
    private static string RealPng()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.SetPixel(0, 0, new SKColor(0x33, 0x66, 0x99, 0xFF));
        return Lightbox.Raster.PngCodec.Encode(bitmap);
    }

    [AvaloniaFact]
    public void ExposureEditing_FromCells_ExtendsAndClears()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.BeginStroke(50, 50, 1);
        vm.EndStroke();

        vm.ExtendExposureAt(Cell(vm, -1, 0));
        Assert.Equal(2, vm.Doc.Scene.FrameCount);
        Assert.Null(vm.PaintLayer().Cels[1].Frame); // the new hold

        vm.ClearCelAt(Cell(vm, -1, 0));
        Assert.Null(vm.PaintLayer().Cels[0].Frame);

        vm.UndoCommand.Execute(null); // clear-cel is one undo step
        Assert.NotNull(vm.PaintLayer().Cels[0].Frame);
    }
}
