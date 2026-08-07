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
    /// The folder this document is filed in, or null for the project root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Beside <see cref="Path"/> rather than instead of it, and that is the
    /// migration: a document written before folders existed keeps the path it
    /// has and reports no folder, so every project on disk today opens and
    /// saves unchanged. <see cref="Path"/> stays the truth about where the file
    /// is; this says where the artist put it.
    /// </para>
    /// <para>
    /// The two are kept in step by <see cref="ProjectFolders.FileDocument"/>,
    /// which is the only thing that should set either — deriving the path on
    /// every read instead would rename files underneath an artist who renamed a
    /// folder, which is a move the project deliberately does not make.
    /// </para>
    /// </remarks>
    public string? FolderId { get; set; }

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

    /// <summary>
    /// Where this document is in production, or null when nobody has said.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>On the manifest rather than in the document</b>, and that is the whole
    /// choice: marking something Ready must not dirty the artwork file, must not
    /// touch a pixel, and must not need the document open. Status is production
    /// metadata about a drawing, not part of it — so invariant 1 is not in play and
    /// a status change cannot alter what re-renders.
    /// </para>
    /// <para>
    /// Nullable so a project that never uses statuses writes no key, and so
    /// "nobody has said" stays distinct from "Design". A project imported from a
    /// folder of loose files has no statuses, and pretending every file is at the
    /// start of a pipeline it was never in would be a guess.
    /// </para>
    /// </remarks>
    public AssetStatus? Status { get; set; }

    /// <summary>
    /// Bumped when this document is saved, so anything built from it can tell
    /// whether it is out of date.
    /// </summary>
    /// <remarks>
    /// <b>Not a new subsystem.</b> This is <c>Symbol.Version</c> against
    /// <c>SymbolPlacement.SeenVersion</c> — Pillar 3's S7 — applied to a second
    /// kind of thing. An integer bumped on edit, and whoever consumed it records
    /// what they saw; the two differing *is* staleness. No history, no diffing,
    /// no store.
    /// <para>
    /// It answers the case a status filter cannot: a shipped sheet where one
    /// animation goes back to <c>Reopened</c> keeps what it had and reads stale,
    /// rather than regenerating with a hole in it.
    /// </para>
    /// </remarks>
    public int Version { get; set; } = 1;

    /// <summary>Whether anybody has set a status on this document.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasStatus => Status is not null;
}


/// <summary>
/// The serialized root of a project — <c>project.json</c>. Everything here is
/// an index; the artwork lives in the documents it points at.
/// </summary>
public sealed class ProjectManifest
{
    /// <summary>
    /// The manifest format. <b>2</b> since <c>DESIGN-project-scoping.md</c>
    /// dissolved characters and scenes into the folder tree.
    /// </summary>
    /// <remarks>
    /// There is deliberately no migration from 1 — Q36: the application is
    /// alpha, single-user, and nothing has been produced in it, so writing one
    /// for zero real projects is cost with no beneficiary. <c>ProjectIo.Load</c>
    /// refuses an older manifest with a sentence rather than crashing on it, and
    /// says the drawings survive: documents are their own files in their own
    /// unchanged format, so only the index is lost.
    ///
    /// Write the migration the day a second person has a project.
    /// </remarks>
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;

    public string Id { get; set; } = Ids.NewId("proj");

    public string Name { get; set; } = "Project";

    /// <summary>Nullable on purpose: a project with no declared type writes no type key.</summary>
    public ProjectType? Type { get; set; }

    /// <summary>
    /// <b>Every document in the project</b>, each filed by
    /// <see cref="DocumentRef.FolderId"/>.
    /// </summary>
    /// <remarks>
    /// One list, and that is the point of <b>B114</b>. There used to be three —
    /// this one for loose documents, <c>Character.Animations</c>, and
    /// <c>Scene.Shots</c> — and only this one was wired into scoped resources
    /// and export planning. A character's animations therefore resolved no
    /// palette from any folder and appeared in no export plan, silently, which
    /// is most of the content of an animation project.
    /// </remarks>
    public List<DocumentRef> Documents { get; set; } = [];

