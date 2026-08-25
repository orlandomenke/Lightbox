using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Projects;

/// <summary>One subject available for import, and the project it lives in.</summary>
/// <remarks>
/// A subject is a folder with a reading — see <c>DESIGN-project-scoping.md</c>.
/// The library used to enumerate a `Character` list; it now enumerates the
/// folders that describe one, which is the same set expressed once.
/// </remarks>
public sealed record LibraryEntry(Project Source, ProjectFolder Folder)
{
    public string Name => Folder.Name;

    public string LibraryName => Source.Name;

    public int DocumentCount => ProjectFolders.DocumentsIn(Source.Manifest, Folder).Count;

    public int VariantCount => Folder.Variants?.Count ?? 0;
}

/// <summary>
/// What one import did, named so a caller can say it to the artist: the folder
/// it landed in, the documents it added, the ones it replaced with the
/// library's newer copy, and the edited copies it deliberately kept.
/// </summary>
public sealed record ImportResult(
    ProjectFolder Folder,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Replaced,
    IReadOnlyList<string> KeptEdited);

/// <summary>
/// Characters shared between projects.
/// </summary>
/// <remarks>
/// A library is not a new file format — it is a project whose
/// <see cref="ProjectType.AssetLibrary"/> says its characters are meant to be
/// used elsewhere. That reuse is the point of the type: without it,
/// <c>AssetLibrary</c> is a label on an enum that changes nothing.
///
/// Reusing the project format rather than inventing a package keeps one
/// on-disk contract, one reader and one writer. A library can be opened,
/// drawn in and versioned exactly like any other project, which matters
/// because a shared character is something people maintain, not something
/// they publish once.
///
/// Import <b>copies</b>. A linked character that edits in place is a real
/// feature — it is Pillar 3's "edit once, update everywhere" — and it needs a
/// dependency graph and a resolution story that do not exist yet. Copying is
/// honest about what this does, and does not quietly create a link that later
/// breaks.
/// </remarks>
public static class CharacterLibrary
{
    /// <summary>
    /// Every character offered by the library projects under
    /// <paramref name="roots"/>. Folders that are not projects, and projects
    /// that are not libraries, are skipped rather than reported — a library
    /// directory is somewhere people also keep other things.
    /// </summary>
    public static List<LibraryEntry> Scan(IEnumerable<string> roots)
    {
        var entries = new List<LibraryEntry>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var candidate in Candidates(root))
            {
                Project project;
                try
                {
                    project = ProjectIo.Load(candidate);
                }
                catch (Exception e) when (e is IOException or System.Text.Json.JsonException)
                {
                    continue;
                }
                if (project.Manifest.Type != ProjectType.AssetLibrary) continue;
                // Q40: every folder, not only the ones something has read. A
                // shared environment or a prop set is exactly the thing a library
                // is for, and offering only folders with a reading would make
                // "character" a designation again — this time one that decides
                // what can be shared.
                entries.AddRange(
                    ProjectFolders.All(project.Manifest).Select(f => new LibraryEntry(project, f)));
            }
        }
        return entries;
    }

    private static IEnumerable<string> Candidates(string root)
    {
        // The root may be a library itself, or a folder holding several.
        if (File.Exists(Path.Combine(root, "project.json"))) yield return root;
        foreach (var child in Directory.EnumerateDirectories(root, "*" + ProjectIo.Extension))
        {
            yield return child;
        }
    }

    /// <summary>
    /// Copy a library character into <paramref name="target"/>: its animations,
    /// its variants and the palettes both depend on. When the character was
    /// imported before — or a folder merely shares its name — the import
    /// <b>merges</b> instead of duplicating (Q138): it replaces exactly the
    /// documents whose <see cref="ImportOrigin"/> matches the same library
    /// entry, adds documents the library gained, and never touches documents
    /// the artist made locally.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything is given a fresh identity except <b>swatch ids</b>, which are
    /// kept. The art references swatches by id, so renumbering them would
    /// import a character that paints nothing — and keeping them is also what
    /// lets the imported variants keep working, since a variant is a second
    /// palette carrying the same ids.
    /// </para>
    /// <para>
    /// A provenance-matched copy the artist has <b>edited since import</b> is
    /// the one thing a replace would destroy, so it is kept unless
    /// <paramref name="replaceEdited"/> says otherwise — and the caller is
    /// meant to ask first: <see cref="WhatReimportWouldReplace"/> names the
    /// edited copies, Q35-style, before the destructive act rather than after.
    /// </para>
    /// <para>
    /// The merge is about <b>documents</b>, exactly as Q138 answered it.
    /// Palettes and variants arrive with a first import and with a merge into
    /// a folder that lacks them; a library's later recolour or new variant
    /// does not propagate into a folder that already has its own — that is
    /// Pillar 3's "edit once, update everywhere", and it starts from these
    /// stamps when it comes.
    /// </para>
    /// </remarks>
    public static ImportResult Import(LibraryEntry entry, Project target, bool replaceEdited = false)
    {
        var libraryId = entry.Source.Manifest.Id;
        var copy = ProjectFolders.All(target.Manifest)
                .FirstOrDefault(f => f.Origin?.Matches(libraryId, entry.Folder.Id) == true)
            ?? ProjectFolders.All(target.Manifest).FirstOrDefault(f => f.Name == entry.Folder.Name)
            ?? ProjectFolders.Add(target.Manifest, entry.Folder.Name);
        // Stamped even on a name-matched merge: the artist has said these are
        // the same subject, and the stamp is what lets the next import say so
        // without guessing from the name again.
        copy.Origin ??= new ImportOrigin { LibraryId = libraryId, SourceId = entry.Folder.Id };
        copy.Pivot ??= entry.Folder.Pivot;
        copy.Notes ??= entry.Folder.Notes;
        // The reading travels with the subject: it describes the character, not
        // the project, and re-reading it in the target would spend a request to
        // learn what is already known. `Reviewed` travels too — a correction an
        // artist made is theirs wherever the subject goes. On a merge the
        // target's own reading wins, for the same Reviewed reason.
        copy.Taxonomy ??= entry.Folder.Taxonomy;

        // Palettes first: the documents are about to reference them. Only when
        // the folder does not already paint from one — a merge must not stack
        // a second copy of the palette beside the first.
        var palettes = new Dictionary<string, string>(); // source palette id → copy's id
        if (ResourceScopes.NearestAt(target.Manifest, copy, PaletteScopes.Kind) is null)
        {
            var sourcePaletteId =
                ResourceScopes.NearestAt(entry.Source.Manifest, entry.Folder, PaletteScopes.Kind)?.Id;
            foreach (var palette in PalettesUsedBy(entry))
            {
                var duplicate = new Palette
                {
                    Name = palette.Name,
                    Columns = palette.Columns,
                    // Swatch ids preserved on purpose — see the remarks.
                    Swatches = palette.Swatches
                        .Select(s => new Swatch { Id = s.Id, Color = s.Color, Name = s.Name })
                        .ToList(),
                };
                target.Palettes.Add(duplicate);
                palettes[palette.Id] = duplicate.Id;
            }
            if (sourcePaletteId is not null && palettes.TryGetValue(sourcePaletteId, out var mine))
            {
                ResourceScopes.Declare(target.Manifest, copy, PaletteScopes.Kind, mine);
            }
        }

        var added = new List<string>();
        var replaced = new List<string>();
        var keptEdited = new List<string>();
        var existing = ProjectFolders.DocumentsIn(target.Manifest, copy);
        var documents = new Dictionary<string, DocumentRef>(); // source ref id → copy's ref
        foreach (var source in ProjectFolders.DocumentsIn(entry.Source.Manifest, entry.Folder))
        {
            if (ProjectIo.LoadDocument(entry.Source, source) is not { } doc) continue;
            var imported = DocJson.Clone(doc);
            var mine = existing.FirstOrDefault(d => d.Origin?.Matches(libraryId, source.Id) == true);
            if (mine is null)
            {
                mine = ProjectIo.AddDocument(target, source.Name, imported, copy);
                mine.Origin = new ImportOrigin
                {
                    LibraryId = libraryId, SourceId = source.Id, Hash = HashOf(imported),
                };
                added.Add(mine.Name);
            }
            else if (!Edited(target, mine) || replaceEdited)
            {
                // The ref keeps its id and path — everything local that points
                // at this document (an override, an ordering, an open tab)
                // must survive the content underneath it moving forward.
                target.Loaded[mine.Id] = imported;
                mine.Name = source.Name;
                mine.Origin!.Hash = HashOf(imported);
                replaced.Add(mine.Name);
            }
            else
            {
                keptEdited.Add(mine.Name);
            }
            documents[source.Id] = mine;
        }

        foreach (var variant in entry.Folder.Variants ?? [])
        {
            // Merge rule: a variant the folder already carries (by name) is
            // the artist's — a re-import must not double or overwrite it.
            if ((copy.Variants ?? []).Any(v => v.Name == variant.Name)) continue;
            var duplicate = new SubjectVariant
            {
                Name = variant.Name,
                PaletteId = variant.PaletteId is null ? null : palettes.GetValueOrDefault(variant.PaletteId),
            };
            foreach (var (baseId, overrideId) in variant.Overrides)
            {
                if (!documents.TryGetValue(baseId, out var rebased)) continue;
                var over = entry.Source.Manifest.Documents.FirstOrDefault(d => d.Id == overrideId);
                if (over is null) continue;
                if (ProjectIo.LoadDocument(entry.Source, over) is not { } doc) continue;
                ProjectIo.OverrideDocument(target, copy, duplicate, rebased, DocJson.Clone(doc));
            }
            (copy.Variants ??= []).Add(duplicate);
        }
        return new ImportResult(copy, added, replaced, keptEdited);
    }

    /// <summary>
    /// The imported copies that a re-import would overwrite <b>and</b> the
    /// artist has edited since import — the Q35 pattern: name what the
    /// destructive act would discard, so the caller can ask before doing it.
    /// Empty on a first import, and empty when every matched copy is untouched,
    /// because replacing an unedited copy loses nothing.
    /// </summary>
    public static IReadOnlyList<string> WhatReimportWouldReplace(LibraryEntry entry, Project target)
    {
        var libraryId = entry.Source.Manifest.Id;
        var folder = ProjectFolders.All(target.Manifest)
                .FirstOrDefault(f => f.Origin?.Matches(libraryId, entry.Folder.Id) == true)
            ?? ProjectFolders.All(target.Manifest).FirstOrDefault(f => f.Name == entry.Folder.Name);
        if (folder is null) return [];

        var offered = ProjectFolders.DocumentsIn(entry.Source.Manifest, entry.Folder)
            .Select(d => d.Id)
            .ToHashSet();
        return ProjectFolders.DocumentsIn(target.Manifest, folder)
            .Where(d => d.Origin is { } origin
                && origin.LibraryId == libraryId
                && offered.Contains(origin.SourceId)
                && Edited(target, d))
            .Select(d => d.Name)
            .ToList();
    }

    /// <summary>
    /// Whether the copy's content has moved since it was imported. A missing
    /// hash (a stamp from before hashes existed) reads as edited — when the
    /// answer is unknown, the safe claim is the one that warns.
    /// </summary>
    private static bool Edited(Project target, DocumentRef reference)
    {
        if (reference.Origin?.Hash is not { } stamped) return true;
        if (ProjectIo.LoadDocument(target, reference) is not { } doc) return true;
        return HashOf(doc) != stamped;
    }

    /// <summary>
    /// The document's content identity: SHA-256 over its serialized form. The
    /// stroke record is the document (invariant 1), so its JSON is the honest
    /// thing to hash — pixels are derived and would hash the renderer too.
    /// </summary>
    private static string HashOf(Doc doc)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(DocJson.Serialize(doc)));
        return Convert.ToHexStringLower(bytes);
    }

    private static IEnumerable<Palette> PalettesUsedBy(LibraryEntry entry)
    {
        var wanted = (entry.Folder.Variants ?? [])
            .Select(v => v.PaletteId)
            .Append(ResourceScopes.NearestAt(entry.Source.Manifest, entry.Folder, PaletteScopes.Kind)?.Id)
            .OfType<string>()
            .ToHashSet();
        return entry.Source.Palettes.Where(p => wanted.Contains(p.Id));
    }
}
