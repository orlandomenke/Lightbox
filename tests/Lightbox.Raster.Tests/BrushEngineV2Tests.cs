using System.Security.Cryptography;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.Raster.Tests;

public class BrushEngineV2Tests
{
    private static Stroke Line(BrushSettings brush, string color = "#000000", ToolKind tool = ToolKind.Brush) => new()
    {
        Tool = tool,
        Color = color,
        Brush = brush,
        Points = [new(20, 40, 0.8), new(60, 40, 0.8), new(100, 40, 0.8)],
    };

    private static string Hash(SKBitmap bmp) => Convert.ToHexString(SHA256.HashData(bmp.GetPixelSpan()));

    [Fact]
    public void EffectBrushes_AreDeterministic_AcrossRerenders()
    {
        // Scatter, jitter, granulation and wet edge all re-render identically —
        // the inbetween invariant depends on it.
        var brush = new BrushSettings
        {
            Size = 16, Hardness = 0.6, Flow = 0.7, WetEdge = 0.6, Granulation = 0.4,
            Scatter = 0.5, RotationJitter = 0.5, PressureFlowGamma = 1,
        };
        var strokes = new List<Stroke> { Line(brush, "#2244aa") };
        using var a = FrameRasterizer.Rasterize(strokes, 128, 80);
        using var b = FrameRasterizer.Rasterize(strokes, 128, 80);
        Assert.Equal(Hash(a), Hash(b));
    }

    [Fact]
    public void Flow_BuildsUpWithinAStroke_ButOpacityCapsIt()
    {
        // Low flow: a single pass is lighter than the opacity cap.
        var lowFlow = new BrushSettings { Size = 20, Hardness = 1, Opacity = 1, Flow = 0.3, Spacing = 0.05 };
        using var low = FrameRasterizer.Rasterize([Line(lowFlow)], 128, 80);
        var lowAlpha = low.GetPixel(60, 40).Alpha;
        Assert.InRange(lowAlpha, 60, 254); // dabs overlap and build, but not to full

        // Full flow reaches the cap.
        var fullFlow = new BrushSettings { Size = 20, Hardness = 1, Opacity = 1, Flow = 1 };
        using var full = FrameRasterizer.Rasterize([Line(fullFlow)], 128, 80);
        Assert.Equal(255, full.GetPixel(60, 40).Alpha);

        // And opacity still caps a full-flow stroke.
        var capped = new BrushSettings { Size = 20, Hardness = 1, Opacity = 0.5, Flow = 1 };
        using var cap = FrameRasterizer.Rasterize([Line(capped)], 128, 80);
        Assert.InRange(cap.GetPixel(60, 40).Alpha, 120, 135);
    }

    [Fact]
    public void WetEdge_DarkensTheRim_RelativeToTheCenter()
    {
        var brush = new BrushSettings { Size = 24, Hardness = 1, Opacity = 1, Flow = 1, WetEdge = 1 };
        using var bmp = FrameRasterizer.Rasterize([Line(brush, "#cc4444")], 128, 80);
        var center = bmp.GetPixel(60, 40);
        var rim = bmp.GetPixel(60, 32); // inside the rim band (radius ≈9.6 at pressure 0.8)
        Assert.True(rim.Alpha > 0, "rim should be painted");
        // The rim is darker (lower red channel) than the body where paint pooled.
        Assert.True(rim.Red < center.Red, $"rim {rim} should be darker than center {center}");
    }

    [Fact]
    public void Granulation_CarvesTexture_IntoTheStroke()
    {
        var flat = new BrushSettings { Size = 24, Hardness = 1, Opacity = 1, Flow = 1 };
        var grainy = new BrushSettings { Size = 24, Hardness = 1, Opacity = 1, Flow = 1, Granulation = 0.9 };
        using var a = FrameRasterizer.Rasterize([Line(flat)], 128, 80);
        using var b = FrameRasterizer.Rasterize([Line(grainy)], 128, 80);

        // Sample alphas along the stroke: flat is uniform, grainy varies.
        var flatAlphas = Enumerable.Range(0, 20).Select(i => (int)a.GetPixel(30 + i * 3, 40).Alpha).ToList();
        var grainAlphas = Enumerable.Range(0, 20).Select(i => (int)b.GetPixel(30 + i * 3, 40).Alpha).ToList();
        Assert.True(flatAlphas.Max() - flatAlphas.Min() <= 1);
        Assert.True(grainAlphas.Max() - grainAlphas.Min() > 20, "granulation should modulate alpha");
    }

