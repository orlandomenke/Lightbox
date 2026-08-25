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
/// Where an imported copy came from: the library project and the entry inside
/// it. The stamp is what lets a re-import merge instead of duplicate (Q138) —
/// it replaces exactly the copies that came from the same source and never
/// touches work the artist made locally.
/// </summary>
/// <remarks>
/// <para>
/// Optional and absent on anything never imported — the "optional means
/// absent" rule; a project that never used the library writes no origin keys.
/// Identified by <b>ids, never paths</b>: the manifest id survives the library
/// being moved, renamed or offline, which is also what makes these stamps the
/// edges a future dependency graph reads (Pillar 3) without a migration.
/// </para>
/// <para>
/// <see cref="Hash"/> is the imported copy's content at the moment of import,
/// so "edited since import" is answerable without the library present: hash
/// the copy again and compare. Replacing an edited copy is the one destructive
/// act in the merge, and it warns first, Q35-style.
/// </para>
/// </remarks>
public sealed class ImportOrigin
{
    /// <summary>The library project's manifest id.</summary>
    public string LibraryId { get; set; } = "";

    /// <summary>The source folder or document ref id inside that library.</summary>
    public string SourceId { get; set; } = "";

    /// <summary>
    /// Content hash of the copy as imported — documents only, null on folders.
    /// </summary>
    public string? Hash { get; set; }

    public bool Matches(string libraryId, string sourceId)
        => LibraryId == libraryId && SourceId == sourceId;
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

    /// <summary>
    /// Whether the document is a template, refreshed whenever it is written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A hint like <see cref="Frames"/>, and for the same reason: the flag
    /// lives in the document (<c>Doc.IsTemplate</c>, so an artist can mark
    /// work they already did), and a row that wanted the truth would have to
    /// load the file — which is the one thing the folder layout exists to
    /// avoid. Written at the moment it can be right, the save that produced
    /// the file; <c>Templates.InProject</c> stays the answer that reads the
    /// documents.
    /// </para>
    /// <para>
    /// Null rather than false when it is not one, so an ordinary document
    /// writes no key — the same rule the flag itself follows in the document.
    /// </para>
    /// </remarks>
    public bool? IsTemplate { get; set; }

    /// <summary>
    /// Where this document was imported from, or null for work made here —
    /// see <see cref="ImportOrigin"/>.
    /// </summary>
    public ImportOrigin? Origin { get; set; }

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

    /// <summary>
    /// Tags an artist put on this document, or null when nobody has.
    /// </summary>
    /// <remarks>
    /// <b>The same shape <see cref="ProjectFolder.Tags"/> already has</b>, and
    /// it had nowhere to go on a document until the project window needed it —
    /// tagging is half of what that window is for.
    /// <para>
    /// <b>Free strings, and the vocabulary is derived.</b> Every tag in use
    /// anywhere in the project is the offered list; typing a new one adds it by
    /// using it. A declared vocabulary is a registry somebody maintains and a
    /// wall in front of the first word that is not in it.
    /// </para>
    /// </remarks>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Resources declared on this document itself — the narrowest scope.
    /// </summary>
    /// <remarks>
    /// <b>The tier <see cref="ResourceScopes"/> always described and never
    /// had.</b> Its own comment says the chain is four deep — user library →
    /// project → folder path → document — and only the middle two existed, so
    /// "this one drawing paints from that palette" had no target.
    /// <para>
    /// Nullable and absent until something is declared, and nearest already
    /// wins, so a document's own palette beating its folder's needs no new rule.
    /// </para>
    /// </remarks>
    public List<ScopedResource>? Resources { get; set; }

