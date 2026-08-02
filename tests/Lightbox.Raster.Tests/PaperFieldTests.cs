using System.Diagnostics;
using Lightbox.Core.Documents;
using Lightbox.Raster.Media;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The paper surface. What these tests defend is not "it produces numbers"
/// but the four properties the rest of the medium simulation leans on: the
/// field is the same on every run, it has no seam where the tile repeats,
/// the four sheets are measurably different surfaces, and asking for a
/// bigger grain actually gets you a bigger grain.
/// </summary>
[Collection("Performance")]
public class PaperFieldTests
{
    private const double Scale = 12;

    /// <summary>
    /// Height statistics over a square region: standard deviation is tooth
    /// depth, and the mean absolute step between neighbours is tooth
    /// frequency. Their ratio (<see cref="Stats.Wavelength"/>) is a
    /// characteristic wavelength in pixels, independent of depth — which is
    /// what lets depth and wavelength be asserted separately.
    /// </summary>
    private readonly record struct Stats(
        double Mean, double Min, double Max, double Std, double StepX, double StepY)
    {
        public double Wavelength => Std / StepX;
        public double Anisotropy => StepX / StepY;
    }

    private static Stats Measure(PaperKind kind, double scale, int n = 512, int originX = 0, int originY = 0)
    {
        var h = new float[n * n];
        PaperField.Fill(h, n, n, originX, originY, kind, scale);

        double sum = 0, min = double.MaxValue, max = double.MinValue;
        foreach (var value in h)
        {
            sum += value;
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }
        var mean = sum / h.Length;

        double variance = 0, stepX = 0, stepY = 0;
        foreach (var value in h) variance += (value - mean) * (value - mean);
        for (var y = 0; y < n - 1; y++)
        {
            for (var x = 0; x < n - 1; x++)
            {
                stepX += Math.Abs(h[y * n + x + 1] - h[y * n + x]);
                stepY += Math.Abs(h[(y + 1) * n + x] - h[y * n + x]);
            }
        }
        var pairs = (n - 1.0) * (n - 1.0);
        return new Stats(mean, min, max, Math.Sqrt(variance / h.Length), stepX / pairs, stepY / pairs);
    }

    /// <summary>
    /// Push enough distinct sheets through the cache to evict everything, so
    /// the next request has to build its tile again from scratch. Without
    /// this a "determinism" test only proves the cache returns the array it
    /// stored.
    /// </summary>
    private static void EvictCache()
    {
        for (var i = 0; i < 20; i++)
        {
            PaperField.HeightAt(0, 0, PaperKind.Smooth, 20 + i);
        }
    }

    [Fact]
    public void RebuiltTileIsBitIdentical()
    {
        var before = new float[64];
        for (var i = 0; i < before.Length; i++)
        {
            before[i] = PaperField.HeightAt(i * 37 - 400, i * 11 + 3, PaperKind.ColdPress, Scale);
        }

        // The samples must actually vary, or equality below proves nothing.
        Assert.True(before.Distinct().Count() > 50, "sample points are not independent");

        EvictCache();

        for (var i = 0; i < before.Length; i++)
        {
            var again = PaperField.HeightAt(i * 37 - 400, i * 11 + 3, PaperKind.ColdPress, Scale);
            Assert.Equal(before[i], again); // exact: the field is bit-reproducible, not merely close
        }
    }

