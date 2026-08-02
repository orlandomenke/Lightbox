using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Rendering the same drawing bigger must produce the same drawing, sharper —
/// never a different one. This is what lets a camera push past 100% by
/// re-rasterising the stroke record instead of magnifying pixels.
///
/// The trap it guards is specific and easy to fall into. Every dab dynamic is
/// seeded from the dab's position through <c>Hash01</c>, which hashes the
/// IEEE-754 bit pattern of the coordinate. Doubling a coordinate changes its
/// exponent, and FNV-1a avalanches that into an unrelated value — so a render
/// that scaled stroke geometry would re-roll scatter, size, flow, roundness,
/// rotation and all three colour jitters. The mark would be statistically
/// similar and visibly different, and in a sequence it would boil.
///
/// The engine therefore scales the SURFACE and leaves the geometry alone.
/// <see cref="ScalingTheCoordinatesInstead_ProducesADifferentMark"/> renders
/// the wrong way round on purpose, so the reason for that choice is written
/// down in a test rather than only in a comment.
/// </summary>
public class OutputScaleTests
{
    private const int W = 200, H = 140;

    /// <summary>Everything stochastic turned on at once — the hardest case to keep stable.</summary>
    private static BrushSettings Jittery(double size = 22) => new()
    {
        Size = size,
        Hardness = 0.8,
        Opacity = 1,
        Flow = 0.9,
        Spacing = 0.2,
        AntiAlias = true,
        Scatter = 0.35,
        SizeJitter = 0.5,
        MinimumDiameter = 0.3,
        FlowJitter = 0.4,
        RoundnessJitter = 0.3,
        RotationJitter = 0.5,
        ColorJitter = 0.4,
        SecondaryColor = "#c04020",
        HueJitter = 0.3,
        SaturationJitter = 0.3,
        BrightnessJitter = 0.3,
    };

    private static List<StrokePoint> Path(double scale = 1.0)
    {
        var pts = new List<StrokePoint>();
        for (var i = 0; i < 16; i++)
        {
            pts.Add(new StrokePoint((25 + i * 10) * scale, (70 + Math.Sin(i * 0.5) * 35) * scale, 0.9));
        }
        return pts;
    }

