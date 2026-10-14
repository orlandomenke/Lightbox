using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>
/// Trace to strokes (Q269): a layer's pixels traced into brush strokes on a
/// new layer above, the pixel layer kept and hidden.
/// </summary>
/// <remarks>
/// The window asks how (<c>TraceDialog</c>); this is the part that does it, in
/// one undo step, and the two questions the window asks first — is there
/// anything to trace, and what does the drawing at the playhead look like.
/// </remarks>
public partial class MainViewModel
{
    /// <summary>How many drawings on the layer hold pixels a trace would read (shared drawings once).</summary>
    public int DrawingsToTrace(Layer? layer)
    {
        layer ??= ActiveLayer;
        return layer.Cels.Select(c => c.Frame).OfType<Frame>().Where(f => f.HasBaseline)
            .Distinct(ReferenceEqualityComparer.Instance).Count();
    }

    /// <summary>
    /// The pixels to preview a trace on: the drawing at the playhead when it
    /// has some, otherwise the layer's first that does. The caller disposes it.
    /// </summary>
    public SKBitmap? TraceSampleFor(Layer? layer)
    {
        layer ??= ActiveLayer;
        var key = ExposureSheet.KeyIndexAtOrBefore(layer, CurrentFrameIndex);
        var frame = key >= 0 && layer.Cels[key].Frame is { HasBaseline: true } here
            ? here
            : layer.Cels.Select(c => c.Frame).OfType<Frame>().FirstOrDefault(f => f.HasBaseline);
        return frame is null ? null : PngCodec.Decode(frame.PngBase64!);
    }

    /// <summary>
    /// Trace <paramref name="layer"/> (the active one when null) onto a new layer
    /// above, and hide it. One undo step. False, with the reason said, when it
    /// holds no pixels or will not take an edit.
    /// </summary>
    public bool TraceLayer(Layer? layer, LineTraceOptions options)
    {
        layer ??= ActiveLayer;
        if (!LayerTrace.HasPixels(layer))
        {
            AiStatus = $"“{layer.Name}” holds no pixels to trace — its drawings are strokes already.";
            return false;
        }
        if (!CanEdit(layer, "trace it")) return false;

        LayerTrace.Result? result = null;
        var layerId = layer.Id;
        _editor.Perform(doc =>
        {
            if (doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId) is { } inDoc)
                result = LayerTrace.Trace(doc.Scene, inDoc, options);
        });
        if (result is null) return false;

        ActiveLayerIndex = Scene.Layers.FindIndex(l => l.Id == result.Layer.Id);
        var drawings = result.Drawings == 1 ? "1 drawing" : $"{result.Drawings} drawings";
        AiStatus = $"Traced {drawings} into {result.Strokes} strokes on “{result.Layer.Name}”. "
            + $"“{layer.Name}” is hidden, not deleted.";
        return true;
    }
}
