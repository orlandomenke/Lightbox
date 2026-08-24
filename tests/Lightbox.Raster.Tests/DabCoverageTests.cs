using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Overlapping antialiased dabs destroy antialiasing, and these are the numbers
/// that say so. See <c>BrushEngine.DrawsAsOneSilhouette</c> for the mechanism.
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured across the mark, never along it.</b> Alpha saturates along a
/// stroke — twenty dabs of flow 0.12 come out at 0.92 — so a reading taken down
/// the centreline says the same thing whatever the brush is doing. Every number
/// here is the <em>integral of alpha across</em> the mark at one row, which for
/// a stroke of a known width has a known exact answer and no accumulation in it.
/// </para>
/// </remarks>
public class DabCoverageTests(ITestOutputHelper output)
{
    /// <summary>The Ink preset, which is the brush the defect was reported against.</summary>
    private static BrushSettings Ink() => new()
    {
        Size = 5, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.1,
        PressureSizeGamma = 1.4, AntiAlias = true,
    };

    private static Stroke Vertical(double x, BrushSettings brush) => new()
    {
        Tool = ToolKind.Brush, Color = "#000000", Brush = brush,
        Points = [new(x, 8, 1), new(x, 56, 1)],
    };

    /// <summary>
    /// Ink across one row of the mark. A size-5 stroke covers exactly 5.000
    /// pixels' worth of alpha however the mark falls on the grid; anything else
    /// is the rasteriser inventing or losing ink.
    /// </summary>
    private static double MassAcross(SKBitmap bmp, int y)
    {
        double m = 0;
        for (var x = 0; x < bmp.Width; x++) m += bmp.GetPixel(x, y).Alpha / 255.0;
        return m;
    }

    [Fact]
    public void AMarkKeepsItsWidthWhereverItFallsOnThePixelGrid()
    {
        // Sixteen sub-pixel positions across one pixel. Before the silhouette
        // this swung 4.996..5.886 — the mark pulsing 17.7% wider and back with a
        // period of one pixel, which is what reads as a staggered diagonal.
        double worst = 0, worstAt = 0;
        for (var i = 0; i < 16; i++)
        {
            var x = 32.0 + i / 16.0;
            using var bmp = FrameRasterizer.Rasterize([Vertical(x, Ink())], 64, 64);
            var mass = MassAcross(bmp, 32);
            if (Math.Abs(mass - 5.0) > worst) { worst = Math.Abs(mass - 5.0); worstAt = x; }
            output.WriteLine($"x={x:0.0000}  ink across the mark = {mass:0.000}");
        }
        output.WriteLine($"worst error {worst / 5.0:P2} at x={worstAt:0.0000}");

        // 8% leaves room for Skia's own analytic-AA quantisation (measured 4.9%)
        // without leaving room for the 17.7% the per-dab path had.
        Assert.True(
            worst / 5.0 < 0.08,
            $"the mark's width varies {worst / 5.0:P2} with its sub-pixel position "
            + $"(worst {5 + worst:0.000} against an exact 5.000 at x={worstAt:0.0000})");
    }

    [Fact]
    public void TheEdgeOfAMarkKeepsPartialCoverage()
    {
        // A single dab is the honest reference: one antialiased circle, whose rim
        // Skia computes exactly. A stroke of the same brush must not come out
        // with a harder edge than one of its own dabs.
        using var stroke = FrameRasterizer.Rasterize([Vertical(32.0, Ink())], 64, 64);
        var dot = new Stroke
        {
            Tool = ToolKind.Brush, Color = "#000000", Brush = Ink(), Points = [new(32.0, 32, 1)],
        };
        using var dab = FrameRasterizer.Rasterize([dot], 64, 64);

        var strokeRim = stroke.GetPixel(29, 32).Alpha;
        var dabRim = dab.GetPixel(29, 32).Alpha;
        output.WriteLine($"left rim: stroke {strokeRim}, single dab {dabRim}");

        Assert.True(dabRim is > 0 and < 255, $"the reference dab has no rim to compare against ({dabRim})");
        // Before the silhouette this was 242 against the dab's 128: eleven dabs
        // of the same partial coverage composited to opaque.
        Assert.True(
            Math.Abs(strokeRim - dabRim) <= 24,
            $"the stroke's rim ({strokeRim}) has saturated away from its own dab's ({dabRim})");
    }

    [Fact]
    public void AShallowDiagonalHoldsTheInkItsGeometryImplies()
    {
        // The reported case: hair is a mass of thin shallow diagonals. A
        // saturated rim does not only harden the edge, it adds ink — the mark
        // comes out heavier than the geometry asks for, and heavier by an amount
        // that varies along its length. Total ink is the reading with no
        // accumulation in it at all.
        //
        // (8,20) to (56,34) is 50 px long; at size 5 the mark is a 5 x 50
        // rectangle with a half-disc at each end: 250 + pi * 2.5^2 = 269.6.
        const double expected = 250 + Math.PI * 2.5 * 2.5;
        var strand = new Stroke
        {
            Tool = ToolKind.Brush, Color = "#000000", Brush = Ink(),
            Points = [new(8, 20, 1), new(56, 34, 1)],
        };
        using var bmp = FrameRasterizer.Rasterize([strand], 64, 64);

        double ink = 0;
        foreach (var px in bmp.Pixels) ink += px.Alpha / 255.0;
        var error = (ink - expected) / expected;
        output.WriteLine($"ink {ink:0.0}, geometry says {expected:0.0}, error {error:P2}");

        // Before the silhouette this measured 298.2 — 10.6% more ink than the
        // stroke describes, laid down as a saturated rim on both sides.
        Assert.True(
            Math.Abs(error) < 0.05,
            $"the strand carries {error:P2} of the ink its geometry implies "
            + $"({ink:0.0} against {expected:0.0})");
    }

