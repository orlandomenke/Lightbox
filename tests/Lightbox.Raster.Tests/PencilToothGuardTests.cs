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
    // First recorded on the engine before the gate existed (E9BD4D83…), and
    // re-recorded 2026-10-10 for the paint path's first rule
    // (docs/DESIGN-paint-path.md): a document-scale render now stamps its dabs
    // in document coordinates through a pooled plane, where it stamped them
    // into a stroke-sized scratch under a translation, so that replaying a
    // stroke is the same arithmetic as drawing it and pen-up need not render
    // the stroke a second time. Skia's gradient arithmetic is not
    // translation-invariant, so the same mark comes out a level different on
    // a scattering of edge pixels — measured at 3 to 35 of ~42,000 inked
    // pixels, worst 1 level, on the shipped soft brushes — and identical
    // everywhere else. An existing drawing therefore moves by an amount no eye
    // can find; what it buys is that live and replay are now the same bytes.
    private const string UngatedFingerprint = "4561D665D52BD7E6E217F2835292941A58646AA7BA5131919981FD7D2EA8E3F6";

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
        // Phase 2 of the paint path: an ungated paper is a stencil applied to
        // the band under the pen, so it too shows its own mark while drawing.
        Assert.True(BrushEngine.NeedsBandFinish(ungated), "the ungated paper is not finished under the pen");
        Assert.False(BrushEngine.LiveTipWouldDivergeTooFar(ungated), "a brush finished in place must show its mark live");
    }

    /// <summary>
    /// A live mark may only be kept where a replay shares its frame of
    /// reference: above the document plane's limit a replay stamps into a
    /// stroke-sized scratch under a translation, a level away on a scattering
    /// of pixels. Asked of the size alone, so it is pinned without rendering a
    /// document that large on a test runner.
    /// </summary>
    [Fact]
    public void ADocumentTooLargeForThePlaneIsNotReplayedInDocumentSpace()
    {
        Assert.True(BrushEngine.ReplaysInDocumentSpace(1920, 1080));
        Assert.True(BrushEngine.ReplaysInDocumentSpace(8000, 8000));
        Assert.False(BrushEngine.ReplaysInDocumentSpace(8192, 8193));
        Assert.False(BrushEngine.ReplaysInDocumentSpace(0, 1080));
    }

    /// <summary>
    /// How far a later dab can change a finished pixel it does not touch: a
    /// falloff's width for the ceiling, further for a wet edge, nothing for a
    /// brush with no ceiling or a tooth (which is read as it was laid).
    /// </summary>
    [Fact]
    public void TheFinishReachesAsFarAsTheEdgeItReads()
    {
        var plain = new BrushSettings { Size = 100, Hardness = 1 };
        var soft = new BrushSettings { Size = 100, Hardness = 0.2, Flow = 1 };
        var wet = new BrushSettings { Size = 100, Hardness = 0.9, Flow = 1, WetEdge = 0.6 };
        Assert.Equal(0, BrushEngine.FinishReachPx(plain));
        Assert.Equal(0, BrushEngine.FinishReachPx(Textured(100, gate: 0.8).Brush));
        Assert.True(BrushEngine.NeedsFootprintCap(soft), "the control has no ceiling, so its reach says nothing");
        Assert.True(BrushEngine.FinishReachPx(soft) >= 40, $"soft: {BrushEngine.FinishReachPx(soft)} px for a 40 px falloff");
        Assert.True(BrushEngine.FinishReachPx(wet) >= 13, $"wet edge: {BrushEngine.FinishReachPx(wet)} px for a 12 px rim");
        Assert.True(BrushEngine.FinishReachPx(soft) <= 60 && BrushEngine.FinishReachPx(wet) <= 30, "a reach this generous is paid for on every pointer event");
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
