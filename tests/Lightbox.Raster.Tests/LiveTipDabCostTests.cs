using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// What one tip dab actually costs, measured where the budget cannot censor it
/// (B322).
/// </summary>
/// <remarks>
/// <para>
/// <b>The in-app estimator can only ever see the stamps its own budget allowed.</b>
/// It divides the stamp's total time by the dabs stamped, and the budget admits
/// nothing large — the owner's capture of 17:26 stamped a median of 3 dabs on a
/// rebuild and 15 on an addition, so every sample sat where the per-operation
/// constant dominates. It read 87.46 us a dab and bought a budget of 34, which
/// then refused every stamp that would have shown the true rate. The estimate
/// is self-confirming.
/// </para>
/// <para>
/// So this measures the same call over ranges the application never reaches, and
/// fits <c>cost = fixed + marginal * dabs</c>. The fixed half is real — the
/// <c>Flush</c> is one per operation whatever the range holds — and separating
/// the two is the whole point: a budget in milliseconds must subtract the fixed
/// cost and divide the rest by the marginal one.
/// </para>
/// </remarks>
public class LiveTipDabCostTests
{
    // The owner's document, because a dab's cost is an area and the canvas it
    // lands on is the tip's full-size scratch.
    private const int Width = 3840;
    private const int Height = 2160;

    private readonly ITestOutputHelper _out;

    public LiveTipDabCostTests(ITestOutputHelper output) => _out = output;

    /// <summary>The owner's case: size 70 or above, with a live effect.</summary>
    private static BrushSettings Big() => new()
    {
        Size = 70, Hardness = 0.6, Flow = 0.7, Opacity = 1,
        WetEdge = 0.6, Granulation = 0.4,
    };

    private static BrushSettings Small() => new()
    {
        Size = 24, Hardness = 0.6, Flow = 0.7, Opacity = 1,
        WetEdge = 0.6, Granulation = 0.4,
    };

    /// <summary>A long fast stroke across the document — the failing case.</summary>
    private static Stroke FastStroke(BrushSettings brush, int points = 900)
    {
        var pts = new List<StrokePoint>(points);
        double x = 200, y = 1000, heading = -0.15;
        for (var i = 0; i < points; i++)
        {
            pts.Add(new StrokePoint(x, y, 1));
            heading += 0.0009;
            // A fast pen puts its points far apart; the dabs between them are
            // interpolated by spacing, which is what makes the run long.
            x += 3.8 * Math.Cos(heading);
            y += 3.8 * Math.Sin(heading);
        }

        return new Stroke { Tool = ToolKind.Brush, Color = "#203040", Brush = brush, Points = pts };
    }

    private static double Median(List<double> xs)
    {
        xs.Sort();
        return xs.Count == 0 ? 0
            : xs.Count % 2 == 1 ? xs[xs.Count / 2]
            : (xs[xs.Count / 2 - 1] + xs[xs.Count / 2]) / 2;
    }

