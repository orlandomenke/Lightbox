namespace Lightbox.Core.Documents;

/// <summary>One line of the layer docker: a layer or a folder, at a depth.</summary>
/// <param name="Item">A <see cref="Layer"/> or a <see cref="LayerGroup"/>.</param>
/// <param name="Depth">How many folders it is inside.</param>
/// <param name="Hidden">Inside a collapsed folder: in the stack, not on the docker.</param>
/// <param name="Continued">
/// A folder's header shown a second time, because something outside it sits
/// between two of its layers. Only a file written before folders kept
/// themselves together (or an edit that bypassed the docker) does that; the
/// docker says so rather than reordering layers on load.
/// </param>
public readonly record struct StackRow(object Item, int Depth, bool Hidden, bool Continued);

/// <summary>
/// The folder tree over <see cref="Scene.Layers"/>: which folders a layer is
/// inside, where a folder sits, and the docker's rows (Q204).
/// </summary>
/// <remarks>
/// <para>
/// <b>Layers stay one flat list.</b> Compositing, the X-sheet and some forty
/// other paths read and reorder <see cref="Scene.Layers"/> directly, so the
/// tree is derived from it rather than stored beside it: a layer names its
/// folder (<see cref="Layer.GroupId"/>), a folder names its parent
/// (<see cref="LayerGroup.ParentId"/>), and a folder holding layers sits where
/// they are. A second stored order would have to be kept in step by every one
/// of those paths.
/// </para>
/// <para>
/// <b>Only an empty folder needs a slot of its own</b>, and
/// <see cref="LayerGroup.Under"/> is it: the layer directly above, or null for
/// the top of its parent. <see cref="Settle"/> keeps it pointing at the same
/// place across any edit, so no operation outside the docker has to know
/// empty folders exist.
/// </para>
/// <para>
/// <b>Nothing here may hang or lose a folder on a bad file.</b> A parent chain
/// that loops is cut where it repeats, a parent that does not exist is the top
/// level, and a folder the walk never reached is still listed, at the bottom.
/// The owner's complaint that started this was a folder vanishing; a corrupt
/// file must not be a second way to get it.
/// </para>
/// </remarks>
public static class FolderTree
{
    /// <summary>A folder by id, or null.</summary>
    public static LayerGroup? Folder(Scene scene, string? id) =>
        id is null ? null : scene.LayerGroups.FirstOrDefault(g => g.Id == id);

    /// <summary>The folders containing <paramref name="folder"/>, innermost first.</summary>
    public static List<LayerGroup> Ancestors(Scene scene, LayerGroup folder)
    {
        var chain = new List<LayerGroup>();
        var seen = new HashSet<string> { folder.Id };
        for (var p = Folder(scene, folder.ParentId); p is not null && seen.Add(p.Id); p = Folder(scene, p.ParentId))
        {
            chain.Add(p);
        }
        return chain;
    }

    /// <summary>The folder directly containing <paramref name="folder"/>, as the tree resolves it.</summary>
    public static LayerGroup? ParentOf(Scene scene, LayerGroup folder) =>
        Folder(scene, folder.ParentId) is { } p && p.Id != folder.Id && !Ancestors(scene, p).Contains(folder)
            ? p
            : null;

    /// <summary>The folders a layer is inside, innermost first; empty for a loose layer.</summary>
    public static List<LayerGroup> FoldersOf(Scene scene, Layer layer)
    {
        if (Folder(scene, layer.GroupId) is not { } own) return [];
        var chain = new List<LayerGroup> { own };
        chain.AddRange(Ancestors(scene, own));
        return chain;
    }

    /// <summary>The innermost locked folder a layer is inside, or null — the one to name when an edit is refused.</summary>
    public static LayerGroup? LockedFolderOf(Scene scene, Layer layer) =>
        layer.GroupId is null ? null : FoldersOf(scene, layer).FirstOrDefault(f => f.Locked);

    /// <summary>Whether <paramref name="inner"/> is <paramref name="outer"/> or inside it at any depth.</summary>
    public static bool IsWithin(Scene scene, LayerGroup inner, LayerGroup outer) =>
        inner.Id == outer.Id || Ancestors(scene, inner).Any(a => a.Id == outer.Id);

