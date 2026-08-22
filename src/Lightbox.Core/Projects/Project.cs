using Lightbox.Core.Documents;

namespace Lightbox.Core.Projects;

/// <summary>
/// An open project: its manifest, where it lives, and the documents that have
/// actually been read.
///
/// The manifest is what serializes; this is the runtime shape around it. The
/// distinction matters because of the loading rule — a character with forty
/// animations must open without reading forty documents, so
/// <see cref="Loaded"/> fills in as documents are opened rather than up front.
/// That is the entire reason a project is a folder and not one file.
/// </summary>
public sealed class Project
{
    public Project(ProjectManifest manifest, string root)
    {
        Manifest = manifest;
        Root = root;
    }

    public ProjectManifest Manifest { get; }

    /// <summary>Absolute path of the <c>.lbproj</c> folder.</summary>
    public string Root { get; set; }

    /// <summary>
    /// Palettes shared by everything in the project. Held here rather than on
    /// each document, which is the whole promise of Pillar 1: a character's
    /// animations share one palette, and editing it changes all of them.
    /// </summary>
    public List<Palette> Palettes { get; } = [];

    /// <summary>
    /// The folders those shared palettes are filed under.
    /// </summary>
    /// <remarks>
    /// A project is where a hierarchy earns its keep — it is the scope with
    /// enough palettes in it to need one. Stored in its own file so a project
    /// that never makes a folder is byte-identical to one written before there
    /// were any.
    /// </remarks>
    public List<PaletteFolder> PaletteFolders { get; } = [];

    /// <summary>Gradients shared the same way, keyed by id.</summary>
    public Dictionary<string, Gradient> Gradients { get; } = [];

    /// <summary>
    /// Symbols shared by everything in the project, keyed by id.
    /// </summary>
    /// <remarks>
    /// The same scope as the palettes and for the same reason. Pillar 3's
    /// promise — edit the sword once, every animation holding it updates —
    /// is only true if the sword lives above the animations that hold it.
    /// A per-document symbol would be a copy with extra steps.
    /// </remarks>
    public Dictionary<string, Symbol> Symbols { get; } = [];

    /// <summary>Documents read so far, by <see cref="DocumentRef.Id"/>.</summary>
    public Dictionary<string, Doc> Loaded { get; } = [];

    /// <summary>Character sheets read so far, by <see cref="SheetRef.Id"/>.</summary>
    /// <remarks>
    /// The same lazy rule as <see cref="Loaded"/> and for the same reason: the
    /// docker lists sheets from the manifest's names alone, and a sheet's views
    /// are only read when somebody opens or renders one.
    /// </remarks>
    public Dictionary<string, ReferenceSheet> LoadedSheets { get; } = [];

    /// <summary>Sheets edited since the last save, by <see cref="SheetRef.Id"/>.</summary>
    /// <remarks>
    /// Runtime, not serialized — the sheet-side counterpart of the dirty set
    /// the docker keeps for documents. <c>ProjectSheets.Save</c> writes and
    /// clears it, so a project with forty sheets rewrites one file when one
    /// stroke lands.
    /// </remarks>
    public HashSet<string> DirtySheets { get; } = [];

    public string Name => Manifest.Name;

    /// <summary>The folders something has read.</summary>
    /// <remarks>
    /// <b>Q40.</b> Was <c>Subjects</c>. A folder with a reading is a folder with
    /// a reading; whether it is a character, a creature or a crowd is the
    /// artist's to say with <see cref="ProjectFolder.Icon"/>, and nothing here
    /// needs to know.
    /// </remarks>
    public IEnumerable<ProjectFolder> WithReading => ProjectFolders.WithReading(Manifest);

    /// <summary>Every document in the project.</summary>
    /// <remarks>
    /// <b>This used to concatenate four lists</b> — character animations,
    /// variant overrides, scene shots and loose documents — which is B114 in one
    /// property. There is one list now, so anything reading
    /// <see cref="ProjectManifest.Documents"/> sees the whole project rather than
    /// the leftovers, and this property is kept only because a great deal of
    /// code says what it means by asking for all of them.
    /// </remarks>
    public IEnumerable<DocumentRef> AllDocuments => Manifest.Documents;

