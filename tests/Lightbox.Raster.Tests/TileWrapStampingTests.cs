using System.Security.Cryptography;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Seamless tiles on the pixel path (Q192): a mark that leaves one edge comes
/// in on the other, the seam is invisible by construction, and a mark nowhere
/// near an edge costs nothing.
/// </summary>
/// <remarks>
/// The load-bearing test is <see cref="TheSeamIsInvisibleByConstruction"/>:
/// it compares the wrapped-in strip byte for byte against the same stroke
/// rendered on a wider page with no wrap at all. That is the whole claim of
/// doing wrap as a canvas translation — the copy IS the mark, moved — and it
/// is made with a brush whose scatter and jitter would betray a re-rolled
/// copy at once.
/// </remarks>
public class TileWrapStampingTests(ITestOutputHelper output)
{
    private const int W = 200;
    private const int H = 140;

    /// <summary>Scatter and rotation jitter on: a brush a wrong seed would visibly change.</summary>
    private static BrushSettings Speckled => new()
    {
        Size = 16,
        Hardness = 0.8,
        Opacity = 1.0,
        Flow = 0.9,
        Spacing = 0.18,
        Scatter = 0.5,
        RotationJitter = 0.6,
        Granulation = 0,
        WetEdge = 0,
        PressureFlowGamma = 1,
    };

    private static TileWrap Page => new() { Left = 0, Top = 0, Width = W, Height = H };

