using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The volume checker's arithmetic: alpha-weighted area and centroid of a
/// rendered frame.
/// </summary>
/// <remarks>
/// Synthetic bitmaps rather than brush output, because the claims under test
/// are about the moments, not the marks: a known rectangle has a known area
/// and a known centre, and any disagreement is this code's fault alone.
/// </remarks>
public class InkMomentsTests
{
    private static SKBitmap Blank(int w = 100, int h = 80)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        bmp.Erase(SKColors.Transparent);
        return bmp;
    }

    private static void FillRect(SKBitmap bmp, int left, int top, int right, int bottom, byte alpha = 255)
    {
        using var canvas = new SKCanvas(bmp);
        using var paint = new SKPaint { Color = new SKColor(20, 20, 20, alpha), BlendMode = SKBlendMode.Src };
        canvas.DrawRect(new SKRect(left, top, right, bottom), paint);
        canvas.Flush();
    }

    [Fact]
    public void ASolidRectangleMeasuresItsOwnAreaAndCentre()
    {
        using var bmp = Blank();
        FillRect(bmp, 10, 20, 30, 60);   // 20 × 40 = 800 px, centred on (20, 40)

        var m = InkMoments.Measure(bmp);

        Assert.True(m.HasInk);
        Assert.Equal(800, m.Area, 1);
        Assert.Equal(20, m.CentroidX, 2);
        Assert.Equal(40, m.CentroidY, 2);
    }

    [Fact]
    public void AreaIsAlphaWeightedSoHalfCoverCountsHalf()
    {
        // Anti-aliased edges must not inflate a mark's mass: the same
        // rectangle at half alpha is half the volume, exactly.
        using var solid = Blank();
        using var faint = Blank();
        FillRect(solid, 10, 10, 50, 50);
        FillRect(faint, 10, 10, 50, 50, alpha: 128);

        var a = InkMoments.Measure(solid);
        var b = InkMoments.Measure(faint);

        Assert.Equal(a.Area * (128.0 / 255.0), b.Area, 1);
    }

    [Fact]
    public void SquashPreservesVolumeAndTheMeasurementAgrees()
    {
        // The property the checker exists to check, demonstrated on itself: a
        // 40×40 square and its 80×20 squash have the same mass, and the
        // measurement must say so or flag squash that was drawn correctly.
        using var square = Blank(200, 200);
        using var squashed = Blank(200, 200);
        FillRect(square, 80, 80, 120, 120);
        FillRect(squashed, 60, 100, 140, 120);

        var a = InkMoments.Measure(square);
        var b = InkMoments.Measure(squashed);

        Assert.Equal(a.Area, b.Area, 1);
    }

    [Fact]
    public void TheCentroidFollowsTheMassNotTheCanvas()
    {
        // Two unequal blobs: the centroid sits closer to the heavier one, at
        // the exact weighted position.
        using var bmp = Blank(300, 100);
        FillRect(bmp, 0, 0, 100, 100);      // 10,000 px centred at (50, 50)
        FillRect(bmp, 200, 40, 250, 60);    // 1,000 px centred at (225, 50)

        var m = InkMoments.Measure(bmp);

        var expectedX = (10_000 * 50 + 1_000 * 225) / 11_000.0;
        Assert.Equal(expectedX, m.CentroidX, 1);
        Assert.Equal(50, m.CentroidY, 1);
    }

    [Fact]
    public void AnEmptyFrameSaysSoInsteadOfInventingAPlace()
    {
        // (0, 0) as a "centroid" would put a balance dot in the corner of
        // every blank frame; absence is a fact the caller branches on.
        using var bmp = Blank();

        var m = InkMoments.Measure(bmp);

        Assert.False(m.HasInk);
        Assert.Equal(0, m.Area);
    }

    [Fact]
    public void MeasuringTwiceIsBitIdentical()
    {
        // A checker that drifts between reads is a random-number generator
        // with a UI. Invariant 2's spirit, applied to reporting.
        using var bmp = Blank();
        FillRect(bmp, 13, 7, 87, 61, alpha: 190);

        var a = InkMoments.Measure(bmp);
        var b = InkMoments.Measure(bmp);

        Assert.Equal(a, b);
    }
}
