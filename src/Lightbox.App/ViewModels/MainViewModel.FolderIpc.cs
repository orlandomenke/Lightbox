using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>
/// Folders for an agent (Q204): make one, file things into one, group a
/// selection — the docker's own operations, reached by id over the IPC.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three verbs, and none of them destroys anything.</b> Ungroup and delete
/// are left to the artist: an agent that can only create and rearrange cannot
/// lose work, and every one of these is a single undo step through
/// <see cref="StackEdit"/>, tried on a skeleton first so a refusal changes
/// nothing.
/// </para>
/// <para>
/// <b>Stricter than the docker about locks.</b> The artist's own drag reorders
/// a locked layer, because a person dragging a row means it. An agent's request
/// does not carry that intent, so a locked layer or folder — its own lock or
/// any folder above it — and a locked destination are refused, in words,
/// naming the lock.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    /// <summary>The longest folder name an agent can set; a docker row shows far less.</summary>
    private const int MaxExternalFolderName = 100;

    /// <summary>A layer or folder by id — layers first, since an id names one or the other.</summary>
    private StackRef? StackRefFor(string id)
    {
        if (Scene.Layers.Any(l => l.Id == id)) return new StackRef(id, false);
        if (FolderTree.Folder(Scene, id) is not null) return new StackRef(id, true);
        return null;
    }

    /// <summary>Why an item may not be moved by an agent, or null.</summary>
    private string? ExternalLockOn(StackRef item)
    {
        switch (FolderTree.Find(Scene, item))
        {
            case Layer layer when !Scene.IsLayerEditable(layer):
                return FolderTree.LockedFolderOf(Scene, layer) is { } lockedFolder
                    ? $"Folder “{lockedFolder.Name}” is locked."
                    : $"Layer “{layer.Name}” is locked.";
            case LayerGroup own when own.Locked:
                return $"Folder “{own.Name}” is locked.";
            case LayerGroup inner when FolderTree.Ancestors(Scene, inner).FirstOrDefault(f => f.Locked) is { } above:
                return $"Folder “{above.Name}” is locked.";
        }
        return null;
    }

    /// <summary>Why nothing may be put into <paramref name="folder"/> by an agent, or null.</summary>
    private static string? ExternalLockOnDestination(Scene scene, LayerGroup? folder) =>
        folder is null ? null
        : (folder.Locked ? folder : FolderTree.Ancestors(scene, folder).FirstOrDefault(f => f.Locked)) is { } locked
            ? $"Folder “{locked.Name}” is locked."
            : null;

    private string FolderNameOrNext(string? name) =>
        string.IsNullOrWhiteSpace(name) ? NextFolderName()
        : name.Trim().Length > MaxExternalFolderName ? name.Trim()[..MaxExternalFolderName]
        : name.Trim();

    /// <summary>
    /// <c>create_folder</c>: an empty folder at the top of the stack, or at the
    /// top inside <paramref name="insideId"/>. Returns its id, or why not.
    /// </summary>
    public (string? FolderId, string? Refusal) ExternalCreateFolder(string? name, string? insideId)
    {
        LayerGroup? inside = null;
        if (insideId is not null)
        {
            inside = FolderTree.Folder(Scene, insideId);
            if (inside is null) return (null, $"No folder with id “{insideId}”.");
            if (ExternalLockOnDestination(Scene, inside) is { } locked) return (null, locked);
        }
        var id = Ids.NewId("group");
        var folderName = FolderNameOrNext(name);
        string? refusal = null;
        var made = StackEdit(scene =>
        {
            FolderTree.AddFolder(scene, new LayerGroup { Id = id, Name = folderName }, null);
            if (insideId is null) return null;
            return refusal = FolderTree.Move(scene, [new StackRef(id, true)], new StackRef(insideId, true), StackDrop.Into);
        }, "New folder");
        return made ? (id, null) : (null, refusal is { Length: > 0 } ? refusal : "The folder could not be made there.");
    }

    /// <summary>
    /// <c>move_to_folder</c>: file a layer or folder at the top inside
    /// <paramref name="folderId"/>, or — with none — take it out of every
    /// folder, to sit at the top level just above the folder it was in.
    /// Returns null, or why not.
    /// </summary>
    public string? ExternalMoveToFolder(string itemId, string? folderId)
    {
        if (StackRefFor(itemId) is not { } item) return $"No layer or folder with id “{itemId}”.";
        if (ExternalLockOn(item) is { } locked) return locked;

        StackRef target;
        StackDrop where;
        if (folderId is not null)
        {
            if (FolderTree.Folder(Scene, folderId) is not { } folder) return $"No folder with id “{folderId}”.";
            if (ExternalLockOnDestination(Scene, folder) is { } shut) return shut;
            (target, where) = (new StackRef(folderId, true), StackDrop.Into);
        }
        else
        {
            var outermost = FolderTree.Find(Scene, item) switch
            {
                Layer l => FolderTree.FoldersOf(Scene, l).LastOrDefault(),
                LayerGroup g => FolderTree.Ancestors(Scene, g).LastOrDefault(),
                _ => null,
            };
            if (outermost is null) return null; // already at the top level: nothing to do, and nothing wrong
            (target, where) = (StackRef.Of(outermost), StackDrop.Above);
        }

        string? refusal = null;
        StackEdit(scene => refusal = FolderTree.Move(scene, [item], target, where), item.IsFolder ? "Move folder" : "Move layer");
        return refusal is { Length: > 0 } ? refusal : null;
    }

    /// <summary>
    /// <c>group_layers</c>: wrap layers and folders in a new folder where the
    /// topmost of them was, gathering them if they were apart. Returns the new
    /// folder's id, or why not.
    /// </summary>
    public (string? FolderId, string? Refusal) ExternalGroup(IReadOnlyList<string> itemIds, string? name)
    {
        if (itemIds.Count == 0) return (null, "Name at least one layer or folder to group.");
        var items = new List<StackRef>();
        foreach (var itemId in itemIds.Distinct())
        {
            if (StackRefFor(itemId) is not { } item) return (null, $"No layer or folder with id “{itemId}”.");
            if (ExternalLockOn(item) is { } locked) return (null, locked);
            items.Add(item);
        }
        var id = Ids.NewId("group");
        var folderName = FolderNameOrNext(name);
        string? refusal = null;
        var made = StackEdit(
            scene => refusal = FolderTree.Group(scene, new LayerGroup { Id = id, Name = folderName }, items),
            items.Count == 1 ? "Group layer" : "Group layers");
        return made ? (id, null) : (null, refusal is { Length: > 0 } ? refusal : "Those could not be grouped.");
    }
}