    /// <summary>Whether a layer is inside <paramref name="folder"/> at any depth.</summary>
    public static bool IsWithin(Scene scene, Layer layer, LayerGroup folder) =>
        FoldersOf(scene, layer).Any(f => f.Id == folder.Id);

    /// <summary>Every layer inside <paramref name="folder"/> at any depth, bottom first.</summary>
    public static List<Layer> SubtreeLayers(Scene scene, LayerGroup folder) =>
        scene.Layers.Where(l => IsWithin(scene, l, folder)).ToList();

    /// <summary>Every folder inside <paramref name="folder"/> at any depth, not counting itself.</summary>
    public static List<LayerGroup> SubtreeFolders(Scene scene, LayerGroup folder) =>
        scene.LayerGroups.Where(g => g.Id != folder.Id && IsWithin(scene, g, folder)).ToList();

    /// <summary>The ids of every folder holding at least one layer, at any depth.</summary>
    public static HashSet<string> HoldingLayers(Scene scene)
    {
        var holding = new HashSet<string>();
        if (scene.LayerGroups.Count == 0) return holding;
        foreach (var layer in scene.Layers)
        {
            if (layer.GroupId is null) continue;
            foreach (var f in FoldersOf(scene, layer)) holding.Add(f.Id);
        }
        return holding;
    }

    /// <summary>
    /// The layer an empty folder sits directly under, if its
    /// <see cref="LayerGroup.Under"/> still names one inside the folder's
    /// parent — otherwise null, which is the top of the parent.
    /// </summary>
    public static Layer? AnchorOf(Scene scene, LayerGroup folder)
    {
        if (folder.Under is not { } id || scene.Layers.FirstOrDefault(l => l.Id == id) is not { } layer) return null;
        return ParentOf(scene, folder) is { } parent && !IsWithin(scene, layer, parent) ? null : layer;
    }

    /// <summary>The docker's rows, topmost first.</summary>
    public static List<StackRow> Rows(Scene scene)
    {
        var layers = scene.Layers;
        var rows = new List<StackRow>(layers.Count + scene.LayerGroups.Count);
        if (scene.LayerGroups.Count == 0)
        {
            for (var i = layers.Count - 1; i >= 0; i--) rows.Add(new StackRow(layers[i], 0, false, false));
            return rows;
        }

        var holding = HoldingLayers(scene);
        var order = scene.LayerGroups.Select((g, i) => (g.Id, i)).ToDictionary(p => p.Id, p => p.i);
        // Empty folders, each with the parent and the anchor the tree resolves.
        var empties = scene.LayerGroups
            .Where(g => !holding.Contains(g.Id))
            .Select(g => (Folder: g, Parent: ParentOf(scene, g), Anchor: AnchorOf(scene, g)))
            .ToList();
        var emitted = new HashSet<string>();
        var open = new List<LayerGroup>(); // outermost first

        bool HiddenNow() => open.Any(f => f.Collapsed);

        void EmitEmpty(LayerGroup folder, int depth, bool hidden)
        {
            if (!emitted.Add(folder.Id)) return;
            rows.Add(new StackRow(folder, depth, hidden, false));
            // Inside an empty folder there is nothing to anchor to: every child
            // is at its top, later in the list higher, as siblings are.
            foreach (var child in empties.Where(e => e.Parent?.Id == folder.Id).OrderByDescending(e => order[e.Folder.Id]))
            {
                EmitEmpty(child.Folder, depth + 1, hidden || folder.Collapsed);
            }
        }

        void EmitTopOf(LayerGroup? parent)
        {
            foreach (var e in empties
                         .Where(e => e.Parent?.Id == parent?.Id && e.Anchor is null)
                         .OrderByDescending(e => order[e.Folder.Id]))
            {
                EmitEmpty(e.Folder, open.Count, HiddenNow());
            }
        }

        void Open(LayerGroup folder)
        {
            var again = !emitted.Add(folder.Id);
            rows.Add(new StackRow(folder, open.Count, HiddenNow(), again));
            open.Add(folder);
            if (!again) EmitTopOf(folder);
        }

        EmitTopOf(null);
        Layer? above = null;
        for (var i = layers.Count - 1; i >= -1; i--)
        {
            if (above is not null)
            {
                // Empty folders in the slot directly under the layer just
                // emitted — the deepest parent first, so each closes only the
                // folders it is outside of.
                foreach (var e in empties
                             .Where(e => e.Anchor?.Id == above.Id)
                             .OrderByDescending(e => e.Parent is null ? 0 : open.IndexOf(e.Parent) + 1)
                             .ThenByDescending(e => order[e.Folder.Id]))
                {
                    var depth = e.Parent is null ? 0 : open.IndexOf(e.Parent) + 1;
                    if (depth < 0 || depth > open.Count) continue; // cannot happen for a resolved anchor
                    open.RemoveRange(depth, open.Count - depth);
                    EmitEmpty(e.Folder, depth, HiddenNow());
                }
            }
            if (i < 0) break;

            var layer = layers[i];
            var path = FoldersOf(scene, layer);
            path.Reverse(); // outermost first
            var common = 0;
            while (common < open.Count && common < path.Count && open[common].Id == path[common].Id) common++;
            open.RemoveRange(common, open.Count - common);
            for (var k = common; k < path.Count; k++) Open(path[k]);
            rows.Add(new StackRow(layer, open.Count, HiddenNow(), false));
            above = layer;
        }

        // The safety net: a folder no walk reached is still a folder.
        foreach (var g in scene.LayerGroups.Where(g => !emitted.Contains(g.Id)).ToList())
        {
            EmitEmpty(g, 0, false);
        }
        return rows;
    }

