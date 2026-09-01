using Lightbox.App.Services;
using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The live tip stamped at the resolution it is displayed rather than at the
/// document's, which is the only lever B322 has left (B322).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an arm rather than a fix.</b> Six attempts at B322 treated the tip's
/// cost as a constant to be chosen better. It is not a constant, it is an area:
/// <c>LiveTipDabCostTests</c> measures a size-70 dab at about <b>45-50 us</b>
/// into a 3840x2160 buffer and about <b>11 us</b> into the 1440x810 surface the
/// artist is actually looking at. Covering a fast stroke's median outstanding
/// run is about <b>12.5 ms</b> one way and <b>3.0</b> the other, against a 3 ms
/// budget. No value of the budget bridges the first; only the area does.
/// </para>
/// <para>
/// <b>What these tests hold, and what they cannot.</b> They pin that a tip
/// stamped smaller lands in the same document place, that it still respects its
/// declared bounds, and that the default arm is unchanged. Whether the
/// resolution seam between preview-scale ink and the processed body behind it is
/// acceptable is a question about how a mark looks, and no assertion answers it
/// — the owner asked to see both arms, which is what the flag is for.
/// </para>
/// </remarks>
public class LiveTipPreviewScaleTests(ITestOutputHelper output)
{
    private const int W = 64, H = 48;

    private static readonly SKRectI Body = new(4, 20, 24, 28);
    private static readonly SKRectI Tip = new(40, 20, 56, 28);
    private static readonly SKRectI Stray = new(4, 4, 20, 12);

    private static SKBitmap Filled(int width, int height, double scale, params SKRectI[] marks)
    {
        var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false };
        foreach (var m in marks)
        {
            canvas.DrawRect(
                SKRect.Create(
                    (float)(m.Left * scale), (float)(m.Top * scale),
                    (float)(m.Width * scale), (float)(m.Height * scale)),
                paint);
        }

