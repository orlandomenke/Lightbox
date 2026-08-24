using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// A soft brush's hardness has to mean what it says. See
/// <c>BrushEngine.NeedsFootprintCap</c> for the mechanism it was losing to.
/// </summary>
/// <remarks>
/// <b>Every reading here is a profile ACROSS the mark, never a value along
/// it.</b> Alpha saturates along a stroke, so a reading down the centreline
/// reports the same thing whatever the brush is doing — the trap this suite
/// documents. The edge width is the honest measure: it cannot be reached by
/// accumulating anything.
/// </remarks>
public class FootprintCapTests(ITestOutputHelper output)
{
    private const int W = 120, H = 120;

    private static BrushSettings Soft(double hardness, double flow = 1) => new()
    {
        Size = 30, Hardness = hardness, Opacity = 1, Flow = flow, Spacing = 0.15, AntiAlias = true,
    };

    private static SKBitmap Stroke_(BrushSettings b) => FrameRasterizer.Rasterize(
        [new Stroke
        {
            Tool = ToolKind.Brush, Color = "#000000", Brush = b,
            Points = [new(60, 20, 1), new(60, 100, 1)],
        }], W, H);

    private static SKBitmap Dab(BrushSettings b) => FrameRasterizer.Rasterize(
        [new Stroke { Tool = ToolKind.Brush, Color = "#000000", Brush = b, Points = [new(60, 60, 1)] }],
        W, H);

    /// <summary>Distance over which alpha falls from 0.9 to 0.1, across the mark.</summary>
    private static double EdgeWidth(SKBitmap bmp)
    {
        double? a90 = null, a10 = null;
        for (var x = 60; x < W; x++)
        {
            var a = bmp.GetPixel(x, 60).Alpha / 255.0;
            if (a90 is null && a <= 0.9) a90 = x;
            if (a10 is null && a <= 0.1) { a10 = x; break; }
        }
        return (a10 ?? W) - (a90 ?? 60);
    }

    [Theory]
    [InlineData(0.10)]
    [InlineData(0.35)]
    [InlineData(0.60)]
    public void AStrokesEdgeIsAsSoftAsItsOwnDabs(double hardness)
    {
        // The brush's own single dab is the reference, because that is the
        // falloff the artist set. Before the cap the stroke's edge came out at
        // 6 px against 11, 4 against 8, and 3 against 4 — the softest settings
        // losing the most, which is the wrong way round.
        var b = Soft(hardness);
        using var stroke = Stroke_(b);
        using var dab = Dab(b);
        double s = EdgeWidth(stroke), d = EdgeWidth(dab);
        output.WriteLine($"hardness {hardness:0.00}: stroke edge {s} px, its own dab {d} px");

        Assert.True(d >= 3, $"the reference dab has no soft edge to compare against ({d} px)");
        Assert.Equal(d, s);
    }

    [Fact]
    public void TheProfileMatchesTheDabPointForPoint()
    {
        var b = Soft(0.35);
        using var stroke = Stroke_(b);
        using var dab = Dab(b);
        for (var i = 0; i <= 8; i++)
        {
            var x = 60 + i * 2;
            int s = stroke.GetPixel(x, 60).Alpha, d = dab.GetPixel(x, 60).Alpha;
            output.WriteLine($"  +{i * 2,2} px: stroke {s,3}, dab {d,3}");
            Assert.True(Math.Abs(s - d) <= 2, $"at +{i * 2} px the stroke reads {s} and its dab {d}");
        }
    }

    [Fact]
    public void PaintStillBuildsUpWithinAStroke()
    {
        // The ceiling must not turn an airbrush into a wash. Below the footprint
        // it does not bind at all, so flow behaves exactly as it did.
        foreach (var (flow, atLeast) in new[] { (0.05, 0.15), (0.10, 0.30), (0.25, 0.60) })
        {
            var b = Soft(0.35, flow);
            using var stroke = Stroke_(b);
            using var dab = Dab(b);
            double centre = stroke.GetPixel(60, 60).Alpha / 255.0;
            double one = dab.GetPixel(60, 60).Alpha / 255.0;
            output.WriteLine($"flow {flow:0.00}: one dab {one:0.000}, stroke centre {centre:0.000}");
            Assert.True(
                centre > atLeast,
                $"flow {flow:0.00} stopped building up: the mark's centre reads {centre:0.000}");
            Assert.True(centre > one * 2, "overlap is no longer darkening at all");
        }
    }