    /// <summary>
    /// Keep every empty folder where it was across an edit: clear the slot of
    /// a folder that now holds layers, and re-pin one whose layers just left
    /// it or whose anchor moved or went away.
    /// </summary>
    /// <param name="after">The scene the edit produced; changed in place.</param>
    /// <param name="before">The scene before the edit (the undo snapshot's).</param>
    /// <remarks>
    /// <para>
    /// <b>Run after every snapshot edit</b> (<c>DocumentEditor.Perform</c>), so
    /// that a merge, a delete, an AI edit or a script reordering layers can
    /// leave an empty folder exactly where the artist put it without knowing
    /// such a thing exists. A scene with no folders returns at once.
    /// </para>
    /// <para>
    /// An edit that moved a folder on purpose changes its
    /// <see cref="LayerGroup.ParentId"/> or <see cref="LayerGroup.Under"/>, and
    /// that is respected — only checked that it still points somewhere.
    /// </para>
    /// </remarks>
    public static void Settle(Scene after, Scene? before)
    {
        if (after.LayerGroups.Count == 0) return;
        var holding = HoldingLayers(after);
        var beforeHolding = before is null ? null : HoldingLayers(before);
        foreach (var folder in after.LayerGroups)
        {
            if (holding.Contains(folder.Id))
            {
                folder.Under = null;
                continue;
            }
            var old = before is null ? null : Folder(before, folder.Id);
            if (old is null || old.ParentId != folder.ParentId || old.Under != folder.Under && !beforeHolding!.Contains(old.Id))
            {
                // New, or moved on purpose: take its slot as given.
                folder.Under = AnchorOf(after, folder)?.Id;
                continue;
            }

            // The slot it had before, as the layers either side of it then.
            string? aboveId, belowId;
            var was = before!.Layers;
            if (beforeHolding!.Contains(old.Id))
            {
                var top = was.FindLastIndex(l => IsWithin(before, l, old));
                var bottom = was.FindIndex(l => IsWithin(before, l, old));
                aboveId = top + 1 < was.Count ? was[top + 1].Id : null;
                belowId = bottom > 0 ? was[bottom - 1].Id : null;
            }
            else if (AnchorOf(before, old) is { } anchor)
            {
                var at = was.IndexOf(anchor);
                aboveId = anchor.Id;
                belowId = at > 0 ? was[at - 1].Id : null;
            }
            else
            {
                folder.Under = null; // the top of its parent stays the top of its parent
                continue;
            }

            var now = after.Layers;
            var aboveAt = aboveId is null ? -1 : now.FindIndex(l => l.Id == aboveId);
            var belowAt = belowId is null ? -1 : now.FindIndex(l => l.Id == belowId);
            string? pin;
            if (aboveAt >= 0 && (belowId is null ? aboveAt == 0 : belowAt == aboveAt - 1))
            {
                pin = aboveId; // its neighbours are still neighbours
            }
            else if (belowAt >= 0)
            {
                pin = belowAt + 1 < now.Count ? now[belowAt + 1].Id : null; // stay on the layer it sat on
            }
            else
            {
                pin = aboveAt >= 0 ? aboveId : null;
            }
            folder.Under = pin;
            folder.Under = AnchorOf(after, folder)?.Id; // still inside its parent, or the top of it
        }
    }

