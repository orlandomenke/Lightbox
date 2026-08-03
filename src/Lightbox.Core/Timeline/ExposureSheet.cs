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

    /// <summary>
    /// Re-expose the drawings in a range to a timing pattern, in place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Q11's answer. It <b>never creates or destroys a drawing</b> — it collects
    /// the drawings the range already exposes, in order, and lays them back down
    /// on the pattern's spacing. That is the property that makes it safe to
    /// reach for: the worst it can do to an animator's art is re-time it, and
    /// one undo puts the timing back.
    /// </para>
    /// <para>
    /// The range keeps its length. A pattern that would run past the end simply
    /// stops, and a pattern that finishes early repeats — which is what makes
    /// <c>[2]</c> mean "on 2s for as far as I dragged". Drawings that no longer
    /// fit are dropped from the exposure rather than deleted: they are still in
    /// the record, still on the frames they were on before this ran, and undo
    /// restores them. Nothing about this call is destructive to a
    /// <see cref="Frame"/>.
    /// </para>
    /// <para>
    /// Returns how many drawings ended up exposed, so a caller can say
    /// "9 drawings over 18 cels" rather than reporting nothing.
    /// </para>
    /// </remarks>
    /// <param name="start">First cel of the range, clamped to the layer.</param>
    /// <param name="count">Cels in the range. Below 1 is a no-op.</param>
    public static int ApplyTiming(Layer layer, int start, int count, TimingPreset preset)
    {
        if (layer.Cels.Count == 0 || count < 1) return 0;
        start = Math.Clamp(start, 0, layer.Cels.Count - 1);
        var end = Math.Min(layer.Cels.Count, start + count);
        if (end <= start) return 0;

        // The drawings the range shows now, in the order it shows them. The
        // first cel resolves through a hold, because a range that begins
        // mid-hold is showing a drawing and has to keep showing it — starting
        // from nothing would blank the front of the range.
        var drawings = new List<Frame>();
        if (ExposedFrame(layer, start) is { } leading) drawings.Add(leading);
        for (var i = start; i < end; i++)
        {
            if (layer.Cels[i].Frame is { } keyed && (drawings.Count == 0 || !ReferenceEquals(drawings[^1], keyed)))
            {
                drawings.Add(keyed);
            }
        }
        if (drawings.Count == 0) return 0;

        for (var i = start; i < end; i++) layer.Cels[i].Frame = null;

        var at = start;
        var n = 0;
        while (at < end && n < drawings.Count)
        {
            layer.Cels[at].Frame = drawings[n];
            at += preset.HoldFor(n);
            n++;
        }
        return n;
    }
}