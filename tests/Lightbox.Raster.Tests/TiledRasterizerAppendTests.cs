using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The tiled counterpart of FrameRasterizer.Append: a commit stamps one
/// stroke into the tiles it reaches, and the store afterwards says exactly
/// what a from-scratch rasterise of the whole record says.
/// </summary>
public class TiledRasterizerAppendTests(ITestOutputHelper output)
{
    private static readonly SKImageInfo Info =
        new(960, 540, SKColorType.Rgba8888, SKAlphaType.Premul);

    private static Stroke Line(double x0, double y0, double x1, double y1) => new()
    {
        Color = "#102030",
        Points = [new StrokePoint(x0, y0, 1), new StrokePoint(x1, y1, 1)],
        Brush = new BrushSettings { Size = 14, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
    };

    [Fact]
    public void AppendingTheLastStrokeMatchesRasterisingTheWholeRecord()
    {
        // Crosses tile boundaries on purpose: 256 and 512 both lie under it.
        List<Stroke> strokes =
        [
            Line(40, 100, 900, 120),
            Line(200, 50, 260, 500),
            Line(500, 300, 700, 480),
        ];

        using var appended = new TileStore();
        TiledRasterizer.Rasterize(appended, strokes.GetRange(0, 2), Info);
        Assert.True(TiledRasterizer.AppendStroke(appended, strokes[2], Info));

        using var whole = new TileStore();
        TiledRasterizer.Rasterize(whole, strokes, Info);

        using var a = TiledRasterizer.Flatten(appended, Info.Width, Info.Height);
        using var b = TiledRasterizer.Flatten(whole, Info.Width, Info.Height);
        var worst = 0;
        for (var y = 0; y < Info.Height; y += 2)
        {
            for (var x = 0; x < Info.Width; x += 2)
            {
                var pa = a.GetPixel(x, y);
                var pb = b.GetPixel(x, y);
                worst = Math.Max(worst, Math.Abs(pa.Red - pb.Red));
                worst = Math.Max(worst, Math.Abs(pa.Alpha - pb.Alpha));
            }
        }
        output.WriteLine($"appended tiles {appended.TileCount}, whole tiles {whole.TileCount}, worst diff {worst}");
        // Identical, not close: both paths run the same StampStroke over the
        // same pixels in the same order — invariant 2 leaves no room to wobble.
        Assert.Equal(0, worst);
        Assert.Equal(whole.TileCount, appended.TileCount);
    }

    [Fact]
    public void AnEffectStrokeRefusesAndStampsNothing()
    {
        using var store = new TileStore();
        TiledRasterizer.Rasterize(store, [Line(40, 100, 400, 100)], Info);
        var before = store.AllocatedBytes;

        var smudge = Line(50, 100, 300, 100);
        smudge.Brush.Kind = BrushKind.Smudge;

        Assert.False(TiledRasterizer.AppendStroke(store, smudge, Info));
        Assert.Equal(before, store.AllocatedBytes);
    }
}
