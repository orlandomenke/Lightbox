using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The content version caches key on when bitmap identity is not enough —
/// see <see cref="BitmapVersion"/> for why identity fails here.
/// </summary>
public class BitmapVersionTests
{
    [Fact]
    public void AnUntouchedBitmapIsVersionZeroAndBumpsCount()
    {
        using var bmp = new SKBitmap(new SKImageInfo(8, 8));
        Assert.Equal(0, BitmapVersion.Of(bmp));

        BitmapVersion.Bump(bmp);
        BitmapVersion.Bump(bmp);
        Assert.Equal(2, BitmapVersion.Of(bmp));

        // Versions are per-instance: another bitmap is untouched by the bumps.
        using var other = new SKBitmap(new SKImageInfo(8, 8));
        Assert.Equal(0, BitmapVersion.Of(other));
    }

    /// <summary>
    /// The one mutator that exists today announces itself — the discipline
    /// point the class remarks name, pinned so a refactor cannot drop it.
    /// </summary>
    [Fact]
    public void AppendBumpsTheLayerItStampsInto()
    {
        using var layer = new SKBitmap(
            new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul));
        var before = BitmapVersion.Of(layer);

        FrameRasterizer.Append(layer, new Stroke
        {
            Points = [new StrokePoint(10, 10, 1), new StrokePoint(40, 10, 1)],
        });

        Assert.True(BitmapVersion.Of(layer) > before,
            "Append mutated the bitmap in place without bumping its version — every identity-keyed cache above it is now blind to the edit");
    }
}
