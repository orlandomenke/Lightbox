using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// A pencil is graphite caught on the paper's tooth: a light touch marks only
/// the peaks, a hard press fills the valleys. The owner's finding (2026-10-09)
/// was that the shipped pencil is "just some noise texture" — a hard round
/// with a little granulation, the same at any pressure. These tests pin the
/// thing a pencil does that the old one could not: the paper shows through
/// in proportion to how lightly the pen was held.
/// </summary>
public class PencilToothTests(ITestOutputHelper output)
{
    private const int W = 320, H = 120;

    private static BrushSettings Graphite(double? gate = 0.85) => new()
    {
        Size = 9, Hardness = 0.7, Opacity = 1, Flow = 1, Spacing = 0.1,
        PressureFlowGamma = 0, PressureSizeGamma = 0,   // only the tooth answers pressure here
        TextureSurface = PaperKind.ColdPress, TextureScale = 2, TextureDepth = 0.8,
        TexturePressure = gate,
    };

    private static Stroke Line(double pressure, BrushSettings? brush = null) => new()
    {
        Color = "#202020",
        Points = [.. Enumerable.Range(0, 14).Select(i => new StrokePoint(20 + i * 20.0, 60, pressure))],
        Brush = brush ?? Graphite(),
    };

    private static Stroke Ramp(BrushSettings? brush = null) => new()
    {
        Color = "#202020",
        Points = [.. Enumerable.Range(0, 28).Select(i => new StrokePoint(20 + i * 10.0, 60, 0.15 + 0.85 * i / 27.0))],
        Brush = brush ?? Graphite(),
    };

    /// <summary>
    /// Mean graphite over the stroke's core band (two pixels either side of the
    /// path), as a share of full. Ink rather than a thresholded count, because
    /// the gate makes the paper's valleys lighter inside a continuous line, not
    /// empty — a pencil, not a dotted line.
    /// </summary>
    private static double Coverage(SKBitmap bmp, int x0, int x1)
    {
        double ink = 0; var total = 0;
        for (var x = x0; x < x1; x++)
        for (var y = 58; y <= 62; y++)
        {
            total++;
            ink += bmp.GetPixel(x, y).Alpha / 255.0;
        }
        return ink / total;
    }

    [Fact]
    public void ALightTouchCatchesOnlyThePeaksAndAHardPressFillsTheValleys()
    {
        using var light = FrameRasterizer.Rasterize([Line(0.25)], W, H);
        using var hard = FrameRasterizer.Rasterize([Line(0.95)], W, H);
        var cl = Coverage(light, 60, 260);
        var ch = Coverage(hard, 60, 260);
        output.WriteLine($"core coverage: light {cl:P0}, hard {ch:P0}");
        // Set by breaking it: without the gate both read the same number
        // (the old pencil, and this brush with TexturePressure null: 61 % and
        // 61 %), and a tooth that fills at any pressure is the complaint.
        // Measured with the gate: light 57 %, hard 91 %, a ratio of 0.63.
        Assert.True(cl > 0.2, $"a light touch left almost no mark: {cl:P0}");
        Assert.True(cl < ch * 0.75, $"a light touch laid down as much graphite as a hard press: light {cl:P0}, hard {ch:P0}");
        Assert.True(ch > 0.8, $"a hard press did not fill the tooth: {ch:P0}");
    }

    [Fact]
    public void TheToothFillsInAlongAStrokeAsThePressureRises()
    {
        using var bmp = FrameRasterizer.Rasterize([Ramp()], W, H);
        var thirds = new[] { Coverage(bmp, 30, 100), Coverage(bmp, 120, 190), Coverage(bmp, 210, 280) };
        output.WriteLine($"coverage by third: {thirds[0]:P0}, {thirds[1]:P0}, {thirds[2]:P0}");
        Assert.True(thirds[0] < thirds[1] && thirds[1] < thirds[2],
            $"coverage does not rise with pressure along the stroke: {thirds[0]:P0}, {thirds[1]:P0}, {thirds[2]:P0}");
        // Measured 69 %, 73 %, 90 %: the last third is 1.3x the first. Ungated
        // the three read the same number.
        Assert.True(thirds[2] > thirds[0] * 1.2, $"the rise is too small to read as a pencil: {thirds[0]:P0} -> {thirds[2]:P0}");
    }

    [Fact]
    public void WithoutTheGateTheToothIsTheSameAtAnyPressure()
    {
        // The existing texture pass, untouched: a textured brush that does not
        // ask for the gate bites the same at a light and a hard press.
        using var light = FrameRasterizer.Rasterize([Line(0.25, Graphite(gate: null))], W, H);
        using var hard = FrameRasterizer.Rasterize([Line(0.95, Graphite(gate: null))], W, H);
        var cl = Coverage(light, 60, 260);
        var ch = Coverage(hard, 60, 260);
        output.WriteLine($"ungated core coverage: light {cl:P0}, hard {ch:P0}");
        Assert.True(Math.Abs(cl - ch) < 0.02, $"an ungated texture changed with pressure: light {cl:P0}, hard {ch:P0}");
    }

    [Fact]
    public void TheGatedToothIsDeterministic()
    {
        using var a = FrameRasterizer.Rasterize([Ramp()], W, H);
        using var b = FrameRasterizer.Rasterize([Ramp()], W, H);
        Assert.True(a.Bytes.AsSpan().SequenceEqual(b.Bytes), "two renders of the same gated stroke differ");
    }
}
