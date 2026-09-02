using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// A smudge moves pigment. It does not darken it, desaturate it, or eat the mark.
/// </summary>
/// <remarks>
/// <para>
/// <b>B91.</b> Reported as "smudge lightens the pigment on the page instead of
/// smearing it". Three defects were behind it, and the first is the one that makes
/// the rest legible:
/// </para>
/// <list type="number">
/// <item><description>
/// <c>SampleAverage</c> averaged <em>unpremultiplied</em> RGB and alpha as four
/// independent channels. Skia reports a transparent pixel as <c>rgb(0,0,0)</c>, so
/// every tap landing off a mark's edge pulled the sample toward black. A white block
/// sampled at its own edge deposited <c>rgb(50,50,50)</c>.
/// </description></item>
/// <item><description>
/// <c>Mix</c> did the same to the carried colour dab over dab.
/// </description></item>
/// <item><description>
/// <c>LerpDab</c> truncated its byte writes instead of rounding. The bias only ever
/// points one way and compounds over the ~40 dabs that overlap a pixel, and it costs
/// small channel values most — a red mark <c>rgb(220,40,40)</c> arrived down the
/// stroke as <c>rgb(211,12,12)</c>, its green and blue all but gone.
/// </description></item>
/// </list>
/// <para>
/// <b>What was deliberately left alone.</b> A smudge still lowers coverage at the
/// boundary it is dragged across, and that is correct — it is the feathered edge you
/// get dragging a finger through wet paint. Measured, it is a smooth ramp over 11–13
/// columns that dies to nothing inside the mark, not a hole in the body. Krita's
/// answer to wanting it gone is a <i>Smear Alpha</i> toggle; nothing here needs one,
/// because with the three fixes above the mark keeps its colour and its body.
/// </para>
/// </remarks>
public class SmudgeTransportTests(ITestOutputHelper output)
{
    private const int W = 300, H = 120;
    private const int Edge = 150;

