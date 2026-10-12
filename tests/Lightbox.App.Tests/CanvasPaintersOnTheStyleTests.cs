using Lightbox.App.Rendering;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The crop frame, the reference boxes and the rig's marks, each drawn from
/// <see cref="OverlayStyle"/> (Q249, Q252) — rendered and read back.
/// </summary>
/// <remarks>
/// One class for three painters because it asks each the same three things:
/// is the line the accent, is a grip the style's open square, and has the
/// colour of its own that it used to carry gone. What each painter is
/// <em>for</em> stays in its own test class.
/// </remarks>
public class CanvasPaintersOnTheStyleTests(ITestOutputHelper output)
{
    private const int W = 240, H = 160;
    private static readonly SKColor Page = new(0x80, 0x80, 0x80);

    private static SKBitmap Render(Action<SKCanvas> draw)
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)!;
        surface.Canvas.Clear(Page);
        draw(surface.Canvas);
        surface.Canvas.Flush();
        using var image = surface.Snapshot();
        return SKBitmap.FromImage(image);
    }

    private static int From(SKColor a, SKColor b) => Math.Max(
        Math.Abs(a.Red - b.Red), Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue)));

    /// <summary>The nearest any pixel in a small box comes to a colour.</summary>
    private static int Nearest(SKBitmap bmp, int cx, int cy, SKColor colour, int half = 2)
    {
        var best = 255;
        for (var y = cy - half; y <= cy + half; y++)
        {
            for (var x = cx - half; x <= cx + half; x++) best = Math.Min(best, From(bmp.GetPixel(x, y), colour));
        }
        return best;
    }

    /// <summary>The nearest any pixel in the whole picture comes to a colour.</summary>
    private static int NearestAnywhere(SKBitmap bmp, SKColor colour)
    {
        var best = 255;
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++) best = Math.Min(best, From(bmp.GetPixel(x, y), colour));
        }
        return best;
    }

    // ---- crop ------------------------------------------------------------------

    [Fact]
    public void TheCropFrameTakesItsPensFromTheStyle()
    {
        // A frame round the whole page, so nothing is dimmed and the page
        // colour under the frame is known.
        using var bmp = Render(c => CropOverlayPainter.Paint(
            c, new SKRect(40, 30, 200, 130), new SKRect(40, 30, 200, 130), scale: 1f));

        var edge = Nearest(bmp, 80, 30, OverlayStyle.Accent);
        var corner = bmp.GetPixel(40, 30);
        var middleOfAnEdge = bmp.GetPixel(120, 30);
        output.WriteLine($"crop edge is {edge} from the accent; corner grip centre {corner}; edge grip centre {middleOfAnEdge}");
        Assert.True(edge < 60, "the frame is the accent");
        Assert.True(From(corner, SKColors.White) < 12, "a corner is an open grip");
        Assert.True(From(middleOfAnEdge, SKColors.White) < 12, "and so is the middle of an edge");
    }

    // ---- reference boxes ---------------------------------------------------------

    [Fact]
    public void ASelectedReferenceBoxIsTheStyledLineWithOpenGripsAndAnAmberMark()
    {
        var box = new CanvasControl.ReferenceBox(60, 40, 120, 80, 120.5f, 80.5f, Selected: true);
        using var bmp = Render(c => ReferenceBoxPainter.Paint(c, [box], null, null, 1f));

        var edge = Nearest(bmp, 90, 40, OverlayStyle.Accent);
        var corner = bmp.GetPixel(60, 40);
        var mark = Nearest(bmp, 124, 80, OverlayStyle.Reference);
        output.WriteLine($"selected box edge {edge} from the accent; corner centre {corner}; registration mark {mark} from amber");
        Assert.True(edge < 60);
        Assert.True(From(corner, SKColors.White) < 12);
        Assert.True(mark < 60, "the registration mark is a reference mark");
    }

    [Fact]
    public void AnUnselectedReferenceBoxIsAThinLineWithNoGrips()
    {
        var box = new CanvasControl.ReferenceBox(60, 40, 120, 80, float.NaN, float.NaN, Selected: false);
        using var bmp = Render(c => ReferenceBoxPainter.Paint(c, [box], null, null, 1f));

        var edge = Nearest(bmp, 90, 40, OverlayStyle.Accent);
        // Four pixels in from the corner on the diagonal: inside a grip if
        // there were one, bare page if there is not.
        var whereAGripWouldBe = From(bmp.GetPixel(63, 43), Page);
        output.WriteLine($"unselected box edge {edge} from the accent; inside where a grip would be: {whereAGripWouldBe} from the page");
        Assert.True(edge < 80);
        Assert.True(whereAGripWouldBe < 12, "handles only on the box that is selected");
    }

    // ---- the rig -------------------------------------------------------------------

    private static RigMark Shape(bool selected) =>
        new("s", RigMarkKind.Shape, "", 60, 40, 120, 80, selected);

    private static RigMark Anchor(bool selected) =>
        new("a", RigMarkKind.Anchor, "", 120, 80, 0, 0, selected);

    [Fact]
    public void TheRigsMarksAreTheAccentAndNotTheirOldBlueOrange()
    {
        using var bmp = Render(c => RigOverlayPainter.Paint(c, [Shape(false), Anchor(false)], 1f));

        var accent = NearestAnywhere(bmp, OverlayStyle.Accent);
        var oldAnchorBlue = NearestAnywhere(bmp, new SKColor(0x4d, 0xc4, 0xff));
        var oldShapeOrange = NearestAnywhere(bmp, new SKColor(0xff, 0xa5, 0x3d));
        output.WriteLine($"nearest to the accent {accent}; to the old anchor blue {oldAnchorBlue}; to the old shape orange {oldShapeOrange}");
        Assert.True(accent < 40);
        Assert.True(oldAnchorBlue > 40, "an anchor is told from a shape by its cross, not by a colour");
        Assert.True(oldShapeOrange > 60);
    }

    [Fact]
    public void ASelectedShapeHasOpenGripsAtItsCorners()
    {
        using var selected = Render(c => RigOverlayPainter.Paint(c, [Shape(true)], 1f));
        using var plain = Render(c => RigOverlayPainter.Paint(c, [Shape(false)], 1f));

        output.WriteLine($"corner centre: selected {selected.GetPixel(60, 40)}, unselected three in on the diagonal {plain.GetPixel(63, 43)}");
        Assert.True(From(selected.GetPixel(60, 40), SKColors.White) < 12);
        Assert.True(From(plain.GetPixel(63, 43), Page) < 12);
    }
}
