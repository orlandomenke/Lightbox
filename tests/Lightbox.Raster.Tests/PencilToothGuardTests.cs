using System.Security.Cryptography;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Two guards the pencil's tooth was reviewed into. The ungated texture pass
/// is pinned byte for byte, because nothing else in the suite covered a
/// textured stroke and a later edit to the gate could have moved every saved
/// textured stroke and stayed green (sensitivity-guardian). And the live pass
/// over a gated brush is bounded by the band, not the stroke, because the
/// press map is carried across the stroke rather than rebuilt per event
/// (leak-hunter).
/// </summary>
[Collection("Performance")]
public class PencilToothGuardTests(ITestOutputHelper output)
{
    private const int W = 960, H = 540;

    private static Stroke Textured(int points, double? gate) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#2050b0",
        Points = [.. Enumerable.Range(0, points).Select(i =>
            new StrokePoint(40 + i * (880.0 / points), 270 + Math.Sin(i * 0.05) * 120, 0.3 + 0.7 * (i % 7) / 6.0))],
        Brush = new BrushSettings
        {
            Size = 9, Hardness = 0.7, Opacity = 1, Flow = 0.9, Spacing = 0.1,
            TextureSurface = PaperKind.ColdPress, TextureScale = 5, TextureDepth = 0.8,
            TexturePressure = gate,
        },
    };

    /// <summary>
    /// Recorded on the engine before the gate existed (the branch with
    /// BrushEngine.cs stashed), so this is the render every saved textured
    /// stroke without a <c>texturePressure</c> key had and must keep.
    /// </summary>
    private const string UngatedFingerprint = "E9BD4D831D658EE2808A23A85975EBF137E3563123AC51F5F7FD52EA04BB3507";

    [Fact]
    public void AnUngatedTextureRendersExactlyAsItDidBeforeTheGateExisted()
    {
        using var bitmap = FrameRasterizer.Rasterize([Textured(60, gate: null)], W, H, 1.0);
        var hash = Convert.ToHexString(SHA256.HashData(bitmap.GetPixelSpan()));
        output.WriteLine($"ungated textured stroke: {hash}");
        Assert.Equal(UngatedFingerprint, hash);
    }

    /// <summary>
    /// B437: a gated texture is a ceiling applied in place as the pen moves,
    /// never a pass. The engine's own predicates say so for the brush under
    /// test, and the shipped pencil is pinned the same way in the App suite.
    /// </summary>
    [Fact]
    public void AGatedTextureIsACeilingNotAPass()
    {
        var brush = Textured(10, gate: 0.8).Brush;
        Assert.True(BrushEngine.NeedsToothCeiling(brush), "the gated texture is not a tooth ceiling");
        Assert.True(BrushEngine.NeedsFootprintCap(brush), "the tooth ceiling does not ride the footprint cap");
        Assert.Equal(0, BrushEngine.CeilingReachPx(brush, 1.0));
        Assert.False(BrushEngine.LiveTipWouldDivergeTooFar(brush), "a capped-in-place brush must show its mark live");
        var ungated = Textured(10, gate: null).Brush;
        Assert.False(BrushEngine.NeedsToothCeiling(ungated));
        Assert.True(BrushEngine.LiveTipWouldDivergeTooFar(ungated), "the ungated paper keeps Q168's route");
    }

    private static SKBitmap Copy(SKBitmap src, SKRectI r)
    {
        var bmp = new SKBitmap(new SKImageInfo(r.Width, r.Height, src.ColorType, src.AlphaType));
        using var canvas = new SKCanvas(bmp);
        using var sub = new SKBitmap();
        Assert.True(src.ExtractSubset(sub, r));
        using var px = sub.PeekPixels();
        using var view = SKImage.FromPixels(px);
        using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
        canvas.DrawImage(view, 0, 0, paint);
        canvas.Flush();
        return bmp;
    }
}
