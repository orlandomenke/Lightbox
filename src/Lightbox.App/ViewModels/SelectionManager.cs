namespace Lightbox.App.ViewModels;

/// <summary>
/// Unified selection manager for canvas objects (placements, guides, reference boxes, anchors, collision shapes).
/// Supports multi-object selection, keyboard modifiers, and selection state tracking.
/// </summary>
public sealed class SelectionManager
{
    /// <summary>Currently selected object IDs by category.</summary>
    private readonly HashSet<string> _selectedPlacementIds = [];
    private readonly HashSet<int> _selectedGuideIndices = [];
    private readonly HashSet<int> _selectedRefBoxIndices = [];
    private readonly HashSet<string> _selectedAnchorIds = [];
    private readonly HashSet<string> _selectedShapeIds = [];

    /// <summary>Raised when selection changes.</summary>
    public event Action? SelectionChanged;

    /// <summary>True if there are any selected objects.</summary>
    public bool HasSelection => _selectedPlacementIds.Count > 0 || _selectedGuideIndices.Count > 0 || _selectedRefBoxIndices.Count > 0 || _selectedAnchorIds.Count > 0 || _selectedShapeIds.Count > 0;

    /// <summary>Number of selected objects across all types.</summary>
    public int SelectionCount => _selectedPlacementIds.Count + _selectedGuideIndices.Count + _selectedRefBoxIndices.Count + _selectedAnchorIds.Count + _selectedShapeIds.Count;

    /// <summary>Get all selected placement IDs.</summary>
    public IReadOnlySet<string> SelectedPlacementIds => _selectedPlacementIds;

    /// <summary>Get all selected guide indices.</summary>
    public IReadOnlySet<int> SelectedGuideIndices => _selectedGuideIndices;

    /// <summary>Get all selected reference box indices.</summary>
    public IReadOnlySet<int> SelectedRefBoxIndices => _selectedRefBoxIndices;

    /// <summary>Get all selected anchor IDs.</summary>
    public IReadOnlySet<string> SelectedAnchorIds => _selectedAnchorIds;

    /// <summary>Get all selected collision shape IDs.</summary>
    public IReadOnlySet<string> SelectedShapeIds => _selectedShapeIds;

    /// <summary>Check if a placement is selected.</summary>
    public bool IsPlacementSelected(string placementId) => _selectedPlacementIds.Contains(placementId);

    /// <summary>Check if a guide is selected.</summary>
    public bool IsGuideSelected(int guideIndex) => _selectedGuideIndices.Contains(guideIndex);

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
    public void SelectGuide(int guideIndex)
    {
        if (_selectedGuideIndices.Count == 1 && _selectedGuideIndices.Contains(guideIndex))
            return;

        ClearAllSelections();
        _selectedGuideIndices.Add(guideIndex);
        SelectionChanged?.Invoke();
    }

    /// <summary>Add a guide to selection.</summary>
    public void AddGuideToSelection(int guideIndex)
    {
        if (_selectedGuideIndices.Add(guideIndex))
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

    /// <summary>Clear all selections.</summary>
    public void ClearAllSelections()
    {
        bool hadSelection = HasSelection;
        _selectedPlacementIds.Clear();
        _selectedGuideIndices.Clear();
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
    public void SelectGuideWithModifiers(int guideIndex, bool shift, bool alt)
    {
        if (alt)
        {
            if (_selectedGuideIndices.Remove(guideIndex))
                SelectionChanged?.Invoke();
        }
        else if (shift)
            AddGuideToSelection(guideIndex);
        else
            SelectGuide(guideIndex);
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
