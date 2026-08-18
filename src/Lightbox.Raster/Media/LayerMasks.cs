using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster.Media;

/// <summary>
/// Resolves a painted mask layer into per-cell coverage for one element.
/// </summary>
/// <remarks>
/// <para>
/// <b>Downsampled by rendering smaller, never by scaling the strokes</b>
/// (invariant 7). The layer is rasterised through the ordinary
/// <see cref="FrameRasterizer"/> at an output scale of <c>1/Scale</c>, so the
/// marks keep their own dab seeding and simply land on a coarser surface. Scaling
/// the geometry instead would re-roll every dab dynamic and give a mask that
/// disagreed with the drawing an artist is looking at.
/// </para>
/// <para>
/// <b>Cached per layer and frame</b>, because a hold means the same cel is asked
/// for on several frames and several emitters may read one layer. Cleared with
/// the instance, which lives for one bake.
/// </para>
/// </remarks>
public sealed class LayerMasks : ISimMasks
{
    private readonly Doc _doc;
    private readonly SimElement _element;
    private readonly Dictionary<(string Layer, int Frame), float[]?> _cache = [];

    public LayerMasks(Doc doc, SimElement element)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _element = element ?? throw new ArgumentNullException(nameof(element));
    }

    public float[]? For(Emitter emitter, int frame)
    {
        ArgumentNullException.ThrowIfNull(emitter);
        return emitter.MaskLayerId is { } id ? Coverage(id, frame) : null;
    }

    /// <summary>The obstacle layer's coverage on a frame, or null when none is named.</summary>
    public float[]? Obstacle(int frame) =>
        _element.ObstacleLayerId is { } id ? Coverage(id, frame) : null;

    private float[]? Coverage(string layerId, int frame)
    {
        if (_cache.TryGetValue((layerId, frame), out var cached)) return cached;

        var made = Rasterize(layerId, frame);
        _cache[(layerId, frame)] = made;
        return made;
    }

    private float[]? Rasterize(string layerId, int frame)
    {
        var layer = _doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId);

        // A mask layer that has been deleted leaves its emitter silent rather
        // than throwing. Losing a layer should cost the effect, not the document.
        if (layer is null || frame < 0 || frame >= layer.Cels.Count) return null;
        if (layer.Cels[frame].Frame is not { Strokes.Count: > 0 } cel) return null;

        int w = Math.Max(1, _element.GridWidth), h = Math.Max(1, _element.GridHeight);
        var scale = _element.Scale > 0 ? _element.Scale : 1;

        using var bitmap = FrameRasterizer.Rasterize(
            cel.Strokes,
            (int)Math.Round(w * scale),
            (int)Math.Round(h * scale),
            outputScale: 1 / scale,
            origin: new SKPointI((int)Math.Round(_element.OriginX), (int)Math.Round(_element.OriginY)));

        using var pixmap = bitmap.PeekPixels();
        if (pixmap is null || pixmap.ColorType != SKColorType.Rgba8888) return null;

        var bytes = pixmap.GetPixelSpan();
        var rowBytes = pixmap.RowBytes;
        var coverage = new float[w * h];
        var any = false;

        var rows = Math.Min(h, pixmap.Height);
        var cols = Math.Min(w, pixmap.Width);
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < cols; x++)
            {
                var alpha = bytes[y * rowBytes + x * 4 + 3] / 255f;
                if (alpha <= 0f) continue;
                coverage[y * w + x] = alpha;
                any = true;
            }
        }

        // A blank cel and a missing layer mean the same thing to an emitter, so
        // they are reported the same way rather than as an array of zeroes that
        // every caller then has to check.
        return any ? coverage : null;
    }
}
