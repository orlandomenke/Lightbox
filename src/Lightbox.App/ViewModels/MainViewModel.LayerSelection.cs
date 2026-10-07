using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>
/// Multi-layer selection in the layers docker: Ctrl+click picks layers one at a
/// time, Shift+click takes the run between the anchor and the click.
/// </summary>
/// <remarks>
/// <para>
/// <b>The active layer and the selection are two different things, and both are
/// needed.</b> Exactly one layer is <em>active</em> — it is where the next
/// stroke lands, and there is no sensible answer to "which of these five did
/// the brush mean". The selection is what a docker <em>operation</em> covers:
/// delete these four, hide these three, put these six in a folder. So the
/// active layer is always inside the selection and the selection is never
/// empty; deselecting the last layer is refused rather than allowed to leave
/// the app with nowhere to paint.
/// </para>
/// <para>
/// Layers are held by id rather than by index. A selection outlives the list it
/// was made from — deleting one layer shifts every index above it — so indices
/// would silently come to mean different layers between the click and the
/// operation.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    private readonly HashSet<string> _selectedLayerIds = [];

    /// <summary>Where a Shift+click ranges from: the last layer picked deliberately.</summary>
    private string? _layerAnchorId;

    /// <summary>
    /// True while this file is the one moving the active layer, so the reset in
    /// <see cref="SyncLayerSelectionToActive"/> does not undo the selection that
    /// just asked for the move.
    /// </summary>
    private bool _selectingLayers;

    public IReadOnlySet<string> SelectedLayerIds => _selectedLayerIds;

    /// <summary>The selected layers, bottom of the stack first.</summary>
    public IReadOnlyList<Layer> SelectedLayers =>
        Scene.Layers.Where(l => _selectedLayerIds.Contains(l.Id)).ToList();

    public int SelectedLayerCount => _selectedLayerIds.Count;

    /// <summary>More than one layer picked — drives the wording of the docker's verbs.</summary>
    public bool HasMultiLayerSelection => _selectedLayerIds.Count > 1;

    /// <summary>
    /// The rows a Shift+click ranges over: what the docker actually shows, so a
    /// collapsed folder's members are not swept into a range that visibly
    /// skipped them.
    /// </summary>
    private List<LayerRow> SelectableLayerRows() => LayerPanelItems.OfType<LayerRow>().ToList();

    /// <summary>
    /// A click on a layer row. <paramref name="toggle"/> is Ctrl (add or drop
    /// this one), <paramref name="range"/> is Shift (take the run from the
    /// anchor), neither is a plain click (this layer alone).
    /// </summary>
    public void SelectLayer(LayerRow row, bool toggle, bool range)
    {
        // Ctrl adds to the pick, folders included; any other click starts again.
        if (!toggle) _selectedGroupIds.Clear();
        var rows = SelectableLayerRows();
        var target = row.Layer.Id;

        if (range && _layerAnchorId is { } anchorId)
        {
            var from = rows.FindIndex(r => r.Layer.Id == anchorId);
            var to = rows.FindIndex(r => r.Layer.Id == target);
            // A stale anchor — its folder collapsed, its layer deleted — is not
            // a reason to do nothing; fall through to a plain click, which is
            // what the artist would have got had they never set one.
            if (from >= 0 && to >= 0)
            {
                _selectedLayerIds.Clear();
                for (var i = Math.Min(from, to); i <= Math.Max(from, to); i++)
                {
                    _selectedLayerIds.Add(rows[i].Layer.Id);
                }
                _selectedGroupIds.Clear();
                ActivateWithinSelection(row.SceneIndex);
                RefreshLayerSelectionHighlights();
                return;
            }
        }

        if (toggle)
        {
            if (!_selectedLayerIds.Add(target) && _selectedLayerIds.Count > 1)
            {
                _selectedLayerIds.Remove(target);
                UnpickFoldersHolding(row.Layer);
                if (ActiveLayerIndex == row.SceneIndex
                    && rows.FirstOrDefault(r => _selectedLayerIds.Contains(r.Layer.Id)) is { } next)
                {
                    ActivateWithinSelection(next.SceneIndex);
                }
            }
            else
            {
                // Newly added, or the last one left and so not droppable.
                ActivateWithinSelection(row.SceneIndex);
            }
            _layerAnchorId = target;
            RefreshLayerSelectionHighlights();
            return;
        }

        _selectedLayerIds.Clear();
        _selectedLayerIds.Add(target);
        _layerAnchorId = target;
        ActivateWithinSelection(row.SceneIndex);
        RefreshLayerSelectionHighlights();
    }

    // ---- folders picked by their headers ---------------------------------------

    /// <summary>
    /// The folders picked on their headers, in the order they were picked: Ctrl
    /// picks several, as it picks several layers (B399, Q213).
    /// </summary>
    /// <remarks>
    /// It used to be one id, and Ctrl cleared it — so a folder picked with Ctrl
    /// never lit, a second Ctrl+click could not find anything to take back out,
    /// and with the folders collapsed the click changed nothing on screen.
    /// </remarks>
    private readonly List<string> _selectedGroupIds = [];

    /// <summary>
    /// The folder picked last, or null. A new layer goes inside it, and a new
    /// folder above it.
    /// </summary>
    /// <remarks>
    /// Cleared by anything that picks a layer instead — a plain or Shift click on
    /// a row, a cel click, the arrow-key walk — so it only ever describes the
    /// latest pick. A Ctrl+click on a layer adds to the pick and keeps it.
    /// </remarks>
    public LayerGroup? SelectedGroup =>
        _selectedGroupIds.Count > 0 ? Scene.LayerGroups.FirstOrDefault(g => g.Id == _selectedGroupIds[^1]) : null;

    /// <summary>Every folder picked on its header.</summary>
    public IReadOnlyList<LayerGroup> SelectedGroups =>
        Scene.LayerGroups.Where(g => _selectedGroupIds.Contains(g.Id)).ToList();

    /// <summary>
    /// A click on a folder header: the folder becomes the pick, and its members
    /// the selection, so the docker's verbs (hide, delete, new layer) act on
    /// the folder the artist pointed at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A header used to do nothing when clicked.</b> So "pick the folder,
    /// then add a layer" added the layer beside whatever layer had been active
    /// before — usually not in the folder at all.
    /// </para>
    /// <para>
    /// The topmost member becomes active rather than none: a stroke needs
    /// somewhere to land, and the top of the folder is where a new layer in it
    /// goes too. Ctrl adds the folder — or takes it back out when it is already
    /// picked, or every layer in it is — and Shift takes the run from the
    /// anchor to the folder, as on a layer row.
    /// </para>
    /// </remarks>
    public void SelectGroup(GroupRow header, bool toggle, bool range)
    {
        // Everything inside it, at any depth — and an empty folder is picked
        // all the same (Q204): it is something to add to, move or delete.
        var members = FolderTree.SubtreeLayers(Scene, header.Group);
        var top = members.Count > 0 ? members[^1] : null;

        if (range && _layerAnchorId is { } anchorId)
        {
            var items = LayerPanelItems.ToList();
            var from = items.FindIndex(i => i is LayerRow r && r.Layer.Id == anchorId);
            var to = items.IndexOf(header);
            if (from >= 0 && to >= 0)
            {
                _selectedLayerIds.Clear();
                _selectedGroupIds.Clear();
                var headers = new List<LayerGroup>();
                for (var i = Math.Min(from, to); i <= Math.Max(from, to); i++)
                {
                    if (items[i] is LayerRow r) _selectedLayerIds.Add(r.Layer.Id);
                    if (items[i] is GroupRow g) headers.Add(g.Group);
                }
                foreach (var m in members) _selectedLayerIds.Add(m.Id);
                // A header inside the run is picked only when the run holds the
                // whole folder — collapsed, its rows are behind the header, so
                // the header is the folder. A run that crosses a header and
                // stops part-way through its layers does not pick it: Delete
                // takes a picked folder whole, and would take the layers the
                // run left out (the adversarial review's first find).
                foreach (var folder in headers)
                {
                    var inside = FolderTree.SubtreeLayers(Scene, folder);
                    if (folder.Collapsed)
                    {
                        foreach (var m in inside) _selectedLayerIds.Add(m.Id);
                    }
                    if (inside.All(m => _selectedLayerIds.Contains(m.Id))) _selectedGroupIds.Add(folder.Id);
                }
                if (top is not null) ActivateWithinSelection(Scene.Layers.IndexOf(top));
                RefreshLayerSelectionHighlights();
                return;
            }
        }

        if (toggle && IsPicked(header.Group, members))
        {
            UnpickFolder(header.Group, members);
            return;
        }

        if (!toggle)
        {
            _selectedLayerIds.Clear();
            _selectedGroupIds.Clear();
        }
        foreach (var m in members) _selectedLayerIds.Add(m.Id);
        _selectedGroupIds.Remove(header.Group.Id);
        _selectedGroupIds.Add(header.Group.Id);
        if (top is not null)
        {
            _layerAnchorId = top.Id;
            ActivateWithinSelection(Scene.Layers.IndexOf(top));
        }
        RefreshLayerSelectionHighlights();
    }

    /// <summary>
    /// Picked on its header, or picked one layer at a time until every layer in
    /// it is — the owner's answer to Q213 treats the two alike.
    /// </summary>
    private bool IsPicked(LayerGroup folder, IReadOnlyList<Layer> members) =>
        _selectedGroupIds.Contains(folder.Id)
        || (members.Count > 0 && members.All(m => _selectedLayerIds.Contains(m.Id)));

    /// <summary>
    /// Ctrl+click on a picked folder: it and everything in it leave the pick —
    /// unless nothing would be left, which is refused as for the last layer
    /// (the class remarks: the selection is never empty).
    /// </summary>
    private void UnpickFolder(LayerGroup folder, IReadOnlyList<Layer> members)
    {
        var memberIds = members.Select(m => m.Id).ToHashSet();
        var inside = FolderTree.SubtreeFolders(Scene, folder).Select(f => f.Id).Append(folder.Id).ToHashSet();
        var layersLeft = _selectedLayerIds.Count(id => !memberIds.Contains(id));
        var foldersLeft = _selectedGroupIds.Count(id => !inside.Contains(id));
        if (layersLeft == 0 && foldersLeft == 0)
        {
            RefreshLayerSelectionHighlights();
            return;
        }
        _selectedLayerIds.ExceptWith(memberIds);
        _selectedGroupIds.RemoveAll(inside.Contains);
        // A folder around it is no longer whole either.
        foreach (var outer in FolderTree.Ancestors(Scene, folder)) _selectedGroupIds.Remove(outer.Id);
        if (_selectedLayerIds.Count > 0
            && !(ActiveLayerIndex >= 0 && ActiveLayerIndex < Scene.Layers.Count
                 && _selectedLayerIds.Contains(Scene.Layers[ActiveLayerIndex].Id)))
        {
            ActivateWithinSelection(Scene.Layers.FindLastIndex(l => _selectedLayerIds.Contains(l.Id)));
        }
        if (_layerAnchorId is { } anchor && memberIds.Contains(anchor))
        {
            _layerAnchorId = ActiveLayerIndex >= 0 && ActiveLayerIndex < Scene.Layers.Count
                ? Scene.Layers[ActiveLayerIndex].Id : null;
        }
        RefreshLayerSelectionHighlights();
    }

    /// <summary>A layer Ctrl+clicked out of the pick takes every picked folder around it out too.</summary>
    private void UnpickFoldersHolding(Layer layer) =>
        _selectedGroupIds.RemoveAll(id =>
            Scene.LayerGroups.FirstOrDefault(g => g.Id == id) is { } g && FolderTree.IsWithin(Scene, layer, g));

    /// <summary>
    /// The folders an operation aimed at <paramref name="folder"/> covers: every
    /// picked folder when it is one of them, otherwise just that one — the
    /// folder counterpart of <see cref="LayersForOp"/>.
    /// </summary>
    internal IReadOnlyList<LayerGroup> GroupsForOp(LayerGroup folder) =>
        _selectedGroupIds.Count > 1 && _selectedGroupIds.Contains(folder.Id)
            ? SelectedGroups
            : [folder];

    private void RefreshGroupSelectionHighlights()
    {
        _selectedGroupIds.RemoveAll(id => !Scene.LayerGroups.Any(g => g.Id == id));
        foreach (var header in LayerPanelItems.OfType<GroupRow>())
        {
            header.IsSelected = _selectedGroupIds.Contains(header.Group.Id);
        }
    }

    private void ActivateWithinSelection(int sceneIndex)
    {
        _selectingLayers = true;
        try
        {
            ActiveLayerIndex = sceneIndex;
        }
        finally
        {
            _selectingLayers = false;
        }
    }

    /// <summary>
    /// The active layer moved without going through the docker — a cel click, a
    /// delete, opening a document, the arrow-key walk — so the selection starts
    /// again from it. Anything else would leave a selection describing a stack
    /// the artist has since navigated away from.
    /// </summary>
    // B395: the selection trace names this method; inlined, it vanishes from the stack.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal void SyncLayerSelectionToActive(int sceneIndex)
    {
        if (_selectingLayers)
        {
            RefreshLayerSelectionHighlights();
            return;
        }
        var id = sceneIndex >= 0 && sceneIndex < Scene.Layers.Count ? Scene.Layers[sceneIndex].Id : null;
        _selectedGroupIds.Clear();
        _selectedLayerIds.Clear();
        if (id is not null) _selectedLayerIds.Add(id);
        _layerAnchorId = id;
        RefreshLayerSelectionHighlights();
    }

    /// <summary>
    /// Push the selection onto the rows, dropping ids whose layer has gone.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal void RefreshLayerSelectionHighlights()
    {
        _selectedLayerIds.RemoveWhere(id => !Scene.Layers.Any(l => l.Id == id));
        if (Services.LayerSelectionTrace.On)
        {
            var picked = Enumerable.Range(0, Scene.Layers.Count).Where(i => _selectedLayerIds.Contains(Scene.Layers[i].Id));
            Services.LayerSelectionTrace.Note(
                $"selection now [{string.Join(",", picked)}], active {ActiveLayerIndex}, via {Services.LayerSelectionTrace.Callers()}");
        }
        // An empty folder picked on its own selects no layer, and that is the
        // selection — not a gap for the active layer to fill.
        _selectedGroupIds.RemoveAll(id => !Scene.LayerGroups.Any(g => g.Id == id));
        if (_selectedLayerIds.Count == 0 && Scene.Layers.Count > 0 && _selectedGroupIds.Count == 0)
        {
            var active = Math.Clamp(ActiveLayerIndex, 0, Scene.Layers.Count - 1);
            _selectedLayerIds.Add(Scene.Layers[active].Id);
            _layerAnchorId ??= Scene.Layers[active].Id;
        }
        foreach (var row in LayerRows) row.IsSelected = _selectedLayerIds.Contains(row.Layer.Id);
        RefreshGroupSelectionHighlights();
        OnPropertyChanged(nameof(SelectedLayerCount));
        OnPropertyChanged(nameof(HasMultiLayerSelection));
    }

    /// <summary>
    /// The layers an operation aimed at <paramref name="layer"/> covers: the
    /// whole selection when the layer is inside it, otherwise just that layer.
    /// </summary>
    /// <remarks>
    /// The same shape the timeline uses for cels, and for the same reason:
    /// right-clicking a row outside the selection is an operation on <em>that
    /// row</em>, not a surprise edit to five others.
    /// </remarks>
    internal IReadOnlyList<Layer> LayersForOp(Layer layer) =>
        _selectedLayerIds.Count > 1 && _selectedLayerIds.Contains(layer.Id)
            ? SelectedLayers
            : [layer];

    /// <summary>
    /// Reorder a set of layers by one step, blocking at the ends of the stack.
    /// </summary>
    /// <remarks>
    /// Worked from the end the layers are moving towards, so a selection that
    /// hits the ceiling compacts against it instead of scrambling: the topmost
    /// selected layer is tried first, and each one that cannot move becomes the
    /// barrier for the next. Without the barrier a blocked layer would be
    /// jumped over by the one below it, which reorders a selection the artist
    /// only asked to shift.
    /// </remarks>
    private static void ShiftLayers(List<Layer> list, HashSet<string> ids, int delta)
    {
        if (delta == 0) return;
        var picked = Enumerable.Range(0, list.Count).Where(i => ids.Contains(list[i].Id));
        var order = delta > 0 ? picked.OrderByDescending(i => i) : picked.OrderBy(i => i);
        var barrier = delta > 0 ? list.Count : -1;
        foreach (var from in order.ToList())
        {
            var to = from + delta;
            if (delta > 0 ? to >= barrier : to <= barrier)
            {
                barrier = from; // blocked: everything behind it stacks up here
                continue;
            }
            var layer = list[from];
            list.RemoveAt(from);
            list.Insert(to, layer);
            barrier = to;
        }
    }
}
