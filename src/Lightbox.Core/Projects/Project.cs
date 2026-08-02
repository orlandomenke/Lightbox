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

    /// <summary>
    /// Every animation in the project, whether loaded or not — including the
    /// documents that variants override with. Save and load walk this, so a
    /// variant's own art has to be in it or it never reaches disk.
    /// </summary>
    public IEnumerable<DocumentRef> AllDocuments =>
        Manifest.Characters.SelectMany(c => c.Animations)
            .Concat(Manifest.Characters
                .SelectMany(c => c.Variants)
                .SelectMany(v => v.AnimationOverrides.Values))
            .Concat(Manifest.Documents);

    /// <summary>
    /// The variant being viewed, per character id.
    ///
    /// Runtime, not serialized. Which version of a character you are looking at
    /// is the same kind of thing as where the playhead is: it changes what
    /// renders and never touches the record. Saving it would also mean a file
    /// that opens differently depending on who closed it last.
    /// </summary>
    public Dictionary<string, string> ActiveVariant { get; } = [];

    public CharacterVariant? VariantOf(Character character) =>
        character.FindVariant(ActiveVariant.GetValueOrDefault(character.Id));

    /// <summary>The palette a character paints with right now, variant included.</summary>
    public Palette? PaletteFor(Character character)
    {
        var id = VariantOf(character)?.PaletteId ?? character.PaletteId;
        return id is null ? null : Palettes.FirstOrDefault(p => p.Id == id);
    }

    public DocumentRef? FindRef(string id) => AllDocuments.FirstOrDefault(d => d.Id == id);

    public Character? OwnerOf(DocumentRef reference) =>
        Manifest.Characters.FirstOrDefault(c => c.Animations.Any(a => a.Id == reference.Id));

    /// <summary>Absolute path of a document, from its project-relative one.</summary>
    public string PathOf(DocumentRef reference) =>
        Path.Combine(Root, reference.Path.Replace('/', Path.DirectorySeparatorChar));
}
