using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using SkiaSharp;
using Glyph = Lightbox.App.Rendering.CanvasControl.PathNodeGlyph;

namespace Lightbox.App.Tests;

/// <summary>
/// The path overlay — the pen's line in progress, the white arrow's points and
/// their handles — rendered and read back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Each test is something that was wrong on screen</b>, found by putting the
/// real application's overlay in front of the owner (perf lab, pen-path,
/// 2026-10-12): a handle was a hollow circle a pixel smaller than the hollow
/// circle it belonged to, so a selected point read as three points in a row;
/// and nothing but a pale halo stood between the line and artwork of its own
/// colour.
/// </para>
/// <para>
/// It is also the first overlay drawn from <see cref="OverlayStyle"/>, the one
/// place the canvas's chrome takes its colour and its sizes from (Q249), so
/// what is pinned here is the style as much as the pen.
/// </para>
/// </remarks>
public class PathOverlayPainterTests(ITestOutputHelper output)
{
    private const int W = 240, H = 120;

    private static SKBitmap Render(
        SKColor page, IReadOnlyList<Glyph>? nodes = null,
        IReadOnlyList<StrokePoint>? trace = null, float scale = 1f)
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)!;
        surface.Canvas.Clear(page);
        // The document transform the real draw runs under: coordinates are in
        // document units, sizes are divided by the scale.
        surface.Canvas.Scale(scale);
        PathOverlayPainter.Paint(surface.Canvas, nodes, trace, null, scale);
        surface.Canvas.Flush();
        using var image = surface.Snapshot();
        return SKBitmap.FromImage(image);
    }

    private static int Difference(SKColor a, SKColor b) => Math.Max(
        Math.Abs(a.Red - b.Red), Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue)));

    /// <summary>How many pixels of a square around a point are not the page.</summary>
    private static int Marked(SKBitmap bmp, SKColor page, int cx, int cy, int half) =>
        Marked(bmp, page, cx, cy, half, half);

    private static int Marked(SKBitmap bmp, SKColor page, int cx, int cy, int halfWide, int halfHigh)
    {
        var count = 0;
        for (var y = cy - halfHigh; y <= cy + halfHigh; y++)
        {
            for (var x = cx - halfWide; x <= cx + halfWide; x++)
            {
                if (Difference(bmp.GetPixel(x, y), page) > 24) count++;
            }
        }
        return count;
    }

    private static List<StrokePoint> Across() =>
        [new StrokePoint { X = 20, Y = 60 }, new StrokePoint { X = 220, Y = 60 }];

    /// <summary>
    /// The line is two passes, and between them it stands out from any page —
    /// including one that is exactly its own colour.
    /// </summary>
    [Theory]
    [InlineData(255, 255, 255)]
    [InlineData(0, 0, 0)]
    [InlineData(0x00, 0xD1, 0xBC)]
    [InlineData(0x26, 0x29, 0x2F)]
    public void TheLineReadsOnAnyPage(byte r, byte g, byte b)
    {
        var page = new SKColor(r, g, b);
        using var bmp = Render(page, trace: Across());

        var strongest = 0;
        for (var y = 54; y <= 66; y++) strongest = Math.Max(strongest, Difference(bmp.GetPixel(120, y), page));
        output.WriteLine($"page #{r:X2}{g:X2}{b:X2}: strongest difference across the line {strongest}");
        Assert.True(strongest >= 90, $"the line differs from its page by only {strongest}");
    }

    [Fact]
    public void AHandleIsVisiblySmallerThanThePointItBelongsTo()
    {
        var page = SKColors.White;
        // One handle only, out to the right: the point's left half and the
        // knob's right half are then both clear of the stem, and those are
        // what is compared.
        Glyph[] nodes = [new(120, 60, 0, 0, 50, 0, Corner: false, Selected: true)];
        using var bmp = Render(page, nodes);

        var point = Marked(bmp, page, 115, 60, 4, 8);
        var handle = Marked(bmp, page, 175, 60, 4, 8);
        output.WriteLine($"point covers {point} px, handle {handle} px, ratio {(double)handle / point:0.00}");
        Assert.True(handle > 6, "the handle has to be there to be grabbed");
        Assert.True(handle * 2 <= point, $"a handle of {handle} px beside a point of {point} px reads as another point");
    }

    [Fact]
    public void ACornerIsSquareAndASmoothPointIsRound()
    {
        var page = SKColors.White;
        Glyph[] nodes =
        [
            new(60, 60, 0, 0, 0, 0, Corner: true, Selected: false),
            new(180, 60, 0, 0, 0, 0, Corner: false, Selected: false),
        ];
        using var bmp = Render(page, nodes);

        // Four pixels out on the diagonal: inside a nine-pixel square, outside
        // a circle of the same width.
        var corner = Difference(bmp.GetPixel(64, 64), page);
        var smooth = Difference(bmp.GetPixel(184, 64), page);
        output.WriteLine($"diagonal of the corner {corner}, of the smooth point {smooth}");
        Assert.True(corner > 60);
        Assert.True(smooth < 24);
    }

    [Fact]
    public void ASelectedPointIsFilledAndAnUnselectedOneIsOpen()
    {
        var page = new SKColor(0x80, 0x80, 0x80);
        Glyph[] nodes =
        [
            new(60, 60, 0, 0, 0, 0, Corner: true, Selected: false),
            new(180, 60, 0, 0, 0, 0, Corner: true, Selected: true),
        ];
        using var bmp = Render(page, nodes);

        var open = bmp.GetPixel(60, 60);
        var filled = bmp.GetPixel(180, 60);
        output.WriteLine($"open centre {open}, filled centre {filled}, accent {OverlayStyle.Accent}");
        Assert.True(Difference(open, SKColors.White) < 12);
        Assert.True(Difference(filled, OverlayStyle.Accent) < 12);
    }

    /// <summary>
    /// A point is the same size to grab at 25% as at 400%: sizes are screen
    /// pixels, whatever the document is scaled by.
    /// </summary>
    [Fact]
    public void EverySizeIsScreenPixelsAtAnyZoom()
    {
        var page = SKColors.White;
        using var atOne = Render(page, [new Glyph(120, 60, -50, 0, 50, 0, Corner: true, Selected: true)]);
        using var atFour = Render(
            page, [new Glyph(30, 15, -12.5, 0, 12.5, 0, Corner: true, Selected: true)], scale: 4f);
        using var atQuarter = Render(
            page, [new Glyph(480, 240, -200, 0, 200, 0, Corner: true, Selected: true)], scale: 0.25f);

        var one = Marked(atOne, page, 120, 60, 10);
        var four = Marked(atFour, page, 120, 60, 10);
        var quarter = Marked(atQuarter, page, 120, 60, 10);
        output.WriteLine($"point at 100% {one} px, at 400% {four} px, at 25% {quarter} px");
        Assert.InRange(four, one * 0.85, one * 1.15);
        Assert.InRange(quarter, one * 0.85, one * 1.15);
    }

    [Fact]
    public void TheOverlayAccentIsThePalettesCyan()
    {
        var palette = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Lightbox.App", "Styles", "Palette.axaml"));
        var hex = $"#{OverlayStyle.Accent.Red:X2}{OverlayStyle.Accent.Green:X2}{OverlayStyle.Accent.Blue:X2}";
        Assert.Contains($"<Color x:Key=\"AccentCyan\">{hex}</Color>", palette);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Lightbox.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
