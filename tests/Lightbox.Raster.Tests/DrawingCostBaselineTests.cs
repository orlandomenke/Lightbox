using System.Security.Cryptography;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// What one pointer event costs today, recorded before symmetry exists.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this file is dated rather than merely written.</b> Symmetry painting
/// stamps a mark more than once — that is what it is for — and the cheapest way
/// to build it is also the one that quietly makes ordinary drawing slower: a
/// branch in the per-dab loop, a scratch allocated per copy, or a dirty region
/// widened to the union of where every copy landed. None of those are visible
/// in a diff and none of them fail an existing test. They are only visible as a
/// number, and a number is only evidence against a number recorded earlier.
/// </para>
/// <para>
/// <c>RuntimeDeterminismTests</c> makes this argument for a runtime migration
/// and it is the same argument here: a baseline taken <em>after</em> the change
/// captures the changed behaviour as the reference and destroys the only
/// evidence that would have shown a difference. So these run and pass on a tree
/// where nothing about symmetry has been written yet, and their whole value is
/// what they do on the tree that comes next.
/// </para>
/// <para>
/// <b>Two claims, and only one of them is "nothing gets slower".</b> Symmetry
/// switched on necessarily costs more, because N copies means stamping N marks;
/// pretending otherwise would be a budget nobody could keep. What must hold is
/// the pair:
/// </para>
/// <list type="bullet">
/// <item>with symmetry off, drawing costs what it costs today — the
/// canvas-independence, linearity and fingerprint tests below;</item>
/// <item>with symmetry on, the cost is proportional to the number of copies and
/// to the region they touch, never to the canvas — which is what
/// <see cref="CostGrowsLinearlyInTheNumberOfCopies"/> and
/// <see cref="TheDirtyRegionIsTheSegmentsOwnReach"/> pre-register.</item>
/// </list>
/// <para>
/// <b>The non-timing tests are the load-bearing ones.</b> A pinned dirty-region
/// area and a render fingerprint cannot be moved by a busy machine, so they
/// fail for one reason only. The timed tests are ratios measured in a single
/// alternating run (<see cref="Bench.PairedFastestMs"/>) precisely because an
/// absolute millisecond figure on this runner is a measurement of the runner.
/// Absolutes are printed anyway — a ratio that holds while both sides tripled
/// is the failure mode of ratios, and the printed numbers are how that gets
/// noticed.
/// </para>
/// </remarks>
[Trait("Category", "Performance")]
[Collection("Performance")]
public class DrawingCostBaselineTests(ITestOutputHelper output)
{
    private const int W = 960;
    private const int H = 540;

    // Four times the area, to ask whether cost follows the segment or the page.
    // Raster-side only: the App suite is where large bitmaps have killed a
    // runner before, and this is one allocation outside every timed region.
    private const int WideW = 1920;
    private const int WideH = 1080;

    /// <summary>A plain brush: no effect passes, so this is the dab loop itself.</summary>
    private static BrushSettings Plain => new()
    {
        Size = 24, Hardness = 0.9, Opacity = 0.9, Flow = 0.8, Spacing = 0.12,
        PressureFlowGamma = 1,
    };

    /// <summary>
    /// An effect brush, which is where a per-copy scratch would show up. The
    /// live path defers the stroke-global passes to the commit, so this still
    /// has to be bounded to the segment.
    /// </summary>
    private static BrushSettings Watercolour => new()
    {
        Size = 22, Hardness = 0.4, Opacity = 0.65, Flow = 0.55, Spacing = 0.12,
        WetEdge = 0.55, Granulation = 0.35, PressureFlowGamma = 1,
    };