    [Fact]
    public void AMarkThatDoublesBackOnItselfSurvives()
    {
        // The silhouette is described by the dabs where the mark turns or
        // changes width, and a stroke drawn out and back along its own line
        // turns without ever leaving the chord — every dab's deviation is
        // exactly zero. Reversal has to be its own condition, and when it was
        // not, this whole mark collapsed into the two circles at its ends.
        var thereAndBack = new Stroke
        {
            Tool = ToolKind.Brush, Color = "#000000", Brush = Ink(),
            Points = [new(20, 32, 1), new(52, 32, 1), new(20, 32, 1)],
        };
        using var bmp = FrameRasterizer.Rasterize([thereAndBack], 64, 64);

        var middle = bmp.GetPixel(36, 32).Alpha;
        output.WriteLine($"alpha at the middle of the doubled-back mark: {middle}");
        Assert.Equal(255, middle);

        // And it is one line, not two: doubling back over an opaque mark cannot
        // make it wider than the brush. Measured down a COLUMN, because this
        // mark is horizontal — a row here would run along its length and report
        // 37, which is the trap this file opens by warning about.
        double across = 0;
        for (var y = 0; y < bmp.Height; y++) across += bmp.GetPixel(36, y).Alpha / 255.0;
        output.WriteLine($"ink across it: {across:0.000}");
        Assert.True(Math.Abs(across - 5.0) / 5.0 < 0.08, $"the doubled mark is {across:0.000} wide, not 5.000");
    }

    [Fact]
    public void ACurveIsNotFlattenedIntoItsChord()
    {
        // The other way decimation can go wrong: drop too much and an arc
        // becomes a straight line. Sampled at the apex, where a chord would
        // miss by the most.
        var points = new List<StrokePoint>();
        for (var i = 0; i <= 40; i++)
        {
            var t = i / 40.0;
            points.Add(new StrokePoint(8 + t * 48, 40 - Math.Sin(t * Math.PI) * 20, 1));
        }
        var arc = new Stroke { Tool = ToolKind.Brush, Color = "#000000", Brush = Ink(), Points = points };
        using var bmp = FrameRasterizer.Rasterize([arc], 64, 64);

        // The apex sits at x=32, y=20. A flattened arc would leave it blank and
        // put ink on the chord at y=40 instead.
        var apex = bmp.GetPixel(32, 20).Alpha;
        var chord = bmp.GetPixel(32, 40).Alpha;
        output.WriteLine($"apex alpha {apex}, chord alpha {chord}");
        Assert.True(apex > 250, $"the arc's apex is missing (alpha {apex})");
        Assert.Equal(0, chord);
    }

    [Theory]
    // The hard round family, which is what the silhouette is for.
    [InlineData(1.0, 0.0, 0.0, 1.0, true)]
    // Soft brushes: each dab is a radial gradient a union cannot represent.
    [InlineData(0.35, 0.0, 0.0, 1.0, false)]
    // Geometry the (position, radius) silhouette does not carry.
    [InlineData(1.0, 0.4, 0.0, 1.0, false)]   // scatter
    [InlineData(1.0, 0.0, 0.3, 1.0, false)]   // size jitter
    [InlineData(1.0, 0.0, 0.0, 0.4, false)]   // squashed to a chisel
    public void OnlyBrushesWhoseDabsAreInterchangeableTakeTheSilhouette(
        double hardness, double scatter, double sizeJitter, double roundness, bool expected)
    {
        var brush = new BrushSettings
        {
            Size = 5, Hardness = hardness, Flow = 1, Opacity = 1, Spacing = 0.1,
            Scatter = scatter, SizeJitter = sizeJitter, Roundness = roundness, AntiAlias = true,
        };
        Assert.Equal(expected, BrushEngine.DrawsAsOneSilhouette(brush));
    }

    [Fact]
    public void AnAliasedBrushIsLeftExactlyAsItWas()
    {
        // Deliberately aliased art must not become smooth behind the artist's
        // back — that is a different request from fixing saturated coverage.
        var aliased = Ink();
        aliased.AntiAlias = false;
        Assert.False(BrushEngine.DrawsAsOneSilhouette(aliased));

        using var bmp = FrameRasterizer.Rasterize([Vertical(32.3, aliased)], 64, 64);
        var partial = 0;
        foreach (var px in bmp.Pixels) if (px.Alpha is > 0 and < 255) partial++;
        Assert.Equal(0, partial);
    }
}
