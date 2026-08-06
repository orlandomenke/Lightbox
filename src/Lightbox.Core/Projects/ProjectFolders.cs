using Lightbox.Core.Documents;

namespace Lightbox.Core.Projects;

/// <summary>
/// A folder in a project, with a name the artist chose.
/// </summary>
/// <remarks>
/// <para>
/// <b>The primitive that replaces the naming convention.</b> Paths were built
/// from fixed words — <c>characters/&lt;slug&gt;/animations/</c>,
/// <c>scenes/&lt;slug&gt;/shots/</c> — which is coherent while a project is one
/// character and wrong the moment it is a production. An entire feature, an
/// episode, or every 2D asset in a game does not decompose into two levels
/// somebody else named.
/// </para>
/// <para>
/// So a folder is a record with a parent, and the tree is whatever the artist
/// builds. <see cref="Tags"/> is here and nullable because Q28 answered that a
/// reference binds to a *list of targets* and that a tag becomes one of those
/// kinds later — the field exists so that arrives as a binding kind rather than
/// as a model change.
/// </para>
/// </remarks>
public sealed class ProjectFolder
{
    public string Id { get; set; } = Ids.NewId("folder");

    /// <summary>
    /// What the artist called it. Arbitrary — there is no convention to obey.
    /// </summary>
    public string Name { get; set; } = "Folder";

    /// <summary>The folder this one is inside, or null for the project root.</summary>
    public string? ParentId { get; set; }

    /// <summary>
    /// Tags, absent until one is applied.
    /// </summary>
    /// <remarks>
    /// Nullable rather than an empty list, so a project that never tags anything
    /// writes no <c>tags</c> key — the same rule the camera and the project type
    /// follow. `AFolderThatWasNeverTaggedWritesNoTagsKey` is the guard.
    /// </remarks>
    public List<string>? Tags { get; set; }
}

/// <summary>
/// Reading and reshaping a project's folder tree.
/// </summary>
/// <remarks>
/// <para>
/// Model code rather than view-model code, on purpose: <b>Q29</b> answered that
/// the docker and the project window are two surfaces onto one hierarchy, and
/// building the tree inside the docker is how it acquires a second
/// implementation written by somebody who cannot change the first.
/// </para>
/// <para>
/// Its own file rather than more of <c>ProjectIo</c>, which is already 800
/// lines and is about reading and writing files. This is about the shape of the
/// project, and it touches no disk at all — every operation here rearranges the
/// manifest, and the next save puts the files where the manifest now says.
/// </para>
/// </remarks>
public static class ProjectFolders
{
    /// <summary>Every folder in the project, or an empty list when there are none.</summary>
    public static IReadOnlyList<ProjectFolder> All(ProjectManifest manifest) =>
        manifest.Folders ?? [];

    public static ProjectFolder? ById(ProjectManifest manifest, string? id) =>
        id is null ? null : All(manifest).FirstOrDefault(f => f.Id == id);

    /// <summary>The folders directly inside <paramref name="parent"/>, or at the root when null.</summary>
    public static IReadOnlyList<ProjectFolder> ChildrenOf(ProjectManifest manifest, ProjectFolder? parent) =>
        All(manifest).Where(f => f.ParentId == parent?.Id).ToList();

    /// <summary>
    /// Every folder beneath <paramref name="folder"/>, at any depth, deepest last.
    /// </summary>
    /// <remarks>
    /// Iterative rather than recursive, and it tracks what it has seen. A cycle
    /// cannot be produced by <see cref="Move"/>, which refuses to make one — but
    /// a hand-edited or agent-written <c>project.json</c> can, and a stack
    /// overflow deep inside a docker rebuild is the worst way to learn that.
    /// </remarks>
    public static IReadOnlyList<ProjectFolder> Descendants(ProjectManifest manifest, ProjectFolder folder)
    {
        var found = new List<ProjectFolder>();
        var seen = new HashSet<string> { folder.Id };
        var queue = new Queue<ProjectFolder>(ChildrenOf(manifest, folder));
        while (queue.Count > 0)
        {
            var next = queue.Dequeue();
            if (!seen.Add(next.Id)) continue;
            found.Add(next);
            foreach (var child in ChildrenOf(manifest, next)) queue.Enqueue(child);
        }
        return found;
    }

