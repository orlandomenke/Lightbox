using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The Match lines overlay as painted (Q271): an unmatched line in the onion
/// colour of its key, a matched one in its match's colour, shared with its
/// partner.
/// </summary>
public sealed class LineMatchPainterTests
{
    private static LineMatchLine Line(double y, int key, int? match = null, bool pending = false) =>
        new([new StrokePoint(10, y + 0.5, 1), new StrokePoint(90, y + 0.5, 1)], key, match, pending);   // on the pixel's centre: a one-pixel line

    private static SKColor PaintAndRead(LineMatchOverlay overlay, double y)
    {
        using var bitmap = new SKBitmap(100, 100);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            LineMatchPainter.Paint(canvas, overlay, 1f);
        }
        // x 30: on a dash of key B's line as well as on key A's solid one.
        return bitmap.GetPixel(30, (int)y);
    }

    [Fact]
    public void AnUnmatchedLineIsInItsKeysOnionColour()
    {
        var overlay = new LineMatchOverlay([Line(20, 0), Line(60, 1)], "#d04040", "#3060c0");

        var a = PaintAndRead(overlay, 20);
        var b = PaintAndRead(overlay, 60);

        Assert.True(a.Red > 180 && a.Blue < 100, $"key A drew {a}");
        Assert.True(b.Blue > 160 && b.Red < 100, $"key B drew {b}");
    }

    [Fact]
    public void BothSidesOfAMatchShareItsColour()
    {
        var overlay = new LineMatchOverlay([Line(20, 0, match: 2), Line(60, 1, match: 2)], "#d04040", "#3060c0");
        var expected = LineMatchPainter.MatchColour(2);

        var a = PaintAndRead(overlay, 20);
        var b = PaintAndRead(overlay, 60);

        Assert.Equal(expected.WithAlpha(255), a.WithAlpha(255));
        Assert.Equal(expected.WithAlpha(255), b.WithAlpha(255));
    }

    [Fact]
    public void NeighbouringMatchesAreFarApartInColour()
    {
        for (var i = 0; i < 12; i++)
        {
            LineMatchPainter.MatchColour(i).ToHsv(out var h1, out _, out _);
            LineMatchPainter.MatchColour(i + 1).ToHsv(out var h2, out _, out _);
            var apart = Math.Abs(h1 - h2);
            Assert.True(Math.Min(apart, 360 - apart) > 60, $"matches {i} and {i + 1} are {apart:0} degrees apart");
        }
    }
}
