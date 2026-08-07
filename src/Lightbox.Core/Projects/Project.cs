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

    public string Name => Manifest.Name;

    /// <summary>The folders that describe a subject — what "the characters" means now.</summary>
    public IEnumerable<ProjectFolder> Subjects => ProjectFolders.Subjects(Manifest);

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

    /// <summary>The palette a subject paints with right now, variant included.</summary>
    public Palette? PaletteFor(ProjectFolder folder)
    {
        var id = VariantOf(folder)?.PaletteId
                 ?? ResourceScopes.NearestAt(Manifest, folder, PaletteScopes.Kind)?.Id;
        return id is null ? null : Palettes.FirstOrDefault(p => p.Id == id);
    }

    /// <summary>The nearest folder above a document that describes a subject.</summary>
    public ProjectFolder? SubjectOf(DocumentRef reference) =>
        ProjectFolders.SubjectFor(Manifest, reference);

    public DocumentRef? FindRef(string id) => AllDocuments.FirstOrDefault(d => d.Id == id);

    /// <summary>Absolute path of a document, from its project-relative one.</summary>
    public string PathOf(DocumentRef reference) =>
        Path.Combine(Root, reference.Path.Replace('/', Path.DirectorySeparatorChar));
}
