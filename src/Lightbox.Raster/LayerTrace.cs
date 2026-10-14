using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// Trace a layer's pixels into strokes (Q269): onto a new layer directly
/// above, with the pixel layer kept and hidden, so nothing is lost and the two
/// can be compared.
/// </summary>
/// <remarks>
/// The new layer is the old one as strokes: the same name with "(traced)", the
/// same blend, opacity, clip, mask, effects and folder, the same timing — a cel
/// that held holds, and drawings the exposure sheet shares stay shared. Each
/// drawing's pixels become strokes under the strokes it already had, which are
/// copied across, so the traced layer is the whole drawing and the hidden one
/// is only the way back.
/// </remarks>
public static class LayerTrace
{
    /// <summary>What a trace made, for the status line and the tests.</summary>
    /// <param name="Layer">The new layer, already in the scene directly above.</param>
    /// <param name="Drawings">How many drawings had pixels to trace.</param>
    /// <param name="Strokes">How many strokes the tracing made, across them.</param>
    /// <param name="Unreadable">Drawings whose pixels would not decode, left as they were.</param>
    public sealed record Result(Layer Layer, int Drawings, int Strokes, int Unreadable = 0);

    /// <summary>
    /// The strokes each drawing's pixels trace to — the slow part, safe off the
    /// UI thread: it reads the drawings and writes nothing. A drawing whose
    /// pixels will not decode maps to null and is left alone.
    /// </summary>
    public static Dictionary<Frame, List<Stroke>?> Prepare(
        Layer layer, LineTraceOptions? options = null, CancellationToken cancel = default)
    {
        var traced = new Dictionary<Frame, List<Stroke>?>(ReferenceEqualityComparer.Instance);
        foreach (var frame in layer.Cels.Select(c => c.Frame).OfType<Frame>().Where(f => f.HasBaseline))
        {
            if (traced.ContainsKey(frame)) continue;
            cancel.ThrowIfCancellationRequested();
            traced[frame] = TraceOne(frame, options, cancel);
        }
        return traced;
    }

    private static List<Stroke>? TraceOne(Frame frame, LineTraceOptions? options, CancellationToken cancel)
    {
        SKBitmap? pixels;
        try
        {
            pixels = PngCodec.Decode(frame.PngBase64!);
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            return null;
        }
        if (pixels is null) return null;
        using (pixels) return LineTracer.Trace(pixels, options, cancel);
    }

    /// <summary>Does this layer hold any pixels a trace would read?</summary>
    public static bool HasPixels(Layer layer) =>
        layer.Cels.Any(c => c.Frame is { HasBaseline: true });

    /// <summary>
    /// Trace every drawing on <paramref name="layer"/> that holds pixels, onto
    /// a new layer above it, and hide <paramref name="layer"/>. Null — having
    /// changed nothing — when the layer has no pixels.
    /// </summary>
    public static Result? Trace(Scene scene, Layer layer, LineTraceOptions? options = null) =>
        Apply(scene, layer, Prepare(layer, options));

    /// <summary>
    /// Put what <see cref="Prepare"/> traced onto a new layer above
    /// <paramref name="layer"/>, and hide it. Quick: no tracing happens here.
    /// Null, having changed nothing, when the layer has no pixels.
    /// </summary>
    public static Result? Apply(Scene scene, Layer layer, Dictionary<Frame, List<Stroke>?> prepared)
    {
        var at = scene.Layers.IndexOf(layer);
        if (at < 0 || !HasPixels(layer)) return null;

        // As a duplicate is made (LayerCopy): no second background, no second
        // answer to a simulation, no drawing sharing an id with the original's.
        var traced = LayerCopy.Duplicate(layer, $"{layer.Name} (traced)", layer.Cels.Count, layer.GroupId);
        traced.Visible = true;

        // Drawings the exposure sheet shares between cels are one object, and
        // stay one: copied once, shared the same way.
        var made = new Dictionary<Frame, Frame>(ReferenceEqualityComparer.Instance);
        int drawings = 0, strokes = 0, unreadable = 0;
        for (var cel = 0; cel < layer.Cels.Count; cel++)
        {
            if (layer.Cels[cel].Frame is not { } frame) continue;   // the duplicate holds there too
            if (!made.TryGetValue(frame, out var copy))
            {
                copy = traced.Cels[cel].Frame!;
                if (frame.HasBaseline)
                {
                    if (prepared.TryGetValue(frame, out var lines) && lines is not null)
                    {
                        copy.Strokes.InsertRange(0, lines.Select(s => s.Clone()));
                        copy.PngBase64 = null;
                        copy.Checkpoint = null;
                        drawings++;
                        strokes += lines.Count;
                    }
                    else
                    {
                        unreadable++;   // left as pixels, on both layers
                    }
                }
                made[frame] = copy;
            }
            // The cel keeps whatever else it carried (its Unknown keys among
            // them); only the drawing it points at is the shared copy.
            traced.Cels[cel].Frame = copy;
        }

        layer.Visible = false;
        scene.Layers.Insert(at + 1, traced);
        return new Result(traced, drawings, strokes, unreadable);
    }
}
