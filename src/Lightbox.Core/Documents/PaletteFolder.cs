namespace Lightbox.Core.Documents;

/// <summary>
/// A folder in the palette hierarchy.
/// </summary>
/// <remarks>
/// <para>
/// A record of its own rather than a path string on the palette, because a
/// folder has to be able to be <i>empty</i>. Someone organising a project makes
/// "Characters" and "Backgrounds" before there is anything to put in them, and
/// a hierarchy derived from the palettes it contains cannot represent that —
/// the folder would vanish the moment you dragged its last palette out, which
/// is not a filing system, it is a grouping.
/// </para>
/// <para>
/// Absent by default. A document that never makes a folder writes no folder
/// key and reads exactly as it does today.
/// </para>
/// </remarks>
public sealed class PaletteFolder
{
    public string Id { get; set; } = Ids.NewId("pf");

    public string Name { get; set; } = "Folder";

    /// <summary>The folder this one sits in, or null for the top level.</summary>
    public string? ParentId { get; set; }
}

/// <summary>
/// The rules of the palette hierarchy: what is inside what, what may be moved
/// where, and what happens to the contents when a folder goes.
/// </summary>
/// <remarks>
/// Pure functions over the two lists, deliberately. Every way a tree goes
/// wrong — a folder inside itself, an orphan pointing at a folder that has
/// been deleted, a rename that loses the children — is shape, not pixels, and
/// none of it needs a window or a document to catch.
/// </remarks>
public static class PaletteTree
{
    /// <summary>The folders directly inside <paramref name="parentId"/>, in order.</summary>
    public static List<PaletteFolder> FoldersIn(
        IReadOnlyList<PaletteFolder> folders, string? parentId) =>
        folders.Where(f => f.ParentId == parentId).ToList();

    /// <summary>The palettes directly inside <paramref name="folderId"/>, in order.</summary>
    public static List<Palette> PalettesIn(
        IReadOnlyList<Palette> palettes, string? folderId) =>
        palettes.Where(p => p.FolderId == folderId).ToList();

    /// <summary>
    /// Is <paramref name="candidateId"/> <paramref name="ancestorId"/> itself,
    /// or somewhere beneath it?
    /// </summary>
    /// <remarks>
    /// The guard on every move. Dropping a folder into its own child detaches
    /// the pair from the tree entirely: they still reference each other, so
    /// nothing looks corrupt, and both simply stop appearing anywhere.
    /// </remarks>
    public static bool IsSelfOrDescendant(
        IReadOnlyList<PaletteFolder> folders, string? candidateId, string ancestorId)
    {
        var seen = new HashSet<string>();
        var walk = candidateId;
        while (walk is not null && seen.Add(walk))
        {
            if (walk == ancestorId) return true;
            walk = folders.FirstOrDefault(f => f.Id == walk)?.ParentId;
        }
        return false;
    }

    /// <summary>
    /// Move a folder under a new parent. Returns false and changes nothing when
    /// the move would detach a branch.
    /// </summary>
    public static bool Reparent(
        IReadOnlyList<PaletteFolder> folders, string folderId, string? newParentId)
    {
        if (folders.FirstOrDefault(f => f.Id == folderId) is not { } folder) return false;
        if (newParentId is not null)
        {
            if (folders.All(f => f.Id != newParentId)) return false;
            if (IsSelfOrDescendant(folders, newParentId, folderId)) return false;
        }
        folder.ParentId = newParentId;
        return true;
    }

    /// <summary>
    /// "Characters / Knight / Armour" — for a tooltip, and for telling two
    /// folders with the same name apart.
    /// </summary>
    public static string PathOf(IReadOnlyList<PaletteFolder> folders, string? folderId)
    {
        var parts = new List<string>();
        var seen = new HashSet<string>();
        var walk = folderId;
        while (walk is not null && seen.Add(walk))
        {
            if (folders.FirstOrDefault(f => f.Id == walk) is not { } folder) break;
            parts.Insert(0, folder.Name);
            walk = folder.ParentId;
        }
        return string.Join(" / ", parts);
    }

    /// <summary>
    /// Delete a folder and everything under it, lifting the palettes out.
    /// </summary>
    /// <remarks>
    /// The palettes survive, at the deleted folder's own level. Deleting a
    /// folder is a filing decision; taking the colours with it would make
    /// tidying up the fastest way to destroy an afternoon's work, and the
    /// strokes that reference those swatches would go with them.
    /// </remarks>
    public static void RemoveFolder(
        List<PaletteFolder> folders, List<Palette> palettes, string folderId)
    {
        if (folders.FirstOrDefault(f => f.Id == folderId) is not { } folder) return;
        var doomed = Subtree(folders, folderId);
        foreach (var palette in palettes.Where(p => p.FolderId is { } id && doomed.Contains(id)))
        {
            palette.FolderId = folder.ParentId;
        }
        folders.RemoveAll(f => doomed.Contains(f.Id));
    }

    /// <summary>A folder and every folder beneath it, by id.</summary>
    public static HashSet<string> Subtree(IReadOnlyList<PaletteFolder> folders, string folderId)
    {
        var found = new HashSet<string> { folderId };
        // Repeat until nothing new appears: the list is in no particular order,
        // so one pass would miss a grandchild listed before its parent.
        bool grew;
        do
        {
            grew = false;
            foreach (var folder in folders)
            {
                if (folder.ParentId is { } parent && found.Contains(parent) && found.Add(folder.Id))
                {
                    grew = true;
                }
            }
        }
        while (grew);
        return found;
    }

    /// <summary>
    /// Point anything that references a folder which is no longer there back at
    /// the top level.
    /// </summary>
    /// <remarks>
    /// Called after loading. A palette whose folder went missing — an older
    /// file, a hand-edited one, a merge — must appear <i>somewhere</i>; the
    /// alternative is a palette that exists, resolves, colours the art and
    /// cannot be found in the panel.
    /// </remarks>
    public static void Prune(List<PaletteFolder> folders, List<Palette> palettes)
    {
        var known = folders.Select(f => f.Id).ToHashSet();
        foreach (var folder in folders)
        {
            if (folder.ParentId is { } parent
                && (!known.Contains(parent) || IsSelfOrDescendant(folders, parent, folder.Id)))
            {
                folder.ParentId = null;
            }
        }
        foreach (var palette in palettes)
        {
            if (palette.FolderId is { } id && !known.Contains(id)) palette.FolderId = null;
        }
    }
}
