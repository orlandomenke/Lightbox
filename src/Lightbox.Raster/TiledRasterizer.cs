using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// strokes[] → tiles. The same pipeline as <see cref="FrameRasterizer"/>, with
/// the pixels landing in a sparse grid instead of one document-sized bitmap.
/// </summary>
/// <remarks>
/// <para>
/// <b>The contract is bit-identity, not speed.</b> Whatever this produces must
/// composite to exactly what <see cref="FrameRasterizer.Rasterize"/> produces —
/// a tiled render is a different *arrangement* of the same work, and the moment
/// it is a different *result* the application has two renderers that will drift.
/// `ATiledRenderIsBitIdenticalToAnUntiledOne` is the guard, and it is the reason
/// this class does almost nothing clever.
/// </para>
/// <para>
/// <b>Translate the surface, never the geometry.</b> A tile is rendered by
/// translating the canvas by minus the tile's origin and stamping the stroke at
/// its ordinary document coordinates. Invariant 7 is why: every dab dynamic is
/// seeded from a dab's document position through <c>Hash01</c>, so subtracting
/// the tile origin from the *points* would re-roll scatter, jitter and rotation
/// per tile — every tile boundary would become a visible seam where the texture
/// changes. The canvas transform moves where the marks land without touching
/// what they are.
/// </para>
/// <para>
/// <b>The document info is passed through unchanged, and that is load-bearing.</b>
/// <c>BrushEngine</c> clamps stroke bounds to <c>[0, info.Width]</c>, so handing
/// it a tile-sized info would clamp every stroke to the tile and silently crop
/// the marks that overhang it — which reads as a render bug rather than as an
/// argument mistake.
/// </para>
/// </remarks>
public static class TiledRasterizer
{
    /// <summary>
    /// Stamp a stroke list into a tile store.
    /// </summary>
    /// <param name="region">
    /// Which part of the document to build, or null for all of it. This is the
    /// hook culling will use; it changes nothing about what a tile contains, only
    /// which tiles are built.
    /// </param>
    /// <remarks>
    /// Only tiles a stroke actually reaches are rented, so an empty region of the
    /// document costs nothing — that is <c>TileStore</c>'s promise carried
    /// through the render rather than restated.
    /// </remarks>
    public static void Rasterize(
        TileStore store,
        IReadOnlyList<Stroke> strokes,
        SKImageInfo info,
        SKRectI? region = null)
    {
        var index = StrokeIndex.Of(strokes, info, store.Grid);
        var area = region ?? SKRectI.Create(0, 0, info.Width, info.Height);
        if (area.Width <= 0 || area.Height <= 0) return;

        var size = store.Grid.TileSize;
        foreach (var coord in store.Grid.Covering(area.Left, area.Top, area.Width, area.Height))
        {
            var (originX, originY) = store.Grid.OriginOf(coord);
            var tileRect = SKRectI.Create(originX, originY, size, size);

            // Which strokes reach this tile, in record order. Empty means the
            // tile stays unallocated, which is the whole economy of the design.
            var reaching = index.Intersecting(tileRect).ToList();
            if (reaching.Count == 0) continue;

            var tile = store.Rent(coord);
            using var canvas = new SKCanvas(tile);
            canvas.Save();
            canvas.Translate(-originX, -originY);
            foreach (var position in reaching)
            {
                BrushEngine.StampStroke(canvas, strokes[position], info, tile);
            }
            canvas.Restore();
            canvas.Flush();
        }
    }

    /// <summary>
    /// Composite a tile store back into one document-sized bitmap.
    /// </summary>
    /// <remarks>
    /// For comparing against the untiled path and for the callers that still want
    /// a whole bitmap. It is not what the compositor will do — that draws the
    /// visible tiles straight onto the canvas surface and never flattens — but a
    /// render that cannot be flattened cannot be proved identical to anything.
    /// </remarks>
    public static SKBitmap Flatten(TileStore store, int width, int height)
    {
        var bitmap = new SKBitmap(
            new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        foreach (var (_, x, y, tile) in store.Intersecting(0, 0, width, height))
        {
            // Src over transparent-black is the tile's own pixels, and the tiles
            // do not overlap — so the order they arrive in cannot matter.
            canvas.DrawBitmap(tile, x, y);
        }
        canvas.Flush();
        return bitmap;
    }
}
