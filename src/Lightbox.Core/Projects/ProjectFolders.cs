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
    /// The glyph the artist chose for this folder, or null for the default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q38, and it is the answer to the rigidity Q35 left behind.</b> A
    /// production has props, environments, effects, vehicles, layouts and
    /// crowds; two privileged nouns cannot name them and deriving an icon from
    /// what a folder carries only moves the decision into code that has to pick
    /// a winner. The artist picks.
    /// </para>
    /// <para>
    /// <b>A label, not data.</b> Nothing reads this — it is <see cref="Notes"/>
    /// with one character. The AI path asks for <em>the nearest folder above
    /// this with a reading</em>, export asks for a pivot; neither asks what the
    /// icon is. That is what lets an artist's vocabulary be theirs without
    /// anything downstream depending on it.
    /// </para>
    /// <para>
    /// Nullable, so a folder nobody labelled writes no key.
    /// </para>
    /// </remarks>
    public string? Icon { get; set; }

    /// <summary>
    /// Tags, absent until one is applied.
    /// </summary>
    /// <remarks>
    /// Nullable rather than an empty list, so a project that never tags anything
    /// writes no <c>tags</c> key — the same rule the camera and the project type
    /// follow. `AFolderThatWasNeverTaggedWritesNoTagsKey` is the guard.
    /// </remarks>
    public List<string>? Tags { get; set; }
    /// <summary>
    /// Palettes, references and the like declared on this folder, or null when
    /// it declares none.
    /// </summary>
    /// <remarks>
    /// <b>Q30.</b> Null rather than empty, and absent from the file until
    /// something is declared — the camera's rule, and the reason a project that
    /// never uses scoped resources serializes exactly as it did before. Resolved
    /// by <see cref="ResourceScopes.Resolve"/>, which walks up from a document.
    /// </remarks>
    public List<ScopedResource>? Resources { get; set; }

    // ---- the facets a folder may carry ----------------------------------------
    //
    // B114 and `docs/DESIGN-project-scoping.md`. There were two containers —
    // folders, and Character/ProjectScene — and only folders were wired into
    // scoped resources and export planning, so a character's animations
    // resolved nothing and exported nowhere.
    //
    // The containment half of those types dissolves into the tree; what is left
    // is the handful of things below, which are *about the subject* rather than
    // about holding documents. Each is nullable and absent until used, so an
    // ordinary folder serializes exactly as it did.
    //
    // Q40: there is no noun for a folder that has one. A folder with a reading
    // is not "a character" in the code any more than a folder with a pivot is "a
    // prop" — it is a folder with a reading, and the questions the application
    // actually asks are about the facet, never about a kind.

    /// <summary>What the AI read about the subject this folder holds.</summary>
    public SubjectTaxonomy? Taxonomy { get; set; }

    /// <summary>Where an engine positions this subject from, for export.</summary>
    public Pivot? Pivot { get; set; }

    /// <summary>Versions of this subject that reuse its documents.</summary>
    public List<SubjectVariant>? Variants { get; set; }

    /// <summary>
    /// Document ids in the order the artist arranged them — a scene's shots
    /// play in this order, and a character's animations list in it.
    /// </summary>
    /// <remarks>
    /// <b>The one thing a folder could not previously express</b>, and the only
    /// reason `ProjectScene.Shots` had to be a separate list: the manifest says
    /// outright that folder membership order "is not the display order and
    /// nothing should read it as one".
    ///
    /// <b>Partial on purpose.</b> It need not name every document; anything
    /// absent sorts after what is listed, by name. A scene with three pinned
    /// opening shots and forty unsorted ones should not require sorting forty.
    /// </remarks>
    public List<string>? Order { get; set; }

    /// <summary>What this folder is, in the artist's or director's words.</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Where this folder was imported from, or null for one made here — see
    /// <see cref="ImportOrigin"/>. No content hash on a folder: the documents
    /// carry their own, and a folder's "content" is them.
    /// </summary>
    public ImportOrigin? Origin { get; set; }

    /// <summary>
    /// Whether anything has read this folder.
    /// </summary>
    /// <remarks>
    /// <b>Q40.</b> This was <c>IsSubject</c>, which read as a kind. It is a
    /// question about one facet and nothing else: a folder with a reading is a
    /// folder with a reading, and whether an artist calls it a character, a
    /// creature or a crowd is theirs to say with <see cref="Icon"/>.
    /// </remarks>
    public bool HasReading => Taxonomy is not null;

    /// <summary>
    /// What an artist loses if this folder's reading is cleared or the folder is
    /// deleted, phrased for a confirmation. Empty when there is nothing to lose.
    /// </summary>
    /// <remarks>
    /// <b>Q35's condition, and Q39 leans on it.</b> Under the old model "delete
    /// character" was explicitly destructive. Under this one, clearing a reading
    /// or deleting a folder quietly takes the pivot, the variants and a
    /// hand-corrected taxonomy with it. So the gesture names what goes,
    /// specifically, the way the export confirmation counts what it would write
    /// — never a bare "are you sure".
    /// <para>
    /// Q39 put the facet list in a details panel rather than on every row, which
    /// means this is the <em>only</em> place an artist is told, and it is told at
    /// the moment it matters rather than in passing.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> WhatClearingTheReadingDiscards()
    {
        var lost = new List<string>();
        if (Taxonomy is { } taxonomy)
        {
            lost.Add(taxonomy.Reviewed
                ? "the reading you corrected by hand"
                : $"its reading ({taxonomy.Kind}, {Plural(taxonomy.Parts.Count, "part")})");
        }
        if (Pivot is not null) lost.Add("its pivot");
        if (Variants is { Count: > 0 } variants) lost.Add(Plural(variants.Count, "variant"));
        return lost;
    }

    private static string Plural(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";
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

    /// <summary>
    /// The nearest folder at or above <paramref name="document"/> that has been
    /// read, or null when nothing above it has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Replaces <c>ProjectManifest.CharacterOwning</c>, which searched a
    /// separate list of characters. Walking the tree instead is not merely a
    /// like-for-like swap — it is strictly more capable, because a subfolder
    /// under a character folder now inherits that character's reading. The old
    /// model could not express that at all: an animation was in a character's
    /// list or it was not.
    /// </para>
    /// <para>
    /// Nearest wins, the same rule <see cref="ResourceScopes.Resolve"/> uses, so
    /// a variant folder inside a character folder can carry its own reading and
    /// override the one above it.
    /// </para>
    /// </remarks>
    public static ProjectFolder? ReadingFor(ProjectManifest manifest, DocumentRef? document) =>
        NearestAbove(manifest, document, f => f.HasReading);

    /// <summary>
    /// The nearest folder at or above a document satisfying
    /// <paramref name="carries"/>, or null when nothing above it does.
    /// </summary>
    /// <remarks>
    /// <b>Q40.</b> A facet question rather than a kind question, and the reason
    /// it is written once: the pivot wants the same walk, and so will whatever
    /// facet arrives next. Nearest wins, the same rule
    /// <see cref="ResourceScopes.Resolve"/> uses.
    /// </remarks>
    public static ProjectFolder? NearestAbove(
        ProjectManifest manifest, DocumentRef? document, Func<ProjectFolder, bool> carries)
    {
        var folder = ById(manifest, document?.FolderId);
        if (folder is null) return null;

        // AncestryOf runs root-first, so walk it backwards for nearest-first.
        var chain = AncestryOf(manifest, folder);
        for (var i = chain.Count - 1; i >= 0; i--)
            if (carries(chain[i])) return chain[i];
        return null;
    }

    /// <summary>Every folder that has been read.</summary>
    public static IEnumerable<ProjectFolder> WithReading(ProjectManifest manifest) =>
        All(manifest).Where(f => f.HasReading);

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
    /// <remarks>
    /// A variant's own art is filed here like anything else — it has to be, or
    /// it resolves no palette and exports nowhere — but it is not one of the
    /// folder's documents. It <em>stands in for</em> one, so it is excluded here
    /// and substituted by <see cref="DocumentsFor"/>. Listing both would show a
    /// character with two walk cycles.
    /// </remarks>
    public static IReadOnlyList<DocumentRef> DocumentsIn(ProjectManifest manifest, ProjectFolder? folder)
    {
        var replacements = Replacements(folder);
        return manifest.Documents
            .Where(d => d.FolderId == folder?.Id && !replacements.Contains(d.Id))
            .ToList();
    }

    /// <summary>Every document id that stands in for another one in this folder.</summary>
    private static HashSet<string> Replacements(ProjectFolder? folder) =>
        folder?.Variants is not { Count: > 0 } variants
            ? []
            : [.. variants.SelectMany(v => v.Overrides.Values)];

    /// <summary>
    /// What a variant of this subject actually plays: the folder's documents in
    /// order, with the ones it overrides swapped for its own.
    /// </summary>
    /// <remarks>
    /// <b>"Inherits documents" is this method.</b> A walk cycle drawn once is
    /// the walk cycle of every variant; a variant that replaced it shows its
    /// own, in the same place in the running order. Passing null gives the base
    /// subject, which is what an artist looking at no variant sees.
    /// </remarks>
    public static IReadOnlyList<DocumentRef> DocumentsFor(
        ProjectManifest manifest, ProjectFolder folder, SubjectVariant? variant)
    {
        var documents = InOrder(manifest, folder);
        if (variant is null || variant.Overrides.Count == 0) return documents;

        // Only the replacements, not every document: this runs per folder in
        // the docker's rebuild, and the rebuild runs on every save, so a
        // dictionary over the whole manifest here is folders × documents work
        // for a lookup that needs Overrides.Count entries.
        var wanted = new HashSet<string>(variant.Overrides.Values);
        var byId = new Dictionary<string, DocumentRef>(wanted.Count);
        foreach (var d in manifest.Documents)
        {
            if (wanted.Contains(d.Id)) byId[d.Id] = d;
        }
        return [.. documents.Select(d =>
            variant.Overrides.TryGetValue(d.Id, out var replacement)
            && byId.TryGetValue(replacement, out var over) ? over : d)];
    }

    /// <summary>
    /// The documents in a folder, arranged by its <see cref="ProjectFolder.Order"/>.
    /// </summary>
    /// <remarks>
    /// The order is <b>partial</b>: anything it names comes first, in that
    /// sequence, and everything else follows by name. A scene with three pinned
    /// opening shots and forty unsorted ones should not require sorting forty,
    /// and an id left in the order after its document was deleted is skipped
    /// rather than being an error — an ordering is a preference, not a claim
    /// about what exists.
    /// </remarks>
    public static IReadOnlyList<DocumentRef> InOrder(ProjectManifest manifest, ProjectFolder? folder) =>
        Arrange(DocumentsIn(manifest, folder), folder?.Order, d => d.Id, d => d.Name);

    /// <summary>
    /// The child folders of <paramref name="parent"/>, arranged by its
    /// <see cref="ProjectFolder.Order"/> — a film's scenes in running order.
    /// </summary>
    /// <remarks>
    /// <b>The same <c>Order</c> list, read by a second reader.</b> A folder's
    /// order names ids, and an id is a document's or a child folder's; each
    /// reader takes the ones it owns and skips the rest, which is the behaviour
    /// the partial ordering already has for an id whose document was deleted.
    /// Two lists would be two things to keep in step for no gain — a scene that
    /// contains both shots and sub-scenes has one running order, not two.
    /// </remarks>
    public static IReadOnlyList<ProjectFolder> ChildrenInOrder(
        ProjectManifest manifest, ProjectFolder? parent) =>
        Arrange(ChildrenOf(manifest, parent), parent?.Order, f => f.Id, f => f.Name);

    private static IReadOnlyList<T> Arrange<T>(
        IReadOnlyList<T> items, List<string>? order, Func<T, string> id, Func<T, string> name)
    {
        if (order is not { Count: > 0 }) return [.. items.OrderBy(name)];

        var byId = items.ToDictionary(id);
        var arranged = new List<T>();
        foreach (var wanted in order)
            if (byId.Remove(wanted, out var item)) arranged.Add(item);
        arranged.AddRange(byId.Values.OrderBy(name));
        return arranged;
    }

    /// <summary>
    /// Move something within a folder's running order, by its current and wanted
    /// positions among the folder's <em>documents</em>.
    /// </summary>
    /// <remarks>
    /// <b>Materialises the order on first use</b>, which is what keeps "absent
    /// until used" honest: a folder nobody has arranged carries no <c>order</c>
    /// key, and the first drag writes the list it implied. Returns false and
    /// changes nothing for a move that cannot happen, so a caller can bind it to
    /// a gesture without guarding first.
    /// </remarks>
    public static bool MoveDocument(
        ProjectManifest manifest, ProjectFolder folder, int from, int to)
    {
        var documents = InOrder(manifest, folder);
        if (from < 0 || from >= documents.Count || to < 0 || to >= documents.Count || from == to)
        {
            return false;
        }

        var ids = documents.Select(d => d.Id).ToList();
        var moved = ids[from];
        ids.RemoveAt(from);
        ids.Insert(to, moved);

        // Keep any ids the order names that are not documents here — the child
        // folders it also arranges, and anything a deleted document left behind.
        var kept = (folder.Order ?? []).Where(i => !documents.Any(d => d.Id == i));
        folder.Order = [.. ids, .. kept];
        return true;
    }

    /// <summary>Move a child folder within its parent's running order.</summary>
    public static bool MoveFolder(
        ProjectManifest manifest, ProjectFolder? parent, int from, int to)
    {
        var children = ChildrenInOrder(manifest, parent);
        if (parent is null) return false;   // the root has no record to arrange
        if (from < 0 || from >= children.Count || to < 0 || to >= children.Count || from == to)
        {
            return false;
        }

        var ids = children.Select(f => f.Id).ToList();
        var moved = ids[from];
        ids.RemoveAt(from);
        ids.Insert(to, moved);

        var kept = (parent.Order ?? []).Where(i => !children.Any(f => f.Id == i));
        parent.Order = [.. ids, .. kept];
        return true;
    }

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
    ///
    /// <para><b>This is the manifest half only.</b> A surface acting on a real
    /// project calls <see cref="ProjectIo.MoveFolder"/>, which moves the
    /// directory and repaths everything below — calling this alone is B188:
    /// a tree the panel shows and the disk stopped having.</para>
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

        // B105. The name of the unfiled directory belongs to ProjectIo, which is
        // what creates it — a literal here was a second copy of the string and
        // would have kept the old name on exactly the paths this computes.
        var directory = folder is null ? ProjectIo.DocumentsDir : PathOf(manifest, folder);
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
