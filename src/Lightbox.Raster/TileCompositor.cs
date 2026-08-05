using Lightbox.Core.Geometry;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// tiles → screen. The read half of culling: draw the tiles a viewport touches
/// and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is where the design's first prediction is either true or false.</b>
/// <c>docs/DESIGN-infinite-canvas.md</c> measured recompositing at <c>n^1.05</c>
/// in canvas area — 1120 ms at 8K against an 83 ms budget — and named the cause:
/// a repaint is proportional to the document because every layer bitmap *is* the
/// document. <see cref="TileStore"/> removed the allocation; this removes the
/// walk. Cost becomes proportional to the viewport, which is the exponent the
/// drawing path has had all along.
/// </para>
/// <para>
/// <b>Why this is not <see cref="TiledRasterizer.Flatten"/>.</b> Flatten builds
/// one document-sized bitmap so a tiled render can be proved identical to an
/// untiled one, and it is the right tool for that. It is the wrong tool for a
/// repaint: it allocates the very thing tiling exists to avoid, and on an
/// unbounded canvas there is no width to hand it. A compositor draws the visible
/// tiles straight onto the surface and never flattens.
/// </para>
/// <para>
/// <b>Document coordinates in, caller's transform decides where they land.</b>
/// Tiles are drawn at their document origins, so the view transform — zoom, pan,
/// rotation, mirror — stays entirely in the caller's canvas matrix. That is
/// invariant 5 kept by construction rather than by discipline: a compositor that
/// took a zoom factor would be a second place where the view transform lives,
/// and two places that both know about zoom eventually disagree about it.
/// </para>
/// <para>
/// <b>No paint, and that is load-bearing.</b> At an integer offset and 1:1 scale
/// Skia copies a bitmap exactly. Passing a paint with a filter — or drawing at a
/// fractional offset — resamples, and a resampled tile edge is a seam that
/// <c>ATiledRenderIsBitIdenticalToAnUntiledOne</c> would catch while a human
/// eye would not. Tile origins are integer multiples of the tile size, so the
/// exactness is a property of the grid rather than a hope.
/// </para>
/// <para>
/// <b>Nothing here allocates a tile.</b> Compositing goes through
/// <see cref="TileStore.Intersecting"/>, which reads. A viewport panned across
/// blank canvas must not materialise the blankness it crosses, or "infinite"
/// would cost what "very large" costs — see
/// <c>PanningAcrossEmptySpaceAllocatesNothing</c>.
/// </para>
/// </remarks>
public static class TileCompositor
{
    /// <summary>
    /// Draw every existing tile the viewport touches onto <paramref name="target"/>,
    /// at document coordinates. Returns how many tiles were drawn.
    /// </summary>
    /// <param name="viewport">
    /// The visible document rectangle, half-open — <c>[Left, Left+Width)</c> — as
    /// pixel rectangles are everywhere else in the engine. Empty or inverted
    /// yields nothing rather than throwing: a window can legitimately have no
    /// area for a frame during a resize, and a repaint is not the place to fail
    /// over it.
    /// </param>
    /// <returns>
    /// Tiles drawn. Returned rather than discarded so the culling property is
    /// observable without a stopwatch — the same reason
    /// <see cref="TileGrid.CountCovering"/> exists. A timing assertion on a
    /// shared runner measures the runner; a tile count is exact, and
    /// <c>RecompositingCostsWhatIsOnScreenNotWhatExists</c> is the assertion it
    /// makes possible.
    /// </returns>
    /// <remarks>
    /// The target is not cleared. Layers stack, so a compositor that cleared
    /// would erase the layer beneath it — and the caller already knows whether
    /// this is the first pass.
    /// </remarks>
    public static int Composite(SKCanvas target, TileStore store, SKRectI viewport)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(store);

        if (viewport.Width <= 0 || viewport.Height <= 0) return 0;

        var drawn = 0;
        foreach (var (_, x, y, tile) in store.Intersecting(
            viewport.Left, viewport.Top, viewport.Width, viewport.Height))
        {
            // Tiles do not overlap, so src-over onto whatever is there is the
            // tile's own pixels and the order they arrive in cannot matter.
            target.DrawBitmap(tile, x, y);
            drawn++;
        }
        return drawn;
    }

    /// <summary>
    /// What is visible, as a viewport-sized bitmap.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sibling of <see cref="TiledRasterizer.Flatten"/> for a region rather
    /// than a document: it allocates the viewport, never the canvas, so it is
    /// safe on an unbounded document where <c>Flatten</c> has no width to take.
    /// For export of an authored region, and for tests that need to compare a
    /// culled composite against the same rectangle of an untiled render.
    /// </para>
    /// <para>
    /// A tile that straddles the viewport edge is clipped by the bitmap rather
    /// than excluded, which is why the result is exactly the requested rectangle
    /// and not a tile-aligned enlargement of it.
    /// </para>
    /// <para>
    /// Throws on an empty rectangle where <see cref="Composite"/> tolerates one,
    /// and the asymmetry is deliberate: a repaint legitimately runs with no area
    /// mid-resize and must not fail over it, while asking for a bitmap of nothing
    /// is a caller bug — and answering it with a 1×1 would hide that.
    /// </para>
    /// </remarks>
    public static SKBitmap CompositeToBitmap(TileStore store, SKRectI viewport)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(viewport), viewport, "A composited region has to have an area.");
        }

        var bitmap = new SKBitmap(
            new SKImageInfo(viewport.Width, viewport.Height,
                SKColorType.Rgba8888, SKAlphaType.Premul));

        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        // Translate the surface so the viewport's top-left becomes the bitmap's
        // origin. The tiles are still drawn at their document coordinates —
        // moving the geometry instead would be the invariant-7 mistake, one
        // level up from the one TiledRasterizer avoids.
        canvas.Save();
        canvas.Translate(-viewport.Left, -viewport.Top);
        Composite(canvas, store, viewport);
        canvas.Restore();
        canvas.Flush();
        return bitmap;
    }
}