    [Fact]
    public void TheCapKeepsThePixelsPremultiplied()
    {
        // The scratch is premultiplied, so clamping alpha without scaling the
        // colour channels leaves a pixel whose colour is brighter than its own
        // alpha. That is not a colour: it composites as an over-bright halo
        // exactly where the ceiling bit.
        //
        // Asserted on the RAW bytes on purpose. GetPixel unpremultiplies and
        // clamps to 255, so it reports a perfectly ordinary red for a pixel that
        // is broken — this test passed against the bug before it read the buffer
        // directly.
        var b = Soft(0.2);
        using var bmp = FrameRasterizer.Rasterize(
            [new Stroke
            {
                Tool = ToolKind.Brush, Color = "#ff2200", Brush = b,
                Points = [new(60, 20, 1), new(60, 100, 1)],
            }], W, H);

        using var pixels = bmp.PeekPixels();
        Assert.NotNull(pixels);
        var span = pixels!.GetPixelSpan<byte>();
        var row = pixels.RowBytes;
        var checked_ = 0;
        var worst = 0;
        for (var y = 20; y < 100; y++)
        for (var x = 60; x < 90; x++)
        {
            var i = y * row + x * 4;
            var a = span[i + 3];
            if (a is 0 or 255) continue;
            checked_++;
            var over = Math.Max(
                Math.Max(span[i] - a, span[i + 1] - a), span[i + 2] - a);
            if (over > worst) worst = over;
        }
        output.WriteLine($"{checked_} partial pixels, worst channel above its own alpha: {worst}");
        Assert.True(checked_ > 200, $"only {checked_} partial pixels — the falloff is not being sampled");
        Assert.True(worst <= 1, $"a colour channel sits {worst}/255 above its own alpha");
    }

    /// <summary>
    /// <b>A brush whose flow can never reach its own footprint does not pay for
    /// the ceiling</b> (B293).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The derivation is in <c>BrushEngine.CanOutrunItsFootprint</c>: a pixel is
    /// covered by at most one diameter of travel, a diameter holds
    /// <c>1/spacing</c> dabs, so <c>n·flow ≤ flow/spacing</c> and
    /// <c>flow ≤ spacing</c> cannot reach the footprint however the dabs fall.
    /// </para>
    /// <para>
    /// <b>The rows above spacing are the half that keeps this honest.</b> A
    /// predicate that answered "no ceiling" to everything would satisfy the
    /// first three and is what this is guarding against.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(0.02, false)]  // an airbrush dialled right down
    [InlineData(0.05, false)]
    [InlineData(0.15, false)]  // exactly at spacing: the bound is not strict
    [InlineData(0.25, true)]   // above it, and the ceiling is kept even though
    [InlineData(1.00, true)]   // it happens not to bind until about 0.5
    public void OnlyAFlowThatCanReachTheFootprintPaysForTheCeiling(double flow, bool expected)
    {
        var brush = Soft(0.35, flow);
        output.WriteLine($"flow {flow:0.00} vs spacing {brush.Spacing:0.00}: capped {BrushEngine.NeedsFootprintCap(brush)}");
        Assert.Equal(expected, BrushEngine.NeedsFootprintCap(brush));
    }

    /// <summary>
    /// <b>Scatter puts the ceiling back, because scatter is what breaks the
    /// bound the skip rests on.</b>
    /// </summary>
    /// <remarks>
    /// A dab thrown <c>scatter × Size</c> off the centreline can be reached from
    /// a longer stretch of travel, so more dabs pile onto one pixel than
    /// <c>1/spacing</c> of them — three times as many at scatter 1. And the
    /// throw is measured in the brush's nominal size while the step follows the
    /// pressure-scaled one, so a light touch widens the ratio without limit.
    /// This is the row that would have shipped a wrong "sufficient condition".
    /// </remarks>
    [Fact]
    public void AScatteredBrushKeepsTheCeilingHoweverLowItsFlow()
    {
        var still = Soft(0.35, 0.02);
        var scattered = new BrushSettings
        {
            Size = 30, Hardness = 0.35, Opacity = 1, Flow = 0.02, Spacing = 0.15,
            AntiAlias = true, Scatter = 0.4,
        };
        output.WriteLine($"flow 0.02, spacing 0.15: unscattered capped {BrushEngine.NeedsFootprintCap(still)}, "
            + $"scatter 0.4 capped {BrushEngine.NeedsFootprintCap(scattered)}");

        Assert.False(BrushEngine.NeedsFootprintCap(still));
        Assert.True(BrushEngine.NeedsFootprintCap(scattered));
    }

