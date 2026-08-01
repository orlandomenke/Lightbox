using Lightbox.Core.Documents;

namespace Lightbox.Core.Projects;

/// <summary>
/// What kind of work a project is for. Recorded on the manifest so tooling and
/// export can adapt, and <b>absent unless chosen</b> — the same discipline
/// <see cref="Scene"/>'s camera and pivot follow. An illustration project must
/// not start carrying game-art keys because the feature exists.
/// </summary>
public enum ProjectType
{
    Illustration,
    Animation,
    GameArt,
    Storyboard,
    Comic,
    AssetLibrary,
}

/// <summary>
/// A pointer to a document inside the project, not the document itself.
///
/// This is the whole reason the project is a folder rather than one file: a
/// character with forty animations must open without reading forty documents.
/// The <see cref="Doc"/> behind a ref is loaded when something actually needs
/// it (see <c>ProjectIo.LoadDocument</c>).
/// </summary>
public sealed class DocumentRef
{
    public string Id { get; set; } = Ids.NewId("docref");

    public string Name { get; set; } = "Animation";

    /// <summary>Path relative to the project root, with forward slashes.</summary>
    public string Path { get; set; } = "";
}

/// <summary>
/// A character: the unit of work Pillar 1 is named after. Its animations share
/// one palette, one set of references and one pivot, which is exactly what a
/// folder of loose files cannot express.
/// </summary>
public sealed class Character
{
    public string Id { get; set; } = Ids.NewId("char");

    public string Name { get; set; } = "Character";

    /// <summary>Folder name under <c>characters/</c>. Derived from the name, kept stable across renames.</summary>
    public string Slug { get; set; } = "character";

    /// <summary>
    /// The project palette this character's art paints with. Null means the
    /// character has no palette of its own and strokes carry literal colours.
    /// </summary>
    public string? PaletteId { get; set; }

    /// <summary>Where the engine positions this character from. Absent unless set.</summary>
    public Pivot? Pivot { get; set; }

    public List<DocumentRef> Animations { get; set; } = [];

    /// <summary>Reference art files, relative to the project root.</summary>
    public List<string> References { get; set; } = [];
}

/// <summary>
/// The serialized root of a project — <c>project.json</c>. Everything here is
/// an index; the artwork lives in the documents it points at.
/// </summary>
public sealed class ProjectManifest
{
    public int Version { get; set; } = 1;

    public string Id { get; set; } = Ids.NewId("proj");

    public string Name { get; set; } = "Project";

    /// <summary>Nullable on purpose: a project with no declared type writes no type key.</summary>
    public ProjectType? Type { get; set; }

    public List<Character> Characters { get; set; } = [];

    /// <summary>
    /// Documents that belong to the project but not to any character —
    /// backgrounds, tests, a one-off illustration.
    /// </summary>
    public List<DocumentRef> Documents { get; set; } = [];

    /// <summary>
    /// Palettes shared by everything in the project, as paths to <c>.gpl</c>
    /// files relative to the root. Read into <c>Project.Palettes</c> on load.
    /// </summary>
    public List<string> Palettes { get; set; } = [];
}
