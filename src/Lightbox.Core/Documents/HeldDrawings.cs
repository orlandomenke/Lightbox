namespace Lightbox.Core.Documents;

/// <summary>
/// Keeping a hold a hold: one drawing standing in several cels stays one
/// drawing when the document is read back (B467, Q260).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a hold needs this at all.</b> An effect baked on 2s stores one
/// <see cref="Frame"/> in each pair of cels, so it reads as an animator's hold
/// (<c>SimBakeOps</c>). The file has no way to say "the same object": it writes
/// the drawing out in full at every cel that holds it, and the reader made a
/// new drawing for each — every one with the same id. The hold was gone, and
/// the app, which finds a drawing by its id and takes the first, sent a stroke
/// drawn on the second cel to the first.
/// </para>
/// <para>
/// <b>The owner's decision (Q260): a hold read back is a hold again.</b> Two
/// cels carrying the same id <em>and the same content</em> are the same
/// drawing written twice, so they are joined into one. Two that share an id
/// and differ are two drawings — only a bug or a hand-edited file makes that —
/// and joining them would throw one away, so the later one is kept and given
/// an id of its own. Nothing is ever merged that was not already identical.
/// </para>
/// </remarks>
public static class HeldDrawings
{
    /// <summary>
    /// Join the drawings a file wrote out once per cel back into the one drawing
    /// they were, and give an id of its own to any drawing that only shares an
    /// id with another. Run on every document read from JSON.
    /// </summary>
    public static Doc Relink(Doc doc)
    {
        var byId = new Dictionary<string, Frame>(StringComparer.Ordinal);
        foreach (var layer in doc.Scene.Layers)
        {
            foreach (var cel in layer.Cels)
                if (cel.Frame is { } frame) cel.Frame = Resolve(frame);
            // A mask is a drawing with an id like any other; it can only be
            // joined with something it is identical to, which in practice is
            // never, but it can collide with a cel's id and must not be
            // mistaken for it.
            if (layer.Mask is { } mask) mask.Frame = Resolve(mask.Frame);
        }
        return doc;

        Frame Resolve(Frame frame)
        {
            if (!byId.TryGetValue(frame.Id, out var first))
            {
                byId[frame.Id] = frame;
                return frame;
            }
            if (ReferenceEquals(first, frame)) return frame;
            if (ContentEquality.Same(first, frame)) return first;
            frame.Id = Ids.NewId("f");
            byId[frame.Id] = frame;
            return frame;
        }
    }
}
