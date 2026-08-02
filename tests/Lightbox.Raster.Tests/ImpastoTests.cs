using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Thick paint has to look thick. B23: <c>Body</c> and <c>Relief</c> were
/// settings nothing read, and the artist's report was exact — "gouache and oil
/// have no height nor edges that catch light".
/// </summary>
public class ImpastoTests(ITestOutputHelper output)
{
    private const int W = 200, H = 140;

    private static MediumSettings Oil(double body, double relief) => new()
    {
        Kind = MediumKind.Oil, Wetness = 0.2, Viscosity = 0.9, Drag = 0.85,
        FlowSteps = 4, Absorbency = 0.9, EdgePull = 0.05,
        PigmentDensity = 1, Granularity = 0.1, Hiding = 0.95,
        Paper = PaperKind.Canvas, PaperScale = 8, PaperInfluence = 0.6,
        Body = body, Relief = relief,
    };

    private static SKBitmap Render(MediumSettings medium)
    {
        var pts = new List<StrokePoint>();
        for (var i = 0; i < 9; i++) pts.Add(new StrokePoint(40 + i * 14, 70, 1));
        var stroke = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#7a5030",
            Points = pts,
            Brush = new BrushSettings
            {
                Size = 34, Hardness = 0.6, Opacity = 1, Flow = 0.9, Spacing = 0.08,
                PressureFlowGamma = 1, Medium = medium,
            },
        };

        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        var layer = new SKBitmap(info);
        using var canvas = new SKCanvas(layer);
        canvas.Clear(SKColors.Transparent);
        BrushEngine.StampStroke(canvas, stroke, info, layer);
        canvas.Flush();
        return layer;
    }

    /// <summary>Luminance spread across the mark — how much it is modelled.</summary>
    private static (double Range, double Mean, int Ink) Shading(SKBitmap bmp)
    {
        double sum = 0;
        double lo = 1, hi = 0;
        var n = 0;
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        {
            var c = bmp.GetPixel(x, y);
            if (c.Alpha < 200) continue; // the solid body, not the soft fringe
            var l = (0.2126 * c.Red + 0.7152 * c.Green + 0.0722 * c.Blue) / 255.0;
            sum += l;
            lo = Math.Min(lo, l);
            hi = Math.Max(hi, l);
            n++;
        }
        return (n == 0 ? 0 : hi - lo, n == 0 ? 0 : sum / n, n);
    }

    [Fact]
    public void ThickPaintIsModelled_AndFlatPaintIsNot()
    {
        // The measurement that failed before: Body and Relief at 0 against 1
        // differed in 0 of 41 600 pixels.
        using var flat = Render(Oil(body: 0, relief: 0));
        using var thick = Render(Oil(body: 0.8, relief: 0.6));

        var f = Shading(flat);
        var t = Shading(thick);
        output.WriteLine($"flat  range {f.Range:F3} mean {f.Mean:F3} over {f.Ink} px");
        output.WriteLine($"thick range {t.Range:F3} mean {t.Mean:F3} over {t.Ink} px");

        Assert.True(f.Ink > 500, "nothing was painted");
        Assert.True(t.Range > f.Range * 2,
            $"thick paint was no more modelled than flat: {f.Range:F3} -> {t.Range:F3}");
    }

    [Fact]
    public void TheRaisedEdgeCatchesTheLight()
    {
        // Not just "more contrast somewhere": the light comes from the upper
        // left, so the mark's upper-left flank has to be the bright side and
        // the lower-right flank the dark one. Shading that ignored the normal's
        // sign would pass a variance test and look like noise.
        using var bmp = Render(Oil(body: 0.8, relief: 0.6));

        double upper = 0, lower = 0;
        int upperN = 0, lowerN = 0;
        for (var x = 60; x < 150; x++)
        {
            var wet = new List<int>();
            for (var y = 0; y < H; y++)
            {
                if (bmp.GetPixel(x, y).Alpha >= 200) wet.Add(y);
            }
            if (wet.Count < 8) continue;
            foreach (var y in wet.Take(3))
            {
                upper += Luma(bmp.GetPixel(x, y));
                upperN++;
            }
            foreach (var y in wet.TakeLast(3))
            {
                lower += Luma(bmp.GetPixel(x, y));
                lowerN++;
            }
        }

        upper /= upperN;
        lower /= lowerN;
        output.WriteLine($"top flank {upper:F4}, bottom flank {lower:F4}");
        Assert.True(upper > lower * 1.05,
            $"the light is upper-left but the top flank is not the lit one: {upper:F4} vs {lower:F4}");
    }

    private static double Luma(SKColor c) =>
        (0.2126 * c.Red + 0.7152 * c.Green + 0.0722 * c.Blue) / 255.0;

    [Fact]
    public void PaintWithNoBodyRendersExactlyAsItAlwaysDid()
    {
        // Optional means absent. Every existing medium preset except gouache
        // and oil leaves Body at 0, and none of them may shift by a byte.
        using var a = Render(Oil(body: 0, relief: 0));
        using var b = Render(Oil(body: 0, relief: 0.9));
        using var c = Render(Oil(body: 0.9, relief: 0));

        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        {
            Assert.Equal(a.GetPixel(x, y), b.GetPixel(x, y));
            Assert.Equal(a.GetPixel(x, y), c.GetPixel(x, y));
        }
    }

    [Fact]
    public void ReliefIsAsRepeatableAsEverythingElse()
    {
        // Invariant 2. The light is a constant and the height comes from the
        // pixels, so there is nothing here that could vary — which is the
        // point, and worth an assertion rather than an argument.
        using var first = Render(Oil(0.8, 0.6));
        using var again = Render(Oil(0.8, 0.6));
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
            Assert.Equal(first.GetPixel(x, y), again.GetPixel(x, y));
    }

    [Fact]
    public void ShadingPaintsNoExtraCoverage()
    {
        // Light falling on paint is not more paint. If relief changed alpha it
        // would grow the mark, and a brush whose footprint depended on its
        // lighting would break every bounded-region assumption around it.
        using var flat = Render(Oil(0, 0));
        using var thick = Render(Oil(0.9, 0.9));

        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
            Assert.Equal(flat.GetPixel(x, y).Alpha, thick.GetPixel(x, y).Alpha);
    }
}
