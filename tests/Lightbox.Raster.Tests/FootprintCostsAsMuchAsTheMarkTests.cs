using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// What the second walk over every dab costs — the footprint — against the
/// first, which is the mark itself (B189).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every dab in the live path is stamped twice.</b> Once as colour into the
/// scratch, once as a footprint into the coverage buffer, both document-sized.
/// The footprint is a running maximum of the tip's coverage, kept so a soft
/// brush can be capped to it (Q157, B293, B299) — load-bearing, not vestigial.
/// </para>
/// <para>
/// <b>Why it is measured here rather than read off a capture.</b> The in-app
/// split put the footprint at 56% of the dab work, but its timer includes a
/// <c>Flush</c> and the colour stamp's does not, so the two are not directly
/// comparable. This times both the same way, on the same dabs, with no flush in
/// either — which is the only version of the question that has one answer.
/// </para>
/// <para>
/// <b>Reported, not bounded.</b> The number decides where B189's remaining cost
/// is, and a threshold here would be a guess at a figure nobody has. What is
/// asserted is only that the footprint is a real share rather than a rounding
/// error, because if it were negligible the in-app split would be wrong and
/// that is worth failing over.
/// </para>
/// </remarks>
public class FootprintCostsAsMuchAsTheMarkTests(ITestOutputHelper output)
{
    private const int Width = 3840;
    private const int Height = 2160;

    /// <summary>
    /// A soft brush at the owner's size: soft, because the footprint cap only
    /// applies to brushes that can outrun their own footprint.
    /// </summary>
    private static BrushSettings Soft() => new()
    {
        Size = 70, Hardness = 0.6, Flow = 0.7, Opacity = 1,
    };

    private static Stroke Long(BrushSettings brush)
    {
        var pts = new List<StrokePoint>(600);
        double x = 200, y = 700, heading = -0.15;
        for (var i = 0; i < 600; i++)
        {
            pts.Add(new StrokePoint(x, y, 1));
            heading += 0.0012;
            x += 4.2 * Math.Cos(heading);
            y += 4.2 * Math.Sin(heading);
        }

        return new Stroke { Tool = ToolKind.Brush, Color = "#203040", Brush = brush, Points = pts };
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void TheFootprintWalkCostsAsMuchAsTheColourWalk()
    {
        var brush = Soft();
        Assert.True(
            BrushEngine.NeedsFootprintCap(brush),
            "this brush does not take the footprint path at all, so the test is measuring "
            + "nothing — pick one that does or the number is meaningless");

        var stroke = Long(brush);
        var dabs = BrushEngine.WalkDabs(stroke);
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);

        using var scratch = new SKBitmap(info);
        using var colourCanvas = new SKCanvas(scratch);
        using var coverage = SKSurface.Create(
            new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        Assert.NotNull(coverage);
        coverage!.Canvas.Clear(SKColors.Black);

        const int Range = 240;
        // Warm both paths so neither pays for jitting inside the clock.
        BrushEngine.StampDabRange(colourCanvas, stroke, dabs, 0, 32);
        BrushEngine.AccumulateFootprint(coverage.Canvas, stroke, dabs, 0, 32);

        double colour = double.MaxValue, footprint = double.MaxValue;
        for (var i = 0; i < 5; i++)
        {
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            BrushEngine.StampDabRange(colourCanvas, stroke, dabs, 0, Range);
            colour = Math.Min(colour, Ms(t0));

            var t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            BrushEngine.AccumulateFootprint(coverage.Canvas, stroke, dabs, 0, Range);
            footprint = Math.Min(footprint, Ms(t1));
        }

        var share = footprint / (colour + footprint) * 100;
        output.WriteLine($"  {Range} dabs at size {brush.Size} on {Width}x{Height}, best of 5, no flush:");
        output.WriteLine($"    colour     {colour,8:0.##} ms   ({colour * 1000 / Range,6:0} us a dab)");
        output.WriteLine($"    footprint  {footprint,8:0.##} ms   ({footprint * 1000 / Range,6:0} us a dab)");
        output.WriteLine($"    the footprint is {share:0}% of the dab work");
        output.WriteLine($"  the in-app split, whose footprint timer includes a Flush, said 56%");

        Assert.True(
            share > 15,
            $"the footprint is only {share:0}% of the dab work here, so the in-app split's 56% "
            + "was mostly its Flush and B189's remaining cost is in the colour stamp instead");

        // **What dropping the coverage buffer's resolution would buy**, measured
        // rather than assumed. The owner's instruction, 2026-08-28: measure the
        // real gain before changing what any mark looks like. This is the same
        // method that predicted the tip's saving offline and was then confirmed
        // in situ at 33.69 us a dab, so it is the cheapest honest answer — and
        // it needs no change to the cap's pixel loop, which is what a flagged
        // in-app arm would require.
        const double ComposeScale = 0.375;
        var small = new SKImageInfo(
            (int)Math.Ceiling(Width * ComposeScale), (int)Math.Ceiling(Height * ComposeScale),
            SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var smallCoverage = SKSurface.Create(small);
        Assert.NotNull(smallCoverage);
        smallCoverage!.Canvas.Clear(SKColors.Black);
        smallCoverage.Canvas.Scale((float)ComposeScale);
        BrushEngine.AccumulateFootprint(smallCoverage.Canvas, stroke, dabs, 0, 32);

        var scaled = double.MaxValue;
        for (var i = 0; i < 5; i++)
        {
            var t = System.Diagnostics.Stopwatch.GetTimestamp();
            BrushEngine.AccumulateFootprint(smallCoverage.Canvas, stroke, dabs, 0, Range);
            scaled = Math.Min(scaled, Ms(t));
        }

        output.WriteLine("");
        output.WriteLine(
            $"    footprint at {ComposeScale} scale  {scaled,8:0.##} ms   ({scaled * 1000 / Range,6:0} us a dab)"
            + $"   {footprint / scaled:0.#}x cheaper");
        var saved = footprint - scaled;
        output.WriteLine(
            $"  >> that is {saved:0.##} ms off {colour + footprint:0.##} ms of dab work,"
            + $" or {100 * saved / (colour + footprint):0}% of an event's stamping");

        Assert.True(
            scaled < footprint,
            "accumulating the footprint over fewer pixels did not cost less, so the resolution "
            + "is not the lever and B189 needs a different idea");
    }

    private static double Ms(long since) =>
        (System.Diagnostics.Stopwatch.GetTimestamp() - since) * 1000.0
        / System.Diagnostics.Stopwatch.Frequency;
}
