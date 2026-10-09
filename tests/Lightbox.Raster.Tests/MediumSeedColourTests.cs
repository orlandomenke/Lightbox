using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Q232: the lattice takes the colours the dabs laid down, cell by cell, instead
/// of one uniform stroke colour. Before this a medium discarded every per-dab
/// colour — jitter, a second colour, and the mixing this change adds — which is
/// most of why a simulated wash read as a tinted blob.
/// </summary>
public class MediumSeedColourTests(ITestOutputHelper output)
{
    [Fact]
    public void TheLatticeKeepsThePerDabColourItWasSeededWith()
    {
        // Full colour jitter between two very different colours, so the dabs
        // are visibly varied before the medium runs.
        var stroke = new Stroke
        {
            Color = "#e02020",
            Points = [.. Enumerable.Range(0, 12).Select(i => new StrokePoint(30 + i * 24.0, 60, 1))],
            Brush = new BrushSettings
            {
                Size = 36, Hardness = 0.8, Opacity = 1, Flow = 1, Spacing = 0.12,
                SecondaryColor = "#2040e0", ColorJitter = 1,
                Medium = new MediumSettings { Kind = MediumKind.Gouache, FlowSteps = 2, PigmentDensity = 1, Hiding = 0.9 },
            },
        };

        using var render = FrameRasterizer.Rasterize([stroke], 340, 120);
        var hues = new List<double>();
        for (var x = 40; x <= 290; x += 10)
        {
            var p = render.GetPixel(x, 60);
            if (p.Alpha < 64) continue;
            p.ToHsv(out var h, out _, out _);
            hues.Add(h);
        }
        var spread = hues.Count == 0 ? 0 : hues.Max() - hues.Min();
        output.WriteLine($"hue spread along the stroke: {spread:0.#} degrees over {hues.Count} samples");
        // Red and blue are 120 degrees apart; a uniform seed gives a spread of
        // a degree or two from rounding. Anything that kept the jitter is wide.
        Assert.True(spread > 40, $"the medium flattened the dabs' colours to one: hue spread {spread:0.#}");
    }
}
