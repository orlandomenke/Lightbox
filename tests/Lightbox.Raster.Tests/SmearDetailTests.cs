using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// B92: a smearing smudge moves pixels, so what it drags keeps its detail; a
/// dulling one moves a colour, so detail dissolves. The two are meant to differ
/// in kind, and before this they differed only in degree.
/// </summary>
/// <remarks>
/// <para>
/// The measurement is a block striped red and blue in two-pixel rows, smeared
/// out across its edge. What lands past the edge is read row by row: smearing
/// should still alternate — red rows red, blue rows blue — and dulling should
/// have averaged the two into one purple. The number reported is the mean
/// red-minus-blue swing between a stripe row and its neighbour, which is about
/// 180 on the untouched block, near zero on anything averaged.
/// </para>
/// <para>
/// The other half is what a smear must <em>not</em> do now that it copies:
/// nothing at all on the first dab, the same pixels twice for the same stroke,
/// and the same pixels whether the stroke is stamped in one range or resumed
/// from a checkpoint — the last of which <c>EffectPreviewMatchesCommitTests</c>
/// already holds over a textured ground, and is the reason the carried patch is
/// copied out rather than handed back.
/// </para>
/// </remarks>
public class SmearDetailTests(ITestOutputHelper output)
{
    private const int W = 300, H = 120, Edge = 150;
    private static readonly SKColor Red = new(220, 40, 40, 255);
    private static readonly SKColor Blue = new(40, 60, 220, 255);

    /// <summary>A block striped red/blue in two-pixel rows on the left, bare canvas on the right.</summary>
    private static SKBitmap Striped()
    {
        var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var red = new SKPaint { Color = Red, IsAntialias = false };
        using var blue = new SKPaint { Color = Blue, IsAntialias = false };
        for (var y = 0; y < H; y += 2)
        {
            canvas.DrawRect(new SKRect(0, y, Edge, y + 2), (y / 2) % 2 == 0 ? red : blue);
        }
        canvas.Flush();
        return bmp;
    }

    private static Stroke SmudgeOut(SmudgeMode mode, double length = 0.5, double flow = 0.3) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#000000",
        Points = [.. Enumerable.Range(0, 25).Select(i => new StrokePoint(100 + (i * 4), 60, 1))],
        Brush = new BrushSettings
        {
            Kind = BrushKind.Smudge, Size = 20, Hardness = 0.5, Flow = flow, Spacing = 0.1,
            SmudgeMode = mode, SmudgeLength = length, SmudgeRadius = 0.5,
        },
    };

    private static void Stamp(SKBitmap bmp, Stroke stroke)
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        BrushEngine.StampStroke(canvas, stroke, info, bmp);
        canvas.Flush();
    }

    /// <summary>
    /// Mean |Δ(red − blue)| between each stripe row and the next at column
    /// <paramref name="x"/>, over the rows the dab's centre sweeps.
    /// </summary>
    private static double StripeSwing(SKBitmap bmp, int x)
    {
        double sum = 0;
        var n = 0;
        for (var y = 52; y < 68; y += 2)
        {
            var a = bmp.GetPixel(x, y);
            var b = bmp.GetPixel(x, y + 2);
            sum += Math.Abs((a.Red - a.Blue) - (b.Red - b.Blue));
            n++;
        }
        return sum / n;
    }

    [Fact]
    public void SmearingCarriesTheStripesOutAndDullingDissolvesThem()
    {
        using var smeared = Striped();
        using var dulled = Striped();
        Stamp(smeared, SmudgeOut(SmudgeMode.Smearing));
        Stamp(dulled, SmudgeOut(SmudgeMode.Dulling));

        // Six pixels past the edge: well inside the trail at these settings, and
        // far enough out that nothing of the original block is being read.
        var x = Edge + 6;
        var untouched = StripeSwing(smeared, 20);
        var smear = StripeSwing(smeared, x);
        var dull = StripeSwing(dulled, x);
        output.WriteLine(
            $"stripe swing: untouched block {untouched:F0}, smeared {smear:F0}, dulled {dull:F0} "
            + $"(alpha there: smeared {smeared.GetPixel(x, 60).Alpha}, dulled {dulled.GetPixel(x, 60).Alpha})");

        Assert.True(smeared.GetPixel(x, 60).Alpha > 40, "the smear carried nothing past the edge — nothing is being measured");
        Assert.True(dulled.GetPixel(x, 60).Alpha > 40, "the dulling carried nothing past the edge — nothing is being measured");
        Assert.True(smear > untouched * 0.5, $"smearing lost the stripes: swing {smear:F0} against {untouched:F0} on the block");
        Assert.True(dull < untouched * 0.15, $"dulling kept the stripes: swing {dull:F0} against {untouched:F0} on the block");
    }

    [Fact]
    public void ASmearThatHasNotMovedChangesNothing()
    {
        // The first dab carries what is under it, so a tap lays down only what
        // was already there. A colour-carrying smudge softened a boundary on a
        // tap; a pixel-carrying one has nothing to move yet.
        using var before = Striped();
        using var after = Striped();
        Stamp(after, new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#000000",
            Points = [new StrokePoint(Edge, 60, 1)],
            Brush = new BrushSettings { Kind = BrushKind.Smudge, Size = 30, Hardness = 1, Flow = 1, Spacing = 0.5 },
        });

        var changed = 0;
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                if (before.GetPixel(x, y) != after.GetPixel(x, y)) changed++;
            }
        }
        Assert.Equal(0, changed);
    }

    [Fact]
    public void TheSameSmearRendersTheSamePixelsTwice()
    {
        using var first = Striped();
        using var second = Striped();
        Stamp(first, SmudgeOut(SmudgeMode.Smearing, length: 0.8));
        Stamp(second, SmudgeOut(SmudgeMode.Smearing, length: 0.8));

        Assert.True(first.Bytes.AsSpan().SequenceEqual(second.Bytes), "a smear rendered differently on a second pass");
    }

    [Fact]
    public void ASmearNeverHollowsOutTheBodyItIsDraggedFrom()
    {
        // Copying pixels from the body onto the trail leaves the body as it
        // was: the block is a source of paint, not a supply that runs out.
        using var bmp = Striped();
        Stamp(bmp, SmudgeOut(SmudgeMode.Smearing, length: 1.0, flow: 1.0));

        for (var x = 0; x < Edge; x++)
        {
            Assert.True(bmp.GetPixel(x, 60).Alpha == 255, $"the body lost coverage at x={x}");
        }
    }
}
