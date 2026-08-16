using Lightbox.Core.Documents;
using Lightbox.Core.Export;

namespace Lightbox.Core.Projects;

/// <summary>How tightly each exported cell hugs the drawing.</summary>
public enum SpriteTrim
{
    /// <summary>Every cell is the full canvas. Simple, and often wasteful.</summary>
    None,

    /// <summary>
    /// One rectangle for the whole sequence: the union of every frame's ink.
    ///
    /// This is the default, and the reason is that the obvious alternative is
    /// wrong. Trimming each frame to its own tight box makes the trim follow
    /// the drawing rather than the rig, so the character <em>jitters</em> in
    /// the engine — every frame a different size, every frame a different
    /// offset, and nothing moved that the animator moved.
    /// </summary>
    Union,

    /// <summary>
    /// Each frame trimmed to its own ink, with the offset recorded so an
    /// importer can put it back. Tighter, and only safe because the offsets
    /// are measured from the pivot.
    /// </summary>
    PerFrame,
}

/// <summary>How the cells are arranged on the sheet.</summary>
public enum SpritePack
{
    /// <summary>
    /// A uniform grid. The default, and it stays the default.
    /// </summary>
    /// <remarks>
    /// Equal cells are what union bounds produce anyway, and every engine
    /// importer in existence reads a grid — including ones that ignore the
    /// sidecar entirely.
    /// </remarks>
    Grid,

    /// <summary>
    /// Bottom-left skyline packing: each sprite at its own size, tighter.
    /// </summary>
    /// <remarks>
    /// Worth reaching for when the frames are <b>ragged</b> — per-frame trimming,
    /// or a sheet holding several animations. It is only usable with the
    /// per-sprite rects in the sidecar, so an importer that reads
    /// <c>meta.columns</c> and divides will get this wrong; that is why a packed
    /// sheet reports <c>columns</c> and <c>rows</c> as zero rather than a number
    /// that would look plausible and be false.
    /// </remarks>
    Skyline,
}

/// <summary>How many artifacts a declaring scope produces.</summary>
public enum ExportGrouping
{
    /// <summary>One artifact per document. Today's behaviour, and the default.</summary>
    PerDocument,

    /// <summary>Everything under the declaring scope becomes one artifact.</summary>
    OneArtifact,

    /// <summary>
    /// One artifact per immediate child folder — shared settings, separate
    /// deliverables.
    /// </summary>
    PerChildFolder,
}

/// <summary>What an export produces.</summary>
public enum ExportTarget
{
    /// <summary>Numbered PNGs, one per frame. What the File menu has always offered.</summary>
    PngSequence,

    /// <summary>One sheet plus the generic sidecar. Every engine reads this.</summary>
    SpriteSheet,

    /// <summary>
    /// A sheet, the sidecar with a Godot block, and a Godot-side importer in GDScript.
    /// </summary>
    /// <remarks>
    /// A separate target for the same reason Unity is: it writes an extra file into
    /// somebody's project.
    /// </remarks>
    Godot,

    /// <summary>
    /// One <c>_stripN</c> strip per animation, which GameMaker slices on import.
    /// </summary>
    /// <remarks>
    /// The one target that writes several images and no importer: GameMaker has no
    /// import-time scripting, and the strip convention needs none. It also forces its own
    /// layout, so <see cref="ExportPreset.UsesStripLayout"/> hides the controls it would
    /// otherwise override.
    /// </remarks>
    GameMaker,

    /// <summary>
    /// A sheet, the sidecar, and an Unreal-side importer in Python.
    /// </summary>
    /// <remarks>
    /// Unreal's <c>.uasset</c> is binary and cannot be written from outside the editor,
    /// so the Unreal-side script is not a convenience here — it is the only mechanism.
    /// </remarks>
    Unreal,

    /// <summary>
    /// A sheet, the sidecar with a Unity block, and the Unity-side importer script.
    /// </summary>
    /// <remarks>
    /// A separate target rather than a checkbox on <see cref="SpriteSheet"/>, because
    /// it writes an extra file into somebody's project and takes a setting nothing
    /// else needs (world height). Naming it makes that visible.
    /// </remarks>
    Unity,

    /// <summary>
    /// The rig as a DragonBones skeleton file (<c>*_ske.json</c>), with our own
    /// rig JSON beside it. Motion only — pixels travel by sprite sheet.
    /// </summary>
    /// <remarks>
    /// The one target that needs an armature and touches no sheet: it exports
    /// the skeleton and its baked animation, for the engines whose DragonBones
    /// importers already exist. A document with no rig is refused with a
    /// sentence, not exported empty.
    /// </remarks>
    DragonBones,
}

