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

    /// <summary>
    /// B424: the commit already holds the frame rendered exactly, so the tiles
    /// take the mark's region from that render instead of stamping the stroke
    /// again per tile. Byte-identical to a from-scratch rasterise, and it holds
    /// for an effect brush too — the render is exact for a smudge even though a
    /// tile on its own could never be.
    /// </summary>
    /// <param name="kind">
    /// <c>paint</c>; <c>smudge</c>; <c>mirror</c> — a stroke on the left whose
    /// copy lands on the right, outside the authored mark's rectangle; <c>wrap</c>
    /// — a stroke on the right edge whose copy comes in on the left; <c>gradient</c>
    /// — two points, a whole-layer fill. The last three are the cases
    /// <c>CommitBounds</c> alone left stale in the tiles, on main too.
    /// </param>
    [Theory]
    [InlineData("paint")]
    [InlineData("smudge")]
    [InlineData("mirror")]
    [InlineData("wrap")]
    [InlineData("gradient")]
    public void AppendingFromTheRenderedFrameMatchesRasterisingTheWholeRecord(string kind)
    {
        List<Stroke> strokes =
        [
            Line(40, 100, 900, 120),
            Line(200, 50, 260, 500),
            Line(500, 300, 700, 480),
        ];
        switch (kind)
        {
            case "smudge":
                strokes[2].Brush.Kind = BrushKind.Smudge;
                break;
            case "mirror":
                strokes[2] = Line(100, 300, 200, 320);
                strokes[2].Symmetry = new SymmetryAxis
                {
                    CenterX = Info.Width / 2.0, CenterY = Info.Height / 2.0, AngleDeg = 90, Order = 1, Mirror = true,
                };
                break;
            case "wrap":
                strokes[2] = Line(920, 300, 955, 300);
                strokes[2].Wrap = new TileWrap { Left = 0, Top = 0, Width = Info.Width, Height = Info.Height };
                break;
            case "gradient":
                strokes[2] = Line(100, 300, 300, 300);
                strokes[2].Tool = ToolKind.Gradient;
                break;
        }

        using var appended = new TileStore();
        TiledRasterizer.Rasterize(appended, strokes.GetRange(0, 2), Info);
        using var rendered = FrameRasterizer.Rasterize(strokes.GetRange(0, 2), Info.Width, Info.Height);
        FrameRasterizer.Append(rendered, strokes[2]);
        var region = BrushEngine.CommitRegion(strokes[2], Info);
        Assert.NotNull(region);
        TiledRasterizer.AppendRendered(appended, rendered, region!.Value);

        using var whole = FrameRasterizer.Rasterize(strokes, Info.Width, Info.Height);
        using var flat = TiledRasterizer.Flatten(appended, Info.Width, Info.Height);
        Assert.Equal(0, WorstDifference(flat, whole));
    }

    /// <summary>
    /// B424, the cost half. Stamping a stroke once per tile it reaches made a
    /// pen-up at 300 px cost fourteen stamps of the mark on 1080p: 726 ms for
    /// Ink and 2.4 s for a simulated medium, against 51 and 99 ms for the one
    /// stamp the frame bitmap takes. Measured against the per-tile route in the
    /// same run, so the bar is the broken version rather than a number.
    /// </summary>
    [Fact]
    [Trait("Category", "Performance")]
    public void ACommitStampsTheMarkOnceNotOncePerTile()
    {
        // 960x540 on purpose (a 1080p render in a suite has taken CI down); a
        // 150 px brush across most of the width still covers several tiles.
        var background = Line(40, 400, 900, 420);
        var mark = Line(100, 270, 860, 270);
        mark.Brush.Size = 150;

        double perTile = double.MaxValue, fromRender = double.MaxValue;
        for (var i = 0; i < 3; i++)
        {
            using var a = new TileStore();
            TiledRasterizer.Rasterize(a, [background], Info);
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            Assert.True(TiledRasterizer.AppendStroke(a, mark, Info));
            perTile = Math.Min(perTile, Ms(t0));

            using var b = new TileStore();
            TiledRasterizer.Rasterize(b, [background], Info);
            using var rendered = FrameRasterizer.Rasterize([background], Info.Width, Info.Height);
            var t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            FrameRasterizer.Append(rendered, mark);
            TiledRasterizer.AppendRendered(b, rendered, BrushEngine.CommitRegion(mark, Info)!.Value);
            fromRender = Math.Min(fromRender, Ms(t1));
        }
        output.WriteLine($"per tile {perTile:0.0} ms, once from the render {fromRender:0.0} ms (minimum of 3)");
        // Half, not "less": the render route pays one stamp plus a copy, and a
        // stroke this wide reaches four tiles or more, so a route that merely
        // ties the per-tile one is still stamping per tile somewhere.
        Assert.True(fromRender <= perTile / 2,
            $"appending from the render cost {fromRender:0.0} ms against {perTile:0.0} ms per tile");
    }

    private static double Ms(long since) =>
        (System.Diagnostics.Stopwatch.GetTimestamp() - since) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    private static int WorstDifference(SKBitmap a, SKBitmap b)
    {
        using var pa = a.PeekPixels();
        using var pb = b.PeekPixels();
        var sa = pa.GetPixelSpan();
        var sb = pb.GetPixelSpan();
        var worst = 0;
        for (var i = 0; i < sa.Length; i++) worst = Math.Max(worst, Math.Abs(sa[i] - sb[i]));
        return worst;
    }

    /// <summary>
    /// A mixing brush reads the layer under its dabs at document offsets, the
    /// same assumption B59 measured as wrong per tile, so it takes the
    /// whole-frame route a smudge takes — and a region repaint, and a merge,
    /// refuse it for the same reason (the adversary on Q232).
    /// </summary>
    [Fact]
    public void AMixingStrokeIsNotTiledPerTile()
    {
        var mixing = Line(40, 100, 400, 100);
        mixing.Brush.Mixing = new ColourMixing();
        Assert.False(TiledRasterizer.CanTile([mixing]));
        Assert.True(TiledRasterizer.CanTile([Line(40, 100, 400, 100)]));
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
