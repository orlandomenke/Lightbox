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
    public void PaperTextureCommit_DoesNotStallThePen()
    {
        // Texture built its mask with SetPixel per pixel, which over a
        // stroke-sized region is millions of bounds-checked calls that repack
        // an SKColor each time: it cost 3.0 s on this stroke. Filling a byte
        // buffer and installing it once is 225 ms.
        var info = new SKImageInfo(3840, 2160, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)!;
        var stroke = Stroke(500, wet: 0, gran: 0);
        stroke.Brush.TextureSurface = PaperKind.Rough;
        stroke.Brush.TextureScale = 12;
        stroke.Brush.TextureDepth = 0.7;

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
        Assert.True(median < 1200,
            $"textured commit on a 4K canvas took {median:0} ms — the per-pixel path is back");
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

/// <summary>
/// Smudge carries canvas colour and never its own. The first dab is the
/// interesting case: it has nothing carried yet, so what it does decides
/// whether the brush feels alive on contact or dead until you move.
/// </summary>
public class SmudgeFirstDabTests
{
    private static SKBitmap TwoTone(int w = 200, int h = 120)
    {
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bmp = new SKBitmap(info);
        using var surface = SKSurface.Create(info)!;
        // A hard vertical boundary at x = 100: black left, white right.
        surface.Canvas.Clear(SKColors.White);
        using (var p = new SKPaint { Color = SKColors.Black })
        {
            surface.Canvas.DrawRect(new SKRect(0, 0, 100, h), p);
        }
        surface.Canvas.Flush();
        using var image = surface.Snapshot();
        image.ReadPixels(info, bmp.GetPixels(), bmp.RowBytes, 0, 0);
        return bmp;
    }

    private static Stroke Tap(double x, double y, double size) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#ff0000", // must never appear: smudge carries canvas colour only
        Points = [new StrokePoint(x, y, 1)],
        Brush = new BrushSettings
        {
            Kind = BrushKind.Smudge, Size = size, Hardness = 1, Flow = 1, Spacing = 0.1,
        },
    };

    [Fact]
    public void ASingleTapOnABoundary_SoftensIt_RatherThanDoingNothing()
    {
        using var canvas = TwoTone();
        var before = canvas.GetPixel(100, 60);

        var info = new SKImageInfo(canvas.Width, canvas.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var skCanvas = new SKCanvas(canvas);
        BrushEngine.StampStroke(skCanvas, Tap(100, 60, 40), info, canvas);
        skCanvas.Flush();

        var after = canvas.GetPixel(100, 60);
        Assert.True(after != before,
            "a tap straddling a hard boundary should deposit the averaged pickup, not nothing");
        // The averaged pickup of black and white is a grey, so the black side
        // must lighten rather than take on the brush's red.
        Assert.Equal(after.Red, after.Green);
        Assert.Equal(after.Green, after.Blue);
    }

    [Fact]
    public void ATapOnFlatColour_ChangesNothing()
    {
        using var canvas = TwoTone();
        var before = canvas.GetPixel(40, 60); // deep in the black field

        var info = new SKImageInfo(canvas.Width, canvas.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var skCanvas = new SKCanvas(canvas);
        BrushEngine.StampStroke(skCanvas, Tap(40, 60, 30), info, canvas);
        skCanvas.Flush();

        Assert.Equal(before, canvas.GetPixel(40, 60));
    }

    [Fact]
    public void SmudgeNeverDepositsTheBrushColour()
    {
        using var canvas = TwoTone();
        var stroke = Tap(100, 60, 40);
        stroke.Points = [new StrokePoint(60, 60, 1), new StrokePoint(140, 60, 1)];

        var info = new SKImageInfo(canvas.Width, canvas.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var skCanvas = new SKCanvas(canvas);
        BrushEngine.StampStroke(skCanvas, stroke, info, canvas);
        skCanvas.Flush();

        for (var x = 20; x < 180; x++)
        for (var y = 30; y < 90; y += 7)
        {
            var px = canvas.GetPixel(x, y);
            Assert.True(px.Red == px.Green && px.Green == px.Blue,
                $"smudge introduced colour at ({x},{y}): {px} — it must only ever carry what was already there");
        }
    }
}
