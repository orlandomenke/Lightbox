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

    /// <summary>
    /// How long the document is, refreshed whenever it is written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Derived data in an index, which is normally a smell — but a scene list
    /// has to show a running time, and computing it honestly would mean
    /// loading every document in the project. That is the one thing the folder
    /// layout exists to avoid.
    /// </para>
    /// <para>
    /// So it is a <b>hint</b>, written at the only moment it can be right: the
    /// save that produced the file. Zero means "not known" rather than "empty",
    /// and a scene containing an unknown shot reports its duration as unknown
    /// instead of guessing low. Nothing reads this to render; it exists to put
    /// a number next to a row.
    /// </para>
    /// </remarks>
    public int Frames { get; set; }

    /// <summary>Frame rate at the last save. Zero means not known.</summary>
    public int Fps { get; set; }

    /// <summary>Seconds this shot runs, or null when the hint is not filled in.</summary>
    public double? Seconds => Frames > 0 && Fps > 0 ? Frames / (double)Fps : null;
}

/// <summary>
/// A version of a character that reuses its animations — Winter Armour,
/// Damaged, Player Two.
/// </summary>
/// <remarks>
/// A variant owns <b>overrides, not copies</b>. It names a palette, and it may
/// point specific animations at different documents; everything it does not
/// override comes from the character. That is what "inherits animations"
/// means: a walk cycle drawn once is the walk cycle of every variant, and
/// fixing it fixes all of them.
///
/// The mechanism is the live palette already in the engine. A variant is
/// mostly a different set of swatch values behind the same swatch ids, so
/// switching variant re-scopes the registry and the same drawings paint in
/// different colours — no second copy of the art, and nothing to keep in sync.
///
/// Shape differences that colour cannot express (a helmet the base character
/// does not have) are what <see cref="AnimationOverrides"/> is for: one
/// animation replaced wholesale, the rest still shared.
/// </remarks>
public sealed class CharacterVariant
{
    public string Id { get; set; } = Ids.NewId("var");

    public string Name { get; set; } = "Variant";

    /// <summary>The palette this variant paints with. Null falls back to the character's.</summary>
    public string? PaletteId { get; set; }

    /// <summary>
    /// Animations this variant replaces, base <see cref="DocumentRef.Id"/> →
    /// the variant's own ref. Everything absent from here is inherited.
    /// </summary>
    public Dictionary<string, DocumentRef> AnimationOverrides { get; set; } = [];
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

    /// <summary>
    /// Versions of this character that reuse its animations. Empty is the
    /// ordinary state — the base character is not a variant and does not need
    /// to be listed as one, so a character nobody varied carries no variant
    /// keys at all.
    /// </summary>
    public List<CharacterVariant> Variants { get; set; } = [];

    /// <summary>
    /// The animations a variant actually plays: its own overrides where it has
    /// them, the character's everywhere else. Null asks for the base
    /// character, which is what "no variant selected" means.
    /// </summary>
    public IEnumerable<DocumentRef> AnimationsFor(CharacterVariant? variant) =>
        variant is null
            ? Animations
            : Animations.Select(a => variant.AnimationOverrides.GetValueOrDefault(a.Id, a));

    public CharacterVariant? FindVariant(string? id) =>
        id is null ? null : Variants.FirstOrDefault(v => v.Id == id);
}

/// <summary>
/// A scene: a run of shots that belong together in a film or a show.
/// </summary>
/// <remarks>
/// <para>
/// The second axis of a project, and deliberately not the first. A
/// <see cref="Character"/> groups drawings by <i>who</i>; a scene groups them
/// by <i>when</i>. They cross: one scene holds several characters, and one
/// character appears in several scenes, which is exactly why neither can be a
/// folder inside the other.
/// </para>
/// <para>
/// Named for the shots output target in the charter — a sequence for a film,
/// where the canvas is a world and a camera frames part of it. A project
/// making sprite sheets has no use for scenes and, following the camera's
/// rule, gets none: <see cref="ProjectManifest.Scenes"/> is null until one is
/// made.
/// </para>
/// <para>
/// Called <c>ProjectScene</c> rather than <c>Scene</c> because
/// <see cref="Lightbox.Core.Documents.Scene"/> already means the canvas world
/// inside one document. Two types called Scene in one solution is a bug
/// waiting for someone to write the wrong <c>using</c>.
/// </para>
/// </remarks>
public sealed class ProjectScene
{
    public string Id { get; set; } = Ids.NewId("scn");

    public string Name { get; set; } = "Scene";

    /// <summary>Folder name under <c>scenes/</c>. Kept stable across renames.</summary>
    public string Slug { get; set; } = "scene";

    /// <summary>The shots, in the order they play.</summary>
    public List<DocumentRef> Shots { get; set; } = [];

    /// <summary>
    /// What the scene is, in the director's words. Absent unless written —
    /// a scene list with an empty note on every row is a scene list with a
    /// column of nothing in it.
    /// </summary>
    public string? Notes { get; set; }
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
    /// The scenes, in running order. Null until one is made, so a project that
    /// is a bag of sprites writes no scene key at all.
    /// </summary>
    public List<ProjectScene>? Scenes { get; set; }

    /// <summary>
    /// Palettes shared by everything in the project, as paths to <c>.gpl</c>
    /// files relative to the root. Read into <c>Project.Palettes</c> on load.
    /// </summary>
    public List<string> Palettes { get; set; } = [];
}
