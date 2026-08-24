using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// A stroke at an ordinary spacing has to read as a line, not as a row of
/// dabs — and a stroke at a spacing the artist deliberately widened still has
/// to read as dabs. See <c>BrushEngine.SubdividesForFidelity</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured across the mark, never along it.</b> Alpha saturates along a
/// stroke, so a reading down the centreline says the same thing whatever the
/// walk is doing (the <c>brush-measurement</c> skill). What is measured here is
/// the <em>peak-to-peak wobble</em> of alpha along each row of the mark: a
/// perfectly swept brush gives the same profile at every point of a straight
/// stroke, so any wobble at all is the walk showing through, and its size is
/// how visible the stepping is.
/// </para>
/// <para>
/// The numbers in these assertions are the ones the defect was reported
/// against, taken on a size-120 brush over hardness 0.2–0.9 at flow 1 and 0.3:
/// spacing 0.25 → 81/255; 0.20 → 51/255; 0.15 → 28/255; 0.10 → 13/255. After
/// the walk subdivides they are 1–5/255 across the same grid.
/// </para>
/// </remarks>
public class StrokeFidelityTests(ITestOutputHelper output)
{
    private static BrushSettings Soft(double hardness, double spacing, double flow) => new()
    {
        Size = 120, Hardness = hardness, Opacity = 1, Flow = flow, Spacing = spacing, AntiAlias = true,
    };

    private static Stroke Straight(BrushSettings brush) => new()
    {
        Tool = ToolKind.Brush, Color = "#000000", Brush = brush,
        Points = [new(80, 200, 1), new(720, 200, 1)],
    };

    /// <summary>
    /// The worst peak-to-peak alpha wobble along any row of the mark, in 0..255
    /// steps. Sampled well inside the stroke so neither cap is in the window.
    /// </summary>
    private static int Wobble(BrushSettings brush)
    {
        using var bmp = FrameRasterizer.Rasterize([Straight(brush)], 800, 400);
        var worst = 0;
        var reach = (int)(brush.Size / 2) + 2;
        for (var off = -reach; off <= reach; off++)
        {
            int lo = 255, hi = 0;
            for (var x = 300; x < 500; x++)
            {
                var a = bmp.GetPixel(x, 200 + off).Alpha;
                lo = Math.Min(lo, a);
                hi = Math.Max(hi, a);
            }
            worst = Math.Max(worst, hi - lo);
        }
        return worst;
    }

    [Fact]
    public void AStrokeAtAnOrdinarySpacingReadsAsALine()
    {
        var worst = 0;
        (double H, double S, double F) worstAt = default;
        foreach (var hardness in new[] { 0.2, 0.5, 0.65, 0.8, 0.9 })
        foreach (var spacing in new[] { 0.10, 0.15, 0.20, 0.25 })
        foreach (var flow in new[] { 1.0, 0.5, 0.3 })
        {
            var wobble = Wobble(Soft(hardness, spacing, flow));
            output.WriteLine(
                $"hardness {hardness:0.00} spacing {spacing:0.00} flow {flow:0.0} → {wobble,3}/255");
            if (wobble > worst) (worst, worstAt) = (wobble, (hardness, spacing, flow));
        }

        output.WriteLine(
            $"worst {worst}/255 at hardness {worstAt.H:0.00} spacing {worstAt.S:0.00} flow {worstAt.F:0.0}");

        // 81/255 before the walk subdivided, at hardness 0.9 / spacing 0.25 /
        // flow 1. 8 leaves room for the antialiaser's own quantisation without
        // leaving room for anything an eye can pick out as a repeating step.
        Assert.True(
            worst <= 8,
            $"the mark wobbles {worst}/255 along its length at hardness {worstAt.H:0.00}, "
            + $"spacing {worstAt.S:0.00}, flow {worstAt.F:0.0} — the dabs are showing through");
    }

    [Fact]
    public void AWidenedSpacingStillLaysSeparateDabs()
    {
        // The other half of the report: dabs are a thing an artist asks for by
        // widening the spacing, and subdividing them away would take a tool
        // rather than fix a defect. Above SmoothSpacing the walk is untouched.
        var brush = Soft(0.8, 0.5, 1.0);
        Assert.False(BrushEngine.SubdividesForFidelity(brush));

        var wobble = Wobble(brush);
        output.WriteLine($"spacing 0.50 → {wobble}/255 (dabs, deliberately)");
        Assert.True(
            wobble > 60,
            $"a deliberately widened spacing came out smooth ({wobble}/255) — the dotted "
            + "trail an artist asked for has been resolved away");
    }