/// <summary>
/// A named set of export settings.
/// </summary>
/// <remarks>
/// <para>
/// The record that makes Pillar 5's "one click" true on the <em>second</em> export.
/// Everything the pillar built — trim, packing, background handling, the engine
/// block — is a decision an artist makes once for a project and then wants to stop
/// thinking about. Without somewhere to keep them, "one click" means "one click and
/// six dropdowns, every time".
/// </para>
/// <para>
/// <b>Per application, not per document</b>, and for the same reason
/// <c>TimingPresetStore</c> is: these are *how this studio ships*, not a property
/// of one walk cycle. Invariant 4 is not in play — an export preset never reaches a
/// pixel in the document, it only decides what a file on disk looks like, and
/// deleting one cannot change a drawing.
/// </para>
/// <para>
/// A plain record with defaults matching today's behaviour, so a preset nobody
/// edited produces exactly the export the app produced before presets existed.
/// </para>
/// </remarks>
public sealed record ExportPreset
{
    /// <summary>
    /// What a scope declaration points at.
    /// </summary>
    /// <remarks>
    /// <b>Q30.</b> A name is not an identity — two folders can each reasonably
    /// have a preset called <em>Sheet</em>, and a declaration has to name one of
    /// them. Added when presets became scopeable rather than user-global.
    /// </remarks>
    public string Id { get; init; } = Ids.NewId("expreset");

    public string Name { get; init; } = "Export";

    /// <summary>
    /// How many artifacts the declaring scope produces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The thing "export scope" actually means.</b> A sprite sheet is one
    /// artifact from many documents, so everything packed into it shares a cell
    /// size — settings cannot be resolved per document and then packed together.
    /// Declaring a preset on <c>knight/</c> therefore says *everything under here
    /// is one sheet*; declaring it a level down makes locomotion its own.
    /// </para>
    /// <para>
    /// <see cref="ExportGrouping.PerChildFolder"/> is what lets settings be
    /// shared without the artifacts being merged — one declaration on
    /// <c>characters/</c> giving every character its own sheet at one cell size,
    /// which is the case declaration-as-boundary would otherwise have forced
    /// into a declaration per character.
    /// </para>
    /// </remarks>
    public ExportGrouping Grouping { get; init; } = ExportGrouping.PerDocument;

    /// <summary>
    /// Which statuses are allowed into the artifact, or null for all of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The state axis. Grouping answers *what packages together* and says
    /// nothing about *what is allowed out* — a production wants finished shots
    /// exported and work in progress not, and that is a status question.
    /// </para>
    /// <para>
    /// Null rather than every value listed, so a preset that does not filter
    /// writes no key and reads as "everything" rather than as a list somebody
    /// has to keep in step with the enum.
    /// </para>
    /// </remarks>
    public List<AssetStatus>? IncludeStatuses { get; init; }

    /// <summary>
    /// Reaching this status rebuilds the artifact, or null for manual only.
    /// </summary>
    /// <remarks>
    /// The trigger is per document and the effect is per artifact: one animation
    /// reaching <c>Ready</c> means the sheet holding it is rebuilt. That is what
    /// <see cref="Grouping"/> is for — it is the dependency graph that tells a
    /// status change what it invalidated.
    /// </remarks>
    public AssetStatus? RebuildOn { get; init; }

    /// <summary>Whether this preset filters by status at all.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Filters => IncludeStatuses is { Count: > 0 };

    /// <summary>Whether a document of this status belongs in the artifact.</summary>
    public bool Admits(AssetStatus? status) =>
        !Filters || (status is { } s && IncludeStatuses!.Contains(s));

    public ExportTarget Target { get; init; } = ExportTarget.SpriteSheet;

    // ---- sheet layout -------------------------------------------------------

    public SpriteTrim Trim { get; init; } = SpriteTrim.Union;

    public SpritePack Pack { get; init; } = SpritePack.Grid;

    /// <summary>Columns in a grid; null picks a near-square layout.</summary>
    public int? Columns { get; init; }

    /// <summary>Transparent gutter around each cell, against bilinear bleed.</summary>
    public int Padding { get; init; }

    // ---- what goes in -------------------------------------------------------

    public BackgroundHandling Background { get; init; } = BackgroundHandling.PaperOnly;

    // ---- engine -------------------------------------------------------------

    /// <summary>
    /// How many world units tall the canvas is, for
    /// <see cref="ExportTarget.Unity"/>.
    /// </summary>
    /// <remarks>
    /// Taken rather than assumed: how many pixels make a unit is a project-wide
    /// decision, and defaulting it to Unity's 100 silently leaves somebody
    /// wondering why their character is nine units tall.
    /// </remarks>
    public double WorldHeightUnits { get; init; } = 1.0;

