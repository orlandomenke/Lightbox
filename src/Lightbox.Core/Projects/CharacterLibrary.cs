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
                entries.AddRange(project.Subjects.Select(f => new LibraryEntry(project, f)));
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
    /// its variants and the palettes both depend on.
    /// </summary>
    /// <remarks>
    /// Everything is given a fresh identity except <b>swatch ids</b>, which are
    /// kept. The art references swatches by id, so renumbering them would
    /// import a character that paints nothing — and keeping them is also what
    /// lets the imported variants keep working, since a variant is a second
    /// palette carrying the same ids.
    /// </remarks>
    public static ProjectFolder Import(LibraryEntry entry, Project target)
    {
        var copy = ProjectFolders.Add(target.Manifest, entry.Folder.Name);
        copy.Pivot = entry.Folder.Pivot;
        copy.Notes = entry.Folder.Notes;
        // The reading travels with the subject: it describes the character, not
        // the project, and re-reading it in the target would spend a request to
        // learn what is already known. `Reviewed` travels too — a correction an
        // artist made is theirs wherever the subject goes.
        copy.Taxonomy = entry.Folder.Taxonomy;

        // Palettes first: the documents are about to reference them.
        var sourcePaletteId =
            ResourceScopes.NearestAt(entry.Source.Manifest, entry.Folder, PaletteScopes.Kind)?.Id;
        var palettes = new Dictionary<string, string>(); // source palette id → copy's id
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

        // Declaring it on the folder is how the copy paints from it. Under the
        // old model this was `Character.PaletteId`; scoping it means the
        // imported subject's palette also reaches every subfolder of it, which
        // the character field could not express.
        if (sourcePaletteId is not null && palettes.TryGetValue(sourcePaletteId, out var mine))
        {
            ResourceScopes.Declare(target.Manifest, copy, PaletteScopes.Kind, mine);
        }

        var documents = new Dictionary<string, DocumentRef>(); // source ref id → copy's ref
        foreach (var source in ProjectFolders.DocumentsIn(entry.Source.Manifest, entry.Folder))
        {
            if (ProjectIo.LoadDocument(entry.Source, source) is not { } doc) continue;
            documents[source.Id] = ProjectIo.AddDocument(target, source.Name, DocJson.Clone(doc), copy);
        }

        foreach (var variant in entry.Folder.Variants ?? [])
        {
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
        return copy;
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
