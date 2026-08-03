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

    /// <summary>What re-timing a range did, for a caller that has to report it.</summary>
    /// <param name="Drawings">Drawings re-exposed. Zero means the range held none.</param>
    /// <param name="Frames">Cels the range occupies now.</param>
    /// <param name="Grew">
    /// Change in the range's length — positive when the pattern needed more
    /// room than the selection had, negative when it needed less.
    /// </param>
    public readonly record struct TimingChange(int Drawings, int Frames, int Grew);

    /// <summary>
    /// Re-expose the drawings keyed in a range to a timing pattern, in place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Q11's answer, and it <b>never creates or destroys a drawing</b>. It takes
    /// the drawings keyed inside the range, in order, and lays them back down on
    /// the pattern's spacing. The worst it can do to an animator's art is
    /// re-time it, and one undo puts the timing back.
    /// </para>
    /// <para>
    /// <b>The pattern decides the length, not the selection.</b> Twelve drawings
    /// put on 2s occupy twenty-four cels, so the range grows and the rest of the
    /// row moves down; the same twelve put on 1s occupy twelve and it shrinks.
    /// The alternative — hold the selection's length and drop whatever no longer
    /// fits — would silently lose half an artist's drawings when they asked for
    /// "on 2s", which is never what that phrase means. Thinning a range on
    /// purpose is <see cref="DocumentEditor.ReduceToStep"/>, and it is a separate
    /// command precisely because it is destructive.
    /// </para>
    /// <para>
    /// A pattern shorter than the run of drawings repeats, which is what makes
    /// <c>[2]</c> mean "on 2s for everything I selected" rather than "on 2s for
    /// the first drawing".
    /// </para>
    /// <para>
    /// Only cels keyed <em>inside</em> the range are moved. A range that starts
    /// mid-hold does not drag the drawing keyed before it into the selection —
    /// that drawing keeps its key and merely loses the part of its hold that lay
    /// inside the range, which is what re-timing a selection means. Pulling it in
    /// would put one <see cref="Frame"/> in two cels, and editing either would
    /// then edit both.
    /// </para>
    /// </remarks>
    /// <param name="start">First cel of the range, clamped to the layer.</param>
    /// <param name="count">Cels in the range. Below 1 is a no-op.</param>
    public static TimingChange ApplyTiming(Layer layer, int start, int count, TimingPreset preset)
    {
        if (layer.Cels.Count == 0 || count < 1) return default;
        start = Math.Clamp(start, 0, layer.Cels.Count - 1);
        var end = Math.Min(layer.Cels.Count, start + count);
        if (end <= start) return default;

        // Every keyed cel is one drawing. No de-duplicating: if two cels really
        // do reference one frame, laying it twice preserves what was there,
        // where collapsing them would be the one thing this must never do.
        var drawings = new List<Frame>();
        for (var i = start; i < end; i++)
        {
            if (layer.Cels[i].Frame is { } keyed) drawings.Add(keyed);
        }
        if (drawings.Count == 0) return default;

        var rebuilt = new List<Cel>();
        for (var n = 0; n < drawings.Count; n++)
        {
            rebuilt.Add(new Cel { Frame = drawings[n] });
            for (var h = 1; h < preset.HoldFor(n); h++) rebuilt.Add(new Cel());
        }

        var was = end - start;
        layer.Cels.RemoveRange(start, was);
        layer.Cels.InsertRange(start, rebuilt);
        return new TimingChange(drawings.Count, rebuilt.Count, rebuilt.Count - was);
    }
}