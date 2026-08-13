using Lightbox.App.Rendering;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// B17: guides must be visible over the drawing, and the drawing must still
/// read through them.
/// </summary>
/// <remarks>
/// <para>
/// This was <c>evidence: manual</c> for a real reason — the rig is painted by
/// a Skia custom draw op that only runs inside a compositor lease, which a
/// headless run never grants, so nothing in it could be reached by a test. The
/// fix for that was not a cleverer test but moving the painting somewhere a
/// test can call: <see cref="GuidePainter"/> is pure Skia and takes a canvas.
/// </para>
/// <para>
/// The bug itself was an ordering one — guides drawn under the artwork, on the
/// reasoning that a ruler on paper is something you draw over, which does not
/// survive a document that opens with an opaque white background layer. So the
/// order is what <c>PaintDocument</c> owns and what the first test here drives.
/// </para>
/// </remarks>
public class GuidePainterTests
{
    private const int W = 200, H = 120;

    private static SKImage OpaqueWhiteArtwork()
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bmp = new SKBitmap(info);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.White);
            canvas.Flush();
        }
        return SKImage.FromBitmap(bmp);
    }

    /// <summary>A vertical guide down the middle.</summary>
    private static GuidePainter.Line Vertical(float x = 100) =>
        new(Kind: 2, X: x, Y: 0, Spacing: 0, Angles: [90.0]);

    private static SKBitmap PaintOverArtwork(
        IReadOnlyList<GuidePainter.Line>? guides, GuidePainter.Line? draft = null)
    {
        var bmp = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var art = OpaqueWhiteArtwork();
        GuidePainter.PaintDocument(canvas, art, W, H, scale: 1f, checkerboard: null, guides, draft);
        canvas.Flush();
        return bmp;
    }

    /// <summary>The most guide-coloured pixel on the row, and how far from white it is.</summary>
    private static (SKColor Colour, int Distance) StrongestOnRow(SKBitmap b, int y)
    {
        var best = SKColors.White;
        var far = 0;
        for (var x = 0; x < W; x++)
        {
            var c = b.GetPixel(x, y);
            var d = Math.Abs(255 - c.Red) + Math.Abs(255 - c.Green) + Math.Abs(255 - c.Blue);
            if (d <= far) continue;
            far = d;
            best = c;
        }
        return (best, far);
    }

    [Fact]
    public void AGuideIsVisibleOverAnOpaqueDrawing()
    {
        // The bug, as reported: "it shows on the grey surround and vanishes the
        // moment it crosses the canvas." An opaque white artwork is exactly the
        // background layer a new document opens with.
        using var bmp = PaintOverArtwork([Vertical()]);

        var (_, distance) = StrongestOnRow(bmp, H / 2);

        Assert.True(distance > 30, $"the guide left nothing on the artwork (distance {distance})");
    }

    [Fact]
    public void TheArtStillReadsThroughIt()
    {
        // The other half, and the thing the old under-the-art order was
        // protecting. Paid for with alpha instead — so the guide must be a
        // blend of its own colour and the white beneath it, never the flat
        // colour, or the rig would be hiding the drawing again.
        using var bmp = PaintOverArtwork([Vertical()]);

        var (colour, _) = StrongestOnRow(bmp, H / 2);

        Assert.True(colour.Blue > colour.Red, "the guide should read blue");
        Assert.True(colour.Red > 80 + 40,
            $"the guide painted nearly opaque (red {colour.Red}, its own colour is 80)");
    }

    [Fact]
    public void NoGuidesMeansNothingIsPaintedOverTheArt()
    {
        // A document with no rig must be untouched — chrome that costs
        // something when it is absent is the camera's rule broken.
        using var withNone = PaintOverArtwork(null);
        using var withEmpty = PaintOverArtwork([]);

        Assert.Equal(0, StrongestOnRow(withNone, H / 2).Distance);
        Assert.Equal(0, StrongestOnRow(withEmpty, H / 2).Distance);
    }

    [Fact]
    public void ADraftIsBrighterThanAPlacedGuide()
    {
        // The whole gesture is aiming a line, and a draft drawn in the same
        // faint blue as the rig behind it cannot be aimed.
        using var placed = PaintOverArtwork([Vertical()]);
        using var drafting = PaintOverArtwork(null, Vertical());

        Assert.True(
            StrongestOnRow(drafting, H / 2).Distance > StrongestOnRow(placed, H / 2).Distance,
            "the guide being pulled out should stand out from the ones already down");
    }

    [Fact]
    public void AVanishingPointIsMarkedWhereItsRaysMeet()
    {
        // Without a mark you cannot tell which of the rays meet where.
        var vp = new GuidePainter.Line(Kind: 3, X: 100, Y: 60, Spacing: 0, Angles: [0.0, 45.0, 90.0]);

        using var bmp = PaintOverArtwork([vp]);

        // The cross arms are warm, unlike the cool rays.
        var centre = bmp.GetPixel(100, 60);
        Assert.True(centre.Red > centre.Blue, $"no mark at the vanishing point (got {centre})");
    }

    [Fact]
    public void AGridTooFineToReadIsNotDrawnAtAll()
    {
        // Invariant 6's spirit applied to chrome: below a few pixels on screen
        // a grid is a grey wash, and drawing it costs thousands of lines to
        // produce something nobody can use.
        var fine = new GuidePainter.Line(Kind: 1, X: 0, Y: 0, Spacing: 4, Angles: [0.0]);

        using var zoomedOut = PaintOverArtwork(null);
        var bmp = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            using var art = OpaqueWhiteArtwork();
            // 4 px cells at 25% zoom is one pixel on screen.
            GuidePainter.PaintDocument(canvas, art, W, H, 0.25f, null, [fine], null);
            canvas.Flush();
        }

        Assert.Equal(0, StrongestOnRow(bmp, H / 2).Distance);
        bmp.Dispose();
    }

    /// <summary>Any pixel in the rectangle that is not white, or null.</summary>
    private static SKColor? AnyInkIn(SKBitmap b, int left, int top, int right, int bottom)
    {
        for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
            {
                var c = b.GetPixel(x, y);
                if (c.Red != 255 || c.Green != 255 || c.Blue != 255) return c;
            }
        return null;
    }

    [Fact]
    public void AHeightScaleDrawsItsPostAndItsRungs()
    {
        // Four heads of 20 standing on y=100: the post runs up the middle and
        // a rung crosses it at every head. The post is warm — it is the
        // scale's own mark, like a vanishing point's cross — and the rungs
        // are the rig's usual cool blue.
        var scale = new GuidePainter.Line(
            Kind: 4, X: 100, Y: 100, Spacing: 20, Angles: [], Divisions: 4);

        using var bmp = PaintOverArtwork([scale]);

        var post = bmp.GetPixel(100, 60);
        Assert.True(post.Red > post.Blue, $"no post up the scale's middle (got {post})");
        var rung = bmp.GetPixel(105, 80);
        Assert.True(rung.Blue > rung.Red, $"no rung at the first head (got {rung})");
        // And nothing above the top: the whole point of the chart is its extent.
        Assert.Null(AnyInkIn(bmp, 95, 0, 106, 15));
    }

    [Fact]
    public void AHeightScaleSaysHowManyHeadsItIs()
    {
        // Without the count it is a ladder, not a height chart. The label sits
        // just right of the top rung.
        var scale = new GuidePainter.Line(
            Kind: 4, X: 60, Y: 110, Spacing: 20, Angles: [], Divisions: 5);

        using var bmp = PaintOverArtwork([scale]);

        Assert.NotNull(AnyInkIn(bmp, 85, 0, W, 25));
    }

    [Fact]
    public void ANamedGuideWearsItsName()
    {
        // An eye-line that does not say "eye line" is just a line — on a rig
        // somebody else set up, the label is what makes it readable.
        var bare = new GuidePainter.Line(Kind: 0, X: 100, Y: 60, Spacing: 0, Angles: [0.0]);
        var named = bare with { Label = "Horizon" };

        using var without = PaintOverArtwork([bare]);
        using var with = PaintOverArtwork([named]);

        // Above the line, where only the label can be.
        Assert.Null(AnyInkIn(without, 100, 30, W, 52));
        Assert.NotNull(AnyInkIn(with, 100, 30, W, 52));
    }

    [Fact]
    public void AGridCoarseEnoughToReadIsDrawn()
    {
        // The other side of that threshold, so the guard cannot be satisfied by
        // never drawing a grid at all.
        var coarse = new GuidePainter.Line(Kind: 1, X: 0, Y: 0, Spacing: 40, Angles: [0.0]);

        using var bmp = PaintOverArtwork([coarse]);

        Assert.True(StrongestOnRow(bmp, H / 2).Distance > 30);
    }
}