    private static SKBitmap Render(BrushSettings brush, double outputScale, double geometryScale = 1.0)
    {
        var w = (int)Math.Round(W * geometryScale);
        var h = (int)Math.Round(H * geometryScale);
        var stroke = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#2050b0",
            Points = Path(geometryScale),
            Brush = brush,
        };
        return FrameRasterizer.Rasterize([stroke], w, h, outputScale);
    }

    /// <summary>Resample to WxH so two renders at different scales are comparable.</summary>
    private static SKBitmap ToReference(SKBitmap source)
    {
        if (source.Width == W && source.Height == H) return source;
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        var small = new SKBitmap(info);
        using (var image = SKImage.FromBitmap(source))
        {
            image.ScalePixels(small.PeekPixels(), new SKSamplingOptions(SKFilterMode.Linear));
        }
        source.Dispose();
        return small;
    }

    /// <summary>Mean absolute per-channel difference, 0–255. Zero means identical.</summary>
    private static double MeanDifference(SKBitmap a, SKBitmap b)
    {
        Assert.Equal(a.Width, b.Width);
        Assert.Equal(a.Height, b.Height);
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

    /// <summary>Where the ink is, coarsely — the jitter PATTERN rather than its edges.</summary>
    private static bool[] InkGrid(SKBitmap b, int cells = 20)
    {
        var grid = new bool[cells * cells];
        using var pixels = b.PeekPixels();
        var span = pixels.GetPixelSpan();
        for (var y = 0; y < b.Height; y++)
        {
            for (var x = 0; x < b.Width; x++)
            {
                if (span[y * pixels.RowBytes + x * 4 + 3] < 40) continue;
                var cx = Math.Min(cells - 1, x * cells / b.Width);
                var cy = Math.Min(cells - 1, y * cells / b.Height);
                grid[cy * cells + cx] = true;
            }
        }
        return grid;
    }

    private static int GridDisagreement(bool[] a, bool[] b)
    {
        var n = 0;
        for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) n++;
        return n;
    }

    [Theory]
    [InlineData(2.0)]
    [InlineData(3.0)]
    [InlineData(1.5)]
    public void AHigherOutputScale_RendersTheSameMark(double scale)
    {
        using var once = Render(Jittery(), 1.0);
        using var bigger = ToReference(Render(Jittery(), scale));

        var diff = MeanDifference(once, bigger);
        var pattern = GridDisagreement(InkGrid(once), InkGrid(bigger));

        // Resampling a sharper render down can never match a direct render
        // exactly — antialiasing differs at every edge — so the tolerance is on
        // the average, and the pattern check is what actually pins the jitter:
        // if a single Hash01 were re-rolled, dabs would move by up to
        // Scatter x Size (about 8 px here) and cells would light up elsewhere.
        Assert.True(diff < 14, $"{scale}x render differs by {diff:0.0}/255 on average");
        Assert.True(pattern <= 6, $"{scale}x render put ink in {pattern} different cells");
    }

    [Fact]
    public void ScalingTheCoordinatesInstead_ProducesADifferentMark()
    {
        using var once = Render(Jittery(), 1.0);
        // The tempting implementation: multiply the geometry by two and render
        // into a bigger canvas at 1x. Same picture in principle.
        using var scaledGeometry = ToReference(Render(Jittery(44), 1.0, geometryScale: 2.0));

        var pattern = GridDisagreement(InkGrid(once), InkGrid(scaledGeometry));
        var diff = MeanDifference(once, scaledGeometry);

        // It is not the same picture, and this is the whole reason the engine
        // scales surfaces rather than coordinates. If this ever starts
        // matching, Hash01 has stopped depending on the coordinate bits and
        // the surface-transform approach can be revisited.
        Assert.True(pattern > 6 || diff > 14,
            $"scaled coordinates matched too closely ({pattern} cells, {diff:0.0}/255) — " +
            "if Hash01 is now scale-invariant this test has outlived its purpose");
    }

    [Fact]
    public void OutputScaleOne_IsUntouched()
    {
        // The path every existing document takes. A refactor that made 1x go
        // through the scaling code differently would be a silent change to
        // every drawing already made.
        using var a = Render(Jittery(), 1.0);
        using var b = Render(Jittery(), 1.0);
        Assert.Equal(0.0, MeanDifference(a, b));
        Assert.Equal(W, a.Width);
        Assert.Equal(H, a.Height);
    }

    [Fact]
    public void AHigherOutputScale_ActuallyResolvesMoreDetail()
    {
        // Sharper, not merely bigger: a magnified 1x render would have the
        // same number of distinct edge values as the original, while a true
        // re-render resolves intermediate coverage the small one cannot hold.
        var brush = new BrushSettings
        {
            Size = 9, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.15, AntiAlias = true,
        };
        using var once = Render(brush, 1.0);
        using var quad = Render(brush, 4.0);

        Assert.Equal(W * 4, quad.Width);

        // Sharpness as the share of the mark that is a partially covered edge
        // pixel. Ink area grows with the square of the scale and the outline
        // only linearly, so a genuine re-render at 4x spends a far smaller
        // fraction of itself on soft edges. Magnifying a 1x render instead
        // would keep that fraction roughly constant.
        var (edgeOnce, softOnce) = EdgeFraction(once);
        var (edgeQuad, softQuad) = EdgeFraction(quad);
        Assert.True(softQuad < softOnce / 2,
            $"4x render is {softQuad:P0} edge pixels against {softOnce:P0} at 1x " +
            $"({edgeQuad} vs {edgeOnce} partial pixels)");
    }

    /// <summary>
    /// A selection is a path in document coordinates and the scratch it clips
    /// is in device pixels. Getting that mapping wrong shifts or rescales the
    /// clip — invisible at 1x, where the two spaces coincide, which is exactly
    /// why this is asserted at 2x.
    /// </summary>
    [Fact]
    public void AClippedStroke_ClipsToTheSameRegionAtEveryScale()
    {
        var id = "clip_outputscale_t";
        ClipRegionRegistry.Register(id, new ClipRegion
        {
            Contours =
            [[
                new StrokePoint(60, 40, 1), new StrokePoint(140, 40, 1),
                new StrokePoint(140, 100, 1), new StrokePoint(60, 100, 1),
            ]],
            Feather = 0,
        });

        var stroke = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#2050b0",
            ClipId = id,
            // Runs the full width, so the clip is the only thing bounding it.
            Points = [new StrokePoint(10, 70, 1), new StrokePoint(190, 70, 1)],
            Brush = new BrushSettings
            {
                Size = 20, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.1, AntiAlias = false,
            },
        };

        using var once = FrameRasterizer.Rasterize([stroke], W, H, 1.0);
        using var twice = FrameRasterizer.Rasterize([stroke], W, H, 2.0);

        // Well inside the selection: painted at both scales.
        Assert.True(once.GetPixel(100, 70).Alpha > 200);
        Assert.True(twice.GetPixel(200, 140).Alpha > 200, "2x render lost the paint inside the clip");
        // Outside it, on the same stroke line: empty at both scales.
        Assert.Equal((byte)0, once.GetPixel(30, 70).Alpha);
        Assert.Equal((byte)0, twice.GetPixel(60, 140).Alpha);
        Assert.Equal((byte)0, twice.GetPixel(340, 140).Alpha);
    }

    /// <summary>
    /// Alpha lock masks against the layer bitmap, which is itself at output
    /// resolution — so the offset into it is a device-pixel offset. At 1x that
    /// is indistinguishable from a document offset.
    /// </summary>
    [Fact]
    public void AnAlphaLockedStroke_StaysInsideExistingPaintAtEveryScale()
    {
        SKBitmap Paint(double scale)
        {
            var blob = new Stroke
            {
                Tool = ToolKind.Fill,
                Color = "#1040a0",
                Points =
                [
                    new StrokePoint(70, 50, 1), new StrokePoint(130, 50, 1),
                    new StrokePoint(130, 90, 1), new StrokePoint(70, 90, 1),
                ],
                Brush = new BrushSettings { Opacity = 1, AntiAlias = false },
            };
            var bar = new Stroke
            {
                Tool = ToolKind.Brush,
                Color = "#ff0000",
                AlphaLocked = true,
                Points = [new StrokePoint(10, 70, 1), new StrokePoint(190, 70, 1)],
                Brush = new BrushSettings
                {
                    Size = 16, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.1, AntiAlias = false,
                },
            };
            return FrameRasterizer.Rasterize([blob, bar], W, H, scale);
        }

        using var once = Paint(1.0);
        using var twice = Paint(2.0);

        Assert.Equal(SKColors.Red, once.GetPixel(100, 70));
        Assert.Equal(SKColors.Red, twice.GetPixel(200, 140));
        Assert.Equal((byte)0, once.GetPixel(30, 70).Alpha);
        Assert.Equal((byte)0, twice.GetPixel(60, 140).Alpha);
    }

    /// <summary>Partially covered pixels, and their share of all inked pixels.</summary>
    private static (int Partial, double Share) EdgeFraction(SKBitmap b)
    {
        int partial = 0, ink = 0;
        using var pixels = b.PeekPixels();
        var span = pixels.GetPixelSpan();
        for (var y = 0; y < b.Height; y++)
        {
            for (var x = 0; x < b.Width; x++)
            {
                var a = span[y * pixels.RowBytes + x * 4 + 3];
                if (a == 0) continue;
                ink++;
                if (a < 245) partial++;
            }
        }
        return (partial, ink == 0 ? 0 : partial / (double)ink);
    }
}
