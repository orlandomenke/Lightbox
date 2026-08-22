using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Strokes that were still wet together are one wash, not a stack of six.
/// </summary>
/// <remarks>
/// <para>
/// The measurement that produced <see cref="BrushSettings.WetStrokes"/>: the
/// same dab coverage laid as six overlapping strokes mottles at 11.1% against
/// 2.2% for one stroke covering the same paper, so what makes a wash read as
/// ribbons is neither the paper nor double-counted pigment — it is that each
/// stroke got its own wet region, its own boundary and its own drying.
/// </para>
/// <para>
/// These tests hold both halves: the grouping stays narrow enough that nothing
/// which paints differently is silently merged, and the grouping actually pays
/// what the spike said it would when it goes through the real render path.
/// </para>
/// </remarks>
public class WetRunTests(ITestOutputHelper output)
{
    private const int W = 200, H = 160;

    private static MediumSettings Wash() => new()
    {
        Kind = MediumKind.Watercolour,
        Wetness = 0.85, Viscosity = 0.1, Drag = 0.25, FlowSteps = 14,
        Absorbency = 0.35, EdgePull = 0.1,
        PigmentDensity = 0.5, Granularity = 0.6, Hiding = 0.05,
        Paper = PaperKind.ColdPress, PaperScale = 14, PaperInfluence = 0.7,
        PressureWater = 0.8, Rewetting = 0.6,
    };

