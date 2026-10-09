using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// B434 and Q236, pinned on the brushes that actually ship rather than on a
/// hand copy of their settings: the adversary found that reverting the
/// Watercolor's load left every test green. These read the presets the picker
/// offers and render them.
/// </summary>
public class ShippedLoadsTests(ITestOutputHelper output)
{
    private const int W = 900, H = 140;

    private static BrushPreset Shipped(string id) =>
        BuiltInPresets.Create().Single(p => p.Id == id);

    private static double[] Fifths(SKBitmap bmp) => Enumerable.Range(0, 5).Select(k =>
    {
        double sum = 0; var n = 0;
        for (var x = 60 + k * 156; x < 60 + (k + 1) * 156; x++)
        for (var y = 49; y <= 91; y++) { sum += bmp.GetPixel(x, y).Alpha; n++; }
        return sum / n;
    }).ToArray();

    /// <summary>
    /// The shipped Watercolor under a rising hand (pressure 0.35 → 0.95): the
    /// end is no heavier than the start. At a load of 1 the same stroke reads
    /// 34 → 62 — the owner's complaint; this is what guards the preset's load.
    /// </summary>
    [Fact]
    public void TheShippedWatercolourDoesNotGetHeavierAlongAStroke()
    {
        var preset = Shipped("builtin-watercolor-wet");
        Assert.True(preset.Settings.Medium.PaintLoad < 1, "the shipped Watercolor never runs out again");
        var stroke = new Stroke
        {
            Color = "#101828",
            Points = [.. Enumerable.Range(0, 40).Select(i => new StrokePoint(40 + i * 20.0, 70, 0.35 + 0.6 * i / 39.0))],
            Brush = preset.Settings.Clone(),
        };
        using var bmp = FrameRasterizer.Rasterize([stroke], W, H);
        var f = Fifths(bmp);
        output.WriteLine($"shipped Watercolor, rising hand: {string.Join(" ", f.Select(v => v.ToString("0")))}");
        Assert.True(f[4] <= f[0] * 1.15, $"the shipped wash still gets heavier along the stroke: {string.Join(" ", f.Select(v => v.ToString("0")))}");
        Assert.True(f[2] > 15, $"the shipped wash ran out before its middle: {string.Join(" ", f.Select(v => v.ToString("0")))}");
    }

    /// <summary>
    /// Q236 moved the reach to Size × 6 × load / (1 − load); the body gouache and the
    /// oil were re-tuned to the load whose reach is what their old
    /// load had (d / (d + 6) for d diameters: 10.2 → 0.63, 7.2 → 0.55). A band
    /// rather than a number, so a later tuning by eye does not fail this; a
    /// return to the old numbers (0.85, 0.6) does, because on the new reach
    /// those are three and one and a half times as far.
    /// </summary>
    [Theory]
    [InlineData("builtin-gouache-body", 0.58, 0.70)]
    [InlineData("builtin-oil", 0.50, 0.60)]
    public void TheShippedLoadsKeepTheReachTheyHad(string id, double low, double high)
    {
        var load = Shipped(id).Settings.Medium.PaintLoad;
        output.WriteLine($"{id}: paint load {load}");
        Assert.InRange(load, low, high);
    }
}
