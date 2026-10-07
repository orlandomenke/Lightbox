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
}
