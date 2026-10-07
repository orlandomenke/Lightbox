using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;
using Lightbox.Raster;

namespace Lightbox.App.ViewModels;

/// <summary>Which piece of a folder shape's bracket a docker row draws (Q215).</summary>
public enum LayerShapeMark
{
    /// <summary>Neither the shape nor kept inside one — no bracket.</summary>
    None,

    /// <summary>The topmost layer kept inside: the corner, with the line running down.</summary>
    KeptTop,

    /// <summary>Kept inside, with another kept layer above it: the line straight through.</summary>
    Kept,

    /// <summary>The folder's shape layer: where the bracket lands.</summary>
    Shape,
}

/// <summary>
/// A folder that keeps its layers inside a shape (Q215) — Krita's inherit
/// alpha, set once on the folder, and said out loud in the docker.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Whether this layer is its folder's shape.</summary>
    internal bool IsFolderShape(Layer layer) =>
        layer.GroupId is not null && FolderTree.Folder(Scene, layer.GroupId)?.ShapeLayerId == layer.Id;

    /// <summary>
    /// Make <paramref name="layer"/> its folder's shape, so every layer above
    /// it in the folder shows only where it and everything under it in the
    /// folder have content — or release the folder. One undo step.
    /// </summary>
    /// <remarks>
    /// A loose layer has no folder to keep anything inside, and the artist is
    /// told so rather than the command doing nothing: "nothing happened" is
    /// Krita's inherit alpha outside a group, the confusion this replaces.
    /// </remarks>
    internal void SetFolderShape(Layer layer, bool keepInside, string? labelPrefix = null)
    {
        if (FolderTree.Folder(Scene, layer.GroupId) is not { } folder)
        {
            if (keepInside)
            {
                AiStatus = $"Put “{layer.Name}” in a folder first — the folder is what keeps the layers above it inside.";
            }
            return;
        }
        if (keepInside == (folder.ShapeLayerId == layer.Id)) return;
        _editor.Perform(_ => folder.ShapeLayerId = keepInside ? layer.Id : null,
            label: labelPrefix + (keepInside ? "Keep layers above inside" : "Stop keeping layers inside"),
            frameContentUnchanged: true);
        SyncMaskRows();
        AiStatus = keepInside
            ? $"Layers above “{layer.Name}” in “{folder.Name}” now stay inside it and everything under it."
            : $"Layers in “{folder.Name}” are no longer kept inside “{layer.Name}”.";
    }

    /// <summary>
    /// The MCP surface's door to <see cref="SetFolderShape"/>: refuses rather
    /// than reporting in the status bar, because an agent reads the reply.
    /// </summary>
    internal string? ExternalSetFolderShape(string layerId, bool keepInside, out string? folderId)
    {
        folderId = null;
        if (Scene.Layers.FirstOrDefault(l => l.Id == layerId) is not { } layer)
            return $"No layer \"{layerId}\".";
        if (FolderTree.Folder(Scene, layer.GroupId) is not { } folder)
            return $"Layer \"{layer.Name}\" is in no folder; a shape keeps the layers above it inside its folder, so group it first.";
        if (FolderTree.LockedFolderOf(Scene, layer) is { } locked)
            return $"Folder \"{locked.Name}\" is locked.";
        folderId = folder.Id;
        if (keepInside || folder.ShapeLayerId == layer.Id) SetFolderShape(layer, keepInside, "Agent: ");
        return null;
    }

    [RelayCommand]
    private void ToggleActiveLayerFolderShape()
    {
        if (ActiveLayer is { } layer) SetFolderShape(layer, !IsFolderShape(layer));
    }

    /// <summary>
    /// Which piece of the bracket this row draws — decided by the row's
    /// neighbours, like the link bracket, so a run reads as one line.
    /// </summary>
    internal LayerShapeMark ShapeMarkOf(Layer layer)
    {
        var layers = Scene.Layers;
        var index = layers.IndexOf(layer);
        if (index < 0) return LayerShapeMark.None;
        if (IsFolderShape(layer)) return LayerShapeMark.Shape;
        if (LayerShapes.FolderShapingOf(Scene, index) is not { } folder) return LayerShapeMark.None;
        // Scene.Layers is bottom-first; "above" in the docker is the next index up.
        var aboveIsKept = index + 1 < layers.Count
            && ReferenceEquals(LayerShapes.FolderShapingOf(Scene, index + 1), folder);
        return aboveIsKept ? LayerShapeMark.Kept : LayerShapeMark.KeptTop;
    }

    /// <summary>What the bracket means on this row, in words.</summary>
    internal string ShapeTipOf(Layer layer)
    {
        if (IsFolderShape(layer))
        {
            return $"The shape of “{FolderTree.Folder(Scene, layer.GroupId)!.Name}” — layers above it in the folder "
                + "show only where this layer and the ones under it have content. Right-click to release.";
        }
        var index = Scene.Layers.IndexOf(layer);
        if (index >= 0 && LayerShapes.FolderShapingOf(Scene, index) is { } folder
            && Scene.Layers.FirstOrDefault(l => l.Id == folder.ShapeLayerId) is { } shape)
        {
            return $"Kept inside “{shape.Name}” and everything under it in “{folder.Name}”.";
        }
        return "";
    }

    /// <summary>
    /// Why a carve on this row is showing nothing, or not carving — null when
    /// all is as it looks. Structural, not per frame: a row cannot keep up
    /// with the playhead, and these are the cases an artist trips over.
    /// </summary>
    internal string? ShapeWarningOf(Layer layer)
    {
        var index = Scene.Layers.IndexOf(layer);
        if (index < 0) return null;
        if (layer.IsClipped && LayerShapes.BaseOf(Scene.Layers, index) is null)
        {
            return layer.GroupId is null
                ? "Nothing below to clip to — this layer shows unclipped."
                : "Nothing below in this folder to clip to — this layer shows unclipped.";
        }
        if (LayerShapes.FolderShapingOf(Scene, index) is { } folder && !ShapeHasAVisibleLayer(folder))
        {
            return $"The shape of “{folder.Name}” is hidden — this layer shows nothing until a layer at or under the shape is visible.";
        }
        return null;
    }

    private bool ShapeHasAVisibleLayer(LayerGroup folder)
    {
        var top = Scene.Layers.FindIndex(l => l.Id == folder.ShapeLayerId);
        for (var i = top; i >= 0 && FolderTree.IsWithin(Scene, Scene.Layers[i], folder); i--)
        {
            var member = Scene.Layers[i];
            if (!member.IsAdjustment && Scene.IsLayerVisible(member)) return true;
        }
        return false;
    }
}
