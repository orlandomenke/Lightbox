using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.Raster.Tests;

/// <summary>
/// How a custom brush tip is resampled when it is not stamped at its own size.
/// </summary>
/// <remarks>
/// <para>
/// Imported tips are big — a Photoshop <c>.abr</c> tip is routinely 512 px —
/// and they are stamped at whatever the brush size says, usually far smaller.
/// <c>DrawBitmap</c> with no sampling options is nearest-neighbour, and
/// <c>IsAntialias</c> does not change that: it smooths the edge of the shape
/// being drawn, not the resampling of what is inside it.
/// </para>
/// <para>
/// Two separate faults, measured rather than assumed. Nearest sampling is
/// blocky enlarged and gritty reduced, which a linear filter fixes. Heavy
/// minification of a <em>sparse</em> tip is the second one, and it needs
/// mipmaps: four texels are not a fair sample of the twenty-pixel region they
/// stand for, so a spatter tip came out 2.3x too dark and the brush got heavier
/// as it got smaller.
/// </para>
/// <para>
/// Worth writing down what this is <em>not</em>. Boiling was the first guess —
/// nearest sampling picking a different subset per dab — and the measurement
/// says no: both samplers were stable across sub-pixel offsets. The fault is
/// density, not flicker.
/// </para>
/// </remarks>
[Collection("Registries")]
public class BrushTipSamplingTests
{
    private const int W = 120, H = 80;

    /// <summary>
    /// A one-pixel checkerboard in the alpha channel.
    /// </summary>
    /// <remarks>
    /// Chosen because it makes the two samplers disagree as loudly as possible:
    /// averaged down it is a flat half-alpha, and point-sampled down it is
    /// still all-or-nothing. A solid tip cannot tell them apart at all, which
    /// is why the existing custom-tip test does not catch this.
    /// </remarks>
    private static string CheckerTip(int size)
    {
        using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var on = (x + y) % 2 == 0;
                bitmap.SetPixel(x, y, on ? new SKColor(255, 255, 255, 255) : SKColors.Transparent);
            }
        }
        return PngCodec.Encode(bitmap);
    }

    private static string Register(string id, int size)
    {
        BrushTipRegistry.Register(new Dictionary<string, string> { [id] = CheckerTip(size) });
        return id;
    }

    private static Stroke Dot(string tipId, double size, double x = 60, double y = 40) => new()
    {
        Color = "#000000",
        Brush = new BrushSettings
        {
            Size = size, Flow = 1, Opacity = 1, Spacing = 1, TipId = tipId, AntiAlias = true,
        },
        Points = [new StrokePoint(x, y, 1)],
    };

    private static List<byte> AlphasIn(SKBitmap bmp, SKRectI area)
    {
        var alphas = new List<byte>();
        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                alphas.Add(bmp.GetPixel(x, y).Alpha);
            }
        }
        return alphas;
    }

    [Fact]
    public void AMinifiedTipIsAveraged_NotPointSampled()
    {
        // 64px of one-pixel checker stamped at 16px. Averaged, the middle of
        // the dab is a flat mid alpha. Point-sampled, every pixel is a texel
        // that happened to be hit, so it is all 0 or all 255 and nothing
        // between.
        var tip = Register("tip_checker_min", 64);

        using var bmp = FrameRasterizer.Rasterize([Dot(tip, 16)], W, H);

        var middle = AlphasIn(bmp, new SKRectI(56, 36, 65, 45));
        var between = middle.Count(a => a is > 40 and < 215);
        Assert.True(
            between > middle.Count / 2,
            $"a minified checker tip should average to mid alphas; {between} of {middle.Count} were between");
    }

    [Fact]
    public void PointSamplingIsWhatThatRulesOut()
    {
        // The counter-check, so the test above cannot pass by the dab being
        // empty or uniformly solid: the same tip at its own size keeps the
        // checker, extremes and all.
        var tip = Register("tip_checker_same", 32);

        using var bmp = FrameRasterizer.Rasterize([Dot(tip, 32)], W, H);

        var middle = AlphasIn(bmp, new SKRectI(56, 36, 65, 45));
        Assert.Contains(middle, a => a > 215);
        Assert.Contains(middle, a => a < 40);
    }

    /// <summary>
    /// A tip that is mostly nothing: 2x2 dots on a 16px grid, so 1/64 of it is
    /// ink.
    /// </summary>
    /// <remarks>
    /// This is what a spatter, stipple or dry-brush scan looks like to a
    /// sampler, and it is the case a four-texel filter cannot handle: at heavy
    /// minification those four texels are nowhere near a fair sample of the
    /// region they stand for.
    /// </remarks>
    private static string SparseTip(int size)
    {
        using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var ink = x % 16 < 2 && y % 16 < 2;
                bitmap.SetPixel(x, y, ink ? new SKColor(255, 255, 255, 255) : SKColors.Transparent);
            }
        }
        return PngCodec.Encode(bitmap);
    }

    [Fact]
    public void AHeavilyMinifiedTipKeepsTheInkDensityItActuallyHas()
    {
        // The one that needs mipmaps rather than just a linear filter, and the
        // reason it matters: without them a sparse tip gets *heavier* as the
        // brush gets smaller, because a four-texel window that clips one dot
        // reports a quarter of full alpha for the whole region it stands for.
        // Measured at 2.3x too dark before the fix.
        //
        // The expected figure is not a magic number — it is the tip's own ink
        // fraction spread over the area the dab covers.
        BrushTipRegistry.Register(new Dictionary<string, string> { ["tip_sparse"] = SparseTip(256) });

        using var bmp = FrameRasterizer.Rasterize([Dot("tip_sparse", 12, 40, 40)], W, H);

        const double coverage = 4.0 / 256;                  // 2x2 ink per 16x16 cell
        var area = Math.PI * 6 * 6;                         // a 12px dab
        var expected = coverage * area * 255;
        var laid = AlphasIn(bmp, new SKRectI(32, 32, 49, 49)).Sum(a => (long)a);

        Assert.InRange(laid, expected * 0.6, expected * 1.4);
    }

    [Fact]
    public void AnEnlargedTipIsSmoothedRatherThanBlocky()
    {
        // The other direction, and the one an artist notices first: a small tip
        // blown up should not have visible square texels.
        var tip = Register("tip_checker_mag", 8);

        using var bmp = FrameRasterizer.Rasterize([Dot(tip, 48)], W, H);

        var middle = AlphasIn(bmp, new SKRectI(50, 30, 71, 51));
        Assert.Contains(middle, a => a is > 40 and < 215);
    }
}
