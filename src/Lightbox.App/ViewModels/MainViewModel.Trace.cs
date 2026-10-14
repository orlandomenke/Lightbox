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
        if (frame is null) return null;
        try
        {
            return PngCodec.Decode(frame.PngBase64!);
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            return null;   // pixels that will not read: nothing to preview, and the trace leaves them be
        }
    }

    /// <summary>
    /// Trace <paramref name="layer"/> (the active one when null) onto a new layer
    /// above, and hide it. One undo step. False, with the reason said, when it
    /// holds no pixels or will not take an edit.
    /// </summary>
    /// <remarks>
    /// The tracing — about 0.4 s a 1080p drawing, more for a busy one — runs on
    /// a worker; only putting the result in the document is on the UI thread,
    /// as one undo step. The adversary found the first version tracing every
    /// drawing inside the edit, which froze the window for the whole layer.
    /// </remarks>
    public async Task<bool> TraceLayerAsync(Layer? layer, LineTraceOptions options, CancellationToken cancel = default)
    {
        layer ??= ActiveLayer;
        if (!LayerTrace.HasPixels(layer))
        {
            AiStatus = $"“{layer.Name}” holds no pixels to trace — its drawings are strokes already.";
            return false;
        }
        if (!CanEdit(layer, "trace it")) return false;

        AiStatus = $"Tracing “{layer.Name}”…";
        Dictionary<Frame, List<Stroke>?> prepared;
        try
        {
            prepared = await Task.Run(() => LayerTrace.Prepare(layer, options, cancel), cancel);
        }
        catch (OperationCanceledException)
        {
            AiStatus = "The trace was stopped; nothing changed.";
            return false;
        }
        // Read from the document as it stood; put back only into the same one.
        if (!Scene.Layers.Contains(layer))
        {
            AiStatus = $"“{layer.Name}” went while it was being traced; nothing changed.";
            return false;
        }

        LayerTrace.Result? result = null;
        _editor.Perform(doc => result = LayerTrace.Apply(doc.Scene, layer, prepared), "Trace to strokes");
        if (result is null) return false;

        ActiveLayerIndex = Scene.Layers.FindIndex(l => l.Id == result.Layer.Id);
        var drawings = result.Drawings == 1 ? "1 drawing" : $"{result.Drawings} drawings";
        AiStatus = $"Traced {drawings} into {result.Strokes} strokes on “{result.Layer.Name}”. "
            + $"“{layer.Name}” is hidden, not deleted."
            + (result.Unreadable > 0 ? $" {result.Unreadable} drawing(s) would not read and were left as pixels." : "");
        return true;
    }
}
