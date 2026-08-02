using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// A document's paper is a real, editable layer rather than a render
/// setting. That is the whole point: erasing it must reveal transparency
/// instead of a colour the renderer keeps putting back.
/// </summary>
/// <remarks>
/// In the <c>BrushState</c> collection because it sets brush parameters, and
/// those live in a process-wide store: running beside a test that assumes
/// defaults hands it this one’s brush. It opted out until a CI run caught
/// it — the live-preview pixel check went red on a loaded machine and was
/// green on every local run, which is what this collection exists to stop.
/// </remarks>
[Collection("BrushState")]
public class BackgroundLayerTests : BrushStateIsolated
{
    private static MainViewModel WithPaper(string hex = "#204080")
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.NewDocument(new NewDocumentSettings("Paper", 120, 80, 12, 72, hex, TransparentBackground: false));
        return vm;
    }

    private static SKBitmap Composed(MainViewModel vm)
    {
        RenderSnapshot? last = null;
        vm.SnapshotChanged += s => last = s;
        vm.PublishSnapshot();
        return SKBitmap.FromImage(last!.Image);
    }

    [AvaloniaFact]
    public void ANewPaperDocument_GetsALockedBackgroundLayerBelowThePaintLayer()
    {
        var vm = WithPaper();
        var layers = vm.Doc.Scene.Layers;

        Assert.Equal(2, layers.Count);
        Assert.True(layers[0].IsBackground);
        Assert.Equal("Background", layers[0].Name);
        Assert.True(layers[0].Locked);
        // Bottom of the stack, so everything paints over it.
        Assert.False(layers[1].IsBackground);
        // And the artist lands on the paintable layer, not the paper.
        Assert.False(vm.Doc.Scene.Layers[vm.ActiveLayerIndex].IsBackground);
    }

    [AvaloniaFact]
    public void ThePaperColourComesOutInTheComposite()
    {
        var vm = WithPaper("#204080");
        using var bmp = Composed(vm);
        var px = bmp.GetPixel(60, 40);
        Assert.Equal(0x20, px.Red);
        Assert.Equal(0x40, px.Green);
        Assert.Equal(0x80, px.Blue);
        Assert.Equal(255, px.Alpha);
    }

    [AvaloniaFact]
    public void ATransparentDocument_HasNoBackgroundLayer_AndStaysTransparent()
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings("Alpha", 120, 80, 12, 72, "#ffffff", TransparentBackground: true));

        Assert.DoesNotContain(vm.Doc.Scene.Layers, l => l.IsBackground);
        using var bmp = Composed(vm);
        Assert.Equal(0, bmp.GetPixel(60, 40).Alpha);
    }

    [AvaloniaFact]
    public void TheBackgroundLayerRefusesEditsUntilUnlocked()
    {
        var vm = WithPaper();
        var background = vm.Doc.Scene.Layers[0];
        vm.ActiveLayerIndex = 0;

        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(80, 60, 1);
        vm.EndStroke();

        Assert.Single(((PaintedFrame)background.Cels[0].Frame!).Strokes); // just the paper fill
        Assert.Contains("locked", vm.AiStatus);

        vm.SetLayerLocked(background, false);
        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(80, 60, 1);
        vm.EndStroke();

        Assert.Equal(2, ((PaintedFrame)background.Cels[0].Frame!).Strokes.Count);
    }

    [AvaloniaFact]
    public void ErasingTheUnlockedBackground_RevealsRealTransparency()
    {
        // The point of making the paper a layer: with a scene-level fill the
        // renderer would keep putting the colour back underneath.
        var vm = WithPaper();
        var background = vm.Doc.Scene.Layers[0];
        vm.SetLayerLocked(background, false);
        vm.ActiveLayerIndex = 0;
        vm.ActiveTool = ToolId.Eraser;
        vm.BrushSize = 40;

        vm.BeginStroke(60, 40, 1);
        vm.MoveStroke(62, 42, 1);
        vm.EndStroke();

        using var bmp = Composed(vm);
        Assert.True(bmp.GetPixel(60, 40).Alpha < 40,
            "erasing the background layer should expose transparency, not the scene colour");
    }

    [AvaloniaFact]
    public void ADocumentSavedBeforeBackgroundLayersExisted_StillOpensOnItsPaper()
    {
        // Legacy files carry the colour on the scene and have no background
        // layer. They must not suddenly open transparent.
        var doc = DocumentFactory.CreateDoc(64, 64, 12);
        doc.Scene.BackgroundColor = "#c08040";
        doc.Scene.TransparentBackground = false;

        Assert.Equal(
            BrushEngineColour("#c08040"),
            SceneRenderer.BackgroundOf(doc.Scene));

        static SKColor BrushEngineColour(string hex) => Lightbox.Raster.BrushEngine.ParseColor(hex);
    }

    [AvaloniaFact]
    public void ThePaperIsAStrokeRecord_NotBakedPixels()
    {
        // Invariant 1: the record is the document. A baked PNG baseline would
        // be pixels the record cannot regenerate.
        var vm = WithPaper("#336699");
        var frame = (PaintedFrame)vm.Doc.Scene.Layers[0].Cels[0].Frame!; // the paper itself

        Assert.True(string.IsNullOrEmpty(frame.PngBase64));
        var fill = Assert.Single(frame.Strokes);
        Assert.Equal(ToolKind.Fill, fill.Tool);
        Assert.Equal("#336699", fill.Color);
        Assert.Equal(4, fill.Points.Count);
    }
}
