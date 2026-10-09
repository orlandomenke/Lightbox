using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Paint that picks up what it is laid on (Q232, <c>docs/DESIGN-colour-mixing.md</c>):
/// a dab samples the ground under it, carries that colour along the stroke, and
/// lays its own paint mixed toward it — in pigment, so yellow into blue goes green.
/// </summary>
public class ColourMixingTests(ITestOutputHelper output)
{
    private const int W = 320, H = 120;

    private static SKBitmap Field(SKColor colour)
    {
        var bitmap = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(colour);
        return bitmap;
    }

    private static Stroke Line(string colour, double x0, double x1, double y, ColourMixing? mixing, double size = 30) => new()
    {
        Color = colour,
        Points = [new StrokePoint(x0, y, 1), new StrokePoint((x0 + x1) / 2, y, 1), new StrokePoint(x1, y, 1)],
        Brush = new BrushSettings { Size = size, Hardness = 0.9, Opacity = 1, Flow = 1, Spacing = 0.15, Mixing = mixing },
    };

    private static SKBitmap Render(SKBitmap layer, Stroke stroke)
    {
        var copy = layer.Copy();
        FrameRasterizer.Append(copy, stroke);
        return copy;
    }

    [Fact]
    public void YellowOverBlueGoesGreenNotGrey()
    {
        using var blue = Field(new SKColor(30, 60, 220));
        using var mixed = Render(blue, Line("#f0e020", 40, 280, 60, new ColourMixing { Amount = 0.5, Length = 0.5 }));
        using var plain = Render(blue, Line("#f0e020", 40, 280, 60, null));

        var m = mixed.GetPixel(200, 60);
        var p = plain.GetPixel(200, 60);
        output.WriteLine($"mixed {m}  plain {p}");
        // Without mixing the brush's own yellow lands. With it, the pigment mix
        // of yellow and blue is green: green above red, and well above blue.
        // Both the unmixed yellow (green 16 below red) and a source-over lerp
        // of the two at the share the brush picks up (green 3 below red) fail
        // the first clause — which is the one that says pigment rather than
        // film. Measured, not chosen.
        Assert.True(p.Red > 200 && p.Green > 180, "the unmixed stroke should be the brush's yellow");
        Assert.True(m.Red < p.Red - 40, $"the brush never picked up the blue: {m} against {p}");
        Assert.True(m.Green > m.Red + 10 && m.Green > m.Blue + 40,
            $"yellow mixed into blue should read green, got {m}");
    }

    [Fact]
    public void OnABlankCanvasAMixingBrushIsExactlyItselfWithoutMixing()
    {
        using var blank = Field(SKColors.Transparent);
        using var mixed = Render(blank, Line("#f0e020", 40, 280, 60, new ColourMixing { Amount = 0.3, Length = 0.8 }));
        using var plain = Render(blank, Line("#f0e020", 40, 280, 60, null));
        // Nothing under the brush means nothing to mix with: byte-identical, so
        // turning the option on cannot shift a mark that had nothing to pick up.
        var worst = WorstDifference(mixed, plain);
        var count = CountDifferent(mixed, plain);
        output.WriteLine($"worst channel difference {worst}, differing pixels {count}");
        Assert.Equal(0, worst);
    }

    [Theory]
    [InlineData(0.1, 0.9)]
    public void LengthCarriesAPickedUpColourFurther(double shortLength, double longLength)
    {
        // A blue patch under the first third of the stroke, blank after it. The
        // blue the brush picks up there travels on by Length: further at 0.9
        // than at 0.1, which is what Length means on the slider.
        using var layer = Field(SKColors.Transparent);
        using (var c = new SKCanvas(layer)) c.DrawRect(SKRect.Create(0, 0, 110, H), new SKPaint { Color = new SKColor(30, 60, 220) });

        using var shortRun = Render(layer, Line("#f0e020", 40, 280, 60, new ColourMixing { Amount = 0.5, Length = shortLength }));
        using var longRun = Render(layer, Line("#f0e020", 40, 280, 60, new ColourMixing { Amount = 0.5, Length = longLength }));

        var s = shortRun.GetPixel(170, 60);
        var l = longRun.GetPixel(170, 60);
        output.WriteLine($"60 px past the patch: length {shortLength} -> {s}, length {longLength} -> {l}");
        // Blueness past the patch: less red than yellow has. The long length
        // keeps more of the blue, so it is less red and less green there.
        Assert.True(l.Red < s.Red - 10, $"a longer length should carry the blue further: {l} vs {s}");
    }

