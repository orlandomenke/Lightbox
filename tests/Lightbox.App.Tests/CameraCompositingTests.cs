using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Compositing through a camera. Two things are worth pinning: that the matrix
/// frames what it claims to, and that a document <em>without</em> a camera
/// still takes the old uniform-scale path unchanged — optional means absent,
/// and a document that never asked for a camera must not start rendering
/// through one.
/// </summary>
public class CameraCompositingTests
{
    private const int DocW = 400, DocH = 300;

    /// <summary>A document with a distinct colour in each quadrant.</summary>
    private static SKBitmap Quadrants()
    {
        var info = new SKImageInfo(DocW, DocH, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bmp = new SKBitmap(info);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        void Fill(float x, float y, SKColor c)
        {
            using var p = new SKPaint { Color = c, IsAntialias = false };
            canvas.DrawRect(new SKRect(x, y, x + DocW / 2f, y + DocH / 2f), p);
        }
        Fill(0, 0, SKColors.Red);
        Fill(DocW / 2f, 0, SKColors.Lime);
        Fill(0, DocH / 2f, SKColors.Blue);
        Fill(DocW / 2f, DocH / 2f, SKColors.Yellow);
        canvas.Flush();
        return bmp;
    }

    private static SKBitmap Compose(
        SKBitmap layer, int outW, int outH, double scale = 1.0, SKMatrix? transform = null, SKRectI? clip = null)
    {
        var info = new SKImageInfo(outW, outH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)!;
        SceneRenderer.ComposeInto(
            surface, [new RenderPass(layer, null, 1.0)], SKColors.White, clip, scale, transform);
        using var image = surface.Snapshot();
        var result = new SKBitmap(info);
        image.ReadPixels(info, result.GetPixels(), result.RowBytes, 0, 0);
        return result;
    }

    [Fact]
    public void ACameraCentredOnAQuadrant_FramesThatQuadrant()
    {
        using var layer = Quadrants();
        // Centre on the middle of the bottom-right quadrant at 2x, into a
        // frame half the document's size: it should be filled with yellow.
        var framing = new CameraFraming(DocW * 0.75, DocH * 0.75, 2.0, 0);
        var m = CameraTransform.Matrix(framing, DocW / 2, DocH / 2, 1.0);
        using var shot = Compose(layer, DocW / 2, DocH / 2, transform: m);

        Assert.Equal(SKColors.Yellow, shot.GetPixel(shot.Width / 2, shot.Height / 2));
        Assert.Equal(SKColors.Yellow, shot.GetPixel(4, 4));
        Assert.Equal(SKColors.Yellow, shot.GetPixel(shot.Width - 5, shot.Height - 5));
    }

    [Fact]
    public void ZoomingOutShowsMoreOfTheDocument()
    {
        using var layer = Quadrants();
        // Dead centre at 0.5x into a document-sized frame: all four quadrants
        // fit in the middle, with paper around them.
        var framing = new CameraFraming(DocW / 2.0, DocH / 2.0, 0.5, 0);
        var m = CameraTransform.Matrix(framing, DocW, DocH, 1.0);
        using var shot = Compose(layer, DocW, DocH, transform: m);

        Assert.Equal(SKColors.Red, shot.GetPixel(DocW / 2 - 40, DocH / 2 - 30));
        Assert.Equal(SKColors.Yellow, shot.GetPixel(DocW / 2 + 40, DocH / 2 + 30));
        // Outside the shrunken document: paper.
        Assert.Equal(SKColors.White, shot.GetPixel(4, 4));
    }

    [Fact]
    public void RollingTheCameraRotatesWhatTheFrameSees()
    {
        using var layer = Quadrants();
        var upright = CameraTransform.Matrix(
            new CameraFraming(DocW / 2.0, DocH / 2.0, 1.0, 0), DocW, DocH, 1.0);
        var rolled = CameraTransform.Matrix(
            new CameraFraming(DocW / 2.0, DocH / 2.0, 1.0, 90), DocW, DocH, 1.0);

        using var a = Compose(layer, DocW, DocH, transform: upright);
        using var b = Compose(layer, DocW, DocH, transform: rolled);

        // Upright, up and to the left of centre is the top-left quadrant: red.
        Assert.Equal(SKColors.Red, a.GetPixel(DocW / 2 - 50, DocH / 2 - 40));
        // Rolled 90 degrees, the quadrant that swings into that spot is blue.
        Assert.Equal(SKColors.Blue, b.GetPixel(DocW / 2 - 50, DocH / 2 - 40));
    }

    [Fact]
    public void NoTransform_ComposesExactlyAsItDidBeforeCamerasExisted()
    {
        using var layer = Quadrants();
        // Null is not identity: it takes the branch that predates cameras.
        // Passing an identity matrix instead must produce the same pixels, or
        // the two paths have diverged.
        using var plain = Compose(layer, DocW, DocH);
        using var identity = Compose(layer, DocW, DocH, transform: SKMatrix.CreateIdentity());

        Assert.True(Identical(plain, identity), "the camera path disagrees with the plain path at identity");
    }

    [Fact]
    public void NoTransform_StillHonoursTheUniformScalePath()
    {
        using var layer = Quadrants();
        using var half = Compose(layer, DocW / 2, DocH / 2, scale: 0.5);
        Assert.Equal(SKColors.Red, half.GetPixel(20, 20));
        Assert.Equal(SKColors.Yellow, half.GetPixel(DocW / 2 - 20, DocH / 2 - 20));
    }

    [Fact]
    public void AClipUnderARollingCamera_CoversWhereTheRegionActuallyLanded()
    {
        // The dirty rect is document-space. Under a roll it maps to a rotated
        // quad, and clipping to the unrotated rect would leave the parts that
        // swung outside it unpainted — stale pixels that only appear once a
        // camera has rotation on it.
        var doc = new SKRectI(0, 0, 100, 100);
        var rolled = CameraTransform.Matrix(
            new CameraFraming(50, 50, 1.0, 45), 200, 200, 1.0);

        var bounds = CameraTransform.DeviceBounds(doc, 1.0, rolled);
        // A 100x100 square rolled 45 degrees needs a bounding box of about
        // 141 on a side; the unrotated mapping would have said 101.
        Assert.True(bounds.Width > 138, $"rotated dirty rect only spanned {bounds.Width:0.0} px");
        Assert.True(bounds.Height > 138, $"rotated dirty rect only spanned {bounds.Height:0.0} px");
    }

    [Fact]
    public void DeviceBoundsWithoutACamera_IsTheMappingTheCompositorAlwaysUsed()
    {
        var doc = new SKRectI(10, 20, 110, 140);
        var bounds = CameraTransform.DeviceBounds(doc, 0.5, null);
        Assert.Equal(5, bounds.Left);
        Assert.Equal(10, bounds.Top);
        Assert.Equal(51, bounds.Width);   // ceil(100 * 0.5) + 1
        Assert.Equal(61, bounds.Height);  // ceil(120 * 0.5) + 1
    }

    [Fact]
    public void TheComposeRingCopiesForwardCorrectlyUnderACamera()
    {
        // The ring copies regions between buffers, and it must map them with
        // the same matrix the compositor drew them with. A mismatch moves the
        // wrong pixels, and only shows once a camera has rotation.
        using var layer = Quadrants();
        var m = CameraTransform.Matrix(new CameraFraming(200, 150, 1.0, 30), DocW, DocH, 1.0);
        var info = new SKImageInfo(DocW, DocH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var ring = new ComposeRing();

        SKImage Publish(SKRectI? dirty) => ring.Publish(
            info, dirty,
            (surface, clip) => SceneRenderer.ComposeInto(
                surface, [new RenderPass(layer, null, 1.0)], SKColors.White, clip, 1.0, m),
            1.0, m);

        void Paint(SKRectI r, SKColor c)
        {
            using var canvas = new SKCanvas(layer);
            using var paint = new SKPaint { Color = c, IsAntialias = false, BlendMode = SKBlendMode.Src };
            canvas.DrawRect(new SKRect(r.Left, r.Top, r.Right, r.Bottom), paint);
            canvas.Flush();
        }

        for (var i = 0; i < 3; i++) Publish(null).Dispose();

        // Two small, well-separated changes, published one after the other.
        // Two are needed: with a single region every buffer repaints it for
        // itself when picked, so a wrong copy would be harmless and the test
        // would pass while proving nothing. With two, the buffers picked for
        // the second publish can only have the FIRST change if they were
        // caught up correctly — which is exactly the mapping under test.
        var first = new SKRectI(40, 30, 120, 90);
        var second = new SKRectI(260, 190, 350, 260);

        Paint(first, SKColors.Black);
        Publish(first).Dispose();

        Paint(second, SKColors.White);
        using var reference = Compose(layer, DocW, DocH, transform: m);

        for (var i = 0; i < 3; i++)
        {
            using var published = Publish(second);
            using var got = new SKBitmap(info);
            published.ReadPixels(info, got.GetPixels(), got.RowBytes, 0, 0);

            // Identical everywhere the artwork actually is. The one exclusion
            // is precise rather than a tolerance: pixels whose document
            // preimage sits within a pixel of the document's outer boundary,
            // where Skia's clipped and unclipped rotated blits disagree about
            // edge coverage. A wrong mapping moves interior pixels by tens of
            // units, so this still catches it.
            var (n, worst, where) = Diff(reference, got, m, DocW, DocH);
            Assert.True(n == 0,
                $"publish {i} diverged from a fresh composite: {n} px, worst {worst} at {where}");
        }
    }

    /// <summary>Differing pixels, ignoring the document's own outer boundary.</summary>
    private static (int Count, int Worst, string Where) Diff(
        SKBitmap a, SKBitmap b, SKMatrix m, int docW, int docH)
    {
        m.TryInvert(out var toDoc);
        using var pa = a.PeekPixels();
        using var pb = b.PeekPixels();
        var sa = pa.GetPixelSpan();
        var sb = pb.GetPixelSpan();
        int count = 0, worst = 0;
        var where = "";
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                var ia = y * pa.RowBytes + x * 4;
                var ib = y * pb.RowBytes + x * 4;
                var d = 0;
                for (var c = 0; c < 4; c++) d = Math.Max(d, Math.Abs(sa[ia + c] - sb[ib + c]));
                if (d == 0) continue;

                var p = toDoc.MapPoint(x + 0.5f, y + 0.5f);
                if (p.X < 1 || p.Y < 1 || p.X > docW - 1 || p.Y > docH - 1) continue;

                count++;
                if (d > worst)
                {
                    worst = d;
                    where = $"({x},{y}) doc({p.X:0.0},{p.Y:0.0}) " +
                            $"ref={sa[ia]},{sa[ia + 1]},{sa[ia + 2]},{sa[ia + 3]} " +
                            $"got={sb[ib]},{sb[ib + 1]},{sb[ib + 2]},{sb[ib + 3]}";
                }
            }
        }
        return (count, worst, where);
    }

    private static bool Identical(SKBitmap a, SKBitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;
        using var pa = a.PeekPixels();
        using var pb = b.PeekPixels();
        var sa = pa.GetPixelSpan();
        var sb = pb.GetPixelSpan();
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                var ia = y * pa.RowBytes + x * 4;
                var ib = y * pb.RowBytes + x * 4;
                for (var c = 0; c < 4; c++) if (sa[ia + c] != sb[ib + c]) return false;
            }
        }
        return true;
    }
}