    /// <summary>A solid block on the left, bare canvas on the right.</summary>
    private static SKBitmap Block(SKColor colour)
    {
        var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = colour, IsAntialias = false };
        canvas.DrawRect(new SKRect(0, 0, Edge, H), paint);
        canvas.Flush();
        return bmp;
    }

    private static Stroke SmudgeOut(SmudgeMode mode, double flow, double length) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#000000",
        // Starts inside the block and drags out across the boundary.
        Points = [.. Enumerable.Range(0, 25).Select(i => new StrokePoint(100 + i * 4, 60, 1))],
        Brush = new BrushSettings
        {
            Kind = BrushKind.Smudge, Size = 20, Hardness = 0.5, Flow = flow, Spacing = 0.1,
            SmudgeMode = mode, SmudgeLength = length, SmudgeRadius = 0.5,
        },
    };

    private static SKBitmap Smudged(SKColor colour, SmudgeMode mode, double flow, double length = 0.5)
    {
        var bmp = Block(colour);
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        BrushEngine.StampStroke(canvas, SmudgeOut(mode, flow, length), info, bmp);
        canvas.Flush();
        return bmp;
    }

    /// <summary>Farthest column past the original edge still carrying real coverage.</summary>
    private static int Reach(SKBitmap b)
    {
        var reach = 0;
        for (var x = Edge; x < W; x++)
        {
            if (b.GetPixel(x, 60).Alpha > 8) reach = x - Edge;
        }
        return reach;
    }

    /// <summary>
    /// The sharpest form of the bug: one dab straddling the edge, four of its five
    /// taps on bare canvas.
    /// </summary>
    /// <remarks>
    /// Dulling, because the five-tap sampler this guards is dulling's since B92 —
    /// a smearing dab reads its pixels one for one and has no sampler to get wrong,
    /// and a smear that has not moved deposits nothing (<c>SmearDetailTests</c>).
    /// </remarks>
    [Theory]
    [InlineData(255, 255, 255)]   // white — was rgb(50,50,50)
    [InlineData(220, 40, 40)]     // red   — was rgb(40,5,5)
    [InlineData(40, 90, 220)]     // blue
    public void AMarkSmudgesItsOwnColourRatherThanADarkenedGhost(byte r, byte g, byte b)
    {
        var colour = new SKColor(r, g, b, 255);
        using var bmp = Block(colour);
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        var tap = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#000000",
            Points = [new StrokePoint(Edge, 60, 1), new StrokePoint(Edge + 1, 60, 1)],
            Brush = new BrushSettings
            {
                Kind = BrushKind.Smudge, Size = 30, Hardness = 1, Flow = 1, Spacing = 0.5,
                SmudgeMode = SmudgeMode.Dulling, SmudgeRadius = 1.0,
            },
        };
        using (var canvas = new SKCanvas(bmp))
        {
            BrushEngine.StampStroke(canvas, tap, info, bmp);
            canvas.Flush();
        }

        var got = bmp.GetPixel(Edge, 60);
        output.WriteLine($"source rgb({r},{g},{b}) -> deposited rgb({got.Red},{got.Green},{got.Blue}) a{got.Alpha}");

        Assert.True(got.Alpha > 0, "the dab deposited nothing at all — this would pass on a dead brush");
        Assert.True(
            Math.Abs(got.Red - r) <= 4 && Math.Abs(got.Green - g) <= 4 && Math.Abs(got.Blue - b) <= 4,
            $"the smudge deposited rgb({got.Red},{got.Green},{got.Blue}) where the mark is rgb({r},{g},{b}) — "
            + "transparent neighbours are colouring the sample");
    }

    [Theory]
    [InlineData(SmudgeMode.Smearing)]
    [InlineData(SmudgeMode.Dulling)]
    public void AColouredMarkKeepsItsHueDownTheStroke(SmudgeMode mode)
    {
        var colour = new SKColor(220, 40, 40, 255);
        using var bmp = Smudged(colour, mode, 0.08);

        // The farthest place the smear still has real body, which is where an
        // accumulated bias shows up worst.
        var at = -1;
        SKColor carried = default;
        for (var x = Edge + 1; x < W; x++)
        {
            var c = bmp.GetPixel(x, 60);
            if (c.Alpha > 40) { carried = c; at = x; }
        }

        Assert.True(at > 0, "nothing with real coverage was carried past the edge at all");
        output.WriteLine(
            $"{mode}: carried rgb({carried.Red},{carried.Green},{carried.Blue}) a{carried.Alpha} "
            + $"at x={at} ({at - Edge}px out), source rgb(220,40,40)");

        // Green and blue are the channels truncation used to eat: 40 -> 12.
        Assert.True(
            Math.Abs(carried.Green - 40) <= 8 && Math.Abs(carried.Blue - 40) <= 8,
            $"the carried colour desaturated to rgb({carried.Red},{carried.Green},{carried.Blue})");
    }

    /// <summary>
    /// The mark may feather where it is dragged across; it may not be hollowed out.
    /// </summary>
    [Theory]
    [InlineData(SmudgeMode.Smearing)]
    [InlineData(SmudgeMode.Dulling)]
    public void SmudgingDoesNotHollowOutTheBodyOfAMark(SmudgeMode mode)
    {
        using var before = Block(SKColors.Black);
        using var after = Smudged(SKColors.Black, mode, 0.08);

        var columns = 0;
        var worst = 0;
        for (var x = 0; x < Edge; x++)
        {
            var lost = before.GetPixel(x, 60).Alpha - after.GetPixel(x, 60).Alpha;
            if (lost <= 0) continue;
            columns++;
            worst = Math.Max(worst, lost);
        }
        output.WriteLine($"{mode}: {columns} columns lost coverage, worst {worst}/255, all within {columns}px of the edge");

        // The stroke starts 50px inside the block, so anything beyond a boundary
        // feather would show up deep in the body.
        for (var x = 0; x < Edge - 30; x++)
        {
            Assert.True(
                after.GetPixel(x, 60).Alpha == 255,
                $"the mark lost coverage {Edge - x}px inside its own body at x={x}");
        }
        Assert.True(columns <= 25, $"the feather is {columns}px wide, which is a hole rather than an edge");
    }

    /// <summary>
    /// Both knobs reach the trail, and monotonically — B90 put strength on the bar,
    /// so this is what that control is worth.
    /// </summary>
    [Fact]
    public void TheTrailLengthRespondsToItsControls()
    {
        var byLength = new[] { 0.25, 0.5, 0.75, 1.0 }
            .Select(len => (len, reach: Reach(Smudged(SKColors.Black, SmudgeMode.Smearing, 0.08, len))))
            .ToArray();
        var byStrength = new[] { 0.08, 0.3 }
            .Select(f => (f, reach: Reach(Smudged(SKColors.Black, SmudgeMode.Smearing, f))))
            .ToArray();

        foreach (var (len, reach) in byLength) output.WriteLine($"SmudgeLength {len:F2} -> {reach}px");
        foreach (var (f, reach) in byStrength) output.WriteLine($"Strength {f:F2} -> {reach}px");

        Assert.True(byLength[0].reach > 0, "the shortest trail carried nothing — nothing is being measured");
        for (var i = 1; i < byLength.Length; i++)
        {
            Assert.True(
                byLength[i].reach >= byLength[i - 1].reach,
                $"trail is not monotonic in length: {byLength[i - 1]} then {byLength[i]}");
        }
        Assert.True(
            byLength[^1].reach > byLength[0].reach * 2,
            $"smudge length barely moved the trail: {byLength[0].reach}px to {byLength[^1].reach}px");
        Assert.True(
            byStrength[1].reach > byStrength[0].reach,
            $"strength did not lengthen the trail: {byStrength[0].reach}px to {byStrength[1].reach}px");
    }
}