    private static Stroke Band(double y, int? wetStrokes, double x0 = 40, double x1 = 160, double flow = 0.9)
    {
        var pts = new List<StrokePoint>();
        for (var x = x0; x <= x1; x += 4) pts.Add(new StrokePoint(x, y, 1));
        return new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#3050a0",
            Points = pts,
            Brush = new BrushSettings
            {
                Size = 26, Hardness = 0.5, Opacity = 1, Flow = flow, Spacing = 0.1,
                PressureFlowGamma = 1,
                Medium = Wash(),
                WetStrokes = wetStrokes,
            },
        };
    }

    /// <summary>
    /// Six overlapping bands, which is how anybody fills an area: rows 46..94
    /// are painted by more than one of them.
    /// </summary>
    private static List<Stroke> Sweep(int? wetStrokes, double flow = 0.9)
    {
        var strokes = new List<Stroke>();
        for (var i = 0; i < 6; i++) strokes.Add(Band(40 + i * 10, wetStrokes, flow: flow));
        return strokes;
    }

    /// <summary>
    /// A pale wash, which is the case that matters and the case a measurement
    /// has to use. At full flow six overlapping strokes clip — mean alpha 253
    /// of 255 — and a clipped wash reads flat whatever the simulation did under
    /// it, so measuring there would report the seams as absent because the
    /// paint is opaque. That is the saturation trap the charter names, and this
    /// is the same wash laid thin enough for the numbers to mean something.
    /// </summary>
    private const double PaleFlow = 0.06;

    /// <summary>Paper inside the sweep, every row of it covered by two bands or more.</summary>
    private static SKRectI Interior => new(60, 46, 140, 94);

    // ---- the grouping ------------------------------------------------------

    [Fact]
    public void AStrokeWithNoMediumNeverFuses()
    {
        var a = Band(60, wetStrokes: 4);
        var b = Band(70, wetStrokes: 4);
        a.Brush.Medium = new MediumSettings { Kind = MediumKind.None };
        b.Brush.Medium = new MediumSettings { Kind = MediumKind.None };

        Assert.False(WetRun.StaysWet(a));
        Assert.Equal(2, WetRun.Split([a, b]).Count);
    }

    [Fact]
    public void AMediumStrokeWithNoWindowNeverFuses()
    {
        // The default, and therefore what every existing document does: the
        // paint is dry the moment the pen lifts.
        var runs = WetRun.Split([Band(60, null), Band(70, null)]);
        Assert.Equal(2, runs.Count);
        Assert.Equal(2, WetRun.Split([Band(60, 0), Band(70, 0)]).Count);
    }

    [Fact]
    public void TwoWetStrokesFromTheSameBrushFuse()
    {
        var runs = WetRun.Split([Band(60, 4), Band(70, 4)]);
        Assert.Single(runs);
        Assert.Equal(2, runs[0].Count);
    }

    [Fact]
    public void StrokesThatWouldPaintDifferentlyDoNotFuse()
    {
        // Everything the one shared composite can only carry once. Fusing
        // across any of these would silently change what a stroke's own
        // colour, opacity or blend mode means.
        var runs = WetRun.Split([Band(60, 4), Different(Band(70, 4), s => s.Color = "#a03020")]);
        Assert.Equal(2, runs.Count);

        Assert.Equal(2, WetRun.Split(
            [Band(60, 4), Different(Band(70, 4), s => s.Brush.Opacity = 0.4)]).Count);
        Assert.Equal(2, WetRun.Split(
            [Band(60, 4), Different(Band(70, 4), s => s.Brush.Blend = LayerBlendMode.Multiply)]).Count);
        Assert.Equal(2, WetRun.Split(
            [Band(60, 4), Different(Band(70, 4), s => s.Brush.Size = 30)]).Count);
        Assert.Equal(2, WetRun.Split(
            [Band(60, 4), Different(Band(70, 4), s => s.Brush.Medium.Wetness = 0.2)]).Count);
        Assert.Equal(2, WetRun.Split(
            [Band(60, 4), Different(Band(70, 4), s => s.AlphaLocked = true)]).Count);
        // And an eraser does not stay wet at all — it takes paint away rather
        // than laying a pass another stroke could join.
        Assert.Equal(2, WetRun.Split(
            [Band(60, 4), Different(Band(70, 4), s => s.Tool = ToolKind.Eraser)]).Count);
        Assert.False(WetRun.StaysWet(Different(Band(70, 4), s => s.Tool = ToolKind.Eraser)));
        Assert.Equal(2, WetRun.Split(
            [Different(Band(60, 4), s => s.Tool = ToolKind.Eraser),
             Different(Band(70, 4), s => s.Tool = ToolKind.Eraser)]).Count);
    }

    private static Stroke Different(Stroke stroke, Action<Stroke> change)
    {
        change(stroke);
        return stroke;
    }

    [Fact]
    public void StrokesTooFarApartToTouchDoNotFuse()
    {
        // Wet at the same time and nothing to do with each other. Fusing these
        // would put one lattice across all the blank paper between them.
        var runs = WetRun.Split([Band(20, 4, x0: 10, x1: 40), Band(140, 4, x0: 150, x1: 190)]);
        Assert.Equal(2, runs.Count);
    }

    [Fact]
    public void TheWindowBoundsTheRun()
    {
        // A window of two means this stroke and the two after it, so runs cap
        // at three however many matching strokes follow.
        var strokes = new List<Stroke>();
        for (var i = 0; i < 7; i++) strokes.Add(Band(40 + i * 8, 2));
        var runs = WetRun.Split(strokes);

        Assert.Equal([3, 3, 1], runs.Select(r => r.Count));
        Assert.Equal(7, runs.Sum(r => r.Count));
    }

    [Fact]
    public void TheWindowIsCappedRatherThanTrusted()
    {
        // A hand-edited document cannot ask for a hundred strokes of re-simulation.
        Assert.Equal(BrushSettings.MaxWetStrokes, Band(60, 100).Brush.WetStrokeWindow);
        Assert.Equal(0, Band(60, -3).Brush.WetStrokeWindow);
    }

    [Fact]
    public void EveryStrokeSurvivesTheSplit()
    {
        // The one thing a grouping pass must never do is lose or duplicate a
        // mark, whatever it decides about wetness.
        var mixed = new List<Stroke>
        {
            Band(30, null), Band(40, 3), Band(50, 3), Band(60, null),
            Band(70, 3), Band(80, 3), Band(90, 3),
        };
        var flat = WetRun.Split(mixed).SelectMany(r => r).ToList();
        Assert.Equal(mixed.Count, flat.Count);
        Assert.Equal(mixed, flat, ReferenceEqualityComparer.Instance);
    }

    // ---- the payoff --------------------------------------------------------

    /// <summary>
    /// The seams, measured directly: the spread of per-row mean alpha across a
    /// patch that should be one flat wash, as a fraction of its own mean.
    /// </summary>
    /// <remarks>
    /// The bands are horizontal, so a row profile is exactly the ribboning and
    /// nothing else. A box metric answers the same question and is sensitive to
    /// where its grid happens to fall relative to the band spacing — the same
    /// pair of renders read 10.9% against 4.2% at 8 px boxes over one patch and
    /// 4.0% against 2.8% over a patch two pixels shorter. A threshold on a
    /// number that moves like that guards nothing.
    /// </remarks>
    private static double Ribbing(SKBitmap bmp, SKRectI patch)
    {
        var rows = new List<double>();
        for (var y = patch.Top; y < patch.Bottom; y++)
        {
            double sum = 0;
            for (var x = patch.Left; x < patch.Right; x++) sum += bmp.GetPixel(x, y).Alpha;
            rows.Add(sum / patch.Width);
        }
        var mean = rows.Average();
        if (mean <= 0) return 0;
        return Math.Sqrt(rows.Sum(r => (r - mean) * (r - mean)) / rows.Count) / mean;
    }

    /// <summary>
    /// Unevenness at a given scale: the spread of box means over a patch that
    /// should be flat, as a fraction of its own mean. Reported rather than
    /// asserted on — see <see cref="Ribbing"/> for why.
    /// </summary>
    private static double Mottle(SKBitmap bmp, SKRectI patch, int box)
    {
        var means = new List<double>();
        for (var y = patch.Top; y + box <= patch.Bottom; y += box)
        for (var x = patch.Left; x + box <= patch.Right; x += box)
        {
            double sum = 0;
            for (var j = 0; j < box; j++)
            for (var i = 0; i < box; i++) sum += bmp.GetPixel(x + i, y + j).Alpha;
            means.Add(sum / (box * box));
        }
        var mean = means.Average();
        if (mean <= 0) return 0;
        var variance = means.Sum(m => (m - mean) * (m - mean)) / means.Count;
        return Math.Sqrt(variance) / mean;
    }

    [Fact]
    public void AFusedWashIsFlatterThanTheSameStrokesDriedOneByOne()
    {
        // The interior of the sweep: every row here is covered by at least two
        // bands, so it is paper an artist would expect to be one flat wash.
        var patch = Interior;

        using var separate = FrameRasterizer.Rasterize(Sweep(null, PaleFlow), W, H);
        using var fused = FrameRasterizer.Rasterize(Sweep(6, PaleFlow), W, H);

        var apart = Ribbing(separate, patch);
        var together = Ribbing(fused, patch);
        output.WriteLine($"ribbing: separate {apart:P1}, fused {together:P1}");
        foreach (var box in new[] { 2, 4, 8 })
        {
            output.WriteLine(
                $"  {box} px boxes: separate {Mottle(separate, patch, box):P1}, "
                + $"fused {Mottle(fused, patch, box):P1}");
        }

        Assert.True(
            together < apart * 0.5,
            $"fusing gave {together:P1} ribbing against {apart:P1} apart");
    }

    [Fact]
    public void SixSeparateStrokesAtFullFlowClipRatherThanShowTheirSeams()
    {
        // Why the measurement above is taken pale, kept as a test so the reason
        // cannot quietly stop being true. Six overlapping strokes at full flow
        // saturate, and saturated paint has no unevenness left to measure.
        using var separate = FrameRasterizer.Rasterize(Sweep(null), W, H);
        var mean = Mean(separate, Interior);
        output.WriteLine($"full flow, six separate strokes: mean alpha {mean:F1} of 255");
        Assert.True(mean > 248, $"mean alpha {mean:F1} — no longer clipped, so retune the pale case");
    }

    [Fact]
    public void AFusedWashDoesNotBuildUpToSixCoats()
    {
        // The trade, measured rather than discovered later. A run shares one
        // dab scratch, and scratch alpha stops at one coat — so six strokes
        // into standing water read as one wash, where six dried glazes stacked.
        // That is what wet-into-wet does: you cannot deepen a colour into water
        // that has not dried, you wait and glaze again. An artist who wants the
        // buildup turns the window off, which is the default.
        using var separate = FrameRasterizer.Rasterize(Sweep(null, PaleFlow), W, H);
        using var fused = FrameRasterizer.Rasterize(Sweep(6, PaleFlow), W, H);

        var (apartInk, apartAlpha) = Ink(separate);
        var (fusedInk, fusedAlpha) = Ink(fused);
        output.WriteLine($"separate {apartInk} px at {apartAlpha:F1}, fused {fusedInk} px at {fusedAlpha:F1}");

        // Same paper covered: fusing evens a wash out, it does not shrink it.
        Assert.True(fusedInk > apartInk * 0.9, $"fused covered {fusedInk} against {apartInk}");
        // And the wash is still a wash rather than a stain — lighter, not gone.
        Assert.True(fusedAlpha > 90, $"fused sat at {fusedAlpha:F1}, too pale to be paint");
        Assert.True(fusedAlpha < apartAlpha, $"fused sat at {fusedAlpha:F1} against {apartAlpha:F1}");
    }

    private static double Mean(SKBitmap bmp, SKRectI patch)
    {
        double sum = 0;
        for (var y = patch.Top; y < patch.Bottom; y++)
        for (var x = patch.Left; x < patch.Right; x++) sum += bmp.GetPixel(x, y).Alpha;
        return sum / (patch.Width * patch.Height);
    }

    private static (int Pixels, double MeanAlpha) Ink(SKBitmap bmp)
    {
        var pixels = 0;
        double alpha = 0;
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        {
            var a = bmp.GetPixel(x, y).Alpha;
            if (a <= 4) continue;
            pixels++;
            alpha += a;
        }
        return (pixels, pixels == 0 ? 0 : alpha / pixels);
    }

    [Fact]
    public void ADocumentWithNoWetWindowRendersExactlyAsItDidBefore()
    {
        // Optional means absent: the grouping pass has to be a no-op for every
        // document that predates it, to the bit.
        var strokes = Sweep(null);
        using var grouped = FrameRasterizer.Rasterize(strokes, W, H);

        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var one = new SKBitmap(info);
        using var canvas = new SKCanvas(one);
        canvas.Clear(SKColors.Transparent);
        foreach (var stroke in strokes)
        {
            BrushEngine.StampStroke(canvas, stroke, info, one);
            canvas.Flush();
        }

        Assert.Equal(one.Bytes, grouped.Bytes);
    }

    [Fact]
    public void ARunOfOneIsTheStrokeOnItsOwn()
    {
        // Which is what makes the window safe to switch on: a lone stroke in a
        // wet brush renders identically whether it went through the run path or
        // not, so turning the setting on changes only washes.
        var stroke = Band(80, 4);
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);

        using var viaRun = new SKBitmap(info);
        using var runCanvas = new SKCanvas(viaRun);
        runCanvas.Clear(SKColors.Transparent);
        BrushEngine.StampWetRun(runCanvas, [stroke], info, viaRun);
        runCanvas.Flush();

        using var direct = new SKBitmap(info);
        using var directCanvas = new SKCanvas(direct);
        directCanvas.Clear(SKColors.Transparent);
        BrushEngine.StampStroke(directCanvas, stroke, info, direct);
        directCanvas.Flush();

        Assert.Equal(direct.Bytes, viaRun.Bytes);
    }

    // ---- the interactive commit -------------------------------------------

    /// <summary>
    /// Paint the sweep the way the app does — one stroke at a time onto a
    /// cached layer bitmap — and hand each stroke to <see cref="WetRunCommit"/>
    /// exactly as the view model would.
    /// </summary>
    private int Rerenders;

    private SKBitmap PaintOneStrokeAtATime(
        List<Stroke> strokes, WetRunCommit? commit = null, int freshFrom = int.MaxValue)
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        var layer = new SKBitmap(info);
        using (var clear = new SKCanvas(layer)) clear.Clear(SKColors.Transparent);

        var owned = commit is null;
        commit ??= new WetRunCommit();
        var committed = new List<Stroke>();
        var rerenders = 0;
        for (var i = 0; i < strokes.Count; i++)
        {
            // A frame that left the cache mid-wash: the caller gets a fresh
            // instance with no baseline, which is what the fallback is for.
            if (i == freshFrom) commit = new WetRunCommit();

            var stroke = strokes[i];
            switch (commit.Commit(layer, "frame", committed, stroke))
            {
                case WetRunCommit.Outcome.Append:
                    FrameRasterizer.Append(layer, stroke);
                    break;
                case WetRunCommit.Outcome.Done:
                    break;
                case WetRunCommit.Outcome.Rerender:
                    // What the view model does: throw the cached pixels away
                    // and rebuild the frame from the record.
                    rerenders++;
                    layer.Dispose();
                    committed.Add(stroke);
                    layer = FrameRasterizer.Rasterize(committed, W, H);
                    continue;
            }
            committed.Add(stroke);
        }
        if (owned) commit.Dispose();
        Rerenders = rerenders;
        Assert.True(
            rerenders <= Math.Max(0, strokes.Count - freshFrom),
            $"{rerenders} frame re-renders for {strokes.Count} strokes");
        return layer;
    }

    [Fact]
    public void PaintingAWashStrokeByStrokeMatchesReloadingIt()
    {
        // The whole point of the commit path: what the artist watched appear is
        // what the record renders to. A live path that fused differently would
        // show seams while painting and lose them on reload, which is worse
        // than either.
        var strokes = Sweep(6, PaleFlow);
        using var painted = PaintOneStrokeAtATime(strokes);
        using var reloaded = FrameRasterizer.Rasterize(strokes, W, H);
        Assert.Equal(reloaded.Bytes, painted.Bytes);
    }

    [Fact]
    public void AWashPaintedOverExistingArtMatchesReloadingIt()
    {
        // The baseline has to restore what was under the run, not assume blank
        // paper — a wash is usually laid over a drawing.
        var strokes = new List<Stroke> { Band(66, null, x0: 30, x1: 170) };
        strokes[0].Color = "#804010";
        strokes.AddRange(Sweep(6, PaleFlow));

        using var painted = PaintOneStrokeAtATime(strokes);
        using var reloaded = FrameRasterizer.Rasterize(strokes, W, H);
        Assert.Equal(reloaded.Bytes, painted.Bytes);
    }

    [Fact]
    public void ARunThatGrowsBeyondItsFirstStrokeStillMatches()
    {
        // Each band reaches paper the ones before it did not, so the baseline
        // has to widen mid-run — and the widened part has to come off the layer
        // while it is still pre-run there.
        var strokes = new List<Stroke>();
        for (var i = 0; i < 5; i++) strokes.Add(Band(50 + i * 9, 5, x0: 40 + i * 12, x1: 120 + i * 12, flow: PaleFlow));

        using var painted = PaintOneStrokeAtATime(strokes);
        using var reloaded = FrameRasterizer.Rasterize(strokes, W, H);
        Assert.Equal(reloaded.Bytes, painted.Bytes);
    }

    [Fact]
    public void LosingTheBaselineMidRunFallsBackToARerenderRatherThanASeam()
    {
        // The cache evicted the frame, or an undo rebuilt it. There is no way
        // back to the pre-run pixels, so the commit says so instead of guessing
        // — and the fallback lands on the same bytes, just slower.
        var strokes = Sweep(6, PaleFlow);
        using var painted = PaintOneStrokeAtATime(strokes, freshFrom: 3);
        using var reloaded = FrameRasterizer.Rasterize(strokes, W, H);
        Assert.Equal(reloaded.Bytes, painted.Bytes);

        // Bounded, and the bound is the run: nothing back to the pre-run pixels
        // exists any more, so every stroke left in this wash pays a frame
        // re-render. Costly, and it stops at the end of the run rather than
        // running for the rest of the session.
        output.WriteLine($"{Rerenders} frame re-renders after losing the baseline at stroke 4 of 6");
        Assert.True(Rerenders <= strokes.Count, $"{Rerenders} re-renders");
    }

    [Fact]
    public void ADryDocumentIsCommittedWithoutTheRunPathAtAll()
    {
        // Optional means absent: a brush with no window never asks the commit
        // for anything but "append it the ordinary way", so it cannot hold a
        // baseline bitmap or re-render anything.
        using var commit = new WetRunCommit();
        var committed = new List<Stroke>();
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var layer = new SKBitmap(info);
        using (var clear = new SKCanvas(layer)) clear.Clear(SKColors.Transparent);

        foreach (var stroke in Sweep(null))
        {
            Assert.Equal(WetRunCommit.Outcome.Append, commit.Commit(layer, "frame", committed, stroke));
            FrameRasterizer.Append(layer, stroke);
            committed.Add(stroke);
            Assert.Equal(0, commit.AllocatedBytes);
        }
    }

    [Fact]
    public void TheBaselineIsGivenBackWhenTheRunEnds()
    {
        // It is a bitmap per open run, so the run ending has to free it —
        // otherwise a session's worth of washes is held for nothing.
        using var commit = new WetRunCommit();
        var committed = new List<Stroke>();
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var layer = new SKBitmap(info);
        using (var clear = new SKCanvas(layer)) clear.Clear(SKColors.Transparent);

        var wet = Band(60, 4, flow: PaleFlow);
        commit.Commit(layer, "frame", committed, wet);
        FrameRasterizer.Append(layer, wet);
        committed.Add(wet);
        Assert.True(commit.AllocatedBytes > 0, "no baseline taken for the first stroke of a run");

        // A stroke that cannot join it — different colour — closes the run.
        var dry = Band(70, null);
        Assert.Equal(WetRunCommit.Outcome.Append, commit.Commit(layer, "frame", committed, dry));
        Assert.Equal(0, commit.AllocatedBytes);
    }

    [Fact]
    public void TheBaselineCostsTheRunsRegionRatherThanTheFrame()
    {
        // Invariant 6 in memory as well as in time: what is held is what the
        // run can reach, not a copy of the layer.
        using var commit = new WetRunCommit();
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var layer = new SKBitmap(info);
        using (var clear = new SKCanvas(layer)) clear.Clear(SKColors.Transparent);

        commit.Commit(layer, "frame", [], Band(60, 4, flow: PaleFlow));
        output.WriteLine($"baseline {commit.AllocatedBytes} bytes of a {layer.ByteCount} byte layer");
        Assert.True(
            commit.AllocatedBytes < layer.ByteCount / 3,
            $"baseline held {commit.AllocatedBytes} bytes of a {layer.ByteCount} byte layer");
    }

    [Fact]
    public void AppendingAWholeListOntoExistingPixelsGroupsItToo()
    {
        // The callers that have something to draw over — a baseline image, a
        // partial render — cannot start from a fresh bitmap, and appending one
        // stroke at a time would put the seams back into every wash they
        // contain.
        var strokes = Sweep(6, PaleFlow);
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);

        using var onto = new SKBitmap(info);
        using (var ground = new SKCanvas(onto)) ground.Clear(SKColors.Transparent);
        FrameRasterizer.AppendAll(onto, strokes);

        using var fresh = FrameRasterizer.Rasterize(strokes, W, H);
        Assert.Equal(fresh.Bytes, onto.Bytes);
    }

    [Fact]
    public void AWetRunIsRenderedTheSameTwice()
    {
        // Invariant 1: the wet state at stroke k is a pure function of the
        // strokes before it in the record, so a reload reproduces the painting.
        using var first = FrameRasterizer.Rasterize(Sweep(6), W, H);
        using var second = FrameRasterizer.Rasterize(Sweep(6), W, H);
        Assert.Equal(first.Bytes, second.Bytes);
    }
}
