using System.Security.Cryptography;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Compositing costs what is on screen rather than what exists — the first
/// prediction in <c>docs/DESIGN-infinite-canvas.md</c>, stated so it can fail.
/// </summary>
/// <remarks>
/// <para>
/// The design measured recompositing at <c>n^1.05</c> in canvas area against an
/// <c>n^0.22</c> drawing path, and named the cause: a repaint is proportional to
/// the document because every layer bitmap *is* the document.
/// <c>TileStore</c> removed the allocation and <c>TileCompositor</c> removes the
/// walk. This is where that is checked.
/// </para>
/// <para>
/// <b>Counted, not timed.</b> Every assertion here is on the number of tiles
/// drawn. A stopwatch on a shared runner measures the runner — and worse, it
/// would pass on a compositor that walked every tile in the store quickly, which
/// is exactly the bug that matters on an unbounded canvas where "every tile" has
/// no upper bound. `TileCompositor.Composite` returns its count so the property
/// can be asserted exactly rather than inferred from a duration.
/// </para>
/// <para>
/// <b>The strongest form holds the viewport still and grows the store.</b>
/// Asserting that a small viewport draws few tiles is weaker than it looks — it
/// passes on a compositor that happens to be cheap at that size. Asserting that
/// the same viewport draws the *same* tiles when ninety-six more exist elsewhere
/// is the claim the design actually made.
/// </para>
/// </remarks>
[Collection("Registries")]
public class TileCullingTests(ITestOutputHelper output)
{
    private const int Tile = 256;
    private const int W = 900, H = 700;
    private static readonly SKImageInfo Info = new(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);

    private static string Fingerprint(SKBitmap bitmap) =>
        Convert.ToHexString(SHA256.HashData(bitmap.GetPixelSpan()));

    /// <summary>
    /// A store with pixels in exactly the tiles named, and nowhere else.
    /// </summary>
    /// <remarks>
    /// Built with <c>Rent</c> rather than by rendering, so the counts under test
    /// are a property of the compositor and not of where a brush happened to put
    /// ink. The bit-identity tests below use a real render; these use a known
    /// grid, because a culling bug and a rasterising bug should not be able to
    /// hide behind each other.
    /// </remarks>
    private static TileStore StoreWith(params TileCoord[] tiles)
    {
        var store = new TileStore(new TileGrid(Tile));
        foreach (var tile in tiles) store.Rent(tile);
        return store;
    }

    private static TileCoord[] Block(int x0, int y0, int across, int down)
    {
        var tiles = new List<TileCoord>();
        for (var y = y0; y < y0 + down; y++)
        {
            for (var x = x0; x < x0 + across; x++) tiles.Add(new TileCoord(x, y));
        }
        return [.. tiles];
    }

