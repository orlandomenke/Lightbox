using System.Diagnostics;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Wet edge and granulation are what make watercolour and gouache read as
/// paint. They used to cost ten seconds on a large stroke, which the artist
/// felt as the pen sticking to the page at the end of every mark.
/// </summary>
public class TexturedBrushTests
{
    private static Stroke Stroke(double size, double wet, double gran, int points = 40)
    {
        var pts = new List<StrokePoint>();
        for (var i = 0; i < points; i++)
            pts.Add(new StrokePoint(200 + i * 40, 400 + Math.Sin(i * 0.3) * 250, 0.85));
        return new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#3a6ea5",
            Points = pts,
            Brush = new BrushSettings
            {
                Size = size, Hardness = 0.35, Opacity = 0.85, Flow = 0.7,
                WetEdge = wet, Granulation = gran, Spacing = 0.08,
            },
        };
    }

    private static SKBitmap Render(Stroke stroke, int w = 1600, int h = 900)
    {
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bmp = new SKBitmap(info);
        using var surface = SKSurface.Create(info)!;
        surface.Canvas.Clear(SKColors.White);
        BrushEngine.StampStroke(surface.Canvas, stroke, info);
        surface.Canvas.Flush();
        using var image = surface.Snapshot();
        image.ReadPixels(info, bmp.GetPixels(), bmp.RowBytes, 0, 0);
        return bmp;
    }

    [Fact]
    public void WetEdge_DarkensTheOutline_NotTheInterior()
    {
        using var plain = Render(Stroke(160, wet: 0, gran: 0, points: 2));
        using var wet = Render(Stroke(160, wet: 0.8, gran: 0, points: 2));

        // The first stroke point is at (200,400) with a 160 px brush, so the
        // centre is solid interior and the rim sits near the 80 px radius.
        // The deep interior must be untouched — a wet edge that darkens the
        // whole mark is the bug this replaced, not a wet edge.
        for (var r = 0; r <= 16; r += 4)
        {
            var plainPx = plain.GetPixel(200, 400 - r);
            var wetPx = wet.GetPixel(200, 400 - r);
            Assert.True(Math.Abs(wetPx.Red - plainPx.Red) <= 1,
                $"interior at r={r} should be untouched, was {plainPx.Red} → {wetPx.Red}");
        }

        // And somewhere along the outline it must actually darken.
        var deepest = 0;
        var deepestAt = -1;
        for (var r = 20; r <= 78; r++)
        {
            var plainPx = plain.GetPixel(200, 400 - r);
            var wetPx = wet.GetPixel(200, 400 - r);
            var delta = plainPx.Red - wetPx.Red;
            if (delta > deepest) { deepest = delta; deepestAt = r; }
        }
        Assert.True(deepest >= 3,
            $"wet edge produced no darker band along the outline (deepest {deepest} at r={deepestAt})");
        Assert.True(deepestAt >= 24,
            $"the darkening peaked at r={deepestAt}, which is interior, not outline");
    }

    [Fact]
    public void Granulation_IsDeterministic_AndAnchoredToTheDocument()
    {
        using var first = Render(Stroke(160, wet: 0, gran: 0.6, points: 3));
        using var again = Render(Stroke(160, wet: 0, gran: 0.6, points: 3));
        for (var x = 150; x < 400; x += 17)
        for (var y = 300; y < 500; y += 19)
            Assert.Equal(first.GetPixel(x, y), again.GetPixel(x, y));

        // It must actually vary the alpha, or the tile is not being sampled.
        using var flat = Render(Stroke(160, wet: 0, gran: 0.0, points: 3));
        var differing = 0;
        for (var x = 150; x < 400; x += 3)
        for (var y = 340; y < 460; y += 3)
            if (first.GetPixel(x, y) != flat.GetPixel(x, y)) differing++;
        Assert.True(differing > 100, $"granulation changed almost nothing ({differing} pixels)");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void TexturedStrokeCommit_DoesNotStallThePen()
    {
        var info = new SKImageInfo(3840, 2160, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)!;
        var stroke = Stroke(500, wet: 0.7, gran: 0.35);

        BrushEngine.StampStroke(surface.Canvas, stroke, info); // warm
        var times = new List<double>();
        var sw = new Stopwatch();
        for (var i = 0; i < 5; i++)
        {
            sw.Restart();
            BrushEngine.StampStroke(surface.Canvas, stroke, info);
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        var median = times[times.Count / 2];

        // A morphological erode put this at ~10 400 ms. The budget is loose
        // enough for a loaded CI box but an order of magnitude under that.
        Assert.True(median < 2000,
            $"watercolour commit on a 4K canvas took {median:0} ms — the pen-lift stall is back");
    }
}
