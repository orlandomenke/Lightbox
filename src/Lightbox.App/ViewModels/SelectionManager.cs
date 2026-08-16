namespace Lightbox.App.ViewModels;

/// <summary>
/// Unified selection manager for canvas objects (placements, guides, reference boxes, anchors, collision shapes).
/// Supports multi-object selection, keyboard modifiers, and selection state tracking.
/// </summary>
public sealed class SelectionManager
{
    /// <summary>Currently selected object IDs by category.</summary>
    private readonly HashSet<string> _selectedPlacementIds = [];

    /// <summary>
    /// Guides picked on the canvas, by <c>Guide.Id</c>.
    /// </summary>
    /// <remarks>
    /// <b>B215.</b> By id for the reason the stroke set below gives at length,
    /// and it applied here all along: <c>RemoveGuide</c> exists, so a selection
    /// held by position comes to mean a different guide the moment one before
    /// it goes — and an undo replaces the whole list, which
    /// <c>MainViewModel.SelectedGuides</c> already had to work around by
    /// resolving to references before a drag rather than during one.
    /// </remarks>
    private readonly HashSet<string> _selectedGuideIds = [];

    private readonly HashSet<int> _selectedRefBoxIndices = [];
    private readonly HashSet<string> _selectedAnchorIds = [];
    private readonly HashSet<string> _selectedShapeIds = [];

    /// <summary>
    /// Strokes picked with the Select tool, by <c>Stroke.Id</c>.
    /// </summary>
    /// <remarks>
    /// <b>Ids rather than record positions, and the difference matters.</b>
    /// <c>StrokePicker</c> answers in positions because it queries an index built
    /// over one list, and a position is what the renderer replays. A
    /// <i>selection</i> outlives that list: delete one stroke and every position
    /// after it shifts, so a selection held by position would silently come to
    /// mean different strokes. The caller converts once, at the point of picking.
    /// </remarks>
    private readonly HashSet<string> _selectedStrokeIds = [];

    /// <summary>Raised when selection changes.</summary>
    public event Action? SelectionChanged;

    /// <summary>True if there are any selected objects.</summary>
    public bool HasSelection => _selectedPlacementIds.Count > 0 || _selectedGuideIds.Count > 0 || _selectedRefBoxIndices.Count > 0 || _selectedAnchorIds.Count > 0 || _selectedShapeIds.Count > 0 || _selectedStrokeIds.Count > 0;

    /// <summary>Number of selected objects across all types.</summary>
    public int SelectionCount => _selectedPlacementIds.Count + _selectedGuideIds.Count + _selectedRefBoxIndices.Count + _selectedAnchorIds.Count + _selectedShapeIds.Count + _selectedStrokeIds.Count;

    /// <summary>Get all selected placement IDs.</summary>
    public IReadOnlySet<string> SelectedPlacementIds => _selectedPlacementIds;

    /// <summary>Get all selected guide ids.</summary>
    public IReadOnlySet<string> SelectedGuideIds => _selectedGuideIds;

    /// <summary>Get all selected reference box indices.</summary>
    public IReadOnlySet<int> SelectedRefBoxIndices => _selectedRefBoxIndices;

    /// <summary>Get all selected anchor IDs.</summary>
    public IReadOnlySet<string> SelectedAnchorIds => _selectedAnchorIds;

    /// <summary>Get all selected collision shape IDs.</summary>
    public IReadOnlySet<string> SelectedShapeIds => _selectedShapeIds;

    /// <summary>Get all selected stroke IDs.</summary>
    public IReadOnlySet<string> SelectedStrokeIds => _selectedStrokeIds;

    /// <summary>Check if a stroke is selected.</summary>
    public bool IsStrokeSelected(string strokeId) => _selectedStrokeIds.Contains(strokeId);

    /// <summary>Check if a placement is selected.</summary>
    public bool IsPlacementSelected(string placementId) => _selectedPlacementIds.Contains(placementId);

    /// <summary>Check if a guide is selected.</summary>
    public bool IsGuideSelected(string guideId) => _selectedGuideIds.Contains(guideId);

    /// <summary>Check if a reference box is selected.</summary>
    public bool IsRefBoxSelected(int boxIndex) => _selectedRefBoxIndices.Contains(boxIndex);

    /// <summary>Check if an anchor is selected.</summary>
    public bool IsAnchorSelected(string anchorId) => _selectedAnchorIds.Contains(anchorId);

    /// <summary>Check if a collision shape is selected.</summary>
    public bool IsShapeSelected(string shapeId) => _selectedShapeIds.Contains(shapeId);

