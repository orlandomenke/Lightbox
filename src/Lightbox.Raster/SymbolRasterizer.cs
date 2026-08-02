using System.Collections.Concurrent;
using System.Globalization;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// Placements → pixels: the render pass that makes a symbol appear where it
/// was put.
/// </summary>
/// <remarks>
/// <para>
/// A placement is rendered by rasterising the symbol <b>in symbol space</b>
/// and then transforming that image onto the canvas. That ordering is the
/// whole design, and it follows from invariant 2 rather than from convenience.
/// </para>
/// <para>
/// Every dab dynamic — scatter, size, flow, roundness, rotation and all three
/// colour jitters — is seeded by <c>Hash01</c> from the IEEE-754 bits of a dab
/// position. Rewriting a placement's stroke coordinates would therefore re-roll
/// all ten: the sword placed at (100, 40) and the sword placed at (300, 40)
/// would not be the same sword, and two characters carrying one would boil
/// against each other at twelve frames a second. Rasterising once in the
/// symbol's own coordinates and moving the <em>image</em> makes the seed a
/// property of the symbol instead of a property of where somebody dropped it.
/// This is invariant 7 pointed at a second axis, and
/// <see cref="SymbolRenderTests"/> keeps the reason written down.
/// </para>
/// <para>
/// The consequence worth stating plainly: a placement is resampled, not
/// re-rasterised, when it is rotated or squashed. The cache is built at the
/// resolution the placement actually occupies, so a symbol scaled up is sharp
/// rather than magnified; a symbol scaled non-uniformly is sharp along its
/// larger axis. Re-rasterising per placement would be sharper still and would
/// cost the determinism above, which is not a trade this application makes.
/// </para>
/// </remarks>
public static class SymbolRasterizer
{
    /// <summary>
    /// A symbol frame rendered once, and where it sits in symbol space.
    /// </summary>
    /// <param name="Bitmap">Ink only, at <paramref name="Scale"/> pixels per symbol unit.</param>
    /// <param name="Rect">The ink's bounding box, in symbol coordinates.</param>
    private sealed record Rendered(SKBitmap Bitmap, SKRect Rect, double Scale) : IDisposable
    {
        public void Dispose() => Bitmap.Dispose();
    }

    private static readonly ConcurrentDictionary<string, Rendered> Cache = new();

    /// <summary>
    /// How many rendered symbol frames to hold.
    /// </summary>
    /// <remarks>
    /// A cap rather than a byte budget, because the entries are ink-cropped and
    /// a prop is small — the frame cache next door needs a budget because one
    /// of its entries can be a hundred megabytes. What this number is really
    /// protecting against is a cache keyed partly by scale growing an entry per
    /// zoom step; when it is exceeded the whole thing goes, which is a stutter
    /// and never a leak.
    /// </remarks>
    private const int MaxCached = 64;

    /// <summary>Render every placement on a frame, in record order.</summary>
    /// <param name="celIndex">
    /// Where on the timeline this cel sits — what makes a placed cycle advance
    /// with the sequence rather than freezing on one drawing.
    /// </param>
    public static void StampPlacements(
        SKCanvas target, PaintedFrame frame, SKImageInfo info, int celIndex, double outputScale = 1.0)
    {
        if (!frame.HasPlacements) return;
        foreach (var placement in frame.Placements!)
        {
            Stamp(target, placement, info, celIndex, outputScale);
        }
    }

    /// <summary>Render one placement.</summary>
    public static void Stamp(
        SKCanvas target, SymbolPlacement placement, SKImageInfo info, int celIndex, double outputScale = 1.0)
    {
        if (SymbolRegistry.Resolve(placement.SymbolId) is not { } symbol)
        {
            // Nothing in the pixels. A missing asset that renders as a pink box
            // gets exported by somebody in a hurry; the complaint belongs on
            // the registry, where a panel can read it and a render cannot.
            SymbolRegistry.NoteUnresolved(placement.SymbolId);
            return;
        }
        if (symbol.Frames.Count == 0) return;
        if (placement.Opacity <= 0) return;

        var index = placement.FrameIndexAt(celIndex, symbol.FrameCount);
        if (index < 0 || index >= symbol.Frames.Count) return;

        var scale = RenderScale(placement, outputScale);
        if (Resolve(symbol, index, info, scale) is not { } rendered) return;

        target.Save();
        // Device ← document. Materialize hands us a canvas in device pixels
        // with no transform, so the output scale has to be reapplied here or a
        // placement would render at 1× on a 2× surface.
        if (outputScale != 1.0) target.Scale((float)outputScale);

        // Document ← symbol, about the pivot. A hand symbol is placed by the
        // wrist, not by the corner of its bounding box.
        target.Translate((float)placement.X, (float)placement.Y);
        if (placement.Angle != 0) target.RotateDegrees((float)placement.Angle);
        if (placement.ScaleX != 1 || placement.ScaleY != 1)
        {
            target.Scale((float)placement.ScaleX, (float)placement.ScaleY);
        }
        target.Translate(-(float)symbol.PivotX, -(float)symbol.PivotY);

        // Symbol ← the cached image, which covers Rect at Scale px per unit.
        target.Translate(rendered.Rect.Left, rendered.Rect.Top);
        target.Scale((float)(1.0 / rendered.Scale));

        using var paint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(
                (byte)Math.Round(Math.Clamp(placement.Opacity, 0, 1) * 255)),
            // Linear rather than nearest: a placement is an image being moved,
            // and nearest-neighbour on a rotated sword is a staircase.
            IsAntialias = true,
        };
        using var image = SKImage.FromBitmap(rendered.Bitmap);
        target.DrawImage(
            image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        target.Restore();
    }

