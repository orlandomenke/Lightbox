using Lightbox.Core.Geometry;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// Half-resolution mip levels over a <see cref="TileStore"/>, so a zoomed-out
/// composite can draw tiles 1:1 at a nearby scale and resample the whole
/// viewport once — instead of resampling every tile individually.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this fixes was measured before it was built, and it is not what
/// intuition says.</b> The obvious fear — bilinear filtering leaking past
/// tile edges and etching a hairline grid into the artwork — turned out not
/// to exist: Skia's raster backend renders per-tile scaled draws
/// bit-identically to a whole-image draw (measured with a gradient across
/// four tiles at 0.4×, worst channel diff 0). What does exist is a pair of
/// problems at deep zoom-out. Plain linear sampling at 0.125× skips seven of
/// every eight source pixels, so line art shimmers and crawls while zooming;
/// and the per-tile cure, mipmapped sampling, really would seam — each tile's
/// mip chain averages only its own pixels, so adjacent tiles disagree along
/// the boundary about what the half-resolution edge looks like.
/// </para>
/// <para>
/// The pyramid gives mip quality without per-tile mips: pick the level whose
/// resolution is just above what the screen needs, composite those tiles at
/// 1:1 (integer offsets, exact copies), and scale the finished viewport image
/// by the residual ≤2× in one draw. It also bounds the intermediate: a
/// zoomed-out viewport can span arbitrarily many document pixels, and
/// compositing it at level 0 would allocate them all.
/// </para>
/// <para>
/// <b>Each level is seam-free by construction, not by care.</b> A level-k tile
/// is four level-(k−1) tiles, each box-downscaled 2× into its own quadrant.
/// The tile size is even, so a 2× box filter's every 2×2 source block lies
/// wholly inside one source tile and lands wholly inside one quadrant —
/// no sample ever crosses a source-tile edge, so no quadrant boundary can
/// disagree with the pixels an untiled downscale would produce.
/// </para>
/// <para>
/// <b>Sparsity survives.</b> A mip tile is built only where at least one of
/// its four sources exists, so the pyramid of a mostly-empty store is mostly
/// empty, and the whole pyramid costs at most a third of the source
/// (¼ + ¹⁄₁₆ + …) — the geometric series every mip chain pays.
/// </para>
/// <para>
/// Levels build lazily and are immutable once built: the pyramid describes
/// the store as it stood when a level was first asked for. The owner drops
/// the whole pyramid when the source store changes — the same
/// identity-not-subscription contract the compose caches use.
/// </para>
/// </remarks>
public sealed class TilePyramid : IDisposable
{
    /// <summary>
    /// Deeper than any real zoom: level 8 is a 256× reduction, where an 8K
    /// document is thirty pixels wide.
    /// </summary>
    public const int MaxLevel = 8;

    private readonly TileStore _source;
    private readonly TileStore?[] _levels = new TileStore?[MaxLevel + 1];

    /// <param name="source">Level 0. Not owned — the caller keeps disposing it.</param>
    public TilePyramid(TileStore source)
    {
        _source = source;
    }

    /// <summary>
    /// The level whose tiles, drawn 1:1, need only an upscale of at most 2× to
    /// reach <paramref name="scale"/> — i.e. the finest level not finer than
    /// twice what the screen will keep.
    /// </summary>
    public static int LevelFor(double scale)
    {
        if (!double.IsFinite(scale) || scale >= 1 || scale <= 0) return 0;
        return Math.Clamp((int)Math.Floor(Math.Log2(1.0 / scale)), 0, MaxLevel);
    }

    /// <summary>Document pixels per level-k pixel.</summary>
    public static int StepOf(int level) => 1 << level;

    /// <summary>The store at this level, built (and cached) on first use.</summary>
    public TileStore Level(int level)
    {
        if (level <= 0) return _source;
        if (level > MaxLevel) level = MaxLevel;
        return _levels[level] ??= Downscale(Level(level - 1));
    }

    public void Dispose()
    {
        for (var i = 0; i < _levels.Length; i++)
        {
            _levels[i]?.Dispose();
            _levels[i] = null;
        }
    }

    private static TileStore Downscale(TileStore source)
    {
        var store = new TileStore(source.Grid);
        var size = source.Grid.TileSize;
        var half = size / 2;
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);

        foreach (var coord in SourceCoords(source))
        {
            // Which mip tile, and which quadrant of it. Subtracting the doubled
            // floor-half keeps the quadrant in {0,1} for negative coordinates
            // too — (-3) goes to tile -2, quadrant 1.
            var dest = new TileCoord(FloorHalf(coord.X), FloorHalf(coord.Y));
            var qx = (coord.X - dest.X * 2) * half;
            var qy = (coord.Y - dest.Y * 2) * half;

            var tile = store.Rent(dest);
            using var canvas = new SKCanvas(tile);
            var src = source.Peek(coord)!;
            using var image = SKImage.FromBitmap(src);
            canvas.DrawImage(
                image,
                SKRect.Create(0, 0, size, size),
                SKRect.Create(qx, qy, half, half),
                sampling);
            canvas.Flush();
        }
        return store;
    }

    private static IEnumerable<TileCoord> SourceCoords(TileStore source)
    {
        // Enumerate via the ink bounds — the store exposes no key list, and the
        // bounds walk is proportional to the inked region, which is the same
        // proportionality everything else here has.
        if (source.InkBounds() is not { } b) yield break;
        foreach (var (coord, _, _, _) in source.Intersecting(b.X, b.Y, b.Width, b.Height))
        {
            yield return coord;
        }
    }

    private static int FloorHalf(int v) => v >= 0 ? v / 2 : (v - 1) / 2;
}
