namespace Lightbox.Core.Export;

/// <summary>An engine Lightbox exports for.</summary>
public enum EngineTarget
{
    Unity,
    Godot,
    Unreal,
    GameMaker,
}

/// <summary>How the engine gets from Lightbox's files to its own assets.</summary>
public enum ImportMechanism
{
    /// <summary>A C# editor script we ship, run from the engine's own menu.</summary>
    EditorScriptCSharp,

    /// <summary>A GDScript <c>EditorScript</c> we ship.</summary>
    EditorScriptGdScript,

    /// <summary>A Python script we ship, run from the editor.</summary>
    EditorScriptPython,

    /// <summary>
    /// No script at all: the engine's own documented behaviour, triggered by how the
    /// file is named.
    /// </summary>
    FilenameConvention,
}

/// <param name="Engine">Which engine.</param>
/// <param name="MinimumVersion">The oldest version this was written against.</param>
/// <param name="Mechanism">How the engine-side work happens.</param>
/// <param name="ApiSurface">
/// The engine symbols the importer calls, spelled as they appear in the shipped source.
/// <c>EngineApiTests</c> asserts each one is actually there, so this list cannot claim an
/// API the code does not use, and code that switches API without updating the list fails.
/// </param>
/// <param name="OnAnOlderVersion">
/// What happens below <paramref name="MinimumVersion"/> — stated because the answer is
/// usually "nothing visible", which is the whole hazard.
/// </param>
/// <param name="DeprecationRisk">What could stop working, and how we would find out.</param>
/// <param name="VerifiedByRealImport">
/// Whether anybody has run this against a real installation of the engine and recorded
/// the result. <b>False everywhere today</b>, and deliberately part of the record rather
/// than a caveat in a document nobody opens.
/// </param>
public sealed record EngineApiNote(
    EngineTarget Engine,
    string MinimumVersion,
    ImportMechanism Mechanism,
    IReadOnlyList<string> ApiSurface,
    string OnAnOlderVersion,
    string DeprecationRisk,
    bool VerifiedByRealImport = false);