    /// <summary>
    /// Pixels per symbol unit to rasterise at: enough that the placement is
    /// sharp at the size it lands.
    /// </summary>
    /// <remarks>
    /// Quantised so the cache is keyed by a scale somebody meant rather than by
    /// a float. A slider or a drag produces 1.0000001× as readily as 1×, and an
    /// unrounded key would rasterise the symbol again for each one — a cache
    /// that grows with the number of distinct floats anybody has dragged
    /// through, which is the shape of a leak rather than of a cache.
    /// </remarks>
    private static double RenderScale(SymbolPlacement placement, double outputScale)
    {
        var stretch = Math.Max(Math.Abs(placement.ScaleX), Math.Abs(placement.ScaleY));
        if (stretch <= 0 || double.IsNaN(stretch)) return 0;
        var scale = outputScale * stretch;
        // Clamped so a placement scaled to 400× cannot ask for a bitmap that
        // does not fit in memory, and one scaled to nothing still renders.
        return Math.Round(Math.Clamp(scale, 1.0 / 16, 8), 3);
    }

    private static Rendered? Resolve(Symbol symbol, int index, SKImageInfo info, double scale)
    {
        if (scale <= 0) return null;
        var key = string.Create(
            CultureInfo.InvariantCulture,
            // The scale goes in unformatted on purpose. Rounding it here too
            // would mean two roundings, and the key's would silently cover for
            // RenderScale dropping its own — leaving the cache correct and the
            // reason for it untested.
            $"{symbol.Id}|v{symbol.Version}|f{index}|{info.Width}x{info.Height}@{scale}");
        if (Cache.TryGetValue(key, out var hit)) return hit;

        if (Render(symbol.Frames[index], info, scale) is not { } rendered) return null;
        if (Cache.Count >= MaxCached) ClearCache();
        return Cache.GetOrAdd(key, rendered);
    }

    /// <summary>
    /// Rasterise one of a symbol's frames and crop it to its ink.
    /// </summary>
    /// <remarks>
    /// Rendered at the placing document's size first, and cropped afterwards,
    /// rather than straight into a small surface. The full-size pass is what
    /// lets a smudge or blur stroke inside a symbol read the strokes beneath
    /// it: those brushes sample the target bitmap by document coordinate, so a
    /// pre-cropped surface would have them sampling the wrong place. The crop
    /// is what keeps the cached entry the size of a sword rather than the size
    /// of the canvas.
    /// </remarks>
    private static Rendered? Render(Frame frame, SKImageInfo info, double scale)
    {
        using var full = frame switch
        {
            PaintedFrame p => FrameRasterizer.Materialize(p, info.Width, info.Height, scale),
            VectorFrame v => FrameRasterizer.Rasterize(v.Strokes, info.Width, info.Height, scale),
            _ => null,
        };
        if (full is null) return null;
        if (InkBounds(full) is not { } ink) return null;

        var cropped = new SKBitmap(
            new SKImageInfo(ink.Width, ink.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(cropped))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(full, -ink.Left, -ink.Top);
            canvas.Flush();
        }

        // Back to symbol coordinates: the crop was measured in device pixels of
        // a surface that is `scale` pixels per symbol unit.
        var rect = new SKRect(
            (float)(ink.Left / scale), (float)(ink.Top / scale),
            (float)(ink.Right / scale), (float)(ink.Bottom / scale));
        return new Rendered(cropped, rect, scale);
    }

    /// <summary>The smallest rectangle holding every non-transparent pixel, or null.</summary>
    private static SKRectI? InkBounds(SKBitmap bitmap)
    {
        using var pixmap = bitmap.PeekPixels();
        if (pixmap is null) return null;
        var pixels = pixmap.GetPixelSpan();
        var stride = pixmap.RowBytes;
        int w = bitmap.Width, h = bitmap.Height;

        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (var y = 0; y < h; y++)
        {
            var row = pixels.Slice(y * stride, w * 4);
            for (var x = 0; x < w; x++)
            {
                if (row[x * 4 + 3] == 0) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                maxY = y;
            }
        }
        return maxX < 0 ? null : new SKRectI(minX, minY, maxX + 1, maxY + 1);
    }

    /// <summary>Drop every rendered symbol frame.</summary>
    internal static void ClearCache()
    {
        foreach (var key in Cache.Keys)
        {
            if (Cache.TryRemove(key, out var entry)) entry.Dispose();
        }
    }

    /// <summary>How many rendered symbol frames are held right now.</summary>
    public static int CachedFrames => Cache.Count;
}
