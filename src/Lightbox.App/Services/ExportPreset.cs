using Lightbox.Core.Export;

namespace Lightbox.App.Services;

/// <summary>What an export produces.</summary>
public enum ExportTarget
{
    /// <summary>Numbered PNGs, one per frame. What the File menu has always offered.</summary>
    PngSequence,

    /// <summary>One sheet plus the generic sidecar. Every engine reads this.</summary>
    SpriteSheet,

    /// <summary>
    /// A sheet, the sidecar with a Unity block, and the Unity-side importer script.
    /// </summary>
    /// <remarks>
    /// A separate target rather than a checkbox on <see cref="SpriteSheet"/>, because
    /// it writes an extra file into somebody's project and takes a setting nothing
    /// else needs (world height). Naming it makes that visible.
    /// </remarks>
    Unity,
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
    public string Name { get; init; } = "Export";

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
    public bool UsesSheetSettings => Target is ExportTarget.SpriteSheet or ExportTarget.Unity;

    /// <summary>Whether this preset's engine settings mean anything.</summary>
    public bool UsesEngineSettings => Target is ExportTarget.Unity;
}
