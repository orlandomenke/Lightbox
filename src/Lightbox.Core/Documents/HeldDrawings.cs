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
/// cels of one layer carrying the same id <em>and the same content</em> are the
/// same drawing written twice, so they are joined into one. Anything else that
/// shares an id — a different drawing, a drawing on another layer, a layer's
/// mask — is a different drawing, which only a bug or a hand-edited file makes;
/// joining it would throw one away or tie two layers together, so it is kept
/// and given an id of its own. Nothing is ever merged that was not already
/// identical and already one layer's hold.
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
        // Every id already given to a drawing somewhere in the document.
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layer in doc.Scene.Layers)
        {
            // This layer's drawings by the id the file gave them: the first one
            // and any later ones that had to be renamed, so the rest of a hold
            // that collided with another drawing still joins its own first cel.
            var written = new Dictionary<string, List<Frame>>(StringComparer.Ordinal);
            foreach (var cel in layer.Cels)
            {
                // A file can say "cels": [null]; that is odd, and not this
                // method's to fall over on.
                if (cel?.Frame is not { } frame) continue;
                var id = frame.Id;
                if (written.TryGetValue(id, out var same))
                {
                    // The first drawing that had this id, and the latest: a
                    // hold is a run of cels, so its next cel matches one of
                    // those two. Comparing against every earlier one made a
                    // file of thousands of different drawings under one id —
                    // which only a bug or a hand-edited file makes — quadratic
                    // to open (the adversary: 13.9 s at four thousand).
                    var match = Matching(same[0]) ?? (same.Count > 1 ? Matching(same[^1]) : null);
                    if (match is not null)
                    {
                        cel.Frame = match;
                        continue;
                    }
                }
                else
                {
                    written[id] = same = [];
                }
                if (!taken.Add(id))
                {
                    frame.Id = Ids.NewId("f");
                    taken.Add(frame.Id);
                }
                same.Add(frame);

                Frame? Matching(Frame candidate) =>
                    ReferenceEquals(candidate, frame) || ContentEquality.Same(Unnamed(candidate, id), frame) ? candidate : null;
            }
            // A mask is never part of a hold; it only must not answer to a
            // drawing's id.
            if (layer.Mask?.Frame is { } mask && !taken.Add(mask.Id))
            {
                mask.Id = Ids.NewId("f");
                taken.Add(mask.Id);
            }
        }
        return doc;
    }

    /// <summary>
    /// Compare a renamed drawing as it was written: its content against the
    /// drawing being read, with the id it had in the file.
    /// </summary>
    private static Frame Unnamed(Frame frame, string writtenId)
    {
        if (frame.Id == writtenId) return frame;
        var asWritten = frame.Clone();
        asWritten.Id = writtenId;
        return asWritten;
    }
}
