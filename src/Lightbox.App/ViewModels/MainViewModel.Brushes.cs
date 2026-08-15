using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Ai;
using Lightbox.App.Input;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;
using Lightbox.Core.Timeline;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>Part of MainViewModel — see MainViewModel.cs.</summary>
/// <remarks>
/// Split out of <c>MainViewModel.cs</c> under Q78, which was 13,628 lines across 61
/// sections. Every field this file uses is either declared here — meaning no other
/// section touches it — or in the shared-state block at the top of
/// <c>MainViewModel.cs</c>. See <c>docs/DESIGN-mainviewmodel-decomposition.md</c>.
/// </remarks>
public partial class MainViewModel
{
    // ---- brush cursor ----------------------------------------------------------

    private double _cursorPressure = 1;

    /// <summary>
    /// Track live pen pressure in the brush ring. On means the ring shrinks
    /// and grows with the stroke it is about to lay down; off pins it to the
    /// brush's maximum, which some artists prefer as a stable target.
    /// </summary>
    [ObservableProperty]
    private bool _cursorTracksPressure = true;

    partial void OnCursorTracksPressureChanged(bool value) => OnPropertyChanged(nameof(BrushCursorDiameter));

    /// <summary>
    /// Diameter the ring should draw, in document pixels. Taken from
    /// <see cref="BrushEngine.RadiusAt"/> — the same call the engine makes for
    /// each dab — so the ring cannot drift away from the stroke. A mouse
    /// reports pressure 1, so it always shows the true full thickness.
    /// </summary>
    public double BrushCursorDiameter
    {
        get
        {
            var pressure = CursorTracksPressure ? _cursorPressure : 1;
            return Math.Max(1, BrushEngine.RadiusAt(CurrentToolSettings, pressure) * 2);
        }
    }

    /// <summary>
    /// The tip the ring should outline, or null for the round dab.
    /// </summary>
    /// <remarks>
    /// <b>B74.</b> Read from the same <see cref="CurrentToolSettings"/> the engine
    /// stamps from, and passed as an id so the canvas traces the actual tip bitmap
    /// rather than being told a shape. Null on a brush with no tip, which is the
    /// honest answer and not a shrug — the round dab really is an ellipse.
    /// </remarks>
    public string? BrushCursorTipId => CurrentToolSettings.TipId;

    /// <summary>How flat the ring should be: 1 round, less an ellipse.</summary>
    /// <remarks>
    /// The brush's nominal roundness, deliberately <b>not</b>
    /// <c>BrushEngine.RoundnessAt</c>. That one folds in a per-dab jitter seeded
    /// from the dab's position, so a cursor built on it would wobble as the pointer
    /// moved and report the jitter rather than the brush — the same reason
    /// invariant 2 exists, seen from the UI side.
    /// </remarks>
    public double BrushCursorRoundness => Math.Clamp(CurrentToolSettings.Roundness, 0.05, 1);

    /// <summary>
    /// The ring's angle in degrees, so a chisel is previewed at the angle it will
    /// print at.
    /// </summary>
    /// <remarks>
    /// <c>TipRotationDeg</c> only — the base rotation
    /// <see cref="BrushEngine.StampDab"/> starts from. <c>AngleFollowsDirection</c>
    /// adds the stroke's heading and a hovering pointer has no heading, so folding
    /// it in here would mean inventing one; <c>RotationJitter</c> is seeded from the
    /// dab's position and belongs to the mark rather than to the brush.
    /// </remarks>
    public double BrushCursorAngle => CurrentToolSettings.TipRotationDeg;

    /// <summary>Canvas reports what the pen is doing; the ring follows it.</summary>
    public void SetCursorPressure(double pressure, bool penDown)
    {
        // Hovering shows the maximum: a pen off the tablet has no pressure to
        // report, and a ring that collapsed to nothing on hover would be
        // useless for aiming.
        var next = penDown ? Math.Clamp(pressure, 0, 1) : 1;
        if (Math.Abs(next - _cursorPressure) < 0.0001) return;
        _cursorPressure = next;
        OnPropertyChanged(nameof(BrushCursorDiameter));
    }

    /// <summary>Fired with a fresh snapshot whenever the canvas must repaint.</summary>
    public event Action<RenderSnapshot>? SnapshotChanged;

    /// <summary>
    /// The real constructor. Builds the artist through the same path the
    /// Configure window uses, so there is one way a provider becomes an
    /// artist rather than a startup way and a settings way.
    /// </summary>
    public MainViewModel() : this(artist: null)
    {
        ReloadAiProvider();
    }

