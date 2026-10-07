using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.ViewModels;

/// <summary>
/// Copying whole layers and pasting them back as new ones.
/// </summary>
/// <remarks>
/// <para>
/// <b>A third clipboard, and the reason it is not one of the other two.</b> Lines
/// are strokes and the cel clipboard is a run of drawings; a layer is the whole
/// thing — every drawing and hold, its mask, effects, blend mode and opacity.
/// Pasting it has to give back a layer, not drawings on the active one, so it
/// cannot share the cel clipboard's shape. It is also not in the "whichever was
/// copied last" contest between those two: it is reached only from a layer
/// surface, where a layer is the only thing a paste can mean.
/// </para>
/// <para>
/// <b>Within the document, on purpose (Q198).</b> A stroke's clip region is an
/// entry in <em>its document's</em> region table; carried to another document
/// the reference points at nothing and the stroke silently stops being clipped.
/// So the clipboard remembers the editor it was filled from and refuses a paste
/// anywhere else, in words, rather than pasting something subtly different.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    /// <summary>The copied layers, bottom of the stack first, and where they came from.</summary>
    private (DocumentEditor Source, List<Layer> Layers)? _layerClipboard;

    public bool HasLayerClipboard => _layerClipboard is not null;

    /// <summary>
    /// Copy <paramref name="layer"/> — or, when it is inside a multi-selection,
    /// the whole selection. Null means the active layer.
    /// </summary>
    public bool CopyLayers(Layer? layer = null)
    {
        layer ??= ActiveLayer;
        if (layer is null)
        {
            AiStatus = "Nothing to copy — there is no layer.";
            return false;
        }
        var layers = LayersForOp(layer);
        // Ids kept: the copy is a snapshot, and fresh ids are given at paste time,
        // once per paste, so pasting twice gives two independent layers.
        _layerClipboard = (_editor, layers.Select(l => l.Clone()).ToList());
        OnPropertyChanged(nameof(HasLayerClipboard));
        AiStatus = layers.Count == 1 ? $"Layer “{layers[0].Name}” copied." : $"{layers.Count} layers copied.";
        return true;
    }

    /// <summary>
    /// Paste the copied layers directly above the active layer (or the picked
    /// folder's top), in the folder that puts them in, as one undo step. The
    /// pasted layers become the selection.
    /// </summary>
    public bool PasteLayers()
    {
        if (_layerClipboard is not { } clip)
        {
            AiStatus = "The layer clipboard is empty.";
            return false;
        }
        if (!ReferenceEquals(clip.Source, _editor))
        {
            AiStatus = "Those layers were copied from another document — a layer can only be pasted into the document it came from.";
            return false;
        }

        var (anchorId, groupId) = NewLayerPlacement();
        if (groupId is not null && Scene.LayerGroups.FirstOrDefault(g => g.Id == groupId) is { Collapsed: true } closed)
        {
            closed.Collapsed = false;
        }

        var addedIds = new List<string>();
        _editor.Perform(doc =>
        {
            var layers = doc.Scene.Layers;
            var at = anchorId is null ? -1 : layers.FindIndex(l => l.Id == anchorId);
            foreach (var source in clip.Layers)
            {
                var name = LayerCopy.CopyName(source.Name, layers.Select(l => l.Name));
                var copy = LayerCopy.Duplicate(source, name, doc.Scene.FrameCount, groupId);
                if (at < 0) layers.Add(copy);
                else layers.Insert(++at, copy);
                addedIds.Add(copy.Id);
            }
        }, frameContentUnchanged: false);

        SelectLayersById(addedIds);
        _allThumbsDirty = true;
        RefreshThumbnails();
        AiStatus = addedIds.Count == 1 ? "Layer pasted." : $"{addedIds.Count} layers pasted.";
        return true;
    }

    /// <summary>
    /// Make exactly these layers the selection, the topmost active — what a paste
    /// leaves behind, so the next Ctrl+C or a delete acts on what just appeared.
    /// </summary>
    internal void SelectLayersById(IReadOnlyList<string> ids)
    {
        if (ids.Count == 0) return;
        _selectedLayerIds.Clear();
        foreach (var id in ids) _selectedLayerIds.Add(id);
        _layerAnchorId = ids[^1];
        var top = Scene.Layers.FindLastIndex(l => _selectedLayerIds.Contains(l.Id));
        if (top >= 0) ActivateWithinSelection(top);
        RefreshLayerSelectionHighlights();
    }
}
