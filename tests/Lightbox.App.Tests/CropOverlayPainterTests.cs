using Lightbox.App.Rendering;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The crop overlay, rendered and read back.
/// </summary>
/// <remarks>
/// <para>
/// <b>The dim is the feature</b>, so it is worth pixels rather than a promise:
/// a frame drawn as an outline alone asks the artist to imagine the crop, and
/// darkening what falls outside it is what makes framing something you judge by
/// eye rather than by arithmetic. An overlay that drew the outline and skipped
/// the dim would look almost right in a screenshot and be the wrong tool.
/// </para>
/// <para>
/// Read back off a surface rather than screenshotted through Xvfb, per the
/// house rule: a dropped click in synthetic input looks exactly like a bug.
/// </para>
/// </remarks>
public class CropOverlayPainterTests(ITestOutputHelper output)
{
    private const int W = 200, H = 120;

    /// <summary>Paint the overlay on a white page and hand back the pixels.</summary>
    private static SKBitmap Render(SKRect? frame)
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)!;
        surface.Canvas.Clear(SKColors.White);
        CropOverlayPainter.Paint(surface.Canvas, frame, new SKRect(0, 0, W, H), scale: 1f);
        surface.Canvas.Flush();
        using var image = surface.Snapshot();
        return SKBitmap.FromImage(image);
    }

    [Fact]
    public void OutsideTheFrameIsDarkenedAndInsideIsNot()
    {
        using var bmp = Render(new SKRect(60, 40, 140, 90));

        // Well inside, and off the thirds lines so the faint guides are not
        // what is being measured.
        var inside = bmp.GetPixel(75, 50);
        // Well outside, and clear of the frame's own edge and handles.
        var outside = bmp.GetPixel(15, 15);

        output.WriteLine($"inside R={inside.Red}, outside R={outside.Red}");
        Assert.Equal(255, inside.Red);
        Assert.True(outside.Red < 200, $"outside was not dimmed: R={outside.Red}");
    }

    /// <summary>
    /// The dim is even all the way round — the corners are not doubled.
    /// </summary>
    /// <remarks>
    /// Four overlapping rectangles is the obvious way to write this and it
    /// stacks alpha at the corners, leaving four darker squares. It looks like
    /// a vignette, which is plausible enough that nobody questions it.
    /// </remarks>
    [Fact]
    public void TheDimIsEvenRatherThanDoubledAtTheCorners()
    {
        using var bmp = Render(new SKRect(60, 40, 140, 90));

        var above = bmp.GetPixel(100, 15);   // directly over the frame
        var left = bmp.GetPixel(15, 65);     // directly beside it
        var corner = bmp.GetPixel(15, 15);   // diagonally off it

        output.WriteLine($"above={above.Red}, left={left.Red}, corner={corner.Red}");
        Assert.Equal(above.Red, corner.Red);
        Assert.Equal(left.Red, corner.Red);
    }

    [Fact]
    public void NoFrameDrawsNothingAtAll()
    {
        using var bmp = Render(null);

        // Every pixel still white: with no frame the painter must not dim, must
        // not outline, and must not cost anything.
        Assert.Equal(255, bmp.GetPixel(15, 15).Red);
        Assert.Equal(255, bmp.GetPixel(100, 60).Red);
        Assert.Equal(255, bmp.GetPixel(W - 5, H - 5).Red);
    }

    /// <summary>
    /// A frame filling the page dims nothing, which is the state the tool opens
    /// in.
    /// </summary>
    /// <remarks>
    /// Worth pinning because it is what an artist sees the instant they press
    /// C: if the four bands were computed with an off-by-one the whole canvas
    /// would darken the moment the tool was picked, and the tool would look
    /// like it had done something.
    /// </remarks>
    [Fact]
    public void AFrameRoundTheWholePageDimsNothing()
    {
        using var bmp = Render(new SKRect(0, 0, W, H));

        var middle = bmp.GetPixel(W / 2 + 7, H / 2 + 7);
        output.WriteLine($"middle R={middle.Red}");
        Assert.Equal(255, middle.Red);
    }
}
