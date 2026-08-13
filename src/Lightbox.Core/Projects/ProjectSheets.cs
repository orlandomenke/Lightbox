using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Projects;

/// <summary>
/// Character sheets as project files: creating them, finding the ones a
/// document can see, and moving them between folders.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q25, re-answered 2026-08-12.</b> The first answer kept sheets inside one
/// document, which made the model sheet of a character reachable only from the
/// animation that happened to create it. Inside a project a sheet is now its
/// own <c>.sheet.json</c> file, filed in a folder the way a document is
/// (<see cref="SheetRef"/>), and visible to every document in that folder's
/// subtree. A new sheet defaults to the <em>top-level</em> folder above the
/// document that made it — the knight's sheet belongs to the knight, not to
/// <c>knight/locomotion</c> — and the project window can refile it.
/// </para>
/// <para>
/// <b>Visibility is filing, not declaration.</b> A sheet on a folder is
/// consulted by that subtree with no further gesture, the way a folder's
/// palette already cascades. The scoped-resource reference declarations stay
/// for the reaches filing cannot say — publish project-wide, share sideways —
/// exactly as <c>docs/DESIGN-scoped-resources.md</c> divided the two.
/// </para>
/// <para>
/// Outside a project none of this exists: a standalone document keeps its
/// sheets in <c>Doc.ReferenceSheets</c>, because there is no tree to file
/// into and no second file to save (B66's behaviour, unchanged).
/// </para>
/// </remarks>
public static class ProjectSheets
{
    /// <summary>What a sheet file ends in, beside <c>.lightbox.json</c>.</summary>
    public const string Extension = ".sheet.json";

    /// <summary>
    /// Where a sheet filed in no folder lives — the project-wide ones.
    /// </summary>
    /// <remarks>
    /// Its own directory rather than <see cref="ProjectIo.DocumentsDir"/>,
    /// because a project-wide sheet is not unfiled work waiting to be sorted;
    /// it is deliberately above the tree. In <see cref="ProjectIo.SystemFolders"/>
    /// so B83's accountability check can explain it.
    /// </remarks>
    public const string RootDir = "sheets";

    // ---- creating ------------------------------------------------------------

    /// <summary>
    /// Make a sheet, filed in <paramref name="folder"/> (or project-wide when
    /// null). Nothing is written until the next project save.
    /// </summary>
    public static ReferenceSheet Add(Project project, string name, ProjectFolder? folder)
    {
        var sheet = new ReferenceSheet
        {
            Name = string.IsNullOrWhiteSpace(name)
                ? $"Character {(project.Manifest.Sheets?.Count ?? 0) + 1}"
                : name.Trim(),
        };
        var reference = new SheetRef
        {
            Id = sheet.Id,
            Name = sheet.Name,
            FolderId = folder?.Id,
        };
        reference.Path = PathFor(project.Manifest, reference, folder);
        (project.Manifest.Sheets ??= []).Add(reference);
        project.LoadedSheets[sheet.Id] = sheet;
        project.DirtySheets.Add(sheet.Id);
        return sheet;
    }

    /// <summary>
    /// The folder a document's new sheet defaults to: the <b>top-level</b>
    /// ancestor of the folder the document is filed in.
    /// </summary>
    /// <remarks>
    /// The top rather than the nearest, because a sheet describes a subject and
    /// the subject is the top of its own subtree — a sheet made while drawing in
    /// <c>knight/locomotion</c> is the knight's, and filing it on
    /// <c>locomotion</c> would hide it from <c>knight/combat</c> next door.
    /// Null for a document filed nowhere, which files the sheet project-wide.
    /// </remarks>
    public static ProjectFolder? DefaultScope(ProjectManifest manifest, DocumentRef? document)
    {
        if (ProjectFolders.ById(manifest, document?.FolderId) is not { } folder) return null;
        return ProjectFolders.AncestryOf(manifest, folder)[0];
    }