    /// <summary>
    /// Invariant 7: a 2x render is the same mark, sharper. The sampling ring is
    /// in device pixels, so it has to scale with the output; left in document
    /// units it read half as far out at 2x and the stroke drifted toward its
    /// own colour sooner (the adversary).
    /// </summary>
    [Fact]
    public void AMixingStrokeReadsTheSameGroundAtTwiceTheOutputScale()
    {
        var blue = new SKColor(30, 60, 220);
        var under = new Stroke
        {
            Color = "#1e3cdc",
            Points = [new StrokePoint(0, 60, 1), new StrokePoint(W, 60, 1)],
            Brush = new BrushSettings { Size = 120, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.1 },
        };
        var over = Line("#f0e020", 40, 280, 60, new ColourMixing { Amount = 0.5, Length = 0.5 });
        using var one = FrameRasterizer.Rasterize([under, over], W, H);
        using var two = FrameRasterizer.Rasterize([under, over], W, H, outputScale: 2.0);

        // The same document point, far enough along for the carry to have settled.
        var a = one.GetPixel(240, 60);
        var b = two.GetPixel(480, 120);
        output.WriteLine($"1x {a}  2x {b}  (field {blue})");
        Assert.True(Math.Abs(a.Red - b.Red) <= 8 && Math.Abs(a.Green - b.Green) <= 8 && Math.Abs(a.Blue - b.Blue) <= 8,
            $"the 2x render picked up a different ground: {a} at 1x against {b} at 2x");
    }

    [Fact]
    public void MixingIsDeterministic()
    {
        using var blue = Field(new SKColor(30, 60, 220));
        var stroke = Line("#f0e020", 40, 280, 60, new ColourMixing { Amount = 0.5, Length = 0.6 });
        using var a = Render(blue, stroke);
        using var b = Render(blue, stroke);
        Assert.Equal(0, WorstDifference(a, b));
    }

    [Fact]
    public void AMixingBrushIsNotASilhouetteButStillSubdivides()
    {
        // A silhouette computes coverage once for the whole mark and has no
        // per-dab colour, so a mixing brush cannot take it. Subdivision it keeps:
        // the sub-dabs each sample their own ground, and that is what lets a
        // mixing brush on a blank canvas stay byte-identical to itself without.
        var brush = new BrushSettings { Size = 30, Hardness = 1, Mixing = new ColourMixing() };
        Assert.False(BrushEngine.DrawsAsOneSilhouette(brush));
        brush.Hardness = 0.9;
        Assert.True(BrushEngine.SubdividesForFidelity(brush));
        brush.Mixing = null;
        brush.Hardness = 1;
        Assert.True(BrushEngine.DrawsAsOneSilhouette(brush));
    }

    private static int CountDifferent(SKBitmap a, SKBitmap b)
    {
        using var pa = a.PeekPixels();
        using var pb = b.PeekPixels();
        var sa = pa.GetPixelSpan();
        var sb = pb.GetPixelSpan();
        var n = 0;
        for (var i = 0; i < sa.Length; i += 4)
        {
            if (sa[i] != sb[i] || sa[i + 1] != sb[i + 1] || sa[i + 2] != sb[i + 2] || sa[i + 3] != sb[i + 3]) n++;
        }
        return n;
    }

    private static int WorstDifference(SKBitmap a, SKBitmap b)
    {
        using var pa = a.PeekPixels();
        using var pb = b.PeekPixels();
        var sa = pa.GetPixelSpan();
        var sb = pb.GetPixelSpan();
        var worst = 0;
        for (var i = 0; i < sa.Length; i++) worst = Math.Max(worst, Math.Abs(sa[i] - sb[i]));
        return worst;
    }
}
