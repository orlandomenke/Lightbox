using System.Text.Json;
using Lightbox.Core.Documents;

namespace Lightbox.App.Services;

/// <summary>
/// The preferences that are not about pixels.
/// </summary>
/// <remarks>
/// Deliberately small, and deliberately separate from anything a document
/// carries. Invariant 4 says a setting that reaches pixels lives on the stroke,
/// so that an artist returning to a scene after a month finds it exactly as
/// they left it. What is left over — how often to autosave, where the last
/// project was — is about the person and belongs here.
/// </remarks>
public sealed class AppSettings
{
    /// <summary>
    /// Minutes between autosaves. Zero turns it off.
    /// </summary>
    /// <remarks>
    /// A minute by default. Autosave writes the whole document, and at this
    /// app's sizes that is milliseconds, so the cost of being generous is
    /// nothing next to the cost of losing a drawing.
    /// </remarks>
    public double AutosaveMinutes { get; set; } = 1;

    /// <summary>
    /// Onion skin, which is a property of the artist, not of the artwork.
    /// </summary>
    /// <remarks>
    /// Here rather than in the document because onion skin never touches
    /// pixels — it is a drawing aid, on the view-only side of invariant 5 —
    /// and because an animator's depth and falloff are how *they* work, not
    /// something each scene should be asked again. Here rather than in the
    /// workspace for the same reason: rearranging panels must not change how
    /// far back you can see.
    /// </remarks>
    public OnionSettings Onion { get; set; } = new();

    /// <summary>
    /// The motion trail, here for the same reason <see cref="Onion"/> is: a
    /// drawing aid that never reaches pixels, set up the way the artist works.
    /// </summary>
    public MotionTrailSettings Trail { get; set; } = new();

    /// <summary>
    /// What you had open last. See <see cref="RecentItems"/> for why it lives
    /// with the person rather than with any document.
    /// </summary>
    public RecentItems Recent { get; set; } = new();

    /// <summary>Where character libraries live — see <see cref="LibrarySettings"/>.</summary>
    public LibrarySettings Library { get; set; } = new();

    /// <summary>
    /// How fonts are found and whether they travel — see
    /// <see cref="FontSettings"/>.
    /// </summary>
    /// <remarks>
    /// Here rather than on the document because neither switch can change a
    /// picture: text is baked to contours when it is set, so a document renders
    /// the same whatever these say. What they decide is where an artist may pick
    /// a font from and how portable the file they save is.
    /// </remarks>
    public FontSettings Fonts { get; set; } = new();

    /// <summary>
    /// How far a frame's ink area may drift from the shot's median before the
    /// volume checker flags it, as a fraction (0.10 = ten percent).
    /// </summary>
    /// <remarks>
    /// With the onion settings for the same reason they are here: the checker
    /// reads the drawing and never touches it, so this is a property of the
    /// artist's eye, not of any document. Ten percent is where drift starts
    /// reading as off-model rather than as line-weight noise.
    /// </remarks>
    public double VolumeTolerance { get; set; } = 0.10;

    /// <summary>
    /// Show the start screen when the application opens.
    /// </summary>
    /// <remarks>
    /// On by default now that there is one. Escape still leaves you on a blank
    /// untitled document, so "open it and draw" is a keystroke rather than a
    /// setting — but somebody who only ever wants that can turn the screen off
    /// from the screen itself.
    /// </remarks>
    public bool ShowStartScreen { get; set; } = true;

    /// <summary>
    /// Whether to open a console window at startup for the diagnostic traces.
    /// </summary>
    /// <remarks>
    /// Off, and it stays off unless somebody deliberately turns it on from
    /// <b>Help</b>. It is remembered rather than asked each time because the
    /// use for it is "turn this on, restart, and make the problem happen
    /// again" — a switch that forgot itself between runs would be no use for
    /// the one job it has.
    /// </remarks>
    public bool ShowDiagnosticsConsole { get; set; }

