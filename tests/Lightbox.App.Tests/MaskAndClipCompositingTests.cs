using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Layer masks and clipping masks (Q147, Q148) at the two seams they were
/// built on: <see cref="PassShape"/> carving a pass in the compositor, and
/// <see cref="LayerShapes"/> describing which frames carve which layer.
/// </summary>
public class MaskAndClipCompositingTests(ITestOutputHelper output)
{
    // ---- the compositor: shapes carve pixels --------------------------------

    private static SKBitmap Solid(SKColor color, int size = 8)
    {
        var bmp = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(color);
        return bmp;
    }

    /// <summary>Opaque on the left half, fully transparent on the right.</summary>
    private static SKBitmap LeftHalf(int size = 8)
    {
        var bmp = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(SKColors.Transparent);
        using var canvas = new SKCanvas(bmp);
        using var paint = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(SKRect.Create(0, 0, size / 2f, size), paint);
        canvas.Flush();
        return bmp;
    }

    [AvaloniaFact]
    public void AShapeCarvesThePassToItsCoverage()
    {
        using var bottom = Solid(SKColors.Red);
        using var top = Solid(SKColors.Blue);
        using var mask = LeftHalf();
        using var image = SceneRenderer.Compose(8, 8,
        [
            new RenderPass(bottom, null, 1),
            new RenderPass(top, null, 1, Shapes: [new PassShape(mask)]),
        ], SKColors.White);
        using var bmp = SKBitmap.FromImage(image);

        var covered = bmp.GetPixel(1, 4);
        var outside = bmp.GetPixel(6, 4);
        output.WriteLine($"covered {covered}, outside {outside}");
        Assert.Equal(SKColors.Blue, covered);
        Assert.Equal(SKColors.Red, outside);
    }

    [AvaloniaFact]
    public void AnInvertedShapeCarvesTheOtherWay()
    {
        using var bottom = Solid(SKColors.Red);
        using var top = Solid(SKColors.Blue);
        using var mask = LeftHalf();
        using var image = SceneRenderer.Compose(8, 8,
        [
            new RenderPass(bottom, null, 1),
            new RenderPass(top, null, 1, Shapes: [new PassShape(mask, Inverted: true)]),
        ], SKColors.White);
        using var bmp = SKBitmap.FromImage(image);

        Assert.Equal(SKColors.Red, bmp.GetPixel(1, 4));
        Assert.Equal(SKColors.Blue, bmp.GetPixel(6, 4));
    }

    [AvaloniaFact]
    public void OpacityAndBlendApplyToTheCarvedResultOnce()
    {
        // Grey multiply over grey, carved to the left half: the covered side
        // darkens exactly as an uncarved multiply would, and the uncovered
        // side is untouched — the shape must not leak the layer past itself,
        // and the isolation must not double-apply opacity.
        using var bottom = Solid(new SKColor(128, 128, 128));
        using var top = Solid(new SKColor(128, 128, 128));
        using var mask = LeftHalf();

        using var carved = SceneRenderer.Compose(8, 8,
        [
            new RenderPass(bottom, null, 1),
            new RenderPass(top, null, 1, SKBlendMode.Multiply, Shapes: [new PassShape(mask)]),
        ], SKColors.White);
        using var plain = SceneRenderer.Compose(8, 8,
        [
            new RenderPass(bottom, null, 1),
            new RenderPass(top, null, 1, SKBlendMode.Multiply),
        ], SKColors.White);

        using var carvedBmp = SKBitmap.FromImage(carved);
        using var plainBmp = SKBitmap.FromImage(plain);
        var covered = carvedBmp.GetPixel(1, 4);
        var outside = carvedBmp.GetPixel(6, 4);
        var reference = plainBmp.GetPixel(1, 4);
        output.WriteLine($"covered {covered}, outside {outside}, uncarved multiply {reference}");
        Assert.Equal(reference, covered);
        Assert.Equal(128, outside.Red);
    }

    [AvaloniaFact]
    public void TwoShapesIntersect()
    {
        // A clipped-and-masked layer carries both carves; only where both
        // cover does anything survive. Same coverage twice must equal once —
        // the cheap way to prove the chain is an intersection, not a sum.
        using var bottom = Solid(SKColors.Red);
        using var top = Solid(SKColors.Blue);
        using var mask = LeftHalf();
        using var again = LeftHalf();
        using var image = SceneRenderer.Compose(8, 8,
        [
            new RenderPass(bottom, null, 1),
            new RenderPass(top, null, 1,
                Shapes: [new PassShape(mask), new PassShape(again)]),
        ], SKColors.White);
        using var bmp = SKBitmap.FromImage(image);
        Assert.Equal(SKColors.Blue, bmp.GetPixel(1, 4));
        Assert.Equal(SKColors.Red, bmp.GetPixel(6, 4));
    }

    // ---- the description: which frames carve which layer --------------------

    private static Layer LayerWith(string name, int cels = 3)
    {
        var layer = new Layer { Name = name };
        for (var i = 0; i < cels; i++) layer.Cels.Add(new Cel { Frame = new Frame() });
        return layer;
    }

