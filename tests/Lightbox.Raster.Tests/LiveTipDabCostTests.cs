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

    /// <summary>
    /// The fastest run, which is the only load-robust estimator here.
    /// </summary>
    /// <remarks>
    /// <b>This was a median and the full suite caught it.</b> Run alone the
    /// table below reads 107.5 us at one dab and 60.9 at 640; run inside the
    /// four-assembly suite it read <b>269.4 and 232.5</b> — every timing
    /// inflated about fourfold, and the long runs picking up proportionally more
    /// interference than the short ones, which flattens the very ratio this test
    /// asserts. Contention only ever ADDS time, so the minimum is what the
    /// machine can do and the median is what it happened to be doing. The cost
    /// of a dab is a property of the first.
    /// </remarks>
    private static double Best(List<double> xs) => xs.Count == 0 ? 0 : xs.Min();

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

    /// <summary>
    /// Measure one case and return what a dab costs at the margin, in
    /// microseconds, with the per-operation constant separated out.
    /// </summary>
    private (double MarginalUs, double FixedMs) Measure(string which)
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
        _out.WriteLine("  dabs |    best ms | us a dab (average, INCLUDING the fixed cost)");
        foreach (var n in counts)
        {
            var runs = new List<double>();
            for (var i = 0; i < 11; i++) runs.Add(StampOnce(bmp, stroke, dabs, n, scale));
            var med = Best(runs);
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

        _out.WriteLine("");
        Assert.True(slope > 0, $"{which}: stamping more dabs must cost more");
        return (slope * 1000, intercept);
    }

    /// <summary>
    /// What a tip dab costs, and the two ratios B322's seventh attempt rests on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One test measuring three cases, rather than three tests measuring
    /// one.</b> The first draft was a Theory and asserted an absolute shape —
    /// that a single-dab stamp reads far dearer per dab than a large one,
    /// because of a fixed per-operation cost. That is true and it is <b>weak</b>:
    /// 1.35x on a quiet machine, and unmeasurable on a busy one. It failed in
    /// the full four-assembly suite for exactly that reason, and the failure was
    /// worth more than the assertion — see <see cref="Best"/>.
    /// </para>
    /// <para>
    /// <b>What is worth asserting is what the fix rests on, and both are
    /// ratios.</b> A ratio taken in one run cancels most of whatever else the
    /// machine is doing, where an absolute microsecond figure does not:
    /// </para>
    /// <list type="number">
    /// <item>a dab stamped at document resolution costs several times one
    /// stamped at the resolution it is displayed — the whole basis of
    /// <c>LiveTipScale</c>;</item>
    /// <item>a size-70 dab costs several times a size-24 one — the owner's
    /// "on larger brush sizes it jumps", which had been reported for two days
    /// before anyone measured it.</item>
    /// </list>
    /// <para>
    /// The absolute figures are printed rather than asserted, because they are a
    /// property of one machine on one afternoon and the ledger should quote them
    /// with that attached.
    /// </para>
    /// </remarks>
    [Fact]
    [Trait("Category", "Performance")]
    public void WhatOneTipDabCosts()
    {
        var big = Measure("size 70");
        var small = Measure("size 24");
        var composed = Measure("size 70 at compose scale");

        _out.WriteLine($"size 70 document {big.MarginalUs:0.#} us a dab, fixed {big.FixedMs:0.###} ms");
        _out.WriteLine($"size 70 composed {composed.MarginalUs:0.#} us a dab, fixed {composed.FixedMs:0.###} ms");
        _out.WriteLine($"size 24 document {small.MarginalUs:0.#} us a dab, fixed {small.FixedMs:0.###} ms");
        _out.WriteLine($"  stamping at the displayed size is {big.MarginalUs / composed.MarginalUs:0.#}x cheaper");
        _out.WriteLine($"  a size-70 dab is {big.MarginalUs / small.MarginalUs:0.#}x a size-24 one");

        // **The ratio is the claim; its size is a property of the machine.** This
        // asserted 3x and went red on CI at 2.5x while reading 4.8x on the
        // owner's — the arm is genuinely cheaper on both, and only the margin
        // moved. A runner with no GPU and a different Skia path pays relatively
        // more for the scaled stamp, which is a fact about runners, not about
        // whether the lever exists.
        //
        // What the number has to refute is "LiveTipScale buys nothing", and that
        // version of the code returns scale 1.0 for every input — the two arms
        // become the same measurement and the ratio is **1.0**. So the bar is
        // set above 1.0 with room for noise, not at the best figure any machine
        // has produced. Measured: 4.8x (owner, Release, 2026-08-28), 2.5x (CI
        // runner, same commit), 1.0x by construction on the arm-does-nothing
        // build.
        Assert.True(
            big.MarginalUs > composed.MarginalUs * 1.5,
            $"stamping at the displayed size ({composed.MarginalUs:0.#} us) must be materially "
            + $"cheaper than at document resolution ({big.MarginalUs:0.#} us) — at a ratio of "
            + $"{big.MarginalUs / composed.MarginalUs:0.##}x it is not, and a build where "
            + "LiveTipScale returns 1.0 for everything reads 1.0x here. B322 would have no "
            + "lever left");

        // Same reasoning, same exposure: a size-70 dab covers about 8.5x the
        // area of a size-24 one, so anything at or below 1.0 would mean cost had
        // stopped following area entirely. Measured 4.9x on the owner's machine;
        // left with the same margin as the ratio above rather than waiting for
        // CI to find it the expensive way.
        Assert.True(
            big.MarginalUs > small.MarginalUs * 1.5,
            $"a size-70 dab ({big.MarginalUs:0.#} us) must cost materially more than a size-24 "
            + $"one ({small.MarginalUs:0.#} us) — at {big.MarginalUs / small.MarginalUs:0.##}x it "
            + "does not, and that size dependence is what the owner reported as \"on larger "
            + "brush sizes it jumps\". It is why no fixed dab budget works");

    }
}
