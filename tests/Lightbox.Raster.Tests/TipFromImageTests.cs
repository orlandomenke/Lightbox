using Lightbox.Raster;
using Lightbox.Raster.Tips;
using SkiaSharp;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Scans into tips. The pipeline's job is the mechanical half of the isolation
/// workflow — the half where going wrong is silent.
/// </summary>
public class TipFromImageTests
{
    /// <summary>A scan: dark ink on light paper, with an off-centre blob.</summary>
    private static SKBitmap Scan(int w, int h, int cx, int cy, int radius, byte paper = 240, byte ink = 20)
    {
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);
        var bytes = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var dx = x - cx;
            var dy = y - cy;
            var v = dx * dx + dy * dy <= radius * radius ? ink : paper;
            var o = (y * w + x) * 4;
            bytes[o] = bytes[o + 1] = bytes[o + 2] = v;
            bytes[o + 3] = 255;
        }
        System.Runtime.InteropServices.Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
        return bitmap;
    }

    private static byte AlphaAt(string png, double u, double v)
    {
        using var bitmap = PngCodec.Decode(png);
        return bitmap.GetPixel(
            Math.Clamp((int)(u * bitmap.Width), 0, bitmap.Width - 1),
            Math.Clamp((int)(v * bitmap.Height), 0, bitmap.Height - 1)).Alpha;
    }

    [Fact]
    public void InkBecomesTheShapeAndPaperBecomesNothing()
    {
        // Levels set the way an artist would, because that is the step that
        // decides what counts as paper — left wide open, a scan's off-white
        // background is still 20% ink and the pipeline is right to say so.
        using var scan = Scan(400, 400, 200, 200, 80);
        var settings = new TipImageSettings { Size = 128, BlackPoint = 0.2, WhitePoint = 0.8 };
        var result = TipFromImage.Create(scan, settings, "blob");

        Assert.True(result.Ok, result.Rejection);
        Assert.Equal(255, AlphaAt(result.Tip!.Png, 0.5, 0.5));
        Assert.Equal(0, AlphaAt(result.Tip.Png, 0.05, 0.05));
    }

    [Fact]
    public void AMarkTouchingTheCropIsRejectedRatherThanFaded()
    {
        // The most valuable thing this pipeline does. Feathering a mark the
        // crop cut through hides the evidence and ships a tip that stamps a
        // soft box down every stroke — the commonest way a hand-made brush is
        // ruined, and trivially detectable. The fix is re-cropping the scan,
        // so the pipeline says so instead of papering over it.
        using var scan = Scan(200, 200, 100, 100, 140);

        var result = TipFromImage.Create(scan, new TipImageSettings { Size = 128 }, "overflow");

        Assert.False(result.Ok);
        Assert.Contains("re-crop", result.Rejection, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AGoodCropIsStillFeatheredAtTheBorder()
    {
        // Belt and braces: the check catches the mark running off, the ramp
        // catches the scan grain that survives levels.
        using var scan = Scan(400, 400, 200, 200, 60);
        var result = TipFromImage.Create(
            scan,
            new TipImageSettings { Size = 128, EdgeFeather = 4, BlackPoint = 0.2, WhitePoint = 0.8 },
            "blob");

        Assert.True(result.Ok, result.Rejection);
        using var tip = PngCodec.Decode(result.Tip!.Png);
        for (var x = 0; x < tip.Width; x++)
        {
            Assert.Equal(0, tip.GetPixel(x, 0).Alpha);
            Assert.Equal(0, tip.GetPixel(x, tip.Height - 1).Alpha);
        }
    }

    [Fact]
    public void TheCropFollowsTheMarkRatherThanThePage()
    {
        // A stamp centred on the paper rotates about itself; one centred on
        // wherever it landed on the platen wobbles.
        using var scan = Scan(400, 400, 120, 300, 50);
        var settings = new TipImageSettings { Size = 128, BlackPoint = 0.2, WhitePoint = 0.8 };

        var centred = TipFromImage.Create(scan, settings, "a");
        var asScanned = TipFromImage.Create(
            scan,
            new TipImageSettings
            {
                Size = 128, BlackPoint = 0.2, WhitePoint = 0.8, CentreOnMass = false,
            },
            "b");

        Assert.True(centred.Ok, centred.Rejection);
        Assert.True(asScanned.Ok, asScanned.Rejection);
        Assert.True(AlphaAt(centred.Tip!.Png, 0.5, 0.5) > 240, "the mark is not in the middle");
        Assert.True(AlphaAt(asScanned.Tip!.Png, 0.5, 0.5) < 240, "test is vacuous — both came out centred");
    }

    [Fact]
    public void LevelsDecideWhatCountsAsPaper()
    {
        // A weak scan: mid-grey ink on off-white paper. With the points left
        // wide the mark comes out soft; pulled in, it comes out solid — which
        // is the whole reason the sliders are there.
        using var scan = Scan(400, 400, 200, 200, 70, paper: 200, ink: 120);

        // Both put the white point at the paper, so the border is clean either
        // way and the only thing under test is the black point.
        var wide = TipFromImage.Create(
            scan, new TipImageSettings { Size = 128, BlackPoint = 0, WhitePoint = 0.78 }, "wide");
        var tight = TipFromImage.Create(
            scan, new TipImageSettings { Size = 128, BlackPoint = 0.5, WhitePoint = 0.78 }, "tight");

        Assert.True(wide.Ok && tight.Ok);
        Assert.True(
            AlphaAt(tight.Tip!.Png, 0.5, 0.5) > AlphaAt(wide.Tip!.Png, 0.5, 0.5) + 40,
            "the levels did nothing");
    }

    [Fact]
    public void AMaskThatIsAlreadyWhiteOnBlackIsNotInvertedAgain()
    {
        using var scan = Scan(400, 400, 200, 200, 70, paper: 10, ink: 245);
        var result = TipFromImage.Create(
            scan, new TipImageSettings { Size = 128, Invert = false }, "mask");

        Assert.True(result.Ok, result.Rejection);
        Assert.True(AlphaAt(result.Tip!.Png, 0.5, 0.5) > 240);
    }

    [Fact]
    public void OneSetOfLevelsAppliedToABatchGivesMatchingTips()
    {
        // The rule that matters most for a series that will be blended: a
        // capture a shade darker than its neighbour makes the brush pulse in
        // brightness as the artist tilts the pen. Same settings in, same
        // density out.
        var settings = new TipImageSettings { Size = 128, BlackPoint = 0.2, WhitePoint = 0.8 };
        using var a = Scan(400, 400, 200, 200, 60);
        using var b = Scan(400, 400, 180, 220, 60);

        var first = TipFromImage.Create(a, settings, "a");
        var second = TipFromImage.Create(b, settings, "b");

        Assert.True(first.Ok && second.Ok);
        Assert.Equal(AlphaAt(first.Tip!.Png, 0.5, 0.5), AlphaAt(second.Tip!.Png, 0.5, 0.5));
    }

    [Fact]
    public void ThePivotStartsAtTheInksOwnCentre()
    {
        // Right for a symmetric stamp, and the starting point an angled one
        // needs — where the pen touched is not where the paint ended up, and
        // the artist can drag it from here.
        using var scan = Scan(400, 400, 200, 200, 60);
        var result = TipFromImage.Create(
            scan, new TipImageSettings { Size = 128, BlackPoint = 0.2, WhitePoint = 0.8 }, "blob");

        Assert.True(result.Ok, result.Rejection);
        Assert.Equal(0.5, result.Tip!.PivotX, 0.05);
        Assert.Equal(0.5, result.Tip.PivotY, 0.05);
    }

    [Fact]
    public void ACollapsedLevelRangeIsClampedRatherThanDividingByZero()
    {
        using var scan = Scan(400, 400, 200, 200, 60);
        var result = TipFromImage.Create(
            scan, new TipImageSettings { Size = 64, BlackPoint = 0.5, WhitePoint = 0.5 }, "x");


        Assert.True(result.Ok, result.Rejection);
        Assert.True(AlphaAt(result.Tip!.Png, 0.5, 0.5) > 240);
    }
}
