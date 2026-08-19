namespace Lightbox.Core.Documents;

/// <summary>
/// What you can do to a <see cref="SimGroup"/> — all of it by writing the
/// members' own records.
/// </summary>
/// <remarks>
/// <b>Pure document surgery, no rendering and no view model</b>, so the
/// behaviour is testable without a window and reachable from the MCP surface
/// when that arrives. Every method here leaves a document that would bake
/// identically to one an artist had edited element by element — which is the
/// property that lets the bake path stay ignorant of groups entirely.
/// </remarks>
public static class SimGroupOps
{
    /// <summary>Make a group holding these elements, back to front.</summary>
    public static SimGroup Create(Doc doc, string name, IEnumerable<SimElement> members)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(members);

        var group = new SimGroup
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Effect" : name.Trim(),
            ElementIds = members.Select(e => e.Id).ToList(),
        };

        // An element belongs to one group at a time. Joining a second silently
        // would give two groups that each think they can retime it, and the
        // second retime would move it twice.
        foreach (var existing in (doc.SimGroups ??= []).Values)
        {
            existing.ElementIds.RemoveAll(group.ElementIds.Contains);
        }

        doc.SimGroups[group.Id] = group;
        return group;
    }

    /// <summary>The group's members, in drawing order, skipping any that have gone.</summary>
    public static List<SimElement> Members(Doc doc, SimGroup group)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(group);

        var members = new List<SimElement>(group.ElementIds.Count);
        foreach (var id in group.ElementIds)
        {
            if (doc.Sims is { } sims && sims.TryGetValue(id, out var element)) members.Add(element);
        }
        return members;
    }

    /// <summary>Move every member by the same amount, in document pixels.</summary>
    public static void Move(Doc doc, SimGroup group, double dx, double dy)
    {
        foreach (var element in Members(doc, group))
        {
            element.OriginX += dx;
            element.OriginY += dy;
        }
    }

    /// <summary>
    /// Shift every member along the timeline by the same number of frames.
    /// </summary>
    /// <remarks>
    /// <b>Relative offsets between members are the effect's timing</b> — the
    /// smoke starting four frames after the flash is what makes it read as one
    /// event — so every member moves by the same delta rather than being set to
    /// a common frame. Clamped so the earliest member stops at frame zero and
    /// the rest keep their spacing, because a member dragged to a negative
    /// frame would silently stop being drawn.
    /// </remarks>
    public static void Retime(Doc doc, SimGroup group, int deltaFrames)
    {
        var members = Members(doc, group);
        if (members.Count == 0) return;

        var earliest = members.Min(e => e.FirstFrame);
        if (earliest + deltaFrames < 0) deltaFrames = -earliest;

        foreach (var element in members) element.FirstFrame += deltaFrames;
    }

    /// <summary>Where the group starts, which is its earliest member.</summary>
    public static int FirstFrame(Doc doc, SimGroup group)
    {
        var members = Members(doc, group);
        return members.Count == 0 ? 0 : members.Min(e => e.FirstFrame);
    }

    /// <summary>The last frame any member covers, exclusive.</summary>
    public static int EndFrame(Doc doc, SimGroup group)
    {
        var members = Members(doc, group);
        return members.Count == 0 ? 0 : members.Max(e => e.FirstFrame + Math.Max(0, e.FrameCount));
    }

    /// <summary>
    /// Copy the group and everything in it, so the copy can be retuned without
    /// touching the original.
    /// </summary>
    /// <remarks>
    /// Deep by necessity: a shallow copy would be two groups naming one set of
    /// elements, and editing either would edit both — which is the bug an artist
    /// reports as "duplicating my explosion changed the first one".
    /// </remarks>
    public static SimGroup Duplicate(Doc doc, SimGroup group)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(group);

        var copy = new SimGroup { Name = $"{group.Name} copy" };
        foreach (var element in Members(doc, group))
        {
            var made = element.Clone();
            made.Id = Ids.NewId("sim");
            // The copy has baked nothing yet, so it must not claim the
            // original's layer folder and overwrite its drawings on first bake.
            (doc.Sims ??= [])[made.Id] = made;
            copy.ElementIds.Add(made.Id);
        }

        (doc.SimGroups ??= [])[copy.Id] = copy;
        return copy;
    }

    /// <summary>
    /// Take the group apart. Its elements stay exactly where they are.
    /// </summary>
    /// <remarks>
    /// Nothing to un-apply, which is the quiet benefit of a group that carries
    /// no transform: ungrouping is lossless by construction rather than by
    /// bookkeeping.
    /// </remarks>
    public static void Ungroup(Doc doc, SimGroup group)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(group);
        doc.SimGroups?.Remove(group.Id);
    }

    /// <summary>Delete the group and every element in it, drawings included.</summary>
    public static void Delete(Doc doc, SimGroup group)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(group);

        foreach (var element in Members(doc, group))
        {
            SimBakeOps.Clear(doc, element);
            doc.Sims?.Remove(element.Id);
        }
        doc.SimGroups?.Remove(group.Id);
    }

    /// <summary>
    /// Put the group's baked layers in a folder of their own, in the group's own
    /// order, and give the folder the group's name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called after baking rather than during it, so the bake path stays
    /// ignorant of groups. A member that has not been baked has no layer yet and
    /// is skipped rather than having an empty one made for it.
    /// </para>
    /// <para>
    /// <b>Layer folders require their members to be contiguous</b>, so this
    /// reorders the stack as well as tagging it. That is a visible change to a
    /// document and belongs to an explicit action, which is why it is a method
    /// somebody calls rather than something the bake does quietly.
    /// </para>
    /// </remarks>
    public static LayerGroup? Fold(Doc doc, SimGroup group)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(group);

        var layers = new List<Layer>();
        foreach (var id in group.ElementIds)
        {
            foreach (var layer in doc.Scene.Layers)
            {
                if (layer.SimId == id) layers.Add(layer);
            }
        }
        if (layers.Count == 0) return null;

        var folder = group.LayerGroupId is { } known
            ? doc.Scene.LayerGroups.FirstOrDefault(g => g.Id == known)
            : null;
        if (folder is null)
        {
            folder = new LayerGroup { Name = group.Name };
            doc.Scene.LayerGroups.Add(folder);
            group.LayerGroupId = folder.Id;
        }
        folder.Name = group.Name;

        // Contiguous, at the position of the lowest member, in the group's own
        // order — so the drawing order an artist set on the group is the order
        // the layers composite in.
        var at = doc.Scene.Layers.IndexOf(layers[0]);
        foreach (var layer in layers)
        {
            at = Math.Min(at, doc.Scene.Layers.IndexOf(layer));
        }
        foreach (var layer in layers) doc.Scene.Layers.Remove(layer);
        at = Math.Clamp(at, 0, doc.Scene.Layers.Count);
        for (var i = 0; i < layers.Count; i++)
        {
            layers[i].GroupId = folder.Id;
            doc.Scene.Layers.Insert(at + i, layers[i]);
        }

        return folder;
    }
}
