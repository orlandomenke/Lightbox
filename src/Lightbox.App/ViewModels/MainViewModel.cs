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
using Lightbox.Core.Serialization;
using Lightbox.Core.Timeline;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

public sealed partial class FrameCell(int index) : ObservableObject
{
    public int Index { get; } = index;

    /// <summary>Index of the owning layer in Scene.Layers (0 = bottom).</summary>
    public int LayerIndex { get; set; }

    public string Display => (Index + 1).ToString();

    [ObservableProperty]
    private bool _isKeyed;

    [ObservableProperty]
    private bool _isCurrent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBreakdown))]
    [NotifyPropertyChangedFor(nameof(IsInbetween))]
    private FrameRole _role = FrameRole.Key;

    public bool IsBreakdown => Role == FrameRole.Breakdown;

    public bool IsInbetween => Role == FrameRole.Inbetween;

    /// <summary>Beyond the timeline's last frame — insertable, but not playable yet.</summary>
    [ObservableProperty]
    private bool _isVirtual;

    /// <summary>Outside the selected playback range (greyed out).</summary>
    [ObservableProperty]
    private bool _outOfRange;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _thumb;

    /// <summary>Frame id the current thumb was rendered from (staleness check).</summary>
    public string? ThumbFrameId { get; set; }
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly FrameBitmapCache _cache = new();
    private readonly StrokeBuilder _strokeBuilder = new();
    private readonly PlaybackClock _clock = new();

    private DocumentEditor _editor;
    private SKSurface? _composeSurface;
    private SKImageInfo _composeInfo;

    /// <summary>Fired with a fresh snapshot whenever the canvas must repaint.</summary>
    public event Action<RenderSnapshot>? SnapshotChanged;

    public MainViewModel() : this(ResolveArtist())
    {
    }

    /// <summary>Test seam: inject a fake artist (or null for "no API key").</summary>
    public MainViewModel(IAiArtist? artist)
    {
        _artist = artist;
        var first = new DocumentTab(new DocumentEditor(DocumentFactory.CreateDoc()), "Untitled-1") { IsActive = true };
        Tabs.Add(first);
        _activeTab = first;
        _editor = first.Editor;
        _editor.Changed += OnDocumentChanged;
        _clock.Tick += OnPlaybackTick;
        _autosave = new AutosaveService(() => SaveTargetTab?.Doc ?? Doc);
        ColorPicker = new ColorPickerViewModel();
        ColorPicker.SetHex(ColorHex);
        ColorPicker.HexCommitted += hex => ColorHex = hex;
        LoadBrushState();
        SyncLayerChoices();
        SyncLayerRows();
    }

    // ---- document tabs --------------------------------------------------------

    public ObservableCollection<DocumentTab> Tabs { get; } = [];

    [ObservableProperty]
    private DocumentTab? _activeTab;

    private bool _switchingTabs;
    private int _untitledCounter = 1;

    partial void OnActiveTabChanged(DocumentTab? value)
    {
        if (value is null) return;
        foreach (var tab in Tabs) tab.IsActive = tab == value;
        OnPropertyChanged(nameof(ShowTimeline));
        OnPropertyChanged(nameof(ReferenceSheetsView));
        if (value.Editor == _editor) return;

        _switchingTabs = true;
        if (Tabs.FirstOrDefault(t => t.Editor == _editor) is { } leaving)
        {
            leaving.SavedFrameIndex = CurrentFrameIndex;
            leaving.SavedLayerIndex = ActiveLayerIndex;
        }
        AttachEditor(value.Editor);
        ActiveLayerIndex = Math.Clamp(value.SavedLayerIndex, 0, Scene.Layers.Count - 1);
        CurrentFrameIndex = Math.Clamp(value.SavedFrameIndex, 0, Math.Max(0, Scene.FrameCount - 1));
        _switchingTabs = false;
    }

    /// <summary>The animation tab a save/AI call should target (a reference tab defers to its owner).</summary>
    public DocumentTab? SaveTargetTab =>
        ActiveTab?.Kind == DocumentTabKind.Reference ? ActiveTab.Owner ?? ActiveTab : ActiveTab;

    /// <summary>Timeline is hidden on reference tabs regardless of the View-menu toggle.</summary>
    public bool ShowTimeline => TimelineVisible && ActiveTab?.Kind != DocumentTabKind.Reference;

    partial void OnTimelineVisibleChanged(bool value) => OnPropertyChanged(nameof(ShowTimeline));

    [RelayCommand]
    private void ActivateTab(DocumentTab tab) => ActiveTab = tab;

    private void AttachEditor(DocumentEditor editor)
    {
        _clock.Stop();
        IsPlaying = false;
        _strokeBuilder.Cancel();
        _editor.Changed -= OnDocumentChanged;
        _editor = editor;
        _editor.Changed += OnDocumentChanged;
        _cache.Clear();
        _allThumbsDirty = true;
        ClearPlaybackRange();
        OnDocumentChanged();
    }

    /// <summary>Create a document from the File → New dialog in a new tab.</summary>
    public void NewDocument(NewDocumentSettings settings)
    {
        var doc = DocumentFactory.CreateDoc(settings.Width, settings.Height, settings.Fps);
        doc.Scene.Name = settings.Name;
        doc.Scene.Ppi = settings.Ppi;
        doc.Scene.BackgroundColor = settings.BackgroundColor;
        doc.Scene.TransparentBackground = settings.TransparentBackground;
        AddTab(new DocumentTab(new DocumentEditor(doc), settings.Name));
    }

    /// <summary>Open a loaded document in a new tab.</summary>
    public void OpenDocumentTab(Doc doc, string? filePath)
    {
        var title = filePath is null ? NextUntitledName() : TitleFromPath(filePath);
        AddTab(new DocumentTab(new DocumentEditor(doc), title) { FilePath = filePath });
    }

    /// <summary>Close a tab. The view confirms unsaved changes before calling this.</summary>
    public void CloseTab(DocumentTab tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0) return;
        Tabs.Remove(tab);
        // An animation tab takes its reference-view tabs with it.
        foreach (var orphan in Tabs.Where(t => t.Owner == tab).ToList()) Tabs.Remove(orphan);
        if (Tabs.Count == 0)
        {
            Tabs.Add(new DocumentTab(new DocumentEditor(DocumentFactory.CreateDoc()), NextUntitledName()));
        }
        if (ActiveTab == tab || ActiveTab is null || !Tabs.Contains(ActiveTab))
        {
            ActiveTab = Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
        }
    }

    /// <summary>The active document was written to disk: adopt the name, clear the dirty dot.</summary>
    public void NotifySaved(string filePath)
    {
        if (SaveTargetTab is not { } tab) return;
        tab.FilePath = filePath;
        tab.Title = TitleFromPath(filePath);
        tab.IsDirty = false;
    }

    private void AddTab(DocumentTab tab)
    {
        Tabs.Add(tab);
        ActiveTab = tab;
    }

    private string NextUntitledName() => $"Untitled-{++_untitledCounter}";

    private static string TitleFromPath(string path)
    {
        var name = Path.GetFileName(path);
        const string suffix = ".lightbox.json";
        return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? name[..^suffix.Length]
            : Path.GetFileNameWithoutExtension(path);
    }

    private void MarkDocumentEdited()
    {
        _autosave.MarkDirty();
        if (_switchingTabs || ActiveTab is not { } tab) return;
        if (tab.Kind == DocumentTabKind.Reference)
        {
            // Undo/redo replaces the wrapper doc's layer list; keep the owning
            // document's view pointed at whatever the editor currently holds.
            if (tab.View is { } view) view.Layers = Doc.Scene.Layers;
            if (tab.Owner is { } owner) owner.IsDirty = true;
        }
        else
        {
            tab.IsDirty = true;
        }
    }

    // ---- character sheets -----------------------------------------------------

    /// <summary>Sheets of the active (or owning) document — fresh list so the docker re-reads.</summary>
    public IReadOnlyList<ReferenceSheet> ReferenceSheetsView =>
        (SaveTargetTab?.Doc ?? Doc).ReferenceSheets.ToList();

    public void AddReferenceSheet()
    {
        var target = SaveTargetTab ?? Tabs[0];
        target.Doc.ReferenceSheets.Add(new ReferenceSheet
        {
            Name = $"Character {target.Doc.ReferenceSheets.Count + 1}",
        });
        target.IsDirty = true;
        OnPropertyChanged(nameof(ReferenceSheetsView));
    }

    public void AddReferenceView(ReferenceSheet sheet)
    {
        var target = SaveTargetTab ?? Tabs[0];
        var view = ReferenceView.Create($"view {sheet.Views.Count + 1}", Scene.Width, Scene.Height);
        sheet.Views.Add(view);
        target.IsDirty = true;
        OnPropertyChanged(nameof(ReferenceSheetsView));
        OpenReferenceView(view);
    }

    /// <summary>A sheet or view was renamed in the docker.</summary>
    public void MarkReferenceEdited()
    {
        if (SaveTargetTab is { } tab) tab.IsDirty = true;
        _autosave.MarkDirty();
        OnPropertyChanged(nameof(ReferenceSheetsView));
    }

    /// <summary>Open (or focus) the tab editing a character-sheet view.</summary>
    public void OpenReferenceView(ReferenceView view)
    {
        if (Tabs.FirstOrDefault(t => t.View == view) is { } open)
        {
            ActiveTab = open;
            return;
        }
        var owner = SaveTargetTab ?? Tabs[0];
        var sheet = owner.Doc.ReferenceSheets.FirstOrDefault(s => s.Views.Contains(view));
        // The wrapper scene SHARES the view's layer list: edits land in the
        // owning document directly.
        var wrapper = new Doc
        {
            Scene = new Scene
            {
                Name = view.Name,
                Width = view.Width,
                Height = view.Height,
                FrameCount = 1,
                Layers = view.Layers,
            },
        };
        AddTab(new DocumentTab(new DocumentEditor(wrapper), $"{sheet?.Name ?? "Sheet"} / {view.Name}")
        {
            Kind = DocumentTabKind.Reference,
            Owner = owner,
            View = view,
        });
    }

    /// <summary>Flatten one character-sheet view to PNG (for AI reference and MCP).</summary>
    public string RenderReferenceViewPng(ReferenceView view)
    {
        var passes = new List<RenderPass>();
        foreach (var layer in view.Layers)
        {
            if (!layer.Visible) continue;
            var frame = ExposureSheet.ExposedFrame(layer, 0);
            if (frame is null) continue;
            passes.Add(new RenderPass(_cache.Get(frame, view.Width, view.Height), null, layer.Opacity));
        }
        using var image = SceneRenderer.Compose(view.Width, view.Height, passes);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG encode failed.");
        return Convert.ToBase64String(data.AsSpan());
    }

    /// <summary>Up to two rendered character-sheet views to ride along with AI requests.</summary>
    private IReadOnlyList<string>? CollectReferenceImages()
    {
        var views = (SaveTargetTab?.Doc ?? Doc).ReferenceSheets
            .SelectMany(s => s.Views)
            .Where(v => v.Layers.Any(l => l.Visible))
            .Take(2)
            .Select(RenderReferenceViewPng)
            .ToList();
        return views.Count > 0 ? views : null;
    }

    /// <summary>The color docker's state, kept in sync with <see cref="ColorHex"/>.</summary>
    public ColorPickerViewModel ColorPicker { get; }

    partial void OnColorHexChanged(string value) => ColorPicker.SetHex(value);

    private static IAiArtist? ResolveArtist()
    {
        var key = ApiKeyProvider.GetApiKey();
        if (key is not null) return new AnthropicArtist(key);
        if (ApiKeyProvider.GetOllamaConfig() is { } ollama)
            return new OllamaArtist(ollama.Url, ollama.Model);
        return null;
    }

    private readonly AutosaveService _autosave;

    public Doc Doc => _editor.Doc;

    private Scene Scene => _editor.Doc.Scene;

    private Layer ActiveLayer => Scene.Layers[Math.Clamp(ActiveLayerIndex, 0, Scene.Layers.Count - 1)];

    // ---- observable state ---------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FrameLabel))]
    private int _currentFrameIndex;

    // ---- brush tool state -----------------------------------------------------
    // Two working configurations (brush + eraser); the bound properties edit
    // whichever tool is active. Everything persists to brushes.json so B
    // always returns to the brush exactly as last configured.

    /// <summary>Test seam: redirect brush persistence away from the real settings dir.</summary>
    internal static string? BrushStorePath { get; set; }

    private BrushSettings _brushWork = new();
    private BrushSettings _eraserWork = new() { Size = 14, Hardness = 0.9 };
    private readonly List<BrushPreset> _userPresets = [];
    private bool _applyingPreset;

    private BrushSettings CurrentToolSettings => IsEraser ? _eraserWork : _brushWork;

    public ObservableCollection<BrushPreset> BrushPresetChoices { get; } = [];

    [ObservableProperty]
    private BrushPreset? _selectedBrushPreset;

    partial void OnSelectedBrushPresetChanged(BrushPreset? value)
    {
        if (value is null || _applyingPreset) return;
        _applyingPreset = true;
        IsEraser = value.Tool == ToolKind.Eraser;
        _brushWork = value.Settings.Clone();
        EnsurePresetTip(value);
        NotifyBrushProperties();
        _applyingPreset = false;
        PersistBrushState();
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

    private void SetBrush(Action<BrushSettings> set, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        set(CurrentToolSettings);
        OnPropertyChanged(name);
        if (!_applyingPreset) PersistBrushState();
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

    public double BrushRotationJitter
    {
        get => GetBrush(s => s.RotationJitter);
        set => SetBrush(s => s.RotationJitter = Math.Clamp(value, 0, 1));
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

    private static readonly string[] BrushPropertyNames =
    [
        nameof(BrushSize), nameof(BrushHardness), nameof(BrushOpacity), nameof(BrushFlow),
        nameof(BrushSpacing), nameof(BrushWetEdge), nameof(BrushGranulation), nameof(BrushScatter),
        nameof(BrushRotationJitter), nameof(BrushPressureSizeGamma), nameof(BrushPressureFlowGamma),
    ];

    private void NotifyBrushProperties()
    {
        foreach (var name in BrushPropertyNames) OnPropertyChanged(name);
    }

    /// <summary>Save the working brush as a reusable preset.</summary>
    public BrushPreset SaveCurrentAsPreset(string name)
    {
        var preset = new BrushPreset
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"Preset {BrushPresetChoices.Count + 1}" : name.Trim(),
            Tool = IsEraser ? ToolKind.Eraser : ToolKind.Brush,
            Settings = CurrentToolSettings.Clone(),
        };
        _userPresets.Add(preset);
        BrushPresetChoices.Add(preset);
        _applyingPreset = true;
        SelectedBrushPreset = preset;
        _applyingPreset = false;
        PersistBrushState();
        return preset;
    }

    /// <summary>Add imported presets (from .abr/.gbr/.gih/.kpp) and persist them.</summary>
    public int AddImportedPresets(IEnumerable<BrushPreset> presets)
    {
        var added = 0;
        foreach (var preset in presets)
        {
            _userPresets.Add(preset);
            BrushPresetChoices.Add(preset);
            added++;
        }
        if (added > 0) PersistBrushState();
        return added;
    }

    /// <summary>
    /// Import brush files (.abr/.gbr/.gih/.kpp) into presets. Unsupported or
    /// broken files are skipped and counted, never fatal.
    /// </summary>
    public (int Added, int Failed) ImportBrushFiles(IEnumerable<(string Name, byte[] Bytes)> files)
    {
        var presets = new List<BrushPreset>();
        var failed = 0;
        foreach (var (name, bytes) in files)
        {
            try
            {
                foreach (var imported in Lightbox.Import.BrushImport.Read(name, bytes))
                {
                    presets.Add(new BrushPreset
                    {
                        Name = imported.Name,
                        Tool = ToolKind.Brush,
                        TipPng = imported.TipPngBase64,
                        Settings = new BrushSettings
                        {
                            Size = Math.Clamp(imported.SizePx, 1, 500),
                            Spacing = imported.Spacing,
                            Opacity = imported.Opacity,
                            Flow = imported.Flow,
                            Hardness = imported.TipPngBase64 is null ? 0.8 : 1,
                            TipId = imported.TipPngBase64 is null ? null : Ids.NewId("tip"),
                        },
                    });
                }
            }
            catch
            {
                failed++;
            }
        }
        var added = AddImportedPresets(presets);
        AiStatus = failed == 0
            ? $"Imported {added} brush(es)."
            : $"Imported {added} brush(es); {failed} file(s) could not be read.";
        return (added, failed);
    }

    private void PersistBrushState()
    {
        PresetStore.Save(new PresetStore.State
        {
            UserPresets = _userPresets,
            LastBrushPresetId = SelectedBrushPreset?.Id,
            LastBrush = _brushWork.Clone(),
            LastEraser = _eraserWork.Clone(),
        }, BrushStorePath);
    }

    private void LoadBrushState()
    {
        foreach (var preset in BuiltInPresets.Create()) BrushPresetChoices.Add(preset);
        var state = PresetStore.Load(BrushStorePath);
        foreach (var preset in state.UserPresets)
        {
            _userPresets.Add(preset);
            BrushPresetChoices.Add(preset);
        }
        if (state.LastBrush is not null) _brushWork = state.LastBrush.Clone();
        else _brushWork = new BrushSettings { Size = 6, Hardness = 0.8 };
        if (state.LastEraser is not null) _eraserWork = state.LastEraser.Clone();
        // Restore the selection WITHOUT re-applying the preset (the working
        // settings above already carry the user's last tweaks on top of it).
        _applyingPreset = true;
        SelectedBrushPreset = BrushPresetChoices.FirstOrDefault(p => p.Id == state.LastBrushPresetId);
        _applyingPreset = false;
    }

    [ObservableProperty]
    private string _colorHex = "#1a1a1a";

    // ---- active tool ----------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEraser))]
    [NotifyPropertyChangedFor(nameof(IsBrushTool))]
    [NotifyPropertyChangedFor(nameof(IsEraserTool))]
    [NotifyPropertyChangedFor(nameof(IsFillTool))]
    [NotifyPropertyChangedFor(nameof(IsSelectTool))]
    [NotifyPropertyChangedFor(nameof(IsPaintTool))]
    private ToolId _activeTool = ToolId.Brush;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectVariantGlyph))]
    [NotifyPropertyChangedFor(nameof(IsFreehandVariant))]
    [NotifyPropertyChangedFor(nameof(IsPolygonVariant))]
    [NotifyPropertyChangedFor(nameof(IsBoxVariant))]
    [NotifyPropertyChangedFor(nameof(IsEllipseVariant))]
    private SelectVariant _activeSelectVariant = SelectVariant.Freehand;

    public bool IsFreehandVariant => ActiveSelectVariant == SelectVariant.Freehand;

    public bool IsPolygonVariant => ActiveSelectVariant == SelectVariant.Polygon;

    public bool IsBoxVariant => ActiveSelectVariant == SelectVariant.Box;

    public bool IsEllipseVariant => ActiveSelectVariant == SelectVariant.Ellipse;

    public bool IsBrushTool => ActiveTool == ToolId.Brush;

    public bool IsEraserTool => ActiveTool == ToolId.Eraser;

    public bool IsFillTool => ActiveTool == ToolId.Fill;

    public bool IsSelectTool => ActiveTool == ToolId.Select;

    /// <summary>Brush or eraser — the tools whose strokes the brush-parameter flyout edits.</summary>
    public bool IsPaintTool => ActiveTool is ToolId.Brush or ToolId.Eraser;

    public string SelectVariantGlyph => ActiveSelectVariant switch
    {
        SelectVariant.Polygon => "⬠",
        SelectVariant.Box => "▭",
        SelectVariant.Ellipse => "◯",
        _ => "◌",
    };

    /// <summary>Compat view of the tool (old XAML/tests): eraser vs everything else painting as brush.</summary>
    public bool IsEraser
    {
        get => ActiveTool == ToolId.Eraser;
        set => ActiveTool = value ? ToolId.Eraser : ToolId.Brush;
    }

    partial void OnActiveToolChanged(ToolId value)
    {
        // The bound sliders edit the active tool's brush configuration.
        NotifyBrushProperties();
        CancelPolygonInProgress();
    }

    [RelayCommand]
    private void SelectTool(ToolId tool)
    {
        if (tool == ToolId.Select && ActiveTool == ToolId.Select)
        {
            CycleSelectVariant();
            return;
        }
        ActiveTool = tool;
    }

    /// <summary>Pressing the selection shortcut repeatedly cycles its variants.</summary>
    public void CycleSelectVariant()
    {
        ActiveSelectVariant = ActiveSelectVariant switch
        {
            SelectVariant.Freehand => SelectVariant.Polygon,
            SelectVariant.Polygon => SelectVariant.Box,
            SelectVariant.Box => SelectVariant.Ellipse,
            _ => SelectVariant.Freehand,
        };
    }

    [RelayCommand]
    private void SelectVariantOf(SelectVariant variant)
    {
        ActiveSelectVariant = variant;
        ActiveTool = ToolId.Select;
    }

    // ---- fill tool -------------------------------------------------------------

    [ObservableProperty]
    private double _fillTolerance = 32;

    /// <summary>Openings up to this many pixels still count as closed ("connected").</summary>
    [ObservableProperty]
    private double _fillGapPx = 4;

    /// <summary>Overfill (+) or underfill (−) the region by pixels.</summary>
    [ObservableProperty]
    private double _fillGrowPx = 2;

    /// <summary>Sample every visible layer (fill what LOOKS empty) instead of only the active one.</summary>
    [ObservableProperty]
    private bool _smartFill = true;

    /// <summary>Insert the fill under the line work (tucks beneath the line); off = fill on top, preserving lines.</summary>
    [ObservableProperty]
    private bool _fillBelowLines = true;

    /// <summary>Fill tool click: flood at a document position, record a fill stroke.</summary>
    public void FillAt(double x, double y)
    {
        if (ActiveTool != ToolId.Fill || IsPlaying) return;
        if (PaintTarget() is not { } target) return;
        if (!ActiveLayer.Visible)
        {
            AiStatus = $"Layer “{ActiveLayer.Name}” is hidden — enable its visibility to fill on it.";
            return;
        }

        var scene = Scene;
        SKBitmap? owned = null;
        try
        {
            SKBitmap sample;
            if (SmartFill)
            {
                var passes = new List<RenderPass>();
                foreach (var layer in scene.Layers)
                {
                    if (!layer.Visible) continue;
                    var frame = ExposureSheet.ExposedFrame(layer, CurrentFrameIndex);
                    if (frame is null) continue;
                    passes.Add(new RenderPass(_cache.Get(frame, scene.Width, scene.Height), null, layer.Opacity));
                }
                using var image = SceneRenderer.Compose(scene.Width, scene.Height, passes, SkiaSharp.SKColors.Transparent);
                owned = SKBitmap.FromImage(image);
                sample = owned;
            }
            else
            {
                sample = _cache.Get(target, scene.Width, scene.Height);
            }

            var result = FloodFill.Fill(
                sample,
                (int)Math.Round(x),
                (int)Math.Round(y),
                new FloodFill.Options(FillTolerance, FillGapPx, FillGrowPx),
                SelectionMask(scene.Width, scene.Height));
            if (result is null)
            {
                AiStatus = "Nothing fillable at that spot.";
                return;
            }

            var stroke = new Stroke
            {
                Tool = ToolKind.Fill,
                Color = ColorHex,
                Brush = new BrushSettings { Opacity = 1 },
                Points = result.Outer,
                Holes = result.Holes.Count > 0 ? result.Holes : null,
                Label = "fill",
            };
            var clip = PrepareClipForSelection();
            if (clip is not null) stroke.ClipId = clip.Value.Id;
            var below = FillBelowLines;
            _editor.Perform(doc =>
            {
                if (clip is { } c) doc.ClipRegions.TryAdd(c.Id, c.Region);
                var list = StrokesOf(target);
                if (below) list.Insert(0, stroke);
                else list.Add(stroke);
            });
            _cache.Invalidate(target.Id);
            _dirtyThumbIds.Add(target.Id);
            PublishSnapshot();
            RefreshThumbnails();
        }
        finally
        {
            owned?.Dispose();
        }
    }

    // ---- selection --------------------------------------------------------------

    private List<List<StrokePoint>> _selectionContours = [];

    /// <summary>Current selection outlines (document space) for the canvas overlay.</summary>
    public IReadOnlyList<List<StrokePoint>> SelectionContours => _selectionContours;

    public bool HasSelection => _selectionContours.Count > 0;

    /// <summary>Raised when the selection outline (or polygon-in-progress) changes.</summary>
    public event Action? SelectionChanged;

    [ObservableProperty]
    private double _selectionFeather;

    /// <summary>Pixels for the Grow/Shrink selection buttons.</summary>
    [ObservableProperty]
    private double _selectionAdjustPx = 2;

    private readonly List<StrokePoint> _polygonPoints = [];

    public IReadOnlyList<StrokePoint> PolygonInProgress => _polygonPoints;

    private void NotifySelection()
    {
        OnPropertyChanged(nameof(HasSelection));
        SelectionChanged?.Invoke();
        PublishSnapshot();
    }

    /// <summary>Combine a closed shape into the selection (Shift adds, Alt subtracts).</summary>
    public void ApplySelectionShape(List<StrokePoint> contour, bool add, bool subtract)
    {
        if (contour.Count < 3) return;
        int w = Scene.Width, h = Scene.Height;
        var shape = MaskFromContours([contour], w, h);
        bool[] mask;
        if (!HasSelection || (!add && !subtract))
        {
            mask = subtract ? new bool[w * h] : shape;
        }
        else
        {
            mask = MaskFromContours(_selectionContours, w, h);
            for (var i = 0; i < mask.Length; i++)
            {
                if (add) mask[i] |= shape[i];
                else if (subtract) mask[i] &= !shape[i];
            }
        }
        SetSelectionFromMask(mask, w, h);
    }

    public void AddPolygonVertex(double x, double y)
    {
        _polygonPoints.Add(new StrokePoint(x, y, 1));
        SelectionChanged?.Invoke();
    }

    public void CompletePolygon(bool add, bool subtract)
    {
        var contour = new List<StrokePoint>(_polygonPoints);
        _polygonPoints.Clear();
        SelectionChanged?.Invoke();
        ApplySelectionShape(contour, add, subtract);
    }

    public void CancelPolygon() => CancelPolygonInProgress();

    private void CancelPolygonInProgress()
    {
        if (_polygonPoints.Count == 0) return;
        _polygonPoints.Clear();
        SelectionChanged?.Invoke();
    }

    [RelayCommand]
    private void SelectAll()
    {
        _selectionContours =
        [
            [new(0, 0, 1), new(Scene.Width, 0, 1), new(Scene.Width, Scene.Height, 1), new(0, Scene.Height, 1)],
        ];
        NotifySelection();
    }

    [RelayCommand]
    private void Deselect()
    {
        if (!HasSelection && _polygonPoints.Count == 0) return;
        _selectionContours = [];
        _polygonPoints.Clear();
        NotifySelection();
    }

    [RelayCommand]
    private void InvertSelection()
    {
        int w = Scene.Width, h = Scene.Height;
        var mask = HasSelection ? MaskFromContours(_selectionContours, w, h) : new bool[w * h];
        for (var i = 0; i < mask.Length; i++) mask[i] = !mask[i];
        SetSelectionFromMask(mask, w, h);
    }

    [RelayCommand]
    private void GrowSelection() => AdjustSelection(Math.Abs(SelectionAdjustPx));

    [RelayCommand]
    private void ShrinkSelection() => AdjustSelection(-Math.Abs(SelectionAdjustPx));

    private void AdjustSelection(double px)
    {
        if (!HasSelection || Math.Abs(px) < 0.5) return;
        int w = Scene.Width, h = Scene.Height;
        var mask = MaskFromContours(_selectionContours, w, h);
        var r = (int)Math.Round(Math.Abs(px));
        mask = px > 0 ? FloodFill.Dilate(mask, w, h, r) : FloodFill.Erode(mask, w, h, r);
        SetSelectionFromMask(mask, w, h);
    }

    private void SetSelectionFromMask(bool[] mask, int w, int h)
    {
        _selectionContours = FloodFill.TraceAllContours(mask, w, h);
        NotifySelection();
    }

    /// <summary>Rasterize contours (even-odd) to a boolean mask.</summary>
    private static bool[] MaskFromContours(IReadOnlyList<List<StrokePoint>> contours, int w, int h)
    {
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Could not create mask surface.");
        surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
        using (var path = BrushEngine.PathFromContours(contours))
        using (var paint = new SKPaint { Color = SkiaSharp.SKColors.White, IsAntialias = false })
        {
            surface.Canvas.DrawPath(path, paint);
        }
        using var image = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(image);
        var pixels = bmp.Pixels;
        var mask = new bool[w * h];
        for (var i = 0; i < mask.Length; i++) mask[i] = pixels[i].Alpha > 127;
        return mask;
    }

    /// <summary>The selection as a flood-fill constraint (null when nothing is selected).</summary>
    private bool[]? SelectionMask(int w, int h) =>
        HasSelection ? MaskFromContours(_selectionContours, w, h) : null;

    /// <summary>
    /// Freeze the active selection as a document clip region (content-hashed,
    /// deduped) so strokes painted under it re-render identically forever.
    /// </summary>
    private (string Id, ClipRegion Region)? PrepareClipForSelection()
    {
        if (!HasSelection) return null;
        var region = new ClipRegion
        {
            Contours = _selectionContours.Select(c => new List<StrokePoint>(c)).ToList(),
            Feather = SelectionFeather,
        };
        var payload = System.Text.Json.JsonSerializer.Serialize(region);
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload)))[..12];
        var id = $"clip_{hash.ToLowerInvariant()}";
        // The renderer resolves clips by id, so it must know this one before
        // the stroke's first re-render — not only after a document reload.
        ClipRegionRegistry.Register(id, region);
        return (id, region);
    }

    [ObservableProperty]
    private bool _onionSkin = true;

    [ObservableProperty]
    private int _onionDepth = 1;

    [ObservableProperty]
    private bool _smoothStrokes = true;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private int _activeLayerIndex;

    [ObservableProperty]
    private bool _sidebarVisible = true;

    /// <summary>Which side the docker sidebar collapses to / sits on.</summary>
    [ObservableProperty]
    private bool _sidebarOnRight = true;

    [ObservableProperty]
    private bool _timelineVisible = true;

    public ObservableCollection<Layer> LayerChoices { get; } = [];

    /// <summary>Kind used by the layer docker's "+" button.</summary>
    public sealed record LayerKindChoice(string Label, LayerKind Kind)
    {
        public override string ToString() => Label;
    }

    public IReadOnlyList<LayerKindChoice> NewLayerKindChoices { get; } =
        [new("Raster", LayerKind.Painted), new("Vector", LayerKind.Vector)];

    [ObservableProperty]
    private LayerKindChoice _newLayerKind = new("Raster", LayerKind.Painted);

    [ObservableProperty]
    private int _playbackSpeedPercent = 100;

    partial void OnPlaybackSpeedPercentChanged(int value)
    {
        var clamped = Math.Clamp(value, 10, 400);
        if (clamped != value)
        {
            PlaybackSpeedPercent = clamped;
            return;
        }
        if (IsPlaying)
        {
            _clock.Stop();
            _clock.Start(Scene.Fps, clamped);
        }
    }

    public int Fps
    {
        get => Scene.Fps;
        set
        {
            var fps = Math.Clamp(value, 1, 60);
            if (Scene.Fps == fps) return;
            Scene.Fps = fps;
            OnPropertyChanged();
            MarkDocumentEdited();
            if (IsPlaying)
            {
                _clock.Stop();
                _clock.Start(fps, PlaybackSpeedPercent);
            }
        }
    }

    partial void OnOnionDepthChanged(int value) => PublishSnapshot();

    partial void OnActiveLayerIndexChanged(int value)
    {
        foreach (var row in LayerRows) row.IsActive = row.SceneIndex == value;
        OnPropertyChanged(nameof(FrameCells));
        PublishSnapshot();
    }

    [ObservableProperty]
    private int _tweenCount = 3;

    [ObservableProperty]
    private Easing _tweenEasing = Easing.EaseInOut;

    public IReadOnlyList<Easing> EasingChoices { get; } =
        [Easing.Linear, Easing.EaseIn, Easing.EaseOut, Easing.EaseInOut];

    /// <summary>Layer rows for the layer docker and the timeline, topmost layer first.</summary>
    public ObservableCollection<LayerRow> LayerRows { get; } = [];

    /// <summary>The active layer's timeline cells (topmost-first rows carry the rest).</summary>
    public ObservableCollection<FrameCell> FrameCells =>
        LayerRows[LayerRows.Count - 1 - Math.Clamp(ActiveLayerIndex, 0, LayerRows.Count - 1)].Cells;

    /// <summary>Cells shown per row: the real frames plus empty tail cells to insert into.</summary>
    public int TimelineExtent => Scene.FrameCount + VirtualTail;

    private const int VirtualTail = 24;

    /// <summary>Last frame the ruler may scrub to.</summary>
    public int MaxScrubFrame => Scene.FrameCount - 1;

    public string FrameLabel => $"{CurrentFrameIndex + 1} / {Scene.FrameCount}";

    partial void OnCurrentFrameIndexChanged(int value)
    {
        RefreshCellHighlights();
        PublishSnapshot();
    }

    partial void OnOnionSkinChanged(bool value) => PublishSnapshot();

    // ---- painting -----------------------------------------------------------

    /// <summary>The keyed frame paint lands on (exposure-sheet: the key at or before the playhead).</summary>
    private Frame? PaintTarget()
    {
        var i = ExposureSheet.KeyIndexAtOrBefore(ActiveLayer, CurrentFrameIndex);
        return i < 0 ? null : ActiveLayer.Cels[i].Frame;
    }

    /// <summary>One pointer sample in document space.</summary>
    public readonly record struct PointerSample(double X, double Y, double Pressure);

    // Live-preview state: a persistent copy of the target frame that only the
    // NEW segment of the stroke gets stamped into per pointer event — this is
    // what keeps painting O(stroke length) instead of O(length²).
    private SKBitmap? _liveComposite;
    private int _liveStampedCount;
    private bool _snapshotQueued;

    public void BeginStroke(double x, double y, double pressure)
    {
        if (ActiveTool is not (ToolId.Brush or ToolId.Eraser)) return;
        if (IsPlaying || PaintTarget() is not { } target) return;
        if (!ActiveLayer.Visible)
        {
            AiStatus = $"Layer “{ActiveLayer.Name}” is hidden — enable its visibility to draw on it.";
            return;
        }
        _strokeBuilder.Begin(
            IsEraser ? ToolKind.Eraser : ToolKind.Brush,
            ColorHex,
            CurrentToolSettings.Clone(),
            x, y, pressure);

        _liveComposite?.Dispose();
        _liveComposite = _cache.Get(target, Scene.Width, Scene.Height).Copy();
        _liveStampedCount = 0;
        FlushLivePreview();
        PublishSnapshot();
    }

    /// <summary>All coalesced samples of one pointer event → one stamp + one (coalesced) repaint.</summary>
    public void MoveStrokeBatch(IReadOnlyList<PointerSample> samples)
    {
        if (!_strokeBuilder.IsActive) return;
        foreach (var s in samples) _strokeBuilder.Add(s.X, s.Y, s.Pressure);
        FlushLivePreview();
        RequestSnapshot();
    }

    public void MoveStroke(double x, double y, double pressure) =>
        MoveStrokeBatch([new PointerSample(x, y, pressure)]);

    /// <summary>
    /// Stamp only the not-yet-stamped tail of the live stroke into the
    /// preview bitmap. (With stroke opacity below 1 the batch joints would
    /// double-composite slightly — the preview accepts that; the committed
    /// frame is always re-rendered exactly from the record.)
    /// </summary>
    private void FlushLivePreview()
    {
        if (_liveComposite is null || _strokeBuilder.Current is not { } live) return;
        var points = live.Points;
        if (_liveStampedCount >= points.Count) return;

        var from = Math.Max(0, _liveStampedCount - 1); // overlap one point so segments connect
        var tail = new Stroke
        {
            Tool = live.Tool,
            Color = live.Color,
            Brush = live.Brush,
            Points = points.Skip(from).ToList(),
        };
        FrameRasterizer.Append(_liveComposite, tail);
        _liveStampedCount = points.Count;
    }

    /// <summary>
    /// Coalesce repaints: at most one queued snapshot at a time. Posted at
    /// Default priority — NEVER Render priority: jobs in the dispatcher's
    /// render phase swallow the InvalidateVisual they trigger, which leaves
    /// the canvas permanently un-scheduled (strokes only appeared after the
    /// next unrelated event — the "frozen cursor, no lines" bug).
    /// </summary>
    private void RequestSnapshot()
    {
        if (_snapshotQueued) return;
        _snapshotQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _snapshotQueued = false;
            PublishSnapshot();
        }, Avalonia.Threading.DispatcherPriority.Default);
    }

    public void EndStroke()
    {
        var stroke = _strokeBuilder.End();
        _liveComposite?.Dispose();
        _liveComposite = null;
        _liveStampedCount = 0;
        if (stroke is null) return;
        var target = PaintTarget();
        if (target is null) return;

        if (SmoothStrokes && stroke.Points.Count >= 3)
        {
            stroke.Points = GeometryOps.Smooth(stroke.Points, 1);
        }

        // A stroke painted under a selection carries it forever (provenance).
        var clip = PrepareClipForSelection();
        if (clip is not null) stroke.ClipId = clip.Value.Id;
        _editor.Perform(doc =>
        {
            if (clip is { } c) doc.ClipRegions.TryAdd(c.Id, c.Region);
            StrokesOf(target).Add(stroke);
        });
        _cache.Invalidate(target.Id);
        _dirtyThumbIds.Add(target.Id);
        PublishSnapshot();
        RefreshThumbnails();
    }

    // ---- commands -----------------------------------------------------------

    // ---- playback transport --------------------------------------------------

    private int _playDirection = 1;

    /// <summary>Playback start (index, -1 = unset → first frame).</summary>
    [ObservableProperty]
    private int _playbackStartFrame = -1;

    /// <summary>Playback end (index, -1 = unset → last frame).</summary>
    [ObservableProperty]
    private int _playbackEndFrame = -1;

    partial void OnPlaybackStartFrameChanged(int value) => RefreshRangeHighlights();

    partial void OnPlaybackEndFrameChanged(int value) => RefreshRangeHighlights();

    private int EffectiveStartFrame =>
        Math.Clamp(PlaybackStartFrame < 0 ? 0 : PlaybackStartFrame, 0, Math.Max(0, Scene.FrameCount - 1));

    private int EffectiveEndFrame =>
        Math.Clamp(PlaybackEndFrame < 0 ? Scene.FrameCount - 1 : PlaybackEndFrame, EffectiveStartFrame, Math.Max(0, Scene.FrameCount - 1));

    [RelayCommand]
    private void Play() => StartPlayback(1);

    [RelayCommand]
    private void PlayBackwards() => StartPlayback(-1);

    [RelayCommand]
    private void Pause()
    {
        if (!IsPlaying) return;
        _clock.Stop();
        IsPlaying = false;
        PublishSnapshot();
    }

    private void StartPlayback(int direction)
    {
        _playDirection = direction;
        if (IsPlaying) return;
        _strokeBuilder.Cancel();
        IsPlaying = true;
        _clock.Start(Scene.Fps, PlaybackSpeedPercent);
        PublishSnapshot();
    }

    [RelayCommand]
    private void TogglePlayback()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    [RelayCommand]
    private void GoToStartFrame() => CurrentFrameIndex = EffectiveStartFrame;

    [RelayCommand]
    private void GoToEndFrame() => CurrentFrameIndex = EffectiveEndFrame;

    [RelayCommand]
    private void PreviousKeyframe()
    {
        var layer = ActiveLayer;
        for (var i = Math.Min(CurrentFrameIndex, Scene.FrameCount) - 1; i >= 0; i--)
        {
            if (ExposureSheet.FrameAtExactIndex(layer, i) is not null)
            {
                CurrentFrameIndex = i;
                return;
            }
        }
    }

    [RelayCommand]
    private void NextKeyframe()
    {
        var layer = ActiveLayer;
        for (var i = CurrentFrameIndex + 1; i < Scene.FrameCount; i++)
        {
            if (ExposureSheet.FrameAtExactIndex(layer, i) is not null)
            {
                CurrentFrameIndex = i;
                return;
            }
        }
    }

    /// <summary>One playback tick: advance in the play direction, looping inside the selected range.</summary>
    public void StepPlayback()
    {
        var start = EffectiveStartFrame;
        var end = EffectiveEndFrame;
        var next = CurrentFrameIndex + _playDirection;
        if (next > end) next = start;
        else if (next < start) next = end;
        CurrentFrameIndex = Math.Clamp(next, 0, Math.Max(0, Scene.FrameCount - 1));
    }

    // ---- playback range + frame insertion (timeline context menu) -----------

    public void SetPlaybackStart(FrameCell cell) =>
        PlaybackStartFrame = Math.Min(cell.Index, Scene.FrameCount - 1);

    public void SetPlaybackEnd(FrameCell cell) =>
        PlaybackEndFrame = Math.Min(cell.Index, Scene.FrameCount - 1);

    public void ClearPlaybackRange()
    {
        PlaybackStartFrame = -1;
        PlaybackEndFrame = -1;
    }

    /// <summary>
    /// Insert a drawn frame with the given role at a timeline cell (possibly a
    /// virtual one beyond the current end — the timeline extends to reach it),
    /// or re-mark an existing frame's role.
    /// </summary>
    public void InsertFrameAt(FrameCell cell, FrameRole role)
    {
        if (cell.LayerIndex < 0 || cell.LayerIndex >= Scene.Layers.Count) return;
        _editor.SetKeyAt(Scene.Layers[cell.LayerIndex].Id, cell.Index, role);
        ActiveLayerIndex = cell.LayerIndex;
        CurrentFrameIndex = Math.Min(cell.Index, Scene.FrameCount - 1);
    }

    private void RefreshRangeHighlights()
    {
        var rangeSet = PlaybackStartFrame >= 0 || PlaybackEndFrame >= 0;
        var start = EffectiveStartFrame;
        var end = EffectiveEndFrame;
        foreach (var row in LayerRows)
        {
            foreach (var cell in row.Cells)
            {
                cell.OutOfRange = rangeSet && !cell.IsVirtual && (cell.Index < start || cell.Index > end);
            }
        }
    }

    [RelayCommand]
    private void AddFrame()
    {
        _editor.AddFrameAfter(CurrentFrameIndex);
        CurrentFrameIndex++;
    }

    [RelayCommand]
    private void DuplicateFrame()
    {
        _editor.DuplicateFrame(CurrentFrameIndex);
        CurrentFrameIndex++;
    }

    [RelayCommand]
    private void DeleteFrame()
    {
        if (Scene.FrameCount <= 1) return;
        _editor.DeleteFrame(CurrentFrameIndex);
        CurrentFrameIndex = Math.Min(CurrentFrameIndex, Scene.FrameCount - 1);
    }

    [RelayCommand]
    private void Undo()
    {
        _editor.Undo();
        _cache.Clear();
        _allThumbsDirty = true;
        ClampCurrentFrame();
        RefreshThumbnails();
    }

    [RelayCommand]
    private void Redo()
    {
        _editor.Redo();
        _cache.Clear();
        _allThumbsDirty = true;
        ClampCurrentFrame();
        RefreshThumbnails();
    }

    [RelayCommand]
    private void AddPaintedLayer() => AddLayer(LayerKind.Painted);

    [RelayCommand]
    private void AddVectorLayer() => AddLayer(LayerKind.Vector);

    /// <summary>The layer docker's "+" button: adds a layer of the dropdown's kind.</summary>
    [RelayCommand]
    private void AddLayerOfSelectedKind() => AddLayer(NewLayerKind.Kind);

    [RelayCommand]
    private void ToggleSidebar() => SidebarVisible = !SidebarVisible;

    [RelayCommand]
    private void SwitchSidebarSide() => SidebarOnRight = !SidebarOnRight;

    [RelayCommand]
    private void ToggleTimeline() => TimelineVisible = !TimelineVisible;

    [RelayCommand]
    private void ActivateLayer(LayerRow row) => ActiveLayerIndex = row.SceneIndex;

    /// <summary>Rename as one undoable step (called by the row on commit).</summary>
    internal void CommitLayerRename(LayerRow row, string name)
    {
        var layer = row.Layer;
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            // Snap the row back to the document name instead of storing a blank.
            row.SyncFromModel(layer, row.SceneIndex);
            return;
        }
        if (layer.Name == trimmed) return;
        _editor.Perform(_ => layer.Name = trimmed);
    }

    internal void SetLayerVisible(Layer layer, bool visible)
    {
        if (layer.Visible == visible) return;
        _editor.Perform(_ => layer.Visible = visible);
    }

    /// <summary>
    /// Per-layer onion-skin participation. A display preference, so it is
    /// persisted (autosave) but deliberately not an undo step.
    /// </summary>
    internal void SetLayerOnionEnabled(Layer layer, bool enabled)
    {
        if (layer.OnionEnabled == enabled) return;
        layer.OnionEnabled = enabled;
        MarkDocumentEdited();
        PublishSnapshot();
    }

    private void AddLayer(LayerKind kind)
    {
        _editor.Perform(doc =>
        {
            var layer = new Layer
            {
                Name = $"{(kind == LayerKind.Vector ? "Vector" : "Paint")} {doc.Scene.Layers.Count + 1}",
                Kind = kind,
                Cels = [new Cel { Frame = kind == LayerKind.Vector ? new VectorFrame() : new PaintedFrame() }],
            };
            while (layer.Cels.Count < doc.Scene.FrameCount) layer.Cels.Add(new Cel());
            doc.Scene.Layers.Add(layer);
        });
        ActiveLayerIndex = Scene.Layers.Count - 1;
    }

    [RelayCommand]
    private void ToggleActiveLayerVisible()
    {
        _editor.Perform(_ => ActiveLayer.Visible = !ActiveLayer.Visible);
    }

    /// <summary>Clicking a cel selects both the frame and the layer it belongs to.</summary>
    [RelayCommand]
    private void SelectFrame(FrameCell cell)
    {
        if (cell.IsVirtual) return; // no frame there yet — insert one via right-click
        if (cell.LayerIndex >= 0 && cell.LayerIndex < Scene.Layers.Count)
            ActiveLayerIndex = cell.LayerIndex;
        CurrentFrameIndex = cell.Index;
    }

    /// <summary>
    /// Deterministic inbetweens between the key at/before the playhead and
    /// the next key. Strokes are interpolated, then re-rendered by the same
    /// brush pipeline as hand-painted frames when the cels are displayed.
    /// </summary>
    [RelayCommand]
    private void InsertInbetweens()
    {
        var layer = ActiveLayer;
        var aIndex = ExposureSheet.KeyIndexAtOrBefore(layer, CurrentFrameIndex);
        if (aIndex < 0) return;
        var bIndex = ExposureSheet.NextKeyIndex(layer, aIndex);
        if (bIndex < 0) return;

        var a = StrokesOf(layer.Cels[aIndex].Frame!);
        var b = StrokesOf(layer.Cels[bIndex].Frame!);
        var series = Inbetweener.InbetweenSeries(a, b, TweenCount, TweenEasing);
        var frames = series.Select(strokes => NewFrameFor(layer, strokes, FrameRole.Inbetween)).ToList();

        _editor.InsertInbetweens(layer.Id, aIndex, frames);
        CurrentFrameIndex = Math.Min(aIndex + 1, Scene.FrameCount - 1);
    }

    private static List<Stroke> StrokesOf(Frame frame) => frame switch
    {
        PaintedFrame p => p.Strokes,
        VectorFrame v => v.Strokes,
        _ => [],
    };

    /// <summary>A new frame of the layer's own kind carrying the given strokes.</summary>
    private static Frame NewFrameFor(Layer layer, List<Stroke> strokes, FrameRole role = FrameRole.Key) => layer.Kind switch
    {
        LayerKind.Vector => new VectorFrame { Strokes = strokes, Role = role },
        _ => new PaintedFrame { Strokes = strokes, Role = role },
    };

    // ---- AI -----------------------------------------------------------------

    private readonly IAiArtist? _artist;
    private CancellationTokenSource? _aiCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseAi))]
    private bool _aiBusy;

    [ObservableProperty]
    private string _aiStatus = "";

    [ObservableProperty]
    private string _aiPrompt = "";

    public bool IsAiAvailable => _artist is not null;

    public bool CanUseAi => IsAiAvailable && !AiBusy;

    public string AiUnavailableHint => IsAiAvailable
        ? ""
        : "Enable AI features by setting ANTHROPIC_API_KEY (Claude API), or LIGHTBOX_OLLAMA_MODEL for a local model via Ollama, or the equivalent settings-file keys. No key at all? Use Claude Desktop with the Lightbox MCP server — see the README.";

    [RelayCommand]
    private void CancelAi() => _aiCts?.Cancel();

    /// <summary>
    /// Claude draws the inbetweens between the key at/before the playhead and
    /// the next key. Same insertion path as the deterministic engine — only
    /// the frame producer differs.
    /// </summary>
    [RelayCommand]
    private async Task AiInbetweenAsync()
    {
        if (_artist is null || AiBusy) return;
        var layer = ActiveLayer;
        var aIndex = ExposureSheet.KeyIndexAtOrBefore(layer, CurrentFrameIndex);
        if (aIndex < 0) return;
        var bIndex = ExposureSheet.NextKeyIndex(layer, aIndex);
        if (bIndex < 0)
        {
            AiStatus = "Needs a second keyframe after the current one.";
            return;
        }

        var ts = Enumerable.Range(1, TweenCount)
            .Select(k => (double)k / (TweenCount + 1))
            .ToList();
        // Send the effective drawings — erased strokes must not leak into
        // the model's input any more than into the deterministic tweens.
        var request = new InbetweenRequest(
            new SceneInfo(Scene.Width, Scene.Height, Scene.Fps),
            StrokeRecordCleaner.EffectiveStrokes(StrokesOf(layer.Cels[aIndex].Frame!)),
            StrokeRecordCleaner.EffectiveStrokes(StrokesOf(layer.Cels[bIndex].Frame!)),
            ts,
            TweenEasing,
            CollectReferenceImages());

        var result = await RunAiAsync(
            $"Claude is drawing {TweenCount} inbetween(s)…",
            ct => _artist.GenerateInbetweensAsync(request, ct));
        if (result is null) return;

        var frames = result
            .OrderBy(f => f.T)
            .Select(f => NewFrameFor(layer, f.Strokes, FrameRole.Inbetween))
            .ToList();
        _editor.InsertInbetweens(layer.Id, aIndex, frames);
        CurrentFrameIndex = Math.Min(aIndex + 1, Scene.FrameCount - 1);
        AiStatus = $"Inserted {frames.Count} AI inbetween(s).";
    }

    /// <summary>Claude paints strokes from a text prompt onto the current frame.</summary>
    [RelayCommand]
    private async Task AiDrawAsync()
    {
        if (_artist is null || AiBusy || string.IsNullOrWhiteSpace(AiPrompt)) return;
        var target = PaintTarget();
        if (target is null) return;
        if (!ActiveLayer.Visible)
        {
            AiStatus = $"Layer “{ActiveLayer.Name}” is hidden — enable its visibility to draw on it.";
            return;
        }

        var request = new DrawRequest(
            new SceneInfo(Scene.Width, Scene.Height, Scene.Fps),
            AiPrompt.Trim(),
            StrokesOf(target),
            CollectReferenceImages());

        var strokes = await RunAiAsync(
            "Claude is drawing…",
            ct => _artist.DrawAsync(request, ct));
        if (strokes is null) return;

        _editor.Perform(_ => StrokesOf(target).AddRange(strokes));
        _cache.Invalidate(target.Id);
        _dirtyThumbIds.Add(target.Id);
        PublishSnapshot();
        RefreshThumbnails();
        AiStatus = $"Drew {strokes.Count} stroke(s).";
    }

    /// <summary>Shared busy/cancel/error plumbing for AI calls; null on failure.</summary>
    private async Task<T?> RunAiAsync<T>(string busyMessage, Func<CancellationToken, Task<AiResult<T>>> call)
        where T : class
    {
        _aiCts = new CancellationTokenSource();
        AiBusy = true;
        AiStatus = busyMessage;
        try
        {
            var result = await call(_aiCts.Token);
            if (result.Outcome == AiOutcome.Success) return result.Value;
            AiStatus = result.Message ?? "AI request failed.";
            return null;
        }
        finally
        {
            AiBusy = false;
            _aiCts.Dispose();
            _aiCts = null;
        }
    }

    // ---- document I/O -------------------------------------------------------

    // ---- external producers (IPC/MCP) ---------------------------------------

    /// <summary>The layer external tools target when they don't name one.</summary>
    public Layer ActiveLayerForIpc => ActiveLayer;

    /// <summary>Composite one timeline frame to PNG (no onion skin, no live stroke).</summary>
    public string RenderFramePng(int frameIndex)
    {
        var scene = Scene;
        var passes = new List<RenderPass>();
        foreach (var layer in scene.Layers)
        {
            if (!layer.Visible) continue;
            var frame = ExposureSheet.ExposedFrame(layer, frameIndex);
            if (frame is null) continue;
            passes.Add(new RenderPass(_cache.Get(frame, scene.Width, scene.Height), null, layer.Opacity));
        }
        using var image = SceneRenderer.Compose(scene.Width, scene.Height, passes, SceneRenderer.BackgroundOf(scene));
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG encode failed.");
        return Convert.ToBase64String(data.AsSpan());
    }

    /// <summary>
    /// Insert externally produced inbetween frames (already validated, sorted
    /// by t) after key <paramref name="aIndex"/>. One undo step. Returns the
    /// number of frames inserted.
    /// </summary>
    public int InsertExternalInbetweens(string layerId, int aIndex, List<List<Stroke>> strokeFrames)
    {
        var layer = Scene.Layers.First(l => l.Id == layerId);
        var frames = strokeFrames.Select(s => NewFrameFor(layer, s, FrameRole.Inbetween)).ToList();
        _editor.InsertInbetweens(layerId, aIndex, frames);
        CurrentFrameIndex = Math.Min(aIndex + 1, Scene.FrameCount - 1);
        return frames.Count;
    }

    /// <summary>
    /// Append externally produced strokes to the key exposed at
    /// <paramref name="frameIndex"/>. One undo step. Returns strokes added
    /// (0 when the layer has no key there).
    /// </summary>
    public int AppendExternalStrokes(string layerId, int frameIndex, List<Stroke> strokes)
    {
        var layer = Scene.Layers.First(l => l.Id == layerId);
        var keyIndex = ExposureSheet.KeyIndexAtOrBefore(layer, frameIndex);
        if (keyIndex < 0) return 0;
        var frame = layer.Cels[keyIndex].Frame!;
        _editor.Perform(_ => StrokesOf(frame).AddRange(strokes));
        _cache.Invalidate(frame.Id);
        _dirtyThumbIds.Add(frame.Id);
        PublishSnapshot();
        RefreshThumbnails();
        return strokes.Count;
    }

    /// <summary>Replace the ACTIVE tab's document (fresh editor, clean state).</summary>
    public void ReplaceDocument(Doc doc)
    {
        _switchingTabs = true;
        var tab = ActiveTab ?? Tabs[0];
        tab.Editor = new DocumentEditor(doc);
        AttachEditor(tab.Editor);
        ActiveLayerIndex = 0;
        CurrentFrameIndex = 0;
        tab.IsDirty = false;
        _switchingTabs = false;
    }

    /// <summary>Serialize the save target (a reference tab serializes its owning document).</summary>
    public string SerializeDocument() => DocJson.Serialize(SaveTargetTab?.Doc ?? Doc);

    // ---- internals ----------------------------------------------------------

    private void OnPlaybackTick() => StepPlayback();

    private void OnDocumentChanged()
    {
        MarkDocumentEdited();
        BrushTipRegistry.Register(Doc.BrushTips);
        ClipRegionRegistry.Register(Doc.ClipRegions);
        OnPropertyChanged(nameof(ReferenceSheetsView));
        SyncLayerChoices();
        ClampCurrentFrame();
        SyncLayerRows();
        OnPropertyChanged(nameof(FrameLabel));
        OnPropertyChanged(nameof(TimelineExtent));
        OnPropertyChanged(nameof(MaxScrubFrame));
        OnPropertyChanged(nameof(Fps));
        PublishSnapshot();
        RefreshThumbnails();
    }

    private void SyncLayerChoices()
    {
        if (LayerChoices.SequenceEqual(Scene.Layers)) return;
        LayerChoices.Clear();
        foreach (var layer in Scene.Layers) LayerChoices.Add(layer);
        if (ActiveLayerIndex >= LayerChoices.Count) ActiveLayerIndex = LayerChoices.Count - 1;
    }

    private void ClampCurrentFrame()
    {
        var max = Math.Max(0, Scene.FrameCount - 1);
        if (CurrentFrameIndex > max) CurrentFrameIndex = max;
        else PublishSnapshot();
    }

    /// <summary>
    /// Mirror Scene.Layers into LayerRows (topmost layer first) and keep each
    /// row's cell strip in step with the timeline.
    /// </summary>
    private void SyncLayerRows()
    {
        var layers = Scene.Layers;
        while (LayerRows.Count > layers.Count) LayerRows.RemoveAt(LayerRows.Count - 1);
        while (LayerRows.Count < layers.Count) LayerRows.Add(new LayerRow(this));

        var active = Math.Clamp(ActiveLayerIndex, 0, layers.Count - 1);
        for (var i = 0; i < LayerRows.Count; i++)
        {
            var row = LayerRows[i];
            var sceneIndex = layers.Count - 1 - i;
            var layer = layers[sceneIndex];
            row.SyncFromModel(layer, sceneIndex);
            row.IsActive = sceneIndex == active;

            while (row.Cells.Count > TimelineExtent) row.Cells.RemoveAt(row.Cells.Count - 1);
            while (row.Cells.Count < TimelineExtent) row.Cells.Add(new FrameCell(row.Cells.Count));
            foreach (var cell in row.Cells)
            {
                var frame = ExposureSheet.FrameAtExactIndex(layer, cell.Index);
                cell.LayerIndex = sceneIndex;
                cell.IsKeyed = frame is not null;
                cell.Role = frame?.Role ?? FrameRole.Key;
                cell.IsVirtual = cell.Index >= Scene.FrameCount;
                cell.IsCurrent = cell.Index == CurrentFrameIndex;
            }
        }
        OnPropertyChanged(nameof(FrameCells));
        RefreshRangeHighlights();
    }

    // ---- thumbnails ----------------------------------------------------------

    private readonly HashSet<string> _dirtyThumbIds = [];
    private bool _allThumbsDirty;

    /// <summary>
    /// Update timeline thumbnails lazily: only cells whose keyed frame is new,
    /// changed, or explicitly marked dirty are re-rendered.
    /// </summary>
    private void RefreshThumbnails()
    {
        foreach (var row in LayerRows)
        {
            foreach (var cell in row.Cells)
            {
                var frame = ExposureSheet.FrameAtExactIndex(row.Layer, cell.Index);
                if (frame is null)
                {
                    cell.Thumb = null;
                    cell.ThumbFrameId = null;
                    continue;
                }
                var stale = _allThumbsDirty
                            || cell.ThumbFrameId != frame.Id
                            || _dirtyThumbIds.Contains(frame.Id);
                if (!stale && cell.Thumb is not null) continue;

                var bmp = _cache.Get(frame, Scene.Width, Scene.Height);
                cell.Thumb = ThumbnailRenderer.Render(bmp);
                cell.ThumbFrameId = frame.Id;
            }
        }
        _dirtyThumbIds.Clear();
        _allThumbsDirty = false;
    }

    private void RefreshCellHighlights()
    {
        foreach (var row in LayerRows)
        {
            foreach (var cell in row.Cells) cell.IsCurrent = cell.Index == CurrentFrameIndex;
        }
    }

    /// <summary>Composite the scene for the current playhead and hand it to the view.</summary>
    public void PublishSnapshot()
    {
        var scene = Scene;
        var passes = new List<RenderPass>();

        if (OnionSkin && !IsPlaying)
        {
            foreach (var layer in scene.Layers)
            {
                if (!layer.Visible || !layer.OnionEnabled) continue;
                for (var d = Math.Max(1, OnionDepth); d >= 1; d--)
                {
                    var prev = ExposureSheet.FrameAtExactIndex(layer, CurrentFrameIndex - d);
                    if (prev is not null)
                        passes.Add(new RenderPass(_cache.Get(prev, scene.Width, scene.Height), SceneRenderer.OnionPrevTint, 0.25 / d));
                    var next = ExposureSheet.FrameAtExactIndex(layer, CurrentFrameIndex + d);
                    if (next is not null)
                        passes.Add(new RenderPass(_cache.Get(next, scene.Width, scene.Height), SceneRenderer.OnionNextTint, 0.25 / d));
                }
            }
        }

        foreach (var layer in scene.Layers)
        {
            if (!layer.Visible) continue;
            var frame = ExposureSheet.ExposedFrame(layer, CurrentFrameIndex);
            if (frame is null) continue;

            var bmp = _cache.Get(frame, scene.Width, scene.Height);

            // Live stroke preview: the active layer shows the incrementally
            // stamped live bitmap instead of the cached committed frame.
            if (_liveComposite is not null && _strokeBuilder.IsActive && layer.Id == ActiveLayer.Id)
            {
                bmp = _liveComposite;
            }

            passes.Add(new RenderPass(bmp, null, layer.Opacity));
        }

        var info = new SKImageInfo(scene.Width, scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        if (_composeSurface is null || !_composeInfo.Equals(info))
        {
            _composeSurface?.Dispose();
            _composeSurface = SKSurface.Create(info)
                ?? throw new InvalidOperationException("Could not create compose surface.");
            _composeInfo = info;
        }
        SceneRenderer.ComposeInto(_composeSurface, passes, SceneRenderer.BackgroundOf(scene));
        SnapshotChanged?.Invoke(new RenderSnapshot(_composeSurface.Snapshot(), scene.Width, scene.Height));
    }
}