    // ---- operations ------------------------------------------------------------------
    //
    // Each takes ids rather than objects, so the view model can run it on a
    // clone first — to refuse with a reason, or to see that it changes nothing
    // and record no step — and then on the document inside one undo step.

    /// <summary>The bottommost and topmost index of a folder's layers, or null when it holds none.</summary>
    private static (int Bottom, int Top)? Span(Scene scene, LayerGroup folder)
    {
        var bottom = scene.Layers.FindIndex(l => IsWithin(scene, l, folder));
        if (bottom < 0) return null;
        return (bottom, scene.Layers.FindLastIndex(l => IsWithin(scene, l, folder)));
    }

    /// <summary>Where a layer inserted "at the top of <paramref name="folder"/>" lands in <see cref="Scene.Layers"/>.</summary>
    private static int TopSlot(Scene scene, LayerGroup? folder)
    {
        if (folder is null) return scene.Layers.Count;
        if (Span(scene, folder) is { } span) return span.Top + 1;
        return OwnSlot(scene, folder);
    }

    /// <summary>Where an empty folder sits, as an insertion index.</summary>
    private static int OwnSlot(Scene scene, LayerGroup folder) =>
        AnchorOf(scene, folder) is { } anchor ? scene.Layers.IndexOf(anchor) : TopSlot(scene, ParentOf(scene, folder));

    /// <summary>The folder an item is directly inside, or null.</summary>
    public static LayerGroup? ContainerOf(Scene scene, object item) => item switch
    {
        Layer layer => Folder(scene, layer.GroupId),
        LayerGroup folder => ParentOf(scene, folder),
        _ => null,
    };

    /// <summary>A layer or folder by id.</summary>
    public static object? Find(Scene scene, StackRef item) => item.IsFolder
        ? Folder(scene, item.Id)
        : scene.Layers.FirstOrDefault(l => l.Id == item.Id);

    private static bool Inside(Scene scene, object item, LayerGroup folder) => item switch
    {
        Layer l => IsWithin(scene, l, folder),
        LayerGroup g => IsWithin(scene, g, folder),
        _ => false,
    };

