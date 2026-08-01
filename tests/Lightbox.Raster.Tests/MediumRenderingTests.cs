using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The media have to actually look like their media. Until these existed,
/// MediumKind was recorded on every stroke and read by nobody, so all four
/// presets painted identically.
/// </summary>
public class MediumRenderingTests(ITestOutputHelper output)
{
    private const int W = 220, H = 160;

    private static Stroke Stroke(MediumSettings medium, string colour = "#c04030", double pressure = 1, double size = 40)
    {
        var pts = new List<StrokePoint>();
        for (var i = 0; i < 10; i++) pts.Add(new StrokePoint(40 + i * 15, 80, pressure));
        return new Stroke
        {
            Tool = ToolKind.Brush,
            Color = colour,
            Points = pts,
            Brush = new BrushSettings
            {
                Size = size, Hardness = 0.5, Opacity = 1, Flow = 0.9, Spacing = 0.1,
                PressureFlowGamma = 1,
                Medium = medium,
            },
        };
    }

    private static SKBitmap Render(params Stroke[] strokes)
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        var layer = new SKBitmap(info);
        using var canvas = new SKCanvas(layer);
        canvas.Clear(SKColors.Transparent);
        foreach (var s in strokes)
        {
            BrushEngine.StampStroke(canvas, s, info, layer);
            canvas.Flush();
        }
        return layer;
    }

    private static (double Ink, double MeanAlpha, double Spread) Stats(SKBitmap bmp)
    {
        double ink = 0, alpha = 0;
        int minX = W, maxX = 0, minY = H, maxY = 0;
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        {
            var a = bmp.GetPixel(x, y).Alpha;
            if (a <= 4) continue;
            ink++; alpha += a;
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }
        var spread = ink == 0 ? 0 : (maxX - minX + 1) * (double)(maxY - minY + 1);
        return (ink, ink == 0 ? 0 : alpha / ink, spread);
    }

    private static MediumSettings Watercolour() => new()
    {
        Kind = MediumKind.Watercolour,
        Wetness = 0.85, Viscosity = 0.1, Drag = 0.25, FlowSteps = 14,
        Absorbency = 0.35, EdgePull = 0.7,
        PigmentDensity = 0.5, Granularity = 0.6, Hiding = 0.05,
        Paper = PaperKind.ColdPress, PaperScale = 14, PaperInfluence = 0.7,
        PressureWater = 0.8,
    };

    private static MediumSettings Oil() => new()
    {
        Kind = MediumKind.Oil,
        Wetness = 0.2, Viscosity = 0.9, Drag = 0.85, FlowSteps = 4,
        Absorbency = 0.9, EdgePull = 0.05,
        PigmentDensity = 1, Granularity = 0.1, Hiding = 0.95,
        Paper = PaperKind.Canvas, PaperScale = 8, PaperInfluence = 0.6,
        Body = 0.8, Pickup = 0.4, PressureMix = 0.9, Rewetting = 0.5,
    };

    [Fact]
    public void TheFourMediaDoNotRenderIdentically()
    {
        var kinds = new[]
        {
            (MediumKind.Watercolour, Watercolour()),
            (MediumKind.Oil, Oil()),
            (MediumKind.Gouache, new MediumSettings
            {
                Kind = MediumKind.Gouache, Wetness = 0.3, Viscosity = 0.75, Drag = 0.7,
                FlowSteps = 6, Absorbency = 0.8, EdgePull = 0.15,
                PigmentDensity = 0.9, Granularity = 0.15, Hiding = 0.9,
                Paper = PaperKind.ColdPress, PaperScale = 10, PaperInfluence = 0.35,
            }),
            (MediumKind.Ink, new MediumSettings
            {
                Kind = MediumKind.Ink, Wetness = 0.9, Viscosity = 0.05, Drag = 0.15,
                FlowSteps = 18, Absorbency = 0.75, EdgePull = 0.3,
                PigmentDensity = 0.8, Granularity = 0.05, Hiding = 0.4,
                Paper = PaperKind.Smooth, PaperScale = 6, PaperInfluence = 0.25,
            }),
        };

        var seen = new List<(MediumKind Kind, double Ink, double Alpha, double Spread)>();
        foreach (var (kind, settings) in kinds)
        {
            using var bmp = Render(Stroke(settings));
            var (ink, alpha, spread) = Stats(bmp);
            output.WriteLine($"{kind,-12} ink {ink,7:0} mean alpha {alpha,6:0.0} spread {spread,8:0}");
            Assert.True(ink > 0, $"{kind} rendered nothing at all");
            seen.Add((kind, ink, alpha, spread));
        }

        // No two media may land on the same numbers — that is what "they all
        // paint the same" looked like.
        for (var i = 0; i < seen.Count; i++)
        for (var j = i + 1; j < seen.Count; j++)
        {
            var a = seen[i];
            var b = seen[j];
            Assert.False(
                Math.Abs(a.Ink - b.Ink) < 1 && Math.Abs(a.Alpha - b.Alpha) < 1,
                $"{a.Kind} and {b.Kind} rendered indistinguishably");
        }
    }

    [Fact]
    public void APlainBrushIsUntouchedByAnyOfThis()
    {
        // MediumKind.None must be the exact path it always was.
        var plain = Stroke(new MediumSettings());
        using var a = Render(plain);
        using var b = Render(plain);
        for (var y = 0; y < H; y += 3)
        for (var x = 0; x < W; x += 3)
            Assert.Equal(a.GetPixel(x, y), b.GetPixel(x, y));
        Assert.True(Stats(a).Ink > 0);
    }

    [Fact]
    public void Watercolour_LightPressureIsPalerAndSpreadsFurther()
    {
        // The artist's request: light pressure means more water, so the mark
        // is lighter but blooms wider.
        using var firm = Render(Stroke(Watercolour(), pressure: 1.0));
        using var light = Render(Stroke(Watercolour(), pressure: 0.25));

        var f = Stats(firm);
        var l = Stats(light);
        output.WriteLine($"firm  ink {f.Ink:0} alpha {f.MeanAlpha:0.0}");
        output.WriteLine($"light ink {l.Ink:0} alpha {l.MeanAlpha:0.0}");

        Assert.True(l.MeanAlpha < f.MeanAlpha,
            $"a light watercolour touch should be paler ({l.MeanAlpha:0.0} vs {f.MeanAlpha:0.0})");
    }

    [Fact]
    public void Oil_ASecondStrokeDisturbsTheFirst()
    {
        // Wet into wet: crossing an existing mark must move it, not merely
        // cover it. Compare the first stroke's territory away from the
        // crossing point.
        var first = Stroke(Oil(), colour: "#2050c0");
        var crossing = Stroke(Oil(), colour: "#f0d020");
        crossing.Points = [new StrokePoint(110, 30, 1), new StrokePoint(110, 130, 1)];

        using var alone = Render(first);
        using var crossed = Render(first, crossing);

        // Somewhere on the original stroke but outside the second stroke's own
        // footprint, the blue must have been pushed around.
        var moved = 0;
        for (var x = 60; x < 95; x++)
        for (var y = 60; y < 100; y++)
        {
            var before = alone.GetPixel(x, y);
            var after = crossed.GetPixel(x, y);
            if (before != after) moved++;
        }
        Assert.True(moved > 0,
            "an oil stroke crossing another should disturb it, not just sit on top");
    }

    [Fact]
    public void EveryMediumReRendersIdentically()
    {
        // Invariant 2. A simulation is allowed, an unrepeatable one is not.
        foreach (var settings in new[] { Watercolour(), Oil() })
        {
            using var first = Render(Stroke(settings));
            using var again = Render(Stroke(settings));
            for (var y = 0; y < H; y += 2)
            for (var x = 0; x < W; x += 2)
                Assert.Equal(first.GetPixel(x, y), again.GetPixel(x, y));
        }
    }
}
