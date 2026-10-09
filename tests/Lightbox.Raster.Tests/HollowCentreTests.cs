using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// B431: a simulated wash must not be hollow down its middle. The capillary
/// term carved a channel a few cells wide along the stroke's medial axis — the
/// cells where the distance to dry paper peaks give pigment outward and, having
/// no neighbour further in, receive none back — so even at the near-zero edge
/// pull the Watercolor preset shipped with, the centre line read as a pale
/// seam, and edge pull could never be turned up to pool at the rim.
/// </summary>
public class HollowCentreTests(ITestOutputHelper output)
{
    private const int W = 360, H = 140;

    private static Stroke Wash(double edgePull, double size = 42) => new()
    {
        Color = "#101828",
        Points = [.. Enumerable.Range(0, 12).Select(i => new StrokePoint(30 + i * 27.0, 70, 0.9))],
        Brush = new BrushSettings
        {
            Size = size, Hardness = 0.25, Opacity = 0.55, Flow = 0.45, Spacing = 0.08,
            Medium = new MediumSettings
            {
                Kind = MediumKind.Watercolour,
                Wetness = 0.85, Viscosity = 0.1, Drag = 0.25, FlowSteps = 16,
                Absorbency = 0.35, EdgePull = edgePull,
                PigmentDensity = 0.5, Granularity = 0, Hiding = 0.05,
                Paper = PaperKind.Smooth, PaperScale = 14, PaperInfluence = 0,
                PressureWater = 0.8, Rewetting = 0,
            },
        },
    };

    /// <summary>
    /// The centre line against the interior a few pixels either side of it,
    /// at four points along the stroke. Smooth paper and no granulation, so
    /// the only structure left in the interior is the solver's own.
    /// </summary>
    private static (double Worst, string Report) CentreAgainstInterior(SKBitmap bmp)
    {
        var worst = 1.0;
        var lines = new List<string>();
        foreach (var x in new[] { 120, 180, 240, 300 })
        {
            var column = Enumerable.Range(45, 51).Select(y => (int)bmp.GetPixel(x, y).Alpha).ToArray();
            var centre = column.Skip(23).Take(5).Average();
            var beside = Math.Max(column.Skip(14).Take(6).Average(), column.Skip(31).Take(6).Average());
            var ratio = centre / Math.Max(1, beside);
            worst = Math.Min(worst, ratio);
            lines.Add($"x={x}: centre {centre:F0} beside {beside:F0} ratio {ratio:F2}");
        }
        return (worst, string.Join("; ", lines));
    }

    /// <summary>
    /// Set by breaking it: before the fix the shipped edge pull of 0.06 gave a
    /// worst centre-to-interior ratio of 0.82 (centre 67 against 84 beside),
    /// and 0.30 gave a white line. A real wash has its interior flat to within
    /// a few percent; the rim is where the pigment goes, not the cells either
    /// side of the middle.
    /// </summary>
    [Theory]
    [InlineData(0.06)]
    [InlineData(0.30)]
    public void AWetStrokeHasNoChannelDownItsMiddle(double edgePull)
    {
        using var bmp = FrameRasterizer.Rasterize([Wash(edgePull)], W, H);
        var (worst, report) = CentreAgainstInterior(bmp);
        output.WriteLine($"edge pull {edgePull}: {report}");
        Assert.True(worst >= 0.95, $"the wash is hollow down its middle at edge pull {edgePull}: {report}");
    }