    /// <summary>A mark that runs off the right edge of the page.</summary>
    private static Stroke OffTheRightEdge(TileWrap? wrap, SymmetryAxis? axis = null) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#1b3f7a",
        Brush = Speckled,
        Points = [new StrokePoint(176, 60, 0.8), new StrokePoint(196, 66, 0.9), new StrokePoint(216, 60, 0.8)],
        Wrap = wrap,
        Symmetry = axis,
    };

    /// <summary>A mark in the middle of the page, no edge within reach.</summary>
    private static Stroke InTheMiddle(TileWrap? wrap) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#1b3f7a",
        Brush = Speckled,
        Points = [new StrokePoint(80, 60, 0.8), new StrokePoint(100, 70, 0.9), new StrokePoint(120, 62, 0.8)],
        Wrap = wrap,
    };

    private static string Hash(SKBitmap b) => Convert.ToHexString(SHA256.HashData(b.GetPixelSpan()));

    private static int InkedIn(SKBitmap b, SKRectI box)
    {
        var n = 0;
        for (var y = Math.Max(0, box.Top); y < Math.Min(b.Height, box.Bottom); y++)
        {
            for (var x = Math.Max(0, box.Left); x < Math.Min(b.Width, box.Right); x++)
            {
                if (b.GetPixel(x, y).Alpha != 0) n++;
            }
        }

        return n;
    }

    [Fact]
    public void AMarkThatLeavesTheRightEdgeComesInOnTheLeft()
    {
        using var wrapped = FrameRasterizer.Rasterize([OffTheRightEdge(Page)], W, H, 1.0);
        using var plain = FrameRasterizer.Rasterize([OffTheRightEdge(null)], W, H, 1.0);

        var left = new SKRectI(0, 40, 30, 90);
        var inWrapped = InkedIn(wrapped, left);
        var inPlain = InkedIn(plain, left);
        output.WriteLine($"left strip: {inWrapped} px wrapped, {inPlain} px without wrap");

        Assert.True(inWrapped > 60, $"the mark did not come in on the left ({inWrapped} px)");
        Assert.Equal(0, inPlain);
    }

    [Fact]
    public void TheSeamIsInvisibleByConstruction()
    {
        // The same stroke, twice: wrapped on the page, and unwrapped on a page
        // twice as wide where it simply continues. The strip that wrapped in
        // must be the continuation, byte for byte — same scatter, same jitter,
        // same antialiasing — because the copy was made by moving the canvas
        // a whole page width and Hash01 saw the authored coordinates.
        using var wrapped = FrameRasterizer.Rasterize([OffTheRightEdge(Page)], W, H, 1.0);
        using var continued = FrameRasterizer.Rasterize([OffTheRightEdge(null)], W * 2, H, 1.0);

        var differing = 0;
        var inked = 0;
        var worstVisible = 0;
        var farthestColumn = -1;
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < 40; x++)
            {
                var a = wrapped.GetPixel(x, y);
                var b = continued.GetPixel(x + W, y);
                if (b.Alpha != 0) inked++;
                if (a == b) continue;
                differing++;
                farthestColumn = Math.Max(farthestColumn, x);
                // Premultiplied colour at an alpha of 4/255 is noise in every
                // channel; only a pixel that can be seen is held to a number.
                if (Math.Max(a.Alpha, b.Alpha) > 16)
                {
                    worstVisible = Math.Max(worstVisible, Math.Max(
                        Math.Max(Math.Abs(a.Red - b.Red), Math.Abs(a.Green - b.Green)),
                        Math.Max(Math.Abs(a.Blue - b.Blue), Math.Abs(a.Alpha - b.Alpha))));
                }
            }
        }
        output.WriteLine(
            $"wrapped-in strip: {inked} inked px against the continuation, {differing} px differ, "
            + $"none past column {farthestColumn}, worst visible channel difference {worstVisible}/255");

        Assert.True(inked > 100, "the continuation strip holds no ink, so the comparison means nothing");
        // Measured on landing: 9 of 300 px differ, all in columns 0–2 — the
        // page edge, where the wrapped copy's scratch is clipped at the page
        // and a dab's rim is antialiased against the clip rather than against
        // more dab — and the visible ones by at most 10/255. The same thing
        // happens to an ordinary mark that starts at a page edge against the
        // same mark on a larger page; it is not wrap's doing. What this guards
        // is that the copy is the SAME mark: a re-rolled scatter or jitter
        // moves whole dabs, differs right across the strip, and differs by
        // hundreds of a channel at full alpha.
        Assert.True(farthestColumn <= 2, $"pixels differ as far in as column {farthestColumn} — the copy is not the mark moved");
        Assert.True(worstVisible <= 16, $"a visible wrapped-in pixel differs by {worstVisible}/255 — that is a different mark, not edge rounding");
    }

    [Fact]
    public void AMarkInTheBottomRightCornerComesInAtTheTopLeft()
    {
        // The diagonal neighbour, which a wrong sign on either offset would
        // send to the wrong corner.
        var corner = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#1b3f7a",
            Brush = Speckled,
            Points = [new StrokePoint(190, 130, 0.9), new StrokePoint(214, 150, 0.9)],
            Wrap = Page,
        };
        using var b = FrameRasterizer.Rasterize([corner], W, H, 1.0);

        var topLeft = InkedIn(b, new SKRectI(0, 0, 30, 30));
        var topRight = InkedIn(b, new SKRectI(W - 30, 0, W, 30));
        var bottomLeft = InkedIn(b, new SKRectI(0, H - 30, 30, H));
        output.WriteLine($"corners: top-left {topLeft}, top-right {topRight}, bottom-left {bottomLeft}");
        Assert.True(topLeft > 20, $"the corner did not wrap diagonally ({topLeft} px)");
        // The straight neighbours land too: left of the mark's run-off, and
        // above its drop below the page.
        Assert.True(topRight > 20);
        Assert.True(bottomLeft > 20);
    }

    [Fact]
    public void AMarkThatTravelsMoreThanAPageAwayStillComesBackOntoIt()
    {
        // Recorded from the left neighbour of the preview across to beyond
        // the right one: the part past 2W is two tiles away, and the fixed
        // ring of eight would have had no copy to bring it back.
        var far = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#1b3f7a",
            Brush = Speckled,
            Points = [new StrokePoint(2 * W + 20, 100, 0.9), new StrokePoint(2 * W + 60, 100, 0.9)],
            Wrap = Page,
        };
        Assert.Equal(15, BrushEngine.SymmetryCopies(far).Length); // kx −1…3 × ky −1…1: 15 cells, the page among them

        using var b = FrameRasterizer.Rasterize([far], W, H, 1.0);
        var onPage = InkedIn(b, new SKRectI(10, 85, 70, 115));
        output.WriteLine($"two tiles away: {onPage} px landed on the page");
        Assert.True(onPage > 100, $"the far part of the stroke did not come back onto the page ({onPage} px)");
    }

    [Fact]
    public void AMarkNowhereNearAnEdgeRendersByteForByteAsWithNoWrap()
    {
        // Eight copies that all miss the page cost eight rectangle tests and
        // draw nothing — the fingerprint says so.
        using var wrapped = FrameRasterizer.Rasterize([InTheMiddle(Page)], W, H, 1.0);
        using var plain = FrameRasterizer.Rasterize([InTheMiddle(null)], W, H, 1.0);

        Assert.Equal(Hash(plain), Hash(wrapped));
    }

    [Fact]
    public void WrapComposesWithSymmetry()
    {
        // Mirror × wrap: the copies are every mirror copy in every cell, so a
        // mark off the right edge mirrored across the vertical centre line
        // also shows up reflected and wrapped — off the LEFT edge's mirror,
        // which is the right edge again, coming in on the left.
        var axis = new SymmetryAxis { CenterX = W / 2.0, CenterY = H / 2.0, AngleDeg = 90, Order = 1, Mirror = true };
        var stroke = OffTheRightEdge(Page, axis);

        // Every mirror copy in every cell the mark reaches into: two mirror
        // copies × the page and its offered neighbours (this mark runs past
        // the right edge, so the ring is one tile wider on that side).
        var cells = Page.OffsetsFor(stroke.Points).Length + 1;
        Assert.True(cells >= 9);
        Assert.Equal(2 * cells, BrushEngine.SymmetryCopies(stroke).Length);

        using var both = FrameRasterizer.Rasterize([stroke], W, H, 1.0);
        // The mirror of x 176–216 is x −16…24: ink at the left edge from the
        // mirror itself, and its wrapped copy at x 184–224 on the right.
        var leftFromMirror = InkedIn(both, new SKRectI(0, 40, 30, 90));
        var rightFromWrappedMirror = InkedIn(both, new SKRectI(184, 40, 200, 90));
        output.WriteLine($"mirror at left {leftFromMirror} px, its wrap at right {rightFromWrappedMirror} px");
        Assert.True(leftFromMirror > 60);
        Assert.True(rightFromWrappedMirror > 30);
    }
}
