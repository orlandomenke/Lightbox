using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Effects;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The effect seam (DESIGN-effects.md, Q151): a self stack filters its own
/// pass, an adjustment pass filters the composite beneath it — carved by
/// shapes, faded by opacity — and the pass list says all of it.
/// </summary>
public class EffectPassTests(ITestOutputHelper output)
{
    private static SKBitmap Solid(SKColor color, int size = 8)
    {
        var bmp = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(color);
        return bmp;
    }

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

    private static EffectStack Desaturate() => new()
    {
        Uses = [new EffectUse
        {
            Kind = "grade.hsl",
            Params = { ["saturation"] = new EffectParam(-100) },
        }],
    };

    private static bool Neutral(SKColor c) =>
        Math.Abs(c.Red - c.Green) <= 2 && Math.Abs(c.Green - c.Blue) <= 2;

    [AvaloniaFact]
    public void AnAdjustmentPassFiltersTheBackdrop()
    {
        using var bottom = Solid(SKColors.Red);
        using var image = SceneRenderer.Compose(8, 8,
        [
            new RenderPass(bottom, null, 1),
            new RenderPass(null, null, 1, AdjustStack: Desaturate()),
        ], SKColors.White);
        using var bmp = SKBitmap.FromImage(image);
        var pixel = bmp.GetPixel(4, 4);
        output.WriteLine($"adjusted {pixel}");
        Assert.True(Neutral(pixel), $"a full desaturation must be neutral, got {pixel}");
    }

    [AvaloniaFact]
    public void AnAdjustmentIsCarvedByItsShapesAndFadedByItsOpacity()
    {
        using var bottom = Solid(SKColors.Red);
        using var mask = LeftHalf();
        using var image = SceneRenderer.Compose(8, 8,
        [
            new RenderPass(bottom, null, 1),
            new RenderPass(null, null, 1, Shapes: [new PassShape(mask)],
                AdjustStack: Desaturate()),
        ], SKColors.White);
        using var bmp = SKBitmap.FromImage(image);
        Assert.True(Neutral(bmp.GetPixel(1, 4)), "covered side is adjusted");
        Assert.Equal(SKColors.Red, bmp.GetPixel(6, 4));

        using var half = SceneRenderer.Compose(8, 8,
        [
            new RenderPass(bottom, null, 1),
            new RenderPass(null, null, 0.5, AdjustStack: Desaturate()),
        ], SKColors.White);
        using var halfBmp = SKBitmap.FromImage(half);
        var faded = halfBmp.GetPixel(4, 4);
        output.WriteLine($"half-strength {faded}");
        Assert.True(faded.Red > faded.Green + 20 && faded.Red < 250,
            $"half strength sits between red and grey, got {faded}");
    }

    [AvaloniaFact]
    public void AnEmptyAdjustmentStackDrawsNothing()
    {
        using var bottom = Solid(SKColors.Red);
        using var image = SceneRenderer.Compose(8, 8,
        [
            new RenderPass(bottom, null, 1),
            new RenderPass(null, null, 1, AdjustStack: new EffectStack()),
        ], SKColors.White);
        using var bmp = SKBitmap.FromImage(image);
        Assert.Equal(SKColors.Red, bmp.GetPixel(4, 4));
    }

    [AvaloniaFact]
    public void ASelfEffectFiltersOnlyItsOwnPass()
    {
        using var bottom = Solid(SKColors.Red);
        using var top = LeftHalf(); // white left half as "content"
        // A native grade: the self path composes only native uses (a CPU
        // pass like HSL is backdrop-only), so levels is the representative.
        using var filter = Lightbox.Raster.Effects.EffectRegistry.FilterFor(
            new EffectStack
            {
                Uses = [new EffectUse
                {
                    Kind = "grade.levels",
                    Params = { ["outWhite"] = new EffectParam(0) },
                }],
            }, 0);
        using var image = SceneRenderer.Compose(8, 8,
        [
            new RenderPass(bottom, null, 1),
            new RenderPass(top, null, 1, Effect: filter),
        ], SKColors.White);
        using var bmp = SKBitmap.FromImage(image);
        var covered = bmp.GetPixel(1, 4);
        var outside = bmp.GetPixel(6, 4);
        output.WriteLine($"darkened content {covered}, untouched backdrop {outside}");
        Assert.True(covered.Red < 40 && covered.Green < 40, "the content went black");
        Assert.Equal(SKColors.Red, outside); // the backdrop never met the filter
    }