    /// <summary>Select a single placement (clears other selections).</summary>
    public void SelectPlacement(string placementId)
    {
        if (_selectedPlacementIds.Count == 1 && _selectedPlacementIds.Contains(placementId))
            return; // Already selected alone

        ClearAllSelections();
        _selectedPlacementIds.Add(placementId);
        SelectionChanged?.Invoke();
    }

    /// <summary>Add a placement to selection (multi-select).</summary>
    public void AddPlacementToSelection(string placementId)
    {
        if (_selectedPlacementIds.Add(placementId))
            SelectionChanged?.Invoke();
    }

    /// <summary>Remove a placement from selection.</summary>
    public void RemovePlacementFromSelection(string placementId)
    {
        if (_selectedPlacementIds.Remove(placementId))
            SelectionChanged?.Invoke();
    }

    /// <summary>Toggle placement selection (add if not selected, remove if selected).</summary>
    public void TogglePlacementSelection(string placementId)
    {
        if (!_selectedPlacementIds.Remove(placementId))
            _selectedPlacementIds.Add(placementId);
        SelectionChanged?.Invoke();
    }

    /// <summary>Select a single guide (clears other selections).</summary>
    public void SelectGuide(string guideId)
    {
        if (_selectedGuideIds.Count == 1 && _selectedGuideIds.Contains(guideId))
            return;

        ClearAllSelections();
        _selectedGuideIds.Add(guideId);
        SelectionChanged?.Invoke();
    }

    /// <summary>Add a guide to selection.</summary>
    public void AddGuideToSelection(string guideId)
    {
        if (_selectedGuideIds.Add(guideId))
            SelectionChanged?.Invoke();
    }

    /// <summary>
    /// Drop the guide selection, leaving every other category alone.
    /// </summary>
    /// <remarks>
    /// A press that missed every guide is what calls this, and it has said
    /// nothing about the placement or the anchor that may also be picked — so
    /// <see cref="ClearAllSelections"/> would be answering a question nobody
    /// asked.
    /// </remarks>
    public void ClearGuideSelection()
    {
        if (_selectedGuideIds.Count == 0) return;
        _selectedGuideIds.Clear();
        SelectionChanged?.Invoke();
    }

    /// <summary>Select a single reference box (clears other selections).</summary>
    public void SelectRefBox(int boxIndex)
    {
        if (_selectedRefBoxIndices.Count == 1 && _selectedRefBoxIndices.Contains(boxIndex))
            return;

        ClearAllSelections();
        _selectedRefBoxIndices.Add(boxIndex);
        SelectionChanged?.Invoke();
    }

    /// <summary>Add a reference box to selection.</summary>
    public void AddRefBoxToSelection(int boxIndex)
    {
        if (_selectedRefBoxIndices.Add(boxIndex))
            SelectionChanged?.Invoke();
    }

    /// <summary>Select a single stroke (clears other selections).</summary>
    public void SelectStroke(string strokeId)
    {
        if (_selectedStrokeIds.Count == 1 && _selectedStrokeIds.Contains(strokeId))
            return;

        ClearAllSelections();
        _selectedStrokeIds.Add(strokeId);
        SelectionChanged?.Invoke();
    }

    /// <summary>Add a stroke to selection (multi-select).</summary>
    public void AddStrokeToSelection(string strokeId)
    {
        if (_selectedStrokeIds.Add(strokeId))
            SelectionChanged?.Invoke();
    }

    /// <summary>Remove a stroke from selection.</summary>
    public void RemoveStrokeFromSelection(string strokeId)
    {
        if (_selectedStrokeIds.Remove(strokeId))
            SelectionChanged?.Invoke();
    }

    /// <summary>Toggle stroke selection.</summary>
    public void ToggleStrokeSelection(string strokeId)
    {
        if (!_selectedStrokeIds.Remove(strokeId))
            _selectedStrokeIds.Add(strokeId);
        SelectionChanged?.Invoke();
    }

    /// <summary>Handle stroke selection with modifiers.</summary>
    public void SelectStrokeWithModifiers(string strokeId, bool shift, bool alt)
    {
        if (alt)
            RemoveStrokeFromSelection(strokeId);
        else if (shift)
            ToggleStrokeSelection(strokeId);
        else
            SelectStroke(strokeId);
    }

    /// <summary>
    /// Replace the stroke selection with a whole set — what a marquee produces.
    /// </summary>
    /// <remarks>
    /// One notification for the set rather than one per stroke. A marquee over a
    /// busy frame can catch a hundred strokes, and a repaint per stroke is a
    /// hundred repaints of the same rectangle.
    /// </remarks>
    public void SelectStrokes(IEnumerable<string> strokeIds, bool add = false)
    {
        if (!add) ClearAllSelections();
        var changed = false;
        foreach (var id in strokeIds) changed |= _selectedStrokeIds.Add(id);
        if (changed || !add) SelectionChanged?.Invoke();
    }