    [Theory]
    [InlineData(PaperKind.Smooth)]
    [InlineData(PaperKind.ColdPress)]
    [InlineData(PaperKind.Rough)]
    [InlineData(PaperKind.Canvas)]
    public void FillAgreesWithHeightAtExactly(PaperKind kind)
    {
        // Straddles the origin, the negative quadrant and the tile wrap.
        var period = PaperField.TilePeriod(kind, Scale);
        foreach (var (ox, oy) in new[] { (0, 0), (-37, -1), (period - 5, 3), (5 * period + 9, -2 * period - 7) })
        {
            const int w = 23, hgt = 19;
            var buf = new float[w * hgt];
            PaperField.Fill(buf, w, hgt, ox, oy, kind, Scale);
            for (var y = 0; y < hgt; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    Assert.Equal(PaperField.HeightAt(ox + x, oy + y, kind, Scale), buf[y * w + x]);
                }
            }
        }
    }

    [Theory]
    [InlineData(PaperKind.Smooth)]
    [InlineData(PaperKind.ColdPress)]
    [InlineData(PaperKind.Rough)]
    [InlineData(PaperKind.Canvas)]
    public void TileWrapsWithoutASeam(PaperKind kind)
    {
        var period = PaperField.TilePeriod(kind, Scale);
        const int span = 256;

        // A window centred on the wrap: column `span/2` of `strip` is the
        // last column of one tile, `span/2 + 1` the first of the next.
        var strip = new float[span * span];
        PaperField.Fill(strip, span, span, period - span / 2, period - span / 2, kind, Scale);

        double seamX = 0, seamY = 0, interiorX = 0, interiorY = 0;
        var mid = span / 2;
        for (var i = 1; i < span - 1; i++)
        {
            seamX += Math.Abs(strip[i * span + mid] - strip[i * span + mid - 1]);
            seamY += Math.Abs(strip[mid * span + i] - strip[(mid - 1) * span + i]);
        }
        seamX /= span - 2;
        seamY /= span - 2;

        var quarter = span / 4;
        for (var i = 1; i < span - 1; i++)
        {
            interiorX += Math.Abs(strip[i * span + quarter] - strip[i * span + quarter - 1]);
            interiorY += Math.Abs(strip[quarter * span + i] - strip[(quarter - 1) * span + i]);
        }
        interiorX /= span - 2;
        interiorY /= span - 2;

        // A discontinuity at the wrap shows up as a step far larger than the
        // ordinary pixel-to-pixel step; a mismatched slope would show as a
        // milder but still clear excess. 1.6x leaves room for the wrap
        // landing on a genuinely busy part of the field.
        Assert.True(seamX < interiorX * 1.6,
            $"vertical seam: {seamX:F5} across the wrap vs {interiorX:F5} inside the tile");
        Assert.True(seamY < interiorY * 1.6,
            $"horizontal seam: {seamY:F5} across the wrap vs {interiorY:F5} inside the tile");
    }

    [Theory]
    [InlineData(PaperKind.Smooth)]
    [InlineData(PaperKind.ColdPress)]
    [InlineData(PaperKind.Rough)]
    [InlineData(PaperKind.Canvas)]
    public void HeightStaysInRangeAndCentred(PaperKind kind)
    {
        var s = Measure(kind, Scale);
        Assert.InRange(s.Min, 0, 1);
        Assert.InRange(s.Max, 0, 1);
        // Callers modulate deposition by height, so a field that drifted off
        // 0.5 would silently darken or lighten every stroke on the sheet.
        Assert.InRange(s.Mean, 0.45, 0.55);
    }

    [Fact]
    public void ToothDepthSeparatesTheThreePapers()
    {
        var smooth = Measure(PaperKind.Smooth, Scale);
        var cold = Measure(PaperKind.ColdPress, Scale);
        var rough = Measure(PaperKind.Rough, Scale);

        // Hot-pressed is near flat: an order of magnitude below cold-press.
        Assert.True(smooth.Std * 3 < cold.Std,
            $"Smooth ({smooth.Std:F4}) is not much flatter than ColdPress ({cold.Std:F4})");
        // Rough is deeper again, by a clear margin rather than by noise.
        Assert.True(rough.Std > cold.Std * 1.25,
            $"Rough ({rough.Std:F4}) is not deeper than ColdPress ({cold.Std:F4})");
    }

    [Fact]
    public void RoughHasTheLongerWavelength()
    {
        var cold = Measure(PaperKind.ColdPress, Scale);
        var rough = Measure(PaperKind.Rough, Scale);

        // Depth alone would let Rough be "cold-press turned up"; the point of
        // Rough is bigger hollows, so its characteristic wavelength has to be
        // longer independently of how deep it is.
        Assert.True(rough.Wavelength > cold.Wavelength * 1.4,
            $"Rough wavelength {rough.Wavelength:F2}px vs ColdPress {cold.Wavelength:F2}px");
    }

    [Fact]
    public void CanvasIsDirectionalAndColdPressIsNot()
    {
        var canvas = Measure(PaperKind.Canvas, Scale);
        var cold = Measure(PaperKind.ColdPress, Scale);

        // Warp and weft at different counts and weights: crossing the warp
        // threads costs far more height change than running along them.
        Assert.True(canvas.Anisotropy > 2.0,
            $"Canvas is not woven: dx/dy energy {canvas.Anisotropy:F2}");
        // Cold-press must not be, or the two sheets would be the same idea.
        Assert.InRange(cold.Anisotropy, 0.9, 1.1);
    }

    [Theory]
    [InlineData(PaperKind.ColdPress)]
    [InlineData(PaperKind.Rough)]
    [InlineData(PaperKind.Canvas)]
    public void ScaleSetsTheWavelength(PaperKind kind)
    {
        var fine = Measure(kind, 8);
        var coarse = Measure(kind, 32);

        // Four times the grain size, so about four times the wavelength. The
        // band is wide because the tile rounds the cell count to a whole
        // number, but it excludes both "scale does nothing" and "scale does
        // something unrelated".
        var ratio = coarse.Wavelength / fine.Wavelength;
        Assert.InRange(ratio, 3.0, 5.0);

        // Depth is a property of the paper, not of the grain size: changing
        // scale must not quietly change the contrast as well.
        Assert.InRange(coarse.Std / fine.Std, 0.85, 1.18);
    }

    [Fact]
    public void DifferentScalesAreDifferentFields()
    {
        // Guards against the cache key ignoring scale, which every wavelength
        // assertion above would still pass if the tiles were merely resized.
        var a = PaperField.TilePeriod(PaperKind.ColdPress, 8);
        var b = PaperField.TilePeriod(PaperKind.ColdPress, 32);
        Assert.True(b > a, $"tile period did not grow with scale: {a} -> {b}");
    }

    [Fact]
    public void FillRejectsATooSmallDestination()
    {
        Assert.Throws<ArgumentException>(() =>
            PaperField.Fill(new float[99], 10, 10, 0, 0, PaperKind.ColdPress, Scale));
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void FillIsFastEnoughForAFullFrame()
    {
        const int w = 1600, h = 1200;
        var buf = new float[w * h];

        // Cold: the tile has to be built first. This happens once per sheet
        // per process, so it is allowed to be slower — but not slow enough to
        // be felt as a stall on the first dab.
        // Fastest of three, each with the cache evicted first — the cold path is
        // the one being measured, so it has to be cold every time.
        var coldMs = Bench.FastestMs(
            3,
            () => PaperField.Fill(buf, w, h, 0, 0, PaperKind.Rough, 47.3),
            before: EvictCache,
            warm: false);
        // The cold path builds the tile, which happens once per (kind, scale)
        // per process and never on the drawing path — so this guards against
        // an order-of-magnitude blowup, not drift. It was 200 ms, which is
        // roughly the Debug figure on an idle machine and flaked under load;
        // measured 141 ms in Release and 440 ms in Debug here. The number
        // that actually recurs is the warm fill below, and that stays tight.
        Assert.True(coldMs < 1500, $"cold fill (tile build included) took {coldMs:F1} ms");

        // Warm: what every stroke actually pays.
        var offset = 0;
        var per = Bench.FastestMs(10, () => PaperField.Fill(buf, w, h, 37 + offset++, 91, PaperKind.Rough, 47.3));
        Assert.True(per < 25, $"warm 1600x1200 fill took {per:F2} ms");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void FillCostFollowsTheRegionNotTheCanvas()
    {
        // Invariant 6: a stroke that touches a small rect must not pay for
        // the sheet. Timing a 64x64 fill against a 1024x1024 one catches a
        // rewrite that quietly evaluates the whole field per call.
        PaperField.HeightAt(0, 0, PaperKind.ColdPress, Scale);
        var small = new float[64 * 64];
        var large = new float[1024 * 1024];

        for (var i = 0; i < 50; i++) PaperField.Fill(small, 64, 64, i, i, PaperKind.ColdPress, Scale);
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 200; i++) PaperField.Fill(small, 64, 64, i, i, PaperKind.ColdPress, Scale);
        sw.Stop();
        var smallMs = sw.Elapsed.TotalMilliseconds / 200;

        PaperField.Fill(large, 1024, 1024, 0, 0, PaperKind.ColdPress, Scale);
        sw.Restart();
        for (var i = 0; i < 10; i++) PaperField.Fill(large, 1024, 1024, i, i, PaperKind.ColdPress, Scale);
        sw.Stop();
        var largeMs = sw.Elapsed.TotalMilliseconds / 10;

        // 256x the pixels; anything under 20x is not region-proportional.
        Assert.True(largeMs > smallMs * 20,
            $"64x64 took {smallMs:F4} ms and 1024x1024 took {largeMs:F4} ms — cost is not tracking the region");
    }
}

