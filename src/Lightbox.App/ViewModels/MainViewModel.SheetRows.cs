using System.Collections.ObjectModel;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The rows the Timeline and the X-sheet show (Q227): the layer stack as a
/// tree, folders included, folded by the sheet's own state.
/// </summary>
/// <remarks>
/// <para>
/// <b>One list, read by both surfaces.</b> The X-sheet binds it and the track
/// timeline is projected from it, so the two can never disagree about which
/// rows exist or what order they are in. <see cref="LayerRows"/> is still every
/// layer, flat — that is what the canvas, the Layers docker and the thumbnail
/// passes mean by it — and this is the subset the sheet draws, with the
/// folders' own rows between.
/// </para>
/// <para>
/// <b>A row index is not a layer index plus an offset any more.</b> Every
/// conversion from a track row to a layer goes through
/// <see cref="SheetItemAtTrack"/>; the alternative is the same arithmetic in
/// four places, which is how a drag comes to retime the layer above the one
/// that was grabbed.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    /// <summary>The sheet's rows, topmost first: a <see cref="SheetFolderRow"/> or a <see cref="LayerRow"/>.</summary>
    public ObservableCollection<object> SheetRows { get; } = [];

    /// <summary>Folder rows by folder id (and a count, for a folder split in two), kept across rebuilds.</summary>
    private readonly Dictionary<string, SheetFolderRow> _sheetFolders = [];

    /// <summary>The layers that have a row on the sheet, in the order they are drawn.</summary>
    internal IEnumerable<LayerRow> SheetLayerRows => SheetRows.OfType<LayerRow>();

    /// <summary>
    /// Bring <see cref="SheetRows"/> in line with the stack, touching only what
    /// changed — <see cref="RebuildLayerPanel"/>'s reason: a row is a heavy
    /// template, and re-inflating a sheet of them on every edit is a stall.
    /// </summary>
    private void RebuildSheetRows()
    {
        var byLayer = new Dictionary<Layer, LayerRow>(ReferenceEqualityComparer.Instance);
        foreach (var row in LayerRows) byLayer.TryAdd(row.Layer, row);
        var desired = new List<object>(LayerRows.Count + Scene.LayerGroups.Count);
        var used = new HashSet<string>();
        var repeats = new Dictionary<string, int>();
        foreach (var line in FolderTree.SheetRows(Scene, ActiveSheetLayer))
        {
            switch (line.Item)
            {
                case Layer layer when byLayer.TryGetValue(layer, out var row):
                    desired.Add(row);
                    break;
                case LayerGroup group:
                    var key = group.Id;
                    if (line.Continued)
                    {
                        repeats[group.Id] = repeats.GetValueOrDefault(group.Id) + 1;
                        key = $"{group.Id}+{repeats[group.Id]}";
                    }
                    if (!_sheetFolders.TryGetValue(key, out var folder))
                    {
                        _sheetFolders[key] = folder = new SheetFolderRow(this, group);
                    }
                    folder.Group = group;
                    folder.Name = group.Name;
                    folder.Syncing = true;
                    folder.Collapsed = group.SheetCollapsed == true;
                    folder.Pinned = group.SheetPinned == true;
                    folder.Syncing = false;
                    folder.Depth = line.Depth;
                    folder.Color = FolderTree.ColorOf(Scene, group);
                    SyncFolderCells(folder);
                    used.Add(key);
                    desired.Add(folder);
                    break;
            }
        }
        foreach (var gone in _sheetFolders.Keys.Where(id => !used.Contains(id)).ToList())
        {
            _sheetFolders.Remove(gone);
        }
        PatchInPlace(SheetRows, desired);
    }

    /// <summary>
    /// A folder row's cells: one per frame of the sheet, keyed where any layer
    /// inside the folder, at any depth, has a drawing of its own on that frame.
    /// </summary>
    private void SyncFolderCells(SheetFolderRow row)
    {
        var layers = FolderTree.SubtreeLayers(Scene, row.Group);
        while (row.Cells.Count > TimelineExtent) row.Cells.RemoveAt(row.Cells.Count - 1);
        while (row.Cells.Count < TimelineExtent) row.Cells.Add(new FrameCell(row.Cells.Count) { LayerIndex = -1 });
        foreach (var cell in row.Cells)
        {
            cell.IsVirtual = cell.Index >= Scene.FrameCount;
            cell.IsKeyed = !cell.IsVirtual && layers.Any(l => ExposureSheet.FrameAtExactIndex(l, cell.Index) is not null);
            cell.IsCurrent = cell.Index == CurrentFrameIndex;
        }
    }

    /// <summary>
    /// Fold or open a folder on the Timeline and the X-sheet. The Layers docker
    /// keeps its own state and is not touched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A view preference, as the docker's collapse is: saved with the document,
    /// and not an undo step — Ctrl+Z after folding a folder should take back
    /// the last mark, not unfold the sheet.
    /// </para>
    /// <para>
    /// <b>What goes out of sight goes out of the selection.</b> A cel selected
    /// inside the folder would otherwise stay selected with no row to show it,
    /// and the next Delete over the sheet would take a drawing nobody could see
    /// was picked.
    /// </para>
    /// </remarks>
    public void ToggleSheetFold(SheetFolderRow row)
    {
        var group = row.Group;
        // Null rather than false, so an open folder writes no key (optional means absent).
        group.SheetCollapsed = group.SheetCollapsed == true ? null : true;
        MarkDocumentEdited();
        RebuildSheetRows();
        DropSelectionOffTheSheet();
        OnPropertyChanged(nameof(TimelineTracks));
        RefreshTimelineSelection();
    }

    // ---- pinning ---------------------------------------------------------------------

    /// <summary>The layer being drawn on, which Pinned only never hides.</summary>
    private Layer? ActiveSheetLayer =>
        ActiveLayerIndex >= 0 && ActiveLayerIndex < Scene.Layers.Count ? Scene.Layers[ActiveLayerIndex] : null;

    /// <summary>
    /// Krita's switch (Q227): the Timeline and the X-sheet show only the
    /// pinned layers and folders, the layer being drawn on, and the folders
    /// those sit in. With nothing pinned it does nothing.
    /// </summary>
    /// <remarks>
    /// Read from and written to the scene, so it is saved with the document
    /// and belongs to it: a document timed with eight pinned rows opens that
    /// way, and the next one opens however it was left. A view preference, as
    /// a fold is — not an undo step.
    /// </remarks>
    public bool SheetPinnedOnly
    {
        get => Scene.SheetPinnedOnly == true;
        set
        {
            if (SheetPinnedOnly == value) return;
            Scene.SheetPinnedOnly = value ? true : null;
            AfterSheetFilterChanged();
        }
    }

    /// <summary>Whether anything is pinned at all — without it the switch has nothing to show less of.</summary>
    public bool HasSheetPins =>
        Scene.Layers.Any(l => l.SheetPinned == true) || Scene.LayerGroups.Any(g => g.SheetPinned == true);

    internal void SetSheetPinned(Layer layer, bool pinned)
    {
        if ((layer.SheetPinned == true) == pinned) return;
        layer.SheetPinned = pinned ? true : null;
        AfterSheetFilterChanged();
    }

    internal void SetSheetPinned(LayerGroup group, bool pinned)
    {
        if ((group.SheetPinned == true) == pinned) return;
        group.SheetPinned = pinned ? true : null;
        AfterSheetFilterChanged();
    }

    /// <summary>Pin or unpin the layer being drawn on — the shortcut's aim, with no row under a pointer.</summary>
    public void ToggleActiveLayerSheetPin()
    {
        if (ActiveSheetLayer is { } layer) SetSheetPinned(layer, layer.SheetPinned != true);
    }

    /// <summary>
    /// Which rows the sheet shows has changed: rebuild them, let go of what
    /// went out of sight, and tell both surfaces.
    /// </summary>
    private void AfterSheetFilterChanged()
    {
        MarkDocumentEdited();
        // Through the layer panel, so the rows of both dockers re-read the
        // pins they show — a folder's is on its header and on its sheet row.
        foreach (var row in LayerRows) row.SyncSheetPin();
        RebuildLayerPanel();
        DropSelectionOffTheSheet();
        OnPropertyChanged(nameof(SheetPinnedOnly));
        OnPropertyChanged(nameof(HasSheetPins));
        OnPropertyChanged(nameof(TimelineTracks));
        RefreshTimelineSelection();
    }

    /// <summary>Unselect every cel whose layer has no row on the sheet.</summary>
    private void DropSelectionOffTheSheet()
    {
        var shown = SheetLayerRows.Select(r => r.SceneIndex).ToHashSet();
        _keySelection.RemoveWhere(k => k.IsCel && !shown.Contains(k.LayerIndex));
    }

    /// <summary>
    /// What a track row of the timeline stands for among the sheet's rows —
    /// a <see cref="LayerRow"/>, a <see cref="SheetFolderRow"/> — or null for
    /// the rows above them (the camera, the rig) and for no row at all.
    /// </summary>
    internal object? SheetItemAtTrack(int trackIndex)
    {
        var index = trackIndex - TracksAboveLayers;
        return index >= 0 && index < SheetRows.Count ? SheetRows[index] : null;
    }

    /// <summary>The track row a layer is drawn on, or -1 when it is folded away.</summary>
    internal int TrackOfLayer(int sceneIndex)
    {
        for (var i = 0; i < SheetRows.Count; i++)
        {
            if (SheetRows[i] is LayerRow row && row.SceneIndex == sceneIndex) return TracksAboveLayers + i;
        }
        return -1;
    }

    /// <summary>A folder's track: its summary's marks, and the chevron.</summary>
    private static Controls.TrackRow FolderTrack(SheetFolderRow folder)
    {
        var keys = folder.Cells.Where(c => c.IsKeyed && !c.IsVirtual).Select(c => c.Index).ToList();
        return new Controls.TrackRow(
            Indented(folder.Name, folder.Depth), keys, keys, keys.Select(_ => false).ToList(),
            Controls.TrackKind.Folder, HasChildren: true, Folded: folder.Collapsed);
    }

    /// <summary>A track's name, stepped in by how deep its row is.</summary>
    private static string Indented(string name, int depth) =>
        depth <= 0 ? name : new string(' ', depth * 2) + name;
}
