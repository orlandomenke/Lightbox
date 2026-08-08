using Lightbox.Core.Documents;
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
/// <c>ATiledRenderIsBitIdenticalToAnUntiledOne</c> is the guard, and it is the
/// reason this class does almost nothing clever.
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
    /// Whether this stroke list can be rendered tile by tile at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>False when any stroke is an effect brush, and the cause is an
    /// assumption in the engine rather than a property of tiles (B59).</b>
    /// <c>SampleAverage</c> indexes its read bitmap with the dab's *document*
    /// position — <c>Math.Clamp((int)(pos.X + dx * spread), 0, pixels.Width - 1)</c>
    /// — and <c>LerpDab</c> does the same with its device rects. That is correct
    /// exactly while the read bitmap's origin is the document's origin, which was
    /// true of every caller that existed before tiling and is false of a tile.
    /// </para>
    /// <para>
    /// So an effect brush cannot be handed a tile — and it cannot be handed an
    /// offset scratch either. That was tried and measured: rendering the
    /// neighbourhood into a shifted scratch made the result *worse*, 3265 wrong
    /// pixels against 3876 but with the worst alpha delta rising from 191 to a
    /// full 255, because a sample read from the wrong place is further from the
    /// truth than one clamped short.
    /// </para>
    /// <para>
    /// The real fix is to thread a read origin through the effect path so the
    /// engine stops assuming, and it is deliberately not in this change: it edits
    /// the hottest and most invariant-critical file in the repository, and
    /// bundling it with the branch that discovered the assumption would put a
    /// diagnosis and a piece of surgery in one diff.
    /// </para>
    /// </remarks>
    public static bool CanTile(IReadOnlyList<Stroke> strokes)
    {
        foreach (var stroke in strokes)
        {
            if (stroke.Brush.Kind is BrushKind.Smudge or BrushKind.Blur) return false;
        }
        return true;
    }

    /// <summary>
    /// Fill a store with a frame's pixels, tile by tile where that is sound and
    /// through a whole-frame render where it is not.
    /// </summary>
    /// <param name="region">
    /// Which part of the document to build, or null for all of it. The hook
    /// culling will use. Ignored on the whole-frame path, which has to render
    /// everything to be correct — another reason to want B59 fixed properly.
    /// </param>
    public static void Rasterize(
        TileStore store,
        IReadOnlyList<Stroke> strokes,
        SKImageInfo info,
        SKRectI? region = null)
    {
        if (!CanTile(strokes))
        {
            RasterizeWhole(store, strokes, info);
            return;
        }
        RasterizeByTile(store, strokes, info, region);
    }

    /// <summary>The tiled path: each tile replays only the strokes that reach it.</summary>
    private static void RasterizeByTile(
        TileStore store, IReadOnlyList<Stroke> strokes, SKImageInfo info, SKRectI? region)
    {
        var index = StrokeIndex.Of(strokes, info, store.Grid);
        var area = region ?? SKRectI.Create(0, 0, info.Width, info.Height);
        if (area.Width <= 0 || area.Height <= 0) return;

        var size = store.Grid.TileSize;
        foreach (var coord in store.Grid.Covering(area.Left, area.Top, area.Width, area.Height))
        {
            var (originX, originY) = store.Grid.OriginOf(coord);
            var reaching = index.Intersecting(SKRectI.Create(originX, originY, size, size)).ToList();

            // No strokes here, so the tile stays unallocated. That is the whole
            // economy of the design, and it is enforced here rather than trusted.
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
    /// Fill a store from a whole-frame render, for the frames <see cref="CanTile"/>
    /// refuses.
    /// </summary>
    /// <remarks>
    /// Correct by construction — the pixels come from the untiled path, so they
    /// cannot disagree with it — and it costs one document-sized allocation while
    /// it runs. That is the trade this fallback makes: a frame with an effect
    /// brush keeps today's peak memory, and every other frame gets the tiled
    /// profile. Blank tiles are still never stored, so what stays *resident* is
    /// proportional to ink even here.
    /// </remarks>
    public static void RasterizeWhole(
        TileStore store, IReadOnlyList<Stroke> strokes, SKImageInfo info)
    {
        using var whole = FrameRasterizer.Rasterize(strokes, info.Width, info.Height);
        Absorb(store, whole);
    }

    /// <summary>Slice a rendered bitmap into the store, skipping tiles with no ink.</summary>
    private static void Absorb(TileStore store, SKBitmap source)
    {
        var size = store.Grid.TileSize;
        foreach (var coord in store.Grid.Covering(0, 0, source.Width, source.Height))
        {
            var (originX, originY) = store.Grid.OriginOf(coord);
            var w = Math.Min(size, source.Width - originX);
            var h = Math.Min(size, source.Height - originY);
            if (w <= 0 || h <= 0) continue;
            if (!AnyInk(source, originX, originY, w, h)) continue;

            var tile = store.Rent(coord);
            using var canvas = new SKCanvas(tile);
            using var replace = new SKPaint { BlendMode = SKBlendMode.Src };
            canvas.DrawBitmap(
                source,
                SKRect.Create(originX, originY, w, h),
                SKRect.Create(0, 0, w, h),
                replace);
            canvas.Flush();
        }
    }

    /// <summary>Whether any pixel in this rectangle is not fully transparent.</summary>
    private static bool AnyInk(SKBitmap source, int x0, int y0, int w, int h)
    {
        using var pixels = source.PeekPixels();
        if (pixels is null) return true;   // cannot tell, so keep the tile
        var span = pixels.GetPixelSpan();
        var rowBytes = pixels.RowBytes;
        for (var y = y0; y < y0 + h; y++)
        {
            var row = y * rowBytes;
            for (var x = x0; x < x0 + w; x++)
            {
                if (span[row + x * 4 + 3] != 0) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Stamp one committed stroke into the tiles it can reach — the tiled
    /// counterpart of <see cref="FrameRasterizer.Append"/>, and what keeps a
    /// commit proportional to the stroke on a store the way invariant 6 keeps
    /// it proportional on a bitmap.
    /// </summary>
    /// <returns>
    /// False when the stroke cannot be stamped tile-by-tile (an effect brush —
    /// see <see cref="CanTile"/>): the caller must drop the store and rebuild
    /// from the record, because a smudge reads pixels a single tile does not
    /// hold. Nothing is half-stamped on the false path.
    /// </returns>
    /// <remarks>
    /// A tile the bounds reach that does not exist yet is rented fresh, and
    /// fresh means transparent — which is correct, not approximate: earlier
    /// strokes whose bounds never reached that tile left no ink there, and a
    /// tile inside an earlier stroke's bounds already exists (blank if the ink
    /// missed it), because <see cref="Rasterize"/> rents by reach.
    /// </remarks>
    public static bool AppendStroke(TileStore store, Stroke stroke, SKImageInfo info)
    {
        if (!CanTile([stroke])) return false;
        if (BrushEngine.CommitBounds(stroke, info) is not { } bounds) return true;

        var size = store.Grid.TileSize;
        foreach (var coord in store.Grid.Covering(
            bounds.Left, bounds.Top, bounds.Width, bounds.Height))
        {
            var (originX, originY) = store.Grid.OriginOf(coord);
            var tile = store.Rent(coord);
            using var canvas = new SKCanvas(tile);
            canvas.Save();
            canvas.Translate(-originX, -originY);
            BrushEngine.StampStroke(canvas, stroke, info, tile);
            canvas.Restore();
            canvas.Flush();
        }
        return true;
    }

    /// <summary>
    /// Composite a tile store back into one document-sized bitmap.
    /// </summary>
    /// <remarks>
    /// For comparing against the untiled path and for callers that still want a
    /// whole bitmap. It is not what the compositor will do — that draws the
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
