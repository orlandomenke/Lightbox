using Lightbox.Core.Geometry;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// Layer pixels held as a sparse grid of tiles rather than as one bitmap the
/// size of the document.
/// </summary>
/// <remarks>
/// <para>
/// <b>Built so a frame can cost its ink rather than its paper.</b> (It was
/// designed as the precondition for an unbounded canvas — since removed — and
/// playback is what kept it.) Measured on the sweep the design was written from,
/// <c>Recomposite the whole frame, by canvas size</c> runs at <c>n^1.05</c> —
/// linear in canvas *area* — reaching 1120 ms and 380 MB of cache for one frame
/// of a three-layer scene at 8K, against a 83 ms budget. The cause is not slow
/// blending: it is that every layer allocates a bitmap the size of the document,
/// so cost is set by how big the canvas is rather than by how much of it holds
/// ink or is on screen. Tiles are what let cost follow the ink, which is why
/// tiling comes first and culling comes second.
/// </para>
/// <para>
/// <b>The load-bearing claim is that an untouched tile is never allocated.</b>
/// Memory becomes proportional to ink rather than to area, and an empty region
/// costs nothing. Every method here is written so that reading a tile that has
/// never been drawn to allocates nothing and returns nothing; only an explicit
/// request for somewhere to *write* creates pixels.
/// </para>
/// <para>
/// <b>Not a renderer.</b> This owns storage and lifetime. What draws into a tile,
/// which tiles a viewport touches, and which strokes reach a tile are three
/// separate questions with their own pieces — deliberately, because bundling
/// them is how a storage bug becomes indistinguishable from a culling bug.
/// </para>
/// </remarks>
public sealed class TileStore : IDisposable
{
    private readonly Dictionary<TileCoord, SKBitmap> _tiles = [];

    public TileStore(TileGrid? grid = null)
    {
        Grid = grid ?? new TileGrid();
    }

    public TileGrid Grid { get; }

    /// <summary>Tiles that hold pixels. An untouched region contributes nothing.</summary>
    public int TileCount => _tiles.Count;

    /// <summary>
    /// What the pixels cost, in bytes.
    /// </summary>
    /// <remarks>
    /// The gauge the design's second prediction is judged on — memory
    /// proportional to ink, not to area. Reported the same way
    /// <c>FrameBitmapCache.CachedBytes</c> is, so the two can be read against
    /// each other during the migration.
    /// </remarks>
    public long AllocatedBytes { get; private set; }

    /// <summary>
    /// The tile at these coordinates, or null when nothing has been drawn there.
    /// </summary>
    /// <remarks>
    /// <b>Never allocates.</b> This is the read path, and the whole point of the
    /// design is that reading empty space is free — a compositor walking a
    /// viewport over blank canvas must not materialise the blankness it walks
    /// across, or sparse would cost the same as dense.
    /// </remarks>
    public SKBitmap? Peek(TileCoord tile) => _tiles.GetValueOrDefault(tile);

    /// <summary>Whether anything has ever been drawn into this tile.</summary>
    public bool Has(TileCoord tile) => _tiles.ContainsKey(tile);