    /// <summary>Test seam: inject a fake artist (or null for "no API key").</summary>
    public MainViewModel(IAiArtist? artist)
    {
        _ai = new ConfiguredArtist(artist);
        _referenceImages = new ReferenceViewImages(_cache);
        // Nothing is open. The application no longer invents a document to sit
        // behind the start screen, because Cancel then adopted a canvas nobody
        // chose — the artist got a 960×540 they never asked for, and only
        // noticed once they had drawn on it.
        //
        // `_editor` still points at something, and that is a deliberate choice
        // rather than an oversight. It is read from 86 places here and `Scene`
        // from 192 more; making it genuinely null would put a guard on every one
        // of them, in the second-riskiest file in the repository, to express a
        // state the UI can describe with one boolean. So the placeholder exists,
        // is never in `Tabs`, is never saved, and is never shown —
        // `HasDocument` is what everything else asks.
        _editor = new DocumentEditor(StartupDoc());
        _activeLayerIndex = FirstPaintableLayer(_editor.Doc);
        _editor.Changed += OnDocumentChanged;
        // The live rig: a frame with bound strokes renders posed for the
        // timeline position asking for it. Reads `_editor` at call time, so
        // switching tabs switches the armature with everything else. A
        // document with no rig takes the null branch inside and pays nothing.
        _cache.PoseResolver = (frame, cel) =>
            Skinning.PoseFrameForRender(_editor.Doc, frame, cel, _cache.Rig);
        // B147: the canvas holds its own copy of the selected outlines, and only
        // OnStrokeSelectionChanged refreshes it. Every path that reaches the
        // manager directly — picking a guide, a symbol or a reference box, all of
        // which call ClearAllSelections — used to leave that copy behind, still
        // drawn. Subscribing here makes the manager the single source: whatever
        // changes the selection, the outlines follow.
        _selectionManager.SelectionChanged += OnStrokeSelectionChanged;
        _clock.Tick += OnPlaybackTick;
        Settings = AppSettings.Load();
        _snapTolerance = Settings.SnapTolerance;
        // Mirror it where the render thread can see it (B125): the draw op has no
        // route to the view model, and an environment variable still forces it on
        // for headless runs.
        Rendering.GpuComposite.SettingEnabled = Settings.GpuCompositing;
        if (Enum.TryParse<CanvasQuality>(Settings.CanvasQuality, out var storedQuality))
        {
            _canvasQuality = storedQuality;
        }
        _autosave = new AutosaveService(
            () => SaveTargetTab?.Doc ?? Doc,
            Settings.AutosaveInterval,
            () => SaveTargetTab?.FilePath)
        {
            InPlace = Settings.AutosaveInPlace,
        };
        LoadTimingPresets();
        ColorPicker = new ColorPickerViewModel();
        ColorPicker.SetHex(ColorHex);
        ColorPicker.HexCommitted += hex => ColorHex = hex;
        // Picking from the palette is not the same as landing on that colour
        // with the wheel: the stroke records the swatch, so a later recolour
        // reaches the art. Without this the palette inside a picker would be
        // a convenient list of colours and nothing more.
        ColorPicker.SwatchPicked += PaintWithSwatch;
        // The pair adopts rather than duplicates. Adding a colour that is
        // already in the palette is a request to paint with a live colour, and
        // the swatch that is already there does that — making a second one
        // would give you the literal you were trying to get away from.
        ColorPicker.AddIntent = PaletteAddIntent.Adopt;
        // The other half of the pair gets a picker of its own rather than a
        // hex field. Reaching for the background colour is the same act as
        // reaching for the foreground one, and offering a wheel for one and a
        // text box for the other is the kind of asymmetry you notice every
        // single time.
        BackgroundPicker = new ColorPickerViewModel { AddIntent = PaletteAddIntent.Adopt };
        BackgroundPicker.SetHex(BackgroundColorHex);
        BackgroundPicker.HexCommitted += hex =>
        {
            _backgroundSwatchId = null;
            BackgroundColorHex = hex;
        };
        BackgroundPicker.SwatchPicked += id =>
        {
            _backgroundSwatchId = id;
            if (PaletteRegistry.ResolveSwatch(id) is { } swatch) BackgroundColorHex = swatch.Color;
        };
        PaletteDocker = new PaletteDockerViewModel(
            OnSwatchRecoloured, PerformPaletteEdit, PaintWithSwatch, () => ColorHex);
        PaletteDocker.SwatchEditRunEnded += CommitSwatchEdit;
        // The docker shows both scopes once a project is open. Set through
        // properties rather than the constructor because the project docker is
        // built below this one, and because a docker with no project should
        // need no ceremony to say so.
        PaletteDocker.ProjectSource = () => ProjectDocker?.Project;
        PaletteDocker.ProjectEdited = OnProjectChanged;
        GradientDocker = new GradientDockerViewModel(OnGradientEdited, PerformGradientEdit);
        ProjectDocker = new ProjectViewModel(NewAnimationDoc, OpenProjectDocument, OnProjectChanged);
        ProjectDocker.OpenSheet = OpenProjectSheet;
        // HasProject is a forwarding property, so it has no notification of its
        // own. Without this relay the project panel stays hidden after New or
        // Open project: the docker's own callback only fires when the docker
        // edits the project, and adopting one is not an edit.
        InitialiseSymbolBrowser();
        ProjectDocker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not nameof(ProjectViewModel.HasProject)) return;
            OnPropertyChanged(nameof(HasProject));
            // The browser shows the project's symbols, so adopting a project is
            // what fills it — and closing one is what empties it.
            SymbolBrowser.Refresh();
        };
        // The forwarding properties above have no backing field to notify from,
        // so the workspace's notifications are relayed under the names the
        // View menu and the tests already bind to.
        Workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null) return;
            OnPropertyChanged(e.PropertyName);
            if (e.PropertyName == nameof(WorkspaceViewModel.TimelineVisible))
            {
                OnPropertyChanged(nameof(ShowTimeline));
            }
        };
        // Bringing the Channels tab forward — opening the panel, or clicking
        // its tab — is the moment its thumbnails are first wanted; the
        // ordinary refresh only runs on document edits. RefreshChannelThumbs
        // gates itself on the tab actually showing, so this costs nothing for
        // every other layout change.
        Workspace.Changed += RefreshChannelThumbs;
        // Through RegisterResources rather than resetting the registry
        // directly, so the pickers' palette source is wired from the first
        // moment too — otherwise every flyout opens with no palette until
        // something else happens to change the document.
        RegisterResources();
        PaletteDocker.Load(Doc);
        GradientDocker.Load(Doc);
        LoadBrushState();
        SyncLayerChoices();
        SyncLayerRows();
        RefreshThumbnails();
        RefreshDocumentStats();
        // Start on the palette's black rather than on a literal. The first
        // stroke of a session is as worth being recolourable as any other,
        // and it is the one an artist is least likely to go back and re-link.
        ResetColors();
    }

    // ---- brush tool state -----------------------------------------------------
    // Two working configurations (brush + eraser); the bound properties edit
    // whichever tool is active. Everything persists to brushes.json so B
    // always returns to the brush exactly as last configured.

    /// <summary>Test seam: redirect brush persistence away from the real settings dir.</summary>
    internal static string? BrushStorePath { get; set; }


    private BrushSettings CurrentToolSettings => IsEraser ? _brushes.Eraser : _brushes.Brush;

    /// <summary>
    /// The settings the tool bar is editing. A test seam: the curve properties
    /// are read back through <see cref="PressureResponse"/>, and asserting on
    /// the record is how a test says "the brush now says this" rather than
    /// "the view model agrees with itself".
    /// </summary>
    internal BrushSettings CurrentToolSettingsForTest => CurrentToolSettings;

    public ObservableCollection<BrushPreset> BrushPresetChoices { get; } = [];

    [ObservableProperty]
    private BrushPreset? _selectedBrushPreset;

    /// <summary>
    /// Put a preset on, whether or not it is already the one selected.
    /// </summary>
    /// <remarks>
    /// Assigning <see cref="SelectedBrushPreset"/> the value it already holds
    /// raises nothing, so picking the brush you are already on used to do
    /// nothing — and after the modified indicator existed, that became the
    /// obvious gesture for "give me this brush back". This is the path the
    /// picker uses.
    /// </remarks>
    public void ApplyPreset(BrushPreset preset)
    {
        if (SelectedBrushPreset?.Id == preset.Id)
        {
            OnSelectedBrushPresetChanged(preset);
            return;
        }
        SelectedBrushPreset = preset;
    }

    partial void OnSelectedBrushPresetChanged(BrushPreset? value)
    {
        if (value is null || _brushes.IsApplying) return;
        _brushes.Applying(() =>
        {
            IsEraser = value.Tool == ToolKind.Eraser;
            // Both of these are settings in Configure rather than parts of a
            // brush, so a preset never overrides them. Sample source was missing
            // from this list and picking any preset silently reset it to
            // "this layer" — which made a window that says the choice applies to
            // the next mark tell the truth only until you changed brush.
            var antiAlias = AntiAliasing;
            var sampleSource = SmudgeSampleSource;
            _brushes.Brush = value.Settings.Clone();
            _brushes.Brush.AntiAlias = antiAlias;
            _brushes.Brush.SampleSource = sampleSource;
            _brushes.Eraser.SampleSource = sampleSource;
            EnsurePresetTip(value);
            NotifyBrushProperties();
        });
        PersistBrushState();
    }

    /// <summary>
    /// Global anti-aliasing for everything that paints (brush, eraser, fill).
    /// The value is stamped into each stroke at paint time, so existing art
    /// re-renders bit-identically no matter how the toggle changes later.
    /// </summary>
    public bool AntiAliasing
    {
        get => _brushes.Brush.AntiAlias;
        set
        {
            if (_brushes.Brush.AntiAlias == value) return;
            _brushes.Brush.AntiAlias = value;
            _brushes.Eraser.AntiAlias = value;
            OnPropertyChanged();
            if (!_brushes.IsApplying) PersistBrushState();
        }
    }

    public IReadOnlyList<SampleSource> SampleSourceChoices { get; } = Enum.GetValues<SampleSource>();

    /// <summary>
    /// What a smudge or blur reads: its own layer, or everything under it.
    /// </summary>
    /// <remarks>
    /// Set here and stamped into each stroke at paint time, exactly like
    /// <see cref="AntiAliasing"/> — so changing it never alters a mark already
    /// made. That is invariant 4, and it is the reason a preference like this
    /// can live in a settings window at all: the window sets what the next
    /// stroke will be, not what every past stroke becomes.
    /// </remarks>
    public SampleSource SmudgeSampleSource
    {
        get => _brushes.Brush.SampleSource;
        set
        {
            if (_brushes.Brush.SampleSource == value) return;
            _brushes.Brush.SampleSource = value;
            _brushes.Eraser.SampleSource = value;
            OnPropertyChanged();
            if (!_brushes.IsApplying) PersistBrushState();
        }
    }

    /// <summary>A preset's custom tip must live in the document so it re-renders standalone.</summary>
    private void EnsurePresetTip(BrushPreset preset)
    {
        if (preset.TipPng is null || preset.Settings.TipId is null) return;
        var doc = (SaveTargetTab?.Doc ?? Doc);
        if (doc.BrushTips.TryAdd(preset.Settings.TipId, preset.TipPng))
        {
            BrushTipRegistry.Register(doc.BrushTips);
            MarkDocumentEdited();
        }
    }

    private double GetBrush(Func<BrushSettings, double> get) => get(CurrentToolSettings);

    /// <summary>For settings that are not doubles — enums, flags, counts.</summary>
    private T GetBrushValue<T>(Func<BrushSettings, T> get) => get(CurrentToolSettings);

    private void SetBrush(Action<BrushSettings> set, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        set(CurrentToolSettings);
        OnPropertyChanged(name);
        // The on-canvas ring is computed from the settings, not stored, so it
        // has to be told. Size is the obvious one, but the minimum diameter
        // and the pressure-size curve move it too, which is why this is
        // unconditional rather than a special case on BrushSize.
        OnPropertyChanged(nameof(BrushCursorDiameter));
        // Any change to the working brush can be the one that makes it differ
        // from the preset it came from, so the dot is recomputed here rather
        // than at a handful of places somebody has to remember.
        NotifyPresetProperties();
        if (!_brushes.IsApplying) PersistBrushState();
    }

    public double BrushSize
    {
        get => GetBrush(s => s.Size);
        set => SetBrush(s => s.Size = Math.Clamp(value, 1, 500));
    }

    public double BrushHardness
    {
        get => GetBrush(s => s.Hardness);
        set => SetBrush(s => s.Hardness = Math.Clamp(value, 0, 1));
    }

    public double BrushOpacity
    {
        get => GetBrush(s => s.Opacity);
        set => SetBrush(s => s.Opacity = Math.Clamp(value, 0.01, 1));
    }

    public double BrushFlow
    {
        get => GetBrush(s => s.Flow);
        set => SetBrush(s => s.Flow = Math.Clamp(value, 0.01, 1));
    }

    public double BrushSpacing
    {
        get => GetBrush(s => s.Spacing);
        set => SetBrush(s => s.Spacing = Math.Clamp(value, 0.02, 2));
    }

    public double BrushWetEdge
    {
        get => GetBrush(s => s.WetEdge);
        set => SetBrush(s => s.WetEdge = Math.Clamp(value, 0, 1));
    }

    public double BrushGranulation
    {
        get => GetBrush(s => s.Granulation);
        set => SetBrush(s => s.Granulation = Math.Clamp(value, 0, 1));
    }

    public double BrushScatter
    {
        get => GetBrush(s => s.Scatter);
        set => SetBrush(s => s.Scatter = Math.Clamp(value, 0, 1));
    }

    // ---- brush dynamics (Photoshop's grouping) ---------------------------------

    public double BrushSizeJitter
    {
        get => GetBrush(s => s.SizeJitter);
        set => SetBrush(s => s.SizeJitter = Math.Clamp(value, 0, 1));
    }

    public double BrushMinimumDiameter
    {
        get => GetBrush(s => s.MinimumDiameter);
        set => SetBrush(s => s.MinimumDiameter = Math.Clamp(value, 0, 1));
    }

    public double BrushRoundness
    {
        get => GetBrush(s => s.Roundness);
        set => SetBrush(s => s.Roundness = Math.Clamp(value, 0.05, 1));
    }

    public double BrushRoundnessJitter
    {
        get => GetBrush(s => s.RoundnessJitter);
        set => SetBrush(s => s.RoundnessJitter = Math.Clamp(value, 0, 1));
    }

    public bool BrushAngleFollowsDirection
    {
        get => GetBrushValue(s => s.AngleFollowsDirection);
        set => SetBrush(s => s.AngleFollowsDirection = value);
    }

    public double BrushFlowJitter
    {
        get => GetBrush(s => s.FlowJitter);
        set => SetBrush(s => s.FlowJitter = Math.Clamp(value, 0, 1));
    }

    public IReadOnlyList<string> TextureSurfaceChoices { get; } =
        ["None", .. Enum.GetNames<PaperKind>()];

    public string BrushTextureSurface
    {
        get => GetBrushValue(s => s.TextureSurface?.ToString() ?? "None");
        set => SetBrush(s => s.TextureSurface =
            Enum.TryParse<PaperKind>(value, out var kind) ? kind : null);
    }

    public double BrushTextureScale
    {
        get => GetBrush(s => s.TextureScale);
        set => SetBrush(s => s.TextureScale = Math.Clamp(value, 1, 128));
    }

    public double BrushTextureDepth
    {
        get => GetBrush(s => s.TextureDepth);
        set => SetBrush(s => s.TextureDepth = Math.Clamp(value, 0, 1));
    }

    /// <summary>Empty means colour dynamics drift only in hue/saturation/value.</summary>
    public string BrushSecondaryColor
    {
        get => GetBrushValue(s => s.SecondaryColor ?? "");
        set => SetBrush(s => s.SecondaryColor = string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public double BrushColorJitter
    {
        get => GetBrush(s => s.ColorJitter);
        set => SetBrush(s => s.ColorJitter = Math.Clamp(value, 0, 1));
    }

    public double BrushHueJitter
    {
        get => GetBrush(s => s.HueJitter);
        set => SetBrush(s => s.HueJitter = Math.Clamp(value, 0, 1));
    }

    public double BrushSaturationJitter
    {
        get => GetBrush(s => s.SaturationJitter);
        set => SetBrush(s => s.SaturationJitter = Math.Clamp(value, 0, 1));
    }

    public double BrushBrightnessJitter
    {
        get => GetBrush(s => s.BrightnessJitter);
        set => SetBrush(s => s.BrightnessJitter = Math.Clamp(value, 0, 1));
    }

    // ---- smudge ----------------------------------------------------------------

    public IReadOnlyList<SmudgeMode> SmudgeModeChoices { get; } = Enum.GetValues<SmudgeMode>();

    /// <summary>Only meaningful for a smudge brush; the page hides otherwise.</summary>
    public bool IsSmudgeBrush => GetBrushValue(s => s.Kind) == BrushKind.Smudge;

    /// <summary>
    /// A brush that reworks pixels already down rather than adding new ones.
    /// </summary>
    /// <remarks>
    /// The tool options bar swaps to this group's controls when one is
    /// selected, because the generic brush row is mostly wrong for them: a
    /// smudge has no opacity in the usual sense, and what you actually reach
    /// for is strength and how wide an area each dab samples.
    /// </remarks>
    public bool IsEffectBrush =>
        GetBrushValue(s => s.Kind) is BrushKind.Smudge or BrushKind.Blur;

    /// <summary>
    /// Whether the effect row belongs in the tool options bar right now.
    /// </summary>
    /// <remarks>
    /// The brush kind and the active tool are different questions, and the bar
    /// was only asking the first. Pick the smudge brush, then switch to the
    /// selection tool, and Strength and Radius were still sitting there —
    /// controls for a brush you are not currently holding, taking room from
    /// the ones you are.
    /// </remarks>
    public bool ShowsEffectOptions => IsBrushTool && IsEffectBrush;

    /// <summary>
    /// How hard the effect bites: <see cref="BrushSettings.Flow"/>, for every
    /// effect brush.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B90.</b> This used to read and write <see cref="BrushSmudgeLength"/>
    /// for a smudge and label itself "Length", which put a *drag distance* under
    /// the artist's strength affordance and left the knob that actually decides
    /// how hard each dab pulls — flow, the <c>strength</c> argument
    /// <c>BrushEngine.StampSmudge</c> hands to <c>LerpDab</c> — unreachable from
    /// the bar entirely. `docs/manual/04-brushes.md` already described the
    /// behaviour this now has, so the app was the half that was wrong.
    /// </para>
    /// <para>
    /// Smudge length is not lost and is not diminished: it keeps its own labelled
    /// row on the ⚙ → Effects page beside smearing/dulling and radius, which is
    /// where a value you set once per brush belongs. The bar carries the three
    /// you reach for mid-stroke.
    /// </para>
    /// </remarks>
    public double EffectStrength
    {
        get => BrushFlow;
        set
        {
            BrushFlow = value;
            OnPropertyChanged();
        }
    }

    public SmudgeMode BrushSmudgeMode
    {
        get => GetBrushValue(s => s.SmudgeMode);
        set => SetBrush(s => s.SmudgeMode = value);
    }

    public double BrushSmudgeLength
    {
        get => GetBrush(s => s.SmudgeLength);
        set => SetBrush(s => s.SmudgeLength = Math.Clamp(value, 0, 1));
    }

    public double BrushSmudgeRadius
    {
        get => GetBrush(s => s.SmudgeRadius);
        set => SetBrush(s => s.SmudgeRadius = Math.Clamp(value, 0.05, 1));
    }

    public double BrushColorRate
    {
        get => GetBrush(s => s.ColorRate);
        set => SetBrush(s => s.ColorRate = Math.Clamp(value, 0, 1));
    }

    // ---- medium ---------------------------------------------------------------
    //
    // The physical simulation. Picking a medium decides which parts of the
    // model run; these numbers tune it. All of them live on the stroke, so a
    // change here never reaches back into paint already down.

    public IReadOnlyList<MediumKind> MediumKindChoices { get; } = Enum.GetValues<MediumKind>();

    public IReadOnlyList<PaperKind> PaperKindChoices { get; } = Enum.GetValues<PaperKind>();

    public MediumKind BrushMedium
    {
        get => GetBrushValue(s => s.Medium.Kind);
        set
        {
            SetBrush(s => s.Medium.Kind = value);
            OnPropertyChanged(nameof(MediumIsSimulated));
            OnPropertyChanged(nameof(MediumHasBody));
        }
    }

    /// <summary>Whether the fluid controls apply at all — everything is inert under None.</summary>
    public bool MediumIsSimulated => BrushMedium != MediumKind.None;

    /// <summary>Body, relief and bristle drag only mean something for paint that has thickness.</summary>
    public bool MediumHasBody => BrushMedium is MediumKind.Gouache or MediumKind.Oil;

    public double MediumWetness
    {
        get => GetBrush(s => s.Medium.Wetness);
        set => SetBrush(s => s.Medium.Wetness = Math.Clamp(value, 0, 1));
    }

    public double MediumViscosity
    {
        get => GetBrush(s => s.Medium.Viscosity);
        set => SetBrush(s => s.Medium.Viscosity = Math.Clamp(value, 0, 1));
    }

    public double MediumDrag
    {
        get => GetBrush(s => s.Medium.Drag);
        set => SetBrush(s => s.Medium.Drag = Math.Clamp(value, 0, 1));
    }

    /// <summary>
    /// Capped at 32. Cost is linear in this, and it is the one control here
    /// that can make a stroke commit feel slow, so the ceiling is deliberate.
    /// </summary>
    public int MediumFlowSteps
    {
        get => GetBrushValue(s => s.Medium.FlowSteps);
        set => SetBrush(s => s.Medium.FlowSteps = Math.Clamp(value, 0, 32));
    }

    public double MediumAbsorbency
    {
        get => GetBrush(s => s.Medium.Absorbency);
        set => SetBrush(s => s.Medium.Absorbency = Math.Clamp(value, 0, 1));
    }

    public double MediumEdgePull
    {
        get => GetBrush(s => s.Medium.EdgePull);
        set => SetBrush(s => s.Medium.EdgePull = Math.Clamp(value, 0, 1));
    }

    public double MediumPigmentDensity
    {
        get => GetBrush(s => s.Medium.PigmentDensity);
        set => SetBrush(s => s.Medium.PigmentDensity = Math.Clamp(value, 0, 1));
    }

    public double MediumGranularity
    {
        get => GetBrush(s => s.Medium.Granularity);
        set => SetBrush(s => s.Medium.Granularity = Math.Clamp(value, 0, 1));
    }

    public double MediumHiding
    {
        get => GetBrush(s => s.Medium.Hiding);
        set => SetBrush(s => s.Medium.Hiding = Math.Clamp(value, 0, 1));
    }

    public bool MediumPhysicalMixing
    {
        get => GetBrushValue(s => s.Medium.PhysicalMixing);
        set => SetBrush(s => s.Medium.PhysicalMixing = value);
    }

    public PaperKind MediumPaper
    {
        get => GetBrushValue(s => s.Medium.Paper);
        set => SetBrush(s => s.Medium.Paper = value);
    }

    public double MediumPaperScale
    {
        get => GetBrush(s => s.Medium.PaperScale);
        set => SetBrush(s => s.Medium.PaperScale = Math.Clamp(value, 1, 128));
    }

    public double MediumPaperInfluence
    {
        get => GetBrush(s => s.Medium.PaperInfluence);
        set => SetBrush(s => s.Medium.PaperInfluence = Math.Clamp(value, 0, 1));
    }

    public double MediumBody
    {
        get => GetBrush(s => s.Medium.Body);
        set => SetBrush(s => s.Medium.Body = Math.Clamp(value, 0, 1));
    }

    public double MediumRelief
    {
        get => GetBrush(s => s.Medium.Relief);
        set => SetBrush(s => s.Medium.Relief = Math.Clamp(value, 0, 1));
    }

    // No MediumBristleDrag or MediumPickup, and their absence is the fix rather than an
    // oversight. Both settings exist on the record and the engine reads neither — they
    // are the directional advection loop that DESIGN-fluid-media.md sequences last —
    // so exposing them gave an artist two sliders that moved and changed nothing.
    // Charter O7, and B23. `MediumSettings` says the same thing at the declarations.

    public double MediumPaintLoad
    {
        get => GetBrush(s => s.Medium.PaintLoad);
        set => SetBrush(s => s.Medium.PaintLoad = Math.Clamp(value, 0, 1));
    }

    public double MediumPressureWater
    {
        get => GetBrush(s => s.Medium.PressureWater);
        set => SetBrush(s => s.Medium.PressureWater = Math.Clamp(value, 0, 1));
    }

    public double MediumPressureMix
    {
        get => GetBrush(s => s.Medium.PressureMix);
        set => SetBrush(s => s.Medium.PressureMix = Math.Clamp(value, 0, 1));
    }

    public double MediumRewetting
    {
        get => GetBrush(s => s.Medium.Rewetting);
        set => SetBrush(s => s.Medium.Rewetting = Math.Clamp(value, 0, 1));
    }

    public double BrushRotationJitter
    {
        get => GetBrush(s => s.RotationJitter);
        set => SetBrush(s => s.RotationJitter = Math.Clamp(value, 0, 1));
    }

    // ---- pen pressure (per brush, per setting — Krita-style) --------------------

    /// <summary>Master pen-pressure switch of the current brush.</summary>
    public bool BrushPressureEnabled
    {
        get => GetBrush(s => s.PressureEnabled ? 1.0 : 0.0) > 0;
        set => SetBrush(s => s.PressureEnabled = value);
    }

    public double BrushPressureSizeGamma
    {
        get => GetBrush(s => s.PressureSizeGamma);
        set => SetBrush(s => s.PressureSizeGamma = Math.Clamp(value, 0, 4));
    }

    public double BrushPressureFlowGamma
    {
        get => GetBrush(s => s.PressureFlowGamma);
        set => SetBrush(s => s.PressureFlowGamma = Math.Clamp(value, 0, 4));
    }

    public double BrushPressureHardnessGamma
    {
        get => GetBrush(s => s.PressureHardnessGamma);
        set => SetBrush(s => s.PressureHardnessGamma = Math.Clamp(value, 0, 4));
    }

    // Per-setting on/off view of the curves (checkbox semantics: off = curve 0).

    public bool BrushPressureAffectsSize
    {
        get => BrushPressureSizeGamma > 0;
        set
        {
            BrushPressureSizeGamma = value ? 1 : 0;
            NotifyBrushProperties();
        }
    }

    public bool BrushPressureAffectsFlow
    {
        get => BrushPressureFlowGamma > 0;
        set
        {
            BrushPressureFlowGamma = value ? 1 : 0;
            NotifyBrushProperties();
        }
    }

    public bool BrushPressureAffectsHardness
    {
        get => BrushPressureHardnessGamma > 0;
        set
        {
            BrushPressureHardnessGamma = value ? 1 : 0;
            NotifyBrushProperties();
        }
    }

    // ---- pressure curves -------------------------------------------------------
    //
    // The editor edits a shape; the record keeps either a curve or a gamma. So
    // these three do the whole of the translation between the two, and nothing
    // else in the app has to know that a brush can be written either way.

    /// <summary>
    /// The shape to draw for a dynamic: the artist's curve, or the one its
    /// gamma describes. Never null, so the editor always has something to show
    /// — and for a brush made before curves existed, what it shows is that
    /// brush's real response rather than a straight line that would flatten it
    /// the moment anybody touched it.
    /// </summary>
    public ResponseCurve BrushCurve(BrushDynamic target) =>
        PressureResponse.Shape(CurrentToolSettings, target);

    /// <summary>Whether anything at all drives a dynamic — what the checkbox reads.</summary>
    public bool BrushDrives(BrushDynamic target) =>
        PressureResponse.IsDriven(CurrentToolSettings, target);

    /// <summary>
    /// Store an artist-drawn curve for a dynamic, replacing whatever drove it.
    /// </summary>
    public void SetBrushCurve(BrushDynamic target, ResponseCurve curve)
    {
        SetBrush(s =>
        {
            s.Curves ??= [];
            s.Curves[target] = curve.Clone();
        }, nameof(BrushCurve));
        NotifyBrushProperties();
    }

    /// <summary>
    /// Turn a dynamic on or off. On means linear unless something already
    /// drives it; off clears the curve and the gamma together, because leaving
    /// one behind would make the checkbox lie about what the brush does.
    /// </summary>
    public void SetBrushDrives(BrushDynamic target, bool on)
    {
        if (on && BrushDrives(target)) return;

        SetBrush(s =>
        {
            s.Curves?.Remove(target);
            switch (target)
            {
                case BrushDynamic.Size: s.PressureSizeGamma = on ? 1 : 0; break;
                case BrushDynamic.Flow: s.PressureFlowGamma = on ? 1 : 0; break;
                case BrushDynamic.Hardness: s.PressureHardnessGamma = on ? 1 : 0; break;
                default:
                    if (on) (s.Curves ??= [])[target] = ResponseCurve.Linear();
                    break;
            }
            if (s.Curves is { Count: 0 }) s.Curves = null;
        }, nameof(BrushCurve));
        NotifyBrushProperties();
    }

    /// <summary>Put a dynamic back to the plain <c>p^1</c> line.</summary>
    public void ResetBrushCurve(BrushDynamic target)
    {
        SetBrushDrives(target, false);
        SetBrushDrives(target, true);
    }

    /// <summary>How the brush's strokes land on the layer.</summary>
    public LayerBlendMode BrushBlend
    {
        get => GetBrushValue(s => s.BlendOrNormal);
        // Normal is stored as absent, so a document that never leaves the
        // default never grows the key. See BrushSettings.Blend.
        set => SetBrush(s => s.Blend = value == LayerBlendMode.Normal ? null : value);
    }

    // The brush picker offers the same list the layer docker does — see
    // BlendModeChoices further down. One list, because a brush's Multiply and
    // a layer's Multiply are the same operation and offering different sets
    // would imply they were not.

    // ---- imported paper texture --------------------------------------------------

    /// <summary>The imported texture this brush bites into, or null for a built-in paper.</summary>
    public string? BrushTextureId => GetBrushValue(s => s.TextureId);

    /// <summary>True when an imported paper is in charge, so the surface list can stand down.</summary>
    public bool HasImportedTexture => BrushTextureId is not null;

    /// <summary>
    /// Take an image into the document as a paper texture and point the brush
    /// at it.
    /// </summary>
    /// <remarks>
    /// Stored in the document rather than referenced on disk, the same as a
    /// brush tip: a file pointing at somebody's scans folder renders
    /// differently on the next machine, which is invariant 1 broken somewhere
    /// nobody looks. Returns the id so a caller can name it back.
    /// </remarks>
    public string ImportBrushTexture(string png)
    {
        var doc = SaveTargetTab?.Doc ?? Doc;
        var id = Ids.NewId("tex");
        (doc.Textures ??= [])[id] = png;
        TextureRegistry.Register(doc.Textures);
        MarkDocumentEdited();

        SetBrush(s =>
        {
            s.TextureId = id;
            // A texture nobody can see is a texture that looks broken. The
            // depth slider starts at zero, so importing one has to open it.
            if (s.TextureDepth <= 0) s.TextureDepth = 0.5;
        }, nameof(BrushTextureId));
        NotifyBrushProperties();
        return id;
    }

    /// <summary>Go back to the built-in papers.</summary>
    public void ClearBrushTexture()
    {
        // The document keeps the pixels: strokes already painted with it still
        // reference the id, the same rule the tip picker follows.
        SetBrush(s => s.TextureId = null, nameof(BrushTextureId));
        NotifyBrushProperties();
    }

    // ---- brush tip -------------------------------------------------------------

    /// <summary>The tip the current brush stamps, or null for a plain round dab.</summary>
    public string? BrushTipId => GetBrushValue(s => s.TipId);

    /// <summary>
    /// Every tip that can be chosen right now: the project's, the artist's own,
    /// and the eight built-ins.
    /// </summary>
    public IReadOnlyList<BrushTip> AvailableTips() =>
        // Q30: which document the tips are for, so a scoped project can narrow
        // the picker. Null with no project slot, which is the unscoped answer.
        TipStore.Available(ProjectDocker.Project, inView: ActiveTab?.Source);

    /// <summary>
    /// Point the brush at a library tip, or at nothing for a round dab.
    /// </summary>
    /// <remarks>
    /// The raster is copied into the document under the same id on the way
    /// past. That is invariant 1 for tips: from here on the drawing renders
    /// whether or not the library still has it, and deleting the library copy
    /// cannot reach back and change a picture.
    /// </remarks>
    public void SetBrushTip(BrushTip? tip)
    {
        if (tip is not null)
        {
            var doc = SaveTargetTab?.Doc ?? Doc;
            TipStore.AdoptInto(doc, tip);
            BrushTipRegistry.Register(doc.BrushTips);
        }

        SetBrush(s => s.TipId = tip?.Id, nameof(BrushTipId));
        NotifyBrushProperties();
    }

    private static readonly string[] BrushPropertyNames =
    [
        nameof(BrushSize), nameof(BrushHardness), nameof(BrushOpacity), nameof(BrushFlow),
        nameof(BrushSpacing), nameof(BrushWetEdge), nameof(BrushGranulation), nameof(BrushScatter),
        nameof(BrushRotationJitter), nameof(BrushPressureEnabled),
        nameof(BrushPressureSizeGamma), nameof(BrushPressureFlowGamma), nameof(BrushPressureHardnessGamma),
        nameof(BrushPressureAffectsSize), nameof(BrushPressureAffectsFlow), nameof(BrushPressureAffectsHardness),
        nameof(BrushBlend), nameof(BrushTextureId), nameof(HasImportedTexture),
        // B74. The ring's shape comes from the brush, so it has to be re-read
        // whenever the brush changes — same list, same reason as its diameter.
        nameof(BrushCursorDiameter), nameof(BrushCursorTipId),
        nameof(BrushCursorRoundness), nameof(BrushCursorAngle),
        nameof(BrushSizeJitter), nameof(BrushMinimumDiameter), nameof(BrushRoundness),
        nameof(BrushRoundnessJitter), nameof(BrushAngleFollowsDirection), nameof(BrushFlowJitter),
        nameof(BrushTextureSurface), nameof(BrushTextureScale), nameof(BrushTextureDepth),
        nameof(BrushSecondaryColor), nameof(BrushColorJitter), nameof(BrushHueJitter),
        nameof(BrushSaturationJitter), nameof(BrushBrightnessJitter),
        nameof(IsSmudgeBrush), nameof(IsEffectBrush), nameof(ShowsEffectOptions), nameof(EffectStrength),
        nameof(BrushSmudgeMode), nameof(BrushSmudgeLength),
        nameof(BrushSmudgeRadius), nameof(BrushColorRate),
        nameof(BrushMedium), nameof(MediumIsSimulated), nameof(MediumHasBody),
        nameof(MediumWetness), nameof(MediumViscosity), nameof(MediumDrag), nameof(MediumFlowSteps),
        nameof(MediumAbsorbency), nameof(MediumEdgePull),
        nameof(MediumPigmentDensity), nameof(MediumGranularity), nameof(MediumHiding),
        nameof(MediumPhysicalMixing),
        nameof(MediumPaper), nameof(MediumPaperScale), nameof(MediumPaperInfluence),
        nameof(MediumBody), nameof(MediumRelief), nameof(MediumPaintLoad),
        nameof(MediumPressureWater), nameof(MediumPressureMix), nameof(MediumRewetting),
    ];

    private void NotifyBrushProperties()
    {
        foreach (var name in BrushPropertyNames) OnPropertyChanged(name);
        NotifyPresetProperties();
        // Switching brush or preset can change which stabilisation is in
        // effect, and the sliders show whichever it is.
        NotifySmoothingProperties();
    }

    /// <summary>Save the working brush as a reusable preset.</summary>
    public BrushPreset SaveCurrentAsPreset(string name, IEnumerable<string>? tags = null)
    {
        var preset = new BrushPreset
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"Preset {BrushPresetChoices.Count + 1}" : name.Trim(),
            Tool = IsEraser ? ToolKind.Eraser : ToolKind.Brush,
            Settings = CurrentToolSettings.Clone(),
            Tags = CleanTags(tags),
        };
        _brushes.UserPresets.Add(preset);
        BrushPresetChoices.Add(preset);
        _brushes.Applying(() =>
        {
            SelectedBrushPreset = preset;
        });
        RefreshTagChoices();
        PersistBrushState();
        return preset;
    }
}
