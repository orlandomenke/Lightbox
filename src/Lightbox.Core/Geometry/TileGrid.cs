namespace Lightbox.Core.Geometry;

/// <summary>
/// Where a tile sits on the grid. Both components may be negative.
/// </summary>
/// <remarks>
/// Negative addresses exist because ink is not confined to the paper: a
/// stroke dragged past the document's top-left edge still needs a tile to
/// live in, so the grid extends in all four directions rather than two.
/// </remarks>
public readonly record struct TileCoord(int X, int Y);

/// <summary>
/// The mapping between document coordinates and the tiles that hold them.
/// </summary>
/// <remarks>
/// <para>
/// Pure integer arithmetic and no pixels: this is the addressing scheme, and
/// <c>TileStore</c> is what allocates against it. Keeping them apart is what
/// lets the mapping be tested exhaustively without allocating a single bitmap,
/// and it is why this lives in Core, which carries no SkiaSharp.
/// </para>
/// <para>
/// <b>The tile size is a parameter and not a constant on purpose.</b>
/// The design that introduced tiling says 256² or 512² is "to be measured,
/// not guessed", and a constant would quietly settle a question the design left
/// open — the trade is per-tile overhead against wasted area on a partly-covered
/// tile, and it is a measurement rather than an opinion.
/// </para>
/// </remarks>
public sealed class TileGrid
{
    /// <summary>The default until the measurement in the design doc is taken.</summary>
    /// <remarks>
    /// 256 rather than 512 because the failure modes are not symmetric: too small
    /// costs a few more dictionary lookups per composite, too large wastes a
    /// megabyte of zeroes for a stroke that clipped one corner. The first is
    /// measurable and dull, the second is the thing this whole design exists to
    /// stop.
    /// </remarks>
    public const int DefaultTileSize = 256;

    public TileGrid(int tileSize = DefaultTileSize)
    {
        if (tileSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tileSize), tileSize, "A tile has to have an area.");
        }
        TileSize = tileSize;
    }

    public int TileSize { get; }

    /// <summary>
    /// Which tile a document pixel falls in.
    /// </summary>
    /// <remarks>
    /// <b>Floor division, not C# division, and this is the bug this class exists
    /// to prevent.</b> <c>/</c> truncates toward zero, so <c>-1 / 256</c> is
    /// <c>0</c> — which puts the pixel at x = -1 in the same tile as the pixel at
    /// x = 0. Every tile left of the origin would then be half a tile wide and
    /// overlap its neighbour, and the symptom is a seam one pixel from the origin
    /// that only appears once somebody draws to the left of where they started.
    /// </remarks>
    public TileCoord TileAt(int x, int y) => new(FloorDiv(x, TileSize), FloorDiv(y, TileSize));

    /// <summary>The document-space origin of a tile — its top-left pixel.</summary>
    public (int X, int Y) OriginOf(TileCoord tile) => (tile.X * TileSize, tile.Y * TileSize);

    /// <summary>
    /// Every tile a document-space rectangle touches, row by row.
    /// </summary>
    /// <param name="width">Width in pixels. Zero or negative yields nothing.</param>
    /// <remarks>
    /// The rectangle is half-open — <c>[x, x+width)</c> — because that is what a
    /// pixel rectangle means everywhere else in the engine, and mixing the two
    /// conventions is how a render gains a one-tile border that is empty on one
    /// side and clipped on the other.
    /// </remarks>
    public IEnumerable<TileCoord> Covering(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) yield break;

        var first = TileAt(x, y);
        var last = TileAt(x + width - 1, y + height - 1);
        for (var ty = first.Y; ty <= last.Y; ty++)
        {
            for (var tx = first.X; tx <= last.X; tx++)
            {
                yield return new TileCoord(tx, ty);
            }
        }
    }

    /// <summary>
    /// How many tiles a rectangle touches, without walking them.
    /// </summary>
    /// <remarks>
    /// For deciding whether a repaint is worth doing tile-wise at all, and for
    /// asserting in tests that culling looked at what it should have. Counting by
    /// enumerating would make a test of "cost is proportional to the viewport"
    /// pay the cost it is measuring.
    /// </remarks>
    public long CountCovering(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) return 0;
        var first = TileAt(x, y);
        var last = TileAt(x + width - 1, y + height - 1);
        return (long)(last.X - first.X + 1) * (last.Y - first.Y + 1);
    }

    /// <summary>
    /// Integer division that rounds toward negative infinity.
    /// </summary>
    /// <remarks>
    /// Branch rather than <c>Math.Floor</c> on a double: this runs per pixel
    /// rectangle on the composite path, and a double round-trip is both slower
    /// and lossy above 2^53 — technically reachable by a far-flung stroke,
    /// even if an artist never will.
    /// </remarks>
    private static int FloorDiv(int a, int b) => (a >= 0 ? a : a - b + 1) / b;
}
