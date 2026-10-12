using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The shipped Pencil, held to what makes a mark read as graphite. The owner's
/// verdict on the pencil before 2026-10-10 was "too dark, too consistent", and
/// each test here is one half of that sentence turned into a number — measured
/// on the preset itself, so retuning it back fails here rather than on a sheet
/// somebody has to remember to render.
/// </summary>
public class PencilReadsAsGraphiteTests(ITestOutputHelper output)
{
    private static BrushSettings Pencil() =>
        BuiltInPresets.Create().Single(p => p.Id == "builtin-pencil").Settings.Clone();

    private readonly record struct Tone(double Mean, int Max, double Covered);

    /// <summary>One straight line at a pressure, measured down its middle third so the ends do not count.</summary>
    private static Tone Draw(double pressure, int passes = 1, double size = 10)
    {
        var brush = Pencil();
        brush.Size = size;
        var strokes = new List<Stroke>();
        for (var pass = 0; pass < passes; pass++)
        {
            strokes.Add(new Stroke
            {
                Tool = ToolKind.Brush,
                Color = "#000000",
                Brush = brush.Clone(),
                Points = [new StrokePoint(30, 60, pressure), new StrokePoint(370, 60, pressure)],
            });
        }

        using var bitmap = FrameRasterizer.Rasterize(strokes, 400, 120, 1.0);
        var pixels = bitmap.GetPixelSpan();
        long sum = 0;
        int max = 0, covered = 0, counted = 0;
        for (var y = 58; y <= 62; y++)
        {
            for (var x = 130; x < 270; x++)
            {
                var alpha = pixels[(((y * 400) + x) * 4) + 3];
                sum += alpha;
                max = Math.Max(max, alpha);
                if (alpha > 8) covered++;
                counted++;
            }
        }
        return new Tone(sum / (double)counted, max, covered / (double)counted);
    }

    /// <summary>Graphite is never black: a firm stroke stops well short of full.</summary>
    [Fact]
    public void AFirmStrokeIsNotBlack()
    {
        var firm = Draw(1.0);
        output.WriteLine($"firm: mean {firm.Mean:0.0} max {firm.Max} covered {firm.Covered:P0}");
        Assert.True(firm.Max <= 210, $"a firm stroke reaches {firm.Max} of 255: that is ink, not graphite");
        Assert.True(firm.Mean >= 110, $"a firm stroke averages {firm.Mean:0.0}: too faint to be a firm stroke");
    }

    /// <summary>
    /// Pressure is most of a pencil's range. Mean tone, which is below
    /// saturation on both sides here — both numbers are printed so a build
    /// where pressure is wired to nothing cannot pass by accident.
    /// </summary>
    [Fact]
    public void ALightTouchIsMuchLighterThanAFirmOne()
    {
        var light = Draw(0.25);
        var firm = Draw(1.0);
        output.WriteLine($"light: mean {light.Mean:0.0} covered {light.Covered:P0}; firm: mean {firm.Mean:0.0} covered {firm.Covered:P0}");
        Assert.True(light.Mean > 2, $"a light touch left nothing ({light.Mean:0.0})");
        Assert.True(firm.Mean >= light.Mean * 3, $"firm {firm.Mean:0.0} against light {light.Mean:0.0}: pressure barely changes the mark");
        // And the paper shows through a light touch: it catches the peaks only.
        Assert.True(light.Covered < firm.Covered - 0.15, $"a light touch covers {light.Covered:P0}, a firm one {firm.Covered:P0}: no tooth");
    }

    /// <summary>Tone builds: going over a light stroke again darkens it, and still does not reach black.</summary>
    [Fact]
    public void GoingOverAStrokeBuildsTone()
    {
        var once = Draw(0.5);
        var thrice = Draw(0.5, passes: 3);
        output.WriteLine($"once: mean {once.Mean:0.0}; three passes: mean {thrice.Mean:0.0} max {thrice.Max}");
        Assert.True(thrice.Mean >= once.Mean * 1.25, $"three passes {thrice.Mean:0.0} against one {once.Mean:0.0}: the tone does not build");
        Assert.True(thrice.Max < 250, "three medium passes are already black");
    }

    /// <summary>Even a firm stroke leaves the sheet's deepest valleys: the mark is not a flat band.</summary>
    [Fact]
    public void TheSheetShowsInAFirmStroke()
    {
        var firm = Draw(1.0, size: 16);
        output.WriteLine($"firm, 16 px: mean {firm.Mean:0.0} max {firm.Max} covered {firm.Covered:P0}");
        Assert.True(firm.Mean <= firm.Max * 0.92, $"mean {firm.Mean:0.0} of a maximum {firm.Max}: a flat band, the paper is not in it");
        Assert.Equal(PaperKind.Plain, Pencil().TextureSurface);
    }
}