    /// <summary>
    /// Move layers and folders, each folder with everything inside it, to sit
    /// above or below <paramref name="target"/>, or at the top inside it.
    /// Returns null, or why it was refused — an empty reason for a drop on
    /// itself — and nothing is changed when it refuses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Above or below an item puts the moved items in that item's container;
    /// above a folder's header is outside that folder, and above its top layer
    /// is inside it — which is what the docker shows at each place.
    /// </para>
    /// <para>
    /// The moved layers keep their order and land as one run, so a folder that
    /// was together stays together, and a scattered selection is gathered.
    /// </para>
    /// </remarks>
    public static string? Move(Scene scene, IReadOnlyList<StackRef> items, StackRef target, StackDrop where)
    {
        var resolved = items.Select(i => Find(scene, i)).ToList();
        if (resolved.Any(r => r is null) || Find(scene, target) is not { } onto) return "That item is no longer in the stack.";
        if (where == StackDrop.Into && onto is not LayerGroup) where = StackDrop.Above;

        var folders = resolved.OfType<LayerGroup>().ToList();
        // Something inside a moved folder moves with it, not on its own.
        var roots = resolved
            .Where(r => !folders.Any(f => !ReferenceEquals(r, f) && Inside(scene, r!, f)))
            .Select(r => r!)
            .Distinct()
            .ToList();
        if (roots.Contains(onto)) return "";
        if (folders.Any(f => Inside(scene, onto, f))) return "A folder can't go inside itself.";

        var run = scene.Layers
            .Where(l => roots.Contains(l) || roots.OfType<LayerGroup>().Any(f => IsWithin(scene, l, f)))
            .ToList();
        if (run.FirstOrDefault(l => l.IsBackground) is { } paper)
        {
            return $"“{paper.Name}” is the paper — it stays at the bottom of the stack.";
        }
        if (where == StackDrop.Below && onto is Layer { IsBackground: true } under)
        {
            return $"“{under.Name}” is the paper — nothing goes under it.";
        }

        var parent = where == StackDrop.Into ? (LayerGroup)onto : ContainerOf(scene, onto);
        foreach (var layer in run) scene.Layers.Remove(layer);
        var at = (where, onto) switch
        {
            (StackDrop.Into, LayerGroup t) => TopSlot(scene, t),
            (StackDrop.Above, Layer t) => scene.Layers.IndexOf(t) + 1,
            (StackDrop.Below, Layer t) => scene.Layers.IndexOf(t),
            (StackDrop.Above, LayerGroup t) => Span(scene, t) is { } s ? s.Top + 1 : OwnSlot(scene, t),
            (_, LayerGroup t) => Span(scene, t) is { } s ? s.Bottom : OwnSlot(scene, t),
            _ => scene.Layers.Count,
        };
        // Nothing under the paper, whichever way the slot was reached.
        if (at == 0 && scene.Layers.Count > 0 && scene.Layers[0].IsBackground) at = 1;
        scene.Layers.InsertRange(at, run);

        foreach (var root in roots)
        {
            switch (root)
            {
                case Layer l: l.GroupId = parent?.Id; break;
                case LayerGroup f: f.ParentId = parent?.Id; break;
            }
        }
        // Empty folders among the moved items sit at the top of the run.
        var above = at + run.Count < scene.Layers.Count ? scene.Layers[at + run.Count].Id : null;
        foreach (var f in roots.OfType<LayerGroup>().Where(f => Span(scene, f) is null))
        {
            f.Under = above;
            f.Under = AnchorOf(scene, f)?.Id;
        }
        // Empty folders sharing a slot stack in list order, later higher, so
        // the list order is where a moved one lands among them: next to the
        // folder it was dropped beside; lowest in the slot when dropped on top
        // of a layer, since it then sits directly on that layer; highest when
        // dropped under a layer or into a folder's top.
        var moved = roots.OfType<LayerGroup>().ToList();
        if (moved.Count > 0)
        {
            foreach (var f in moved) scene.LayerGroups.Remove(f);
            var i = (onto, where) switch
            {
                (LayerGroup sibling, StackDrop.Above) => scene.LayerGroups.IndexOf(sibling) + 1,
                (LayerGroup sibling, StackDrop.Below) => scene.LayerGroups.IndexOf(sibling),
                (Layer, StackDrop.Above) => 0,
                _ => scene.LayerGroups.Count,
            };
            scene.LayerGroups.InsertRange(i, moved);
        }
        return null;
    }

    /// <summary>
    /// Put a new, empty folder directly above <paramref name="active"/>, in
    /// the same container — or at the top of the stack when nothing is active.
    /// </summary>
    /// <remarks>
    /// Photoshop's <em>New Group</em> and Krita's <em>Add Group Layer</em>: an
    /// empty container, made where the artist is working (Q204).
    /// </remarks>
    public static void AddFolder(Scene scene, LayerGroup folder, StackRef? active)
    {
        var item = active is { } a ? Find(scene, a) : null;
        folder.ParentId = item is null ? null : ContainerOf(scene, item)?.Id;
        switch (item)
        {
            case Layer layer:
                var i = scene.Layers.IndexOf(layer);
                folder.Under = i + 1 < scene.Layers.Count ? scene.Layers[i + 1].Id : null;
                scene.LayerGroups.Add(folder);
                break;
            case LayerGroup other when Span(scene, other) is { } span:
                folder.Under = span.Top + 1 < scene.Layers.Count ? scene.Layers[span.Top + 1].Id : null;
                scene.LayerGroups.Add(folder);
                break;
            case LayerGroup other:
                folder.Under = AnchorOf(scene, other)?.Id;
                scene.LayerGroups.Insert(scene.LayerGroups.IndexOf(other) + 1, folder);
                break;
            default:
                folder.Under = null;
                scene.LayerGroups.Add(folder);
                break;
        }
        // The slot above the top of a folder is the top inside it, not outside.
        folder.Under = AnchorOf(scene, folder)?.Id;
    }

