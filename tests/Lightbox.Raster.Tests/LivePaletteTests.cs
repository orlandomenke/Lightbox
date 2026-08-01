using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Live palettes, the way Toon Boom does them: a stroke records WHICH SWATCH
/// it was painted with, not a colour, so recolouring the swatch recolours
/// every stroke referencing it — across every frame and every layer.
///
/// It works for vector and raster by construction, because both are the same
/// stroke record going through the same engine. There is nothing raster- or
/// vector-specific to get right; the colour is resolved once, in one place.
/// </summary>
public class LivePaletteTests : IDisposable
{
    private const int W = 120, H = 80;

    public LivePaletteTests() => PaletteRegistry.Clear();

    public void Dispose() => PaletteRegistry.Clear();

    private static SKImageInfo Info => new(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);

    private static Stroke Bar(string? swatchId, string literal) => new()
    {
        Tool = ToolKind.Brush,
        Color = literal,
        SwatchId = swatchId,
        Points = [new StrokePoint(20, 40, 1), new StrokePoint(100, 40, 1)],
        Brush = new BrushSettings { Size = 20, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.1, AntiAlias = false },
    };

    private static SKColor Paint(Stroke stroke)
    {
        using var bmp = FrameRasterizer.Rasterize([stroke], W, H);
        return bmp.GetPixel(60, 40);
    }

    [Fact]
    public void RecolouringASwatchRecoloursTheStroke()
    {
        var swatch = new Swatch { Color = "#ff0000" };
        PaletteRegistry.Register([new Palette { Swatches = [swatch] }]);
        var stroke = Bar(swatch.Id, "#ff0000");

        Assert.Equal(SKColors.Red, Paint(stroke));

        // The edit an artist makes in the palette docker.
        swatch.Color = "#0000ff";
        Assert.Equal(SKColors.Blue, Paint(stroke));
    }

    [Fact]
    public void OneSwatchDrivesEveryFrameAndEveryLayer()
    {
        // The point of the feature. Five strokes standing in for five frames
        // on two layers; one edit changes all of them.
        var swatch = new Swatch { Color = "#20c040" };
        PaletteRegistry.Register([new Palette { Swatches = [swatch] }]);
        var strokes = Enumerable.Range(0, 5).Select(_ => Bar(swatch.Id, "#20c040")).ToList();

        Assert.All(strokes, s => Assert.Equal(new SKColor(0x20, 0xc0, 0x40), Paint(s)));

        swatch.Color = "#c02040";
        Assert.All(strokes, s => Assert.Equal(new SKColor(0xc0, 0x20, 0x40), Paint(s)));
    }

    [Fact]
    public void VectorAndRasterFramesResolveTheSameSwatch()
    {
        var swatch = new Swatch { Color = "#804020" };
        PaletteRegistry.Register([new Palette { Swatches = [swatch] }]);
        var stroke = Bar(swatch.Id, "#000000");

        var vector = new VectorFrame { Strokes = [stroke] };
        var painted = new PaintedFrame { Strokes = [stroke] };

        using var fromVector = FrameRasterizer.Rasterize(vector.Strokes, W, H);
        using var fromPainted = FrameRasterizer.Materialize(painted, W, H);

        var expected = new SKColor(0x80, 0x40, 0x20);
        Assert.Equal(expected, fromVector.GetPixel(60, 40));
        Assert.Equal(expected, fromPainted.GetPixel(60, 40));

        swatch.Color = "#208040";
        using var vectorAgain = FrameRasterizer.Rasterize(vector.Strokes, W, H);
        using var paintedAgain = FrameRasterizer.Materialize(painted, W, H);
        Assert.Equal(new SKColor(0x20, 0x80, 0x40), vectorAgain.GetPixel(60, 40));
        Assert.Equal(new SKColor(0x20, 0x80, 0x40), paintedAgain.GetPixel(60, 40));
    }

    [Fact]
    public void AStrokeWithNoSwatchKeepsItsOwnColour()
    {
        var swatch = new Swatch { Color = "#ff0000" };
        PaletteRegistry.Register([new Palette { Swatches = [swatch] }]);

        var linked = Bar(swatch.Id, "#ff0000");
        var literal = Bar(null, "#00ff00");

        swatch.Color = "#0000ff";
        Assert.Equal(SKColors.Blue, Paint(linked));
        Assert.Equal(SKColors.Lime, Paint(literal));
    }

    [Fact]
    public void AMissingSwatchFallsBackToTheColourTheArtistLastSaw()
    {
        // A document can arrive with its palette stripped, or referencing a
        // swatch someone deleted. Black would be a lie; the recorded literal
        // is the last thing that was actually on screen.
        var stroke = Bar("sw_does_not_exist", "#3070b0");
        Assert.Equal(new SKColor(0x30, 0x70, 0xb0), Paint(stroke));
    }

    [Fact]
    public void AFillResolvesTheSwatchToo()
    {
        var swatch = new Swatch { Color = "#ff0000" };
        PaletteRegistry.Register([new Palette { Swatches = [swatch] }]);
        var fill = new Stroke
        {
            Tool = ToolKind.Fill,
            Color = "#ff0000",
            SwatchId = swatch.Id,
            Points =
            [
                new StrokePoint(20, 20, 1), new StrokePoint(100, 20, 1),
                new StrokePoint(100, 60, 1), new StrokePoint(20, 60, 1),
            ],
            Brush = new BrushSettings { Opacity = 1, AntiAlias = false },
        };

        Assert.Equal(SKColors.Red, Paint(fill));
        swatch.Color = "#00ff00";
        Assert.Equal(SKColors.Lime, Paint(fill));
    }

    [Fact]
    public void ReRegisteringAPaletteReplacesRatherThanIgnores()
    {
        // Unlike clip regions, which are content-hashed and immutable, a
        // swatch is meant to be edited — a registry that kept the first value
        // would make the whole feature a no-op after a reload.
        var first = new Swatch { Id = "sw_shared", Color = "#ff0000" };
        PaletteRegistry.Register([new Palette { Swatches = [first] }]);
        Assert.Equal(SKColors.Red, Paint(Bar("sw_shared", "#000000")));

        var second = new Swatch { Id = "sw_shared", Color = "#0000ff" };
        PaletteRegistry.Register([new Palette { Swatches = [second] }]);
        Assert.Equal(SKColors.Blue, Paint(Bar("sw_shared", "#000000")));
    }
}