        return bmp;
    }

    private static SKBitmap Filled(params SKRectI[] marks) => Filled(W, H, 1.0, marks);

    private static bool AnyInk(SKBitmap bmp, SKRectI where)
    {
        for (var y = where.Top; y < where.Bottom; y++)
        {
            for (var x = where.Left; x < where.Right; x++)
            {
                if (bmp.GetPixel(x, y).Alpha > 0) return true;
            }
        }

        return false;
    }

    private static SKBitmap Composed(ScenePassBuilder.LiveEdit live)
    {
        var layer = new Layer { Name = "art" };
        layer.Cels.Add(new Cel { Frame = new Frame() });
        var scene = new Scene { Width = W, Height = H, FrameCount = 1 };
        scene.Layers.Add(layer);

        var state = new ScenePassBuilder.State(
            0, layer.Id, false, false, false, new OnionSettings { Enabled = false }, false);

        var result = ScenePassBuilder.Build(
            scene, state, new FrameBitmapCache(), new TileFallbackTally(), live);

        using var image = SceneRenderer.Compose(W, H, result.Passes, SKColors.Transparent);
        var shot = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        Assert.True(image.ReadPixels(shot.Info, shot.GetPixels(), shot.RowBytes, 0, 0));
        return shot;
    }

    private static ScenePassBuilder.LiveEdit MidStroke(
        SKBitmap raw, SKBitmap processed, SKBitmap tip, SKRectI tipBounds, double tipScale) =>
        new(
            Scratch: raw,
            PostScratch: processed,
            TipScratch: tip,
            TipBounds: tipBounds,
            TipScale: tipScale,
            PostStampedCount: 1,
            BrushStroke: new Stroke { Tool = ToolKind.Brush });

    /// <summary>
    /// <b>The whole claim of the cheaper arm in one assertion.</b> A quarter as
    /// many pixels describing the tip, and the mark is still where the document
    /// says it is — because the bounds stay in document space and only the
    /// source rectangle knows the buffer is smaller.
    /// </summary>
    [Fact]
    public void ATipStampedSmallerLandsInTheSameDocumentPlace()
    {
        using var raw = Filled(Body, Tip);
        using var processed = Filled(Body);
        // Half-size buffer, the same mark drawn into it at half coordinates —
        // which is what canvas.Scale does to the dab walk in the live path.
        using var half = Filled(W / 2, H / 2, 0.5, Tip);

        using var screen = Composed(MidStroke(raw, processed, half, Tip, 0.5));

        var body = AnyInk(screen, Body);
        var tip = AnyInk(screen, Tip);
        output.WriteLine($"body on screen: {body}, tip on screen: {tip}");

        Assert.True(body, "the settled body of the stroke is not being shown at all");
        Assert.True(
            tip,
            "a tip stamped at preview resolution did not reach the screen, so the "
            + "cheaper arm draws nothing and B322 has no lever left");
    }

    /// <summary>
    /// <b>The bound has to survive the scale change.</b> Bounding the stamp
    /// without bounding the draw is half a leak, and a scaled draw is exactly
    /// where a rectangle gets mislaid.
    /// </summary>
    [Fact]
    public void InkOutsideTheDeclaredBoundsIsNotDrawnAtPreviewScaleEither()
    {
        using var raw = Filled(Body);
        using var processed = Filled(Body);
        using var half = Filled(W / 2, H / 2, 0.5, Tip, Stray);

        using var screen = Composed(MidStroke(raw, processed, half, Tip, 0.5));

        Assert.True(AnyInk(screen, Tip), "the declared tip was not drawn");
        Assert.False(
            AnyInk(screen, Stray),
            "ink outside the declared bounds reached the screen through the scaled "
            + "draw, so the source rectangle is being computed but not the clip");
    }

    /// <summary>
    /// <b>The default arm is untouched.</b> A capture of today's behaviour has to
    /// mean what it meant before this branch, or the comparison the owner asked
    /// for is between two changed things.
    /// </summary>
    [Fact]
    public void TheDefaultIsPreviewResolutionAndDocumentIsTheOptOut()
    {
        // Q170, decided 2026-09-02 on the owner's own captures: the cheaper arm
        // is the default. The opt-out still pins document resolution at every
        // compose scale, and the report names each arm for what it is.
        Assert.True(LiveTipScale.PreviewScale);
        Assert.Equal(0.375, LiveTipScale.For(0.375, previewScale: true));
        Assert.Equal(1.0, LiveTipScale.For(0.375, previewScale: false));
        Assert.Equal(1.0, LiveTipScale.For(1.0, previewScale: false));
        Assert.Contains("document resolution", LiveTipScale.Describe(1.0));
        Assert.Contains("the default", LiveTipScale.Describe(0.375));
    }

    /// <summary>
    /// <b>The saving is view-dependent, and the arm says so honestly.</b> At
    /// fit-to-window there is 4.2x in it; composing 1:1 there is nothing in it,
    /// and a tip buffer larger than the document would be worse than useless.
    /// </summary>
    [Theory]
    [InlineData(0.375, 0.375)]
    [InlineData(0.5, 0.5)]
    [InlineData(1.0, 1.0)]
    [InlineData(2.0, 1.0)]
    [InlineData(0.0, 1.0)]
    public void ThePreviewArmFollowsTheComposeScaleAndNeverExceedsIt(double compose, double expected)
    {
        Assert.Equal(expected, LiveTipScale.For(compose, previewScale: true));
    }

    /// <summary>
    /// <b>The buffer is rounded outward, and the failure it prevents would be
    /// invisible in a report.</b> A document dimension times a compose scale is
    /// almost never whole. Round down and the buffer loses its last row and
    /// column — exactly where the newest dab sits when the pen heads right or
    /// down — so the tip would be clipped on two edges, at some zoom levels and
    /// not others, with every count in the report still reading correctly.
    /// </summary>
    [Theory]
    [InlineData(3840, 2160, 0.375, 1440, 810)]
    // 0.4 of 3840 is 1536 exactly; 0.4 of 2161 is 864.4 and must not become 864.
    [InlineData(3840, 2161, 0.4, 1536, 865)]
    [InlineData(1001, 1001, 0.333, 334, 334)]
    // At or above 1 the buffer is the document, with no arithmetic at all.
    [InlineData(3840, 2160, 1.0, 3840, 2160)]
    // A scale small enough to round to nothing still has to be a real bitmap.
    [InlineData(3, 3, 0.001, 1, 1)]
    public void TheBufferIsRoundedOutwardAndNeverToNothing(
        int width, int height, double scale, int expectedWidth, int expectedHeight)
    {
        var (w, h) = LiveTipScale.BufferSize(width, height, scale);
        Assert.Equal(expectedWidth, w);
        Assert.Equal(expectedHeight, h);
        Assert.True(
            w >= width * scale && h >= height * scale,
            "the buffer must cover the scaled document rather than fall inside it");
    }

    /// <summary>
    /// The report has to name which arm ran, or two captures that differ only in
    /// the tip's resolution are indistinguishable afterwards.
    /// </summary>
    [Fact]
    public void TheArmIsNamedForTheReport()
    {
        Assert.Contains("preview resolution", LiveTipScale.Describe(0.375));
        Assert.Contains("0.375", LiveTipScale.Describe(0.375));
        Assert.Contains(LiveTipScale.Variable, LiveTipScale.Describe(0.375));
        Assert.Contains(LiveTipScale.Variable, LiveTipScale.Describe(1.0));
    }
}