    private static Scene SceneWith(params Layer[] layers)
    {
        var scene = new Scene { Width = 64, Height = 48, FrameCount = 3 };
        scene.Layers.AddRange(layers);
        return scene;
    }

    [Fact]
    public void AnOrdinaryLayerDescribesNoShapes()
    {
        var scene = SceneWith(LayerWith("Paper"), LayerWith("Ink"));
        Assert.Null(LayerShapes.For(scene, 1, 0));
    }

    [Fact]
    public void AMaskedLayerDescribesItsMask()
    {
        var ink = LayerWith("Ink");
        ink.Mask = new LayerMask { Inverted = true };
        var scene = SceneWith(LayerWith("Paper"), ink);

        var shapes = LayerShapes.For(scene, 1, 0);
        var shape = Assert.Single(shapes!);
        Assert.Same(ink.Mask.Frame, shape.Frame);
        Assert.True(shape.Inverted);
    }

    [Fact]
    public void ADisabledMaskDescribesNoShape()
    {
        var ink = LayerWith("Ink");
        ink.Mask = new LayerMask { Disabled = true };
        var scene = SceneWith(LayerWith("Paper"), ink);
        Assert.Null(LayerShapes.For(scene, 1, 0));
    }

    [Fact]
    public void AClippedLayerDescribesItsBaseAndTheBasesMask()
    {
        var fill = LayerWith("Fill");
        fill.Mask = new LayerMask();
        var shade = LayerWith("Shade");
        shade.ClipToBelow = true;
        var scene = SceneWith(fill, shade);

        var shapes = LayerShapes.For(scene, 1, frameIndex: 1);
        Assert.Equal(2, shapes!.Count);
        // The base's exposed drawing first, then the mask carving the base.
        Assert.Same(fill.Cels[1].Frame, shapes[0].Frame);
        Assert.False(shapes[0].Inverted);
        Assert.Same(fill.Mask.Frame, shapes[1].Frame);
    }

    [Fact]
    public void ConsecutiveClippedLayersShareTheFirstUnclippedBase()
    {
        var fill = LayerWith("Fill");
        var shade = LayerWith("Shade");
        shade.ClipToBelow = true;
        var glow = LayerWith("Glow");
        glow.ClipToBelow = true;
        var scene = SceneWith(fill, shade, glow);

        Assert.Same(fill, LayerShapes.BaseOf(scene.Layers, 1));
        Assert.Same(fill, LayerShapes.BaseOf(scene.Layers, 2));
        var shapes = LayerShapes.For(scene, 2, 0);
        Assert.Same(fill.Cels[0].Frame, Assert.Single(shapes!).Frame);
    }

    [Fact]
    public void AClippedLayerAtTheBottomRendersUnclipped()
    {
        var shade = LayerWith("Shade");
        shade.ClipToBelow = true;
        var scene = SceneWith(shade);
        Assert.Null(LayerShapes.For(scene, 0, 0));
    }

    [Fact]
    public void AClippedLayerOverAnEmptyOrHiddenBaseContributesNothing()
    {
        var empty = new Layer { Name = "Fill" };
        empty.Cels.Add(new Cel()); // a hold with nothing before it: no drawing
        var shade = LayerWith("Shade");
        shade.ClipToBelow = true;
        var scene = SceneWith(empty, shade);
        Assert.Empty(LayerShapes.For(scene, 1, 0)!);

        var hidden = LayerWith("Fill");
        hidden.Visible = false;
        var scene2 = SceneWith(hidden, shade);
        Assert.Empty(LayerShapes.For(scene2, 1, 0)!);
    }

    [Fact]
    public void TheDescribedListSkipsAClippedLayerOverNothing()
    {
        var empty = new Layer { Name = "Fill" };
        empty.Cels.Add(new Cel());
        var shade = LayerWith("Shade");
        shade.ClipToBelow = true;
        var scene = SceneWith(empty, shade);

        using var cache = new FrameBitmapCache();
        var state = new ScenePassBuilder.State(
            0, shade.Id, IsPlaying: false, IsLightTable: false, HaveViewport: false,
            new OnionSettings { Enabled = false });
        var plan = ScenePassBuilder.Describe(
            scene, state, cache, new TileFallbackTally(), ScenePassBuilder.LiveEdit.None);

        Assert.Empty(plan.Specs);
    }

    [Fact]
    public void GhostsAreCarvedByTheLayersMask()
    {
        var ink = LayerWith("Ink");
        ink.Mask = new LayerMask();
        var scene = SceneWith(LayerWith("Paper"), ink);

        var state = new ScenePassBuilder.State(
            1, ink.Id, IsPlaying: false, IsLightTable: false, HaveViewport: false,
            new OnionSettings { Enabled = true, Before = 1, After = 1 });
        var ghosts = ScenePassBuilder.GhostSpecsFor(ink, scene, state);

        Assert.NotEmpty(ghosts);
        Assert.All(ghosts, g =>
        {
            var shape = Assert.Single(g.Shapes!);
            Assert.Same(ink.Mask.Frame, shape.Frame);
        });
    }
}
