using System.Security.Cryptography;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.Raster.Tests;

public class BrushEngineTests
{
    private static Stroke Line(double x0, double y0, double x1, double y1, Action<Stroke>? mutate = null)
    {
        var s = new Stroke
        {
            Color = "#000000",
            Brush = new BrushSettings { Size = 10, Hardness = 1, Opacity = 1, Spacing = 0.15 },
            Points = [new(x0, y0, 0.5), new(x1, y1, 0.5)],
        };
        mutate?.Invoke(s);
        return s;
    }

    private static string HashOf(SKBitmap bmp) =>
        Convert.ToHexString(SHA256.HashData(bmp.GetPixelSpan()));

    [Fact]
    public void Rasterize_SameStrokes_IdenticalPixels()
    {
        var strokes = new[]
        {
            Line(10, 10, 90, 40),
            Line(20, 80, 70, 20, s => { s.Color = "#ff0000"; s.Brush.Hardness = 0.3; }),
        };
        using var a = FrameRasterizer.Rasterize(strokes, 100, 100);
        using var b = FrameRasterizer.Rasterize(strokes, 100, 100);
        Assert.Equal(HashOf(a), HashOf(b));
    }

    [Fact]
    public void Rasterize_MarksPixelsAlongThePath()
    {
        using var bmp = FrameRasterizer.Rasterize([Line(10, 50, 90, 50)], 100, 100);
        // Pixels on the line are opaque black; far corners stay transparent.
        Assert.True(bmp.GetPixel(50, 50).Alpha > 200);
        Assert.True(bmp.GetPixel(10, 50).Alpha > 200);
        Assert.True(bmp.GetPixel(90, 50).Alpha > 200);
        Assert.Equal(0, bmp.GetPixel(5, 5).Alpha);
        Assert.Equal(0, bmp.GetPixel(95, 95).Alpha);
    }

    [Fact]
    public void PressureControlsDabRadius()
    {
        var thin = Line(10, 50, 90, 50, s => s.Points = [new(10, 50, 0.2), new(90, 50, 0.2)]);
        var thick = Line(10, 50, 90, 50, s => s.Points = [new(10, 50, 1.0), new(90, 50, 1.0)]);

        using var thinBmp = FrameRasterizer.Rasterize([thin], 100, 100);
        using var thickBmp = FrameRasterizer.Rasterize([thick], 100, 100);

        // size=10 → radius 1 at p=0.2 vs radius 5 at p=1.0: y=53 is inside
        // the thick line but outside the thin one.
        Assert.Equal(0, thinBmp.GetPixel(50, 53).Alpha);
        Assert.True(thickBmp.GetPixel(50, 53).Alpha > 200);
    }

    [Fact]
    public void StrokeOpacity_DoesNotStackWithinAStroke()
    {
        // A self-overlapping half-opacity stroke should read ~0.5 alpha
        // everywhere, including where it crosses itself.
        var s = Line(20, 50, 80, 50, x =>
        {
            x.Brush.Opacity = 0.5;
            x.Points = [new(20, 50, 1), new(80, 50, 1), new(20, 50, 1)];
        });
        using var bmp = FrameRasterizer.Rasterize([s], 100, 100);
        var alpha = bmp.GetPixel(50, 50).Alpha;
        Assert.InRange(alpha, 100, 155); // ~127 ± tolerance
    }

    [Fact]
    public void Eraser_RemovesPaintedPixels()
    {
        var paint = Line(10, 50, 90, 50);
        var erase = Line(50, 10, 50, 90, s => s.Tool = ToolKind.Eraser);
        using var bmp = FrameRasterizer.Rasterize([paint, erase], 100, 100);

        Assert.Equal(0, bmp.GetPixel(50, 50).Alpha); // erased at the crossing
        Assert.True(bmp.GetPixel(15, 50).Alpha > 200); // rest of the line intact
    }

    [Fact]
    public void SoftBrush_FallsOffTowardTheRim()
    {
        var s = Line(50, 50, 50, 50, x =>
        {
            x.Brush.Size = 40;
            x.Brush.Hardness = 0.1;
            x.Points = [new(50, 50, 1)];
        });
        using var bmp = FrameRasterizer.Rasterize([s], 100, 100);
        var center = bmp.GetPixel(50, 50).Alpha;
        var nearRim = bmp.GetPixel(50, 50 - 17).Alpha;
        Assert.True(center > 200);
        Assert.True(nearRim < center / 2);
    }