    private static Stroke Segment(BrushSettings brush, double x0, double y, int points = 4) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#2a4a8a",
        Brush = brush,
        Points = Enumerable.Range(0, points).Select(i => new StrokePoint(x0 + i * 3, y, 0.7)).ToList(),
    };

    private static SKBitmap Layer(int w, int h)
    {
        var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(SKColors.Transparent);
        return bmp;
    }

    private static int MarkedPixels(SKBitmap bmp)
    {
        var n = 0;
        var span = bmp.GetPixelSpan();
        for (var i = 3; i < span.Length; i += 4)
        {
            if (span[i] != 0) n++;
        }

        return n;
    }

    // ---- is the instrument real ------------------------------------------

    /// <summary>
    /// The segment these tests time actually puts paint down, and the two
    /// canvases actually differ in area.
    /// </summary>
    /// <remarks>
    /// Without this the whole file is decoration: a stroke that lands off
    /// canvas, or a brush whose flow resolves to nothing, costs almost the same
    /// on any page and would satisfy every ratio below while measuring an early
    /// return. This is the check <c>RuntimeDeterminismTests</c> keeps for the
    /// same reason — an empty bitmap hashes consistently forever.
    /// </remarks>
    [Fact]
    public void TheInstrumentIsNotVacuous()
    {
        using var layer = Layer(W, H);
        FrameRasterizer.AppendDraft(layer, Segment(Plain, 100, 200));
        var marked = MarkedPixels(layer);
        output.WriteLine($"a four-point plain segment marks {marked} px");
        Assert.True(marked > 200, $"the timed segment only marked {marked} px — it is not drawing");

        using var wet = Layer(W, H);
        FrameRasterizer.AppendDraft(wet, Segment(Watercolour, 100, 200));
        var wetMarked = MarkedPixels(wet);
        output.WriteLine($"a four-point watercolour segment marks {wetMarked} px");
        Assert.True(wetMarked > 200, $"the effect segment only marked {wetMarked} px — it is not drawing");

        Assert.Equal(4, (long)WideW * WideH / ((long)W * H));
    }

    // ---- the dirty region, which is where symmetry goes wrong -------------

    /// <summary>
    /// A live segment's repaint region is the reach of its own dabs, and a
    /// small fraction of the page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the test symmetry is most likely to break, and it cannot
    /// flake.</b> N copies of a mark land in N places — for a radial order of
    /// six, spread right around the centre of the canvas. The obvious
    /// implementation marks one dirty rectangle covering all of them, which for
    /// any order above two is very nearly the whole page: every pointer event
    /// then repaints the canvas, which is invariant 6 and charter O3 broken, and
    /// is the exact shape G7 exists to catch (a compositing buffer accumulating
    /// a union of every dirty region).
    /// </para>
    /// <para>
    /// So the number recorded here is per-copy reach. When symmetry lands, the
    /// published region must be N rectangles of about this size and not one
    /// rectangle enclosing them — and this assertion, unchanged, is what says
    /// so for the un-mirrored case that has to keep costing what it costs.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDirtyRegionIsTheSegmentsOwnReach()
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        var brush = Plain;
        var stroke = Segment(brush, 400, 270);

        var rect = BrushEngine.DraftSegmentBounds(stroke, info);
        Assert.NotNull(rect);

        var reach = BrushEngine.ReachOf(brush);
        var area = (long)rect!.Value.Width * rect.Value.Height;
        var page = (long)W * H;

        // The segment spans 9 px of x at one y, so the analytic region is
        // (span + 2·reach) × (2·reach) — anything much larger is not the mark.
        var expected = (9 + 2 * reach) * (2 * reach);
        output.WriteLine(
            $"reach {reach:0.0} px; region {rect.Value.Width}×{rect.Value.Height} = {area} px² "
            + $"({area / (double)page:P2} of the page); analytic {expected:0} px²");

        // Measured 2026-09-10: 41×32 = 1312 px², against an analytic 1312 —
        // the bound is exact, so the slack here is for rounding rather than for
        // any modelling error, and a tight bar is what makes this discriminating.
        Assert.True(
            area < expected * 1.2,
            $"the live region is {area} px² against an analytic {expected:0} px² — it is wider than the mark");
        Assert.True(
            area < page / 20,
            $"the live region is {area / (double)page:P1} of the page — a pointer event is paying for the canvas");
    }

    /// <summary>
    /// Two segments far apart have regions that are far apart, and each stays
    /// the size of its own mark.
    /// </summary>
    /// <remarks>
    /// The union trap stated directly. These two marks are what a two-axis
    /// mirror produces from one gesture near the left edge; a single rectangle
    /// covering both is 40× the paint. Recorded now against two ordinary
    /// strokes so that the arithmetic is on the record before there is any
    /// symmetry code to argue about.
    /// </remarks>
    [Fact]
    public void TwoDistantMarksAreTwoSmallRegionsNotOneLargeOne()
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        var left = BrushEngine.DraftSegmentBounds(Segment(Plain, 80, 270), info);
        var right = BrushEngine.DraftSegmentBounds(Segment(Plain, 840, 270), info);
        Assert.NotNull(left);
        Assert.NotNull(right);

        var separate = (long)left!.Value.Width * left.Value.Height
            + (long)right!.Value.Height * right.Value.Width;

        var union = SKRectI.Union(left.Value, right.Value);
        var unionArea = (long)union.Width * union.Height;

        output.WriteLine(
            $"two regions = {separate} px²; their union = {unionArea} px² "
            + $"— {unionArea / (double)separate:0.0}× the paint");

        Assert.True(
            unionArea > separate * 5,
            "these two marks are not far enough apart for this test to mean anything");
    }

    // ---- cost follows the segment, not the page --------------------------

    /// <summary>
    /// The same segment on a page of four times the area costs the same.
    /// </summary>
    /// <remarks>
    /// Paired and alternating, because the denominator is a couple of
    /// milliseconds and a millisecond of contention landing on one side moves a
    /// quotient several fold — the reason
    /// <c>FillCostFollowsTheRegionTests.TurningTheGapUpDoesNotGoQuadratic</c>
    /// is written this way after going red in a full-solution run and green on
    /// its own.
    /// </remarks>
    [Theory]
    [InlineData("plain")]
    [InlineData("watercolour")]
    public void ASegmentCostsTheSameOnAFourTimesLargerPage(string kind)
    {
        var brush = kind == "plain" ? Plain : Watercolour;
        using var small = Layer(W, H);
        using var wide = Layer(WideW, WideH);

        var (smallMs, wideMs) = Bench.PairedFastestMs(
            30,
            () => FrameRasterizer.AppendDraft(small, Segment(brush, 100, 200)),
            () => FrameRasterizer.AppendDraft(wide, Segment(brush, 100, 200)),
            log: output);

        output.WriteLine(
            $"{kind}: {W}×{H} in {smallMs:0.000} ms; {WideW}×{WideH} in {wideMs:0.000} ms "
            + $"— {wideMs / smallMs:0.00}× for 4× the area");

        // 1.25×, and the number comes from breaking it rather than from taste:
        // see AFullCanvasTouchPerEventIsWhatTheBarIsSetAgainst, where the
        // weakest regression of this shape — one memset per event — measured
        // 1.44× to 2.09× over six runs across two working trees. The shipped
        // path reads 0.99–1.01 over the same runs, and holds that tightly
        // because both sides of the pair do identical work. So the bar sits at
        // 1.25: roughly a quarter of headroom over the shipped figure, and
        // still clear of the weakest break on its cheapest-looking run.
        Assert.True(
            wideMs < smallMs * 1.25,
            $"a {kind} segment cost {wideMs:0.000} ms on 4× the area against {smallMs:0.000} ms "
            + $"({wideMs / smallMs:0.00}×) — the live path is paying for the page, not the segment");
    }

    /// <summary>
    /// What the test above would read if the live path went canvas-proportional
    /// again — so its bar is set by breaking it, not by guessing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A threshold the correct version clears easily is decoration. The
    /// shipped path measures 1.01× for four times the area; this measures the
    /// same segment with one full-canvas touch per event added, which is the
    /// regression shape by name — a full-canvas scratch per pointer event is
    /// what the live preview used to do, and what
    /// <c>LivePreview_EffectBrushSegment_IsBoundedToTheSegment</c> was written
    /// after removing.
    /// </para>
    /// <para>
    /// <b>It reads 1.44×–2.09×, not 4×, and that is the point.</b> A first guess put
    /// the bar above at 2.0× on the reasoning that four times the area costs
    /// four times as much; measured, the broken shape came in under it and the
    /// bar would have passed a full-canvas regression. The dilution is the fixed
    /// cost: stamping the segment is ~0.30 ms whatever the page, so a memset
    /// that grows from 0.11 ms to 0.43 ms moves the total from 0.41 to 0.73
    /// rather than quadrupling it. Any ratio bar on a path with a fixed floor
    /// has to be set against the measured break, never against the scaling of
    /// the part that grows.
    /// </para>
    /// <para>
    /// A memset is also the <em>weakest</em> regression of this shape — a
    /// per-pixel effect pass or a full-canvas composite would land far nearer
    /// 4× — so a bar that catches this one is conservative in the right
    /// direction. The assertion is that the broken shape still fails the 1.25×
    /// bar the test above applies; if it ever stops doing so, that bar has gone
    /// slack and both tests need re-setting together.
    /// </para>
    /// </remarks>
    [Fact]
    public void AFullCanvasTouchPerEventIsWhatTheBarIsSetAgainst()
    {
        using var small = Layer(W, H);
        using var wide = Layer(WideW, WideH);

        var (smallMs, wideMs) = Bench.PairedFastestMs(
            20,
            () =>
            {
                small.Erase(SKColors.Transparent);
                FrameRasterizer.AppendDraft(small, Segment(Plain, 100, 200));
            },
            () =>
            {
                wide.Erase(SKColors.Transparent);
                FrameRasterizer.AppendDraft(wide, Segment(Plain, 100, 200));
            },
            log: output);

        var ratio = wideMs / smallMs;
        output.WriteLine(
            $"canvas-proportional: {W}×{H} in {smallMs:0.000} ms; {WideW}×{WideH} in {wideMs:0.000} ms "
            + $"— {ratio:0.00}× for 4× the area (the shipped path reads 1.01×)");

        Assert.True(
            ratio > 1.25,
            $"a deliberately canvas-proportional pass only cost {ratio:0.00}× on 4× the area, so the 1.25× bar "
            + "in ASegmentCostsTheSameOnAFourTimesLargerPage does not discriminate and needs re-setting");
    }

    // ---- the scaling law symmetry will have to obey ----------------------

    /// <summary>
    /// Stamping a mark four times costs about four times one — not sixteen, and
    /// not four times a page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the acceptance criterion for symmetry switched on, registered
    /// before the feature exists.</b> Four copies of a mark is what an order-4
    /// radial symmetry does, and the honest budget for it is four times one
    /// mark. What it must not become is superlinear — a densify or a scratch
    /// allocation repeated per copy, a footprint ceiling rebuilt rather than
    /// accumulated (the shape of B299), or a full-canvas pass once per axis.
    /// </para>
    /// <para>
    /// Measured here with four ordinary segments at four different places,
    /// which is the same work by a different route and therefore the reference
    /// the real path is allowed to cost. The lower bound matters as much as the
    /// upper one: if four copies cost barely more than one, this test is
    /// measuring fixed overhead and would pass through any regression.
    /// </para>
    /// </remarks>
    [Fact]
    public void CostGrowsLinearlyInTheNumberOfCopies()
    {
        var brush = Plain;
        using var one = Layer(W, H);
        using var four = Layer(W, H);

        var (oneMs, fourMs) = Bench.PairedFastestMs(
            30,
            () => FrameRasterizer.AppendDraft(one, Segment(brush, 300, 260)),
            () =>
            {
                FrameRasterizer.AppendDraft(four, Segment(brush, 300, 120));
                FrameRasterizer.AppendDraft(four, Segment(brush, 300, 240));
                FrameRasterizer.AppendDraft(four, Segment(brush, 300, 360));
                FrameRasterizer.AppendDraft(four, Segment(brush, 300, 480));
            },
            log: output);

        output.WriteLine(
            $"1 copy {oneMs:0.000} ms; 4 copies {fourMs:0.000} ms — {fourMs / oneMs:0.00}× for 4× the marks");

        Assert.True(
            fourMs > oneMs * 1.5,
            $"4 copies cost {fourMs:0.000} ms against {oneMs:0.000} ms for one — this is timing overhead, "
            + "not stamping, and would pass through any regression");
        Assert.True(
            fourMs < oneMs * 8.0,
            $"4 copies cost {fourMs:0.000} ms against {oneMs:0.000} ms for one — {fourMs / oneMs:0.0}× for 4× "
            + "the marks is superlinear, so the per-copy work is not just stamping");
    }

    // ---- the pixels themselves ------------------------------------------

    /// <summary>
    /// The fingerprint of three renders, recorded before symmetry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The strongest available statement of "drawing without symmetry is
    /// untouched", and it costs nothing to run and cannot be moved by load. A
    /// symmetry implementation that reaches the un-mirrored path at all — a
    /// changed default, a transform applied with an identity that is not quite
    /// identity, a dab seeded from a transformed coordinate instead of an
    /// authored one — moves one of these three hashes.
    /// </para>
    /// <para>
    /// The scenarios localise rather than merely report. <c>scatter</c> is the
    /// one that reaches <c>Hash01</c>'s float path, and is the one that moves if
    /// dabs are ever seeded from a transformed position rather than from the
    /// coordinates the artist drew — the decision recorded in Q15's successor
    /// and the reason symmetry reflects the canvas rather than the geometry.
    /// <c>plain</c> isolates the dab loop with every stochastic control off.
    /// <c>soft</c> isolates the antialiased edge and the blend.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADrawnStrokeRendersByteForByteAsItDidBeforeSymmetry()
    {
        const string recorded = Baseline;
        var now = Fingerprints();
        output.WriteLine(now);

        if (recorded.Length == 0)
        {
            Assert.Fail(
                "no baseline recorded. Paste the three lines above into DrawingCostBaselineTests.Baseline, "
                + "from a run on the tree BEFORE any symmetry code exists.");
        }

        Assert.Equal(recorded, now);
    }

    /// <summary>
    /// Two fresh renders of the same input agree, so the fingerprint above is
    /// measuring the render rather than an empty bitmap.
    /// </summary>
    [Fact]
    public void TheFingerprintIsAFunctionOfTheStroke()
    {
        Assert.Equal(Fingerprints(), Fingerprints());
        Assert.NotEqual(Fingerprint(Scattered()), Fingerprint(PlainStroke()));
        Assert.NotEqual(Fingerprint(PlainStroke()), EmptyPage());
    }

    /// <summary>
    /// The three hashes as they stood before symmetry was written.
    /// </summary>
    /// <remarks>
    /// <b>Recorded 2026-09-10 on .NET 10, win-x64, Debug, at <c>ca00483d</c> —
    /// main's tip, and before a line of symmetry code existed.</b> That
    /// provenance is what the value is evidence about: a mismatch is only
    /// diagnosable against a named starting point. An invented value would be
    /// worse than none — it fails for a reason nobody can diagnose, which is the
    /// argument <c>RuntimeDeterminismTests</c> makes about its own baseline.
    /// <para>
    /// All three hashes came out identical on three different bases —
    /// <c>ca00483d</c>, <c>375eab76</c> (main plus a thumbnail-render fix) and
    /// <c>6d8dd22e</c> (main after that fix and the new-document defaults both
    /// landed). Worth recording as evidence in its own right: the fingerprint is
    /// a function of the render, not of whichever branch happened to be checked
    /// out when it was taken. B364 changed how a thumbnail is rendered and moved
    /// none of these, which is the behaviour a scale-only change should have.
    /// </para>
    /// </remarks>
    private const string Baseline =
        "plain=47DB12B7FC30F519880B60C6034518789E6AD87995448FB954C52DECB4468CB3\n"
        + "scatter=9642CD508E471E8A7A8D3BFB0EC64E221ADA02F517A7B2B805E2BC65B6403523\n"
        + "soft=76583F6762F80DAC19D635CF19CE01DC736DBA683296DB29D569537BD4011C45";

    private static Stroke PlainStroke() => new()
    {
        Tool = ToolKind.Brush,
        Color = "#2a4a8a",
        Brush = new BrushSettings
        {
            Size = 26, Hardness = 1.0, Opacity = 1.0, Flow = 1.0, Spacing = 0.1,
            PressureFlowGamma = 1,
        },
        Points = [new StrokePoint(40, 40, 0.8), new StrokePoint(90, 70, 0.9), new StrokePoint(150, 55, 0.7)],
    };

    private static Stroke Scattered() => new()
    {
        Tool = ToolKind.Brush,
        Color = "#c04020",
        Brush = new BrushSettings
        {
            Size = 20, Hardness = 0.7, Opacity = 0.9, Flow = 0.7, Spacing = 0.15,
            Scatter = 0.6, RotationJitter = 0.5, PressureFlowGamma = 1,
        },
        Points = [new StrokePoint(40, 60, 0.8), new StrokePoint(100, 60, 0.9), new StrokePoint(160, 60, 0.6)],
    };

    private static Stroke SoftEdged() => new()
    {
        Tool = ToolKind.Brush,
        Color = "#204080",
        Brush = new BrushSettings
        {
            Size = 44, Hardness = 0.15, Opacity = 0.6, Flow = 0.4, Spacing = 0.2,
            PressureFlowGamma = 1,
        },
        Points = [new StrokePoint(50, 70, 0.5), new StrokePoint(140, 70, 1.0)],
    };

    private static string Fingerprint(Stroke stroke)
    {
        using var bitmap = FrameRasterizer.Rasterize([stroke], 200, 140, 1.0);
        return Convert.ToHexString(SHA256.HashData(bitmap.GetPixelSpan()));
    }

    private static string EmptyPage()
    {
        using var bitmap = FrameRasterizer.Rasterize([], 200, 140, 1.0);
        return Convert.ToHexString(SHA256.HashData(bitmap.GetPixelSpan()));
    }

    /// <summary>One line per scenario, so a diff names which one moved.</summary>
    private static string Fingerprints() =>
        string.Join(
            "\n",
            $"plain={Fingerprint(PlainStroke())}",
            $"scatter={Fingerprint(Scattered())}",
            $"soft={Fingerprint(SoftEdged())}");
}
