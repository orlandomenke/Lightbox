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
    public void AHueSpinKeepsASaturatedFlatVivid()
    {
        // The art-director's veto, as a regression test: the affine
        // hue-rotation matrix clipped pure red +120° to (0,146,0) — duller
        // and paler than the colour it started as, on exactly the cel flat
        // an animator turns this control on. A true HSL rotation lands on
        // green at full strength.
        var spun = Filtered(Stack(Use("grade.hsl", ("hue", 120.0))), new SKColor(255, 0, 0));
        output.WriteLine($"pure red +120 {spun}");
        Assert.True(spun.Green > 240, $"the spin must stay vivid, got {spun}");
        Assert.True(spun.Red < 15 && spun.Blue < 15, $"and stay on the wheel, got {spun}");

        // Half a turn: red to full cyan, not a 60%-bright one.
        var half = Filtered(Stack(Use("grade.hsl", ("hue", 180.0))), new SKColor(255, 0, 0));
        output.WriteLine($"pure red +180 {half}");
        Assert.True(half.Green > 240 && half.Blue > 240 && half.Red < 15,
            $"half a turn is vivid cyan, got {half}");
    }

    [Fact]
    public void TheHslFilterPreservesAlpha()
    {
        // Pins the premultiplication convention: a half-transparent red,
        // desaturated, is a half-transparent grey — same alpha, neutral
        // colour, no channel blown by handling premul as straight or back.
        using var bmp = new SKBitmap(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(200, 40, 40, 128));
        using var outBmp = new SKBitmap(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(outBmp);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint
        {
            ImageFilter = EffectRegistry.FilterFor(
                Stack(Use("grade.hsl", ("saturation", -100.0))), 0),
        };
        canvas.DrawBitmap(bmp, 0, 0, paint);
        canvas.Flush();
        var pixel = outBmp.GetPixel(4, 4);
        output.WriteLine($"half-transparent desaturated {pixel}");
        Assert.InRange(pixel.Alpha, 126, 130);
        Assert.True(Math.Abs(pixel.Red - pixel.Green) <= 3 && Math.Abs(pixel.Green - pixel.Blue) <= 3,
            $"desaturated must be neutral, got {pixel}");
        Assert.InRange(pixel.Red, 60, 190); // present, not blown or vanished
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
    public void AStaticStackAnswersWithTheSameFilterInstance()
    {
        // The publish path asks per pointer event; a stack nobody is editing
        // must not allocate a chain per ask (the leak review's finding) —
        // and a changed value must.
        var use = Use("blur.gaussian", ("radius", 6.0));
        var stack = Stack(use);
        var first = EffectRegistry.FilterFor(stack, 0);
        Assert.Same(first, EffectRegistry.FilterFor(stack, 0));

        use.Params["radius"] = new EffectParam(9);
        var changed = EffectRegistry.FilterFor(stack, 0);
        Assert.NotSame(first, changed);
        Assert.Same(changed, EffectRegistry.FilterFor(stack, 0));
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
        // Lightness clamps, so the same two offsets in opposite orders end
        // at opposite poles: up-then-down pins at white first and comes back
        // to black; down-then-up pins at black first and comes back to
        // white. A chain that ignored stack order could not tell them apart.
        var red = new SKColor(200, 40, 40);
        var upThenDown = Filtered(Stack(
            Use("grade.hsl", ("lightness", 100.0)),
            Use("grade.hsl", ("lightness", -100.0))), red);
        var downThenUp = Filtered(Stack(
            Use("grade.hsl", ("lightness", -100.0)),
            Use("grade.hsl", ("lightness", 100.0))), red);
        output.WriteLine($"up→down {upThenDown}, down→up {downThenUp}");
        Assert.True(upThenDown.Red < 10, $"white then fully darkened is black, got {upThenDown}");
        Assert.True(downThenUp.Red > 245, $"black then fully lifted is white, got {downThenUp}");
    }
}
