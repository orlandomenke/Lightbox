namespace Lightbox.Core.Documents;

/// <summary>
/// The folder hierarchy, and the operations that move things around in it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two lists, one shape.</b> <c>Scene.Layers</c> is the flat compositing
/// order and <c>Scene.LayerGroups</c> is the set of folders; between them they
/// describe a tree, and this is the only file that knows how. Every operation
/// here works the same way: change <em>parentage</em> — a layer's
/// <c>GroupId</c>, a folder's <c>ParentId</c> — and then call
/// <see cref="Normalise"/>, which rewrites both lists into the one order that
/// is consistent with the new parentage.
/// </para>
/// <para>
/// <b>Why not splice runs by hand.</b> The flat-folder code moved blocks
/// around with index arithmetic — find the run, remove it, find the anchor,
/// insert. That is already delicate with one level, and with nesting every such
/// move has to carry descendants it cannot see from the call site. Re-deriving
/// the order from the parentage cannot leave a folder split around a stranger,
/// because a split is not expressible in the walk. The cost is a pass over the
/// layer list per structural edit, which is nothing beside the document clone
/// the same edit already takes for undo.
/// </para>
/// <para>
/// <b>Bottom-first everywhere.</b> <c>Scene.Layers</c> stores the bottom of the
/// stack first, the docker draws the top first, and every list in this file is
/// in <c>Scene.Layers</c> order. The inversion belongs at the one place that
/// draws, not scattered through the rules.
/// </para>
/// </remarks>
public static class LayerTree
{
    /// <summary>
    /// How deep a folder may be nested, counting the outermost as 0.
    /// </summary>
    /// <remarks>
    /// A limit rather than no limit, because the indentation has to fit the
    /// docker and because a runaway nest is far more likely to be a bug than an
    /// intention. Eight is past anything an artist has been seen to build —
    /// Photoshop's own practical ceiling is around ten — and refusing at the
    /// edge is a message, not a crash.
    /// </remarks>
    public const int MaxDepth = 8;

    // ---- reading the tree ---------------------------------------------------

    /// <summary>An id → folder lookup, built once for a walk.</summary>
    /// <remarks>
    /// Every ancestor question used to be a <c>FirstOrDefault</c> over the
    /// folder list, and the visibility one is asked per layer per composite —
    /// so a 40-layer document with 6 folders paid 240 scans a frame to answer
    /// a question about a chain at most a few links long. Callers that ask more
    /// than once build this instead.
    /// </remarks>
    public static Dictionary<string, LayerGroup> ById(IReadOnlyList<LayerGroup> groups)
    {
        var map = new Dictionary<string, LayerGroup>(groups.Count, StringComparer.Ordinal);
        foreach (var group in groups) map[group.Id] = group;
        return map;
    }

    /// <summary>
    /// The folders enclosing this one, innermost first, not including itself.
    /// </summary>
    /// <remarks>
    /// Stops at <see cref="MaxDepth"/> + 1 links whatever the data says. A cycle
    /// cannot be authored — <see cref="CanMoveGroupInto"/> refuses one — but a
    /// hand-edited or truncated file can carry one, and a walk that loops
    /// forever on load is a worse answer than a folder that reads as shallow.
    /// </remarks>
    public static IEnumerable<LayerGroup> AncestorsOf(
        LayerGroup group, Dictionary<string, LayerGroup> byId)
    {
        var at = group;
        for (var guard = 0; guard <= MaxDepth; guard++)
        {
            if (at.ParentId is not { } parentId || !byId.TryGetValue(parentId, out var parent)) yield break;
            yield return parent;
            at = parent;
        }
    }

    /// <summary>How many folders enclose this one — 0 for a top-level folder.</summary>
    public static int DepthOf(LayerGroup group, Dictionary<string, LayerGroup> byId) =>
        AncestorsOf(group, byId).Count();

    /// <summary>Whether <paramref name="maybeDescendant"/> is inside <paramref name="group"/>, at any depth.</summary>
    public static bool Contains(
        LayerGroup group, LayerGroup maybeDescendant, Dictionary<string, LayerGroup> byId) =>
        AncestorsOf(maybeDescendant, byId).Any(a => a.Id == group.Id);

