using Lightbox.Core.Timeline;

namespace Lightbox.Core.Documents;

/// <summary>
/// Declaring anchors, and positioning them across a range of drawings.
/// </summary>
/// <remarks>
/// P5b. The operations are here rather than on the model so they can be wrapped
/// in one undo step by the editor and tested without one, which is the same shape
/// <see cref="ExposureSheet.ApplyTiming"/> uses.
/// </remarks>
public static class Anchors
{
    /// <summary>Declare a named anchor on the document. Returns the new one.</summary>
    public static Anchor Declare(Scene scene, string name, AnchorKind kind = AnchorKind.Socket)
    {
        var anchor = new Anchor
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Socket" : name.Trim(),
            Kind = kind,
        };
        (scene.Anchors ??= []).Add(anchor);
        return anchor;
    }

    /// <summary>
    /// Remove an anchor and every position of it.
    /// </summary>
    /// <remarks>
    /// Deleting the declaration has to clear the positions too, or the drawings
    /// keep entries nothing can name — invisible, exported, and impossible to
    /// find. Back to null when the last one goes, so removing them all leaves the
    /// document as it was before any existed.
    /// </remarks>
    public static bool Remove(Scene scene, string anchorId)
    {
        if (scene.Anchors?.RemoveAll(a => a.Id == anchorId) is not > 0) return false;
        if (scene.Anchors.Count == 0) scene.Anchors = null;

        foreach (var layer in scene.Layers)
        {
            foreach (var cel in layer.Cels)
            {
                if (cel.Frame?.Anchors is not { } anchors) continue;
                anchors.Remove(anchorId);
                if (anchors.Count == 0) cel.Frame.Anchors = null;
            }
        }
        return true;
    }

    /// <summary>Where an anchor sits on one drawing, or null if it is not placed there.</summary>
    public static AnchorPoint? At(Frame frame, string anchorId) =>
        frame.Anchors is { } anchors && anchors.TryGetValue(anchorId, out var point) ? point : null;

    /// <summary>
    /// Put an anchor at the same place on every drawing a cel range exposes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The multi-frame edit the roadmap asks for, and it works on <b>drawings</b>
    /// rather than cels: a range on 2s holds each drawing twice, and setting the
    /// same anchor twice on one drawing is the same edit done twice. Resolving
    /// through the exposure means a hold is visited once.
    /// </para>
    /// <para>
    /// One position for the whole range rather than an interpolation. Interpolating
    /// a socket across a range is a real feature and a different one — it needs
    /// two authored ends and a curve, and guessing it here would make the simple
    /// case unpredictable.
    /// </para>
    /// </remarks>
    /// <returns>Drawings changed.</returns>
    public static int SetAcross(Layer layer, int start, int count, string anchorId, AnchorPoint point)
    {
        if (layer.Cels.Count == 0 || count < 1) return 0;
        start = Math.Clamp(start, 0, layer.Cels.Count - 1);
        var end = Math.Min(layer.Cels.Count, start + count);

        var touched = new HashSet<string>(StringComparer.Ordinal);
        var changed = 0;
        for (var i = start; i < end; i++)
        {
            // Through the exposure, so a hold sets the drawing it is holding
            // rather than nothing at all.
            if (ExposureSheet.ExposedFrame(layer, i) is not { } frame) continue;
            if (!touched.Add(frame.Id)) continue;

            (frame.Anchors ??= [])[anchorId] = point;
            changed++;
        }
        return changed;
    }

    /// <summary>
    /// Clear an anchor from the drawings a range exposes.
    /// </summary>
    public static int ClearAcross(Layer layer, int start, int count, string anchorId)
    {
        if (layer.Cels.Count == 0 || count < 1) return 0;
        start = Math.Clamp(start, 0, layer.Cels.Count - 1);
        var end = Math.Min(layer.Cels.Count, start + count);

        var touched = new HashSet<string>(StringComparer.Ordinal);
        var changed = 0;
        for (var i = start; i < end; i++)
        {
            if (ExposureSheet.ExposedFrame(layer, i) is not { Anchors: { } anchors } frame) continue;
            if (!touched.Add(frame.Id)) continue;
            if (!anchors.Remove(anchorId)) continue;

            // Back to null when the last one goes, so a drawing that ends up with
            // no anchors writes no key.
            if (anchors.Count == 0) frame.Anchors = null;
            changed++;
        }
        return changed;
    }

    /// <summary>
    /// Every anchor position that applies at a timeline index, resolved through holds.
    /// </summary>
    /// <remarks>
    /// What the exporter asks for, once per exported frame. Resolving through the
    /// exposure matters here for the same reason it does everywhere else: on 2s the
    /// second cel is a hold, and a socket that vanished on every other frame would
    /// read as a rigging bug in the engine.
    /// </remarks>
    public static Dictionary<string, AnchorPoint> ResolvedAt(Scene scene, int index)
    {
        var found = new Dictionary<string, AnchorPoint>(StringComparer.Ordinal);
        if (scene.Anchors is null) return found;

        // Bottom to top, so an anchor placed on an upper layer wins — the same
        // precedence the layer stack already means everywhere else.
        foreach (var layer in scene.Layers)
        {
            if (ExposureSheet.ExposedFrame(layer, index)?.Anchors is not { } anchors) continue;
            foreach (var (id, point) in anchors) found[id] = point;
        }
        return found;
    }
}