    [AvaloniaFact]
    public void AStyleDecoratesTheCarvedSilhouetteNotTheUnmaskedContent()
    {
        // Q155: content spans x 0..16, the mask keeps x 0..8, and the glow
        // must hug the *carved* edge at 8 — present just past it, absent
        // where only the unmasked content's edge at 16 would have glowed.
        var content = new SKBitmap(24, 24, SKColorType.Rgba8888, SKAlphaType.Premul);
        content.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(content))
        using (var paint = new SKPaint { Color = SKColors.White })
        {
            canvas.DrawRect(SKRect.Create(0, 0, 16, 24), paint);
        }
        var mask = new SKBitmap(24, 24, SKColorType.Rgba8888, SKAlphaType.Premul);
        mask.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(mask))
        using (var paint = new SKPaint { Color = SKColors.White })
        {
            canvas.DrawRect(SKRect.Create(0, 0, 8, 24), paint);
        }

        var glow = new EffectStack
        {
            Uses = [new EffectUse
            {
                Kind = "style.outerGlow",
                Params =
                {
                    ["size"] = new EffectParam(6),
                    ["opacity"] = new EffectParam(100),
                },
            }],
        };
        using var image = SceneRenderer.Compose(24, 24,
        [
            new RenderPass(content, null, 1, Shapes: [new PassShape(mask)],
                Style: Lightbox.Raster.Effects.EffectRegistry.StyleFor(glow, 0)),
        ], SKColors.Transparent);
        using var bmp = SKBitmap.FromImage(image);

        var kept = bmp.GetPixel(4, 12);
        var pastCarve = bmp.GetPixel(11, 12);
        var pastOldEdge = bmp.GetPixel(20, 12);
        output.WriteLine(
            $"kept a={kept.Alpha}, past carve a={pastCarve.Alpha}, past old edge a={pastOldEdge.Alpha}");
        Assert.Equal(255, kept.Alpha);
        Assert.True(pastCarve.Alpha > 20, "the glow follows the carved edge");
        Assert.Equal(0, pastOldEdge.Alpha); // the unmasked edge never glowed
        content.Dispose();
        mask.Dispose();
    }

    // ---- the description ---------------------------------------------------

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

    private static ScenePassBuilder.Plan Describe(Scene scene, Layer active, int frame = 0) =>
        ScenePassBuilder.Describe(
            scene,
            new ScenePassBuilder.State(
                frame, active.Id, IsPlaying: false, IsLightTable: false, HaveViewport: false,
                new OnionSettings { Enabled = false }),
            new FrameBitmapCache(), new TileFallbackTally(), ScenePassBuilder.LiveEdit.None);

    [Fact]
    public void AWiggleBoilsWhileTheDrawingHolds()
    {
        // The reason the animation shelf exists (Q159): a drawing held for
        // three frames should still move, and it only can if the effect is
        // evaluated at the *playhead* rather than at the drawing's own index.
        var ink = LayerWith("Ink");
        ink.Cels[1] = new Cel();
        ink.Cels[2] = new Cel(); // one drawing, held across all three frames
        ink.Effects = new EffectStack
        {
            Uses = [new EffectUse
            {
                Kind = "anim.wiggle",
                Params = { ["amount"] = new EffectParam(8), ["hold"] = new EffectParam(1) },
            }],
        };
        var scene = SceneWith(ink);

        var atZero = Describe(scene, ink, 0).Specs[0];
        var atTwo = Describe(scene, ink, 2).Specs[0];
        Assert.Equal(0, atZero.CelIndex);
        Assert.Equal(2, atTwo.CelIndex);

        // And the seam every hand-built compositor uses answers per frame.
        var first = EffectPasses.SelfFilter(ink, 0);
        Assert.NotNull(first);
        Assert.Same(first, EffectPasses.SelfFilter(ink, 0));
        Assert.NotSame(first, EffectPasses.SelfFilter(ink, 2));
    }

    [Fact]
    public void AnAdjustmentLayerDescribesOneBackdropPassAndNoCelFetch()
    {
        var ink = LayerWith("Ink");
        var grade = new Layer { Name = "Grade", Adjusts = true, Effects = Desaturate() };
        var scene = SceneWith(ink, grade);

        var plan = Describe(scene, ink);

        Assert.Equal(2, plan.Specs.Count);
        var adj = plan.Specs[1];
        Assert.True(adj.Adjusts);
        Assert.Null(adj.CelFrame);
        Assert.Same(grade.Effects, adj.Fx);
    }

    [Fact]
    public void AnAdjustmentWithNothingLiveDescribesNothing()
    {
        var ink = LayerWith("Ink");
        var idle = new Layer { Name = "Grade", Adjusts = true }; // no stack at all
        var off = new Layer { Name = "Off", Adjusts = true, Effects = Desaturate() };
        off.Effects!.Uses[0].Disabled = true;
        var scene = SceneWith(ink, idle, off);

        Assert.Single(Describe(scene, ink).Specs);
    }

    [Fact]
    public void TheSceneStackDescribesALastPass()
    {
        var ink = LayerWith("Ink");
        var scene = SceneWith(ink);
        scene.Effects = Desaturate();

        var plan = Describe(scene, ink);
        Assert.Equal(2, plan.Specs.Count);
        Assert.True(plan.Specs[^1].Adjusts);
        Assert.Same(scene.Effects, plan.Specs[^1].Fx);
    }

    [Fact]
    public void ALiveEffectAnywhereRefusesTheTilePath()
    {
        var frame = new Frame();
        Assert.Equal(
            TileFallbackReason.Effects,
            TileFallback.Reason(
                frame, hasCamera: false, haveViewport: true, liveEffectHere: false,
                posed: false, shaped: false, docEffects: true));
    }
}