    /// <summary>
    /// Present each frame through the desktop compositor instead of straight
    /// to a swap chain. Off, because the swap chain is measurably faster.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An escape hatch rather than a preference, and named for what it
    /// does.</b> Measured on the owner's machine, 2026-08-26, same build and
    /// same brush across two sessions of about six thousand pointer events:
    /// presenting to a low-latency swap chain took <c>publish -&gt; drawn</c>
    /// from <b>52.07 ms to 30.67 ms</b>, the wait for a visual pass from 21.27
    /// to 10.19, and delivered 544 frames where the compositor route delivered
    /// 381. Ink arrived in 11.3-event batches instead of 16.8, which is the
    /// half an artist feels as chunkiness rather than as lateness.
    /// </para>
    /// <para>
    /// The trade is real and was audited before this became the default: the
    /// swap chain gives up DWM compositing, so window transparency and blur
    /// effects go with it. Lightbox uses none — no transparency hint, no
    /// acrylic, no mica, and B255 already draws popups inside the window
    /// rather than as native ones. Every <c>Background="Transparent"</c> in the
    /// XAML is a control inside a window, which Skia composites itself.
    /// </para>
    /// <para>
    /// It exists at all because a graphics driver can refuse a swap chain in
    /// ways a test cannot predict, and "turn it off and restart" has to be
    /// reachable by somebody whose window will not draw. Read once at startup —
    /// a platform option cannot change while the app runs.
    /// </para>
    /// </remarks>
    public bool PresentThroughDesktopCompositor { get; set; }

    /// <summary>
    /// Whether to autosave over the document's own file once it has one,
    /// rather than only to the recovery copy.
    /// </summary>
    /// <remarks>
    /// Off by default, and that is not timidity: silently rewriting the file
    /// someone opened takes away the ability to close without saving, which is
    /// a real editing move. Someone who wants it can say so.
    /// </remarks>
    public bool AutosaveInPlace { get; set; }

    /// <summary>
    /// The pitch a new grid guide is made with, in document pixels.
    /// </summary>
    /// <remarks>
    /// A preference rather than document data, and the distinction matters:
    /// once a grid exists its spacing lives on the guide, so changing this
    /// never moves a lattice somebody already drew against. Invariant 4 is
    /// about pixels and this never reaches them, but the same reasoning
    /// applies — a setting must not reach back into finished work.
    /// </remarks>
    public double GridSpacing { get; set; } = 32;

    /// <summary>How close a point must be to a guide to be pulled onto it, in document pixels.</summary>
    public double SnapTolerance { get; set; } = 12;

    /// <summary>How many rays a new vanishing point is drawn with.</summary>
    /// <inheritdoc cref="GridSpacing" path="/remarks"/>
    public int VanishingPointRays { get; set; } = Lightbox.Core.Documents.Guide.DefaultRays;

    /// <summary>How many heads tall a new character height scale is.</summary>
    /// <inheritdoc cref="GridSpacing" path="/remarks"/>
    public int HeightScaleHeads { get; set; } = 6;

    /// <summary>
    /// How much of the canvas height a new character height scale stands in.
    /// </summary>
    /// <remarks>
    /// A fraction rather than a head height in pixels, because the head height
    /// that reads as a figure on a 1080-high scene is the wrong one on a 4K
    /// scene — and the thing an artist means by "that is my default character"
    /// is the proportion, not the pixel count. Applied when the scale is
    /// placed and never afterwards: once it is on the canvas its size is
    /// document data, and a preference must not reach back into it.
    /// </remarks>
    public double HeightScaleFill { get; set; } = 0.7;

    /// <summary>
    /// Whether the brush belongs to the tool or to the drawing —
    /// <c>"Global"</c>, <c>"PerDocument"</c>, or null to let the project type
    /// decide.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Null by default and null is the interesting value: it means "follow the
    /// project", which is the answer that is right more often than either
    /// fixed choice. A comic page and a storyboard want opposite things, and
    /// the same person does both.
    /// </para>
    /// <para>
    /// A string rather than the enum so an unrecognised value in a settings
    /// file written by a newer build falls back to the default instead of
    /// refusing to load — the same reason the canvas quality is one.
    /// </para>
    /// </remarks>
    public string? BrushMemory { get; set; }

    /// <summary>The chosen scope, or null to follow the project type.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public BrushScope? BrushScopeChoice =>
        Enum.TryParse<BrushScope>(BrushMemory, out var scope) ? scope : null;

    /// <summary>How much detail the canvas composites while you work.</summary>
    /// <remarks>
    /// Persisted, which it was not before: somebody who turned it down because
    /// their machine needed it had to turn it down again every launch.
    /// </remarks>
    public string CanvasQuality { get; set; } = "Display";

    /// <summary>
    /// How much detail the canvas composites while an animation is running,
    /// or null to use <see cref="CanvasQuality"/> for playback too.
    /// </summary>
    /// <remarks>
    /// Split from <see cref="CanvasQuality"/> because the two moments want
    /// opposite trades: drawing rewards sharpness on a still image, playback
    /// rewards frames per second on a moving one. Null is the default and the
    /// interesting value — "same as while drawing" — so somebody who never
    /// thinks about it keeps one quality everywhere, and lowering the drawing
    /// quality on a slow machine lowers playback with it. A string for
    /// <see cref="BrushMemory"/>'s reason: an unrecognised value falls back
    /// to the default instead of refusing to load.
    /// </remarks>
    public string? PlaybackQuality { get; set; }

