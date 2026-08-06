using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using Lightbox.Raster.Tips;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// What a simulated medium puts on the paper, measured at the brush's own size.
/// </summary>
/// <remarks>
/// <para>
/// <b>B50, and the ledger's stated cause for it was wrong.</b> The entry claimed
/// "something between seeding and write-back is dropping about half of what it was
/// given". Nothing was: a sweep of <c>PigmentDensity</c> is linear to 0.1%, and at 1.0
/// the lattice delivers within <b>0.46%</b> of the same brush with the medium switched
/// off. <c>FluidLattice</c> conserves pigment, exactly as its own tests said.
/// </para>
/// <para>
/// The defect was in <c>MediumSimulator.WriteBack</c>, which used accumulated pigment
/// <em>mass</em> as alpha — linearly, hard-clipped at 1. Replacing that with
/// Beer–Lambert took the shipped preset from a peak of 54/255 to 96 with no preset
/// value touched, and made glazing work at all.
/// </para>
/// <para>
/// Measured at true size through <c>BrushEngine.StampStroke</c>, not off a picker tile:
/// the preview shrinks the brush and a medium's lattice is size-dependent, which is the
/// distinction the original entry was careful about and this file keeps.
/// </para>
/// </remarks>
public class SimulatedMediumTests(ITestOutputHelper output)
{
    private const int W = 400, H = 200;

    private static BrushSettings Preset(string name) =>
        BuiltInPresets.Create().Single(p => p.Name == name).Settings.Clone();

    /// <summary>Darkening of black paint read over white, which for black is the alpha that landed.</summary>
    private static (int Peak, int Pixels, long Total) Measure(BrushSettings brush, int passes = 1)
    {
        BrushTipRegistry.Register(TipCatalogue.All.ToDictionary(t => t.Id, t => t.Png));
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        var stroke = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#000000",
            Points = [.. Enumerable.Range(0, 12).Select(i => new StrokePoint(60 + i * 24, 100, 1))],
            Brush = brush,
        };

        using var bmp = new SKBitmap(info);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            for (var p = 0; p < passes; p++) BrushEngine.StampStroke(canvas, stroke, info, bmp);
            canvas.Flush();
        }

        int peak = 0, pixels = 0;
        long total = 0;
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        {
            var c = bmp.GetPixel(x, y);
            var dark = 255 - (c.Red * c.Alpha / 255 + (255 - c.Alpha));
            if (dark <= 0) continue;
            peak = Math.Max(peak, dark);
            pixels++;
            total += dark;
        }
        return (peak, pixels, total);
    }

    [Fact]
    public void TheSimulatedWatercolourActuallyDepositsPigment()
    {
        var wet = Measure(Preset("Watercolor"));
        var flat = Measure(Preset("Watercolor (flat)"));

        output.WriteLine($"Watercolor        peak {wet.Peak}, {wet.Pixels} px, total {wet.Total}");
        output.WriteLine($"Watercolor (flat) peak {flat.Peak}, {flat.Pixels} px, total {flat.Total}");

        // 54 was where the linear, clipped map left it — a ghost. The bar is set well
        // above that and well below the flat counterpart, because a wash SHOULD be
        // paler than a brush that just stamps: it spreads over ~3x the area.
        Assert.True(
            wet.Peak >= 80,
            $"the simulated watercolour peaks at {wet.Peak}/255 — it was 54 when B50 was filed");

        // Not vacuous the other way: it must still read as a wash rather than as ink.
        Assert.True(wet.Pixels > flat.Pixels * 2, "the wash is not spreading like a wash");
    }

    /// <summary>
    /// The thing the old map could not do at all, and the most characteristic thing
    /// watercolour does.
    /// </summary>
    /// <remarks>
    /// The linear map clipped at unit mass, so a second wash over the first added
    /// nothing once the first had saturated. Beer–Lambert keeps deepening toward opacity
    /// without reaching it.
    /// </remarks>
    [Fact]
    public void LayeringAWashDeepensIt()
    {
        var brush = Preset("Watercolor");
        var one = Measure(brush);
        var two = Measure(brush, passes: 2);
        var three = Measure(brush, passes: 3);

        output.WriteLine($"one pass {one.Peak}, two {two.Peak}, three {three.Peak}");

        Assert.True(two.Peak > one.Peak + 20, $"a second wash barely darkened it: {one.Peak} -> {two.Peak}");
        Assert.True(three.Peak > two.Peak, $"a third wash did nothing: {two.Peak} -> {three.Peak}");
        // Transparent medium: it approaches opacity without arriving.
        Assert.True(three.Peak < 255, "a watercolour reached full opacity, which is not a watercolour");
    }

    /// <summary>
    /// A body colour must stay a body colour — the same change reaches gouache and oil.
    /// </summary>
    [Theory]
    [InlineData("Gouache", 200)]
    [InlineData("Oil", 200)]
    public void ABodyMediumStaysOpaque(string name, int floor)
    {
        var mark = Measure(Preset(name));
        output.WriteLine($"{name} peak {mark.Peak}/255");
        Assert.True(mark.Peak >= floor, $"{name} peaks at {mark.Peak}/255, which is not body colour");
    }

    /// <summary>
    /// Pigment density is a concentration, so it must behave like one.
    /// </summary>
    /// <remarks>
    /// Under the old map it was a pure linear gain — 0.25/0.5/0.75/1.0 gave 27/54/81/108,
    /// dead straight — which is a volume knob rather than a simulation of anything. It now
    /// saturates: more pigment always reads darker, and each further increment buys less.
    /// </remarks>
    [Fact]
    public void PigmentDensityReadsLikeAConcentration()
    {
        var peaks = new[] { 0.25, 0.5, 0.75, 1.0 }
            .Select(d =>
            {
                var b = Preset("Watercolor");
                b.Medium.PigmentDensity = d;
                return (d, peak: Measure(b).Peak);
            })
            .ToArray();

        foreach (var (d, peak) in peaks) output.WriteLine($"density {d:F2} -> peak {peak}");

        for (var i = 1; i < peaks.Length; i++)
        {
            Assert.True(
                peaks[i].peak > peaks[i - 1].peak,
                $"density is not monotonic: {peaks[i - 1]} then {peaks[i]}");
        }

        var firstStep = peaks[1].peak - peaks[0].peak;
        var lastStep = peaks[^1].peak - peaks[^2].peak;
        Assert.True(
            lastStep < firstStep,
            $"density is still a linear gain: first step {firstStep}, last step {lastStep}");
    }
}