    /// <summary>
    /// Stamp exactly <paramref name="n"/> dabs into a fresh full-size canvas,
    /// timing the stamp and the flush the way the live tip does.
    /// </summary>
    private static double StampOnce(
        SKBitmap bmp, Stroke stroke, IReadOnlyList<BrushEngine.Dab> dabs, int n, float scale = 1f)
    {
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        canvas.Flush();
        // **The SURFACE is scaled, never the geometry** (invariant 7). Dab
        // positions reach Hash01 untouched, so every seeded dynamic re-rolls
        // the same way and the tip is the same mark at a smaller size.
        if (scale != 1f) canvas.Scale(scale);

        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        BrushEngine.StampDabRange(canvas, stroke, dabs, 0, n);
        canvas.Flush();
        var t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        return (t1 - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    [Theory]
    [Trait("Category", "Performance")]
    [InlineData("size 70")]
    [InlineData("size 24")]
    [InlineData("size 70 at compose scale")]
    public void WhatOneTipDabCosts(string which)
    {
        var brush = which.StartsWith("size 70") ? Big() : Small();
        // The compose surface the owner actually sees: 1440x810 at 0.375.
        var composed = which.EndsWith("at compose scale");
        var w = composed ? 1440 : Width;
        var h = composed ? 810 : Height;
        var scale = composed ? 0.375f : 1f;
        var stroke = FastStroke(brush);
        var dabs = BrushEngine.WalkDabs(stroke);
        _out.WriteLine($"{which}: the stroke walks to {dabs.Count} dabs on a {w}x{h} canvas.");

        using var bmp = new SKBitmap(
            new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));

        int[] counts = [1, 3, 8, 15, 34, 80, 160, 320, 640];
        counts = counts.Where(c => c <= dabs.Count).ToArray();

        // Warm the path so the first range does not pay for jitting the walk.
        for (var i = 0; i < 3; i++) StampOnce(bmp, stroke, dabs, 64, scale);

        var xs = new List<double>();
        var ys = new List<double>();
        _out.WriteLine("  dabs |  median ms | us a dab (average, INCLUDING the fixed cost)");
        foreach (var n in counts)
        {
            var runs = new List<double>();
            for (var i = 0; i < 9; i++) runs.Add(StampOnce(bmp, stroke, dabs, n, scale));
            var med = Median(runs);
            xs.Add(n);
            ys.Add(med);
            _out.WriteLine($"  {n,4} | {med,10:0.###} | {med * 1000 / n,10:0.##}");
        }

        // Least squares over the pairs: the intercept is the per-operation
        // constant the tip pays whatever it stamps, the slope is the marginal
        // cost a budget in milliseconds must divide by.
        var n0 = xs.Count;
        var sx = xs.Sum();
        var sy = ys.Sum();
        var sxx = xs.Select(v => v * v).Sum();
        var sxy = xs.Zip(ys, (a, b) => a * b).Sum();
        var slope = (n0 * sxy - sx * sy) / (n0 * sxx - sx * sx);
        var intercept = (sy - slope * sx) / n0;

        _out.WriteLine("");
        _out.WriteLine($"  fixed per operation   {intercept:0.###} ms");
        _out.WriteLine($"  marginal per dab      {slope * 1000:0.##} us");
        _out.WriteLine($"  what the app estimated on the 17:26 capture: 87.46 us a dab");
        _out.WriteLine("");
        foreach (var budgetMs in new[] { 3.0, 6.0 })
        {
            var dabsAffordable = (budgetMs - intercept) / slope;
            _out.WriteLine($"  a {budgetMs} ms budget buys {dabsAffordable:0} dabs once the fixed cost is paid.");
        }

        var outstanding = new[] { 268, 582, 2072 };
        foreach (var o in outstanding)
        {
            _out.WriteLine($"  {o,5} dabs would cost {intercept + slope * o:0.##} ms");
        }

        Assert.True(slope > 0, "stamping more dabs must cost more");

        // **The claim this test exists to defend**, and the reason B322's
        // in-app estimator could not find its own error: a per-dab cost read
        // off a SMALL stamp is mostly the per-operation constant. The app's
        // budget admitted nothing larger than about fifteen dabs, so every
        // sample it averaged sat at the left-hand end of this table — and the
        // figure it reported kept the budget small, which kept the samples
        // small. Measuring the rate at one dab and at hundreds is the only way
        // to see that, and it is why this measurement lives outside the
        // application rather than inside it.
        var atOne = ys[0] * 1000 / xs[0];
        var atMost = ys[^1] * 1000 / xs[^1];
        Assert.True(
            atOne > atMost * 1.4,
            $"a single-dab stamp should read far dearer per dab ({atOne:0.#} us) than a "
            + $"large one ({atMost:0.#} us) — if it does not, the fixed cost has gone and "
            + "the estimator's censored sample was not the problem after all");
    }
}