    /// <summary>
    /// The rim the pull exists for is still there: at the slider's end the
    /// outermost wet pixels are the darkest in the profile — the coffee ring —
    /// while the middle holds. Rim and centre separately, so a wash that
    /// simply got fainter or simply emptied its middle cannot pass.
    /// </summary>
    /// <remarks>
    /// Measured across the fix: with the pull off the top edge's outer six
    /// rows average 13 (a dome's shoulder); at EdgePull 1 they average 37 —
    /// darkest at the edge — and the middle reads 51 against 92. Before the
    /// fix the middle fell to 26 and the edge barely moved, because the pull
    /// drained the centre into the cells beside it rather than to the rim.
    /// </remarks>
    [Fact]
    public void EdgePullBuildsARimAtTheEdgeAndLeavesTheMiddleAWash()
    {
        using var none = FrameRasterizer.Rasterize([Wash(0)], W, H);
        using var full = FrameRasterizer.Rasterize([Wash(1.0)], W, H);

        // The outermost six wet rows of the top edge, averaged along the stroke.
        double Rim(SKBitmap b) => Enumerable.Range(120, 180).Select(x =>
        {
            var col = Enumerable.Range(30, 45).Select(y => (int)b.GetPixel(x, y).Alpha).ToList();
            var first = col.FindIndex(a => a > 2);
            return first < 0 ? 0 : col.Skip(first).Take(6).Average();
        }).Average();
        double Centre(SKBitmap b) => Enumerable.Range(120, 180).Average(x => (double)b.GetPixel(x, 70).Alpha);

        var rimNone = Rim(none); var rimFull = Rim(full);
        var midNone = Centre(none); var midFull = Centre(full);
        output.WriteLine($"rim {rimNone:F1} -> {rimFull:F1}, centre {midNone:F1} -> {midFull:F1}");
        Assert.True(rimFull > rimNone * 1.25, $"edge pull did not darken the rim: {rimNone:F1} -> {rimFull:F1}");
        // The ring is made of the interior's pigment, so the slider's end
        // costs the middle nearly half (92 -> 51 measured): a drying puddle's
        // look, which is what the end of the slider names. The bound is set
        // against the defect rather than against the tuning: the per-cell walk
        // left 26 of 92 (0.28), and 0.4 refuses that with room for the rate
        // to be retuned without this clause moving.
        Assert.True(midFull > midNone * 0.4, $"edge pull emptied the middle to build the rim: {midNone:F1} -> {midFull:F1}");
        // Measured 37 against 51 after the fix: the outer six rows include the
        // faint bleed beyond the ring, so "approach" is over half, not equal.
        Assert.True(rimFull > midFull * 0.6, $"at full pull the rim should approach the middle: rim {rimFull:F1}, middle {midFull:F1}");
    }

    /// <summary>
    /// Inside the rim there is a paler band before the wash — the dome's
    /// shoulder, which gives to the rim and receives nothing — and that is the
    /// wet edge's look rather than a defect, as long as it stays a band of
    /// lighter wash and never a gap. At the shipped pull the cross-section
    /// reads 62 55 48 45 45 49 54 60 66 69 72 from the top edge to the centre:
    /// rim 62, shoulder 45, centre 72. This bounds the shoulder against the
    /// centre so the halo cannot deepen into a second seam unnoticed.
    /// </summary>
    [Fact]
    public void TheHaloInsideTheRimStaysAWashAtTheShippedPull()
    {
        using var bmp = FrameRasterizer.Rasterize([Wash(0.45)], W, H);
        var worst = 1.0;
        var lines = new List<string>();
        foreach (var x in new[] { 120, 180, 240, 300 })
        {
            double Row(int y) => (bmp.GetPixel(x, y).Alpha + bmp.GetPixel(x, y - 1).Alpha + bmp.GetPixel(x, y + 1).Alpha) / 3.0;
            var centre = Row(70);
            // The shallowest point between the rim and the middle, either side.
            var shoulder = Math.Min(Enumerable.Range(49, 14).Min(Row), Enumerable.Range(78, 14).Min(Row));
            var ratio = shoulder / Math.Max(1, centre);
            worst = Math.Min(worst, ratio);
            lines.Add($"x={x}: shoulder {shoulder:F0} centre {centre:F0} ratio {ratio:F2}");
        }
        output.WriteLine(string.Join("; ", lines));
        // Measured 0.72 on three-row means (the single-row profile above
        // reads 45 against 72); a shoulder under half the centre reads as a gap.
        Assert.True(worst >= 0.5, $"the halo inside the rim has deepened into a gap: {string.Join("; ", lines)}");
    }

    /// <summary>
    /// A thin line has an edge to pool at too. The fringe floor was written in
    /// cells and compared against a chamfer in fifths of a cell, so a stroke
    /// under about ten cells wide had no fringe, and the pull did nothing at
    /// all on it: the render at EdgePull 1 was byte-identical to EdgePull 0.
    /// Found by the leak-hunter reading the units, not by any test.
    /// </summary>
    [Fact]
    public void AThinLineStillPoolsAtItsEdge()
    {
        using var none = FrameRasterizer.Rasterize([Wash(0, size: 7)], W, H);
        using var full = FrameRasterizer.Rasterize([Wash(1.0, size: 7)], W, H);
        var moved = 0;
        var wet = 0;
        for (var y = 60; y < 80; y++)
        for (var x = 100; x < 320; x++)
        {
            var a = none.GetPixel(x, y).Alpha;
            if (a > 2) wet++;
            if (a != full.GetPixel(x, y).Alpha) moved++;
        }
        output.WriteLine($"thin line: {wet} wet pixels, {moved} changed by the pull");
        Assert.True(wet > 200, $"the thin line did not render: {wet} wet pixels");
        Assert.True(moved > wet / 4, $"edge pull did nothing to a thin line: {moved} of {wet} wet pixels changed");

        var (_, report) = CentreAgainstInterior(full);
        output.WriteLine($"thin line at full pull: {report}");
    }
}
