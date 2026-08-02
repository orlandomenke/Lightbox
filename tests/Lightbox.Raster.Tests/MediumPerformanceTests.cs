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

    [Fact]
    public void TheMediumCostsTheSameOnAHugeCanvasAsOnASmallOne()
    {
        // Invariant 6, for the expensive path. The lattice is sized to the
        // region a stroke can reach and capped, so simulating a mark must cost
        // what the mark costs — the canvas around it is Skia's problem and
        // already measured elsewhere. Taking the difference against the same
        // stroke with no medium is what isolates the simulation from the
        // compositing that grows with the canvas.
        var medium = Mark(Watercolour(), 200, 14, 120);
        var plain = Mark(new MediumSettings(), 200, 14, 120);

        var smallDelta = FastestMs(medium, 1280, 720) - FastestMs(plain, 1280, 720);
        var hugeDelta = FastestMs(medium, 3840, 2160) - FastestMs(plain, 3840, 2160);
        output.WriteLine($"medium's own cost: {smallDelta:F1} ms at 720p, {hugeDelta:F1} ms at 4K");

        Assert.True(smallDelta > 1, "the medium cost nothing measurable — the test is not measuring it");
        Assert.True(hugeDelta < smallDelta * 3 + 20,
            $"the medium got {hugeDelta / smallDelta:F1}x dearer on a canvas 9x the area — " +
            "it is tracking the canvas, not the stroke");
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
