using Lightbox.App.Rendering;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The balance arc: dots where the mass is, the current frame emphasised, a
/// flagged frame warm — and nothing at all when the checker is off.
/// </summary>
/// <remarks>
/// Pure-Skia pixel tests, for <see cref="GuidePainter"/>'s reason: the draw op
/// runs only inside a compositor lease a headless run never grants, so the
/// painter has to be reachable without one.
/// </remarks>
public class BalanceOverlayPainterTests
{
    private const int W = 200, H = 120;

    private static SKBitmap Paint(IReadOnlyList<BalanceDot>? dots, float scale = 1f)
    {
        var bmp = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        BalanceOverlayPainter.Paint(canvas, dots, scale);
        canvas.Flush();
        return bmp;
    }

    private static int InkAround(SKBitmap b, int cx, int cy, int reach = 8)
    {
        var count = 0;
        for (var y = Math.Max(0, cy - reach); y < Math.Min(H, cy + reach); y++)
            for (var x = Math.Max(0, cx - reach); x < Math.Min(W, cx + reach); x++)
            {
                var c = b.GetPixel(x, y);
                if (c.Red != 255 || c.Green != 255 || c.Blue != 255) count++;
            }
        return count;
    }

    [Fact]
    public void NoDotsPaintsNothingAtAll()
    {
        using var none = Paint(null);
        using var empty = Paint([]);

        Assert.Equal(0, InkAround(none, W / 2, H / 2, reach: 100));
        Assert.Equal(0, InkAround(empty, W / 2, H / 2, reach: 100));
    }

    [Fact]
    public void EveryFrameGetsADotAndTheArcConnectsThem()
    {
        using var bmp = Paint(
        [
            new BalanceDot(40, 60, Current: false, Flagged: false),
            new BalanceDot(160, 60, Current: false, Flagged: false),
        ]);

        Assert.True(InkAround(bmp, 40, 60) > 0, "no dot at the first frame's mass");
        Assert.True(InkAround(bmp, 160, 60) > 0, "no dot at the second");
        // The arc: ink along the line between them, far from either dot.
        Assert.True(InkAround(bmp, 100, 60, reach: 3) > 0, "no arc between the dots");
    }

    [Fact]
    public void TheCurrentFrameIsTheEmphaticOne()
    {
        using var bmp = Paint(
        [
            new BalanceDot(40, 60, Current: false, Flagged: false),
            new BalanceDot(160, 60, Current: true, Flagged: false),
        ]);

        Assert.True(InkAround(bmp, 160, 60) > InkAround(bmp, 40, 60),
            "the dot being judged should out-draw its context");
    }

    [Fact]
    public void AFlaggedFrameReadsWarm()
    {
        using var bmp = Paint([new BalanceDot(100, 60, Current: true, Flagged: true)]);

        var c = bmp.GetPixel(100, 60);
        Assert.True(c.Red > c.Blue, $"a warning that is not warm is not a warning (got {c})");
    }
}