    /// <summary>
    /// Who is working on this, by <see cref="Person.Id"/>, or null when nobody
    /// has said.
    /// </summary>
    /// <remarks>
    /// <b>Q43.</b> An id into <see cref="ProjectManifest.People"/> rather than a
    /// typed name, and the reason is the feature's own purpose: this is the
    /// surface that replaces a spreadsheet, and two spellings of one person is
    /// exactly the spreadsheet problem. Grouping by assignee has to be exact to
    /// be worth having.
    /// <para>
    /// It can name somebody who has been deleted. That is the shape the palette
    /// path already has and it takes the same answer — the id stays, resolution
    /// returns null, and removing a person says how many documents name them
    /// before it happens.
    /// </para>
    /// </remarks>
    public string? AssigneeId { get; set; }

    /// <summary>Whether anybody has set a status on this document.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasStatus => Status is not null;
}

/// <summary>
/// A pointer to a character sheet that belongs to the project, not the sheet
/// itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q25, re-answered 2026-08-12.</b> The first answer put sheets inside one
/// document (<c>Doc.ReferenceSheets</c>), which made them impossible to share
/// or file — the knight's model sheet was locked inside whichever animation
/// happened to create it. Inside a project a sheet is now its own file, filed
/// in a folder exactly the way a document is, and every document in that
/// folder's subtree can consult it. Outside a project nothing changed: a
/// standalone document keeps its sheets in <c>Doc.ReferenceSheets</c>, because
/// there is no tree to file into.
/// </para>
/// <para>
/// <see cref="Id"/> is the <c>ReferenceSheet</c>'s own id rather than a second
/// one, so a scoped-resource declaration that names a sheet and this registry
/// agree about what they are naming.
/// </para>
/// </remarks>
public sealed class SheetRef
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "Character";

    /// <summary>Path relative to the project root, with forward slashes.</summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// The folder this sheet is filed in — visible to every document in that
    /// folder's subtree — or null for the whole project.
    /// </summary>
    public string? FolderId { get; set; }
}

/// <summary>
/// Somebody working on the project.
/// </summary>
/// <remarks>
/// <b>Q43 for the record, Q45 for the boundary — and the boundary matters
/// more.</b> A name and an id. <b>It never gains a role and never gains
/// rights</b>, and that is a decision rather than an omission: the manifest is
/// plain JSON on disk, so a permission here is one a text editor defeats. A
/// permission that cannot be enforced is a UI that lies about what it enforces.
/// <para>
/// It is not the first half of an accounts system either — no auth, no sync, no
/// identity. Sharing is the project file over git or a drive; a tracker adapter
/// is the seam if a studio needs one, and it needs no new model because
/// documents already have stable ids. If Lightbox ever grows real accounts, this
/// is what one replaces.
/// </para>
/// <para>
/// The point of the id is that a rename fixes every row rather than none — which
/// is the whole reason a registry won over a typed name.
/// </para>
/// </remarks>
public sealed class Person
{
    public string Id { get; set; } = Ids.NewId("person");

    public string Name { get; set; } = "";

    /// <summary>
    /// A colour for their rows and chips, or null to let the surface pick one.
    /// </summary>
    /// <remarks>
    /// Nullable so a project that never chose one writes no key, and so a
    /// surface that wants to derive a colour from the name may.
    /// </remarks>
    public string? Color { get; set; }
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

    /// <summary>
    /// The people working on this project, once somebody has been added.
    /// </summary>
    /// <remarks>
    /// <b>Q43.</b> Null and absent until the first person, so a project one
    /// artist works on alone carries no people key — the ordinary state, and the
    /// one where a registry would be pure overhead.
    /// </remarks>
    public List<Person>? People { get; set; }

    /// <summary>
    /// The project's character sheets, once there are any — an index like
    /// <see cref="Documents"/>, each filed by <see cref="SheetRef.FolderId"/>.
    /// </summary>
    /// <remarks>
    /// Null and absent until the first sheet, so a project that never made one
    /// carries no key. The sheet's views and strokes live in the file the entry
    /// points at, loaded when something actually looks at it.
    /// </remarks>
    public List<SheetRef>? Sheets { get; set; }

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
