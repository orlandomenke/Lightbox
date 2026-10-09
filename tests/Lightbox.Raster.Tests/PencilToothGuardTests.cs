using System.Security.Cryptography;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Two guards the pencil's tooth was reviewed into. The ungated texture pass
/// is pinned byte for byte, because nothing else in the suite covered a
/// textured stroke and a later edit to the gate could have moved every saved
/// textured stroke and stayed green (sensitivity-guardian). And the live pass
/// over a gated brush is bounded by the band, not the stroke, because the
/// press map is carried across the stroke rather than rebuilt per event
/// (leak-hunter).
/// </summary>
[Collection("Performance")]
public class PencilToothGuardTests(ITestOutputHelper output)
{
    private const int W = 960, H = 540;

    private static Stroke Textured(int points, double? gate) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#2050b0",
        Points = [.. Enumerable.Range(0, points).Select(i =>
            new StrokePoint(40 + i * (880.0 / points), 270 + Math.Sin(i * 0.05) * 120, 0.3 + 0.7 * (i % 7) / 6.0))],
        Brush = new BrushSettings
        {
            Size = 9, Hardness = 0.7, Opacity = 1, Flow = 0.9, Spacing = 0.1,
            TextureSurface = PaperKind.ColdPress, TextureScale = 5, TextureDepth = 0.8,
            TexturePressure = gate,
        },
    };

    /// <summary>
    /// Recorded on the engine before the gate existed (the branch with
    /// BrushEngine.cs stashed), so this is the render every saved textured
    /// stroke without a <c>texturePressure</c> key had and must keep.
    /// </summary>
    private const string UngatedFingerprint = "E9BD4D831D658EE2808A23A85975EBF137E3563123AC51F5F7FD52EA04BB3507";

    [Fact]
    public void AnUngatedTextureRendersExactlyAsItDidBeforeTheGateExisted()
    {
        using var bitmap = FrameRasterizer.Rasterize([Textured(60, gate: null)], W, H, 1.0);
        var hash = Convert.ToHexString(SHA256.HashData(bitmap.GetPixelSpan()));
        output.WriteLine($"ungated textured stroke: {hash}");
        Assert.Equal(UngatedFingerprint, hash);
    }

    /// <summary>
    /// The live pass over a fixed band, with the press map carried in: four
    /// times the dabs behind the band must not cost the band more. Minimum of
    /// repeats, ratio asserted, absolutes printed.
    /// </summary>
    [Fact]
    [Trait("Category", "Performance")]
    public void TheLivePassOverAGatedBrushIsBoundedByTheBandNotTheStroke()
    {
        double Pass(int points)
        {
            var stroke = Textured(points, gate: 0.8);
            var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
            var dabs = BrushEngine.WalkDabs(stroke);

            using var stamped = new SKBitmap(info);
            using (var canvas = new SKCanvas(stamped))
            {
                BrushEngine.StampDabRange(canvas, stroke, dabs, 0, dabs.Count);
                canvas.Flush();
            }

            // Carried by the caller: accumulated once, outside the timed region.
            using var press = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Opaque));
            using (var canvas = new SKCanvas(press))
            {
                canvas.Clear(SKColors.Black);
                BrushEngine.AccumulatePress(canvas, stroke, dabs, 0, dabs.Count);
                canvas.Flush();
            }

            // The footprint ceiling is carried by the app too (B293); without
            // it the live pass rebuilds the ceiling from the whole stroke, which
            // is the fallback this test is not about.
            using var footprint = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Opaque));
            using (var canvas = new SKCanvas(footprint))
            {
                canvas.Clear(SKColors.Black);
                BrushEngine.AccumulateFootprint(canvas, stroke, dabs, 0, dabs.Count);
                canvas.Flush();
            }

            // The band: the last stretch of the mark, the same size for both.
            var band = new SKRectI(700, 120, 940, 420);
            using var dabsCrop = Copy(stamped, band);
            using var pressCrop = Copy(press, band);
            using var footprintCrop = Copy(footprint, band);

            var best = double.MaxValue;
            for (var i = 0; i < 5; i++)
            {
                var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                using var processed = BrushEngine.PostProcessRegion(
                    dabsCrop, stroke, band, null, band.Location, default,
                    footprintCrop, Lightbox.Raster.FootprintSpace.Document,
                    pressCrop, Lightbox.Raster.FootprintSpace.Document);
                Assert.NotNull(processed);
                var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                best = Math.Min(best, ms);
            }
            return best;
        }

        var shortMs = Pass(600);
        var longMs = Pass(2400);
        output.WriteLine($"live pass over a 240x300 band: {shortMs:F2} ms behind 600 points, {longMs:F2} ms behind 2400");
        Assert.True(longMs < shortMs * 2.0 + 2.0,
            $"the live pass grew with the stroke: {shortMs:F2} ms at 600 points, {longMs:F2} ms at 2400");
    }

    private static SKBitmap Copy(SKBitmap src, SKRectI r)
    {
        var bmp = new SKBitmap(new SKImageInfo(r.Width, r.Height, src.ColorType, src.AlphaType));
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
}
