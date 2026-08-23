using Lightbox.Core.Effects;
using Lightbox.Raster.Effects;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The v1 effect catalogue as pixels: levels and HSL prove the point path,
/// blur proves the kernel path and the reach arithmetic, and an unknown kind
/// proves that a stranger renders as identity rather than as a crash or a
/// drop.
/// </summary>
public class EffectRegistryTests(ITestOutputHelper output)
{
    private static EffectUse Use(string kind, params (string Key, double Value)[] values)
    {
        var use = new EffectUse { Kind = kind };
        foreach (var (key, value) in values) use.Params[key] = new EffectParam(value);
        return use;
    }

    private static EffectStack Stack(params EffectUse[] uses) => new() { Uses = [.. uses] };

    /// <summary>An 8x8 solid, pushed through the stack's filter at a frame.</summary>
    private static SKColor Filtered(EffectStack stack, SKColor input, int frame = 0)
    {
        using var bmp = new SKBitmap(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(input);
        using var outBmp = new SKBitmap(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(outBmp);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { ImageFilter = EffectRegistry.FilterFor(stack, frame) };
        canvas.DrawBitmap(bmp, 0, 0, paint);
        canvas.Flush();
        return outBmp.GetPixel(4, 4);
    }

    [Fact]
    public void AnEmptyOrUnknownStackIsIdentity()
    {
        Assert.Null(EffectRegistry.FilterFor(null, 0));
        Assert.Null(EffectRegistry.FilterFor(Stack(), 0));
        Assert.Null(EffectRegistry.FilterFor(Stack(Use("warp.ripple")), 0));
        Assert.True(EffectRegistry.HasUnknown(Stack(Use("warp.ripple"))));
        Assert.False(EffectRegistry.HasUnknown(Stack(Use("grade.hsl"))));
    }

    [Fact]
    public void ADisabledUseDoesNothing()
    {
        var use = Use("grade.hsl", ("saturation", -100));
        use.Disabled = true;
        Assert.Null(EffectRegistry.FilterFor(Stack(use), 0));
    }

    [Fact]
    public void LevelsGammaLiftsTheMidtones()
    {
        var grey = new SKColor(128, 128, 128);
        var lifted = Filtered(Stack(Use("grade.levels", ("gamma", 2.0))), grey);
        var crushed = Filtered(Stack(Use("grade.levels", ("gamma", 0.5))), grey);
        output.WriteLine($"gamma 2 {lifted}, gamma 0.5 {crushed}");
        Assert.True(lifted.Red > 160, $"gamma 2 should lift 128 well above itself, got {lifted.Red}");
        Assert.True(crushed.Red < 96, $"gamma 0.5 should crush 128 well below itself, got {crushed.Red}");
    }

    [Fact]
    public void LevelsInputBlackClipsShadows()
    {
        var dark = new SKColor(40, 40, 40);
        var clipped = Filtered(Stack(Use("grade.levels", ("inBlack", 64.0))), dark);
        Assert.Equal(0, clipped.Red);
        Assert.Equal(255, clipped.Alpha); // alpha untouched by a grade
    }

    [Fact]
    public void FullDesaturationIsGreyAndHueRotationIsNot()
    {
        var red = new SKColor(200, 40, 40);
        var grey = Filtered(Stack(Use("grade.hsl", ("saturation", -100.0))), red);
        output.WriteLine($"desaturated {grey}");
        Assert.True(Math.Abs(grey.Red - grey.Green) <= 2 && Math.Abs(grey.Green - grey.Blue) <= 2,
            $"saturation -100 must be neutral, got {grey}");

        var spun = Filtered(Stack(Use("grade.hsl", ("hue", 120.0))), red);
        output.WriteLine($"hue+120 {spun}");
        Assert.True(spun.Green > spun.Red, $"a red spun +120 leans green, got {spun}");
    }

    [Fact]
    public void BlurSpreadsInkAndItsReachFollowsTheRadius()
    {
        // One bright pixel in the middle; after a blur its neighbours carry
        // some of it — measured off-centre, so this cannot pass by identity.
        using var bmp = new SKBitmap(17, 17, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(SKColors.Transparent);
        bmp.SetPixel(8, 8, SKColors.White);
        using var outBmp = new SKBitmap(17, 17, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(outBmp);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint
        {
            ImageFilter = EffectRegistry.FilterFor(Stack(Use("blur.gaussian", ("radius", 6.0))), 0),
        };
        canvas.DrawBitmap(bmp, 0, 0, paint);
        canvas.Flush();

        var centre = outBmp.GetPixel(8, 8);
        var near = outBmp.GetPixel(10, 8);
        output.WriteLine($"centre {centre.Alpha}, two off {near.Alpha}");
        Assert.True(centre.Alpha < 255, "the point should have spread");
        Assert.True(near.Alpha > 0, "the neighbourhood should have received it");

        Assert.Equal(9, EffectRegistry.ReachOf(Stack(Use("blur.gaussian", ("radius", 6.0))), 0));
        Assert.Equal(0, EffectRegistry.ReachOf(Stack(Use("grade.hsl")), 0));
    }

    [Fact]
    public void AKeyedRadiusEvaluatesPerFrame()
    {
        var use = Use("blur.gaussian");
        use.Params["radius"] = new EffectParam
        {
            Keys =
            [
                new EffectKey { Frame = 0, Value = 0, Ease = Lightbox.Core.Inbetween.Easing.Linear },
                new EffectKey { Frame = 10, Value = 8 },
            ],
        };
        var stack = Stack(use);

        // Radius 0 at frame 0: the chain drops the blur entirely.
        Assert.Null(EffectRegistry.FilterFor(stack, 0));
        Assert.NotNull(EffectRegistry.FilterFor(stack, 10));
        Assert.Equal(0, EffectRegistry.ReachOf(stack, 0));
        Assert.Equal(12, EffectRegistry.ReachOf(stack, 10));
    }

    [Fact]
    public void TheChainAppliesInStackOrder()
    {
        // Compress the range, then oversaturate — against the reverse. The
        // saturation boost clamps at 255 only when it runs on the *unclamped*
        // input, so the two orders land on measurably different reds; a chain
        // that ignored stack order could not tell them apart.
        var red = new SKColor(200, 40, 40);
        var compressThenSat = Filtered(Stack(
            Use("grade.levels", ("outWhite", 100.0)),
            Use("grade.hsl", ("saturation", 100.0))), red);
        var satThenCompress = Filtered(Stack(
            Use("grade.hsl", ("saturation", 100.0)),
            Use("grade.levels", ("outWhite", 100.0))), red);
        output.WriteLine($"compress→sat {compressThenSat}, sat→compress {satThenCompress}");
        Assert.True(compressThenSat.Red > satThenCompress.Red + 5,
            $"order must be observable: {compressThenSat} vs {satThenCompress}");
    }
}
