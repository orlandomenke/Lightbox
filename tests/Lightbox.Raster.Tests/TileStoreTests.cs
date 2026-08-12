using Lightbox.Core.Geometry;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The tiled backing store, and the claims the tiling design (now
/// <c>docs/DESIGN-tiled-compositor.md</c>) makes about it.
/// </summary>
/// <remarks>
/// The design writes its predictions down "so it can be proved wrong", and two
/// of them are properties of this type rather than of a render:
/// <b>an untouched tile is never allocated</b>, and <b>memory is proportional to
/// ink rather than to area</b>. Everything else here guards the addressing,
/// which is where the arithmetic is easy to get subtly wrong and impossible to
/// see afterwards.
/// </remarks>
public class TileStoreTests(ITestOutputHelper output)
{
    // ---- addressing ----------------------------------------------------------

    /// <summary>
    /// The bug this grid exists to prevent: C# division truncates toward zero, so
    /// a naive <c>x / size</c> puts x = -1 and x = 0 in the same tile.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(255, 0)]
    [InlineData(256, 1)]
    [InlineData(-1, -1)]      // truncation says 0 — the whole point
    [InlineData(-256, -1)]
    [InlineData(-257, -2)]
    [InlineData(-512, -2)]
    public void ATileAddressRoundsTowardNegativeInfinity(int documentX, int expectedTileX)
    {
        var grid = new TileGrid(256);
        Assert.Equal(expectedTileX, grid.TileAt(documentX, 0).X);
    }

    /// <summary>
    /// Left of the origin the tiles must still be a full tile wide and must not
    /// overlap — the symptom of getting this wrong is a seam one pixel from the
    /// origin, visible only once somebody draws leftward.
    /// </summary>
    [Fact]
    public void TilesLeftOfTheOriginAreFullWidthAndDoNotOverlap()
    {
        var grid = new TileGrid(256);
        var seen = new HashSet<TileCoord>();

        for (var x = -512; x < 512; x++)
        {
            var tile = grid.TileAt(x, 0);
            seen.Add(tile);
            var (originX, _) = grid.OriginOf(tile);
            Assert.InRange(x - originX, 0, 255);
        }

        // 1024 contiguous pixels over 256-wide tiles is exactly four tiles. Five
        // would mean an overlap, three would mean a gap.
        Assert.Equal(4, seen.Count);
    }

    [Fact]
    public void ARectangleCoversTheTilesItTouchesAndNoOthers()
    {
        var grid = new TileGrid(256);

        // Straddles the origin in both axes: 2x2 tiles.
        var covering = grid.Covering(-1, -1, 2, 2).ToList();
        Assert.Equal(4, covering.Count);
        Assert.Contains(new TileCoord(-1, -1), covering);
        Assert.Contains(new TileCoord(0, 0), covering);

        // Exactly one tile, aligned: half-open bounds, so 256 wide is one tile
        // and not two. Mixing the conventions gives an empty border column.
        Assert.Single(grid.Covering(0, 0, 256, 256));

        // The count agrees with the walk, since culling tests use the cheap one.
        Assert.Equal(covering.Count, grid.CountCovering(-1, -1, 2, 2));
    }

    [Fact]
    public void AnEmptyRectangleCoversNothing()
    {
        var grid = new TileGrid(256);
        Assert.Empty(grid.Covering(0, 0, 0, 10));
        Assert.Equal(0, grid.CountCovering(0, 0, 10, -1));
    }

    // ---- the design's predictions ---------------------------------------------

    /// <summary>
    /// Design prediction 2, and the claim that makes "infinite" mean anything:
    /// an untouched tile is never allocated.
    /// </summary>
    [Fact]
    public void AnUntouchedTileIsNeverAllocated()
    {
        using var store = new TileStore();

        // Read a million tiles' worth of empty canvas. A store that materialised
        // what it walked across would be allocating 256 GB here.
        var walked = 0;
        for (var y = -500; y < 500; y++)
        {
            for (var x = -500; x < 500; x++)
            {
                Assert.Null(store.Peek(new TileCoord(x, y)));
                Assert.False(store.Has(new TileCoord(x, y)));
                walked++;
            }
        }

        output.WriteLine($"walked {walked} empty tiles, allocated {store.AllocatedBytes} bytes");
        Assert.Equal(0, store.TileCount);
        Assert.Equal(0, store.AllocatedBytes);

        // And the assertion is not vacuous: asking for somewhere to write does allocate.
        store.Rent(new TileCoord(3, -7));
        Assert.Equal(1, store.TileCount);
        Assert.True(store.AllocatedBytes > 0);
    }

    /// <summary>
    /// Design prediction 2 stated as the number that matters: cost follows ink,
    /// not area.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The claim is invariance, not a ratio, and the first cut of this tested
    /// the wrong one.</b> It asserted "three orders smaller than a flat bitmap"
    /// and failed at 544x — a threshold picked by eye, so the test was really
    /// measuring whether the separation I happened to choose was big enough. Any
    /// such ratio can be made to pass by moving the marks further apart, which
    /// means it was never evidence of the property.
    /// </para>
    /// <para>
    /// What "follows ink rather than area" actually says is that moving the marks
    /// apart changes *nothing*. Two marks cost two tiles whether they are 8 K or
    /// 8 million pixels apart; the flat model pays the square of the separation.
    /// That is a property with no tunable in it, and it is the one that makes an
    /// sparse store worth having rather than merely cheaper.
    /// </para>
    /// </remarks>
    [Fact]
    public void MemoryFollowsInkRatherThanArea()
    {
        static long CostOfTwoMarks(int separation)
        {
            using var store = new TileStore();
            store.Rent(store.Grid.TileAt(0, 0));
            store.Rent(store.Grid.TileAt(separation, separation));
            Assert.Equal(2, store.TileCount);
            return store.AllocatedBytes;
        }

        var near = CostOfTwoMarks(8_192);
        var far = CostOfTwoMarks(8_000_000);

        // What a document-sized bitmap over the same extent would have cost.
        var flatNear = 8_448L * 8_448L * 4;
        var flatFar = 8_000_256L * 8_000_256L * 4;

        output.WriteLine(
            $"tiled: {near} B at 8K apart, {far} B at 8M apart — identical: {near == far}");
        output.WriteLine(
            $"flat:  {flatNear / (1024 * 1024)} MB at 8K apart, " +
            $"{flatFar / (1024L * 1024 * 1024)} GB at 8M apart");

        Assert.Equal(near, far);
        Assert.True(far > 0, "two marks must still cost two tiles — this is not a test of nothing");
    }

    [Fact]
    public void RentingTwiceReturnsTheSameTileRatherThanASecondOne()
    {
        using var store = new TileStore();
        var first = store.Rent(new TileCoord(-2, 5));
        var again = store.Rent(new TileCoord(-2, 5));

        Assert.Same(first, again);
        Assert.Equal(1, store.TileCount);
    }

    /// <summary>
    /// A fresh tile has to contribute nothing, because the store returns null for
    /// an absent tile and a blank one for a rented-but-unpainted tile — and those
    /// two must composite identically or that shortcut changes a render.
    /// </summary>
    /// <remarks>
    /// <b>Alpha, not colour, and the first cut of this asserted the wrong one.</b>
    /// <c>SKColors.Transparent</c> is transparent *white* (<c>#00FFFFFF</c>) and a
    /// zeroed bitmap is transparent *black* (<c>#00000000</c>) — the test failed
    /// on a difference that cannot reach a pixel, because SrcOver weights the
    /// source by its alpha and both are zero. What the store actually promises is
    /// "contributes nothing", and alpha is the whole of that promise.
    /// </remarks>
    [Fact]
    public void AFreshTileContributesNothingSoAbsentAndBlankCompositeTheSame()
    {
        using var store = new TileStore(new TileGrid(64));
        var tile = store.Rent(new TileCoord(0, 0));

        Assert.Equal(0, tile.GetPixel(0, 0).Alpha);
        Assert.Equal(0, tile.GetPixel(63, 63).Alpha);
        Assert.Equal(0, tile.GetPixel(31, 47).Alpha);

        // And the equivalence stated directly: drawing a blank tile over a
        // surface leaves it exactly as an absent tile would have.
        var paper = new SKColor(0x20, 0x40, 0x80, 0xFF);
        using var surface = SKSurface.Create(
            new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(paper);
        surface.Canvas.DrawBitmap(tile, 0, 0);
        surface.Canvas.Flush();

        using var after = SKBitmap.FromImage(surface.Snapshot());
        Assert.Equal(paper, after.GetPixel(10, 10));
    }

    // ---- culling, as far as the store is responsible for it --------------------

    [Fact]
    public void IntersectingReturnsOnlyTilesThatExistAndAreInTheRectangle()
    {
        using var store = new TileStore(new TileGrid(256));
        store.Rent(new TileCoord(0, 0));
        store.Rent(new TileCoord(1, 0));
        store.Rent(new TileCoord(40, 40));   // far away — must not come back

        var hits = store.Intersecting(0, 0, 512, 256).ToList();

        Assert.Equal(2, hits.Count);
        Assert.DoesNotContain(hits, h => h.Coord == new TileCoord(40, 40));
        // The origin travels with the tile so callers do not recompute it.
        Assert.Contains(hits, h => h is { X: 256, Y: 0 });
    }

    [Fact]
    public void IntersectingEmptySpaceReturnsNothingAndAllocatesNothing()
    {
        using var store = new TileStore();
        store.Rent(new TileCoord(100, 100));
        var before = store.AllocatedBytes;

        Assert.Empty(store.Intersecting(-5000, -5000, 1000, 1000));
        Assert.Equal(before, store.AllocatedBytes);
    }

    [Fact]
    public void InkBoundsIsNullUntilSomethingIsDrawn()
    {
        using var store = new TileStore();
        Assert.Null(store.InkBounds());

        store.Rent(new TileCoord(-1, -1));
        var bounds = store.InkBounds();
        Assert.NotNull(bounds);
        Assert.Equal((-256, -256, 256, 256), bounds!.Value);
    }

    [Fact]
    public void DroppingATileReleasesItsBytes()
    {
        using var store = new TileStore();
        store.Rent(new TileCoord(0, 0));
        var withOne = store.AllocatedBytes;
        Assert.True(withOne > 0);

        Assert.True(store.Drop(new TileCoord(0, 0)));
        Assert.Equal(0, store.AllocatedBytes);
        Assert.Equal(0, store.TileCount);

        // Dropping what is not there is not an error — absent and empty are the
        // same state, and a caller culling tiles should not have to check first.
        Assert.False(store.Drop(new TileCoord(0, 0)));
    }

    [Fact]
    public void TheTileSizeIsAParameterRatherThanBakedIn()
    {
        // The design leaves 256 vs 512 as a measurement, so the type must not
        // have settled it. Guarding this stops a later constant folding it shut.
        using var small = new TileStore(new TileGrid(64));
        using var large = new TileStore(new TileGrid(512));

        small.Rent(new TileCoord(0, 0));
        large.Rent(new TileCoord(0, 0));

        Assert.Equal(64 * 64 * 4, small.AllocatedBytes);
        Assert.Equal(512 * 512 * 4, large.AllocatedBytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TileGrid(0));
    }
}
