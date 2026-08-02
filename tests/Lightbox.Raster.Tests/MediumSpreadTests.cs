using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// A wet medium bleeds past where the brush went. B27: it managed 20%, and the
/// artist's report was "none seem to spread like a moisture-rich medium".
/// </summary>
public class MediumSpreadTests(ITestOutputHelper output)
{
    private const int W = 400, H = 300;

    private static MediumSettings Wash(double wetness, int steps) => new()
    {
        Kind = MediumKind.Watercolour,
        Wetness = wetness, Viscosity = 0.1, Drag = 0.25, FlowSteps = steps,
        Absorbency = 0.35, EdgePull = 0.7,
        PigmentDensity = 0.5, Granularity = 0.6, Hiding = 0.05,
        Paper = PaperKind.ColdPress, PaperScale = 14, PaperInfluence = 0.7,
        PressureWater = 0.8,
    };

    /// <summary>Height of the mark, which is how far it bled across the stroke.</summary>
    private static int Height(MediumSettings medium)
    {
        var stroke = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#c04030",
            Points = Enumerable.Range(0, 10).Select(i => new StrokePoint(80 + i * 20, 150, 1)).ToList(),
            Brush = new BrushSettings
            {
                Size = 40, Hardness = 0.5, Opacity = 1, Flow = 0.9, Spacing = 0.1,
                PressureFlowGamma = 1, Medium = medium,
            },
        };

        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var layer = new SKBitmap(info);
        using var canvas = new SKCanvas(layer);
        canvas.Clear(SKColors.Transparent);
        BrushEngine.StampStroke(canvas, stroke, info, layer);
        canvas.Flush();

        int lo = H, hi = 0;
        for (var y = 0; y < H; y++)
        for (var x = 100; x < 220; x++)
        {
            if (layer.GetPixel(x, y).Alpha <= 4) continue;
            if (y < lo) lo = y;
            if (y > hi) hi = y;
        }
        return hi < lo ? 0 : hi - lo + 1;
    }

    [Fact]
    public void AWetMediumBleedsPastTheBrush()
    {
        // The stroke is 40 px wide. Before the fix a fully wet wash reached
        // 48 px however long the solver ran, because Wetness mapped to 0..1 of
        // depth and the lattice's capillary entry pressure is 0.15 absolute —
        // the soft fringe was under the threshold and could not flow at all.
        var dry = Height(Wash(0.85, steps: 0));
        var wet = Height(Wash(0.85, steps: 24));
        output.WriteLine($"no flow {dry} px, 24 steps {wet} px — {(double)wet / dry:F2}x");

        Assert.True(wet > dry * 1.4, $"the wash barely bled: {dry} px -> {wet} px");
    }

    [Fact]
    public void AWetterBrushBleedsFurther()
    {
        var damp = Height(Wash(0.25, steps: 24));
        var soaked = Height(Wash(1.0, steps: 24));
        output.WriteLine($"damp {damp} px, soaked {soaked} px");

        Assert.True(soaked > damp, $"wetness did nothing: {damp} px vs {soaked} px");
    }

    [Fact]
    public void ItKeepsSpreadingTheLongerItRuns()
    {
        // The shape of the old defect was that spread flattened out after a
        // few steps and then never moved again, whatever you asked for.
        int four = Height(Wash(0.85, 4)), twelve = Height(Wash(0.85, 12)), thirty = Height(Wash(0.85, 32));
        output.WriteLine($"4 steps {four}, 12 steps {twelve}, 32 steps {thirty}");

        Assert.True(twelve > four, $"stopped spreading between 4 and 12 steps: {four} -> {twelve}");
        Assert.True(thirty > twelve, $"stopped spreading between 12 and 32 steps: {twelve} -> {thirty}");
    }
}