    /// <summary>
    /// Drop any selected stroke that is no longer in the record.
    /// </summary>
    /// <remarks>
    /// Called after an edit that can remove strokes — a delete, an undo, moving
    /// to another frame. Ids are stable but not eternal, and a selection holding
    /// an id nothing resolves would draw no handles while still reporting a
    /// selection, which reads as the tool having stopped working.
    /// </remarks>
    public void RetainStrokes(IReadOnlySet<string> stillPresent)
    {
        if (_selectedStrokeIds.Count == 0) return;
        if (_selectedStrokeIds.RemoveWhere(id => !stillPresent.Contains(id)) > 0)
            SelectionChanged?.Invoke();
    }

    /// <summary>Clear all selections.</summary>
    public void ClearAllSelections()
    {
        bool hadSelection = HasSelection;
        _selectedStrokeIds.Clear();
        _selectedPlacementIds.Clear();
        _selectedGuideIds.Clear();
        _selectedRefBoxIndices.Clear();
        _selectedAnchorIds.Clear();
        _selectedShapeIds.Clear();
        if (hadSelection)
            SelectionChanged?.Invoke();
    }

    /// <summary>
    /// Handle selection with keyboard modifiers.
    /// shift=true adds to selection; alt=true removes; neither replaces.
    /// </summary>
    public void SelectPlacementWithModifiers(string placementId, bool shift, bool alt)
    {
        if (alt)
            RemovePlacementFromSelection(placementId);
        else if (shift)
            AddPlacementToSelection(placementId);
        else
            SelectPlacement(placementId);
    }

    /// <summary>Handle guide selection with modifiers.</summary>
    public void SelectGuideWithModifiers(string guideId, bool shift, bool alt)
    {
        if (alt)
        {
            if (_selectedGuideIds.Remove(guideId))
                SelectionChanged?.Invoke();
        }
        else if (shift)
            AddGuideToSelection(guideId);
        else
            SelectGuide(guideId);
    }

    /// <summary>Handle reference box selection with modifiers.</summary>
    public void SelectRefBoxWithModifiers(int boxIndex, bool shift, bool alt)
    {
        if (alt)
        {
            if (_selectedRefBoxIndices.Remove(boxIndex))
                SelectionChanged?.Invoke();
        }
        else if (shift)
            AddRefBoxToSelection(boxIndex);
        else
            SelectRefBox(boxIndex);
    }

    /// <summary>Select a single anchor (clears other selections).</summary>
    public void SelectAnchor(string anchorId)
    {
        if (_selectedAnchorIds.Count == 1 && _selectedAnchorIds.Contains(anchorId))
            return;

        ClearAllSelections();
        _selectedAnchorIds.Add(anchorId);
        SelectionChanged?.Invoke();
    }

    /// <summary>Add an anchor to selection.</summary>
    public void AddAnchorToSelection(string anchorId)
    {
        if (_selectedAnchorIds.Add(anchorId))
            SelectionChanged?.Invoke();
    }

    /// <summary>Remove an anchor from selection.</summary>
    public void RemoveAnchorFromSelection(string anchorId)
    {
        if (_selectedAnchorIds.Remove(anchorId))
            SelectionChanged?.Invoke();
    }

    /// <summary>Select a single collision shape (clears other selections).</summary>
    public void SelectShape(string shapeId)
    {
        if (_selectedShapeIds.Count == 1 && _selectedShapeIds.Contains(shapeId))
            return;

        ClearAllSelections();
        _selectedShapeIds.Add(shapeId);
        SelectionChanged?.Invoke();
    }

    /// <summary>Add a collision shape to selection.</summary>
    public void AddShapeToSelection(string shapeId)
    {
        if (_selectedShapeIds.Add(shapeId))
            SelectionChanged?.Invoke();
    }

    /// <summary>Remove a collision shape from selection.</summary>
    public void RemoveShapeFromSelection(string shapeId)
    {
        if (_selectedShapeIds.Remove(shapeId))
            SelectionChanged?.Invoke();
    }

    /// <summary>Handle anchor selection with modifiers.</summary>
    public void SelectAnchorWithModifiers(string anchorId, bool shift, bool alt)
    {
        if (alt)
            RemoveAnchorFromSelection(anchorId);
        else if (shift)
            AddAnchorToSelection(anchorId);
        else
            SelectAnchor(anchorId);
    }

    /// <summary>Handle collision shape selection with modifiers.</summary>
    public void SelectShapeWithModifiers(string shapeId, bool shift, bool alt)
    {
        if (alt)
            RemoveShapeFromSelection(shapeId);
        else if (shift)
            AddShapeToSelection(shapeId);
        else
            SelectShape(shapeId);
    }
}
