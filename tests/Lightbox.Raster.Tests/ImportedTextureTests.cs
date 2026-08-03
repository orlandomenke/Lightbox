using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Paper from a scan rather than one of the built-in surfaces.
/// </summary>
public class ImportedTextureTests(ITestOutputHelper output)
{
    private const int W = 320, H = 120;

    /// <summary>A texture with a coarse checker in it, so the tooth is unmistakable.</summary>
    private static SKBitmap Checker(int side = 32, int cell = 8)
    {
        var bitmap = new SKBitmap(new SKImageInfo(side, side, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                var lit = (x / cell + y / cell) % 2 == 0;
                bitmap.SetPixel(x, y, lit ? SKColors.White : SKColors.Black);
            }
        }
        return bitmap;
    }

    private static string Register(SKBitmap texture, string id)
    {
        TextureRegistry.Register(new Dictionary<string, string> { [id] = PngCodec.Encode(texture) });
        return id;
    }

    private static SKBitmap Paint(Action<BrushSettings> tweak)
    {
        var brush = new BrushSettings { Size = 40, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.05 };
        tweak(brush);

        var stroke = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#202020",
            Points = [new StrokePoint(30, 60, 1), new StrokePoint(290, 60, 1)],
            Brush = brush,
        };

        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        var layer = new SKBitmap(info);
        using var canvas = new SKCanvas(layer);
        canvas.Clear(SKColors.Transparent);
        BrushEngine.StampStroke(canvas, stroke, info, layer);
        canvas.Flush();
        return layer;
    }

    /// <summary>Spread of alpha along the middle of the stroke — how toothy the mark is.</summary>
    private static double Roughness(SKBitmap bmp)
    {
        var values = new List<double>();
        for (var x = 60; x < 260; x++) values.Add(bmp.GetPixel(x, 60).Alpha / 255.0);
        var mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / values.Count);
    }

    [Fact]
    public void AnImportedPaperBitesIntoTheStroke()
    {
        using var texture = Checker();
        var id = Register(texture, "tex-bite");

        using var flat = Paint(_ => { });
        using var toothy = Paint(b => { b.TextureId = id; b.TextureDepth = 0.9; b.TextureScale = 24; });

        output.WriteLine($"flat {Roughness(flat):F4}, imported {Roughness(toothy):F4}");
        Assert.True(Roughness(toothy) > Roughness(flat) + 0.05,
            $"the paper did not bite: {Roughness(flat):F4} vs {Roughness(toothy):F4}");
    }

    [Fact]
    public void DepthZeroLeavesTheStrokeExactlyAsItWas()
    {
        // The control has to be able to be off, and off has to mean untouched
        // rather than nearly untouched.
        using var texture = Checker();
        var id = Register(texture, "tex-off");

        using var plain = Paint(_ => { });
        using var zero = Paint(b => { b.TextureId = id; b.TextureDepth = 0; });

        for (var y = 0; y < H; y += 2)
        for (var x = 0; x < W; x += 2)
            Assert.Equal(plain.GetPixel(x, y), zero.GetPixel(x, y));
    }

    [Fact]
    public void AnImportedPaperWinsOverTheBuiltInSurfaces()
    {
        // The artist went and found an image; that is the more specific answer.
        using var texture = Checker();
        var id = Register(texture, "tex-wins");

        using var built = Paint(b => { b.TextureSurface = PaperKind.Rough; b.TextureDepth = 0.9; b.TextureScale = 24; });
        using var imported = Paint(b =>
        {
            b.TextureSurface = PaperKind.Rough;
            b.TextureId = id;
            b.TextureDepth = 0.9;
            b.TextureScale = 24;
        });

        var same = true;
        for (var x = 60; x < 260 && same; x++)
        {
            if (built.GetPixel(x, 60) != imported.GetPixel(x, 60)) same = false;
        }
        Assert.False(same, "the built-in surface was used even though a paper was imported");
    }

    [Fact]
    public void ThePaperIsAnchoredToTheDocumentRatherThanToTheStroke()
    {
        // The property that makes it usable at all: two strokes crossing the
        // same patch of paper sit on the same tooth. Sampling from each
        // stroke's own origin looks right in one mark and obviously wrong in
        // two.
        using var texture = Checker();
        var id = Register(texture, "tex-anchor");

        void Setup(BrushSettings b)
        {
            b.TextureId = id;
            b.TextureDepth = 1;
            b.TextureScale = 24;
        }

        Stroke Bar(double fromX, double toX)
        {
            var brush = new BrushSettings { Size = 40, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.05 };
            Setup(brush);
            return new Stroke
            {
                Tool = ToolKind.Brush,
                Color = "#202020",
                Points = [new StrokePoint(fromX, 60, 1), new StrokePoint(toX, 60, 1)],
                Brush = brush,
            };
        }

        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);

        // The same patch of canvas, reached by two strokes that started in
        // different places.
        using var fromLeft = new SKBitmap(info);
        using (var canvas = new SKCanvas(fromLeft))
        {
            canvas.Clear(SKColors.Transparent);
            BrushEngine.StampStroke(canvas, Bar(30, 290), info, fromLeft);
            canvas.Flush();
        }

        using var fromMiddle = new SKBitmap(info);
        using (var canvas = new SKCanvas(fromMiddle))
        {
            canvas.Clear(SKColors.Transparent);
            BrushEngine.StampStroke(canvas, Bar(150, 290), info, fromMiddle);
            canvas.Flush();
        }

        for (var x = 200; x < 260; x++)
        {
            Assert.Equal(fromLeft.GetPixel(x, 60).Alpha, fromMiddle.GetPixel(x, 60).Alpha);
        }
    }

    [Fact]
    public void TheSamePaperPaintsTheSameMarkEveryTime()
    {
        // Invariant 2. Nothing here is seeded from anything but position, so a
        // re-render is identical and the inbetweener can replay it.
        using var texture = Checker();
        var id = Register(texture, "tex-repeat");

        using var first = Paint(b => { b.TextureId = id; b.TextureDepth = 0.8; });
        using var again = Paint(b => { b.TextureId = id; b.TextureDepth = 0.8; });

        for (var y = 0; y < H; y += 2)
        for (var x = 0; x < W; x += 2)
            Assert.Equal(first.GetPixel(x, y), again.GetPixel(x, y));
    }

    [Fact]
    public void ATextureThatIsNotRegisteredIsIgnoredRatherThanFatal()
    {
        // A document opened without its textures — a hand-edited file, a
        // half-finished import — must still paint.
        using var painted = Paint(b => { b.TextureId = "tex-does-not-exist"; b.TextureDepth = 1; });
        using var plain = Paint(_ => { });

        for (var y = 0; y < H; y += 4)
        for (var x = 0; x < W; x += 4)
            Assert.Equal(plain.GetPixel(x, y), painted.GetPixel(x, y));
    }

    // ---- the registry -------------------------------------------------------

    [Fact]
    public void AHugeScanIsReducedRatherThanHeldWhole()
    {
        // A 6000px scan is 36 million floats — 144 MB for a tooth that repeats
        // every few hundred pixels.
        using var huge = new SKBitmap(new SKImageInfo(2400, 1800, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        var field = TextureRegistry.ToField(huge);

        Assert.True(field.Width <= TextureRegistry.MaxSide, $"{field.Width} wide");
        Assert.True(field.Height <= TextureRegistry.MaxSide, $"{field.Height} tall");
        // The aspect ratio survives, or the grain comes out stretched.
        Assert.Equal(2400.0 / 1800.0, (double)field.Width / field.Height, 2);
    }

    [Fact]
    public void ASmallTextureIsLeftAtItsOwnSize()
    {
        using var small = Checker(64, 8);

        var field = TextureRegistry.ToField(small);

        Assert.Equal(64, field.Width);
        Assert.Equal(64, field.Height);
    }

    [Fact]
    public void HeightComesFromLuminanceNotFromOneChannel()
    {
        // A scan slightly warm or cool has to read as the same tooth it looks
        // like, so a pure red patch and a pure blue one cannot both be "white".
        using var bitmap = new SKBitmap(new SKImageInfo(2, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.SetPixel(0, 0, new SKColor(255, 0, 0));
        bitmap.SetPixel(1, 0, new SKColor(0, 0, 255));

        var field = TextureRegistry.ToField(bitmap);

        Assert.True(field.At(0, 0) > field.At(1, 0),
            $"red {field.At(0, 0):F3} should read brighter than blue {field.At(1, 0):F3}");
        Assert.InRange(field.At(0, 0), 0.25, 0.35);
    }

    [Fact]
    public void ANegativeDocumentCoordinateTilesRatherThanThrowing()
    {
        // A stroke's region can start left of the origin, and plain `%` would
        // index backwards off the array.
        var field = TextureRegistry.ToField(Checker(16, 4));

        Assert.Equal(field.At(0, 0), field.At(-16, -16));
        Assert.Equal(field.At(3, 5), field.At(-13, -11));
    }
}
