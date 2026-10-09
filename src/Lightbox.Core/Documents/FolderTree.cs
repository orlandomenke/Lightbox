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
    /// <summary>
    /// The deepest nesting the tree follows. A parent chain longer than this —
    /// only a crafted or damaged file has one — is cut there, the way a parent
    /// that does not exist is: what lies beyond shows at the top level.
    /// </summary>
    /// <remarks>
    /// It bounds every walk up the tree, so a file cannot make opening it, a
    /// composite or a docker refresh cost more than this many steps per layer
    /// (sensitivity-guardian on Q204: a 10,000-deep chain hung the docker and
    /// could overflow the stack).
    /// </remarks>
    public const int MaxDepth = 32;

    private delegate LayerGroup? Lookup(string? id);

    /// <summary>Find by scanning — for one question asked once.</summary>
    private static Lookup Linear(Scene scene) =>
        id => id is null ? null : scene.LayerGroups.FirstOrDefault(g => g.Id == id);

    /// <summary>
    /// Find by an index built once — for a pass over the whole stack. The first
    /// of two folders sharing an id wins, as the scan's does.
    /// </summary>
    private static Lookup Indexed(Scene scene)
    {
        var byId = new Dictionary<string, LayerGroup>(scene.LayerGroups.Count);
        foreach (var g in scene.LayerGroups) byId.TryAdd(g.Id, g);
        return id => id is not null && byId.TryGetValue(id, out var g) ? g : null;
    }

    /// <summary>A folder by id, or null.</summary>
    public static LayerGroup? Folder(Scene scene, string? id) => Linear(scene)(id);

    /// <summary>The folders containing <paramref name="folder"/>, innermost first, at most <see cref="MaxDepth"/>.</summary>
    public static List<LayerGroup> Ancestors(Scene scene, LayerGroup folder) => Ancestors(Linear(scene), folder);

    private static List<LayerGroup> Ancestors(Lookup find, LayerGroup folder)
    {
        var chain = new List<LayerGroup>();
        var seen = new HashSet<LayerGroup>(ReferenceEqualityComparer.Instance) { folder };
        for (var p = find(folder.ParentId); p is not null && chain.Count < MaxDepth && seen.Add(p); p = find(p.ParentId))
        {
            chain.Add(p);
        }
        return chain;
    }

    /// <summary>
    /// The colour a folder shows: its own once chosen, else the nearest
    /// containing folder's that has one, else <see cref="LayerGroup.DefaultColor"/>.
    /// </summary>
    public static string ColorOf(Scene scene, LayerGroup folder) =>
        folder.Color
        ?? Ancestors(scene, folder).FirstOrDefault(f => f.Color is not null)?.Color
        ?? LayerGroup.DefaultColor;

    /// <summary>The colour of the folder a layer is in, or null for a loose layer.</summary>
    public static string? ColorOf(Scene scene, Layer layer) =>
        FoldersOf(scene, layer) is [var innermost, ..] ? ColorOf(scene, innermost) : null;

    /// <summary>The folder directly containing <paramref name="folder"/>, as the tree resolves it.</summary>
    public static LayerGroup? ParentOf(Scene scene, LayerGroup folder) => ParentOf(Linear(scene), folder);

    private static LayerGroup? ParentOf(Lookup find, LayerGroup folder) =>
        find(folder.ParentId) is { } p && !ReferenceEquals(p, folder) ? p : null;

    /// <summary>The folders a layer is inside, innermost first; empty for a loose layer.</summary>
    public static List<LayerGroup> FoldersOf(Scene scene, Layer layer) => FoldersOf(Linear(scene), layer);

    private static List<LayerGroup> FoldersOf(Lookup find, Layer layer)
    {
        if (find(layer.GroupId) is not { } own) return [];
        var chain = new List<LayerGroup> { own };
        chain.AddRange(Ancestors(find, own));
        if (chain.Count > MaxDepth) chain.RemoveRange(MaxDepth, chain.Count - MaxDepth);
        return chain;
    }

    /// <summary>The innermost locked folder a layer is inside, or null — the one to name when an edit is refused.</summary>
    public static LayerGroup? LockedFolderOf(Scene scene, Layer layer) =>
        layer.GroupId is null ? null : FoldersOf(scene, layer).FirstOrDefault(f => f.Locked);

    /// <summary>The innermost locked folder a folder is inside, or null. Its own lock is not counted.</summary>
    public static LayerGroup? LockedFolderOf(Scene scene, LayerGroup folder) =>
        Ancestors(scene, folder).FirstOrDefault(f => f.Locked);

    /// <summary>
    /// The innermost hidden folder a layer is inside, or null. A layer in one
    /// draws nothing whatever its own switch says.
    /// </summary>
    public static LayerGroup? HiddenFolderOf(Scene scene, Layer layer) =>
        layer.GroupId is null ? null : FoldersOf(scene, layer).FirstOrDefault(f => !f.Visible);

    /// <summary>The innermost hidden folder a folder is inside, or null. Its own switch is not counted.</summary>
    public static LayerGroup? HiddenFolderOf(Scene scene, LayerGroup folder) =>
        Ancestors(scene, folder).FirstOrDefault(f => !f.Visible);

    /// <summary>Whether <paramref name="inner"/> is <paramref name="outer"/> or inside it at any depth.</summary>
    public static bool IsWithin(Scene scene, LayerGroup inner, LayerGroup outer) =>
        inner.Id == outer.Id || Ancestors(scene, inner).Any(a => a.Id == outer.Id);

    /// <summary>Whether a layer is inside <paramref name="folder"/> at any depth.</summary>
    public static bool IsWithin(Scene scene, Layer layer, LayerGroup folder) => IsWithin(Linear(scene), layer, folder);

    private static bool IsWithin(Lookup find, Layer layer, LayerGroup folder) =>
        layer.GroupId is not null && FoldersOf(find, layer).Any(f => f.Id == folder.Id);

    /// <summary>Every layer inside <paramref name="folder"/> at any depth, bottom first.</summary>
    public static List<Layer> SubtreeLayers(Scene scene, LayerGroup folder)
    {
        var find = Indexed(scene);
        return scene.Layers.Where(l => IsWithin(find, l, folder)).ToList();
    }

    /// <summary>Every folder inside <paramref name="folder"/> at any depth, not counting itself.</summary>
    public static List<LayerGroup> SubtreeFolders(Scene scene, LayerGroup folder)
    {
        var find = Indexed(scene);
        return scene.LayerGroups
            .Where(g => !ReferenceEquals(g, folder) && Ancestors(find, g).Any(a => a.Id == folder.Id))
            .ToList();
    }

    /// <summary>The ids of every folder holding at least one layer, at any depth.</summary>
    public static HashSet<string> HoldingLayers(Scene scene) => HoldingLayers(scene, Indexed(scene));

    private static HashSet<string> HoldingLayers(Scene scene, Lookup find)
    {
        var holding = new HashSet<string>();
        if (scene.LayerGroups.Count == 0) return holding;
        foreach (var layer in scene.Layers)
        {
            if (layer.GroupId is null) continue;
            foreach (var f in FoldersOf(find, layer)) holding.Add(f.Id);
        }
        return holding;
    }

    /// <summary>
    /// The layer an empty folder sits directly under, if its
    /// <see cref="LayerGroup.Under"/> still names one inside the folder's
    /// parent — otherwise null, which is the top of the parent.
    /// </summary>
    public static Layer? AnchorOf(Scene scene, LayerGroup folder) => AnchorOf(scene, Linear(scene), folder);

    private static Layer? AnchorOf(Scene scene, Lookup find, LayerGroup folder)
    {
        if (folder.Under is not { } id || scene.Layers.FirstOrDefault(l => l.Id == id) is not { } layer) return null;
        return ParentOf(find, folder) is { } parent && !IsWithin(find, layer, parent) ? null : layer;
    }

    /// <summary>
    /// The rows the Timeline and the X-sheet show (Q227), topmost first: the
    /// docker's tree, folded by <see cref="LayerGroup.SheetCollapsed"/> rather
    /// than by the docker's own <see cref="LayerGroup.Collapsed"/>, and with
    /// what is folded away left out rather than marked.
    /// </summary>
    /// <remarks>
    /// The same walk as <see cref="Rows(Scene)"/> and deliberately not a second
    /// one: where a folder sits, what a split folder does and what a crafted
    /// file cannot do are decided there once, and a sheet that ordered its rows
    /// by a different rule would disagree with the docker beside it.
    /// </remarks>
    public static List<StackRow> SheetRows(Scene scene) =>
        Rows(scene, f => f.SheetCollapsed == true).Where(r => !r.Hidden).ToList();

    /// <summary>The docker's rows, topmost first.</summary>
    /// <remarks>
    /// Every walk here runs on one index built for the call and is bounded by
    /// <see cref="MaxDepth"/>, and folders are told apart by reference, so two
    /// that share an id (a hand-edited file) are both listed rather than one
    /// throwing.
    /// </remarks>
    public static List<StackRow> Rows(Scene scene) => Rows(scene, f => f.Collapsed);

    /// <summary>The same rows, folded by whichever surface is asking.</summary>
    private static List<StackRow> Rows(Scene scene, Func<LayerGroup, bool> folded)
    {
        var layers = scene.Layers;
        var rows = new List<StackRow>(layers.Count + scene.LayerGroups.Count);
        if (scene.LayerGroups.Count == 0)
        {
            for (var i = layers.Count - 1; i >= 0; i--) rows.Add(new StackRow(layers[i], 0, false, false));
            return rows;
        }

        var find = Indexed(scene);
        var holding = HoldingLayers(scene, find);
        var refs = ReferenceEqualityComparer.Instance;
        var order = new Dictionary<LayerGroup, int>(refs);
        for (var i = 0; i < scene.LayerGroups.Count; i++) order.TryAdd(scene.LayerGroups[i], i);
        // Empty folders, highest first within any slot, filed by where they go:
        // inside which folder (the top level as its own key), and under which layer.
        var empties = scene.LayerGroups
            .Where(g => !holding.Contains(g.Id))
            .OrderByDescending(g => order[g])
            .Select(g => (Folder: g, Parent: ParentOf(find, g), Anchor: AnchorOf(scene, find, g)))
            .ToList();
        var topLevel = new object();
        var childrenOf = empties.ToLookup(e => (object?)e.Parent ?? topLevel, refs);
        var underLayer = empties.Where(e => e.Anchor is not null).ToLookup(e => e.Anchor!.Id);
        var emitted = new HashSet<LayerGroup>(refs);
        var open = new List<LayerGroup>(); // outermost first

        bool HiddenNow() => open.Any(folded);

        void EmitEmpty(LayerGroup folder, int depth, bool hidden)
        {
            if (!emitted.Add(folder)) return;
            rows.Add(new StackRow(folder, depth, hidden, false));
            // Inside an empty folder there is nothing to anchor to: every child
            // is at its top, later in the list higher, as siblings are. Past
            // MaxDepth the safety net below lists them instead, so a crafted
            // chain cannot recurse the stack away.
            if (depth >= MaxDepth) return;
            foreach (var child in childrenOf[folder]) EmitEmpty(child.Folder, depth + 1, hidden || folded(folder));
        }

        void EmitTopOf(LayerGroup? parent)
        {
            foreach (var e in childrenOf[(object?)parent ?? topLevel].Where(e => e.Anchor is null))
            {
                EmitEmpty(e.Folder, open.Count, HiddenNow());
            }
        }

        void Open(LayerGroup folder)
        {
            var again = !emitted.Add(folder);
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
                foreach (var e in underLayer[above.Id]
                             .OrderByDescending(e => e.Parent is null ? 0 : open.IndexOf(e.Parent) + 1)
                             .ThenByDescending(e => order[e.Folder]))
                {
                    var depth = e.Parent is null ? 0 : open.IndexOf(e.Parent) + 1;
                    if (depth == 0 && e.Parent is not null) continue; // its parent is not open here; the safety net lists it
                    open.RemoveRange(depth, open.Count - depth);
                    EmitEmpty(e.Folder, depth, HiddenNow());
                }
            }
            if (i < 0) break;

            var layer = layers[i];
            var path = FoldersOf(find, layer);
            path.Reverse(); // outermost first
            var common = 0;
            while (common < open.Count && common < path.Count && ReferenceEquals(open[common], path[common])) common++;
            open.RemoveRange(common, open.Count - common);
            for (var k = common; k < path.Count; k++) Open(path[k]);
            rows.Add(new StackRow(layer, open.Count, HiddenNow(), false));
            above = layer;
        }

        // The safety net: a folder no walk reached is still a folder.
        foreach (var g in scene.LayerGroups.Where(g => !emitted.Contains(g)).ToList())
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
        var find = Indexed(after);
        var findBefore = before is null ? find : Indexed(before);
        var holding = HoldingLayers(after, find);
        var beforeHolding = before is null ? null : HoldingLayers(before, findBefore);
        foreach (var folder in after.LayerGroups)
        {
            // A shape names a layer inside its folder or nothing (Q215): one
            // dragged out, deleted or merged away leaves no key behind that
            // would quietly start carving again if it came back.
            if (folder.ShapeLayerId is { } shapeId
                && !after.Layers.Any(l => l.Id == shapeId && IsWithin(find, l, folder)))
            {
                folder.ShapeLayerId = null;
            }
            if (holding.Contains(folder.Id))
            {
                folder.Under = null;
                folder.PlacedByEdit = false;
                continue;
            }
            var old = before is null ? null : findBefore(folder.Id);
            var placed = folder.PlacedByEdit;
            folder.PlacedByEdit = false;
            if (placed || old is null || old.ParentId != folder.ParentId
                || old.Under != folder.Under && !beforeHolding!.Contains(old.Id))
            {
                // New, or moved on purpose: take its slot as given.
                folder.Under = AnchorOf(after, find, folder)?.Id;
                continue;
            }

            // The slot it had before, as the layers either side of it then.
            string? aboveId, belowId;
            var was = before!.Layers;
            if (beforeHolding!.Contains(old.Id))
            {
                var top = was.FindLastIndex(l => IsWithin(findBefore, l, old));
                var bottom = was.FindIndex(l => IsWithin(findBefore, l, old));
                aboveId = top + 1 < was.Count ? was[top + 1].Id : null;
                belowId = bottom > 0 ? was[bottom - 1].Id : null;
            }
            else if (AnchorOf(before, findBefore, old) is { } anchor)
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
            string? pin = null;
            if (aboveAt >= 0 && (belowId is null ? aboveAt == 0 : belowAt == aboveAt - 1))
            {
                pin = aboveId; // its neighbours are still neighbours
            }
            else
            {
                // Stay on the nearest layer below it that is still there — or,
                // with nothing left below, under the nearest one above. Both
                // neighbours going in one edit (a multi-delete, a merge) must
                // not send the folder to the top of the stack.
                var from = belowId is null ? -1 : was.FindIndex(l => l.Id == belowId);
                var found = false;
                for (var k = from; k >= 0 && !found; k--)
                {
                    var at = now.FindIndex(l => l.Id == was[k].Id);
                    if (at < 0) continue;
                    pin = at + 1 < now.Count ? now[at + 1].Id : null;
                    found = true;
                }
                var up = aboveId is null ? was.Count : was.FindIndex(l => l.Id == aboveId);
                for (var k = up; k < was.Count && k >= 0 && !found; k++)
                {
                    if (now.Any(l => l.Id == was[k].Id))
                    {
                        pin = was[k].Id;
                        found = true;
                    }
                }
            }
            folder.Under = pin;
            folder.Under = AnchorOf(after, find, folder)?.Id; // still inside its parent, or the top of it
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

    /// <summary>
    /// <paramref name="folder"/> and every folder above it, followed to the end
    /// of the chain (a loop stops it) rather than to <see cref="MaxDepth"/>.
    /// Only for the refusals in <see cref="Move"/>, which must not be fooled by
    /// a chain longer than the cap.
    /// </summary>
    private static HashSet<LayerGroup> ChainOf(Scene scene, LayerGroup folder)
    {
        var find = Indexed(scene);
        var chain = new HashSet<LayerGroup>(ReferenceEqualityComparer.Instance);
        for (var f = folder; f is not null && chain.Add(f); f = find(f.ParentId)) { }
        return chain;
    }

    /// <summary>How many folders deep anything put directly inside <paramref name="container"/> would be.</summary>
    private static int Nesting(Scene scene, LayerGroup? container) =>
        container is null ? 0 : ChainOf(scene, container).Count;

    /// <summary>How many levels an item takes up: a layer none, a folder one plus its deepest content.</summary>
    private static int Height(Scene scene, object item)
    {
        if (item is not LayerGroup folder) return 0;
        var find = Indexed(scene);
        var height = 1;
        foreach (var g in scene.LayerGroups)
        {
            var d = 1;
            var seen = new HashSet<LayerGroup>(ReferenceEqualityComparer.Instance);
            for (var p = g; p is not null && seen.Add(p); p = find(p.ParentId), d++)
            {
                if (ReferenceEquals(p, folder))
                {
                    height = Math.Max(height, d);
                    break;
                }
            }
        }
        return height;
    }

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
        // Walked to the end, not to MaxDepth: past the cap the bounded walk
        // would not see a folder's own descendant, and the move would write
        // a real loop into the file (the adversary's 41-deep case).
        var destination = where == StackDrop.Into ? (LayerGroup)onto : ContainerOf(scene, onto);
        if (destination is not null && folders.Any(f => ChainOf(scene, destination).Contains(f)))
        {
            return "A folder can't go inside itself.";
        }
        if (Nesting(scene, destination) + roots.Select(r => Height(scene, r)).DefaultIfEmpty(0).Max() > MaxDepth)
        {
            return $"Folders nest at most {MaxDepth} deep \u2014 that would go deeper.";
        }

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
        // Where the run goes is read BEFORE anything is lifted, as an index in
        // the stack as it stands, and then shifted down by the moved layers
        // that sat below it. Reading it after the lift was wrong twice over:
        // an empty folder's anchor could be one of the moved layers (the
        // adversary's B, A, Z case), and a folder whose last layers are the
        // ones moving has no span left to be beside — so ▲ on the only layer
        // in a folder, or a drop of it back on its own header, failed.
        var original = (where, onto) switch
        {
            (StackDrop.Into, LayerGroup t) => TopSlot(scene, t),
            (StackDrop.Above, Layer t) => scene.Layers.IndexOf(t) + 1,
            (StackDrop.Below, Layer t) => scene.Layers.IndexOf(t),
            (StackDrop.Above, LayerGroup t) => Span(scene, t) is { } s ? s.Top + 1 : OwnSlot(scene, t),
            (_, LayerGroup t) => Span(scene, t) is { } s ? s.Bottom : OwnSlot(scene, t),
            _ => scene.Layers.Count,
        };
        var runSet = run.ToHashSet();
        var at = original - scene.Layers.Take(original).Count(runSet.Contains);
        foreach (var layer in run) scene.Layers.Remove(layer);
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
        // A folder dropped beside that is empty now — it was empty, or the run
        // was everything in it — ends up on the side of the run it was dropped
        // against: under the run's bottom layer for "above it", over the run's
        // top layer for "below it". Without this a layer could never be put
        // above an empty folder sitting directly on it.
        if (where != StackDrop.Into && onto is LayerGroup emptyTarget && run.Count > 0 && Span(scene, emptyTarget) is null)
        {
            emptyTarget.Under = where == StackDrop.Above
                ? run[0].Id
                : at + run.Count < scene.Layers.Count ? scene.Layers[at + run.Count].Id : null;
            emptyTarget.Under = AnchorOf(scene, emptyTarget)?.Id;
            emptyTarget.PlacedByEdit = true;
        }
        // Empty folders among the moved items sit at the top of the run.
        var above = at + run.Count < scene.Layers.Count ? scene.Layers[at + run.Count].Id : null;
        foreach (var f in roots.OfType<LayerGroup>().Where(f => Span(scene, f) is null))
        {
            f.Under = above;
            f.Under = AnchorOf(scene, f)?.Id;
            f.PlacedByEdit = true;
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
