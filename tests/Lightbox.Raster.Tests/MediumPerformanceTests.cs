using Lightbox.Core.Documents;
using Lightbox.Raster.Media;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Budgets for the simulated media. Loose on purpose — they catch a medium
/// going quadratic in the canvas or a stroke starting to allocate a lattice a
/// dab, not drift.
/// </summary>
/// <remarks>
/// These brushes are badged Expressive precisely because they cost more, so
/// the question a budget here can usefully answer is not "is it fast" but "is
/// it still bounded by the stroke". Everything below is a shape assertion in
/// that sense rather than a stopwatch reading dressed up as one.
/// </remarks>
[Collection("Performance")]
[Trait("Category", "Performance")]
public class MediumPerformanceTests(ITestOutputHelper output)
{
    private static MediumSettings Watercolour() => new()
    {
        Kind = MediumKind.Watercolour,
        Wetness = 0.85, Viscosity = 0.1, Drag = 0.25, FlowSteps = 16,
        Absorbency = 0.35, EdgePull = 0.7,
        PigmentDensity = 0.5, Granularity = 0.6, Hiding = 0.05,
        Paper = PaperKind.ColdPress, PaperScale = 14, PaperInfluence = 0.7,
        PressureWater = 0.8, Rewetting = 0.6,
    };

    private static Stroke Mark(MediumSettings medium, double size, int dabs, int span) =>
        new()
        {
            Tool = ToolKind.Brush,
            Color = "#c04030",
            Points = Enumerable.Range(0, dabs)
                .Select(i => new StrokePoint(60 + i * span, 200, 1))
                .ToList(),
            Brush = new BrushSettings
            {
                Size = size, Hardness = 0.5, Opacity = 1, Flow = 0.9, Spacing = 0.08,
                PressureFlowGamma = 1, Medium = medium,
            },
        };

    private static void Commit(Stroke stroke, SKImageInfo info)
    {
        using var layer = new SKBitmap(info);
        using var canvas = new SKCanvas(layer);
        canvas.Clear(SKColors.Transparent);
        BrushEngine.StampStroke(canvas, stroke, info, layer);
        canvas.Flush();
    }

