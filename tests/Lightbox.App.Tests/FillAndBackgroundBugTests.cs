using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Four things that were wrong when an artist actually used the tools: a
/// second fill vanishing under the first, a fill vanishing under an eraser,
/// a startup document with no paper, and a brush ring that stopped following
/// the size slider.
/// </summary>
[Collection("BrushState")]
public class FillAndBackgroundBugTests : BrushStateIsolated
{
    private static MainViewModel Vm() => new(null)
    {
        SmoothStrokes = false,
        ActiveTool = ToolId.Fill,
        BrushSize = 30,
        BrushHardness = 1,
        AntiAliasing = false,
    };

    private static List<Stroke> Strokes(MainViewModel vm) =>
        ((Frame)vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[0].Frame!).Strokes;

    /// <summary>What the active layer alone renders to — no paper underneath.</summary>
    private static SKColor LayerPixel(MainViewModel vm, int x, int y)
    {
        var scene = vm.Doc.Scene;
        using var bmp = FrameRasterizer.Rasterize(Strokes(vm), scene.Width, scene.Height);
        return bmp.GetPixel(x, y);
    }

    [AvaloniaFact]
    public void ASecondFillInADifferentColourReplacesTheFirst()
    {
        var vm = Vm();
        vm.ColorHex = "#ff0000";
        vm.FillAt(50, 50);
        Assert.Equal(SKColors.Red, LayerPixel(vm, 50, 50));

        vm.ColorHex = "#0000ff";
        vm.FillAt(50, 50);

        Assert.Equal(2, Strokes(vm).Count);
        Assert.Equal(SKColors.Blue, LayerPixel(vm, 50, 50));
    }

    [AvaloniaFact]
    public void AFillStillTucksUnderTheLineWork()
    {
        // The point of "below line work": the fill must not cover the lines
        // it was flooded up against.
        var vm = Vm();
        vm.ColorHex = "#000000";
        vm.ActiveTool = ToolId.Brush;
        vm.BeginStroke(20, 10, 1);
        vm.MoveStroke(20, 90, 1);
        vm.EndStroke();

        vm.ActiveTool = ToolId.Fill;
        vm.ColorHex = "#ff0000";
        vm.FillAt(60, 50);

        Assert.Equal(0, Strokes(vm).FindIndex(s => s.Tool == ToolKind.Fill));
        Assert.Equal(SKColors.Black, LayerPixel(vm, 20, 50));
    }

    [AvaloniaFact]
    public void AFillAfterErasingIsNotSwallowedByTheEraser()
    {
        // An eraser removes what was there when it ran. Slipping later content
        // underneath it makes it retroactively delete something that never
        // existed — the artist erases, fills the hole, and nothing happens.
        var vm = Vm();
        vm.ColorHex = "#000000";
        vm.ActiveTool = ToolId.Brush;
        vm.BeginStroke(20, 50, 1);
        vm.MoveStroke(80, 50, 1);
        vm.EndStroke();

        vm.ActiveTool = ToolId.Eraser;
        vm.BrushSize = 30;
        vm.BeginStroke(50, 50, 1);
        vm.MoveStroke(52, 50, 1);
        vm.EndStroke();
        Assert.Equal(0, LayerPixel(vm, 50, 50).Alpha); // erasing left transparency

        vm.ActiveTool = ToolId.Fill;
        vm.ColorHex = "#ff0000";
        vm.FillAt(50, 50);

        var filled = LayerPixel(vm, 50, 50);
        Assert.True(filled.Alpha > 200, $"the fill was erased away — alpha {filled.Alpha}");
        Assert.Equal(SKColors.Red, filled);
    }

    [AvaloniaFact]
    public void ErasingProducesTransparencyNotPaper()
    {
        var vm = Vm();
        vm.ColorHex = "#000000";
        vm.ActiveTool = ToolId.Brush;
        vm.BeginStroke(20, 50, 1);
        vm.MoveStroke(80, 50, 1);
        vm.EndStroke();

        vm.ActiveTool = ToolId.Eraser;
        vm.BeginStroke(50, 50, 1);
        vm.MoveStroke(52, 50, 1);
        vm.EndStroke();

        Assert.Equal(0, LayerPixel(vm, 50, 50).Alpha);
    }

    [AvaloniaFact]
    public void TheStartupDocumentOpensOnPaper()
    {
        // The app's first document went through CreateDoc() with no paper
        // colour, so it opened transparent: checkerboard on the canvas and in
        // the layer thumbnail, with nothing named Background.
        var vm = new MainViewModel(null);
        var paper = vm.Doc.Scene.Layers[0];

        Assert.True(paper.IsBackground);
        Assert.Equal("Background", paper.Name);
        Assert.True(paper.Locked);
        Assert.False(vm.Doc.Scene.TransparentBackground);

        // And it is the paper colour, not an empty layer that merely says so.
        using var bmp = FrameRasterizer.Rasterize(
            ((Frame)paper.Cels[0].Frame!).Strokes, vm.Doc.Scene.Width, vm.Doc.Scene.Height);
        var pixel = bmp.GetPixel(vm.Doc.Scene.Width / 2, vm.Doc.Scene.Height / 2);
        Assert.Equal(255, pixel.Alpha);
        Assert.Equal(SKColors.White, pixel);
    }

    [AvaloniaFact]
    public void TheStartupDocumentLandsOnAPaintableLayer()
    {
        var vm = new MainViewModel(null);
        Assert.False(vm.Doc.Scene.Layers[vm.ActiveLayerIndex].IsBackground);
        Assert.False(vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Locked);
    }

    [AvaloniaFact]
    public void ATransparentDocumentHasNoBackgroundLayer()
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings("Sprite", 128, 128, 12, 72, "#ffffff", true));

        var only = Assert.Single(vm.Doc.Scene.Layers);
        Assert.False(only.IsBackground);
        Assert.False(only.Locked);
        Assert.True(vm.Doc.Scene.TransparentBackground);
    }

    [AvaloniaFact]
    public void TheBrushRingFollowsTheSizeSlider()
    {
        var vm = new MainViewModel(null) { BrushSize = 20 };
        var seen = new List<string>();
        vm.PropertyChanged += (_, e) => seen.Add(e.PropertyName ?? "");

        vm.BrushSize = 120;

        Assert.Contains(nameof(MainViewModel.BrushCursorDiameter), seen);
        Assert.Equal(120, vm.BrushCursorDiameter, 1);
    }
}