    /// <summary>
    /// Whether a human picked the canvas quality.
    /// </summary>
    /// <remarks>
    /// The one thing that lets the app react to a software-rendering machine
    /// without ever overruling a person. Until this is set, the stored value
    /// is a default the app is free to revise; after it, it is a decision.
    /// </remarks>
    public bool CanvasQualityChosen { get; set; }

    /// <summary>
    /// Composite layers on the GPU instead of the CPU (B125, experimental).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Off by default, and it is a measurement instrument rather than a
    /// preference.</b> The GPU path uploads every layer every frame until its
    /// textures are resident, which on integrated graphics competes with the CPU
    /// for the same memory bus — so it may be *slower*, and the only way to find
    /// out on a given machine is to switch it on and write a render report.
    /// </para>
    /// <para>
    /// It became a setting because it needs to be switchable by the person doing
    /// the measuring, and an environment variable is a poor instrument for that.
    /// <c>LIGHTBOX_GPU_COMPOSITE=1</c> still forces it on, which is what a
    /// headless or scripted run needs.
    /// </para>
    /// </remarks>
    [System.Text.Json.Serialization.JsonPropertyName("GpuCompositing")]
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyGpuCompositing { get; set; }

    /// <summary>
    /// Where this install composites: on the card, on the processor, or
    /// whichever the machine turns out to be faster at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Auto by default, which is the change: the path is measured rather than
    /// avoided.</b> It shipped off-by-default as a measurement instrument, on the
    /// argument that the GPU path uploads every layer every frame and may be
    /// slower on integrated graphics. That argument was right and it has been
    /// answered — the leak that made it dangerous is fixed (B179), and the same
    /// entry's capture says playback is better with it on: mean lateness 7.85 ms
    /// against 13.06, worst 96 ms against 1326. What remains true is that it
    /// cannot be right on <em>every</em> machine, and that is what Auto is for.
    /// </para>
    /// <para>
    /// <b>Auto is not "on".</b> <see cref="Rendering.GpuComposeProbe"/> blends the
    /// same passes both ways on the machine it is running on and takes the
    /// faster, which is the only thing that catches a software rasteriser
    /// reporting itself as a GPU. An explicit choice always wins over it.
    /// </para>
    /// <para>
    /// <c>LIGHTBOX_GPU_COMPOSITE=1</c> still forces the card on, whatever this
    /// says, which is what a headless or scripted run needs.
    /// </para>
    /// </remarks>
    [System.Text.Json.Serialization.JsonConverter(
        typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public Rendering.GpuComposeMode GpuCompositingMode { get; set; } = Rendering.GpuComposeMode.Auto;

    /// <summary>
    /// Record the pen's tilt and the hand's speed <em>even when the brush in
    /// hand reads neither</em>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is an override, not the switch.</b> Ordinarily the brush decides
    /// — a brush with tilt curves records tilt, one without records none — and
    /// that needs no preference at all, because response was always the brush's
    /// business (<c>PenAxisUse</c>). Recording axes nobody reads is measurably
    /// expensive: 113 bytes a point, 1.70× the saved document, on an
    /// application whose unit of work is two hundred drawings.
    /// </para>
    /// <para>
    /// <b>What it buys is the ability to change your mind.</b> Brush settings
    /// stay editable forever — a stroke carries its own snapshot and can be
    /// retuned months later — while the hand's motion happened once. Without
    /// this, adding a tilt curve to a stroke drawn with a plain brush does
    /// nothing at all, silently, because absent axes contribute the neutral
    /// value by design. On, the numbers are there when the artist wants them.
    /// </para>
    /// <para>
    /// <b>A preference rather than a document or project option</b>, for the
    /// reason workspaces are: whether a pen reports tilt at all, and whether
    /// its owner wants it in the record, is a property of the person and their
    /// hardware, not of the artwork. Opening somebody else's file must not
    /// start recording axes your tablet cannot supply, and moving a drawing
    /// between machines must not change what the next stroke stores.
    /// </para>
    /// <para>
    /// <b>Turning it off later never touches art already made.</b> Points that
    /// recorded tilt keep it and keep rendering with it — invariant 4, which
    /// exists so that changing a preference cannot repaint finished work. The
    /// setting decides what the *next* stroke captures and nothing else.
    /// </para>
    /// </remarks>
    public bool AlwaysRecordPenAxes { get; set; }

    /// <summary>
    /// Mark the X-sheet's frame numbers where the camera or the armature is
    /// keyed.
    /// </summary>
    /// <remarks>
    /// <b>Off, and the default is the decision.</b> The X-sheet is the exposure
    /// sheet — ink, and what is exposed when — which is exactly why camera moves
    /// and bone poses are not in it. A mark that appeared by default would put
    /// non-drawing tracks back into the grid by the back door, on every document
    /// including the ones that have neither. Somebody who wants to know that
    /// frame 9 carries a pose while they read the sheet can ask for it.
    /// </remarks>
    public bool MarkOffSheetKeys { get; set; }

    /// <summary>
    /// Whether the bone overlay follows the playhead while the animation
    /// plays, rather than being hidden.
    /// </summary>
    /// <remarks>
    /// Off by default, and B152 is the precedent: per-tick overlay work in this
    /// application has already turned out to cost several times what it looked
    /// like. Playback is for watching the animation; somebody checking that a
    /// rigged limb reads through a movement can ask for the rig back.
    /// </remarks>
    public bool AnimateRigDuringPlayback { get; set; }

    /// <summary>What a mark on a held cel does. See <c>HoldDrawing</c>.</summary>
    public string DrawingOnAHold { get; set; } = "StartANewDrawing";

    /// <summary>Whether playback wraps at the end of the range.</summary>
    public bool LoopPlayback { get; set; } = true;

    /// <summary>How wide one timeline frame cell is, in pixels.</summary>
    public double TimelineFrameWidth { get; set; } = 28;

    /// <summary>
    /// Exporting automatically when a document reaches a status.
    /// </summary>
    /// <remarks>
    /// Beside the autosave interval, which is the closest existing thing: a background
    /// action an artist switches on once and then stops thinking about. Off by default,
    /// because it writes files into somebody else's project.
    /// </remarks>
    public AutoExportSettings AutoExport { get; set; } = new();

    public static string Path { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Lightbox", "settings.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static AppSettings Deserialize(string json)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(json, Json) ?? new AppSettings();
            settings.MigrateOnionFalloff();
            settings.MigrateGpuCompositing();
            return settings;
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// Move a never-chosen onion falloff off the old default.
    /// </summary>
    /// <remarks>
    /// On load rather than on save, so it reaches an install that is never
    /// touched again, and only for a value still sitting on the old default —
    /// anything else was set on purpose and is left alone. See
    /// <see cref="OnionSettings.FalloffChosen"/> for what this costs the one
    /// artist who deliberately chose 0.5 before the flag existed.
    /// </remarks>
    private void MigrateOnionFalloff()
    {
        if (Onion.FalloffChosen) return;
        if (Math.Abs(Onion.Falloff - OnionSettings.LegacyFalloff) < 0.0001)
        {
            Onion.Falloff = new OnionSettings().Falloff;
        }
    }

    /// <summary>
    /// Carry the old GPU compositing checkbox onto the three-state mode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only <c>true</c> migrates, and that asymmetry is the point.</b> The old
    /// key was a bool that every settings file carried at <c>false</c> whether the
    /// artist had chosen the processor or had never opened the page — the two are
    /// indistinguishable in the file. Reading <c>false</c> as a decision would
    /// leave every existing install pinned to the processor for good, which is the
    /// exact outcome defaulting to Auto exists to prevent. <c>true</c> is
    /// unambiguous: nothing wrote it but a person ticking the box, so it becomes
    /// <see cref="Rendering.GpuComposeMode.On"/>.
    /// </para>
    /// <para>
    /// The old key is cleared as it is read, so the next save drops it from the
    /// file rather than leaving two keys that can disagree.
    /// <c>ASettingsFileWritesNoLegacyGpuKey</c> is the guard.
    /// </para>
    /// </remarks>
    private void MigrateGpuCompositing()
    {
        if (LegacyGpuCompositing == true && GpuCompositingMode == Rendering.GpuComposeMode.Auto)
        {
            GpuCompositingMode = Rendering.GpuComposeMode.On;
        }
        LegacyGpuCompositing = null;
    }

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(Path) ? Deserialize(File.ReadAllText(Path)) : new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// Write the settings. Failures are swallowed: a preference that will not
    /// persist is an annoyance, and must never be the reason anything else
    /// fails.
    /// </summary>
    public void Save()
    {
        try
        {
            Lightbox.Core.Serialization.DocJson.WriteAtomic(Path, Serialize());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The autosave interval as a timespan, or null when it is off.</summary>
    public TimeSpan? AutosaveInterval =>
        AutosaveMinutes <= 0 ? null : TimeSpan.FromMinutes(Math.Clamp(AutosaveMinutes, 0.25, 60));
}