/// <summary>
/// Regression tests for holes an adversarial review found in the first cut.
/// </summary>
public class PaperFieldScaleTests
{
    private static float[] Field(PaperKind kind, double scale, int n = 96)
    {
        var buffer = new float[n * n];
        PaperField.Fill(buffer, n, n, 0, 0, kind, scale);
        return buffer;
    }

    private static bool Same(float[] a, float[] b)
    {
        for (var i = 0; i < a.Length; i++)
            if (Math.Abs(a[i] - b[i]) > 1e-6f) return false;
        return true;
    }

    [Theory]
    [InlineData(PaperKind.Smooth)]
    [InlineData(PaperKind.ColdPress)]
    [InlineData(PaperKind.Rough)]
    [InlineData(PaperKind.Canvas)]
    public void ScaleActuallyChangesTheGrain_AcrossTheUsableRange(PaperKind kind)
    {
        // The first cut clamped the cell count instead of the wavelength, so
        // several distinct scales collapsed onto a bit-identical field and the
        // slider silently stopped doing anything.
        double[] scales = [4, 8, 12, 24, 48, 96];
        for (var i = 1; i < scales.Length; i++)
        {
            Assert.False(
                Same(Field(kind, scales[i - 1]), Field(kind, scales[i])),
                $"{kind}: scale {scales[i - 1]} and {scales[i]} produced the same field");
        }
    }

    [Theory]
    [InlineData(PaperKind.Smooth)]
    [InlineData(PaperKind.ColdPress)]
    [InlineData(PaperKind.Rough)]
    public void BelowNyquist_TheFieldSaturatesRatherThanAliasing(PaperKind kind)
    {
        // Grain finer than two document pixels cannot be represented. It is
        // clamped deliberately — the point of the test is that it clamps
        // rather than summing octaves the tile cannot carry, which showed up
        // as high-frequency hash instead of paper.
        var floor = Field(kind, 0.1);
        var justAbove = Field(kind, 1.0);
        Assert.True(Same(floor, justAbove), $"{kind}: sub-Nyquist scales should agree, not alias differently");

        // And what it clamps to is still paper: bounded, and not full-contrast
        // noise flipping every pixel.
        var flips = 0;
        for (var i = 1; i < floor.Length; i++)
        {
            Assert.InRange(floor[i], 0f, 1f);
            if (Math.Abs(floor[i] - floor[i - 1]) > 0.4f) flips++;
        }
        Assert.True(flips < floor.Length / 8, $"{kind}: the clamped field looks like hash, not grain ({flips} jumps)");
    }
}
