using Lightbox.Core.Documents;

namespace Lightbox.Core.Cleanup;

/// <summary>
/// The hand-drawn frames a guided clean reads its references from: the
/// nearest key or breakdown before a drawing on its layer, and the nearest
/// after it.
/// </summary>
/// <remarks>
/// Inbetweens are passed over in both directions: a reference is what the
/// artist drew, and an inbetween — generated, or marked as one — is what the
/// cleaner is there to bring in line with it. The drawing's own role does not
/// matter; a key cleaned toward its neighbours is the artist's choice to make.
/// </remarks>
public static class CleanupReferences
{
    /// <summary>The strokes of the extremes either side of cel <paramref name="index"/>; empty when there are none.</summary>
    public static List<IReadOnlyList<Stroke>> Around(Layer layer, int index)
    {
        var found = new List<IReadOnlyList<Stroke>>();
        if (index < 0) return found;
        if (Extreme(layer, index - 1, -1) is { } before) found.Add(before.Strokes);
        if (Extreme(layer, index + 1, +1) is { } after) found.Add(after.Strokes);
        return found;
    }

    private static Frame? Extreme(Layer layer, int from, int step)
    {
        for (var i = from; i >= 0 && i < layer.Cels.Count; i += step)
        {
            if (layer.Cels[i].Frame is { Role: not FrameRole.Inbetween } frame) return frame;
        }
        return null;
    }
}