    [Fact]
    public void SmudgeBrush_DragsExistingColor_AndReplaysDeterministically()
    {
        // A red block, then a smudge stroke dragging out of it to the right.
        var red = new Stroke
        {
            Color = "#ff0000",
            Brush = new BrushSettings { Size = 30, Hardness = 1 },
            Points = [new(30, 40, 1), new(40, 40, 1)],
        };
        var smudge = new Stroke
        {
            Color = "#000000", // ignored — smudge carries canvas color
            Brush = new BrushSettings { Size = 14, Hardness = 0.8, Flow = 0.8, Spacing = 0.1, Kind = BrushKind.Smudge },
            Points = [new(40, 40, 1), new(90, 40, 1)],
        };
        using var a = FrameRasterizer.Rasterize([red, smudge], 128, 80);
        using var b = FrameRasterizer.Rasterize([red, smudge], 128, 80);
        Assert.Equal(Hash(a), Hash(b));

        // Red got dragged well past the block's right edge (~55px).
        var dragged = a.GetPixel(70, 40);
        Assert.True(dragged.Alpha > 0 && dragged.Red > 100, $"expected dragged red at (70,40), got {dragged}");

        // Without the smudge stroke there is nothing there.
        using var baseline = FrameRasterizer.Rasterize([red], 128, 80);
        Assert.Equal(0, baseline.GetPixel(70, 40).Alpha);
    }

    [Fact]
    public void BlurBrush_SoftensAHardEdge()
    {
        var block = new Stroke
        {
            Color = "#000000",
            Brush = new BrushSettings { Size = 40, Hardness = 1 },
            Points = [new(40, 40, 1)],
        };
        var blur = new Stroke
        {
            Color = "#000000",
            Brush = new BrushSettings { Size = 30, Flow = 1, Kind = BrushKind.Blur },
            Points = [new(58, 40, 1), new(62, 40, 1)],
        };
        using var sharp = FrameRasterizer.Rasterize([block], 128, 80);
        using var soft = FrameRasterizer.Rasterize([block, blur], 128, 80);

        // Just outside the disc's hard edge (radius 20 → x=60): sharp is empty,
        // blurred has bled alpha.
        Assert.Equal(0, sharp.GetPixel(66, 40).Alpha);
        Assert.True(soft.GetPixel(66, 40).Alpha > 5, "blur should bleed past the hard edge");
    }

    [Fact]
    public void CustomTip_StampsItsShape()
    {
        // A square tip: corners of the dab get paint where a round dab has none.
        var tipPng = Lightbox.Import.BrushImport.Read("square.gbr", GbrFixture(size: 32, square: true))[0].TipPngBase64!;
        var tipId = "tip_test_square";
        BrushTipRegistry.Register(new Dictionary<string, string> { [tipId] = tipPng });

        var square = new BrushSettings { Size = 30, Flow = 1, Opacity = 1, TipId = tipId, Spacing = 0.5 };
        var round = new BrushSettings { Size = 30, Flow = 1, Opacity = 1, Hardness = 1, Spacing = 0.5 };
        Stroke Dot(BrushSettings b) => new()
        {
            Color = "#000000",
            Brush = b,
            Points = [new(60, 40, 1)],
        };

        using var s = FrameRasterizer.Rasterize([Dot(square)], 128, 80);
        using var r = FrameRasterizer.Rasterize([Dot(round)], 128, 80);
        // Corner of the 30px square (60±13, 40±13) — inside the square, outside the circle.
        Assert.True(s.GetPixel(72, 28).Alpha > 200, "square tip should paint the corner");
        Assert.Equal(0, r.GetPixel(72, 28).Alpha);
    }

    /// <summary>Synthesize a GBR v2 file: solid square or nothing.</summary>
    internal static byte[] GbrFixture(int size, bool square)
    {
        var name = "fixture"u8.ToArray();
        var headerSize = 28 + name.Length + 1;
        using var ms = new MemoryStream();
        void BE(uint v)
        {
            Span<byte> b = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(b, v);
            ms.Write(b);
        }
        BE((uint)headerSize);
        BE(2); // version
        BE((uint)size);
        BE((uint)size);
        BE(1); // gray
        BE(0x47494D50); // GIMP
        BE(20); // spacing percent
        ms.Write(name);
        ms.WriteByte(0);
        for (var i = 0; i < size * size; i++) ms.WriteByte(square ? (byte)255 : (byte)0);
        return ms.ToArray();
    }
}
