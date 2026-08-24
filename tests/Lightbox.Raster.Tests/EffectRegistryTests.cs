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

    /// <summary>
    /// <see cref="Use"/> with the id pinned, for a test that asserts on the
    /// motion itself rather than on the fact that there is some.
    /// </summary>
    /// <remarks>
    /// A per-use effect seeds from its use's id (Q159), and <c>Ids.NewId</c>
    /// mixes the wall clock — so an <em>unsaved</em> use re-rolls its own motion
    /// on every run. A saved document never does: the id is written into the
    /// file once and never changes, which is what makes the seed "stable
    /// forever" as Q159 says. Pinning it here buys the test the same stability
    /// a real document already has (B306).
    /// </remarks>
    private static EffectUse SeededUse(string id, string kind, params (string Key, double Value)[] values)
    {
        var use = Use(kind, values);
        use.Id = id;
        return use;
    }

    private static EffectStack Stack(params EffectUse[] uses) => new() { Uses = [.. uses] };

    /// <summary>
    /// An 8x8 solid, pushed through the stack the way the backdrop path runs
    /// it — the program, native segments and CPU passes alike.
    /// </summary>
    private static SKColor Filtered(EffectStack stack, SKColor input, int frame = 0)
    {
        var bmp = new SKBitmap(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(input);
        var program = EffectRegistry.ProgramFor(stack, frame);
        if (program is null)
        {
            bmp.Dispose();
            return input;
        }
        var processed = EffectRegistry.ApplyTo(bmp, program);
        var pixel = processed.GetPixel(4, 4);
        processed.Dispose();
        return pixel;
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
    public void TheStackMasterSwitchSilencesEveryChainAndTheCacheFollows()
    {
        // Q158: one switch over the whole stack — self chain, style chain,
        // backdrop program and reach all go quiet, and come back rebuilt.
        var stack = Stack(
            Use("grade.levels", ("gamma", 2.0)),
            Use("style.outerGlow"),
            Use("blur.gaussian", ("radius", 6.0)));
        Assert.NotNull(EffectRegistry.FilterFor(stack, 0));
        Assert.NotNull(EffectRegistry.StyleFor(stack, 0));

        stack.Disabled = true;
        Assert.Null(EffectRegistry.FilterFor(stack, 0));
        Assert.Null(EffectRegistry.StyleFor(stack, 0));
        Assert.Null(EffectRegistry.ProgramFor(stack, 0));
        Assert.Equal(0, EffectRegistry.ReachOf(stack, 0));

        stack.Disabled = null;
        Assert.NotNull(EffectRegistry.FilterFor(stack, 0));
        // The glow's 12 (1.5 × its default size 8) plus the blur's 9.
        Assert.Equal(21, EffectRegistry.ReachOf(stack, 0));
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
    public void TheHslPassReadsTheSurfaceByteOrder()
    {
        // The app's compose surfaces snapshot as the platform's N32 —
        // Bgra8888 here — while these tests build Rgba8888. The first CPU
        // pass shipped Rgba-only with a silent identity guard, so every
        // adjustment layer in the running app did nothing while every
        // registry test stayed green. A hue spin is the pin because it is
        // the one operation that cares which byte is red.
        var bmp = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(255, 0, 0));
        var program = EffectRegistry.ProgramFor(Stack(Use("grade.hsl", ("hue", 120.0))), 0)!;
        var spun = EffectRegistry.ApplyTo(bmp, program);
        var pixel = spun.GetPixel(4, 4);
        spun.Dispose();
        output.WriteLine($"bgra pure red +120 {pixel}");
        Assert.True(pixel.Green > 240 && pixel.Red < 15 && pixel.Blue < 15,
            $"the spin must read red as red on a BGRA surface, got {pixel}");
    }

    [Fact]
    public void TheHslPassPreservesAlpha()
    {
        // Pins the premultiplication convention: a half-transparent red,
        // desaturated, is a half-transparent grey — same alpha, neutral
        // colour, no channel blown by handling premul as straight or back.
        var pixel = Filtered(
            Stack(Use("grade.hsl", ("saturation", -100.0))), new SKColor(200, 40, 40, 128));
        output.WriteLine($"half-transparent desaturated {pixel}");
        Assert.InRange(pixel.Alpha, 126, 130);
        Assert.True(Math.Abs(pixel.Red - pixel.Green) <= 3 && Math.Abs(pixel.Green - pixel.Blue) <= 3,
            $"desaturated must be neutral, got {pixel}");
        Assert.InRange(pixel.Red, 60, 190); // present, not blown or vanished
    }

    [Fact]
    public void AnHslUseIsBackdropOnlyAndIdentityOnASelfStack()
    {
        // The self path composes only native uses; a CPU effect there is
        // identity by design — the docker steers Hue/Saturation to an
        // adjustment layer, and clipping it to the layer below is the
        // per-layer use. This is the contract, pinned.
        var stack = Stack(Use("grade.hsl", ("saturation", -100.0)));
        Assert.Null(EffectRegistry.FilterFor(stack, 0));
        Assert.True(EffectRegistry.ProgramFor(stack, 0)!.HasCpu);

        var mixed = Stack(
            Use("grade.levels", ("gamma", 2.0)),
            Use("grade.hsl", ("saturation", -100.0)));
        Assert.NotNull(EffectRegistry.FilterFor(mixed, 0)); // the levels half still applies
        Assert.Equal(2, EffectRegistry.ProgramFor(mixed, 0)!.Steps.Count);
    }

    // ---- layer styles (Q153): drawn through the style group, the way the
    // renderer applies them after the carve (Q155).

    /// <summary>A 33px canvas with an opaque square at 12..20, decorated by the stack's styles.</summary>
    private static SKBitmap Styled(EffectStack stack, SKColor content, int frame = 0)
    {
        var bmp = new SKBitmap(33, 33, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { ImageFilter = EffectRegistry.StyleFor(stack, frame) };
        canvas.SaveLayer(paint);
        using (var fill = new SKPaint { Color = content })
        {
            canvas.DrawRect(SKRect.Create(12, 12, 9, 9), fill);
        }
        canvas.Restore();
        canvas.Flush();
        return bmp;
    }

    [Fact]
    public void AStyleJoinsTheStyleChainAndNeitherOtherPath()
    {
        // The three chains a stack can lower to, kept apart: a style is not
        // a self filter (it applies after the carve) and not a backdrop
        // step (there is no silhouette there to read).
        var styles = Stack(Use("style.outerGlow"));
        Assert.Null(EffectRegistry.FilterFor(styles, 0));
        Assert.Null(EffectRegistry.ProgramFor(styles, 0));
        Assert.NotNull(EffectRegistry.StyleFor(styles, 0));

        var mixed = Stack(Use("blur.gaussian", ("radius", 4.0)), Use("style.outerGlow"));
        Assert.NotNull(EffectRegistry.FilterFor(mixed, 0));
        Assert.NotNull(EffectRegistry.StyleFor(mixed, 0));
        Assert.Null(EffectRegistry.StyleFor(Stack(Use("blur.gaussian")), 0));
    }

    [Fact]
    public void ADropShadowFallsAwayFromTheLight()
    {
        // Light from the upper left (the 120° default): the shadow lands
        // below and to the right, and nothing lands toward the light.
        var bmp = Styled(Stack(Use("style.dropShadow",
            ("distance", 8.0), ("size", 2.0), ("opacity", 100.0))), SKColors.White);
        var shadowSide = bmp.GetPixel(25, 25);
        var lightSide = bmp.GetPixel(7, 7);
        output.WriteLine($"shadow side a={shadowSide.Alpha}, light side a={lightSide.Alpha}");
        Assert.True(shadowSide.Alpha > 60, $"a shadow should land down-right, got {shadowSide}");
        Assert.True(shadowSide.Red < 40, $"and be dark, got {shadowSide}");
        Assert.Equal(0, lightSide.Alpha);
        Assert.Equal(SKColors.White, bmp.GetPixel(16, 16)); // content untouched
        bmp.Dispose();
    }

    [Fact]
    public void AnOuterGlowHalosTheSilhouetteAndAnInnerGlowStaysInside()
    {
        var outer = Styled(Stack(Use("style.outerGlow",
            ("size", 8.0), ("opacity", 100.0))), SKColors.White);
        var halo = outer.GetPixel(24, 16); // just outside the right edge
        var far = outer.GetPixel(1, 1);
        output.WriteLine($"halo a={halo.Alpha} ({halo}), far a={far.Alpha}");
        Assert.True(halo.Alpha > 30, $"the glow should reach past the edge, got {halo}");
        Assert.True(halo.Red >= halo.Blue, $"and carry the warm default colour, got {halo}");
        Assert.True(far.Alpha < 10, "and fade before the far corner");
        Assert.Equal(SKColors.White, outer.GetPixel(16, 16));
        outer.Dispose();

        // Size below the square's half-width, or the whole interior is
        // "near an edge" and the band has no centre to contrast against.
        var inner = Styled(Stack(Use("style.innerGlow",
            ("size", 4.0), ("opacity", 100.0))), new SKColor(20, 20, 160));
        Assert.Equal(0, inner.GetPixel(24, 16).Alpha); // nothing escapes
        var edge = inner.GetPixel(13, 16);
        var centre = inner.GetPixel(16, 16);
        output.WriteLine($"edge {edge}, centre {centre}");
        Assert.True(edge.Red > centre.Red + 20,
            $"the band hugs the inside of the edge: edge {edge} vs centre {centre}");
        inner.Dispose();
    }

    [Fact]
    public void AStrokeOutlinesWhereItsPositionSays()
    {
        var red = ("#ff0000");
        var outside = Stack(Use("style.stroke", ("width", 3.0), ("position", 0.0)));
        outside.Uses[0].Colors = new() { ["color"] = red };
        var bmpOut = Styled(outside, SKColors.White);
        var ringOut = bmpOut.GetPixel(22, 16); // just outside the edge
        output.WriteLine($"outside ring {ringOut}");
        Assert.True(ringOut.Alpha > 200 && ringOut.Red > 200 && ringOut.Green < 40,
            $"an outside stroke rings the silhouette, got {ringOut}");
        Assert.Equal(SKColors.White, bmpOut.GetPixel(16, 16));
        bmpOut.Dispose();

        var inside = Stack(Use("style.stroke", ("width", 3.0), ("position", 1.0)));
        inside.Uses[0].Colors = new() { ["color"] = red };
        var bmpIn = Styled(inside, SKColors.White);
        var ringIn = bmpIn.GetPixel(13, 16); // just inside the edge
        output.WriteLine($"inside ring {ringIn}, outside {bmpIn.GetPixel(22, 16)}");
        Assert.True(ringIn.Red > 200 && ringIn.Green < 40,
            $"an inside stroke paints the rim of the content, got {ringIn}");
        Assert.Equal(0, bmpIn.GetPixel(22, 16).Alpha);
        bmpIn.Dispose();
    }

    [Fact]
    public void ABevelLightsTheEdgeFacingTheLight()
    {
        // Light from the upper left: inside the silhouette, the top-left rim
        // brightens and the bottom-right rim darkens, against a mid-grey.
        var grey = new SKColor(128, 128, 128);
        var bmp = Styled(Stack(Use("style.bevel",
            ("size", 5.0), ("depth", 80.0))), grey);
        var lit = bmp.GetPixel(13, 13);
        var shaded = bmp.GetPixel(19, 19);
        var centre = bmp.GetPixel(16, 16);
        output.WriteLine($"lit {lit}, shaded {shaded}, centre {centre}");
        Assert.True(lit.Red > centre.Red + 15, $"the light-facing rim lifts, got {lit} vs {centre}");
        Assert.True(shaded.Red < centre.Red - 15, $"the far rim falls, got {shaded} vs {centre}");
        Assert.Equal(0, bmp.GetPixel(25, 25).Alpha); // an inner bevel stays inside
        bmp.Dispose();
    }

    [Fact]
    public void AnAuthoredColourChangesTheChainAndTheCacheNotices()
    {
        var use = Use("style.outerGlow", ("size", 8.0), ("opacity", 100.0));
        var stack = Stack(use);
        var first = EffectRegistry.StyleFor(stack, 0);
        Assert.Same(first, EffectRegistry.StyleFor(stack, 0));

        use.Colors = new() { ["color"] = "#00ff00" };
        var recoloured = EffectRegistry.StyleFor(stack, 0);
        Assert.NotSame(first, recoloured);

        var bmp = Styled(stack, SKColors.White);
        var halo = bmp.GetPixel(24, 16);
        output.WriteLine($"green halo {halo}");
        Assert.True(halo.Green > halo.Red + 20, $"the authored colour shows, got {halo}");
        bmp.Dispose();
    }

    [Fact]
    public void StyleReachFollowsItsOwnSliders()
    {
        Assert.Equal(17, EffectRegistry.ReachOf(
            Stack(Use("style.dropShadow", ("distance", 8.0), ("size", 6.0))), 0));
        Assert.Equal(16, EffectRegistry.ReachOf(
            Stack(Use("style.outerGlow", ("size", 8.0), ("spread", 4.0))), 0));
        Assert.Equal(0, EffectRegistry.ReachOf(Stack(Use("style.innerGlow")), 0));
        Assert.Equal(5, EffectRegistry.ReachOf(Stack(Use("style.stroke", ("width", 5.0))), 0));
        Assert.Equal(0, EffectRegistry.ReachOf(Stack(Use("style.bevel")), 0)); // inner
    }

    /// <summary>An 8x8 flat of one colour.</summary>
    private static SKBitmap Solid(SKColor colour)
    {
        var bmp = new SKBitmap(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(colour);
        return bmp;
    }

    // ---- the Photoshop filters (Q160) -------------------------------------

    /// <summary>A 16x16 split down the middle — dark left, light right.</summary>
    private static SKBitmap Edged()
    {
        var bmp = new SKBitmap(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(40, 40, 40));
        using var canvas = new SKCanvas(bmp);
        using var paint = new SKPaint { Color = new SKColor(210, 210, 210) };
        canvas.DrawRect(SKRect.Create(8, 0, 8, 16), paint);
        canvas.Flush();
        return bmp;
    }

    /// <summary>A light bar down the middle of a dark field — two edges.</summary>
    private static SKBitmap Barred()
    {
        var bmp = new SKBitmap(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(40, 40, 40));
        using var canvas = new SKCanvas(bmp);
        using var paint = new SKPaint { Color = new SKColor(210, 210, 210) };
        canvas.DrawRect(SKRect.Create(6, 0, 4, 16), paint);
        canvas.Flush();
        return bmp;
    }

    private static SKBitmap Through(SKBitmap source, EffectStack stack, int frame = 0)
    {
        var outBmp = new SKBitmap(
            source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(outBmp);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { ImageFilter = EffectRegistry.FilterFor(stack, frame) };
        canvas.DrawBitmap(source, 0, 0, paint);
        canvas.Flush();
        return outBmp;
    }

    [Fact]
    public void SharpenSteepensAnEdgeAndAmountZeroIsExactlyIdentity()
    {
        using var source = Edged();
        using var sharp = Through(source,
            Stack(Use("detail.sharpen", ("amount", 100.0), ("radius", 2.0))));
        // The dark side of the edge darkens and the light side lightens:
        // that overshoot *is* sharpening.
        var darkSide = sharp.GetPixel(7, 8).Red;
        var lightSide = sharp.GetPixel(8, 8).Red;
        output.WriteLine($"edge {source.GetPixel(7, 8).Red}->{darkSide}, {source.GetPixel(8, 8).Red}->{lightSide}");
        Assert.True(darkSide < 40, $"the dark side should undershoot, got {darkSide}");
        Assert.True(lightSide > 210, $"the light side should overshoot, got {lightSide}");
        // Away from the edge nothing happens: a flat is already its own mean.
        Assert.Equal(40, sharp.GetPixel(2, 8).Red);

        using var none = Through(source, Stack(Use("detail.sharpen", ("amount", 0.0))));
        // Amount 0 short-circuits to the input rather than to arithmetic
        // that happens to cancel, so this is exact rather than nearly.
        Assert.True(source.Bytes.AsSpan().SequenceEqual(none.Bytes),
            "amount 0 must be the untouched picture, not nearly it");
    }

    [Fact]
    public void FindEdgesKeepsTheEdgeAndDropsTheFlats()
    {
        using var source = Edged();
        using var edges = Through(source, Stack(Use("detail.edges", ("radius", 2.0))));
        var flat = edges.GetPixel(2, 8).Red;
        // The darkest point of the row, not one guessed column: the edge is a
        // couple of pixels wide and which one is darkest is Skia's business.
        var atEdge = 255;
        for (var x = 0; x < 16; x++) atEdge = Math.Min(atEdge, edges.GetPixel(x, 8).Red);
        output.WriteLine($"flat {flat}, darkest {atEdge}");
        // Drawn the way the filter is drawn everywhere else: white paper,
        // dark lines where the picture changes.
        Assert.True(flat > 240, $"a flat has no edges in it, got {flat}");
        Assert.True(flat - atEdge > 40, $"and the edge must read dark, got {atEdge} against {flat}");
    }

    [Fact]
    public void NoFilterChangesTheAlphaItWasGiven()
    {
        // Every fixture in this file was opaque until the adversary review
        // pointed out that both primitives the detail filters are built from
        // move alpha on their own: a blend-mode filter composites alpha
        // Porter-Duff "over" whatever its colour blend is (a half-transparent
        // fill came out of Find edges at 192 instead of 128), and an
        // arithmetic filter applies its coefficients to alpha too (an unsharp
        // mask dipped a soft alpha edge from 128 to 86). Both are invisible
        // on an opaque test and obvious on a half-opacity stroke.
        foreach (var use in new[]
                 {
                     Use("detail.sharpen", ("amount", 100.0), ("radius", 2.0)),
                     Use("detail.edges", ("radius", 2.0)),
                     Use("grade.invert"),
                     Use("grade.threshold"),
                     Use("grade.posterize"),
                     Use("grade.gradientMap"),
                 })
        {
            // A flat half-transparent fill keeps its alpha exactly.
            using var flat = Solid(new SKColor(150, 90, 40, 128));
            using var filtered = Through(flat, Stack(use));
            var alpha = filtered.GetPixel(4, 4).Alpha;
            output.WriteLine($"{use.Kind}: flat 128 -> {alpha}");
            Assert.True(Math.Abs(alpha - 128) <= 2,
                $"{use.Kind} moved a flat's alpha from 128 to {alpha}");
        }

        // And a soft alpha edge — half-opacity beside full — keeps both
        // sides. This is the shape a feathered stroke or a soft eraser
        // leaves behind, and the one the arithmetic filter distorted.
        var soft = new SKBitmap(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul);
        soft.Erase(new SKColor(150, 90, 40, 128));
        using (var canvas = new SKCanvas(soft))
        using (var paint = new SKPaint { Color = new SKColor(150, 90, 40, 255) })
        {
            canvas.DrawRect(SKRect.Create(8, 0, 8, 16), paint);
            canvas.Flush();
        }
        foreach (var kind in new[] { "detail.sharpen", "detail.edges" })
        {
            using var filtered = Through(soft, Stack(Use(kind, ("radius", 2.0))));
            var dim = filtered.GetPixel(6, 8).Alpha;
            var solid = filtered.GetPixel(13, 8).Alpha;
            output.WriteLine($"{kind}: soft edge 128 -> {dim}, 255 -> {solid}");
            Assert.True(Math.Abs(dim - 128) <= 3, $"{kind} dipped the soft side to {dim}");
            Assert.True(solid >= 252, $"{kind} thinned the solid side to {solid}");
        }

        // A transparent region stays transparent: a filter that fills one in
        // turns a layer into its own bounding box.
        using var block = new SKBitmap(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul);
        block.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(block))
        using (var paint = new SKPaint { Color = SKColors.White })
        {
            canvas.DrawRect(SKRect.Create(0, 0, 8, 16), paint);
            canvas.Flush();
        }
        foreach (var kind in new[] { "detail.sharpen", "detail.edges" })
        {
            using var filtered = Through(block, Stack(Use(kind, ("radius", 2.0))));
            output.WriteLine($"{kind}: outside the drawing -> {filtered.GetPixel(12, 8).Alpha}");
            Assert.Equal(0, filtered.GetPixel(12, 8).Alpha);
        }
        soft.Dispose();
    }

    [Fact]
    public void ThresholdIsTwoTonedThroughLuminanceNotPerChannel()
    {
        // A mid grey either side of the level, and — the trap — a saturated
        // colour whose channels straddle it: per-channel thresholding turns
        // that into a primary, luminance keeps it one tone.
        using var mid = Through(Solid(new SKColor(150, 150, 150)),
            Stack(Use("grade.threshold", ("level", 128.0))));
        Assert.Equal(255, mid.GetPixel(4, 4).Red);
        using var dark = Through(Solid(new SKColor(100, 100, 100)),
            Stack(Use("grade.threshold", ("level", 128.0))));
        Assert.Equal(0, dark.GetPixel(4, 4).Red);

        using var colour = Through(Solid(new SKColor(200, 90, 30)),
            Stack(Use("grade.threshold", ("level", 128.0))));
        var p = colour.GetPixel(4, 4);
        output.WriteLine($"straddling colour -> {p}");
        Assert.True(p.Red == p.Green && p.Green == p.Blue,
            $"threshold must be one tone, not a primary, got {p}");
    }

    [Fact]
    public void PosterizeBandsTheRangeAndKeepsBothEnds()
    {
        var seen = new HashSet<byte>();
        for (var v = 0; v < 256; v += 8)
        {
            using var band = Through(Solid(new SKColor((byte)v, (byte)v, (byte)v)),
                Stack(Use("grade.posterize", ("levels", 4.0))));
            seen.Add(band.GetPixel(4, 4).Red);
        }
        output.WriteLine($"4 levels gave [{string.Join(", ", seen.OrderBy(v => v))}]");
        Assert.Equal(4, seen.Count);
        Assert.Contains((byte)0, seen);   // the darkest band reaches black
        Assert.Contains((byte)255, seen); // and the lightest reaches white
    }

    [Fact]
    public void InvertIsItsOwnUndo()
    {
        var original = new SKColor(200, 90, 30);
        using var once = Through(Solid(original), Stack(Use("grade.invert")));
        var flipped = once.GetPixel(4, 4);
        Assert.Equal(55, flipped.Red);
        Assert.Equal(165, flipped.Green);

        using var twice = Through(once, Stack(Use("grade.invert")));
        var back = twice.GetPixel(4, 4);
        output.WriteLine($"{original} -> {flipped} -> {back}");
        Assert.Equal(original.Red, back.Red);
        Assert.Equal(original.Green, back.Green);
        Assert.Equal(original.Blue, back.Blue);
    }

    [Fact]
    public void AGradientMapCarriesToneToItsTwoColours()
    {
        var use = Use("grade.gradientMap", ("midpoint", 50.0));
        use.Colors = new() { ["shadow"] = "#0000ff", ["highlight"] = "#ffff00" };
        var stack = Stack(use);

        using var black = Through(Solid(SKColors.Black), stack);
        using var white = Through(Solid(SKColors.White), stack);
        var low = black.GetPixel(4, 4);
        var high = white.GetPixel(4, 4);
        output.WriteLine($"black -> {low}, white -> {high}");
        Assert.True(low.Blue > 240 && low.Red < 15, $"black takes the shadow colour, got {low}");
        Assert.True(high.Red > 240 && high.Green > 240 && high.Blue < 15,
            $"white takes the highlight colour, got {high}");

        // A mid grey lands between them, and the midpoint slider moves it.
        using var mid = Through(Solid(new SKColor(128, 128, 128)), stack);
        var centre = mid.GetPixel(4, 4).Red;
        use.Params["midpoint"] = new EffectParam(80);
        using var biased = Through(Solid(new SKColor(128, 128, 128)), stack);
        output.WriteLine($"mid at 50 -> {centre}, at 80 -> {biased.GetPixel(4, 4).Red}");
        Assert.InRange(centre, 80, 200);
        Assert.True(biased.GetPixel(4, 4).Red < centre,
            "pushing the midpoint up holds more of the picture in the shadow colour");
    }

    [Fact]
    public void EveryPhotoshopFilterWorksOnALayersOwnStackAndOnTheBackdrop()
    {
        // The point of choosing this set (Q160): all of it is native, so
        // none of it is stranded on the backdrop path the way a CPU pass is.
        foreach (var kind in new[]
                 {
                     "detail.sharpen", "detail.edges",
                     "grade.invert", "grade.threshold", "grade.posterize",
                     "grade.gradientMap",
                 })
        {
            var stack = Stack(Use(kind));
            Assert.NotNull(EffectRegistry.FilterFor(stack, 0));
            Assert.NotNull(EffectRegistry.ProgramFor(stack, 0));
            Assert.False(EffectRegistry.Resolve(kind)!.BackdropOnly, kind);
            Assert.False(EffectRegistry.Resolve(kind)!.SelfOnly, kind);
        }

        // An unsharp mask reaches as far as the blur it subtracts, so the
        // badge follows the radius rather than the strength.
        Assert.Equal(6, EffectRegistry.ReachOf(Stack(Use("detail.sharpen", ("radius", 4.0))), 0));
        Assert.Equal(0, EffectRegistry.ReachOf(Stack(Use("grade.posterize")), 0));
    }

    // ---- the animation shelf (Q159): effects that vary by frame ----------

    /// <summary>A 4x4 mark at (14,14) of a 32x32, through a native filter.</summary>
    private static SKBitmap Marked(SKImageFilter? filter)
    {
        var bmp = new SKBitmap(32, 32, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { ImageFilter = filter, Color = SKColors.White };
        canvas.DrawRect(SKRect.Create(14, 14, 4, 4), paint);
        canvas.Flush();
        return bmp;
    }

    /// <summary>Where the ink ended up, and how much of it there is.</summary>
    private static (double X, double Y, double Mass) Ink(SKBitmap bmp)
    {
        double sx = 0, sy = 0, mass = 0;
        for (var y = 0; y < bmp.Height; y++)
        {
            for (var x = 0; x < bmp.Width; x++)
            {
                double a = bmp.GetPixel(x, y).Alpha;
                sx += x * a;
                sy += y * a;
                mass += a;
            }
        }
        return mass <= 0 ? (0, 0, 0) : (sx / mass, sy / mass, mass);
    }

    private static (double X, double Y, double Mass) WiggledAt(EffectStack stack, int frame)
    {
        using var bmp = Marked(EffectRegistry.FilterFor(stack, frame));
        return Ink(bmp);
    }

    /// <summary>
    /// A wiggle moves the mark, holds it for the length of its hold, and moves
    /// it rather than smearing it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The seed is pinned and the ink is compared as a fraction, and both
    /// halves are B306.</b> The test used to build an unsaved <c>EffectUse</c>,
    /// whose id — and so whose wiggle — is drawn fresh from the wall clock every
    /// run, and then assert that the mark's total ink came back <em>equal to one
    /// decimal place</em> across a sub-pixel move.
    /// </para>
    /// <para>
    /// Translating a 4×4 rect by a fractional offset resamples it, and the total
    /// alpha lands a unit either side depending where the offset falls: 4084
    /// against 4085 out of 4084, which is 0.02%. Most offsets survived that
    /// assertion and about one in twenty did not — measured by applying the old
    /// assertion to 300 distinct ids, <b>15 failed, 5.0%</b> — so it turned main
    /// red on a commit that had not touched effects at all.
    /// </para>
    /// <para>
    /// Pinning the id fixes <em>which</em> wiggle is being asked about, which is
    /// what a saved document has anyway. Comparing the ink as a fraction of
    /// itself fixes what is being asked: "a move, not a smear" is a statement
    /// about ink being conserved, not about a resampler rounding the same way
    /// twice, and it stays true if the pinned seed is ever changed.
    /// </para>
    /// </remarks>
    [Fact]
    public void AWiggleMovesTheMarkAndStaysPutForTheLengthOfItsHold()
    {
        // Hold 2 — the boil an animator working on 2s asks for: frames 0 and
        // 1 are the same drawing's position, frame 2 is a new one.
        var stack = Stack(SeededUse("fx_wiggle_hold2", "anim.wiggle", ("amount", 6.0), ("hold", 2.0)));
        var f0 = WiggledAt(stack, 0);
        var f1 = WiggledAt(stack, 1);
        var f2 = WiggledAt(stack, 2);
        output.WriteLine(
            $"f0 ({f0.X:F2},{f0.Y:F2})  f1 ({f1.X:F2},{f1.Y:F2})  f2 ({f2.X:F2},{f2.Y:F2})"
            + $"   ink {f0.Mass:F0} → {f2.Mass:F0}");

        Assert.Equal(f0.X, f1.X, 3);
        Assert.Equal(f0.Y, f1.Y, 3);
        Assert.True(Math.Abs(f2.X - f0.X) + Math.Abs(f2.Y - f0.Y) > 0.5,
            $"the next hold must land somewhere else, got ({f2.X:F2},{f2.Y:F2})");

        // And it is a move, not a smear: the mark keeps its ink. Half a percent
        // is chosen off the measurement rather than by eye — over 400 seeds the
        // ink moves by a median of 0 units, a 95th percentile of 1, and a worst
        // case of 5 in 4084 (0.12%), which is what resampling a rect onto a
        // fractional offset is entitled to. Half a percent clears that worst
        // case four times over and still catches the failure worth catching:
        // a mark that loses a real share of itself, off the canvas edge or into
        // a filter that eats alpha.
        Assert.True(
            Math.Abs(f2.Mass - f0.Mass) / f0.Mass < 0.005,
            $"the mark lost ink moving: {f0.Mass:F0} → {f2.Mass:F0}, "
            + $"{(f2.Mass - f0.Mass) / f0.Mass:P3}");

        // Same frame, same answer — twice, and after asking for another.
        Assert.Equal(f0.X, WiggledAt(stack, 0).X, 6);
    }

    [Fact]
    public void TwoWigglesDoNotMoveInLockstep()
    {
        // The per-use seed (Q159): two layers wiggling identically read as
        // one rigid object, which is the opposite of the point.
        var a = Stack(Use("anim.wiggle", ("amount", 8.0), ("hold", 1.0)));
        var b = Stack(Use("anim.wiggle", ("amount", 8.0), ("hold", 1.0)));
        var moved = 0;
        for (var frame = 0; frame < 6; frame++)
        {
            var pa = WiggledAt(a, frame);
            var pb = WiggledAt(b, frame);
            if (Math.Abs(pa.X - pb.X) + Math.Abs(pa.Y - pb.Y) > 0.5) moved++;
        }
        output.WriteLine($"{moved} of 6 frames differ between the two uses");
        Assert.True(moved >= 4, $"two uses should mostly disagree, they agreed on {6 - moved}");

        // Dialling one seed to the other's makes them agree — the parameter
        // is the control, not a decoration.
        // The definition's own spec, not a copy of it: a duplicate here went
        // stale the moment the seed's range widened.
        var spec = EffectRegistry.Resolve("anim.wiggle")!.Params.First(p => p.Key == "seed");
        var seed = EffectRegistry.DefaultOf(spec, b.Uses[0]);
        a.Uses[0].Params["seed"] = new EffectParam(seed);
        var same = WiggledAt(a, 3);
        var other = WiggledAt(b, 3);
        Assert.Equal(other.X, same.X, 3);
        Assert.Equal(other.Y, same.Y, 3);
    }

    [Fact]
    public void AFlickerDipsOutOfFullStrengthAndNeverAboveIt()
    {
        var stack = Stack(Use("anim.flicker", ("amount", 60.0), ("hold", 1.0)));
        var masses = new List<double>();
        for (var frame = 0; frame < 8; frame++)
        {
            using var bmp = Marked(EffectRegistry.FilterFor(stack, frame));
            masses.Add(Ink(bmp).Mass);
        }
        using var plain = Marked(null);
        var full = Ink(plain).Mass;
        output.WriteLine($"full {full}, frames [{string.Join(", ", masses)}]");

        Assert.All(masses, m => Assert.True(m <= full + 0.001, $"never brighter than full: {m} vs {full}"));
        Assert.True(masses.Exists(m => m < full * 0.9), "and it must actually dip");
        Assert.True(masses.Distinct().Count() >= 4, "a flicker that repeats itself is a hold, not a flicker");
    }

    [Fact]
    public void AFlickerBuildsOnEveryStepIncludingTheOnesThatAskForFullStrength()
    {
        // Skia answers an identity blend with null rather than a filter, and
        // the flicker asks for one whenever its noise lands near zero — about
        // one step in three hundred at the default depth. The per-use random
        // seed hid it: the chain threw or did not depending on which id the
        // use happened to be handed, which is the worst way for a crash to
        // arrive. Sweeping the seed makes it certain rather than lucky.
        var fullStrength = 0;
        for (var seed = 0; seed < 60; seed++)
        {
            var use = Use("anim.flicker", ("amount", 60.0), ("hold", 1.0));
            use.Params["seed"] = new EffectParam(seed);
            var stack = Stack(use);
            for (var frame = 0; frame < 40; frame++)
            {
                // The assertion is that this does not throw; a full-strength
                // step legitimately answers null, which is identity.
                if (EffectRegistry.FilterFor(stack, frame) is null) fullStrength++;
            }
        }
        // ...and the sweep has to actually reach the case it exists for, or
        // it is 2,400 iterations of proving nothing.
        output.WriteLine($"{fullStrength} of 2400 steps asked for full strength");
        Assert.True(fullStrength > 0, "the sweep never hit a full-strength step");

        // And the full-strength case explicitly: no dip at all is the layer
        // exactly as it was, not an exception.
        var none = Stack(Use("anim.flicker", ("amount", 0.0)));
        Assert.Null(EffectRegistry.FilterFor(none, 0));
    }

    [Fact]
    public void ATimeSeededStackRebuildsPerFrameAndAStaticOneDoesNot()
    {
        // The cache fingerprints a stack on its parameters evaluated at the
        // frame — and a wiggle's parameters do not change with the frame, its
        // output does. Without the frame in the fingerprint every frame would
        // be served frame 0's chain (Q159).
        var wiggle = Stack(Use("anim.wiggle", ("amount", 6.0), ("hold", 1.0)));
        var first = EffectRegistry.FilterFor(wiggle, 0);
        Assert.Same(first, EffectRegistry.FilterFor(wiggle, 0));
        Assert.NotSame(first, EffectRegistry.FilterFor(wiggle, 1));

        var blur = Stack(Use("blur.gaussian", ("radius", 4.0)));
        var stable = EffectRegistry.FilterFor(blur, 0);
        Assert.Same(stable, EffectRegistry.FilterFor(blur, 7));

        // And it is the *phase* that is fingerprinted, not the frame: a hold
        // of 6 is one offset for six frames, so it must be one chain for six
        // frames. Replaced filters go to the finalizer by design (disposing
        // one a deferred publish is still drawing through is a use-after-
        // free), which makes not creating them the only way to keep the
        // churn down — the leak review's finding, answered.
        var held = Stack(Use("anim.wiggle", ("amount", 6.0), ("hold", 6.0)));
        var atZero = EffectRegistry.FilterFor(held, 0);
        for (var frame = 1; frame < 6; frame++)
        {
            Assert.Same(atZero, EffectRegistry.FilterFor(held, frame));
        }
        Assert.NotSame(atZero, EffectRegistry.FilterFor(held, 6));
    }

    // ---- film grain -------------------------------------------------------

    /// <summary>A flat opaque grey, grained through the backdrop program.</summary>
    private static SKBitmap Grained(
        EffectStack stack, int frame, float scale = 1f, SKPointI origin = default,
        int w = 24, int h = 24)
    {
        var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(128, 128, 128));
        var program = EffectRegistry.ProgramFor(stack, frame, scale)!;
        return EffectRegistry.ApplyTo(bmp, program, origin);
    }

    [Fact]
    public void GrainVariesAcrossThePictureAndAcrossTheFrames()
    {
        var stack = Stack(Use("grade.grain", ("amount", 60.0), ("size", 1.0), ("hold", 1.0)));
        using var f0 = Grained(stack, 0);
        using var f1 = Grained(stack, 1);

        var values = new HashSet<byte>();
        var moved = 0;
        for (var y = 0; y < 24; y++)
        {
            for (var x = 0; x < 24; x++)
            {
                values.Add(f0.GetPixel(x, y).Red);
                if (f0.GetPixel(x, y).Red != f1.GetPixel(x, y).Red) moved++;
            }
        }
        output.WriteLine($"{values.Count} distinct values, {moved} of 576 pixels moved between frames");
        Assert.True(values.Count > 20, $"grain must vary across the picture, got {values.Count} values");
        Assert.True(moved > 400, $"and move between frames, only {moved} pixels did");
        Assert.Equal(255, f0.GetPixel(4, 4).Alpha); // and never touch coverage

        // The same frame is the same grain, forever.
        using var again = Grained(stack, 0);
        Assert.True(f0.Bytes.AsSpan().SequenceEqual(again.Bytes), "a re-render must not re-roll it");
    }

    [Fact]
    public void ATiledRepaintGrainsExactlyAsAWholeOneDoes()
    {
        // The publish path hands the pass whatever the dirty region asked
        // for. Seeded from a pixel's index inside that rectangle, a repainted
        // corner would grain differently from a whole recomposite — so the
        // rectangle's origin travels with it (Q159).
        var stack = Stack(Use("grade.grain", ("amount", 70.0), ("size", 2.0)));
        using var whole = Grained(stack, 3, w: 24, h: 24);
        using var right = Grained(stack, 3, origin: new SKPointI(12, 0), w: 12, h: 24);

        for (var y = 0; y < 24; y++)
        {
            for (var x = 0; x < 12; x++)
            {
                Assert.Equal(whole.GetPixel(12 + x, y).Red, right.GetPixel(x, y).Red);
            }
        }
    }

    [Fact]
    public void GrainDoesNotReRollWhenTheSurfaceScales()
    {
        // Invariant 7 as arithmetic: a 2x render is a sharper picture of the
        // same grain, so the cell covering device pixel (2x,2y) at 2x is the
        // one covering (x,y) at 1x.
        var stack = Stack(Use("grade.grain", ("amount", 70.0), ("size", 2.0)));
        using var one = Grained(stack, 0, scale: 1f, w: 16, h: 16);
        using var two = Grained(stack, 0, scale: 2f, w: 32, h: 32);

        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                Assert.Equal(one.GetPixel(x, y).Red, two.GetPixel(x * 2, y * 2).Red);
            }
        }
    }

    [Fact]
    public void GrainIsBackdropOnlyAndTheAnimationShelfIsNot()
    {
        // Grain is a CPU pass, so it is identity on a layer's own stack, the
        // way Hue/Saturation is. Wiggle and flicker are native and reach
        // both paths — a wiggle over the whole composite is a camera shake.
        Assert.Null(EffectRegistry.FilterFor(Stack(Use("grade.grain")), 0));
        Assert.NotNull(EffectRegistry.ProgramFor(Stack(Use("grade.grain")), 0));
        Assert.NotNull(EffectRegistry.FilterFor(Stack(Use("anim.wiggle")), 0));
        Assert.NotNull(EffectRegistry.ProgramFor(Stack(Use("anim.wiggle")), 0));
        Assert.NotNull(EffectRegistry.FilterFor(Stack(Use("anim.flicker")), 0));

        Assert.Equal(6, EffectRegistry.ReachOf(Stack(Use("anim.wiggle")), 0)); // its amount
        Assert.Equal(0, EffectRegistry.ReachOf(Stack(Use("anim.flicker")), 0));
        Assert.Equal(0, EffectRegistry.ReachOf(Stack(Use("grade.grain")), 0));
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