    /// <summary>Whether a layer sits inside this folder, at any depth.</summary>
    public static bool Contains(
        LayerGroup group, Layer layer, Dictionary<string, LayerGroup> byId) =>
        layer.GroupId is { } id
        && byId.TryGetValue(id, out var own)
        && (own.Id == group.Id || Contains(group, own, byId));

    /// <summary>
    /// Every layer in a folder and in the folders inside it, in stack order.
    /// </summary>
    public static List<Layer> SubtreeLayers(
        LayerGroup group, IReadOnlyList<Layer> layers, Dictionary<string, LayerGroup> byId)
    {
        var inside = SubtreeGroupIds(group, byId);
        return layers.Where(l => l.GroupId is { } id && inside.Contains(id)).ToList();
    }

    /// <summary>The folder's own id plus every folder id inside it.</summary>
    public static HashSet<string> SubtreeGroupIds(
        LayerGroup group, Dictionary<string, LayerGroup> byId)
    {
        var inside = new HashSet<string>(StringComparer.Ordinal) { group.Id };
        // Fixed-point rather than recursion from the parent side: folders hold
        // no child list, so the only way down is to ask every folder who its
        // parent is. Bounded by MaxDepth passes over a list that is short.
        for (var pass = 0; pass <= MaxDepth; pass++)
        {
            var grew = false;
            foreach (var candidate in byId.Values)
            {
                if (candidate.ParentId is { } pid && inside.Contains(pid) && inside.Add(candidate.Id)) grew = true;
            }
            if (!grew) break;
        }
        return inside;
    }

    /// <summary>Whether the folder holds no layers at any depth — the header with nothing under it.</summary>
    public static bool IsEmpty(
        LayerGroup group, IReadOnlyList<Layer> layers, Dictionary<string, LayerGroup> byId)
    {
        var inside = SubtreeGroupIds(group, byId);
        foreach (var layer in layers)
        {
            if (layer.GroupId is { } id && inside.Contains(id)) return false;
        }
        return true;
    }

    // ---- the walk -----------------------------------------------------------

    /// <summary>One line of the docker: a folder header, or a layer.</summary>
    /// <param name="Layer">The layer, or null when this is a folder header.</param>
    /// <param name="Group">The folder, or null when this is a layer row.</param>
    /// <param name="Depth">How many folders enclose this row — 0 at the top level.</param>
    public readonly record struct Node(Layer? Layer, LayerGroup? Group, int Depth)
    {
        /// <summary>Whether this row is a folder header.</summary>
        public bool IsGroup => Group is not null && Layer is null;
    }

    /// <summary>
    /// The whole tree as a flat sequence, bottom of the stack first: a folder's
    /// header immediately before the run of rows inside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the single definition of document order. <see cref="Normalise"/>
    /// writes <c>Scene.Layers</c> and <c>Scene.LayerGroups</c> to agree with it,
    /// and the docker draws it reversed. Anything that needs to know where a
    /// folder sits asks here rather than deriving it again.
    /// </para>
    /// <para>
    /// <b>Where an empty folder goes, and why it is arbitrary on purpose.</b>
    /// A folder's place among its siblings is the place of the lowest layer
    /// inside it — that is what makes the walk agree with a layer list that
    /// nobody has re-ordered. A folder holding no layers has no such place, so
    /// it is put at the <em>top</em> of whatever contains it, which is where a
    /// folder you have just made is easiest to see and to drop something into.
    /// The alternative — remembering a slot for something with nothing in it —
    /// is a second ordering to keep in step with the first, for a state that
    /// lasts until the next drag.
    /// </para>
    /// </remarks>
    public static List<Node> Walk(IReadOnlyList<Layer> layers, IReadOnlyList<LayerGroup> groups) =>
        Walk(layers, groups, topFirst: false);

    /// <summary>
    /// The whole tree as a flat sequence, topmost row first — the order the
    /// docker draws, with a folder's header above the rows inside it.
    /// </summary>
    /// <remarks>
    /// <b>Not the bottom-first walk reversed.</b> Reversing turns "header, then
    /// what is inside it" into "what is inside it, then header", which puts
    /// every folder's title under its own contents. So the docker order is its
    /// own traversal: the same containers in the opposite order, with the header
    /// still emitted before the rows it owns.
    /// </remarks>
    public static List<Node> WalkTopFirst(
        IReadOnlyList<Layer> layers, IReadOnlyList<LayerGroup> groups) =>
        Walk(layers, groups, topFirst: true);

