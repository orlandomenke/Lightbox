using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// A region repaint that starts from a held picture of the strokes before one
/// stroke: the same pixels as replaying them, without replaying them.
/// </summary>
/// <remarks>
/// The engine half of "reshaping a line redraws the line, not the drawing".
/// The picture is a render of the drawing's first strokes; the repaint copies
/// its rectangle and replays only what comes after. Both things are held
/// here, where the stamping can be counted with nothing else drawing: that
/// the result is the whole render byte for byte, and that the strokes in the
/// picture were not stamped again.
/// </remarks>
public class RegionRepaintOverHeldArtTests(ITestOutputHelper output)
{
    private const int W = 640, H = 400;

    private static Stroke Sweep(int i, ToolKind tool = ToolKind.Brush, LayerBlendMode? blend = null)
    {
        var stroke = new Stroke
        {
            Tool = tool,
            Color = (i % 3) switch { 0 => "#203040", 1 => "#a03020", _ => "#207040" },
            Brush = new BrushSettings { Size = 14, Hardness = 0.7, Blend = blend },
        };
        var y0 = 30 + i * 26;
        var y1 = H - 30 - i * 22;
        for (var k = 0; k <= 40; k++)
        {
            var t = k / 40.0;
            stroke.Points.Add(new StrokePoint
            {
                X = 20 + t * (W - 40),
                Y = y0 + (y1 - y0) * t + Math.Sin(t * 9 + i) * 14,
                Pressure = 0.35 + 0.6 * Math.Sin(t * Math.PI),
            });
        }
        return stroke;
    }

    private static Frame Below(Frame frame, int count) =>
        new() { Id = frame.Id + "/under", Strokes = [.. frame.Strokes.Take(count)] };

    private static long Differing(SKBitmap a, SKBitmap b)
    {
        using var pa = a.PeekPixels();
        using var pb = b.PeekPixels();
        var sa = pa.GetPixelSpan();
        var sb = pb.GetPixelSpan();
        long n = 0;
        for (var i = 0; i < sa.Length; i++) if (sa[i] != sb[i]) n++;
        return n;
    }

    private static readonly SKRectI[] Regions =
    [
        new(0, 0, W, H), new(40, 150, 600, 260), new(280, 160, 360, 240), new(411, 203, 640, 400),
    ];

    /// <summary>
    /// For every place a stroke can sit in the drawing — first, in the
    /// middle, last — with paint, an eraser and a blend mode over it.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(11)]
    public void ARepaintOverHeldArtIsTheWholeRender(int from)
    {
        var frame = new Frame { Id = "drawing" };
        for (var i = 0; i < 12; i++)
        {
            frame.Strokes.Add(i switch
            {
                7 => Sweep(i, ToolKind.Eraser),
                9 => Sweep(i, blend: LayerBlendMode.Multiply),
                _ => Sweep(i),
            });
        }
        using var whole = FrameRasterizer.Materialize(frame, W, H);
        using var below = FrameRasterizer.Materialize(Below(frame, from), W, H);

        foreach (var region in Regions)
        {
            using var patched = whole.Copy();
            // Spoil the rectangle first, so a repaint that did nothing cannot pass.
            using (var c = new SKCanvas(patched)) c.DrawRect(region, new SKPaint { Color = SKColors.Magenta });
            Assert.True(FrameRasterizer.RepaintRegion(patched, frame, region, W, H, below: below, fromStroke: from));
            var differing = Differing(patched, whole);
            output.WriteLine($"held under stroke {from}, region at {region.Left},{region.Top} {region.Width}×{region.Height}: {differing} bytes differ");
            Assert.True(differing == 0, $"{differing} bytes differ");
        }
    }

    /// <summary>
    /// The point of it: with eleven strokes held under the last one, a repaint
    /// of the whole page stamps the last one and nothing else.
    /// </summary>
    [Fact]
    public void TheStrokesInTheHeldPictureAreNotStampedAgain()
    {
        var frame = new Frame { Id = "drawing" };
        for (var i = 0; i < 12; i++) frame.Strokes.Add(Sweep(i));
        using var whole = FrameRasterizer.Materialize(frame, W, H);
        using var below = FrameRasterizer.Materialize(Below(frame, 11), W, H);
        var page = new SKRectI(0, 0, W, H);

        using var replayed = whole.Copy();
        var before = BrushEngine.DabsStamped;
        Assert.True(FrameRasterizer.RepaintRegion(replayed, frame, page, W, H));
        var byReplay = BrushEngine.DabsStamped - before;

        using var held = whole.Copy();
        before = BrushEngine.DabsStamped;
        Assert.True(FrameRasterizer.RepaintRegion(held, frame, page, W, H, below: below, fromStroke: 11));
        var overHeldArt = BrushEngine.DabsStamped - before;

        output.WriteLine($"dabs to repaint the page: by replay {byReplay:N0}, over held art {overHeldArt:N0}");
        Assert.Equal(0, Differing(held, whole));
        Assert.True(overHeldArt > 0);
        Assert.True(overHeldArt * 6 < byReplay, "the strokes under the line were stamped again");
    }

    /// <summary>A held picture of the wrong size, or a render at another scale, is refused rather than used.</summary>
    [Fact]
    public void AHeldPictureThatDoesNotFitIsRefused()
    {
        var frame = new Frame { Id = "drawing" };
        for (var i = 0; i < 4; i++) frame.Strokes.Add(Sweep(i));
        using var whole = FrameRasterizer.Materialize(frame, W, H);
        using var small = new SKBitmap(new SKImageInfo(W / 2, H / 2, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var patched = whole.Copy();

        Assert.False(FrameRasterizer.RepaintRegion(patched, frame, new SKRectI(0, 0, W, H), W, H, below: small, fromStroke: 2));
        Assert.Equal(0, Differing(patched, whole));
    }
}