    private static double FastestMs(Stroke stroke, int width, int height, int runs = 5)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        Commit(stroke, info); // warm the JIT and the rented lattice
        return Bench.FastestMs(runs, () => Commit(stroke, info));
    }

    [Fact]
    public void AWatercolourStrokeCommitsWithinBudget()
    {
        var ms = FastestMs(Mark(Watercolour(), 40, 12, 14), 900, 400);
        output.WriteLine($"watercolour 40px on 900x400: {ms:F1} ms");

        // Measured around 30 ms. The budget is an order of magnitude up,
        // because what would trip it is the lattice going full-canvas or the
        // flow loop losing its step cap — not a few milliseconds of drift.
        Assert.True(ms < 300, $"a watercolour commit took {ms:F0} ms (budget 300 ms)");
    }

    /// <summary>
    /// Invariant 6 for the expensive path: the lattice is sized to the region a
    /// stroke can reach and capped, so simulating a mark must cost what the
    /// mark costs rather than what the canvas around it costs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This measures how each cost GROWS with the canvas, not how the two
    /// compare at one size, and that is B308.</b> It used to subtract the same
    /// stroke with no medium — <c>medium - plain</c> — and assert the remainder
    /// was positive and did not grow. The subtraction was meant to remove the
    /// compositing both strokes pay; what it actually did was compare a bounded
    /// cost against an unbounded one and read the <em>sign</em> of the
    /// difference.
    /// </para>
    /// <para>
    /// The plain stroke is the dearer of the two on some machines, because it
    /// pays the footprint ceiling — a pixel pass over the stroke's region, which
    /// grows with the canvas — while the medium is excluded from that ceiling
    /// and runs its simulation on a lattice that does not. Measured at
    /// hardness 0.5, flow 0.9, spacing 0.08, both strokes 98 dabs: 720p medium
    /// <b>114 ms</b> against plain <b>126 ms</b>; 4K medium <b>116 ms</b>
    /// against plain <b>164 ms</b>. So <c>medium - plain</c> was <em>negative</em>
    /// and the old sanity guard failed — on <c>main</c> as readily as on the
    /// branch that found it, in Release, with no rendering change between them.
    /// </para>
    /// <para>
    /// Those same numbers are what the invariant should have been read off all
    /// along, and they make the point far more sharply than a difference does:
    /// nine times the canvas area costs the medium <b>+2 ms</b> and the plain
    /// stroke <b>+38 ms</b>. A lattice that started tracking the canvas would
    /// have to out-grow the compositing to pass, which is exactly the failure
    /// worth catching, and no absolute timing on any machine enters the
    /// comparison.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheMediumCostsTheSameOnAHugeCanvasAsOnASmallOne()
    {
        var medium = Mark(Watercolour(), 200, 14, 120);
        var plain = Mark(new MediumSettings(), 200, 14, 120);

        var mediumGrowth = FastestMs(medium, 3840, 2160) - FastestMs(medium, 1280, 720);
        var plainGrowth = FastestMs(plain, 3840, 2160) - FastestMs(plain, 1280, 720);
        output.WriteLine(
            $"nine times the area costs the medium {mediumGrowth:+0.0;-0.0} ms "
            + $"and the plain stroke {plainGrowth:+0.0;-0.0} ms");

        // The compositing that grows with the canvas is what the plain stroke
        // is here to price, and the medium pays it too. What must not appear is
        // a *second* canvas-proportional term from the simulation, so the
        // medium's growth is allowed the plain stroke's and a fixed margin for
        // a loaded runner — and nothing more.
        Assert.True(
            mediumGrowth < plainGrowth + 25,
            $"nine times the canvas area cost the medium {mediumGrowth:F1} ms against the "
            + $"plain stroke's {plainGrowth:F1} ms — the lattice is tracking the canvas, "
            + "not the stroke");
    }

    [Fact]
    public void AMediumStrokeDoesNotAllocateALatticeEachTime()
    {
        // The lattice is about twenty floats a cell and every one of its
        // buffers lands on the large object heap, so building a fresh one per
        // stroke was a megabyte a mark and a full blocking Gen2 every few
        // hundred. Pauses while painting are the performance failure an artist
        // feels directly.
        var stroke = Mark(Watercolour(), 40, 12, 14);
        var info = new SKImageInfo(900, 400, SKColorType.Rgba8888, SKAlphaType.Premul);
        Commit(stroke, info);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetTotalAllocatedBytes(precise: true);

        const int Strokes = 10;
        for (var i = 0; i < Strokes; i++) Commit(stroke, info);

        var kb = (GC.GetTotalAllocatedBytes(precise: true) - before) / 1024.0 / Strokes;
        output.WriteLine($"{kb:F0} KB per stroke");

        // Measured near 290 KB with the lattice reused, against 1191 KB when
        // each stroke built its own. The budget sits between the two, so
        // dropping the reuse fails here rather than quietly costing an artist
        // a Gen2 pause every few marks.
        Assert.True(kb < 700, $"a medium stroke allocated {kb:F0} KB (budget 700 KB)");
    }

    [Fact]
    public void AReusedLatticeRendersExactlyWhatAFreshOneWould()
    {
        // The risk the reuse introduces, and the only one that matters: a
        // lattice still holding the last stroke's water would paint a different
        // mark, and the document would then reload differently — invariant 2
        // through the back door. Same stroke, twice, after a completely
        // different one has been through the same lattice.
        var info = new SKImageInfo(900, 400, SKColorType.Rgba8888, SKAlphaType.Premul);
        var stroke = Mark(Watercolour(), 40, 12, 14);

        using var first = new SKBitmap(info);
        using (var canvas = new SKCanvas(first))
        {
            canvas.Clear(SKColors.Transparent);
            BrushEngine.StampStroke(canvas, stroke, info, first);
            canvas.Flush();
        }

        // Something bigger, wetter and a different shape in between.
        var soaking = Watercolour();
        soaking.Wetness = 1;
        soaking.FlowSteps = 32;
        Commit(Mark(soaking, 180, 6, 90), info);

        using var again = new SKBitmap(info);
        using (var canvas = new SKCanvas(again))
        {
            canvas.Clear(SKColors.Transparent);
            BrushEngine.StampStroke(canvas, stroke, info, again);
            canvas.Flush();
        }

        for (var y = 0; y < info.Height; y += 2)
        for (var x = 0; x < info.Width; x += 2)
        {
            Assert.Equal(first.GetPixel(x, y), again.GetPixel(x, y));
        }
    }
}
