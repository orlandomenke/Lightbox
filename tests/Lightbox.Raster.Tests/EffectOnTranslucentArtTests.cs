using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// B37 — an effect brush on semi-transparent artwork.
/// </summary>
/// <remarks>
/// Every other smudge and blur test in this repository works on an opaque
/// black bar, and that is exactly why B37 survived: where alpha is already
/// 255, <c>SrcOver</c> and <c>Src</c> are indistinguishable. The bug needs
/// artwork with alpha below 1 — which is what every medium brush produces,
/// since a watercolour stroke is black pigment at low alpha rather than grey
/// pixels.
///
/// So these fixtures are deliberately translucent, and the assertions are
/// about alpha rather than about colour. A smudge that doubles a wash's
/// opacity has destroyed it even if every hue is right.
/// </remarks>
public class EffectOnTranslucentArtTests(ITestOutputHelper output)
{
    private const int W = 200, H = 120;

    /// <summary>Black pigment at alpha 60/255 — what a pale watercolour actually is.</summary>
    private const byte WashAlpha = 60;

    private static SKBitmap Wash()
    {
        var bmp = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = new SKColor(0, 0, 0, WashAlpha) };
        canvas.DrawRect(new SKRect(20, 40, 180, 80), paint);
        canvas.Flush();
        return bmp;
    }

    private static Stroke Drag(BrushKind kind, double flow) => new()
    {
        Color = "#000000",
        Brush = new BrushSettings
        {
            Kind = kind, Size = 24, Hardness = 0.5, Flow = flow, Spacing = 0.1,
            SmudgeLength = 0.6, SmudgeRadius = 0.7,
        },
        // Along the wash, so a pixel in the middle is covered by many dabs —
        // which is the condition the accumulation needed.
        Points = Enumerable.Range(0, 60).Select(i => new StrokePoint(40 + i * 2, 60, 0.8)).ToList(),
    };

    private static (byte Max, byte AtCentre) Alpha(SKBitmap b)
    {
        byte max = 0;
        for (var y = 0; y < b.Height; y++)
            for (var x = 0; x < b.Width; x++)
                if (b.GetPixel(x, y).Alpha > max) max = b.GetPixel(x, y).Alpha;
        return (max, b.GetPixel(100, 60).Alpha);
    }

    /// <summary>
    /// Blur only. Smudge has the same runaway and it is NOT fixed — see B38,
    /// and the note in <c>StampSmudgeDabs</c> for why the obvious blend-mode
    /// answer is wrong. Listing smudge here would be a test asserting a bug.
    /// </summary>
    [Theory]
    [InlineData(BrushKind.Blur)]
    public void AnEffectBrushDoesNotMakeAWashMoreOpaqueThanItWas(BrushKind kind)
    {
        using var layer = Wash();
        var before = Alpha(layer);

        FrameRasterizer.Append(layer, Drag(kind, 0.1));
        var after = Alpha(layer);

        // Both numbers, always.
        output.WriteLine(
            $"{kind}: max alpha {before.Max} -> {after.Max}, centre {before.AtCentre} -> {after.AtCentre}");

        Assert.True(
            after.Max <= before.Max + 2,
            $"{kind} raised the wash's peak alpha from {before.Max} to {after.Max} — it is depositing "
            + "opacity rather than moving colour, so a pale wash goes black");
    }

    /// <summary>
    /// The other half of the assertion: a fix that simply refused to draw would
    /// satisfy the alpha check above and break the tool. Kept on smudge because
    /// that is where "does it still move colour" is the live question.
    /// </summary>
    [Fact]
    public void ASmudgeStillMovesColourRatherThanDoingNothing()
    {
        // The other half of the assertion: a fix that simply refused to draw
        // would satisfy the alpha check above and break the tool.
        using var layer = Wash();

        // A hard edge inside the wash for the smudge to drag.
        using (var canvas = new SKCanvas(layer))
        using (var paint = new SKPaint { Color = new SKColor(255, 0, 0, WashAlpha), BlendMode = SKBlendMode.Src })
        {
            canvas.DrawRect(new SKRect(20, 40, 100, 80), paint);
            canvas.Flush();
        }
        var beforeRed = layer.GetPixel(120, 60).Red;

        // Flow sweep, because the two fixes interact: the low default was
        // chosen to tame the accumulation B37 turns out to be, and with the
        // accumulation gone a timid flow moves nothing. Printing the sweep is
        // what tells an artist where the control became useful again.
        foreach (var flow in new[] { 0.08, 0.2, 0.4, 0.6, 0.85 })
        {
            using var probe = Wash();
            using (var c = new SKCanvas(probe))
            using (var p2 = new SKPaint { Color = new SKColor(255, 0, 0, WashAlpha), BlendMode = SKBlendMode.Src })
            {
                c.DrawRect(new SKRect(20, 40, 100, 80), p2);
                c.Flush();
            }
            FrameRasterizer.Append(probe, Drag(BrushKind.Smudge, flow));
            output.WriteLine(
                $"flow {flow:F2}: red at x=120 {probe.GetPixel(120, 60).Red}, "
                + $"peak alpha {Alpha(probe).Max} (was {WashAlpha})");
        }

        FrameRasterizer.Append(layer, Drag(BrushKind.Smudge, 0.6));
        var afterRed = layer.GetPixel(120, 60).Red;

        output.WriteLine($"red carried past the boundary at flow 0.6: {beforeRed} -> {afterRed}");
        Assert.True(afterRed > beforeRed, "the smudge carried no colour across the boundary at all");
    }
}