    private static int CompositeCount(TileStore store, SKRectI viewport)
    {
        using var bitmap = new SKBitmap(
            new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        return TileCompositor.Composite(canvas, store, viewport);
    }

    /// <summary>
    /// The anchor. One viewport, two stores differing only in what exists
    /// off-screen, and the same amount of work.
    /// </summary>
    [Fact]
    public void RecompositingCostsWhatIsOnScreenNotWhatExists()
    {
        // Exactly the four tiles a 512x512 viewport at the origin covers.
        var viewport = SKRectI.Create(0, 0, 2 * Tile, 2 * Tile);

        using var small = StoreWith(Block(0, 0, 2, 2));
        using var large = StoreWith(Block(0, 0, 10, 10));

        var drawnSmall = CompositeCount(small, viewport);
        var drawnLarge = CompositeCount(large, viewport);

        output.WriteLine(
            $"store {small.TileCount} tiles → drew {drawnSmall}; "
            + $"store {large.TileCount} tiles → drew {drawnLarge}");

        Assert.Equal(4, drawnSmall);

        // The claim: twenty-five times the ink off-screen, identical work.
        Assert.Equal(drawnSmall, drawnLarge);
        Assert.Equal(100, large.TileCount);
    }

    /// <summary>
    /// Cost tracks the viewport when the store is held still — the other half of
    /// the same claim, and the one that would catch a compositor ignoring its
    /// rectangle altogether.
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 4)]
    [InlineData(3, 9)]
    public void AWiderViewportDrawsMoreTilesInProportion(int tilesAcross, int expected)
    {
        using var store = StoreWith(Block(0, 0, 10, 10));

        var drawn = CompositeCount(store, SKRectI.Create(0, 0, tilesAcross * Tile, tilesAcross * Tile));

        Assert.Equal(expected, drawn);
    }

    /// <summary>
    /// Reading blank canvas must not create it. The property that makes
    /// "infinite" cost less than "very large".
    /// </summary>
    [Fact]
    public void PanningAcrossEmptySpaceAllocatesNothing()
    {
        using var store = StoreWith(Block(0, 0, 2, 2));
        var before = store.AllocatedBytes;

        // Ten screens away, where nothing has ever been drawn.
        var drawn = CompositeCount(store, SKRectI.Create(40 * Tile, 40 * Tile, 4 * Tile, 4 * Tile));

        Assert.Equal(0, drawn);
        Assert.Equal(4, store.TileCount);
        Assert.Equal(before, store.AllocatedBytes);
    }

    /// <summary>
    /// The canvas is unbounded in four directions, not two, so a viewport
    /// straddling the origin has to work.
    /// </summary>
    /// <remarks>
    /// The trap is floor division: <c>-1 / 256</c> is 0 in C#, which would put
    /// the pixel left of the origin in the origin's own tile. <c>TileGrid</c>
    /// guards the arithmetic; this guards that the compositor goes through it
    /// rather than doing its own.
    /// </remarks>
    [Fact]
    public void AViewportStraddlingTheOriginDrawsTilesOnBothSides()
    {
        // The four tiles meeting at (0,0): (-1,-1), (0,-1), (-1,0), (0,0).
        using var store = StoreWith(Block(-1, -1, 2, 2));

        var drawn = CompositeCount(store, SKRectI.Create(-Tile, -Tile, 2 * Tile, 2 * Tile));

        Assert.Equal(4, drawn);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 100)]
    [InlineData(-50, 100)]
    public void AViewportWithNoAreaDrawsNothing(int width, int height)
    {
        using var store = StoreWith(Block(0, 0, 4, 4));

        Assert.Equal(0, CompositeCount(store, SKRectI.Create(0, 0, width, height)));
    }

    /// <summary>
    /// Asking for a bitmap of nothing is refused, where compositing nothing is
    /// merely a no-op.
    /// </summary>
    /// <remarks>
    /// The asymmetry between the two entry points is deliberate — a repaint runs
    /// with no area mid-resize and must not fail, while a zero-area bitmap
    /// request is a caller bug that a 1×1 of transparency would hide. Pinned
    /// because it was not: deleting the guard left all the other tests here
    /// green, so the contract was documented and unguarded, which is the state
    /// this file exists to prevent elsewhere.
    /// </remarks>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-50, 100)]
    public void AskingForABitmapWithNoAreaIsRefused(int width, int height)
    {
        using var store = StoreWith(Block(0, 0, 2, 2));

        var error = Assert.Throws<ArgumentOutOfRangeException>(
            () => TileCompositor.CompositeToBitmap(store, SKRectI.Create(0, 0, width, height)));

        Assert.Equal("viewport", error.ParamName);
    }

    /// <summary>
    /// Culling must not change a pixel: a composited region is bit-identical to
    /// the same rectangle of an untiled render.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ATiledRenderIsBitIdenticalToAnUntiledOne</c> proves that for a whole
    /// frame through <c>Flatten</c>. This proves it for a *region* through the
    /// compositor, which is a different code path and the one a repaint uses.
    /// The rectangle is deliberately not tile-aligned, so a tile clipped by the
    /// viewport edge is exercised rather than avoided.
    /// </para>
    /// <para>
    /// Fingerprints rather than tolerances, for the reason
    /// <c>RuntimeDeterminismTests</c> gives: "near enough" cannot tell a
    /// resampled tile edge from a stroke landing in the wrong tile.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(300, 200, 400, 300)]
    [InlineData(0, 0, 900, 700)]
    [InlineData(137, 91, 233, 187)]
    public void ACulledCompositeMatchesTheSameRectangleOfAnUntiledRender(
        int x, int y, int width, int height)
    {
        var strokes = Drawing();
        var region = SKRectI.Create(x, y, width, height);

        using var untiled = FrameRasterizer.Rasterize(strokes, W, H);
        using var expected = Crop(untiled, region);

        using var store = new TileStore(new TileGrid(Tile));
        TiledRasterizer.Rasterize(store, strokes, Info);
        using var actual = TileCompositor.CompositeToBitmap(store, region);

        var a = Fingerprint(expected);
        var b = Fingerprint(actual);
        output.WriteLine($"cropped {a[..16]}…  composited {b[..16]}…");

        Assert.Equal(a, b);
    }

    /// <summary>The same rectangle of a bitmap, by integer translation — exact.</summary>
    private static SKBitmap Crop(SKBitmap source, SKRectI region)
    {
        var bitmap = new SKBitmap(
            new SKImageInfo(region.Width, region.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(-region.Left, -region.Top);
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();
        return bitmap;
    }

    /// <summary>Everything stochastic on, so a re-seed cannot hide.</summary>
    private static BrushSettings Jittery() => new()
    {
        Size = 26, Hardness = 0.7, Opacity = 1, Flow = 0.9, Spacing = 0.15,
        AntiAlias = true, Scatter = 0.4, SizeJitter = 0.5, MinimumDiameter = 0.3,
        FlowJitter = 0.4, RoundnessJitter = 0.3, RotationJitter = 0.5,
        ColorJitter = 0.4, SecondaryColor = "#c04020",
        HueJitter = 0.3, SaturationJitter = 0.3, BrightnessJitter = 0.3,
    };

    /// <summary>Strokes that cross every tile seam in a 900x700 document.</summary>
    private static List<Stroke> Drawing()
    {
        var strokes = new List<Stroke>();
        for (var i = 0; i < 12; i++)
        {
            var y = 40 + i * 52;
            strokes.Add(new Stroke
            {
                Tool = ToolKind.Brush,
                Color = i % 2 == 0 ? "#2050b0" : "#b03020",
                Brush = Jittery(),
                Points = Enumerable.Range(0, 20)
                    .Select(k => new StrokePoint(20 + k * 44.0, y + (k % 3) * 9.0, 0.4 + (k % 5) * 0.15))
                    .ToList(),
            });
        }
        return strokes;
    }
}