    [Fact]
    public void ThinningGivesBackExactlyWhatOneDabLaid()
    {
        // The whole reason subdividing does not repaint every drawing darker:
        // k thinned dabs composited SrcOver come back to the one dab's alpha.
        foreach (var alpha in new[] { 0.05, 0.1, 0.3, 0.5, 0.85, 0.99 })
        foreach (var fidelity in new[] { 2, 3, 4, 5, 8 })
        {
            var thin = BrushEngine.Thinned(alpha, fidelity);
            var rebuilt = 1 - Math.Pow(1 - thin, fidelity);
            output.WriteLine($"alpha {alpha:0.00} ÷{fidelity} → {thin:0.0000} → {rebuilt:0.000000}");
            Assert.Equal(alpha, rebuilt, 10);
        }

        // Degenerate inputs pass straight through rather than going near Pow.
        Assert.Equal(0.4, BrushEngine.Thinned(0.4, 1));
        Assert.Equal(0.4, BrushEngine.Thinned(0.4, 0));
        Assert.Equal(1.0, BrushEngine.Thinned(1.0, 4));
        Assert.Equal(0.0, BrushEngine.Thinned(0.0, 4));
    }

    [Fact]
    public void ABrushWhoseTextureIsItsDabsIsLeftAlone()
    {
        // Every dynamic below seeds from the dab's position (invariant 2), so
        // laying three dabs where there was one is a denser spray, not the same
        // spray rendered better. These have to stay out of the subdivision.
        static BrushSettings With(Action<BrushSettings> set)
        {
            var b = new BrushSettings
            {
                Size = 120, Hardness = 0.8, Opacity = 1, Flow = 1, Spacing = 0.15, AntiAlias = true,
            };
            set(b);
            return b;
        }

        Assert.True(BrushEngine.SubdividesForFidelity(With(_ => { })));
        Assert.False(BrushEngine.SubdividesForFidelity(With(b => b.Scatter = 0.2)));
        Assert.False(BrushEngine.SubdividesForFidelity(With(b => b.SizeJitter = 0.2)));
        Assert.False(BrushEngine.SubdividesForFidelity(With(b => b.FlowJitter = 0.2)));
        Assert.False(BrushEngine.SubdividesForFidelity(With(b => b.RotationJitter = 0.2)));
        Assert.False(BrushEngine.SubdividesForFidelity(With(b => b.RoundnessJitter = 0.2)));
        Assert.False(BrushEngine.SubdividesForFidelity(With(b => b.HueJitter = 0.2)));
        Assert.False(BrushEngine.SubdividesForFidelity(With(b => b.SaturationJitter = 0.2)));
        Assert.False(BrushEngine.SubdividesForFidelity(With(b => b.BrightnessJitter = 0.2)));
        Assert.False(BrushEngine.SubdividesForFidelity(With(b => b.AntiAlias = false)));
        Assert.False(BrushEngine.SubdividesForFidelity(With(b => b.TipId = "tip-anything")));
        Assert.False(BrushEngine.SubdividesForFidelity(With(b => b.Medium.Kind = MediumKind.Watercolour)));

        // A brush that already draws as one silhouette has its coverage computed
        // once and exactly, so there is nothing left to resolve.
        Assert.False(BrushEngine.SubdividesForFidelity(With(b => b.Hardness = 1)));
    }

    [Fact]
    public void SubdividingDoesNotMoveTheDabsThatWereAlreadyThere()
    {
        // The nominal spacing grid still has to be walked: subdividing puts
        // extra dabs *between* the old ones rather than shifting the whole
        // walk, or every position-seeded dynamic downstream would re-roll.
        var brush = Soft(0.8, 0.15, 1);
        var stroke = Straight(brush);
        var dabs = BrushEngine.WalkDabs(stroke);
        var fidelity = dabs.Max(d => Math.Max(1, d.Fidelity));
        output.WriteLine($"fidelity ×{fidelity}, {dabs.Count} dabs");
        Assert.Equal(2, fidelity);

        // Every fidelity-th dab sits where the un-subdivided walk would have put
        // it: 18 px apart on a 120 px brush at spacing 0.15.
        for (var i = 0; i + fidelity < dabs.Count; i += fidelity)
        {
            Assert.Equal(18.0, dabs[i + fidelity].Pos.X - dabs[i].Pos.X, 3);
        }
    }
}