    /// <summary>
    /// <b>And skipping it there changes no pixel.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two brushes differ by <b>one ulp of flow</b> — <c>0.15</c> against
    /// the next representable double above it — which is why this is a test of
    /// the code path rather than of the parameter. The dabs land in the same
    /// places with the same shape at a flow that cannot differ by a
    /// two-hundred-and-fifty-fifth of anything; the only thing either render
    /// does differently is take or skip the ceiling.
    /// </para>
    /// <para>
    /// A confounded version of this measurement read 118 changed pixels and was
    /// wrong: it disabled the cap by turning <c>AntiAlias</c> off, which also
    /// changes how every dab is drawn. The lesson is the one
    /// <c>docs/DESIGN-performance.md</c> records for timings and it applies to
    /// pixels — <i>ask what else is in this measurement</i>.
    /// </para>
    /// </remarks>
    [Fact]
    public void SkippingTheCeilingWhereItCannotBindChangesNoPixel()
    {
        var skipped = Soft(0.35, 0.15);
        var capped = Soft(0.35, Math.BitIncrement(0.15));
        Assert.False(BrushEngine.NeedsFootprintCap(skipped));
        Assert.True(BrushEngine.NeedsFootprintCap(capped));

        using var a = Stroke_(skipped);
        using var b = Stroke_(capped);
        var (differing, worst, partial) = Compare(a, b);
        output.WriteLine($"one ulp of flow apart: {differing} px differ, worst {worst}/255, over {partial} partial px");

        Assert.True(partial > 200, $"only {partial} partial pixels — the falloff is not being sampled");
        Assert.Equal(0, differing);

        // And the comparison has teeth: a flow that really is above the
        // footprint moves thousands of these same pixels.
        using var loud = Stroke_(Soft(0.35, 1.0));
        var (moved, byHowMuch, _) = Compare(a, loud);
        output.WriteLine($"against flow 1.00: {moved} px differ, worst {byHowMuch}/255");
        Assert.True(moved > 1000, $"the comparison found only {moved} changed pixels where it should find thousands");
    }

    /// <summary>Pixels that differ, the worst channel difference, and how many are partial.</summary>
    private static (int Differing, int Worst, int Partial) Compare(SKBitmap a, SKBitmap b)
    {
        using var pa = a.PeekPixels();
        using var pb = b.PeekPixels();
        var sa = pa!.GetPixelSpan<byte>();
        var sb = pb!.GetPixelSpan<byte>();
        int differing = 0, worst = 0, partial = 0;
        for (var i = 0; i < sa.Length; i += 4)
        {
            var alpha = sa[i + 3];
            if (alpha is not (0 or 255)) partial++;
            var d = 0;
            for (var c = 0; c < 4; c++) d = Math.Max(d, Math.Abs(sa[i + c] - sb[i + c]));
            if (d == 0) continue;
            differing++;
            if (d > worst) worst = d;
        }
        return (differing, worst, partial);
    }

    [Theory]
    [InlineData(0.35, false, MediumKind.None, true)]    // the soft round family
    [InlineData(1.00, false, MediumKind.None, false)]   // hard: the silhouette owns it
    [InlineData(0.35, true, MediumKind.None, false)]    // aliased on purpose
    [InlineData(0.35, false, MediumKind.Watercolour, false)] // a medium: B27's bleed
    public void OnlyBrushesWhoseFalloffIsTheMarkGetCapped(
        double hardness, bool aliased, MediumKind medium, bool expected)
    {
        var brush = new BrushSettings
        {
            Size = 30, Hardness = hardness, Flow = 1, Opacity = 1, Spacing = 0.15,
            AntiAlias = !aliased,
            Medium = new MediumSettings { Kind = medium },
        };
        Assert.Equal(expected, BrushEngine.NeedsFootprintCap(brush));
    }
}
