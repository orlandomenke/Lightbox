namespace Lightbox.Core.Documents;

/// <summary>
/// Putting a baked element into the document, and taking it out again.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately knows nothing about solving or drawing — it takes strokes
/// somebody else made and finds them a home — so the whole placement story is
/// testable without a solver, a tracer or a UI.
/// </para>
/// <para>
/// <b>An element owns a layer</b> (Q123), created and named on first bake, so it
/// can be hidden, blended and re-baked as a clean replace without touching
/// anything else.
/// </para>
/// </remarks>
public static class SimBakeOps
{
    /// <summary>
    /// This element's layer, created if it is not there yet.
    /// </summary>
    /// <remarks>
    /// Found by <see cref="Layer.SimId"/> rather than by name, so renaming the
    /// layer — an ordinary thing to do — does not orphan the element and cause
    /// the next bake to make a second one.
    /// </remarks>
    public static Layer LayerFor(Doc doc, SimElement element)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(element);

        foreach (var layer in doc.Scene.Layers)
        {
            if (layer.SimId == element.Id) return layer;
        }

        var made = new Layer
        {
            Name = element.Name ?? element.Kind,
            Kind = LayerKind.Painted,
            SimId = element.Id,
        };
        doc.Scene.Layers.Add(made);
        return made;
    }

    /// <summary>
    /// Take out everything this element baked, and nothing else. Returns how
    /// many strokes went.
    /// </summary>
    /// <remarks>
    /// <b>A stroke an artist has touched no longer carries the id</b> (Q123, and
    /// see <see cref="Detach"/>), so hand work survives a re-bake by
    /// construction rather than by a dialog asking permission to destroy it.
    /// That is why this can be a blunt sweep: everything it removes is
    /// reproducible from the element.
    /// </remarks>
    public static int Clear(Doc doc, SimElement element)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(element);

        var removed = 0;
        var seen = new HashSet<Frame>();

        foreach (var layer in doc.Scene.Layers)
        {
            foreach (var cel in layer.Cels)
            {
                // A hold is one Frame in several cels, so without this the same
                // drawing is swept once per cel and the count lies.
                if (cel.Frame is not { } frame || !seen.Add(frame)) continue;
                removed += frame.Strokes.RemoveAll(s => s.SimId == element.Id);
            }
        }

        return removed;
    }

    /// <summary>
    /// This stroke is the artist's now: it keeps its geometry and loses its
    /// link to the element that made it.
    /// </summary>
    /// <remarks>
    /// <b>The whole of Q123's answer to "what happens to hand edits".</b> Any
    /// path that changes a stroke's geometry, colour or brush calls this, and a
    /// re-bake then leaves the edited stroke alone because it is no longer the
    /// element's. There is nothing to warn about and nothing to merge. The
    /// matching <em>re-attach</em> — handing a stroke back so it regenerates —
    /// is a command in the effects window rather than anything here.
    /// </remarks>
    public static void Detach(Stroke stroke)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        stroke.SimId = null;
    }

    /// <summary>
    /// Replace this element's contribution with a freshly baked one.
    /// </summary>
    /// <param name="baked">
    /// One entry per drawing, at the timeline frame it starts on. Gaps between
    /// them are the element's <see cref="SimElement.ExposeOn"/>, and each drawing
    /// is held across them.
    /// </param>
    public static void Apply(
        Doc doc, SimElement element, IEnumerable<(int Frame, List<Stroke> Strokes)> baked)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(baked);

        Clear(doc, element);

        var layer = LayerFor(doc, element);
        var expose = Math.Max(1, element.ExposeOn);
        var last = element.FirstFrame;

        foreach (var (start, strokes) in baked)
        {
            var end = Math.Min(start + expose, element.FirstFrame + Math.Max(0, element.FrameCount));
            Grow(layer, doc.Scene, end);

            // A hold is one Frame standing in several cels, which is how the
            // timeline already says "held" — so an element exposed on 2s reads
            // as an animator's hold rather than as two identical drawings. It is
            // only available when the cels are otherwise empty: sharing a Frame
            // that carries hand-drawn work would pour one cel's drawing into the
            // next, which is a far worse bug than a duplicated stroke list.
            var shareable = true;
            for (var f = start; f < end && shareable; f++)
            {
                shareable = layer.Cels[f].Frame is null or { Strokes.Count: 0 };
            }

            if (shareable)
            {
                var held = layer.Cels[start].Frame ?? new Frame();
                held.Strokes.AddRange(strokes);
                for (var f = start; f < end; f++) layer.Cels[f].Frame = held;
            }
            else
            {
                for (var f = start; f < end; f++)
                {
                    var frame = layer.Cels[f].Frame ??= new Frame();
                    foreach (var stroke in strokes) frame.Strokes.Add(stroke.Clone());
                }
            }

            last = Math.Max(last, end);
        }

        Grow(layer, doc.Scene, last);
    }

    /// <summary>Make sure the layer — and the scene — reach this far along the timeline.</summary>
    private static void Grow(Layer layer, Scene scene, int frames)
    {
        while (layer.Cels.Count < frames) layer.Cels.Add(new Cel());
        if (scene.FrameCount < frames) scene.FrameCount = frames;
    }
}
