using Lightbox.App.Rendering;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The transform box (Ctrl+T), rendered and read back: the second overlay on
/// <see cref="OverlayStyle"/>, and the first with a reference mark — its pivot.
/// </summary>
/// <remarks>
/// It was drawn inside <c>CanvasControl.DrawOp</c> in a cyan of its own
/// (<c>#40C4D4</c>), dashed and uncased, with a pivot in an amber of its own
/// (<c>#E0A030</c>) — legible on white and gone on artwork of either colour.
/// Nothing tested it, because nothing in a draw operation can be.
/// </remarks>
public class TransformGizmoPainterTests(ITestOutputHelper output)
{
    private const int W = 240, H = 160;

    private static readonly SKPoint C0 = new(40, 30), C1 = new(200, 30), C2 = new(200, 130), C3 = new(40, 130);
    private static readonly SKPoint Pivot = new(120, 80);

    private static SKBitmap Render(SKColor page, bool perspective = false, bool pivot = true)
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)!;
        surface.Canvas.Clear(page);
        TransformGizmoPainter.Paint(surface.Canvas, C0, C1, C2, C3, pivot ? Pivot : null, perspective, scale: 1f);
        surface.Canvas.Flush();
        using var image = surface.Snapshot();
        return SKBitmap.FromImage(image);
    }

    private static int Difference(SKColor a, SKColor b) => Math.Max(
        Math.Abs(a.Red - b.Red), Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue)));

    /// <summary>The strongest difference from the page down a column crossing the box's top edge.</summary>
    private static int AcrossTheTopEdge(SKBitmap bmp, SKColor page, int x)
    {
        var strongest = 0;
        for (var y = 24; y <= 36; y++) strongest = Math.Max(strongest, Difference(bmp.GetPixel(x, y), page));
        return strongest;
    }

    /// <summary>
    /// The box reads on any page, including one of its own colour and one of
    /// the colour it used to be.
    /// </summary>
    [Theory]
    [InlineData(255, 255, 255)]
    [InlineData(0, 0, 0)]
    [InlineData(0x00, 0xD1, 0xBC)]
    [InlineData(0x40, 0xC4, 0xD4)]
    public void TheBoxReadsOnAnyPage(byte r, byte g, byte b)
    {
        var page = new SKColor(r, g, b);
        using var bmp = Render(page);
        // A quarter of the way along the top edge: clear of the corner grips
        // and of the grip in the middle of the edge.
        var strongest = AcrossTheTopEdge(bmp, page, 80);
        output.WriteLine($"page #{r:X2}{g:X2}{b:X2}: the top edge differs by {strongest}");
        Assert.True(strongest >= 90, $"the box differs from its page by only {strongest}");
    }

    /// <summary>
    /// A corner is a square grip at rest: open, in the accent. Nothing about a
    /// transform box is "selected", so nothing on it is filled.
    /// </summary>
    [Fact]
    public void ACornerIsAnOpenSquareGrip()
    {
        var page = new SKColor(0x80, 0x80, 0x80);
        using var bmp = Render(page);

        var centre = bmp.GetPixel(40, 30);
        var diagonal = Difference(bmp.GetPixel(44, 34), page);
        output.WriteLine($"corner centre {centre}, four pixels out on the diagonal differs by {diagonal}");
        Assert.True(Difference(centre, SKColors.White) < 12);
        Assert.True(diagonal > 60, "a square reaches its own corner");
    }

    /// <summary>
    /// The pivot is the one reference mark on the box — the point the drawing
    /// turns about, not a part of it — and so the one thing in amber.
    /// </summary>
    [Fact]
    public void ThePivotIsTheReferenceColourAndTheBoxIsNot()
    {
        using var bmp = Render(SKColors.White);

        // On the pivot's ring, off its crosshair.
        var ring = Closest(bmp, Pivot.X + 4.95f, Pivot.Y - 4.95f, OverlayStyle.Reference);
        var edge = Closest(bmp, 80, 30, OverlayStyle.Accent);
        output.WriteLine($"nearest to amber on the pivot ring: {ring}; nearest to the accent on the top edge: {edge}");
        Assert.True(ring < 60, "the pivot ring is the reference colour");
        Assert.True(edge < 60, "the box is the accent");
    }

    [Fact]
    public void WithoutAPivotThereIsNoAmberAnywhere()
    {
        using var bmp = Render(SKColors.White, pivot: false);
        var nearest = 255;
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++) nearest = Math.Min(nearest, Difference(bmp.GetPixel(x, y), OverlayStyle.Reference));
        }
        output.WriteLine($"nearest any pixel comes to amber: {nearest}");
        Assert.True(nearest > 60);
    }

    /// <summary>
    /// Free and perspective transforms move corners only, so the edges carry no
    /// grip: an overlay that showed one nothing could take would be lying.
    /// </summary>
    [Fact]
    public void EdgeGripsAreThereOnlyWhenAnEdgeCanBeDragged()
    {
        var page = new SKColor(0x80, 0x80, 0x80);
        using var affine = Render(page);
        using var perspective = Render(page, perspective: true);

        // Three pixels above the middle of the top edge: inside a grip, clear of the line.
        var withGrip = Difference(affine.GetPixel(120, 27), page);
        var without = Difference(perspective.GetPixel(120, 27), page);
        output.WriteLine($"above the edge's middle: {withGrip} with edge grips, {without} in perspective");
        Assert.True(withGrip > 60);
        Assert.True(without < 24);
    }

    /// <summary>The smallest difference from a colour within a pixel of a point.</summary>
    private static int Closest(SKBitmap bmp, float x, float y, SKColor colour)
    {
        var best = 255;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                best = Math.Min(best, Difference(bmp.GetPixel((int)x + dx, (int)y + dy), colour));
            }
        }
        return best;
    }
}