/// <summary>
/// What each engine exporter depends on, as data rather than as prose.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because a write-only integration cannot fail visibly on our side.</b>
/// Lightbox produces a file, hands it over, and never sees the other end — so "it compiles
/// and the tests pass" says nothing about whether anything read it. The defect that filed
/// this: the Unity importer used <c>TextureImporter.spritesheet</c>, which stopped working
/// in Unity 2021.2 and was removed in 2022.2. Assigning it throws nothing and slices
/// nothing, so the import logged success and produced one sprite. Every Unity anybody is
/// running would have silently ignored the export.
/// </para>
/// <para>
/// <b>Data rather than a document, so it can be checked.</b> A note in a markdown file
/// drifts from the code the first time somebody changes an API call.
/// <c>EngineApiTests</c> asserts every symbol named here appears in the importer it
/// belongs to, and that every engine target has a note at all — so adding an engine
/// without recording its API surface fails, and changing a call without updating the
/// record fails.
/// </para>
/// <para>
/// <b><see cref="EngineApiNote.VerifiedByRealImport"/> is false on every entry, and that
/// is the honest state.</b> None of these has been run against a real installation from
/// here. Recording it as a field rather than as a sentence in a design document means the
/// gap is visible from the code and has to be edited away deliberately.
/// </para>
/// </remarks>
public static class EngineApiNotes
{
    /// <summary>One note per engine. Every engine, checked by test.</summary>
    public static IReadOnlyList<EngineApiNote> All { get; } =
    [
        new(
            EngineTarget.Unity,
            MinimumVersion: "2021.2",
            ImportMechanism.EditorScriptCSharp,
            ApiSurface:
            [
                "SpriteDataProviderFactories",
                "GetSpriteEditorDataProviderFromObject",
                "SetSpriteRects",
                "AssetDatabase",
                "AnimationClip",
                "AnimationUtility",
            ],
            OnAnOlderVersion:
                "Below 2021.2 the script falls back to TextureImporter.spritesheet, which "
                + "is the documented path for those versions. The branch is compiled on "
                + "UNITY_2021_2_OR_NEWER, so the wrong half cannot run.",
            DeprecationRisk:
                "This is the API that already bit us once, from the other side: "
                + "TextureImporter.spritesheet was removed in 2022.2 while still compiling. "
                + "ISpriteEditorDataProvider lives in the com.unity.2d.sprite package rather "
                + "than the engine, so it can move on the package's schedule and not "
                + "Unity's — the script checks for the package and says so if it is absent."),

        new(
            EngineTarget.Godot,
            MinimumVersion: "4.0",
            ImportMechanism.EditorScriptGdScript,
            ApiSurface:
            [
                "AtlasTexture",
                "SpriteFrames",
                "ResourceSaver",
                "add_frame",
                "set_animation_speed",
                "remove_animation",
                "DirAccess",
                "FileAccess",
            ],
            OnAnOlderVersion:
                "On 3.x SpriteFrames.add_frame took no duration argument, so every frame's "
                + "timing would be dropped without an error. The script says it targets "
                + "Godot 4 for that reason.",
            DeprecationRisk:
                "Low, and low by construction: Lightbox writes no .tres, so the resource "
                + "format is Godot's own business and stays correct across versions for "
                + "free. What remains is the handful of method names above."),

        new(
            EngineTarget.Unreal,
            MinimumVersion: "5.0",
            ImportMechanism.EditorScriptPython,
            ApiSurface:
            [
                "AssetToolsHelpers",
                "create_asset",
                "AssetImportTask",
                "PaperSprite",
                "PaperSpriteFactory",
                "PaperFlipbook",
                "PaperFlipbookFactory",
                "PaperFlipbookKeyFrame",
                "source_texture",
                "source_uv",
                "source_dimension",
                "pixels_per_unreal_unit",
                "custom_pivot_point",
                "key_frames",
                "frame_run",
            ],
            OnAnOlderVersion:
                "The same calls exist back to 4.26, but the Python API is marked "
                + "experimental throughout, so an older engine is untested rather than "
                + "known-broken.",
            DeprecationRisk:
                "The highest of the four, and handled in the script rather than hoped "
                + "about. Paper2D's scripting surface is experimental and property names "
                + "have moved between versions, so every write goes through one helper that "
                + "records what it could not set, and the run ends by naming each failure "
                + "and stating the assets are incomplete. A renamed property produces a "
                + "loud error rather than an empty sprite."),

        new(
            EngineTarget.GameMaker,
            MinimumVersion: "2.3",
            ImportMechanism.FilenameConvention,
            ApiSurface: ["_strip"],
            OnAnOlderVersion:
                "The strip convention predates 2.3 and is unchanged by it; 2.3 is named "
                + "because it is where the .yy format last changed, which is what made "
                + "writing project files a bad idea and the convention the right answer.",
            DeprecationRisk:
                "The lowest of the four by a wide margin. There is no API and no file "
                + "format here — only a documented filename rule, so there is nothing to "
                + "version. The risk we avoided is the .yy schema, which moves between "
                + "releases and is not written at all."),
    ];

    /// <summary>The note for an engine.</summary>
    public static EngineApiNote For(EngineTarget engine) =>
        All.First(n => n.Engine == engine);

    /// <summary>
    /// Engines whose importer has never been run against the real thing.
    /// </summary>
    /// <remarks>
    /// Offered as a query rather than left implicit so the gap can be reported, and so
    /// closing it for one engine does not need the others re-checked.
    /// </remarks>
    public static IReadOnlyList<EngineApiNote> Unverified => UnverifiedIn(All);

    /// <summary>
    /// The unverified entries in any set of notes.
    /// </summary>
    /// <remarks>
    /// Takes the set rather than only reading <see cref="All"/> so the narrowing is
    /// testable: over a static list, "the unverified ones" and "all of them" are the same
    /// collection today, and a test that cannot distinguish them proves nothing.
    /// </remarks>
    public static IReadOnlyList<EngineApiNote> UnverifiedIn(IEnumerable<EngineApiNote> notes) =>
        notes.Where(n => !n.VerifiedByRealImport).ToList();
}