    private static List<Node> Walk(
        IReadOnlyList<Layer> layers, IReadOnlyList<LayerGroup> groups, bool topFirst)
    {
        var byId = ById(groups);
        var nodes = new List<Node>(layers.Count + groups.Count);
        Emit(null, 0);
        return nodes;

        void Emit(string? containerId, int depth)
        {
            if (depth > MaxDepth) return;

            // The container's own layers, in stack order, and its child folders
            // keyed by the lowest layer anywhere inside them. Sorting by that
            // key interleaves the two the way the layer list already reads.
            var merged = new List<(int Key, int Ordinal, Layer? Layer, LayerGroup? Group)>();
            for (var i = 0; i < layers.Count; i++)
            {
                if (SameContainer(layers[i].GroupId, containerId)) merged.Add((i, i, layers[i], null));
            }
            for (var g = 0; g < groups.Count; g++)
            {
                var child = groups[g];
                if (!SameContainer(child.ParentId, containerId)) continue;
                merged.Add((FirstLayerIndex(child), g, null, child));
            }
            merged.Sort(static (a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Ordinal.CompareTo(b.Ordinal));
            if (topFirst) merged.Reverse();

            foreach (var (_, _, layer, group) in merged)
            {
                if (layer is not null)
                {
                    nodes.Add(new Node(layer, null, depth));
                    continue;
                }
                nodes.Add(new Node(null, group, depth));
                Emit(group!.Id, depth + 1);
            }
        }

        // int.MaxValue for an empty folder, which sorts it to the top of its
        // container — see the remarks.
        int FirstLayerIndex(LayerGroup group)
        {
            var inside = SubtreeGroupIds(group, byId);
            for (var i = 0; i < layers.Count; i++)
            {
                if (layers[i].GroupId is { } id && inside.Contains(id)) return i;
            }
            return int.MaxValue;
        }
    }

    private static bool SameContainer(string? a, string? b) => string.Equals(a, b, StringComparison.Ordinal);

    /// <summary>
    /// Rewrite the layer and folder lists into the order <see cref="Walk"/>
    /// describes, so every folder's subtree is one contiguous run.
    /// </summary>
    /// <remarks>
    /// Idempotent: a scene already in order comes out identical, which is what
    /// makes it safe to call after any edit rather than only after the ones that
    /// need it. A layer whose <c>GroupId</c> names a folder that is not there —
    /// a deleted folder, a hand-edited file — is emitted at the top level and
    /// has its <c>GroupId</c> cleared, because an unreachable layer is the one
    /// outcome the docker cannot show.
    /// </remarks>
    public static void Normalise(List<Layer> layers, List<LayerGroup> groups)
    {
        var byId = ById(groups);

        // Orphans first, so the walk that follows sees a tree it can reach.
        foreach (var layer in layers)
        {
            if (layer.GroupId is { } id && !byId.ContainsKey(id)) layer.GroupId = null;
        }
        foreach (var group in groups)
        {
            if (group.ParentId is { } pid && (!byId.ContainsKey(pid) || pid == group.Id)) group.ParentId = null;
        }
        BreakCycles(groups, byId);

        var nodes = Walk(layers, groups);

        var orderedLayers = new List<Layer>(layers.Count);
        var orderedGroups = new List<LayerGroup>(groups.Count);
        foreach (var node in nodes)
        {
            if (node.Layer is { } layer) orderedLayers.Add(layer);
            else if (node.Group is { } group) orderedGroups.Add(group);
        }

        // A folder past MaxDepth is not emitted by the walk, and dropping it
        // here would delete an artist's folder to enforce an indentation limit.
        // Keep whatever the walk did not reach, in the order it already had.
        if (orderedGroups.Count != groups.Count)
        {
            var seen = orderedGroups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var group in groups)
            {
                if (seen.Add(group.Id)) orderedGroups.Add(group);
            }
        }
        if (orderedLayers.Count != layers.Count)
        {
            var seen = orderedLayers.Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var layer in layers)
            {
                if (seen.Add(layer.Id)) orderedLayers.Add(layer);
            }
        }

