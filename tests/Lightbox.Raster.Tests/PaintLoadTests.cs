using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// A loaded brush runs out. B26: <c>PaintLoad</c> is documented as "paint on
/// the brush at the start of a stroke — below 1 the brush runs out as the
/// stroke lengthens, which is what dry-brush is", and it was a flat multiplier
/// that made the whole mark evenly transparent instead.
/// </summary>
public class PaintLoadTests(ITestOutputHelper output)
{
    private const int W = 900, H = 200;

    private static Stroke Mark(double load, MediumKind kind = MediumKind.None) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#202020",
        Points = Enumerable.Range(0, 40)
            .Select(i => new StrokePoint(40 + i * 20, 100, 1))
            .ToList(),
        Brush = new BrushSettings
        {
            Size = 24, Hardness = 0.8, Opacity = 1, Flow = 1, Spacing = 0.08,
            PressureFlowGamma = 1,
            Medium = new MediumSettings { Kind = kind, PaintLoad = load },
        },
    };

    private static SKBitmap Render(Stroke stroke)
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        var layer = new SKBitmap(info);
        using var canvas = new SKCanvas(layer);
        canvas.Clear(SKColors.Transparent);
        BrushEngine.StampStroke(canvas, stroke, info, layer);
        canvas.Flush();
        return layer;
    }

    /// <summary>Mean alpha down a column, so the profile is along the stroke.</summary>
    private static double At(SKBitmap bmp, int x)
    {
        // Inside the brush's own width — a 24 px brush over a 40 px band would
        // average in bare paper and never read as full however loaded it is.
        double sum = 0;
        for (var y = 94; y <= 106; y++) sum += bmp.GetPixel(x, y).Alpha / 255.0;
        return sum / 13;
    }

    [Fact]
    public void ALoadedBrushStartsFullAndRunsOut()
    {
        // The defect, stated as the artist's sentence. Before the fix a load
        // of 0.5 painted at half strength from the first dab to the last.
        using var bmp = Render(Mark(0.35));

        double start = At(bmp, 80), middle = At(bmp, 420), end = At(bmp, 780);
        output.WriteLine($"start {start:F3}  middle {middle:F3}  end {end:F3}");

        Assert.True(start > 0.9, $"the brush did not start full: {start:F3}");
        Assert.True(middle < start * 0.85, $"it did not fade: {start:F3} -> {middle:F3}");
        Assert.True(end < middle * 0.85, $"it stopped fading halfway: {middle:F3} -> {end:F3}");
    }

    [Fact]
    public void AFullBrushNeverRunsOut()
    {
        // The default, and it has to be a byte-for-byte no-op or every drawing
        // ever made with a medium moves.
        using var bmp = Render(Mark(1));

        double start = At(bmp, 80), end = At(bmp, 780);
        Assert.True(start > 0.9 && end > 0.9, $"a full brush faded: {start:F3} -> {end:F3}");
        Assert.Equal(start, end, 2);
    }

    [Fact]
    public void ALessLoadedBrushRunsOutSooner()
    {
        // Monotone in the control, which is the property the old flat
        // multiplier also had — so on its own this would not have caught the
        // bug. It is here to stop the fix regressing into a different flat
        // multiplier.
        using var wet = Render(Mark(0.6));
        using var dry = Render(Mark(0.2));

        Assert.True(At(dry, 500) < At(wet, 500),
            $"a drier brush was not drier: {At(dry, 500):F3} vs {At(wet, 500):F3}");
        // Both start full: that is what separates running out from being faint.
        Assert.True(At(dry, 80) > 0.9 && At(wet, 80) > 0.9);
    }

    [Fact]
    public void ABiggerBrushCarriesFurther()
    {
        // The length scale is in brush diameters, so resizing a brush does not
        // silently re-tune its load. Measured at the same absolute distance.
        var small = Mark(0.3);
        small.Brush.Size = 12;
        var large = Mark(0.3);
        large.Brush.Size = 48;

        using var s = Render(small);
        using var l = Render(large);

        Assert.True(At(l, 600) > At(s, 600),
            $"the bigger brush ran out first: {At(l, 600):F3} vs {At(s, 600):F3}");
    }

    [Fact]
    public void ItWorksWithoutASimulatedMedium()
    {
        // Dry-brush is not a watercolour effect. The setting lives on
        // MediumSettings for historical reasons, but depletion happens in the
        // dab walk, so a plain brush gets it too — and that is the honest
        // meaning of the word.
        using var bmp = Render(Mark(0.3, MediumKind.None));

        Assert.True(At(bmp, 80) > 0.9);
        Assert.True(At(bmp, 780) < 0.5, "a plain brush did not run out");
    }

    [Fact]
    public void RunningOutIsAsRepeatableAsEverythingElse()
    {
        // Invariant 2. Depletion is a function of arc length and nothing else —
        // no clock, no index, no RNG — so two renders are identical.
        using var first = Render(Mark(0.4));
        using var again = Render(Mark(0.4));
        for (var y = 0; y < H; y += 2)
        for (var x = 0; x < W; x += 2)
            Assert.Equal(first.GetPixel(x, y), again.GetPixel(x, y));
    }

    [Fact]
    public void LoadIsNotAppliedTwiceWhenAMediumIsOn()
    {
        // MediumSimulator used to multiply pigment by PaintLoad as well. With
        // depletion in the dab walk that would double it, and the medium mark
        // would be the square of the plain one.
        using var plain = Render(Mark(0.5, MediumKind.None));
        using var wet = Render(Mark(0.5, MediumKind.Watercolour));

        double p = At(plain, 80), w = At(wet, 80);
        output.WriteLine($"start: plain {p:F3}, watercolour {w:F3}");

        // A medium is allowed to be lighter — PigmentDensity says so — but not
        // by the square of the load on top of it.
        Assert.True(w > p * 0.3, $"the medium start is {w / p:F2} of the plain one, which is load applied twice");
    }
}
