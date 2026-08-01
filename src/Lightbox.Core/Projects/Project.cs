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

    /// <summary>Gradients shared the same way, keyed by id.</summary>
    public Dictionary<string, Gradient> Gradients { get; } = [];

    /// <summary>Documents read so far, by <see cref="DocumentRef.Id"/>.</summary>
    public Dictionary<string, Doc> Loaded { get; } = [];

    public string Name => Manifest.Name;

    public IEnumerable<Character> Characters => Manifest.Characters;

    /// <summary>Every animation in the project, whether loaded or not.</summary>
    public IEnumerable<DocumentRef> AllDocuments =>
        Manifest.Characters.SelectMany(c => c.Animations).Concat(Manifest.Documents);

    public DocumentRef? FindRef(string id) => AllDocuments.FirstOrDefault(d => d.Id == id);

    public Character? OwnerOf(DocumentRef reference) =>
        Manifest.Characters.FirstOrDefault(c => c.Animations.Any(a => a.Id == reference.Id));

    /// <summary>Absolute path of a document, from its project-relative one.</summary>
    public string PathOf(DocumentRef reference) =>
        Path.Combine(Root, reference.Path.Replace('/', Path.DirectorySeparatorChar));
}