    /// <summary>
    /// The folder tree the artist built, flat, each folder naming its parent.
    /// Null until the first one is made, so a project that never used folders
    /// writes no folder key at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Flat with parent ids rather than nested.</b> A nested list reads
    /// nicely and makes every other operation awkward: moving a folder becomes
    /// a splice, a document's folder cannot be named by one id, and a
    /// hand-edited cycle is unrepresentable in the type but perfectly
    /// representable in the file — so the code has to defend against it anyway.
    /// Flat means <see cref="ProjectFolders.Move"/> is one assignment, and the
    /// one place that walks the tree is the one place that guards it.
    /// </para>
    /// <para>
    /// The <em>order</em> of this list is not the display order and nothing
    /// should read it as one. A surface sorts by name, or by whatever it
    /// offers; the manifest only records what exists and what contains what.
    /// </para>
    /// </remarks>
    public List<ProjectFolder>? Folders { get; set; }

    /// <summary>
    /// Palettes shared by everything in the project, as paths to <c>.gpl</c>
    /// files relative to the root. Read into <c>Project.Palettes</c> on load.
    /// </summary>
    public List<string> Palettes { get; set; } = [];

    /// <summary>
    /// The brush this project's documents are painted with, for
    /// <see cref="Documents.BrushScope.PerProject"/>. Null and absent from
    /// the file otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Here rather than on a document because the answer has to reach the
    /// pages that do not exist yet: page one remembering its own brush leaves
    /// page eleven starting from whatever you last used elsewhere. It sits
    /// beside <see cref="Palettes"/> for the same reason those do — Pillar 1
    /// says a character's work shares one palette and one brush set.
    /// </para>
    /// <para>
    /// It is a bookmark, not a setting that reaches pixels, so invariant 4 is
    /// untouched: every stroke still carries its own settings and nothing
    /// renders from this.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Resources declared on the project itself — the scope above every folder.
    /// </summary>
    /// <remarks>
    /// <b>Q30.</b> Null and absent until something is declared. This sits beside
    /// <see cref="Palettes"/> rather than replacing it: the old list is how
    /// existing projects say the same thing, and Q30's migration answer was
    /// new-projects-only, so both are read for as long as old projects exist.
    /// </remarks>
    public List<ScopedResource>? Resources { get; set; }

    /// <summary>
    /// Guide sets shared across the project, once there are any.
    /// </summary>
    /// <remarks>
    /// <b>Q30.</b> Null and absent until one is made — a project that never
    /// shared a guide carries no key. Which documents may pull from which set is
    /// <see cref="GuideScopes"/>; this is only where they live.
    /// </remarks>
    public List<GuideSet>? GuideSets { get; set; }

    /// <summary>Export presets belonging to this project, once there are any.</summary>
    /// <remarks>
    /// <b>Q30.</b> A preset used to be a user setting in <c>ExportPresetStore</c>,
    /// which was right while every project exported one way and stopped being
    /// right once the knight and the boss want different cell sizes. Null and
    /// absent until one is made, and the user's store still supplies the
    /// built-ins.
    /// </remarks>
    public List<ExportPreset>? ExportPresets { get; set; }

    /// <summary>
    /// What each artifact was last built from, so staleness survives a restart.
    /// </summary>
    /// <remarks>
    /// Null and absent until something is exported. Keyed by the scope that
    /// produced it — a scope makes one deliverable, so the scope names it.
    /// </remarks>
    public Dictionary<string, ExportRecord>? ExportRecords { get; set; }

    public Documents.BrushSettings? Brush { get; set; }

    /// <summary>
    /// Brush tips shared by everything in the project. Null and absent from
    /// the file for a project that never made one.
    /// </summary>
    /// <remarks>
    /// Beside <see cref="Palettes"/> and <see cref="Brush"/>, and for the same
    /// reason: a tip is part of how a project looks, and the next animation
    /// under this character should start with the brushes the last one used.
    /// The raster still travels into each document that paints with it — this
    /// is a library to choose from, not what a drawing renders out of.
    /// </remarks>
    public List<Documents.BrushTip>? Tips { get; set; }

}
