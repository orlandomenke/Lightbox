using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.Core.Effects;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The budget DESIGN-effects.md asks for the day the pass lands: what an
/// effect costs per composite, deliberately loose — it catches an
/// order-of-magnitude regression (an accidental full-resolution round trip
/// per pass, a filter rebuilt per pixel), never drift. The layer-effect
/// output cache is still open on the roadmap; when it lands, the blur ratio
/// here is the number it should visibly move.
/// </summary>
public class EffectComposeCostTests(ITestOutputHelper output)
{
    private const int W = 960;
    private const int H = 540;

    private static EffectStack Stack(string kind, params (string Key, double Value)[] values)
    {
        var use = new EffectUse { Kind = kind };
        foreach (var (key, value) in values) use.Params[key] = new EffectParam(value);
        return new EffectStack { Uses = [use] };
    }

    private static double MsPerCompose(List<RenderPass> passes, int rounds = 20)
    {
        // One warm round for Skia's own lazies, then the measured ones.
        SceneRenderer.Compose(W, H, passes, SKColors.White).Dispose();
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < rounds; i++)
        {
            SceneRenderer.Compose(W, H, passes, SKColors.White).Dispose();
        }
        return clock.Elapsed.TotalMilliseconds / rounds;
    }

    [AvaloniaFact]
    [Trait("Category", "Performance")]
    public void AnAdjustmentPassCostsItsFilterAndNotAnOrderMore()
    {
        using var bottom = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        bottom.Erase(new SKColor(180, 90, 60));
        var plain = new List<RenderPass> { new(bottom, null, 1) };
        var graded = new List<RenderPass>
        {
            new(bottom, null, 1),
            new(null, null, 1, AdjustStack: Stack("grade.hsl", ("saturation", -60.0))),
        };
        var blurred = new List<RenderPass>
        {
            new(bottom, null, 1),
            new(null, null, 1, AdjustStack: Stack("blur.gaussian", ("radius", 8.0))),
        };
        var grained = new List<RenderPass>
        {
            new(bottom, null, 1),
            new(null, null, 1, AdjustStack: Stack("grade.grain", ("amount", 40.0))),
        };

        var baseline = MsPerCompose(plain);
        var grade = MsPerCompose(graded);
        var blur = MsPerCompose(blurred);
        var grain = MsPerCompose(grained);
        output.WriteLine(
            $"plain {baseline:F2} ms, +hsl {grade:F2} ms ({grade / baseline:F1}x), "
            + $"+blur {blur:F2} ms ({blur / baseline:F1}x), +grain {grain:F2} ms ({grain / baseline:F1}x)");

        // Loose on purpose: a point grade is one extra full-surface draw
        // through a color filter, a blur is a kernel over the surface. The
        // ceilings are set an order of magnitude above what a healthy run
        // measures, so only a structural regression trips them.
        Assert.True(grade < baseline * 40 + 30,
            $"a point adjustment should cost about one extra surface draw, got {grade:F2} ms over {baseline:F2} ms");
        Assert.True(blur < baseline * 150 + 120,
            $"a blur adjustment should cost its kernel and nothing structural, got {blur:F2} ms over {baseline:F2} ms");
        // Grain is a per-pixel CPU loop with two hashes in it — the same
        // shape as the true-HSL pass beside it, and held to the same ceiling.
        Assert.True(grain < baseline * 40 + 30,
            $"film grain should cost about one pass over the pixels, got {grain:F2} ms over {baseline:F2} ms");
    }

    [AvaloniaFact]
    [Trait("Category", "Performance")]
    public void AStyledPassCostsItsChainAndNotAnOrderMore()
    {
        // The whole style set on one layer at once — shadow, both glows,
        // stroke and bevel are each a small filter graph over the pass, and
        // five of them are still bounded work, not a structural round trip.
        using var content = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        content.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(content))
        using (var paint = new SKPaint { Color = SKColors.White })
        {
            canvas.DrawRect(SKRect.Create(W / 4, H / 4, W / 2, H / 2), paint);
        }
        var stack = new EffectStack
        {
            Uses =
            [
                new EffectUse { Kind = "style.dropShadow" },
                new EffectUse { Kind = "style.outerGlow" },
                new EffectUse { Kind = "style.innerGlow" },
                new EffectUse { Kind = "style.stroke" },
                new EffectUse { Kind = "style.bevel" },
            ],
        };
        var plain = new List<RenderPass> { new(content, null, 1) };
        var styled = new List<RenderPass>
        {
            new(content, null, 1,
                Style: Lightbox.Raster.Effects.EffectRegistry.StyleFor(stack, 0)),
        };

        var baseline = MsPerCompose(plain);
        var five = MsPerCompose(styled);
        output.WriteLine(
            $"plain {baseline:F2} ms, +5 styles {five:F2} ms ({five / baseline:F1}x)");
        Assert.True(five < baseline * 150 + 120,
            $"five styles are five bounded graphs, got {five:F2} ms over {baseline:F2} ms");
    }
}