    /// <summary>
    /// Wrap the items in a new folder where the topmost of them was — gathering
    /// them into one run if they were apart. Returns null, or why not.
    /// </summary>
    /// <remarks>Photoshop's <em>Group from Layers</em>, Krita's <em>Quick Group</em> — Ctrl+G in both.</remarks>
    public static string? Group(Scene scene, LayerGroup folder, IReadOnlyList<StackRef> items)
    {
        var resolved = items.Select(i => Find(scene, i)).Where(r => r is not null).Select(r => r!).ToList();
        if (resolved.Count == 0) return "Select the layers or folders to put in a folder.";
        if (resolved.OfType<Layer>().FirstOrDefault(l => l.IsBackground) is { } paper)
        {
            return $"“{paper.Name}” is the paper — it stays out of folders.";
        }
        var rows = Rows(scene).Select(r => r.Item).ToList();
        var topmost = resolved.OrderBy(rows.IndexOf).First();
        AddFolder(scene, folder, StackRef.Of(topmost));
        // AddFolder put it just above the topmost item; Into then gathers the
        // items at its top — which is where the topmost one already was.
        return Move(scene, items, new StackRef(folder.Id, true), StackDrop.Into) is { Length: > 0 } why ? why : null;
    }

    /// <summary>Remove a folder and leave what was in it in the folder's place, one level up.</summary>
    public static void Ungroup(Scene scene, LayerGroup folder)
    {
        var parent = ParentOf(scene, folder);
        // The folder's own slot, as the layer directly above it: an empty child
        // that sat at the top inside it sits there once it is gone.
        var slot = Span(scene, folder) is { } span
            ? span.Top + 1 < scene.Layers.Count ? scene.Layers[span.Top + 1].Id : null
            : AnchorOf(scene, folder)?.Id;
        var children = scene.LayerGroups.Where(g => g.ParentId == folder.Id).ToList();
        var atTop = children.Where(c => Span(scene, c) is null && AnchorOf(scene, c) is null).ToList();
        foreach (var layer in scene.Layers.Where(l => l.GroupId == folder.Id)) layer.GroupId = parent?.Id;
        foreach (var child in children) child.ParentId = parent?.Id;
        foreach (var child in atTop) child.Under = slot;
        scene.LayerGroups.Remove(folder);
        foreach (var child in children.Where(c => Span(scene, c) is null)) child.Under = AnchorOf(scene, child)?.Id;
    }

    /// <summary>Remove a folder and everything inside it — layers and folders — as Krita does (Q204).</summary>
    /// <returns>The layers removed, bottom first.</returns>
    public static List<Layer> DeleteWithContents(Scene scene, LayerGroup folder)
    {
        var layers = SubtreeLayers(scene, folder);
        var folders = SubtreeFolders(scene, folder);
        folders.Add(folder);
        scene.Layers.RemoveAll(layers.Contains);
        scene.LayerGroups.RemoveAll(folders.Contains);
        return layers;
    }

    /// <summary>
    /// The stack without the drawings: each layer's id, name, folder and
    /// whether it is the paper, and a copy of every folder — enough to run any
    /// operation here and read its <see cref="Signature"/>.
    /// </summary>
    /// <remarks>
    /// What a drag asks on every pointer move ("would this drop change
    /// anything?"). <see cref="Scene.Clone"/> would copy every stroke on every
    /// frame to answer it.
    /// </remarks>
    public static Scene Skeleton(Scene scene) => new()
    {
        Layers = scene.Layers
            .Select(l => new Layer { Id = l.Id, Name = l.Name, GroupId = l.GroupId, IsBackground = l.IsBackground })
            .ToList(),
        LayerGroups = scene.LayerGroups.Select(g => g.Clone()).ToList(),
    };

    /// <summary>What a stack edit is judged by: the layer order, who is in what, and where empty folders sit.</summary>
    public static string Signature(Scene scene) =>
        string.Join("|", scene.Layers.Select(l => $"{l.Id}:{l.GroupId}"))
        + "#" + string.Join("|", scene.LayerGroups.Select(g => $"{g.Id}:{g.ParentId}:{g.Under}"));
}

/// <summary>A layer or a folder in the stack, by id.</summary>
public readonly record struct StackRef(string Id, bool IsFolder)
{
    public static StackRef Of(object item) => item switch
    {
        Layer l => new StackRef(l.Id, false),
        LayerGroup g => new StackRef(g.Id, true),
        _ => throw new ArgumentException("Not a layer or a folder.", nameof(item)),
    };
}

/// <summary>Where a moved item lands relative to the one it is dropped on.</summary>
public enum StackDrop
{
    Above,
    Below,

    /// <summary>At the top inside a folder.</summary>
    Into,
}