    /// <summary>
    /// The variant being viewed, per folder id.
    ///
    /// Runtime, not serialized. Which version of a subject you are looking at
    /// is the same kind of thing as where the playhead is: it changes what
    /// renders and never touches the record. Saving it would also mean a file
    /// that opens differently depending on who closed it last.
    /// </summary>
    public Dictionary<string, string> ActiveVariant { get; } = [];

    public SubjectVariant? VariantOf(ProjectFolder folder) =>
        (folder.Variants ?? []).FirstOrDefault(
            v => v.Id == ActiveVariant.GetValueOrDefault(folder.Id));

    /// <summary>
    /// The palette substitutions the active variants imply for a document:
    /// which base palette id each variant palette stands in for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This mapping is what makes the palette swap live.</b> A stroke
    /// records the palette it was painted from, and
    /// <c>PaletteRegistry.ResolveSwatch</c> deliberately never answers from a
    /// different palette that happens to share the swatch id (Q30). So a
    /// variant's copy — same swatch ids, its own palette id — repaints nothing
    /// by merely existing; it has to be registered <em>as</em> the base
    /// palette while the variant is being viewed, and this says which id that
    /// is: the palette the folder's scope resolves to, the one
    /// <see cref="ProjectIo.AddVariant"/> copied.
    /// </para>
    /// <para>
    /// The whole ancestry is walked rather than one folder, because each
    /// folder with an active variant substitutes its own scoped palette —
    /// nearest wins where two folders somehow scope the same one, matching
    /// <see cref="ResourceScopes.Resolve"/>.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, Palette> PaletteStandInsFor(DocumentRef? reference)
    {
        var standIns = new Dictionary<string, Palette>();
        if (ProjectFolders.ById(Manifest, reference?.FolderId) is not { } folder) return standIns;
        foreach (var above in ProjectFolders.AncestryOf(Manifest, folder))
        {
            if (VariantOf(above)?.PaletteId is not { } paletteId) continue;
            var baseId = ResourceScopes.NearestAt(Manifest, above, PaletteScopes.Kind)?.Id;
            if (baseId is null || baseId == paletteId) continue;
            if (Palettes.FirstOrDefault(p => p.Id == paletteId) is not { } standIn) continue;
            standIns[baseId] = standIn;
        }
        return standIns;
    }

    /// <summary>
    /// Every palette id some variant owns, active or not.
    /// </summary>
    /// <remarks>
    /// A variant's copy is reachable only <em>through</em> its variant: it
    /// carries the base palette's swatch ids on purpose, so registering it
    /// under its own id beside the base would leave the flat lookup answering
    /// from whichever happened to register last.
    /// </remarks>
    public IEnumerable<string> VariantPaletteIds() =>
        ProjectFolders.All(Manifest)
            .SelectMany(f => f.Variants ?? Enumerable.Empty<SubjectVariant>())
            .Select(v => v.PaletteId)
            .OfType<string>();

    /// <summary>The palette a folder's work paints with right now, variant included.</summary>
    public Palette? PaletteFor(ProjectFolder folder)
    {
        var id = VariantOf(folder)?.PaletteId
                 ?? ResourceScopes.NearestAt(Manifest, folder, PaletteScopes.Kind)?.Id;
        return id is null ? null : Palettes.FirstOrDefault(p => p.Id == id);
    }

    /// <summary>The nearest folder above a document that has been read.</summary>
    public ProjectFolder? ReadingFor(DocumentRef reference) =>
        ProjectFolders.ReadingFor(Manifest, reference);

    public DocumentRef? FindRef(string id) => AllDocuments.FirstOrDefault(d => d.Id == id);

    /// <summary>Absolute path of a document, from its project-relative one.</summary>
    public string PathOf(DocumentRef reference) =>
        Path.Combine(Root, reference.Path.Replace('/', Path.DirectorySeparatorChar));
}