    [Fact]
    public void DabPositions_SpacingFollowsArcLength()
    {
        var s = Line(0, 0, 100, 0);
        s.Brush.Size = 10;
        s.Brush.Spacing = 0.5;
        s.Points = [new(0, 0, 1), new(100, 0, 1)]; // full pressure: 5px steps
        var dabs = BrushEngine.DabPositions(s).ToList();
        Assert.Equal(21, dabs.Count); // origin + 20 steps
        Assert.Equal(0.0, dabs[0].Pos.X, 3);
        Assert.Equal(5.0, dabs[1].Pos.X, 3);
        Assert.Equal(100.0, dabs[^1].Pos.X, 3);
    }

    [Fact]
    public void DabPositions_SpacingShrinksWithPressure()
    {
        // Light pressure halves the dab, so the step halves with it — the
        // fix for pen strokes degenerating into dotted stamp trails.
        var s = Line(0, 0, 100, 0);
        s.Brush.Size = 10;
        s.Brush.Spacing = 0.5;
        s.Points = [new(0, 0, 0.5), new(100, 0, 0.5)];
        var dabs = BrushEngine.DabPositions(s).ToList();
        Assert.Equal(41, dabs.Count); // origin + 40 steps of 2.5px
        Assert.Equal(2.5, dabs[1].Pos.X, 3);

        // With the per-brush pressure master switch off, size (and therefore
        // spacing) ignores pressure entirely.
        s.Brush.PressureEnabled = false;
        Assert.Equal(21, BrushEngine.DabPositions(s).Count());
    }

    [Fact]
    public void Append_MatchesBatchRasterization()
    {
        var s1 = Line(10, 10, 90, 40);
        var s2 = Line(20, 80, 70, 20, s => s.Color = "#0000ff");

        using var batch = FrameRasterizer.Rasterize([s1, s2], 100, 100);

        using var incremental = FrameRasterizer.Rasterize([s1], 100, 100);
        FrameRasterizer.Append(incremental, s2);

        Assert.Equal(HashOf(batch), HashOf(incremental));
    }
}

public class PngCodecTests
{
    [Fact]
    public void EncodeDecode_RoundTripsPixels()
    {
        using var bmp = FrameRasterizer.Rasterize(
            [new Stroke { Points = [new(5, 5, 1)], Brush = new BrushSettings { Size = 6, Hardness = 1 } }],
            20, 20);
        var b64 = PngCodec.Encode(bmp);
        using var back = PngCodec.Decode(b64);
        Assert.Equal(bmp.Width, back.Width);
        Assert.Equal(bmp.Height, back.Height);
        Assert.Equal(bmp.GetPixel(5, 5), back.GetPixel(5, 5));
        Assert.Equal(bmp.GetPixel(0, 19), back.GetPixel(0, 19));
    }

    [Fact]
    public void Materialize_CompositesBaselinePlusStrokes()
    {
        var stroke = new Stroke
        {
            Color = "#000000",
            Points = [new(10, 10, 1)],
            Brush = new BrushSettings { Size = 8, Hardness = 1 },
        };
        var frame = new Frame { Strokes = [stroke] };

        // No baseline → pixels come from the stroke record alone.
        using var fromStrokes = FrameRasterizer.Materialize(frame, 32, 32);
        Assert.True(fromStrokes.GetPixel(10, 10).Alpha > 200);
        Assert.Equal(0, fromStrokes.GetPixel(25, 25).Alpha);

        // With a baseline (red dot at 25,25) → baseline shows AND strokes stamp on top.
        using (var baseline = FrameRasterizer.Rasterize(
                   [new Stroke
                   {
                       Color = "#ff0000",
                       Points = [new(25, 25, 1)],
                       Brush = new BrushSettings { Size = 6, Hardness = 1 },
                   }], 32, 32))
        {
            frame.PngBase64 = PngCodec.Encode(baseline);
        }
        using var combined = FrameRasterizer.Materialize(frame, 32, 32);
        Assert.True(combined.GetPixel(10, 10).Alpha > 200); // stroke on top
        Assert.True(combined.GetPixel(25, 25).Red > 200);   // baseline preserved

        // An eraser stroke in the record erases baseline pixels too.
        frame.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Eraser,
            Points = [new(25, 25, 1)],
            Brush = new BrushSettings { Size = 10, Hardness = 1 },
        });
        using var erased = FrameRasterizer.Materialize(frame, 32, 32);
        Assert.Equal(0, erased.GetPixel(25, 25).Alpha);
    }
}
