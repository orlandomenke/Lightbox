using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// B431, the measurement before the fix: where exactly is a simulated wash
/// hollow, and which part of the pipeline makes it so. Prints the alpha
/// across the stroke at several points along it, for the shipped Watercolor
/// and for the same brush with one thing changed at a time.
/// </summary>
public class HollowCentreDiagnosticTests(ITestOutputHelper output)
{
    private const int W = 360, H = 140;

    private static Stroke Wash(Action<BrushSettings>? tweak = null)
    {
        var brush = new BrushSettings
        {
            Size = 42, Hardness = 0.25, Opacity = 0.55, Flow = 0.45, Spacing = 0.08,
            PressureFlowGamma = 0.8, SizeJitter = 0.15, RoundnessJitter = 0.2,
            TipId = "tip-builtin-wet-edge",
            Medium = new MediumSettings
            {
                Kind = MediumKind.Watercolour,
                Wetness = 0.85, Viscosity = 0.1, Drag = 0.25, FlowSteps = 16,
                Absorbency = 0.35, EdgePull = 0.06,
                PigmentDensity = 0.5, Granularity = 0.6, Hiding = 0.05,
                Paper = PaperKind.ColdPress, PaperScale = 14, PaperInfluence = 0.7,
                PressureWater = 0.8, Rewetting = 0,
            },
        };
        tweak?.Invoke(brush);
        return new Stroke
        {
            Color = "#101828",
            Points = [.. Enumerable.Range(0, 12).Select(i => new StrokePoint(30 + i * 27.0, 70, 0.9))],
            Brush = brush,
        };
    }

    [Theory]
    [InlineData("as shipped before the fix (0.06)", 0)]
    [InlineData("no tip (round)", 1)]
    [InlineData("edge pull 0", 2)]
    [InlineData("flow steps 0", 3)]
    [InlineData("no granulation, smooth paper", 4)]
    [InlineData("no size or roundness jitter", 5)]
    [InlineData("medium off", 6)]
    [InlineData("edge pull 0.3", 7)]
    [InlineData("edge pull 0.45 (shipped)", 10)]
    [InlineData("edge pull 1.0", 8)]
    [InlineData("edge pull 1.0, 32 steps", 9)]
    public void WhereTheWashIsHollow(string label, int variant)
    {
        var stroke = Wash(b =>
        {
            switch (variant)
            {
                case 1: b.TipId = null; break;
                case 2: b.Medium.EdgePull = 0; break;
                case 3: b.Medium.FlowSteps = 0; break;
                case 4: b.Medium.Granularity = 0; b.Medium.PaperInfluence = 0; b.Medium.Paper = PaperKind.Smooth; break;
                case 5: b.SizeJitter = 0; b.RoundnessJitter = 0; break;
                case 6: b.Medium.Kind = MediumKind.None; break;
                case 7: b.Medium.EdgePull = 0.3; break;
                case 8: b.Medium.EdgePull = 1.0; break;
                case 9: b.Medium.EdgePull = 1.0; b.Medium.FlowSteps = 32; break;
                case 10: b.Medium.EdgePull = 0.45; break;
            }
        });
        using var bmp = FrameRasterizer.Rasterize([stroke], W, H);

        // Alpha across the stroke (y = 45..95) at four points along it, and the
        // ratio of the centre line to the best of the two flanks at each.
        var worst = 1.0;
        foreach (var x in new[] { 120, 180, 240, 300 })
        {
            var column = Enumerable.Range(45, 51).Select(y => (int)bmp.GetPixel(x, y).Alpha).ToArray();
            var centre = column.Skip(23).Take(5).Average();
            var flank = Math.Max(column.Skip(10).Take(8).Average(), column.Skip(33).Take(8).Average());
            var ratio = centre / Math.Max(1, flank);
            worst = Math.Min(worst, ratio);
            output.WriteLine($"{label,-32} x={x}: centre {centre,5:F0} flank {flank,5:F0} ratio {ratio:F2}   " +
                             string.Join(" ", column.Where((_, i) => i % 2 == 0).Select(a => a.ToString().PadLeft(3))));
        }
        output.WriteLine($"{label,-32} worst centre/flank {worst:F2}");
    }
}
