using System.Diagnostics;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The live preview recomputes stroke-global effects — medium, wet edge,
/// texture, granulation — over the whole stroke every pass, because a rim
/// derived from half a silhouette is not the rim. Doing that through
/// <see cref="BrushEngine.StampStroke"/> re-stamped every dab each time, so
/// the pass got slower the longer the stroke got: exactly backwards, since a
/// long stroke is when a lagging preview is most annoying.
///
/// <see cref="BrushEngine.PostProcessDabs"/> reuses the dabs the preview
/// already has. What these hold is that the result is unchanged and the cost
/// stops scaling.
/// </summary>
public class PostProcessDabsTests(ITestOutputHelper output)
{
    // Big enough that the difference clears the container's noise floor: at a
    // small canvas with a small brush the dab loop is a few milliseconds and
    // the measurement says nothing either way.
    private const int W = 2400, H = 1200;

    private static Stroke Stroke(int segments, double size, MediumKind medium = MediumKind.None,
        double wetEdge = 0, double granulation = 0, PaperKind? texture = null)
    {
        var pts = new List<StrokePoint>();
        for (var i = 0; i <= segments; i++)
        {
            pts.Add(new StrokePoint(120 + i * 36, 600 + Math.Sin(i * 0.4) * 160, 0.9));
        }
        return new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#3060c0",
            Points = pts,
            Brush = new BrushSettings
            {
                Size = size, Hardness = 0.7, Opacity = 1, Flow = 0.9, Spacing = 0.12,
                Medium = new MediumSettings { Kind = medium },
                WetEdge = wetEdge,
                Granulation = granulation,
                TextureSurface = texture,
                TextureDepth = texture is null ? 0 : 0.6,
            },
        };
    }

    private static SKImageInfo Info => new(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);

    /// <summary>The dabs alone, as the live scratch accumulates them.</summary>
    private static SKBitmap Dabs(Stroke stroke)
    {
        var bmp = new SKBitmap(Info);
        using var canvas = new SKCanvas(bmp);
        BrushEngine.StampDraftDabs(canvas, stroke);
        canvas.Flush();
        return bmp;
    }

    /// <summary>The committed render, minus the masks the compositor applies.</summary>
    private static SKBitmap ViaStampStroke(Stroke stroke, SKBitmap? beneath)
    {
        var bmp = new SKBitmap(Info);
        using var canvas = new SKCanvas(bmp);
        BrushEngine.StampStroke(canvas, stroke, Info, beneath);
        canvas.Flush();
        return bmp;
    }

    private static double MeanDifference(SKBitmap a, SKBitmap b)
    {
        using var pa = a.PeekPixels();
        using var pb = b.PeekPixels();
        var sa = pa.GetPixelSpan();
        var sb = pb.GetPixelSpan();
        long total = 0;
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                var ia = y * pa.RowBytes + x * 4;
                var ib = y * pb.RowBytes + x * 4;
                for (var c = 0; c < 4; c++) total += Math.Abs(sa[ia + c] - sb[ib + c]);
            }
        }
        return total / (double)(a.Width * a.Height * 4);
    }

    [Theory]
    [InlineData(MediumKind.Watercolour, 0, 0, null)]
    [InlineData(MediumKind.Oil, 0, 0, null)]
    [InlineData(MediumKind.None, 0.8, 0, null)]
    [InlineData(MediumKind.None, 0, 0.6, null)]
    [InlineData(MediumKind.None, 0, 0, PaperKind.ColdPress)]
    public void PostProcessingPreStampedDabs_MatchesRenderingFromScratch(
        MediumKind medium, double wetEdge, double granulation, PaperKind? texture)
    {
        // The whole point: the preview must be able to take this shortcut
        // without the picture changing.
        var stroke = Stroke(12, 60, medium, wetEdge, granulation, texture);
        using var beneath = new SKBitmap(Info);

        using var dabs = Dabs(stroke);
        using var expected = ViaStampStroke(stroke, beneath);
        using var actual = new SKBitmap(Info);
        var bounds = BrushEngine.PostProcessDabs(dabs, actual, stroke, Info, beneath);

        Assert.NotNull(bounds);
        var diff = MeanDifference(expected, actual);
        Assert.True(diff < 0.5,
            $"medium={medium} wet={wetEdge} gran={granulation} tex={texture}: " +
            $"differs from a full render by {diff:0.00}/255");
    }

    [Fact]
    public void AStrokeThatReachesNothingReportsNoBounds()
    {
        var stroke = Stroke(4, 40);
        stroke.Points = [new StrokePoint(-5000, -5000, 1), new StrokePoint(-4900, -4900, 1)];
        using var dabs = new SKBitmap(Info);
        using var dest = new SKBitmap(Info);
        Assert.Null(BrushEngine.PostProcessDabs(dabs, dest, stroke, Info, null));
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void TheCostOfAPassDoesNotGrowWithTheLengthOfTheStroke()
    {
        double Median(int segments, bool viaStampStroke)
        {
            var stroke = Stroke(segments, 90, MediumKind.Watercolour);
            using var beneath = new SKBitmap(Info);
            using var dabs = Dabs(stroke);
            using var dest = new SKBitmap(Info);

            void Once()
            {
                if (viaStampStroke)
                {
                    using var canvas = new SKCanvas(dest);
                    BrushEngine.StampStroke(canvas, stroke, Info, beneath);
                    canvas.Flush();
                }
                else
                {
                    BrushEngine.PostProcessDabs(dabs, dest, stroke, Info, beneath);
                }
            }

            Once();
            var times = new List<double>();
            var sw = new Stopwatch();
            for (var i = 0; i < 5; i++) { sw.Restart(); Once(); sw.Stop(); times.Add(sw.Elapsed.TotalMilliseconds); }
            times.Sort();
            return times[times.Count / 2];
        }

        // Warm everything before timing anything. The first medium render in a
        // process pays for PaperField's tables and the JIT of the whole
        // simulation path — several hundred milliseconds — and whichever
        // measurement went first would otherwise absorb all of it and report
        // the opposite of the truth.
        foreach (var segments in new[] { 6, 60 })
        {
            Median(segments, viaStampStroke: false);
            Median(segments, viaStampStroke: true);
        }

        var shortPost = Median(6, viaStampStroke: false);
        var longPost = Median(60, viaStampStroke: false);
        var shortStamp = Median(6, viaStampStroke: true);
        var longStamp = Median(60, viaStampStroke: true);

        output.WriteLine($"PostProcessDabs  6 seg {shortPost,7:0.0} ms | 60 seg {longPost,7:0.0} ms");
        output.WriteLine($"StampStroke      6 seg {shortStamp,7:0.0} ms | 60 seg {longStamp,7:0.0} ms");

        // Ten times the stroke for well under twice the cost. Re-stamping every
        // dab put this near-linear, which is what made a long wet stroke feel
        // like the preview had given up.
        Assert.True(longPost < shortPost * 2,
            $"a 10x longer stroke cost {longPost / Math.Max(0.01, shortPost):0.0}x as much " +
            $"({shortPost:0.0} -> {longPost:0.0} ms)");
    }
}