        layers.Clear();
        layers.AddRange(orderedLayers);
        groups.Clear();
        groups.AddRange(orderedGroups);
    }

    /// <summary>
    /// Cut any parent link that closes a loop, so the walk terminates.
    /// </summary>
    /// <remarks>
    /// Unreachable through the app — <see cref="CanMoveGroupInto"/> refuses the
    /// move that would make one — and reachable through a hand-edited or
    /// truncated file, which is the whole reason load must survive it.
    /// </remarks>
    private static void BreakCycles(List<LayerGroup> groups, Dictionary<string, LayerGroup> byId)
    {
        foreach (var group in groups)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { group.Id };
            var at = group;
            while (at.ParentId is { } pid && byId.TryGetValue(pid, out var parent))
            {
                if (!seen.Add(pid))
                {
                    at.ParentId = null;
                    break;
                }
                at = parent;
            }
        }
    }

    // ---- moving things ------------------------------------------------------

    /// <summary>
    /// Whether a folder may be filed into <paramref name="parent"/> — null
    /// meaning the top level.
    /// </summary>
    /// <remarks>
    /// Three refusals, and each is a thing a drag can ask for: a folder into
    /// itself, a folder into one of its own descendants (which would detach the
    /// pair from the tree entirely), and a nest deeper than
    /// <see cref="MaxDepth"/>.
    /// </remarks>
    public static bool CanMoveGroupInto(
        LayerGroup group, LayerGroup? parent, IReadOnlyList<LayerGroup> groups)
    {
        if (parent is null) return true;
        if (parent.Id == group.Id) return false;
        var byId = ById(groups);
        if (Contains(group, parent, byId)) return false;
        return DepthOf(parent, byId) + 1 + HeightOf(group, groups) <= MaxDepth;
    }

    /// <summary>How many levels of folder sit inside this one — 0 when none do.</summary>
    public static int HeightOf(LayerGroup group, IReadOnlyList<LayerGroup> groups)
    {
        var byId = ById(groups);
        var inside = SubtreeGroupIds(group, byId);
        var deepest = 0;
        foreach (var id in inside)
        {
            if (!byId.TryGetValue(id, out var member)) continue;
            deepest = Math.Max(deepest, DepthOf(member, byId) - DepthOf(group, byId));
        }
        return deepest;
    }

    /// <summary>
    /// Put a layer inside a folder — null meaning the top level — at the top of
    /// that container.
    /// </summary>
    /// <remarks>
    /// The top, because that is where the thing you have just filed should be
    /// visible, and because a folder you drop onto has no other obvious slot.
    /// A drop <em>between</em> two rows says where it goes and uses
    /// <see cref="PlaceLayerBeside"/> instead.
    /// </remarks>
    public static void MoveLayerInto(
        List<Layer> layers, List<LayerGroup> groups, Layer layer, string? groupId)
    {
        layer.GroupId = groupId;
        // Off the end of the list first: Normalise reads the existing order to
        // decide where a layer sits among its new siblings, so a layer left
        // where it was would keep its old neighbours' place in the new folder.
        layers.Remove(layer);
        layers.Add(layer);
        Normalise(layers, groups);
    }

    /// <summary>
    /// Put a layer directly above or below <paramref name="anchor"/>, adopting
    /// the anchor's folder.
    /// </summary>
    public static void PlaceLayerBeside(
        List<Layer> layers, List<LayerGroup> groups, Layer layer, Layer anchor, bool above)
    {
        if (ReferenceEquals(layer, anchor)) return;
        layer.GroupId = anchor.GroupId;
        layers.Remove(layer);
        var at = layers.IndexOf(anchor);
        if (at < 0) layers.Add(layer);
        else layers.Insert(above ? at + 1 : at, layer);
        Normalise(layers, groups);
    }

    /// <summary>
    /// Put a layer directly above or below a whole folder, outside it.
    /// </summary>
    public static void PlaceLayerBesideGroup(
        List<Layer> layers, List<LayerGroup> groups, Layer layer, LayerGroup anchor, bool above)
    {
        var byId = ById(groups);
        layer.GroupId = anchor.ParentId;
        layers.Remove(layer);
        var block = SubtreeLayers(anchor, layers, byId);
        if (block.Count == 0)
        {
            // An empty folder sits at the top of its container, so "above it" is
            // the top of that container and "below it" is just under it.
            layers.Add(layer);
        }
        else
        {
            var at = layers.IndexOf(above ? block[^1] : block[0]);
            if (at < 0) layers.Add(layer);
            else layers.Insert(above ? at + 1 : at, layer);
        }
        Normalise(layers, groups);
    }

    /// <summary>
    /// Move a layer one row up (<paramref name="delta"/> +1, toward the viewer)
    /// or down the docker, entering and leaving folders on the way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One row in the panel, not one place in the list.</b> The ▲/▼ buttons
    /// used to swap two entries of <c>Scene.Layers</c> and touch nothing else,
    /// which with folders is not a small inaccuracy: stepping a loose layer past
    /// the bottom of a folder put it <em>between</em> two of that folder's
    /// members while leaving it outside the folder, so the folder no longer
    /// occupied one run and the order the docker drew stopped being the order
    /// things composited in.
    /// </para>
    /// <para>
    /// So a step is defined against the walk, and the layer adopts whatever
    /// container it steps into. Walking up into a folder header means going
    /// <em>inside</em> at the bottom, and walking up off the top of a folder
    /// means coming out of it — which is what an artist watching the row move
    /// would describe, and it is how every other stack with folders in it
    /// behaves.
    /// </para>
    /// </remarks>
    /// <param name="blocked">
    /// Layers the step may not pass — the rest of a multi-layer selection, so a
    /// selection that hits the ceiling compacts against it instead of
    /// scrambling. The layer stays where it is when its next row is one of
    /// these.
    /// </param>
    /// <returns>Whether the layer moved.</returns>
    public static bool StepLayer(
        List<Layer> layers, List<LayerGroup> groups, Layer layer, int delta,
        IReadOnlySet<string>? blocked = null)
    {
        if (delta == 0) return false;
        var nodes = Walk(layers, groups);
        var at = nodes.FindIndex(n => ReferenceEquals(n.Layer, layer));
        if (at < 0) return false;

        var toward = at + (delta > 0 ? 1 : -1);
        if (toward < 0 || toward >= nodes.Count) return false;
        var next = nodes[toward];
        if (next.Layer is { } neighbour && blocked is not null && blocked.Contains(neighbour.Id)) return false;

        var byId = ById(groups);
        if (delta > 0)
        {
            if (next.Group is { } header)
            {
                // The row above is a folder header, so up means in, at its floor.
                layer.GroupId = header.Id;
                Reinsert(layers, layer, FloorOf(header));
            }
            else if (next.Layer is { } above && SameContainer(above.GroupId, layer.GroupId))
            {
                layer.GroupId = above.GroupId;
                Reinsert(layers, layer, layers.IndexOf(above) + 1);
            }
            else if (next.Layer is { } outside)
            {
                // Off the top of our folder and into whatever holds it.
                layer.GroupId = outside.GroupId;
                Reinsert(layers, layer, layers.IndexOf(outside));
            }
        }
        else
        {
            if (next.Group is { } header)
            {
                if (SameContainer(header.Id, layer.GroupId))
                {
                    // Off the floor of our own folder and out under it.
                    layer.GroupId = header.ParentId;
                    Reinsert(layers, layer, FloorOf(header));
                }
                else
                {
                    // An empty folder sitting below us: down means into it.
                    layer.GroupId = header.Id;
                    Reinsert(layers, layer, FloorOf(header));
                }
            }
            else if (next.Layer is { } below && SameContainer(below.GroupId, layer.GroupId))
            {
                layer.GroupId = below.GroupId;
                Reinsert(layers, layer, layers.IndexOf(below));
            }
            else if (next.Layer is { } inner)
            {
                // The top row of the folder below us: down means into it.
                layer.GroupId = inner.GroupId;
                Reinsert(layers, layer, layers.IndexOf(inner) + 1);
            }
        }

        Normalise(layers, groups);
        return true;

        // Where a folder's block starts, once the moving layer is out of the way.
        int FloorOf(LayerGroup group)
        {
            var block = SubtreeLayers(group, layers, byId).Where(l => !ReferenceEquals(l, layer)).ToList();
            return block.Count == 0 ? layers.Count : layers.IndexOf(block[0]);
        }
    }

    /// <summary>Take a layer out and put it back at <paramref name="at"/>, as read before the removal.</summary>
    private static void Reinsert(List<Layer> layers, Layer layer, int at)
    {
        var from = layers.IndexOf(layer);
        if (from >= 0)
        {
            layers.RemoveAt(from);
            if (from < at) at--;
        }
        layers.Insert(Math.Clamp(at, 0, layers.Count), layer);
    }

    /// <summary>
    /// Move a folder one row up or down among its siblings, taking everything
    /// in it.
    /// </summary>
    /// <remarks>
    /// Among its siblings rather than through the walk: a folder stepping one
    /// row at a time would spend most of its steps halfway inside the folder
    /// next to it, and "move this block up" is what the button means. Dragging
    /// is how a folder changes which container it is in.
    /// </remarks>
    /// <returns>Whether the folder moved.</returns>
    public static bool StepGroup(
        List<Layer> layers, List<LayerGroup> groups, LayerGroup group, int delta)
    {
        if (delta == 0) return false;
        var siblings = Walk(layers, groups)
            .Where(n => n.IsGroup && SameContainer(n.Group!.ParentId, group.ParentId))
            .Select(n => n.Group!)
            .ToList();
        var at = siblings.FindIndex(g => g.Id == group.Id);
        var toward = at + (delta > 0 ? 1 : -1);
        if (at < 0 || toward < 0 || toward >= siblings.Count) return false;
        return PlaceGroupBeside(layers, groups, group, null, siblings[toward], above: delta > 0);
    }

    /// <summary>
    /// Put a folder — and everything in it — inside another folder, or at the
    /// top level when <paramref name="parentId"/> is null.
    /// </summary>
    /// <returns>Whether the move was allowed; see <see cref="CanMoveGroupInto"/>.</returns>
    public static bool MoveGroupInto(
        List<Layer> layers, List<LayerGroup> groups, LayerGroup group, string? parentId)
    {
        var parent = parentId is null ? null : groups.FirstOrDefault(g => g.Id == parentId);
        if (parentId is not null && parent is null) return false;
        if (!CanMoveGroupInto(group, parent, groups)) return false;

        group.ParentId = parentId;
        // To the top of the new container, the same rule MoveLayerInto follows.
        var byId = ById(groups);
        var block = SubtreeLayers(group, layers, byId);
        foreach (var layer in block) layers.Remove(layer);
        layers.AddRange(block);
        groups.Remove(group);
        groups.Add(group);
        Normalise(layers, groups);
        return true;
    }

    /// <summary>
    /// Put a folder — and everything in it — directly above or below a row that
    /// is not inside it, adopting that row's container.
    /// </summary>
    /// <param name="anchorLayer">The layer dropped on, or null when dropping beside a folder.</param>
    /// <param name="anchorGroup">The folder dropped beside, or null when dropping beside a layer.</param>
    /// <returns>Whether the move was allowed.</returns>
    public static bool PlaceGroupBeside(
        List<Layer> layers, List<LayerGroup> groups, LayerGroup group,
        Layer? anchorLayer, LayerGroup? anchorGroup, bool above)
    {
        var container = anchorLayer?.GroupId ?? anchorGroup?.ParentId;
        var parent = container is null ? null : groups.FirstOrDefault(g => g.Id == container);
        if (container is not null && parent is null) return false;
        if (!CanMoveGroupInto(group, parent, groups)) return false;
        if (anchorGroup is not null && anchorGroup.Id == group.Id) return false;

        group.ParentId = container;

        var byId = ById(groups);
        var block = SubtreeLayers(group, layers, byId);
        foreach (var layer in block) layers.Remove(layer);

        // The anchor's own block, read after the dragged one is out of the list
        // so an overlapping run cannot be counted twice.
        var anchorBlock = anchorLayer is not null
            ? [anchorLayer]
            : SubtreeLayers(anchorGroup!, layers, byId);

        if (anchorBlock.Count == 0 || block.Count == 0)
        {
            layers.AddRange(block);
        }
        else
        {
            var at = layers.IndexOf(above ? anchorBlock[^1] : anchorBlock[0]);
            if (at < 0) layers.AddRange(block);
            else layers.InsertRange(above ? at + 1 : at, block);
        }

        // The folder list carries sibling order for folders with nothing in
        // them, so it has to move too — beside the anchor folder when there is
        // one, and off the end otherwise, where Normalise will place it from
        // the layer order it now has.
        groups.Remove(group);
        var anchorIndex = anchorGroup is null ? -1 : groups.FindIndex(g => g.Id == anchorGroup.Id);
        if (anchorIndex < 0) groups.Add(group);
        else groups.Insert(above ? anchorIndex + 1 : anchorIndex, group);

        Normalise(layers, groups);
        return true;
    }
}