    /// <summary>The chain from the root down to and including this folder.</summary>
    public static IReadOnlyList<ProjectFolder> AncestryOf(ProjectManifest manifest, ProjectFolder folder)
    {
        var chain = new List<ProjectFolder>();
        var seen = new HashSet<string>();
        for (var at = folder; at is not null && seen.Add(at.Id); at = ById(manifest, at.ParentId))
        {
            chain.Insert(0, at);
        }
        return chain;
    }

    /// <summary>How deep this folder sits: zero at the project root.</summary>
    public static int DepthOf(ProjectManifest manifest, ProjectFolder folder) =>
        AncestryOf(manifest, folder).Count - 1;

    /// <summary>
    /// Where this folder is on disk, relative to the project root, with forward
    /// slashes — for example <c>art/backgrounds</c>.
    /// </summary>
    /// <remarks>
    /// Derived from the chain of <em>slugs</em>, not of names: a folder called
    /// "Act 2 — Interiors" has to become something a filesystem accepts, and
    /// <see cref="ProjectIo.Slug"/> is the same function every other path in the
    /// project already goes through.
    /// </remarks>
    public static string PathOf(ProjectManifest manifest, ProjectFolder folder) =>
        string.Join('/', AncestryOf(manifest, folder).Select(f => ProjectIo.Slug(f.Name)));

    /// <summary>The documents filed directly in this folder, or at the root when null.</summary>
    public static IReadOnlyList<DocumentRef> DocumentsIn(ProjectManifest manifest, ProjectFolder? folder) =>
        manifest.Documents.Where(d => d.FolderId == folder?.Id).ToList();

    /// <summary>
    /// Make a folder inside <paramref name="parent"/>, or at the root.
    /// </summary>
    /// <remarks>
    /// The name is taken as given except for collisions, which are numbered
    /// rather than refused: creating two folders called "Backgrounds" in one
    /// place is a slip, and the artist is more likely to want the second one
    /// than an error message. Renaming afterwards is one gesture (B64).
    /// </remarks>
    public static ProjectFolder Add(ProjectManifest manifest, string name, ProjectFolder? parent = null)
    {
        manifest.Folders ??= [];
        var folder = new ProjectFolder
        {
            Name = UniqueName(manifest, Named(name), parent, except: null),
            ParentId = parent?.Id,
        };
        manifest.Folders.Add(folder);
        return folder;
    }

    /// <summary>Rename a folder. False when the name is unusable.</summary>
    /// <remarks>
    /// Refused rather than numbered, unlike <see cref="Add"/>, and the
    /// difference is intent: somebody typing a name over an existing one meant
    /// *that* name, and silently giving them "Backgrounds (2)" is the kind of
    /// help that has to be undone.
    /// </remarks>
    public static bool Rename(ProjectManifest manifest, ProjectFolder folder, string name)
    {
        var wanted = Named(name);
        if (wanted == folder.Name) return false;
        var parent = ById(manifest, folder.ParentId);
        if (Collides(manifest, wanted, parent, except: folder)) return false;
        folder.Name = wanted;
        return true;
    }

    /// <summary>
    /// Move a folder under a new parent, or to the root. False when it cannot go.
    /// </summary>
    /// <remarks>
    /// Two refusals, and the first is the one that matters: <b>a folder cannot
    /// be moved inside itself or inside anything beneath it</b>. Dragging a
    /// folder onto its own child is an ordinary slip in a tree view, and the
    /// result would be a subtree detached from the root — invisible in every
    /// surface, still in the file, and impossible to get back to without
    /// editing JSON.
    /// </remarks>
    public static bool Move(ProjectManifest manifest, ProjectFolder folder, ProjectFolder? destination)
    {
        if (ReferenceEquals(folder, destination)) return false;
        if (folder.ParentId == destination?.Id) return false;
        if (destination is not null && Descendants(manifest, folder).Any(d => d.Id == destination.Id))
        {
            return false;
        }
        if (Collides(manifest, folder.Name, destination, except: folder)) return false;
        folder.ParentId = destination?.Id;
        return true;
    }

