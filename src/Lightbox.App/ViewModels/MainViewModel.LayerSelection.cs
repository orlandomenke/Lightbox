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
    internal void SyncLayerSelectionToActive(int sceneIndex)
    {
        if (_selectingLayers)
        {
            RefreshLayerSelectionHighlights();
            return;
        }
        var id = sceneIndex >= 0 && sceneIndex < Scene.Layers.Count ? Scene.Layers[sceneIndex].Id : null;
        _selectedLayerIds.Clear();
        if (id is not null) _selectedLayerIds.Add(id);
        _layerAnchorId = id;
        RefreshLayerSelectionHighlights();
    }

    /// <summary>
    /// Push the selection onto the rows, dropping ids whose layer has gone.
    /// </summary>
    internal void RefreshLayerSelectionHighlights()
    {
        _selectedLayerIds.RemoveWhere(id => !Scene.Layers.Any(l => l.Id == id));
        if (_selectedLayerIds.Count == 0 && Scene.Layers.Count > 0)
        {
            var active = Math.Clamp(ActiveLayerIndex, 0, Scene.Layers.Count - 1);
            _selectedLayerIds.Add(Scene.Layers[active].Id);
            _layerAnchorId ??= Scene.Layers[active].Id;
        }
        foreach (var row in LayerRows) row.IsSelected = _selectedLayerIds.Contains(row.Layer.Id);
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