    /// <summary>Write the Unity-side importer script beside the sheet.</summary>
    public bool WriteImporter { get; init; } = true;

    // ---- normal map ---------------------------------------------------------

    /// <summary>
    /// Also write a tangent-space normal map beside the sheet, as
    /// <c>&lt;name&gt;_normal.png</c>.
    /// </summary>
    /// <remarks>
    /// Off by default. It doubles the texture memory for the asset and most 2D games
    /// do not light their sprites at all, so it is something an artist turns on for
    /// the ones that do.
    /// </remarks>
    public bool NormalMap { get; init; }

    /// <summary>How the normal map is generated. Ignored unless <see cref="NormalMap"/>.</summary>
    /// <remarks>
    /// Green defaults to OpenGL, which is what Unity reads. The Unity target does not
    /// override it — a flipped green is a thing an artist should be able to see and set,
    /// not a thing that changes silently under them when they pick an engine.
    /// </remarks>
    public NormalMapOptions Normal { get; init; } = new();

    /// <summary>
    /// The three presets an artist should not have to invent.
    /// </summary>
    /// <remarks>
    /// Named for the job rather than for the settings, because "Character sprites"
    /// is what somebody is looking for and "Union trim, grid, detected background"
    /// is what they would have to decode. Each one is a position on the arguments
    /// this pillar already settled:
    /// <list type="bullet">
    /// <item><b>Character sprites</b> — union trim so the character does not jitter,
    /// grid because every importer reads one, and background detection on because a
    /// character never wants the grey layer.</item>
    /// <item><b>Packed atlas</b> — per-frame trim and skyline, which is the only
    /// combination where packing actually wins, and it is only readable through the
    /// sidecar.</item>
    /// <item><b>Backdrop</b> — no trim and <see cref="BackgroundHandling.Everything"/>,
    /// for the asset whose background is the point.</item>
    /// </list>
    /// Not stored in the settings file, so a later correction to one of them reaches
    /// everybody rather than being frozen into each artist's file the first time
    /// they opened the app.
    /// </remarks>
    public static IReadOnlyList<ExportPreset> BuiltIns { get; } =
    [
        new()
        {
            Name = "Character sprites",
            Target = ExportTarget.SpriteSheet,
            Trim = SpriteTrim.Union,
            Pack = SpritePack.Grid,
            Background = BackgroundHandling.Detected,
        },
        new()
        {
            Name = "Packed atlas",
            Target = ExportTarget.SpriteSheet,
            Trim = SpriteTrim.PerFrame,
            Pack = SpritePack.Skyline,
            Padding = 1,
            Background = BackgroundHandling.Detected,
        },
        new()
        {
            Name = "Backdrop",
            Target = ExportTarget.SpriteSheet,
            Trim = SpriteTrim.None,
            Pack = SpritePack.Grid,
            Background = BackgroundHandling.Everything,
        },
    ];

    /// <summary>Whether this preset's settings reach a sheet at all.</summary>
    /// <remarks>
    /// A PNG sequence has no cells, no atlas and no sidecar, so showing it a pack
    /// picker would be offering a control that does nothing — which teaches an
    /// artist that the controls lie.
    /// </remarks>
    public bool UsesSheetSettings =>
        Target is ExportTarget.SpriteSheet or ExportTarget.Unity
            or ExportTarget.Godot or ExportTarget.Unreal or ExportTarget.GameMaker;

    /// <summary>
    /// Whether the target dictates the sheet's layout rather than taking it.
    /// </summary>
    /// <remarks>
    /// GameMaker derives a frame's width by dividing the strip's width by the frame count
    /// in its filename, so the strip has to be one row of uniform cells with no padding.
    /// Packing, columns and padding are therefore decided rather than chosen, and showing
    /// controls the exporter overrides would teach an artist that the controls lie.
    /// </remarks>
    public bool UsesStripLayout => Target is ExportTarget.GameMaker;

    /// <summary>Whether this preset's engine settings mean anything.</summary>
    /// <remarks>
    /// Unreal as well as Unity, because both need to know how big the drawing is in
    /// the world — and they disagree about what a world unit is, which is precisely
    /// why the field is stated in metres and converted per engine rather than passed
    /// through. See <see cref="Lightbox.Core.Export.UnrealConvert.UnrealUnitsPerMetre"/>.
    /// </remarks>
    public bool UsesEngineSettings => Target is ExportTarget.Unity or ExportTarget.Unreal;
}
