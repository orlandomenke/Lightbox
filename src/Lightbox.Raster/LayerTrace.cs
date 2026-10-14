using Lightbox.Core.Documents;

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
    public sealed record Result(Layer Layer, int Drawings, int Strokes);

    /// <summary>Does this layer hold any pixels a trace would read?</summary>
    public static bool HasPixels(Layer layer) =>
        layer.Cels.Any(c => c.Frame is { HasBaseline: true });

    /// <summary>
    /// Trace every drawing on <paramref name="layer"/> that holds pixels, onto
    /// a new layer above it, and hide <paramref name="layer"/>. Null — having
    /// changed nothing — when the layer has no pixels.
    /// </summary>
    public static Result? Trace(Scene scene, Layer layer, LineTraceOptions? options = null)
    {
        var at = scene.Layers.IndexOf(layer);
        if (at < 0 || !HasPixels(layer)) return null;

        var traced = layer.Clone();
        traced.Id = new Layer().Id;
        traced.Name = $"{layer.Name} (traced)";
        traced.Visible = true;
        traced.LinkId = null;

        // Drawings the exposure sheet shares between cels are one object, and
        // stay one: traced once, shared the same way.
        var made = new Dictionary<Frame, Frame>(ReferenceEqualityComparer.Instance);
        var drawings = 0;
        var strokes = 0;
        for (var cel = 0; cel < layer.Cels.Count; cel++)
        {
            if (layer.Cels[cel].Frame is not { } frame)
            {
                traced.Cels[cel] = new Cel();
                continue;
            }
            if (!made.TryGetValue(frame, out var copy))
            {
                copy = frame.Clone();
                copy.Id = new Frame().Id;
                copy.Checkpoint = null;
                copy.Strokes = [.. frame.Strokes.Select(s => s.Clone())];
                if (frame.HasBaseline)
                {
                    using var pixels = PngCodec.Decode(frame.PngBase64!);
                    var lines = LineTracer.Trace(pixels, options);
                    copy.Strokes.InsertRange(0, lines);
                    copy.PngBase64 = null;
                    drawings++;
                    strokes += lines.Count;
                }
                made[frame] = copy;
            }
            traced.Cels[cel] = new Cel { Frame = copy };
        }

        layer.Visible = false;
        scene.Layers.Insert(at + 1, traced);
        return new Result(traced, drawings, strokes);
    }
}
