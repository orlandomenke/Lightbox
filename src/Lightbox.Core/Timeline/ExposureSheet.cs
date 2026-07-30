using Lightbox.Core.Documents;

namespace Lightbox.Core.Timeline;

/// <summary>
/// Exposure-sheet semantics: resolving hold cels and locating keyed frames.
/// </summary>
public static class ExposureSheet
{
    /// <summary>
    /// The frame actually shown at timeline index <paramref name="i"/> on a
    /// layer, walking hold cels backwards to the last exposed drawing.
    /// </summary>
    public static Frame? ExposedFrame(Layer layer, int i)
    {
        for (var k = Math.Min(i, layer.Cels.Count - 1); k >= 0; k--)
        {
            var frame = k < layer.Cels.Count ? layer.Cels[k].Frame : null;
            if (frame is not null) return frame;
        }
        return null;
    }

    /// <summary>Only a frame keyed exactly at index i (holds don't count).</summary>
    public static Frame? FrameAtExactIndex(Layer layer, int i)
    {
        if (i < 0 || i >= layer.Cels.Count) return null;
        return layer.Cels[i].Frame;
    }

    /// <summary>Index of the next keyed cel strictly after i, or -1.</summary>
    public static int NextKeyIndex(Layer layer, int i)
    {
        for (var k = i + 1; k < layer.Cels.Count; k++)
        {
            if (layer.Cels[k].Frame is not null) return k;
        }
        return -1;
    }

    /// <summary>Index of the keyed cel at or before i, or -1.</summary>
    public static int KeyIndexAtOrBefore(Layer layer, int i)
    {
        for (var k = Math.Min(i, layer.Cels.Count - 1); k >= 0; k--)
        {
            if (layer.Cels[k].Frame is not null) return k;
        }
        return -1;
    }
}
