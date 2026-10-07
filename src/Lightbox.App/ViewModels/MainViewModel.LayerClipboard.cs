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
    /// <summary>The copied layers and folders, and the editor they came from.</summary>
    private (DocumentEditor Source, StackClip Clip)? _layerClipboard;

    public bool HasLayerClipboard => _layerClipboard is not null;

    /// <summary>
    /// Copy <paramref name="layer"/> — or, when it is inside a multi-selection,
    /// the whole selection. Null means what the docker has picked: a folder
    /// picked on its header comes with everything inside it (Q204), an empty
    /// one included; otherwise the active layer.
    /// </summary>
    public bool CopyLayers(Layer? layer = null)
    {
        List<StackRef> items;
        if (layer is null && SelectedGroup is not null)
        {
            items = SelectedStackItems();
        }
        else
        {
            layer ??= ActiveLayer;
            if (layer is null)
            {
                AiStatus = "Nothing to copy \u2014 there is no layer.";
                return false;
            }
            items = LayersForOp(layer).Select(StackRef.Of).ToList();
        }
        // Ids kept: the copy is a snapshot, and fresh ids are given at paste time,
        // once per paste, so pasting twice gives two independent sets.
        var clip = FolderCopy.Copy(Scene, items);
        _layerClipboard = (_editor, clip);
        OnPropertyChanged(nameof(HasLayerClipboard));
        AiStatus = Describe(clip, "copied");
        return true;
    }

    /// <summary>"Layer “Ink” copied.", "Folder “Hero” copied.", "3 layers and a folder copied."</summary>
    private static string Describe(StackClip clip, string verb)
    {
        var roots = clip.Roots;
        if (roots.Count == 1)
        {
            var name = roots[0].IsFolder
                ? clip.Folders.First(f => f.Id == roots[0].Id).Name
                : clip.Layers.First(l => l.Id == roots[0].Id).Name;
            return $"{(roots[0].IsFolder ? "Folder" : "Layer")} \u201c{name}\u201d {verb}.";
        }
        var folders = roots.Count(r => r.IsFolder);
        var layers = roots.Count - folders;
        var parts = new List<string>();
        if (layers > 0) parts.Add(layers == 1 ? "a layer" : $"{layers} layers");
        if (folders > 0) parts.Add(folders == 1 ? "a folder" : $"{folders} folders");
        var text = string.Join(" and ", parts);
        return char.ToUpperInvariant(text[0]) + text[1..] + $" {verb}.";
    }

    /// <summary>
    /// Paste what was copied where a new layer would go — directly above the
    /// active layer in its folder, or at the top inside a picked folder — as
    /// one undo step. A pasted folder is picked; pasted layers become the
    /// selection.
    /// </summary>
    public bool PasteLayers()
    {
        if (_layerClipboard is not { } held)
        {
            AiStatus = "The layer clipboard is empty.";
            return false;
        }
        if (!ReferenceEquals(held.Source, _editor))
        {
            AiStatus = "Those layers were copied from another document \u2014 a layer can only be pasted into the document it came from.";
            return false;
        }

        var place = NewLayerPlacement();
        var roots = new List<StackRef>();
        var addedIds = new List<string>();
        _editor.Perform(doc =>
        {
            var before = doc.Scene.Layers.Count;
            roots = FolderCopy.Paste(doc.Scene, held.Clip, doc.Scene.FrameCount);
            addedIds = doc.Scene.Layers.Skip(before).Select(l => l.Id).ToList();
            // Wherever a new layer would go — into a picked folder at any depth,
            // or above the active layer in its folder — as one run (Q204).
            if (place is { } p) FolderTree.Move(doc.Scene, roots, p.Target, p.Where);
            foreach (var root in roots)
            {
                var landed = FolderTree.Find(doc.Scene, root);
                var above = landed switch
                {
                    Layer l => FolderTree.FoldersOf(doc.Scene, l),
                    LayerGroup g => FolderTree.Ancestors(doc.Scene, g),
                    _ => [],
                };
                foreach (var folder in above) folder.Collapsed = false;
            }
        }, frameContentUnchanged: false);

        if (roots is [{ IsFolder: true } only]) PickFolder(only.Id);
        else SelectLayersById(addedIds);
        _allThumbsDirty = true;
        RefreshThumbnails();
        AiStatus = Describe(held.Clip, "pasted");
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
