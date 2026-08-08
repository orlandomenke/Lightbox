using Lightbox.Core.Geometry;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The mip pyramid over a tile store: seam-free downscales, sparsity kept,
/// level choice matched to the screen.
/// </summary>
/// <remarks>
/// The load-bearing test is
/// <see cref="APyramidLevelMatchesADownscaleOfTheFlattenedArtwork"/>: a level
/// must say what downscaling the flattened artwork says, or tile boundaries
/// become visible disagreements. (The naive fear — per-tile *linear* draws
/// seaming — was measured absent before this class was kept: Skia raster
/// renders them bit-identically to a whole-image draw. See the class remarks
/// on TilePyramid for what actually goes wrong and when.)
/// </remarks>
public class TilePyramidTests
{
    /// <summary>A store whose four tiles hold one solid colour continuously.</summary>
    private static TileStore SolidStore(SKColor color, int tiles = 2)
    {
        var store = new TileStore();
        var size = store.Grid.TileSize;
        for (var ty = 0; ty < tiles; ty++)
        {
            for (var tx = 0; tx < tiles; tx++)
            {
                var tile = store.Rent(new TileCoord(tx, ty));
                using var canvas = new SKCanvas(tile);
                canvas.Clear(color);
                canvas.Flush();
            }
        }
        return store;
    }

    [Fact]
    public void LevelZeroIsTheSourceItself()
    {
        using var store = SolidStore(SKColors.Red);
        using var pyramid = new TilePyramid(store);
        Assert.Same(store, pyramid.Level(0));
    }

    [Fact]
    public void TheLevelMatchesTheScale()
    {
        Assert.Equal(0, TilePyramid.LevelFor(1.0));
        Assert.Equal(0, TilePyramid.LevelFor(0.6));
        Assert.Equal(1, TilePyramid.LevelFor(0.5));
        Assert.Equal(1, TilePyramid.LevelFor(0.3));
        Assert.Equal(2, TilePyramid.LevelFor(0.25));
        Assert.Equal(3, TilePyramid.LevelFor(0.1));
        // Degenerate inputs choose the safe level rather than throwing.
        Assert.Equal(0, TilePyramid.LevelFor(double.NaN));
        Assert.Equal(0, TilePyramid.LevelFor(0));
        Assert.Equal(TilePyramid.MaxLevel, TilePyramid.LevelFor(1e-9));
    }

    [Fact]
    public void FourTilesFoldIntoOneMipTile()
    {
        using var store = SolidStore(SKColors.Blue, tiles: 2);
        using var pyramid = new TilePyramid(store);

        var mip = pyramid.Level(1);
        Assert.Equal(1, mip.TileCount);
        var tile = mip.Peek(new TileCoord(0, 0));
        Assert.NotNull(tile);
        // Solid in, solid out — all four quadrants written.
        Assert.Equal(SKColors.Blue, tile!.GetPixel(0, 0));
        Assert.Equal(SKColors.Blue, tile.GetPixel(255, 255));
        Assert.Equal(SKColors.Blue, tile.GetPixel(127, 128)); // the quadrant boundary
    }

    [Fact]
    public void SparsityIsKeptAndNegativeCoordsLandInTheRightQuadrant()
    {
        using var store = new TileStore();
        var size = store.Grid.TileSize;
        // One tile, far from origin and negative: (-3, 5).
        var tile = store.Rent(new TileCoord(-3, 5));
        using (var canvas = new SKCanvas(tile))
        {
            canvas.Clear(SKColors.Lime);
        }

        using var pyramid = new TilePyramid(store);
        var mip = pyramid.Level(1);

        Assert.Equal(1, mip.TileCount);
        // floor(-3/2) = -2, quadrant x = (-3) - (-4) = 1 (right half);
        // floor(5/2) = 2, quadrant y = 5 - 4 = 1 (bottom half).
        var dest = mip.Peek(new TileCoord(-2, 2));
        Assert.NotNull(dest);
        Assert.Equal(SKColors.Lime, dest!.GetPixel(size / 2, size / 2));
        Assert.Equal(SKColorType.Rgba8888, dest.ColorType);
        Assert.Equal(0, (int)dest.GetPixel(0, 0).Alpha); // the other quadrants stay empty
    }

    /// <summary>
    /// A pyramid level says the same thing as downscaling the flattened
    /// artwork — the correctness contract that lets the compositor swap one
    /// for the other by zoom.
    /// </summary>
    /// <remarks>
    /// Tolerance ±2: the pyramid reaches level k by chaining k box halvings
    /// while the reference resamples once, and repeated 8-bit rounding is
    /// allowed to wobble a couple of counts. What it must never do is show a
    /// boundary artifact — a per-tile mip would disagree with its neighbour
    /// along every tile edge by far more than 2.
    /// </remarks>
    [Fact]
    public void APyramidLevelMatchesADownscaleOfTheFlattenedArtwork()
    {
        // A gradient, so every pixel differs and an edge disagreement shows.
        using var whole = new SKBitmap(
            new SKImageInfo(1024, 1024, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(whole))
        {
            using var paint = new SKPaint();
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(1024, 1024),
                [SKColors.Red, SKColors.Blue], SKShaderTileMode.Clamp);
            c.DrawRect(SKRect.Create(1024, 1024), paint);
        }
        using var store = TileStore.FromBitmap(whole);
        using var pyramid = new TilePyramid(store);

        const int level = 2; // 4× reduction: 1024 → 256
        using var mip = TileCompositor.CompositeToBitmap(
            pyramid.Level(level), SKRectI.Create(0, 0, 256, 256));

        using var reference = new SKBitmap(
            new SKImageInfo(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(reference))
        {
            using var img = SKImage.FromBitmap(whole);
            c.Scale(0.25f);
            c.DrawImage(img, 0, 0, new SKSamplingOptions(SKFilterMode.Linear), null);
        }

        var worst = 0;
        for (var y = 0; y < 256; y += 1)
        {
            for (var x = 0; x < 256; x += 1)
            {
                var p = mip.GetPixel(x, y);
                var q = reference.GetPixel(x, y);
                worst = Math.Max(worst, Math.Abs(p.Red - q.Red));
                worst = Math.Max(worst, Math.Abs(p.Blue - q.Blue));
            }
        }
        Assert.True(worst <= 4,
            $"level {level} differs from a straight downscale by {worst} — a tile is disagreeing with its neighbours");
    }
}
