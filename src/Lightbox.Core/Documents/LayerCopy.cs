namespace Lightbox.Core.Documents;

/// <summary>
/// Making a second, independent layer out of one that already exists.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not just <see cref="Layer.Clone"/>.</b> That is the undo path's
/// copy and it carries every id, because a snapshot has to restore the thing that
/// was there. A layer an artist pastes is a <em>new</em> layer sitting beside the
/// original in the same document: reusing the layer, frame or stroke ids would
/// have two layers answering to one id, and frame ids key the render cache.
/// </para>
/// <para>
/// What the copy deliberately does not carry is whatever names another layer or
/// group of the document rather than describing this one: the link it was in
/// (<see cref="Layer.LinkId"/>, because a pasted layer joining the original's link
/// would pose and fade with it), the fluid group it belongs to
/// (<see cref="Layer.SimId"/>), and the background flag. The bone it follows is
/// kept — a copied rigged layer should keep following its rig.
/// </para>
/// </remarks>
public static class LayerCopy
{
    /// <summary>
    /// A copy of <paramref name="source"/> sharing nothing with it, ids included,
    /// padded or trimmed to <paramref name="frameCount"/> cels.
    /// </summary>
    public static Layer Duplicate(Layer source, string name, int frameCount, string? groupId)
    {
        var copy = source.Clone();
        copy.Id = Ids.NewId("layer");
        copy.Name = name;
        copy.GroupId = groupId;
        copy.LinkId = null;
        copy.SimId = null;
        copy.IsBackground = false;
        foreach (var cel in copy.Cels)
        {
            if (cel.Frame is { } frame) Refresh(frame);
        }
        if (copy.Mask is { } mask) Refresh(mask.Frame);

        // A layer is one cel per frame of the scene. The scene may have grown or
        // shrunk between the copy and the paste, and a layer longer than its
        // scene is a state nothing else produces.
        if (copy.Cels.Count > frameCount) copy.Cels.RemoveRange(frameCount, copy.Cels.Count - frameCount);
        while (copy.Cels.Count < frameCount) copy.Cels.Add(new Cel());
        return copy;
    }

    /// <summary>
    /// "Ink copy", or "Ink copy 2" when that is taken, so a run of pastes stays
    /// tellable apart in the docker.
    /// </summary>
    public static string CopyName(string name, IEnumerable<string> existing)
    {
        var taken = new HashSet<string>(existing);
        var candidate = $"{name} copy";
        for (var n = 2; taken.Contains(candidate); n++) candidate = $"{name} copy {n}";
        return candidate;
    }

    private static void Refresh(Frame frame)
    {
        frame.Id = Ids.NewId("f");
        frame.Strokes = frame.Strokes.Select(s => s.Clone()).ToList();
    }
}