    /// <summary>Where a sheet filed in <paramref name="folder"/> belongs on disk.</summary>
    /// <remarks>
    /// The document rule (<see cref="ProjectFolders.PathFor"/>) applied to the
    /// second kind of file: slug the name, dedupe against siblings, land in the
    /// folder's directory. The extension is what tells the two apart.
    /// </remarks>
    public static string PathFor(ProjectManifest manifest, SheetRef reference, ProjectFolder? folder)
    {
        var slug = ProjectIo.Slug(reference.Name);
        var taken = (manifest.Sheets ?? [])
            .Where(s => s.Id != reference.Id && s.FolderId == folder?.Id)
            .Select(s => ProjectIo.Slug(s.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unique = slug;
        for (var n = 2; taken.Contains(unique); n++) unique = $"{slug}-{n}";

        var directory = folder is null ? RootDir : ProjectFolders.PathOf(manifest, folder);
        return $"{directory}/{unique}{Extension}";
    }

    // ---- finding -------------------------------------------------------------

    /// <summary>The registry entry for a sheet id, or null.</summary>
    public static SheetRef? FindRef(ProjectManifest manifest, string id) =>
        (manifest.Sheets ?? []).FirstOrDefault(s => s.Id == id);

    /// <summary>
    /// Every sheet <paramref name="document"/> can consult: the ones filed on
    /// its folder or any ancestor, and the project-wide ones.
    /// </summary>
    /// <remarks>
    /// Nearest first, the order <see cref="ResourceScopes"/> already taught —
    /// the goblin's own sheet above the studio style sheet. A null document
    /// (a loose file opened alongside the project) sees everything, for the
    /// reason <c>PaletteScopes.VisibleTo</c> gives: it has no place in the tree
    /// to resolve from, and hiding work would be a worse answer than offering
    /// too much.
    /// </remarks>
    public static IReadOnlyList<SheetRef> VisibleTo(ProjectManifest manifest, DocumentRef? document)
    {
        if (manifest.Sheets is not { Count: > 0 } sheets) return [];
        if (document is null) return sheets;

        IReadOnlyList<ProjectFolder> chain = ProjectFolders.ById(manifest, document.FolderId) is { } folder
            ? ProjectFolders.AncestryOf(manifest, folder)
            : [];
        var found = new List<SheetRef>();
        // Nearest first: walk the ancestry from the document's folder upward.
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            found.AddRange(sheets.Where(s => s.FolderId == chain[i].Id));
        }
        found.AddRange(sheets.Where(s => s.FolderId is null));
        return found;
    }

    /// <summary>The sheets filed directly in one folder, for a tree to list.</summary>
    public static IReadOnlyList<SheetRef> In(ProjectManifest manifest, ProjectFolder? folder) =>
        [.. (manifest.Sheets ?? []).Where(s => s.FolderId == folder?.Id)];

    // ---- reading and writing --------------------------------------------------

    /// <summary>Read a sheet's content, or return the one already in the cache.</summary>
    public static ReferenceSheet? Load(Project project, SheetRef reference)
    {
        if (project.LoadedSheets.TryGetValue(reference.Id, out var cached)) return cached;
        if (ProjectIo.ResolveInProject(project, reference.Path) is not { } path) return null;
        if (!File.Exists(path)) return null;
        var sheet = JsonSerializer.Deserialize<ReferenceSheet>(File.ReadAllText(path), DocJson.Options);
        if (sheet is null) return null;
        project.LoadedSheets[reference.Id] = sheet;
        return sheet;
    }

    /// <summary>
    /// Write the sheets that need writing: the edited ones, and any that have
    /// never reached disk. Called from <see cref="ProjectIo.Save"/>.
    /// </summary>
    /// <remarks>
    /// The document rule again: a project with forty sheets rewrites one file
    /// when one stroke lands. "Never reached disk" is checked as well as the
    /// dirty set because a sheet promoted out of an old document, or created
    /// moments ago, must not depend on somebody remembering to mark it.
    /// </remarks>
    public static void Save(Project project)
    {
        foreach (var reference in project.Manifest.Sheets ?? [])
        {
            if (!project.LoadedSheets.TryGetValue(reference.Id, out var sheet)) continue;
            if (ProjectIo.ResolveInProject(project, reference.Path) is not { } path) continue;
            if (!project.DirtySheets.Contains(reference.Id) && File.Exists(path)) continue;

            // The registry's name is what the docker shows without loading the
            // file, so it follows the sheet's on every write rather than on a
            // rename path that would have to remember.
            reference.Name = sheet.Name;
            if (Path.GetDirectoryName(path) is { Length: > 0 } parent) Directory.CreateDirectory(parent);
            DocJson.WriteAtomic(path, JsonSerializer.Serialize(sheet, DocJson.Options));
        }
        project.DirtySheets.Clear();
    }

    /// <summary>
    /// Take a sheet out of the project's registry. Disk untouched — deleting
    /// the file is the caller's separate, said-out-loud decision.
    /// </summary>
    public static bool Remove(Project project, SheetRef reference)
    {
        if (project.Manifest.Sheets is not { } sheets) return false;
        if (sheets.RemoveAll(s => s.Id == reference.Id) == 0) return false;
        if (sheets.Count == 0) project.Manifest.Sheets = null;
        project.LoadedSheets.Remove(reference.Id);
        project.DirtySheets.Remove(reference.Id);
        return true;
    }

    // ---- refiling --------------------------------------------------------------

    /// <summary>
    /// File a sheet in another folder — the project window's re-assign.
    /// </summary>
    /// <remarks>
    /// Disk first, manifest only if it worked — B106's order, borrowed from the
    /// docker's document move. A sheet that has never been written has nothing
    /// to move, which <see cref="ProjectIo.MoveInProject"/> already counts as
    /// success.
    /// </remarks>
    public static bool Refile(Project project, SheetRef reference, ProjectFolder? folder)
    {
        if (FindRef(project.Manifest, reference.Id) is null) return false;
        if (reference.FolderId == folder?.Id) return false;

        var to = PathFor(project.Manifest, reference, folder);
        if (!ProjectIo.MoveInProject(project, reference.Path, to)) return false;
        reference.FolderId = folder?.Id;
        reference.Path = to;
        return true;
    }

    // ---- migration --------------------------------------------------------------

    /// <summary>
    /// Lift a document's in-document sheets into the project — the migration
    /// from Q25's first answer, run when the document is read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each sheet keeps its id and is filed on the document's top folder, so
    /// promoting is idempotent: a document reopened before its next save finds
    /// its sheets already in the registry and only re-adopts their content.
    /// The document's own list is emptied — its next save writes it without
    /// the <c>referenceSheets</c> key, and the sheets land as files on the
    /// same save because they are marked dirty here.
    /// </para>
    /// <para>
    /// Nothing is lost if the process dies in between: the old document file
    /// still carries the sheets until the save that writes both halves.
    /// </para>
    /// </remarks>
    public static bool Promote(Project project, DocumentRef reference, Doc doc)
    {
        if (doc.ReferenceSheets.Count == 0) return false;

        var folder = DefaultScope(project.Manifest, reference);
        foreach (var sheet in doc.ReferenceSheets)
        {
            project.LoadedSheets[sheet.Id] = sheet;
            project.DirtySheets.Add(sheet.Id);
            if (FindRef(project.Manifest, sheet.Id) is not null) continue;

            var entry = new SheetRef
            {
                Id = sheet.Id,
                Name = sheet.Name,
                FolderId = folder?.Id,
            };
            entry.Path = PathFor(project.Manifest, entry, folder);
            (project.Manifest.Sheets ??= []).Add(entry);
        }
        doc.ReferenceSheets.Clear();
        return true;
    }
}
