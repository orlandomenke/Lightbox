namespace Lightbox.Core.Documents;

/// <summary>
/// What a layer copy holds once folders are in it: the layers, the folders
/// they sit in, and which of those were picked (the roots).
/// </summary>
/// <param name="Layers">Copies of the layers, bottom of the stack first, still carrying their original ids.</param>
/// <param name="Folders">Copies of the folders, still carrying their original ids.</param>
/// <param name="Roots">What was picked, topmost first — everything else came along inside a picked folder.</param>
public sealed record StackClip(List<Layer> Layers, List<LayerGroup> Folders, List<StackRef> Roots);

/// <summary>
/// Copying layers and whole folders, and pasting them back as new ones (Q204).
/// </summary>
/// <remarks>
/// <para>
/// The copy is a snapshot taken by id; fresh ids are handed out at paste time,
/// once per paste, so pasting twice gives two independent sets — the same rule
/// <see cref="LayerCopy"/> follows for a single layer.
/// </para>
/// <para>
/// Inside a pasted folder everything keeps its place relative to the rest:
/// a layer stays in its folder, a folder in its parent, and an empty folder on
/// the copied layer it sat under. Only the roots land somewhere new, and where
/// is the caller's to decide.
/// </para>
/// </remarks>
public static class FolderCopy
{
    /// <summary>Snapshot the picked layers and folders, each folder with everything inside it.</summary>
    public static StackClip Copy(Scene scene, IReadOnlyList<StackRef> items)
    {
        var picked = items.Select(i => FolderTree.Find(scene, i)).Where(i => i is not null).Select(i => i!).ToList();
        var pickedFolders = picked.OfType<LayerGroup>().ToList();
        var folders = new List<LayerGroup>();
        foreach (var f in pickedFolders)
        {
            if (!folders.Contains(f)) folders.Add(f);
            foreach (var inner in FolderTree.SubtreeFolders(scene, f))
            {
                if (!folders.Contains(inner)) folders.Add(inner);
            }
        }
        // Something inside a picked folder comes along with it, not as a root.
        var roots = picked
            .Where(p => !pickedFolders.Any(f => !ReferenceEquals(p, f) && p switch
            {
                Layer l => FolderTree.IsWithin(scene, l, f),
                LayerGroup g => FolderTree.IsWithin(scene, g, f),
                _ => false,
            }))
            .ToList();
        var rootLayers = roots.OfType<Layer>().ToHashSet();
        var layers = scene.Layers
            .Where(l => rootLayers.Contains(l) || pickedFolders.Any(f => FolderTree.IsWithin(scene, l, f)))
            .Select(l =>
            {
                var copy = l.Clone();
                if (rootLayers.Contains(l)) copy.GroupId = null;
                return copy;
            })
            .ToList();
        var folderCopies = folders.Select(f =>
        {
            var copy = f.Clone();
            if (roots.Contains(f)) copy.ParentId = null;
            return copy;
        }).ToList();
        return new StackClip(layers, folderCopies, roots.Select(StackRef.Of).ToList());
    }

    /// <summary>
    /// Add a clip's contents to <paramref name="scene"/> under fresh ids — layers
    /// on top of the stack, folders at the top level — and return the new roots,
    /// topmost first, for the caller to move into place.
    /// </summary>
    /// <param name="frameCount">The scene's frame count, which a pasted layer's cels are fitted to.</param>
    public static List<StackRef> Paste(Scene scene, StackClip clip, int frameCount)
    {
        var folderIds = clip.Folders.ToDictionary(f => f.Id, _ => Ids.NewId("group"));
        var layerIds = new Dictionary<string, string>();
        foreach (var source in clip.Layers)
        {
            var name = LayerCopy.CopyName(source.Name, scene.Layers.Select(l => l.Name));
            var group = source.GroupId is { } g && folderIds.TryGetValue(g, out var mapped) ? mapped : null;
            var copy = LayerCopy.Duplicate(source, name, frameCount, group);
            scene.Layers.Add(copy);
            layerIds[source.Id] = copy.Id;
        }
        var rootFolders = clip.Roots.Where(r => r.IsFolder).Select(r => r.Id).ToHashSet();
        foreach (var source in clip.Folders)
        {
            var copy = source.Clone();
            copy.Id = folderIds[source.Id];
            copy.ParentId = source.ParentId is { } p && folderIds.TryGetValue(p, out var parent) ? parent : null;
            copy.Under = source.Under is { } u && layerIds.TryGetValue(u, out var under) ? under : null;
            if (rootFolders.Contains(source.Id))
            {
                copy.Name = LayerCopy.CopyName(source.Name, scene.LayerGroups.Select(f => f.Name));
            }
            scene.LayerGroups.Add(copy);
        }
        return clip.Roots
            .Select(r => r.IsFolder
                ? new StackRef(folderIds[r.Id], true)
                : new StackRef(layerIds[r.Id], false))
            .ToList();
    }
}
