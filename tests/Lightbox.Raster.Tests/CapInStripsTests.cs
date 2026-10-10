using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// A large band is capped in strips side by side
/// (<see cref="BrushEngine.CapToFootprintBand(SKBitmap, SKBitmap, SKRectI, FootprintSpace, int, BrushEngine.Rim)"/>),
/// and has to come out as the bytes one pass over the whole band gives. The
/// live path and a replay both go through the strips, so a fault in them would
/// be the same fault on both sides and no "live equals replay" test would see
/// it — this compares against the single pass instead.
/// </summary>
public class CapInStripsTests(ITestOutputHelper output)
{
    private const int Width = 1400;
    private const int Height = 900;

    private static Stroke Sweep(BrushSettings brush)
    {
        var points = new List<StrokePoint>();
        for (var i = 0; i <= 60; i++)
        {
            points.Add(new StrokePoint(200 + (i * 16.0), 450 + (Math.Sin(i * 0.15) * 180), 0.5 + (0.5 * i / 60.0)));
        }
        return new Stroke { Tool = ToolKind.Brush, Color = "#203060", Brush = brush, Points = points };
    }

    private static SKBitmap Ink(Stroke stroke)
    {
        var bmp = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        var dabs = BrushEngine.WalkDabs(stroke);
        BrushEngine.StampDabRange(canvas, stroke, dabs, 0, dabs.Count);
        canvas.Flush();
        return bmp;
    }

    private static SKBitmap Footprint(Stroke stroke, double scale)
    {
        var size = FootprintSpace.BufferSize(Width, Height, scale);
        var bmp = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        BrushEngine.AccumulateFootprint(canvas, stroke, BrushEngine.WalkDabs(stroke), 0, int.MaxValue, scale);
        canvas.Flush();
        return bmp;
    }

    [Theory]
    [InlineData("soft", 300)]
    [InlineData("wet edge", 300)]
    [InlineData("wet edge, firm", 220)]
    public void StripsGiveTheBytesOnePassGives(string name, double size)
    {
        var brush = name switch
        {
            "soft" => new BrushSettings { Size = size, Hardness = 0.2, Flow = 0.6, Spacing = 0.1 },
            "wet edge" => new BrushSettings { Size = size, Hardness = 0.4, Flow = 0.5, Spacing = 0.1, WetEdge = 0.7 },
            _ => new BrushSettings { Size = size, Hardness = 0.8, Flow = 0.9, Spacing = 0.12, WetEdge = 0.4 },
        };
        Assert.True(BrushEngine.NeedsFootprintCap(brush), "the brush has no ceiling: nothing is being capped");

        var stroke = Sweep(brush);
        var scale = BrushEngine.CeilingScale(brush);
        var reach = BrushEngine.CeilingReachPx(brush, scale);
        var rim = BrushEngine.RimOf(brush, scale);
        var space = new FootprintSpace(scale, 0, 0);
        var band = new SKRectI(0, 0, Width, Height);

        Assert.True(BrushEngine.CapStrips(band, reach) > 1 || Environment.ProcessorCount < 4,
            $"the band is not split on this machine ({Environment.ProcessorCount} cores, reach {reach})");

        using var footprint = Footprint(stroke, scale);
        using var striped = Ink(stroke);
        using var whole = Ink(stroke);

        BrushEngine.CapToFootprintBand(striped, footprint, band, space, reach, rim);
        using (var cap = footprint.PeekPixels())
        using (var ink = whole.PeekPixels())
        {
            BrushEngine.CapCore(
                ink.GetPixelSpan<byte>(), ink.RowBytes, band,
                cap.GetPixelSpan<byte>(), cap.RowBytes, cap.Width, cap.Height, space, reach, rim);
        }

        var a = striped.GetPixelSpan();
        var b = whole.GetPixelSpan();
        int differing = 0, inked = 0, changed = 0;
        using var raw = Ink(stroke);
        var r = raw.GetPixelSpan();
        for (var i = 0; i < a.Length; i += 4)
        {
            if (b[i + 3] > 0) inked++;
            if (r[i] != b[i] || r[i + 1] != b[i + 1] || r[i + 2] != b[i + 2] || r[i + 3] != b[i + 3]) changed++;
            if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2] || a[i + 3] != b[i + 3]) differing++;
        }
        output.WriteLine($"{name}: {inked} inked, the cap changed {changed}, strips differ on {differing}; scale {scale:0.###} reach {reach} strips {BrushEngine.CapStrips(band, reach)}");

        Assert.True(changed > 2000, $"the cap changed only {changed} pixels: it is not doing anything to compare");
        Assert.Equal(0, differing);
    }

    [Fact]
    public void ASmallBandIsNotSplit()
    {
        Assert.Equal(1, BrushEngine.CapStrips(new SKRectI(0, 0, 200, 200), 20));
        // And never into strips thinner than the halo each has to carry.
        Assert.Equal(1, BrushEngine.CapStrips(new SKRectI(0, 0, 2000, 300), 160));
    }
}
