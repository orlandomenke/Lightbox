using Lightbox.Core.Documents;

namespace Lightbox.Core.Timeline;

/// <summary>
/// One drawing to ghost, and how far away it is.
/// </summary>
/// <param name="Steps">
/// 1 for the nearest ghost, 2 for the one past it, and so on — <b>not</b> a
/// frame count. On 2s the nearest previous drawing is two frames back but it
/// is still the first ghost, and it should be the strongest one. Weighting by
/// frame distance instead is why onion skin on 2s looks wrong in tools that
/// get this wrong: every ghost is half as visible as the artist expects.
/// </param>
/// <param name="Before">Earlier in the timeline. Later ghosts take the other tint.</param>
public readonly record struct OnionGhost(Frame Frame, int Steps, bool Before);

/// <summary>
/// Which drawings to ghost around the playhead.
/// </summary>
/// <remarks>
/// Pure, and separate from anything that renders, because the interesting
/// mistakes here are about the exposure sheet rather than about pixels: a
/// hold is not a drawing, the same drawing must not be ghosted twice, and a
/// sequence animated on 3s has its neighbours three frames away.
/// </remarks>
public static class OnionSkin
{
    /// <summary>
    /// The drawings to ghost, nearest first.
    /// </summary>
    /// <param name="keysOnly">
    /// Step by <b>keyed drawings</b> rather than by timeline index. This is
    /// what an artist working on 2s or 3s wants: "the two drawings before
    /// this one", not "the two frames before this one", which on 3s reaches
    /// nothing but holds of the drawing already on screen.
    /// </param>
    public static List<OnionGhost> Ghosts(Layer layer, int index, int before, int after, bool keysOnly)
    {
        var ghosts = new List<OnionGhost>();
        var current = ExposureSheet.ExposedFrame(layer, index);
        // The same drawing never ghosts itself, and never ghosts twice. Both
        // happen constantly on holds, and both look like a rendering bug.
        var seen = new HashSet<string>();
        if (current is not null) seen.Add(current.Id);

        Collect(layer, index, before, keysOnly, forward: false, seen, ghosts);
        Collect(layer, index, after, keysOnly, forward: true, seen, ghosts);
        return ghosts;
    }

    private static void Collect(
        Layer layer, int index, int depth, bool keysOnly, bool forward,
        HashSet<string> seen, List<OnionGhost> ghosts)
    {
        if (depth <= 0) return;
        var step = forward ? 1 : -1;
        var found = 0;

        for (var i = index + step; found < depth && i >= 0 && i < Math.Max(layer.Cels.Count, index + 1); i += step)
        {
            // Keys only reads the cel itself, so a hold contributes nothing
            // and the walk carries on to the next real drawing. Otherwise the
            // exposed frame is what is on screen at that index, which is what
            // "the frame before this one" means to someone watching.
            var frame = keysOnly
                ? ExposureSheet.FrameAtExactIndex(layer, i)
                : ExposureSheet.ExposedFrame(layer, i);
            if (frame is null || !seen.Add(frame.Id)) continue;
            ghosts.Add(new OnionGhost(frame, ++found, !forward));
        }
    }

    /// <summary>
    /// How visible a ghost <paramref name="steps"/> away should be.
    /// </summary>
    /// <param name="falloff">
    /// What each further step is worth, 0–1. At 1 every ghost is equally
    /// visible, which is right when you are checking registration across a
    /// whole sequence; at 0.5 each is half the one before, which is right when
    /// you are drawing an inbetween and only really care about the neighbours.
    /// </param>
    public static double OpacityAt(int steps, double nearest, double falloff)
    {
        var scale = Math.Pow(Math.Clamp(falloff, 0.05, 1.0), Math.Max(0, steps - 1));
        return Math.Clamp(nearest, 0, 1) * scale;
    }
}
