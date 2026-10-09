using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// B434. The owner, 2026-10-09: "a stroke getting thicker the further it
/// moves. While natural watercolor pools more and becomes smaller the longer
/// the stroke as there is less water." Measured on the shipped Watercolor: a
/// straight stroke at constant pressure is flat along its length (63 63 63 64
/// 58 by fifths), so the heaviness is the hand — pressure rises along a real
/// stroke, and with it the dabs' size and water — and the brush never running
/// out to counter it (paint load 1). The fix is the preset: a paint load below
/// one, which `BrushEngine.LoadAt` already depletes along the stroke.
/// </summary>
public class WashRunsDryTests(ITestOutputHelper output)
{
    private const int W = 900, H = 140;

    /// <summary>The shipped Watercolor's settings, with the load under test.</summary>
    private static Stroke Wash(double load, Func<int, double> pressure) => new()
    {
        Color = "#101828",
        Points = [.. Enumerable.Range(0, 40).Select(i => new StrokePoint(40 + i * 20.0, 70, pressure(i)))],
        Brush = new BrushSettings
        {
            Size = 42, Hardness = 0.25, Opacity = 0.55, Flow = 0.45, Spacing = 0.08, PressureFlowGamma = 0.8,
            SizeJitter = 0.15, RoundnessJitter = 0.2, TipId = "tip-builtin-wet-edge",
            Medium = new MediumSettings
            {
                Kind = MediumKind.Watercolour, Wetness = 0.85, Viscosity = 0.1, Drag = 0.25, FlowSteps = 16,
                Absorbency = 0.35, EdgePull = 0.06, PigmentDensity = 0.5, Granularity = 0.6, Hiding = 0.05,
                Paper = PaperKind.ColdPress, PaperScale = 14, PaperInfluence = 0.7, PressureWater = 0.8, Rewetting = 0,
                PaintLoad = load,
            },
        },
    };

    /// <summary>Mean alpha of the stroke's band by fifths along it, and the mark's width at each fifth's middle.</summary>
    private static (double[] Alpha, int[] Width) AlongTheStroke(SKBitmap bmp)
    {
        var alpha = Enumerable.Range(0, 5).Select(k =>
        {
            double sum = 0; var n = 0;
            for (var x = 60 + k * 156; x < 60 + (k + 1) * 156; x++)
            for (var y = 49; y <= 91; y++) { sum += bmp.GetPixel(x, y).Alpha; n++; }
            return sum / n;
        }).ToArray();
        var width = Enumerable.Range(0, 5).Select(k =>
        {
            var x = 138 + k * 156; int first = -1, last = -1;
            for (var y = 0; y < H; y++)
            {
                if (bmp.GetPixel(x, y).Alpha > 8) { if (first < 0) first = y; last = y; }
            }
            return last - first + 1;
        }).ToArray();
        return (alpha, width);
    }

    private static string Row(double[] a) => string.Join(" ", a.Select(v => v.ToString("0")));

    /// <summary>
    /// A hand's stroke: pressure rising 0.35 → 0.95 along it. At paint load 1
    /// the wash reads 34 46 55 62 62 — the complaint; at the shipped load the
    /// depletion and the hand cancel and the end is no heavier than the start
    /// (30 33 33 32 27 measured, on Q236's reach).
    /// </summary>
    [Fact]
    public void AWashUnderARisingHandDoesNotGetHeavierAlongTheStroke()
    {
        using var never = FrameRasterizer.Rasterize([Wash(1.0, i => 0.35 + 0.6 * i / 39.0)], W, H);
        using var loaded = FrameRasterizer.Rasterize([Wash(0.6, i => 0.35 + 0.6 * i / 39.0)], W, H);
        var (n, _) = AlongTheStroke(never);
        var (l, lw) = AlongTheStroke(loaded);
        output.WriteLine($"load 1.0:  {Row(n)}");
        output.WriteLine($"load 0.6:  {Row(l)}   width {string.Join(" ", lw)}");
        Assert.True(n[4] > n[0] * 1.5, $"the control did not reproduce the complaint: {Row(n)}");
        Assert.True(l[4] <= l[0] * 1.15, $"the wash still gets heavier along the stroke: {Row(l)}");
        Assert.True(l[2] > 15, $"the wash ran out before its middle: {Row(l)}");
    }

    /// <summary>
    /// A level hand: the wash starts full, fades, and narrows as the brush
    /// runs out — "pools more and becomes smaller the longer the stroke".
    /// Measured 59 51 42 34 22 and widths 56 55 48 43 38 at the shipped load on
    /// Q236's reach; flat at load 1.
    /// </summary>
    [Fact]
    public void AWashAtALevelHandFadesAndNarrowsAsTheBrushRunsOut()
    {
        using var bmp = FrameRasterizer.Rasterize([Wash(0.6, _ => 0.8)], W, H);
        var (a, w) = AlongTheStroke(bmp);
        output.WriteLine($"level hand: {Row(a)}   width {string.Join(" ", w)}");
        Assert.True(a[4] < a[0] * 0.6, $"the wash did not run dry: {Row(a)}");
        Assert.True(a[4] > 10, $"the wash vanished before its end: {Row(a)}");
        Assert.True(w[4] < w[0] * 0.85, $"the mark did not narrow as the brush ran out: {string.Join(" ", w)}");
    }
}