    /// <summary>
    /// File a document in a folder, or at the project root, and repath it.
    /// </summary>
    /// <remarks>
    /// The path is rewritten here because the folder tree is what decides it
    /// now. The file on disk is <em>not</em> moved — the manifest carries the
    /// new path and the next save writes it there, which is the rule
    /// <see cref="ProjectIo.MoveDocument"/> already follows and for the same
    /// reason: a rearrangement that deleted an artist's file would be a poor
    /// trade for tidiness.
    /// </remarks>
    public static bool FileDocument(ProjectManifest manifest, DocumentRef reference, ProjectFolder? folder)
    {
        if (!manifest.Documents.Any(d => d.Id == reference.Id)) return false;
        reference.FolderId = folder?.Id;
        reference.Path = PathFor(manifest, reference, folder);
        return true;
    }

    /// <summary>Where a document filed in <paramref name="folder"/> belongs.</summary>
    public static string PathFor(ProjectManifest manifest, DocumentRef reference, ProjectFolder? folder)
    {
        var slug = ProjectIo.Slug(reference.Name);
        var taken = manifest.Documents
            .Where(d => d.Id != reference.Id && d.FolderId == folder?.Id)
            .Select(d => ProjectIo.Slug(d.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unique = slug;
        for (var n = 2; taken.Contains(unique); n++) unique = $"{slug}-{n}";

        var directory = folder is null ? "documents" : PathOf(manifest, folder);
        return $"{directory}/{unique}.lightbox.json";
    }

    /// <summary>
    /// A folder and everything beneath it, with the documents that would go
    /// with it — what a delete has to ask about before it happens.
    /// </summary>
    /// <remarks>
    /// Returned rather than acted on, because "remove from the project" and
    /// "delete from disk" are two operations with different consequences (B87)
    /// and the model should not decide which one a click meant.
    /// </remarks>
    public static (IReadOnlyList<ProjectFolder> Folders, IReadOnlyList<DocumentRef> Documents)
        Contents(ProjectManifest manifest, ProjectFolder folder)
    {
        var folders = new List<ProjectFolder> { folder };
        folders.AddRange(Descendants(manifest, folder));
        var ids = folders.Select(f => f.Id).ToHashSet();
        var documents = manifest.Documents.Where(d => d.FolderId is { } id && ids.Contains(id)).ToList();
        return (folders, documents);
    }

    /// <summary>
    /// Take a folder and everything under it out of the manifest, returning the
    /// documents that were in them.
    /// </summary>
    /// <remarks>
    /// The documents are <b>returned rather than dropped</b>: whether they
    /// follow the folder out of the project, move to the root, or are deleted
    /// from disk is B87's question, and answering it here would make every
    /// caller live with one answer.
    /// </remarks>
    public static IReadOnlyList<DocumentRef> Remove(ProjectManifest manifest, ProjectFolder folder)
    {
        var (folders, documents) = Contents(manifest, folder);
        var ids = folders.Select(f => f.Id).ToHashSet();
        manifest.Folders?.RemoveAll(f => ids.Contains(f.Id));
        // Empty rather than null: a project that has removed its last folder is
        // not a project that never had one, and re-adding must not have to
        // remember to allocate. The absent-key rule is about a project that has
        // never used folders, which `Add` is the only thing that changes.
        return documents;
    }

    private static string Named(string name) =>
        string.IsNullOrWhiteSpace(name) ? "Folder" : name.Trim();

    /// <summary>
    /// Case-insensitive, because two folders whose names differ only in case
    /// are two rows in the app and one directory on Windows and macOS.
    /// </summary>
    private static bool Collides(
        ProjectManifest manifest, string name, ProjectFolder? parent, ProjectFolder? except) =>
        ChildrenOf(manifest, parent)
            .Where(f => !ReferenceEquals(f, except))
            .Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    private static string UniqueName(
        ProjectManifest manifest, string wanted, ProjectFolder? parent, ProjectFolder? except)
    {
        var name = wanted;
        for (var n = 2; Collides(manifest, name, parent, except); n++) name = $"{wanted} ({n})";
        return name;
    }
}
