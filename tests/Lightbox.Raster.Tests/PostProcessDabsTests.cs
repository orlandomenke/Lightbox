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
[Collection("Performance")]
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
        var dabs = BrushEngine.WalkDabs(stroke);
        BrushEngine.StampDabRange(canvas, stroke, dabs, 0, dabs.Count);
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

    /// <summary>A real copy — <c>ExtractSubset</c> alone shares the pixels.</summary>
    private static SKBitmap Copy(SKBitmap src, SKRectI r)
    {
        var bmp = new SKBitmap(new SKImageInfo(r.Width, r.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        using var sub = new SKBitmap();
        Assert.True(src.ExtractSubset(sub, r));
        using var px = sub.PeekPixels();
        using var view = SKImage.FromPixels(px);
        using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
        canvas.DrawImage(view, 0, 0, paint);
        canvas.Flush();
        return bmp;
    }

    /// <summary>
    /// The region pass over cropped copies is bit-identical to the full pass —
    /// not merely close, because live-versus-commit equality is the bar the
    /// effect-brush work settled on (B69/B89), and the async live preview
    /// (B189) hands the worker exactly these crops.
    /// </summary>
    /// <remarks>
    /// Rewetting and physical mixing are both on so the beneath bitmap is
    /// actually sampled — a transparent or unread beneath would pass this test
    /// with the origin arithmetic wrong, which is the one defect it exists to
    /// catch. The beneath crop covers
    /// <see cref="Media.MediumSimulator.ExistingRegionNeeded"/>, the contract a
    /// cropped caller signs.
    /// </remarks>
    [Theory]
    [InlineData(MediumKind.Watercolour)]
    [InlineData(MediumKind.Oil)]
    [InlineData(MediumKind.None)] // wet edge + texture route, no beneath sampling
    public void ACroppedRegionPassIsBitIdenticalToTheFullOne(MediumKind medium)
    {
        var stroke = Stroke(18, 70, medium,
            wetEdge: medium == MediumKind.None ? 0.8 : 0,
            texture: medium == MediumKind.None ? PaperKind.ColdPress : null);
        stroke.Brush.Medium.Rewetting = 0.6;
        stroke.Brush.Medium.PhysicalMixing = true;

        // Something real underneath, so re-wetting has pigment to lift.
        using var beneath = new SKBitmap(Info);
        using (var canvas = new SKCanvas(beneath))
        {
            using var paint = new SKPaint { Color = new SKColor(180, 60, 40, 200) };
            canvas.DrawRect(new SKRect(0, 400, W, 900), paint);
            canvas.Flush();
        }

        using var dabs = Dabs(stroke);

        using var full = new SKBitmap(Info);
        var rect = BrushEngine.PostProcessDabs(dabs, full, stroke, Info, beneath);
        Assert.NotNull(rect);

        var needed = Media.MediumSimulator.ExistingRegionNeeded(rect!.Value, W, H);
        using var dabsCrop = Copy(dabs, rect.Value);
        using var beneathCrop = Copy(beneath, needed);
        using var region = BrushEngine.PostProcessRegion(
            dabsCrop, stroke, rect.Value, beneathCrop,
            origin: rect.Value.Location, beneathOrigin: needed.Location);
        Assert.NotNull(region);

        using var viaCrops = new SKBitmap(Info);
        using (var canvas = new SKCanvas(viaCrops))
        {
            using var replace = new SKPaint { BlendMode = SKBlendMode.Src };
            canvas.DrawImage(region, rect.Value.Left, rect.Value.Top, replace);
            canvas.Flush();
        }

        var diff = MeanDifference(full, viaCrops);
        output.WriteLine($"medium={medium}: mean difference {diff:0.####}/255");
        Assert.Equal(0, diff);
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

            return Bench.FastestMs(5, Once);
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

        // THE SHORT AND LONG MEASUREMENTS ARE TAKEN TOGETHER, AND THE BEST RATIO
        // OF SEVERAL PAIRS IS THE ANSWER (B212).
        //
        // This asserts on a *ratio*, and a ratio is only meaningful when both
        // halves saw the same machine. Taking all the short runs and then all
        // the long ones does not guarantee that: under `dotnet test
        // Lightbox.sln` this assembly runs while MSBuild is still compiling
        // Lightbox.App, so a compile that starts or ends between the two halves
        // inflates one and not the other. That is what failed on 2026-08-15,
        // and the log proves it was the machine rather than the pass — it
        // reported PostProcessDabs at 48.8 ms against StampStroke at 35.9 ms,
        // which is impossible, since StampStroke *contains* the post-process.
        // The same commit was green three hours earlier.
        //
        // `Bench.FastestMs` cannot save this on its own: it takes the fastest of
        // five runs, and the contention here was a twenty-second compile — every
        // run inside the window is slow, so there is no fast one to find.
        //
        // Measuring a pair back to back makes contention *cancel*: both halves
        // are scaled by roughly the same factor and the ratio survives. Taking
        // the smallest ratio over several pairs then only needs one pair to land
        // in a quiet window, which is the property fastest-of-N was reaching for
        // and could not have per-measurement.
        var best = double.MaxValue;
        double shortPost = 0, longPost = 0;
        for (var pair = 0; pair < 3; pair++)
        {
            var s = Median(6, viaStampStroke: false);
            var l = Median(60, viaStampStroke: false);
            output.WriteLine($"PostProcessDabs  6 seg {s,7:0.0} ms | 60 seg {l,7:0.0} ms  ({l / Math.Max(0.01, s):0.00}x)");
            if (l / Math.Max(0.01, s) < best)
            {
                (best, shortPost, longPost) = (l / Math.Max(0.01, s), s, l);
            }
        }

        // The control, logged and not asserted on. It is what tells a later
        // reader whether a failure was the pass or the box: StampStroke does
        // strictly more work than PostProcessDabs, so measuring it as cheaper is
        // a machine artefact and nothing else.
        var shortStamp = Median(6, viaStampStroke: true);
        var longStamp = Median(60, viaStampStroke: true);
        output.WriteLine($"StampStroke      6 seg {shortStamp,7:0.0} ms | 60 seg {longStamp,7:0.0} ms");
        if (shortStamp < shortPost)
        {
            output.WriteLine($"NOTE: StampStroke measured cheaper than the pass it contains "
                             + $"({shortStamp:0.0} < {shortPost:0.0} ms) — this box was contended, "
                             + "so read the ratio above rather than the milliseconds");
        }

        // Ten times the stroke for well under twice the cost. Re-stamping every
        // dab put this near-linear, which is what made a long wet stroke feel
        // like the preview had given up. Measured at 1.44x on an idle box.
        Assert.True(best < 2,
            $"a 10x longer stroke cost {best:0.0}x as much " +
            $"({shortPost:0.0} -> {longPost:0.0} ms), best of 3 paired measurements");
    }
}
