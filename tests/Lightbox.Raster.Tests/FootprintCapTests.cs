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