    /// <summary>
    /// The tile at these coordinates, created transparent if it does not exist.
    /// </summary>
    /// <remarks>
    /// The one method that allocates, and it is named for it. A caller that does
    /// not intend to write wants <see cref="Peek"/>; the split is what keeps
    /// "reading empty space is free" a property of the type rather than a
    /// discipline callers have to remember.
    /// </remarks>
    public SKBitmap Rent(TileCoord tile)
    {
        if (_tiles.TryGetValue(tile, out var existing)) return existing;

        var size = Grid.TileSize;
        var bitmap = new SKBitmap(
            new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        // Skia zeroes a fresh bitmap, and transparent-black is the identity for
        // SrcOver — so an allocated-but-unpainted tile composites to exactly what
        // an absent one does. That equivalence is what lets Peek return null
        // instead of a blank tile without changing any render.
        _tiles[tile] = bitmap;
        AllocatedBytes += (long)bitmap.RowBytes * bitmap.Height;
        return bitmap;
    }

    /// <summary>
    /// Every tile that exists and intersects this document-space rectangle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The read half of culling: the caller says what is visible and gets back
    /// only tiles that both exist and are on screen. Cost is proportional to the
    /// rectangle rather than to the store, which is the first design prediction
    /// stated as a method signature.
    /// </para>
    /// <para>
    /// The origin travels with the tile because the caller needs to know where to
    /// draw it, and recomputing that from the coordinate at every call site is
    /// how a compositor and an exporter end up disagreeing by one tile.
    /// </para>
    /// </remarks>
    public IEnumerable<(TileCoord Coord, int X, int Y, SKBitmap Bitmap)> Intersecting(
        int x, int y, int width, int height)
    {
        foreach (var coord in Grid.Covering(x, y, width, height))
        {
            if (_tiles.TryGetValue(coord, out var bitmap))
            {
                var (ox, oy) = Grid.OriginOf(coord);
                yield return (coord, ox, oy, bitmap);
            }
        }
    }

    /// <summary>
    /// The document-space rectangle that encloses every tile holding pixels, or
    /// null when nothing has been drawn.
    /// </summary>
    /// <remarks>
    /// Tile-granular by construction, so it is the bounds of the tiles that hold
    /// ink rather than of the ink itself. That is the honest thing for it to be:
    /// a tighter answer would have to inspect pixels, and the two have different
    /// uses — this one sizes a walk, and export bounds (Q20, the export-bounds
    /// one — there are two) are authored rather
    /// than derived precisely because a derived bound moves when a stray mark
    /// lands.
    /// </remarks>
    public (int X, int Y, int Width, int Height)? InkBounds()
    {
        if (_tiles.Count == 0) return null;

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var coord in _tiles.Keys)
        {
            if (coord.X < minX) minX = coord.X;
            if (coord.Y < minY) minY = coord.Y;
            if (coord.X > maxX) maxX = coord.X;
            if (coord.Y > maxY) maxY = coord.Y;
        }

        var size = Grid.TileSize;
        return (minX * size, minY * size, (maxX - minX + 1) * size, (maxY - minY + 1) * size);
    }

    /// <summary>Drop a tile's pixels. Absent and empty are the same thing here.</summary>
    public bool Drop(TileCoord tile)
    {
        if (!_tiles.Remove(tile, out var bitmap)) return false;
        AllocatedBytes -= (long)bitmap.RowBytes * bitmap.Height;
        bitmap.Dispose();
        return true;
    }

    /// <summary>
    /// Convert a full-document bitmap to a TileStore, splitting into tile-sized chunks.
    /// Used when transitioning from full-canvas rendering to tiled rendering.
    /// This is a functional but not yet optimized conversion — ideally strokes would render
    /// directly to tiles to avoid allocating the full document bitmap in the first place.
    /// </summary>
    public static TileStore FromBitmap(SKBitmap bitmap, TileGrid? grid = null)
    {
        var store = new TileStore(grid);
        var tileSize = store.Grid.TileSize;

        // Determine which tiles this bitmap covers
        var tilesAcross = (bitmap.Width + tileSize - 1) / tileSize;
        var tilesDown = (bitmap.Height + tileSize - 1) / tileSize;

        for (int ty = 0; ty < tilesDown; ty++)
        {
            for (int tx = 0; tx < tilesAcross; tx++)
            {
                var coord = new TileCoord(tx, ty);
                var srcX = tx * tileSize;
                var srcY = ty * tileSize;
                var copyWidth = Math.Min(tileSize, bitmap.Width - srcX);
                var copyHeight = Math.Min(tileSize, bitmap.Height - srcY);

                if (copyWidth > 0 && copyHeight > 0)
                {
                    var tile = store.Rent(coord);

                    // Copy region using canvas to avoid per-pixel overhead
                    // Extract the source region and draw into the tile
                    using var canvas = new SKCanvas(tile);
                    var srcRect = new SKRect(srcX, srcY, srcX + copyWidth, srcY + copyHeight);
                    var destRect = new SKRect(0, 0, copyWidth, copyHeight);

                    canvas.DrawBitmap(bitmap, srcRect, destRect);
                    canvas.Flush();

                    // A tile that came out blank goes straight back: keeping it
                    // would make memory proportional to area again, which is
                    // the one number this type exists to change. The scan costs
                    // one pass over pixels that were just written and hot.
                    if (IsBlank(tile)) store.Drop(coord);
                }
            }
        }

        return store;
    }

    private static bool IsBlank(SKBitmap tile)
    {
        var span = tile.GetPixelSpan();
        return !span.ContainsAnyExcept((byte)0);
    }

    public void Dispose()
    {
        foreach (var bitmap in _tiles.Values) bitmap.Dispose();
        _tiles.Clear();
        AllocatedBytes = 0;
    }
}
