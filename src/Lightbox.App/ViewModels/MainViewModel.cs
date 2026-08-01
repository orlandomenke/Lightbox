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

    /// <summary>Part of the Shift+click cel range selection.</summary>
    [ObservableProperty]
    private bool _isSelected;

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

    /// <summary>Measured repaint cost, shown as headroom in the info strip.</summary>
    public PerformanceMonitor Performance { get; } = new();

    private DocumentEditor _editor;
    private readonly ComposeRing _composeRing = new();
    private long _publishSeq;

    /// <summary>Document region changed since the last publish (null = everything).</summary>
    private SKRectI? _pendingDirty;
    private bool _dirtyIsWholeCanvas = true;

    /// <summary>What the canvas says it can display: document pixels per screen pixel.</summary>
    private double _displayScale = 1.0;

    /// <summary>
    /// Resolution to composite at, combining what the canvas can show with the
    /// user's quality preference. Capped at 1: compositing beyond document
    /// resolution would invent detail that is not in the record.
    /// </summary>
    private double ComposeScale => CanvasQuality switch
    {
        CanvasQuality.Full => 1.0,
        CanvasQuality.Half => Math.Clamp(_displayScale * 0.5, 0.125, 1.0),
        _ => Math.Clamp(_displayScale, 0.125, 1.0),
    };

    /// <summary>Called by the canvas when zoom or window size changes what it can show.</summary>
    public void SetDisplayScale(double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0) return;
        if (Math.Abs(scale - _displayScale) < 0.001) return;
        _displayScale = scale;
        InvalidateWholeCanvas();
        _composeRing.InvalidateAll();
        Performance.Reset(); // old timings were taken at a different resolution
        PublishSnapshot();
        RefreshDocumentStats();
    }

    /// <summary>
    /// Gate for anything that changes pixels or geometry. Hidden and locked
    /// are both refusals, with different reasons, and a locked folder reports
    /// itself rather than blaming the layer inside it. Every mutating path
    /// goes through here — the old hand-written visibility checks covered
    /// paint, fill and AI draw, which is how transform, cel edits and the
    /// external writers ended up unguarded.
    /// </summary>
    private bool CanEdit(Layer? layer, string verb)
    {
        if (layer is null) return false;
        if (!Scene.IsLayerVisible(layer))
        {
            AiStatus = $"Layer \u201c{layer.Name}\u201d is hidden \u2014 enable its visibility to {verb}.";
            return false;
        }
        if (!Scene.IsLayerEditable(layer))
        {
            AiStatus = Scene.GroupOf(layer) is { Locked: true } folder
                ? $"Folder \u201c{folder.Name}\u201d is locked \u2014 unlock it to {verb}."
                : $"Layer \u201c{layer.Name}\u201d is locked \u2014 unlock it to {verb}.";
            return false;
        }
        return true;
    }

    /// <summary>True when the active layer refuses edits — drives the blocked cursor.</summary>
    public bool ActiveLayerBlocked =>
        ActiveLayer is { } layer && (!Scene.IsLayerVisible(layer) || !Scene.IsLayerEditable(layer));

    /// <summary>Frame times measured on the render thread.</summary>
    public void RecordFrameTime(double milliseconds) => Performance.RecordFrame(milliseconds);

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

    public MainViewModel() : this(ResolveArtist())
    {
    }

    /// <summary>Test seam: inject a fake artist (or null for "no API key").</summary>
    public MainViewModel(IAiArtist? artist)
    {
        _artist = artist;
        var first = new DocumentTab(new DocumentEditor(StartupDoc()), "Untitled-1") { IsActive = true };
        Tabs.Add(first);
        _activeTab = first;
        _editor = first.Editor;
        // Land on something paintable: layer 0 is the locked paper, so leaving
        // the index at 0 would make the very first stroke bounce.
        _activeLayerIndex = FirstPaintableLayer(first.Editor.Doc);
        _editor.Changed += OnDocumentChanged;
        _clock.Tick += OnPlaybackTick;
        _autosave = new AutosaveService(() => SaveTargetTab?.Doc ?? Doc);
        ColorPicker = new ColorPickerViewModel();
        ColorPicker.SetHex(ColorHex);
        ColorPicker.HexCommitted += hex => ColorHex = hex;
        PaletteDocker = new PaletteDockerViewModel(
            OnSwatchRecoloured, PerformPaletteEdit, PaintWithSwatch, () => ColorHex);
        PaletteDocker.SwatchEditRunEnded += CommitSwatchEdit;
        GradientDocker = new GradientDockerViewModel(OnGradientEdited, PerformGradientEdit);
        ProjectDocker = new ProjectViewModel(NewAnimationDoc, OpenProjectDocument, OnProjectChanged);
        PaletteRegistry.Reset(Doc.Palettes, Doc.Gradients);
        PaletteDocker.Load(Doc);
        GradientDocker.Load(Doc);
        LoadBrushState();
        SyncLayerChoices();
        SyncLayerRows();
        RefreshThumbnails();
        RefreshDocumentStats();
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

    /// <summary>
    /// The document the app opens on. It used to come from
    /// <c>CreateDoc()</c> with no paper colour, which produced a document
    /// whose scene declared white paper while no layer supplied it: the canvas
    /// and the layer thumbnail both showed the transparency checkerboard, and
    /// there was nothing called Background to lock. It is now made the same
    /// way File → New makes one, from the scene's own default.
    /// </summary>
    private static Doc StartupDoc() =>
        DocumentFactory.CreateDoc(paperColor: Scene.DefaultBackgroundColor);

    /// <summary>Index of the first layer an artist can actually draw on.</summary>
    private static int FirstPaintableLayer(Doc doc) =>
        doc.Scene.Layers.FindIndex(l => !l.IsBackground) is var i && i >= 0 ? i : 0;

    /// <summary>Create a document from the File → New dialog in a new tab.</summary>
    public void NewDocument(NewDocumentSettings settings)
    {
        var doc = DocumentFactory.CreateDoc(
            settings.Width, settings.Height, settings.Fps,
            settings.TransparentBackground ? null : settings.BackgroundColor);
        doc.Scene.Name = settings.Name;
        doc.Scene.Ppi = settings.Ppi;
        doc.Scene.BackgroundColor = settings.BackgroundColor;
        doc.Scene.TransparentBackground = settings.TransparentBackground;
        AddTab(new DocumentTab(new DocumentEditor(doc), settings.Name)
        {
            // Land on something paintable. The paper is layer 0 and locked, so
            // selecting it would make the very first stroke bounce.
            SavedLayerIndex = FirstPaintableLayer(doc),
        });
    }

    // ---- project commands ---------------------------------------------------

    /// <summary>
    /// Start a project at <paramref name="root"/>, adopting the document that
    /// is already open as its first animation.
    ///
    /// Adopting rather than starting empty is the point: the artist has been
    /// drawing, and the container should form around that work instead of
    /// asking them to recreate it somewhere else.
    /// </summary>
    public void NewProject(string root, string name)
    {
        var project = ProjectIo.Create(name, root);
        var character = ProjectIo.AddCharacter(project, name);

        if (SaveTargetTab is { } tab)
        {
            var reference = ProjectIo.AddAnimation(project, character, tab.Title, tab.Doc);
            tab.Source = reference;
            // The document's palettes and gradients become the project's:
            // shared is the whole reason the container exists.
            project.Palettes.AddRange(tab.Doc.Palettes);
            foreach (var (id, gradient) in tab.Doc.Gradients) project.Gradients[id] = gradient;
        }

        ProjectDocker.Adopt(project);
        SaveProject(everything: true);
        AiStatus = $"Created project “{name}”.";
    }

    public void OpenProject(string root)
    {
        try
        {
            var project = ProjectIo.Load(root);
            ProjectDocker.Adopt(project);
            // Open the first animation so the project is not an empty shell —
            // and so the registries have something to resolve against.
            if (project.Characters.SelectMany(c => c.Animations).FirstOrDefault() is { } first
                && ProjectIo.LoadDocument(project, first) is { } doc)
            {
                OpenProjectDocument(first, doc);
            }
            OnProjectChanged();
            AiStatus = $"Opened project “{project.Name}”.";
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            AiStatus = $"Could not open that project: {ex.Message}";
        }
    }

    /// <summary>
    /// Write the project, and only the animations that changed.
    /// </summary>
    /// <param name="everything">
    /// Write every loaded document regardless. True for the first save of a
    /// new project, where nothing has been "changed" since it was created but
    /// none of it is on disk yet.
    /// </param>
    public void SaveProject(bool everything = false)
    {
        if (ProjectDocker.Project is not { } project) return;
        try
        {
            ProjectIo.Save(project, everything ? null : ProjectDocker.Dirty);
            ProjectDocker.MarkAllSaved();
            foreach (var tab in Tabs)
            {
                if (tab.Source is not null) tab.IsDirty = false;
            }
            AiStatus = $"Saved “{project.Name}”.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AiStatus = $"Could not save the project: {ex.Message}";
        }
    }

    /// <summary>
    /// Can the current tab be saved without asking where? True for a project
    /// animation and for a loose document that already has a path.
    /// </summary>
    public bool CanSaveInPlace =>
        ProjectDocker.HasProject && SaveTargetTab?.Source is not null
        || SaveTargetTab?.FilePath is { Length: > 0 };

    /// <summary>
    /// Save without a picker. Missing entirely until now — every save opened a
    /// dialog even when the tab already knew where it came from.
    /// </summary>
    public void Save()
    {
        if (ProjectDocker.HasProject && SaveTargetTab?.Source is not null)
        {
            SaveProject();
            return;
        }
        if (SaveTargetTab is not { FilePath: { Length: > 0 } path } tab) return;
        try
        {
            DocJson.Save(tab.Doc, path);
            tab.IsDirty = false;
            AiStatus = $"Saved {System.IO.Path.GetFileName(path)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AiStatus = $"Could not save: {ex.Message}";
        }
    }

    /// <summary>
    /// A standalone copy of the active document, with every project resource
    /// it references inlined — what "Export document…" writes.
    /// </summary>
    public string ExportStandaloneDocument()
    {
        var doc = SaveTargetTab?.Doc ?? Doc;
        if (ProjectDocker.Project is { } project) doc = ProjectIo.Flatten(doc, project);
        return DocJson.Serialize(doc);
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
        // Here rather than in OnDocumentChanged: stroke commits take that
        // method's scoped-edit early return, and a stroke is exactly the edit
        // an incremental save must not miss.
        if ((tab.Owner ?? tab).Source is { } source) ProjectDocker.MarkDirty(source);
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
            passes.Add(new RenderPass(_cache.Get(frame, view.Width, view.Height), null, layer.Opacity, SceneRenderer.ToSkia(layer.BlendMode)));
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

    partial void OnColorHexChanged(string value)
    {
        ColorPicker.SetHex(value);
        if (_settingColorFromSwatch) return;

        // In swatch-edit mode the picker drives the selected swatch, which is
        // what makes dragging the wheel recolour the drawing live.
        if (PaletteDocker.ApplyPickerColor(value)) return;

        // Otherwise the artist has chosen a colour by some other means — the
        // wheel, the hex box, the eyedropper — and the link to the swatch has
        // to go. Keeping it would mean painting in one colour while recording
        // a reference to another, and the stroke would jump the moment anyone
        // touched the palette.
        ActiveSwatchId = null;
    }

    // ---- live palettes ------------------------------------------------------

    /// <summary>The palette docker's state.</summary>
    public PaletteDockerViewModel PaletteDocker { get; }

    /// <summary>
    /// The swatch the next stroke should reference, or null to record a
    /// literal colour. Set by selecting a swatch; cleared by choosing a colour
    /// any other way (see <see cref="OnColorHexChanged"/>).
    /// </summary>
    public string? ActiveSwatchId { get; private set; }

    private bool _settingColorFromSwatch;

    /// <summary>The swatch and the colour it held when the current edit run began.</summary>
    private (string Id, string Before)? _pendingSwatchEdit;

    private void PaintWithSwatch(string swatchId)
    {
        if (PaletteRegistry.ResolveSwatch(swatchId) is not { } swatch) return;
        _settingColorFromSwatch = true;
        try
        {
            ColorHex = swatch.Color;
        }
        finally
        {
            _settingColorFromSwatch = false;
        }
        ActiveSwatchId = swatchId;
    }

    /// <summary>A structural palette edit — one undo step, then a full resync.</summary>
    private void PerformPaletteEdit(Action<Doc> mutate)
    {
        CommitSwatchEdit();
        _editor.Perform(mutate);
    }

    private void OnSwatchRecoloured(SwatchRow row, string before)
    {
        // A run is one swatch at a time; touching a different one closes the
        // previous run off so each lands as its own undo step.
        if (_pendingSwatchEdit is { } pending && pending.Id != row.Id) CommitSwatchEdit();
        _pendingSwatchEdit ??= (row.Id, before);

        if (row.Id == ActiveSwatchId) PaintWithSwatch(row.Id);
        RepaintForSwatch(row.Id);
        MarkDocumentEdited();
    }

    /// <summary>
    /// Close off a run of colour edits as a single undo step. Dragging the
    /// colour wheel fires an edit per pointer event; one step each would bury
    /// the drawing history under sixty entries of the same swatch.
    /// </summary>
    internal void CommitSwatchEdit()
    {
        if (_pendingSwatchEdit is not { } pending) return;
        _pendingSwatchEdit = null;
        if (PaletteRegistry.ResolveSwatch(pending.Id)?.Color is not { } after) return;
        if (after == pending.Before) return;

        var (id, before) = (pending.Id, pending.Before);
        // Looked up by id inside the closure rather than captured: a snapshot
        // undo replaces Doc wholesale, so the Swatch object this ran against
        // will not be the one a later redo has to write to.
        _editor.PerformDelta(d => SetSwatchColor(d, id, after), d => SetSwatchColor(d, id, before));
    }

    private static void SetSwatchColor(Doc doc, string swatchId, string color)
    {
        foreach (var palette in doc.Palettes)
        {
            foreach (var swatch in palette.Swatches)
            {
                if (swatch.Id == swatchId) swatch.Color = color;
            }
        }
    }

    /// <summary>
    /// Repaint what a swatch edit actually changed. Only frames holding a
    /// stroke that references the swatch are dropped from the cache — walking
    /// the stroke record is far cheaper than re-rendering frames whose pixels
    /// cannot have moved, and a palette used on one layer must not cost a
    /// whole-document re-render on every pointer event of a wheel drag.
    /// </summary>
    private void RepaintForSwatch(string swatchId)
    {
        foreach (var layer in Scene.Layers)
        {
            foreach (var cel in layer.Cels)
            {
                if (cel.Frame is not { } frame) continue;
                if (!StrokesOf(frame).Any(s => s.SwatchId == swatchId)) continue;
                _cache.Invalidate(frame.Id);
                _dirtyThumbIds.Add(frame.Id);
            }
        }
        InvalidateWholeCanvas();
        _composeRing.InvalidateAll();
        PublishSnapshot();
        RefreshThumbnails();
    }

    // ---- projects -----------------------------------------------------------

    /// <summary>
    /// The project docker's state. Holds no project until one is created or
    /// opened — the app is document-first and shows no project UI until then.
    /// </summary>
    public ProjectViewModel ProjectDocker { get; }

    /// <summary>Whether any project UI should exist at all.</summary>
    public bool HasProject => ProjectDocker.HasProject;

    private void OnProjectChanged()
    {
        OnPropertyChanged(nameof(HasProject));
        RegisterResources();
        MarkDocumentEdited();
    }

    /// <summary>A blank document for a new animation, matching the active scene's shape.</summary>
    private Doc NewAnimationDoc()
    {
        var scene = Scene;
        return DocumentFactory.CreateDoc(
            scene.Width, scene.Height, scene.Fps,
            scene.TransparentBackground ? null : scene.BackgroundColor);
    }

    /// <summary>Open a project animation as a tab, or focus the tab it is already in.</summary>
    private void OpenProjectDocument(DocumentRef reference, Doc doc)
    {
        if (Tabs.FirstOrDefault(t => t.Source?.Id == reference.Id) is { } already)
        {
            ActiveTab = already;
            return;
        }
        AddTab(new DocumentTab(new DocumentEditor(doc), reference.Name)
        {
            Source = reference,
            SavedLayerIndex = FirstPaintableLayer(doc),
        });
    }

    /// <summary>
    /// Point the engine's registries at the project's shared resources AND the
    /// active document's.
    ///
    /// This is the whole of Pillar 1's sharing, and it needs no engine change:
    /// the brush engine already resolves swatches, gradients, tips and clips by
    /// id at render time. Widening the scope is all it takes for two animations
    /// under one character to paint from one palette.
    /// </summary>
    /// <summary>
    /// Re-scope the registries after the project's shared resources changed
    /// outside a document edit — importing a palette, or a test adding one.
    /// </summary>
    public void RefreshProjectResources() => RegisterResources();

    private void RegisterResources()
    {
        var palettes = Doc.Palettes.AsEnumerable();
        var gradients = new Dictionary<string, Gradient>(Doc.Gradients);
        if (ProjectDocker.Project is { } project)
        {
            // Document first, project second, so a document's own copy of a
            // swatch id loses to the project's — the shared one is the live one.
            palettes = palettes.Concat(project.Palettes);
            foreach (var (id, gradient) in project.Gradients) gradients[id] = gradient;
        }
        PaletteRegistry.Reset(palettes, gradients);
    }

    // ---- gradients ----------------------------------------------------------

    /// <summary>The gradient docker's state.</summary>
    public GradientDockerViewModel GradientDocker { get; }

    /// <summary>Opacity the gradient tool lays its ramp down at, 0–1.</summary>
    [ObservableProperty]
    private double _gradientOpacity = 1.0;

    private void PerformGradientEdit(Action<Doc> mutate)
    {
        CommitSwatchEdit();
        _editor.Perform(mutate);
    }

    /// <summary>
    /// A gradient definition changed. Same scoping as a swatch edit: only
    /// frames holding a stroke that paints this gradient are dropped.
    /// </summary>
    private void OnGradientEdited(string gradientId)
    {
        // The registry holds the same Gradient object the docker just edited,
        // so nothing needs re-registering — only the cached pixels are stale.
        foreach (var layer in Scene.Layers)
        {
            foreach (var cel in layer.Cels)
            {
                if (cel.Frame is not { } frame) continue;
                if (!StrokesOf(frame).Any(s => s.GradientId == gradientId)) continue;
                _cache.Invalidate(frame.Id);
                _dirtyThumbIds.Add(frame.Id);
            }
        }
        // A gradient being dragged right now redefines its own preview.
        if (_liveGradient is not null) RenderGradientPreview();
        InvalidateWholeCanvas();
        _composeRing.InvalidateAll();
        MarkDocumentEdited();
        PublishSnapshot();
        RefreshThumbnails();
    }

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
        var antiAlias = AntiAliasing; // global — a preset never overrides it
        _brushWork = value.Settings.Clone();
        _brushWork.AntiAlias = antiAlias;
        EnsurePresetTip(value);
        NotifyBrushProperties();
        _applyingPreset = false;
        PersistBrushState();
    }

    /// <summary>
    /// Global anti-aliasing for everything that paints (brush, eraser, fill).
    /// The value is stamped into each stroke at paint time, so existing art
    /// re-renders bit-identically no matter how the toggle changes later.
    /// </summary>
    public bool AntiAliasing
    {
        get => _brushWork.AntiAlias;
        set
        {
            if (_brushWork.AntiAlias == value) return;
            _brushWork.AntiAlias = value;
            _eraserWork.AntiAlias = value;
            OnPropertyChanged();
            if (!_applyingPreset) PersistBrushState();
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

    public double MediumBristleDrag
    {
        get => GetBrush(s => s.Medium.BristleDrag);
        set => SetBrush(s => s.Medium.BristleDrag = Math.Clamp(value, 0, 1));
    }

    public double MediumPaintLoad
    {
        get => GetBrush(s => s.Medium.PaintLoad);
        set => SetBrush(s => s.Medium.PaintLoad = Math.Clamp(value, 0, 1));
    }

    public double MediumPickup
    {
        get => GetBrush(s => s.Medium.Pickup);
        set => SetBrush(s => s.Medium.Pickup = Math.Clamp(value, 0, 1));
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

    private static readonly string[] BrushPropertyNames =
    [
        nameof(BrushSize), nameof(BrushHardness), nameof(BrushOpacity), nameof(BrushFlow),
        nameof(BrushSpacing), nameof(BrushWetEdge), nameof(BrushGranulation), nameof(BrushScatter),
        nameof(BrushRotationJitter), nameof(BrushPressureEnabled),
        nameof(BrushPressureSizeGamma), nameof(BrushPressureFlowGamma), nameof(BrushPressureHardnessGamma),
        nameof(BrushPressureAffectsSize), nameof(BrushPressureAffectsFlow), nameof(BrushPressureAffectsHardness),
        nameof(BrushCursorDiameter),
        nameof(BrushSizeJitter), nameof(BrushMinimumDiameter), nameof(BrushRoundness),
        nameof(BrushRoundnessJitter), nameof(BrushAngleFollowsDirection), nameof(BrushFlowJitter),
        nameof(BrushTextureSurface), nameof(BrushTextureScale), nameof(BrushTextureDepth),
        nameof(BrushSecondaryColor), nameof(BrushColorJitter), nameof(BrushHueJitter),
        nameof(BrushSaturationJitter), nameof(BrushBrightnessJitter),
        nameof(IsSmudgeBrush), nameof(BrushSmudgeMode), nameof(BrushSmudgeLength),
        nameof(BrushSmudgeRadius), nameof(BrushColorRate),
        nameof(BrushMedium), nameof(MediumIsSimulated), nameof(MediumHasBody),
        nameof(MediumWetness), nameof(MediumViscosity), nameof(MediumDrag), nameof(MediumFlowSteps),
        nameof(MediumAbsorbency), nameof(MediumEdgePull),
        nameof(MediumPigmentDensity), nameof(MediumGranularity), nameof(MediumHiding),
        nameof(MediumPhysicalMixing),
        nameof(MediumPaper), nameof(MediumPaperScale), nameof(MediumPaperInfluence),
        nameof(MediumBody), nameof(MediumRelief), nameof(MediumBristleDrag),
        nameof(MediumPaintLoad), nameof(MediumPickup),
        nameof(MediumPressureWater), nameof(MediumPressureMix), nameof(MediumRewetting),
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
            SmoothingMode = _stabilizer.Mode.ToString(),
            SmoothingWindow = _stabilizer.Window,
            SmoothingStrength = _stabilizer.Strength,
            LazyRadius = _stabilizer.LazyRadius,
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
        if (Enum.TryParse<SmoothingMode>(state.SmoothingMode, out var mode)) _stabilizer.Mode = mode;
        if (state.SmoothingWindow is { } window) _stabilizer.Window = Math.Clamp(window | 1, 3, 25);
        if (state.SmoothingStrength is { } strength) _stabilizer.Strength = Math.Clamp(strength, 0, 0.95);
        if (state.LazyRadius is { } radius) _stabilizer.LazyRadius = Math.Clamp(radius, 4, 200);
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
    [NotifyPropertyChangedFor(nameof(IsPickerTool))]
    [NotifyPropertyChangedFor(nameof(IsGradientTool))]
    [NotifyPropertyChangedFor(nameof(IsPaintTool))]
    private ToolId _activeTool = ToolId.Brush;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectVariantGlyph))]
    [NotifyPropertyChangedFor(nameof(IsFreehandVariant))]
    [NotifyPropertyChangedFor(nameof(IsPolygonVariant))]
    [NotifyPropertyChangedFor(nameof(IsBoxVariant))]
    [NotifyPropertyChangedFor(nameof(IsEllipseVariant))]
    [NotifyPropertyChangedFor(nameof(IsWandVariant))]
    private SelectVariant _activeSelectVariant = SelectVariant.Freehand;

    public bool IsFreehandVariant => ActiveSelectVariant == SelectVariant.Freehand;

    public bool IsPolygonVariant => ActiveSelectVariant == SelectVariant.Polygon;

    public bool IsBoxVariant => ActiveSelectVariant == SelectVariant.Box;

    public bool IsEllipseVariant => ActiveSelectVariant == SelectVariant.Ellipse;

    public bool IsWandVariant => ActiveSelectVariant == SelectVariant.Wand;

    public bool IsBrushTool => ActiveTool == ToolId.Brush;

    public bool IsEraserTool => ActiveTool == ToolId.Eraser;

    public bool IsFillTool => ActiveTool == ToolId.Fill;

    public bool IsSelectTool => ActiveTool == ToolId.Select;

    public bool IsPickerTool => ActiveTool == ToolId.Picker;

    public bool IsGradientTool => ActiveTool == ToolId.Gradient;

    /// <summary>Brush or eraser — the tools whose strokes the brush-parameter flyout edits.</summary>
    public bool IsPaintTool => ActiveTool is ToolId.Brush or ToolId.Eraser;

    /// <summary>Eyedropper click: the color under the cursor (what the eye sees, incl. paper).</summary>
    public void PickColorAt(double x, double y)
    {
        int px = (int)Math.Round(x), py = (int)Math.Round(y);
        if (px < 0 || py < 0 || px >= Scene.Width || py >= Scene.Height) return;
        using var composite = CompositeVisibleLayers();
        var color = composite.GetPixel(px, py);
        if (color.Alpha == 0)
        {
            ColorHex = Scene.TransparentBackground ? "#ffffff" : Scene.BackgroundColor;
            return;
        }
        ColorHex = $"#{color.Red:x2}{color.Green:x2}{color.Blue:x2}";
    }

    /// <summary>Timeline-context shortcut: key the active layer's cel at the playhead.</summary>
    public void InsertKeyframeAtPlayhead() =>
        _editor.SetKeyAt(ActiveLayer.Id, CurrentFrameIndex, FrameRole.Key);

    public string SelectVariantGlyph => ActiveSelectVariant switch
    {
        SelectVariant.Polygon => "⬠",
        SelectVariant.Box => "▭",
        SelectVariant.Ellipse => "◯",
        SelectVariant.Wand => "🪄",
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
        OnPropertyChanged(nameof(LazyRadiusForCursor));
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
            SelectVariant.Ellipse => SelectVariant.Wand,
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

    /// <summary>
    /// Where a "below line work" fill belongs in the stroke list.
    ///
    /// Index 0 was the obvious answer and the wrong one: it put the fill under
    /// EVERYTHING, so a second fill disappeared beneath the first, and a fill
    /// made after erasing was wiped by the eraser it had been slipped behind.
    /// Both read to the artist as "the fill did nothing".
    ///
    /// The rule that holds instead: go under the line work, but no further
    /// back than the last stroke that would swallow you. Only a brush stroke
    /// is line work to tuck beneath; a fill, a gradient or an eraser already
    /// on the layer is content this fill must sit on top of — an eraser
    /// especially, because it removed what was there when it ran, and putting
    /// later content underneath makes it delete something that never existed.
    /// </summary>
    internal static int UnderLineWorkIndex(IReadOnlyList<Stroke> strokes)
    {
        for (var i = strokes.Count - 1; i >= 0; i--)
        {
            if (strokes[i].Tool != ToolKind.Brush) return i + 1;
        }
        return 0;
    }

    /// <summary>Fill tool click: flood at a document position, record a fill stroke.</summary>
    /// <summary>
    /// A colour was dragged from the swatch onto the canvas. Fills there,
    /// choosing the sensible method rather than making the artist pick one:
    /// inside a selection the selection is the region, otherwise it is a
    /// flood fill from the dropped point. The colour becomes the current
    /// colour too — dragging it out is a statement of intent.
    /// </summary>
    public void DropColorAt(string hex, double x, double y)
    {
        if (IsPlaying) return;
        if (!CanEdit(ActiveLayer, "fill on it")) return;

        ColorHex = hex;

        // Inside a selection, the selection is obviously the region the
        // artist means — filling only the contiguous patch under the drop
        // point would be a strange reading of the gesture. Outside one, fall
        // back to a flood fill from where it landed.
        if (HasSelection && SelectionContainsPoint(x, y))
        {
            FillWholeSelection();
            return;
        }
        FillAtInternal(x, y);
    }

    private bool SelectionContainsPoint(double x, double y)
    {
        if (_selectionContours.Count == 0) return false;
        using var path = BrushEngine.PathFromContours(_selectionContours);
        return path.Contains((float)x, (float)y);
    }

    /// <summary>Fill every pixel of the current selection, as one undo step.</summary>
    private void FillWholeSelection()
    {
        if (_selectionContours.Count == 0) return;
        if (PaintTargetOrKey() is not { } target) return;
        var scene = Scene;

        var stroke = new Stroke
        {
            Tool = ToolKind.Fill,
            Color = ColorHex,
            SwatchId = ActiveSwatchId,
            Brush = new BrushSettings { Opacity = 1, AntiAlias = AntiAliasing },
            Points = [.. _selectionContours[0]],
            Holes = _selectionContours.Count > 1
                ? _selectionContours.Skip(1).Select(c => c.ToList()).ToList()
                : null,
            Label = "fill-selection",
        };
        if (PrepareClipForSelection() is { } clip) stroke.ClipId = clip.Id;

        FrameRasterizer.Append(_cache.Get(target, scene.Width, scene.Height), stroke);
        _committingScopedEdit = true;
        try
        {
            _editor.Perform(_ => StrokesOf(target).Add(stroke));
        }
        finally
        {
            _committingScopedEdit = false;
        }
        _dirtyThumbIds.Add(target.Id);
        InvalidateWholeCanvas();
        PublishSnapshot();
        RefreshThumbnails();
        AiStatus = $"Filled the selection with {ColorHex}.";
    }

    public void FillAt(double x, double y)
    {
        if (ActiveTool != ToolId.Fill) return;
        FillAtInternal(x, y);
    }

    /// <summary>
    /// The fill itself, without the tool check — a colour dropped on the
    /// canvas fills whatever tool happens to be selected.
    /// </summary>
    private void FillAtInternal(double x, double y)
    {
        if (IsPlaying) return;
        if (!CanEdit(ActiveLayer, "fill on it")) return;
        if (PaintTargetOrKey() is not { } target) return;

        var scene = Scene;
        SKBitmap? owned = null;
        try
        {
            SKBitmap sample;
            if (SmartFill)
            {
                owned = CompositeVisibleLayers();
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
                SwatchId = ActiveSwatchId,
                Brush = new BrushSettings { Opacity = 1, AntiAlias = AntiAliasing },
                Points = result.Outer,
                Holes = result.Holes.Count > 0 ? result.Holes : null,
                Label = "fill",
            };
            var clip = PrepareClipForSelection();
            if (clip is not null) stroke.ClipId = clip.Value.Id;
            var below = FillBelowLines;

            // Fill-above stamps incrementally onto the cached frame; fill-below
            // changes stroke order, so only that path pays a frame re-render.
            if (below)
            {
                _cache.Invalidate(target.Id);
            }
            else
            {
                FrameRasterizer.Append(_cache.Get(target, scene.Width, scene.Height), stroke);
            }

            var frameId = target.Id;
            var addedClip = false;
            _committingScopedEdit = true;
            try
            {
                _editor.PerformDelta(
                    apply: doc =>
                    {
                        if (clip is { } c) addedClip = doc.ClipRegions.TryAdd(c.Id, c.Region);
                        var list = StrokeListIn(doc, frameId);
                        if (list is null) return;
                        if (below) list.Insert(UnderLineWorkIndex(list), stroke);
                        else list.Add(stroke);
                    },
                    revert: doc =>
                    {
                        RemoveStrokeById(doc, frameId, stroke.Id);
                        if (clip is { } c && addedClip) doc.ClipRegions.Remove(c.Id);
                    },
                    affectedFrameId: frameId);
            }
            finally
            {
                _committingScopedEdit = false;
            }
            _dirtyThumbIds.Add(target.Id);
            PublishSnapshot();
            RefreshThumbnails();
        }
        finally
        {
            owned?.Dispose();
        }
    }

    /// <summary>
    /// Every visible layer composited over transparency at the playhead —
    /// "what the eye sees minus the paper". Caller owns the returned bitmap.
    /// </summary>
    private SKBitmap CompositeVisibleLayers()
    {
        var scene = Scene;
        var passes = new List<RenderPass>();
        foreach (var layer in scene.Layers)
        {
            if (!scene.IsLayerVisible(layer)) continue;
            var frame = ExposureSheet.ExposedFrame(layer, CurrentFrameIndex);
            if (frame is null) continue;
            passes.Add(new RenderPass(
                _cache.Get(frame, scene.Width, scene.Height), null, layer.Opacity,
                SceneRenderer.ToSkia(layer.BlendMode)));
        }
        using var image = SceneRenderer.Compose(scene.Width, scene.Height, passes, SkiaSharp.SKColors.Transparent);
        return SKBitmap.FromImage(image);
    }

    // ---- magic wand -------------------------------------------------------------

    [ObservableProperty]
    private double _wandTolerance = 32;

    /// <summary>Openings up to this many pixels read as closed for the wand.</summary>
    [ObservableProperty]
    private double _wandGapPx;

    /// <summary>Sample the composited visible layers instead of only the active one.</summary>
    [ObservableProperty]
    private bool _wandSampleAllLayers = true;

    /// <summary>Magic-wand click: select the connected color region at a document position.</summary>
    public void WandSelectAt(double x, double y, bool add, bool subtract)
    {
        if (ActiveTool != ToolId.Select || IsPlaying) return;
        int w = Scene.Width, h = Scene.Height;
        SKBitmap? owned = null;
        try
        {
            SKBitmap sample;
            if (WandSampleAllLayers)
            {
                owned = CompositeVisibleLayers();
                sample = owned;
            }
            else
            {
                var frame = ExposureSheet.ExposedFrame(ActiveLayer, CurrentFrameIndex);
                if (frame is null)
                {
                    AiStatus = "The active layer has nothing drawn to select here.";
                    return;
                }
                sample = _cache.Get(frame, w, h);
            }

            var result = FloodFill.Fill(
                sample, (int)Math.Round(x), (int)Math.Round(y),
                new FloodFill.Options(WandTolerance, WandGapPx));
            if (result is null)
            {
                AiStatus = "Nothing selectable at that spot.";
                return;
            }
            var contours = new List<List<StrokePoint>> { result.Outer };
            contours.AddRange(result.Holes);
            ApplySelectionMask(MaskFromContours(contours, w, h), add, subtract);
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
        ApplySelectionMask(MaskFromContours([contour], w, h), add, subtract);
    }

    /// <summary>Combine any shape mask into the selection with the standard modifiers.</summary>
    private void ApplySelectionMask(bool[] shape, bool add, bool subtract)
    {
        int w = Scene.Width, h = Scene.Height;
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

    /// <summary>
    /// Ctrl+click on a layer thumbnail: select the layer's visible pixels
    /// (the exposed drawing at the playhead). Shift adds, Alt subtracts.
    /// </summary>
    public void SelectLayerAlpha(LayerRow row, bool add, bool subtract)
    {
        var frame = ExposureSheet.ExposedFrame(row.Layer, CurrentFrameIndex);
        if (frame is null)
        {
            AiStatus = $"Layer “{row.Layer.Name}” has nothing drawn to select here.";
            return;
        }
        int w = Scene.Width, h = Scene.Height;
        var bmp = _cache.Get(frame, w, h);
        var pixels = bmp.Pixels;
        var shape = new bool[w * h];
        var any = false;
        for (var i = 0; i < shape.Length; i++)
        {
            // Low threshold keeps soft brush edges inside the selection.
            if (pixels[i].Alpha > 25)
            {
                shape[i] = true;
                any = true;
            }
        }
        if (!any)
        {
            AiStatus = $"Layer “{row.Layer.Name}” has nothing drawn to select here.";
            return;
        }
        ApplySelectionMask(shape, add, subtract);
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

    /// <summary>Arrow keys over the canvas: shift the selection outline by whole pixels.</summary>
    public void NudgeSelection(int dx, int dy)
    {
        if (!HasSelection || (dx == 0 && dy == 0)) return;
        foreach (var contour in _selectionContours)
        {
            for (var i = 0; i < contour.Count; i++)
            {
                var p = contour[i];
                contour[i] = p with { X = p.X + dx, Y = p.Y + dy };
            }
        }
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

    // ---- stroke stabilizer (input smoothing) -----------------------------------

    private readonly StrokeStabilizer _stabilizer = new();

    public IReadOnlyList<SmoothingMode> SmoothingChoices { get; } = Enum.GetValues<SmoothingMode>();

    public SmoothingMode SmoothingMode
    {
        get => _stabilizer.Mode;
        set
        {
            if (_stabilizer.Mode == value) return;
            _stabilizer.Mode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SmoothStrokes));
            OnPropertyChanged(nameof(IsWindowSmoothing));
            OnPropertyChanged(nameof(IsEmaSmoothing));
            OnPropertyChanged(nameof(IsPulledStringSmoothing));
            OnPropertyChanged(nameof(LazyRadiusForCursor));
            PersistBrushState();
        }
    }

    /// <summary>Compat view (old XAML/tests): on = the classic light smoothing.</summary>
    public bool SmoothStrokes
    {
        get => SmoothingMode != SmoothingMode.Off;
        set => SmoothingMode = value ? SmoothingMode.Laplacian : SmoothingMode.Off;
    }

    public bool IsWindowSmoothing =>
        SmoothingMode is SmoothingMode.MovingAverage or SmoothingMode.SavitzkyGolay;

    public bool IsEmaSmoothing => SmoothingMode == SmoothingMode.Ema;

    public bool IsPulledStringSmoothing => SmoothingMode == SmoothingMode.PulledString;

    public double SmoothingWindow
    {
        get => _stabilizer.Window;
        set
        {
            var window = Math.Clamp((int)Math.Round(value) | 1, 3, 25);
            if (_stabilizer.Window == window) return;
            _stabilizer.Window = window;
            OnPropertyChanged();
            PersistBrushState();
        }
    }

    public double SmoothingStrength
    {
        get => _stabilizer.Strength;
        set
        {
            var strength = Math.Clamp(value, 0, 0.95);
            if (Math.Abs(_stabilizer.Strength - strength) < 0.001) return;
            _stabilizer.Strength = strength;
            OnPropertyChanged();
            PersistBrushState();
        }
    }

    public double LazyRadius
    {
        get => _stabilizer.LazyRadius;
        set
        {
            var radius = Math.Clamp(value, 4, 200);
            if (Math.Abs(_stabilizer.LazyRadius - radius) < 0.5) return;
            _stabilizer.LazyRadius = radius;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LazyRadiusForCursor));
            PersistBrushState();
        }
    }

    /// <summary>Pulled-string dead-zone radius for the canvas gizmo (0 = hidden).</summary>
    public double LazyRadiusForCursor =>
        SmoothingMode == SmoothingMode.PulledString && IsPaintTool ? LazyRadius : 0;

    /// <summary>Raised while painting with a live smoothing mode: the smoothed brush anchor moved.</summary>
    public event Action<double, double>? LazyBrushMoved;

    /// <summary>Raised when the stroke ends and the gizmo anchor should clear.</summary>
    public event Action? LazyBrushCleared;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private int _activeLayerIndex;

    [ObservableProperty]
    private bool _sidebarVisible = true;

    [ObservableProperty]
    private bool _colorDockerVisible = true;

    [ObservableProperty]
    private bool _sheetsDockerVisible = true;

    /// <summary>
    /// Off by default. A palette is something an artist sets up deliberately;
    /// a document with none has an empty docker taking sidebar height it could
    /// give to the layers.
    /// </summary>
    [ObservableProperty]
    private bool _paletteDockerVisible;

    [RelayCommand]
    private void TogglePaletteDocker() => PaletteDockerVisible = !PaletteDockerVisible;

    /// <summary>Off by default, for the same reason the palette docker is.</summary>
    [ObservableProperty]
    private bool _gradientDockerVisible;

    [RelayCommand]
    private void ToggleGradientDocker() => GradientDockerVisible = !GradientDockerVisible;

    [RelayCommand]
    private void ToggleColorDocker() => ColorDockerVisible = !ColorDockerVisible;

    [RelayCommand]
    private void ToggleSheetsDocker() => SheetsDockerVisible = !SheetsDockerVisible;

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
        NotifyActiveLayerCompositing();
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
        RefreshLayerThumbs();
        RefreshCamera();
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

    /// <summary>
    /// The frame a mark is about to land on, <b>keying the cel if there is
    /// nothing to land on</b>.
    ///
    /// Clearing every cel on a layer used to make it permanently undrawable:
    /// with no key at or before the playhead, <see cref="PaintTarget"/>
    /// returned null and every tool returned silently. Drawing where there is
    /// no drawing is the ordinary way to start one — every animation tool
    /// auto-keys on the first mark — and silence was the worst part of it.
    ///
    /// The new cel is a separate undo step from the stroke that prompted it,
    /// so one undo takes the mark back and a second takes the cel away.
    /// </summary>
    private Frame? PaintTargetOrKey()
    {
        if (PaintTarget() is { } existing) return existing;
        if (ActiveLayer is not { } layer || layer.Cels.Count == 0) return null;

        var index = Math.Clamp(CurrentFrameIndex, 0, layer.Cels.Count - 1);
        var layerId = layer.Id;
        Frame fresh = layer.Kind == LayerKind.Vector ? new VectorFrame() : new PaintedFrame();
        _editor.PerformDelta(
            apply: doc =>
            {
                if (CelIn(doc, layerId, index) is { } cel) cel.Frame = fresh;
            },
            revert: doc =>
            {
                if (CelIn(doc, layerId, index) is { } cel) cel.Frame = null;
            });
        return PaintTarget();
    }

    private static Cel? CelIn(Doc doc, string layerId, int index)
    {
        var layer = doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId);
        return layer is not null && index < layer.Cels.Count ? layer.Cels[index] : null;
    }

    /// <summary>One pointer sample in document space.</summary>
    public readonly record struct PointerSample(double X, double Y, double Pressure);

    /// <summary>
    /// Live report of what the OS delivers while drawing, shown on the
    /// Pen-pressure settings page. "Pen detected — pressure 0.63" means
    /// the tablet works; a Mouse report from a pen means the tablet driver
    /// isn't exposing the pen to Windows Ink (enable "Windows Ink" in the
    /// Huion/Wacom driver settings).
    /// </summary>
    [ObservableProperty]
    private string _penDiagnostic = "No input seen yet — draw a stroke.";

    // Live-preview state: a persistent copy of the target frame that only the
    // NEW segment of the stroke gets stamped into per pointer event — this is
    // what keeps painting O(stroke length) instead of O(length²).
    // Whole-stroke dab accumulator, WITHOUT the stroke's opacity — the
    // compositor lays it over the layer and applies opacity once, so a
    // self-crossing stroke looks the same live as committed. Pooled across
    // strokes: at 4K this bitmap is 33 MB, far too big to allocate per stroke.
    private SKBitmap? _liveScratch;
    private SKCanvas? _liveScratchCanvas;

    // Region of the scratch actually touched by the current stroke, so
    // pen-up can clear just that much instead of the whole canvas.
    private SKRectI? _liveScratchUsed;

    // Blur brushes read the canvas underneath them, so they cannot be
    // composited from a separate scratch — they keep the copy-based path.
    private SKBitmap? _liveComposite;
    private int _liveStampedCount;
    private bool _snapshotQueued;

    // ---- live post-processing (medium, wet edge, texture, granulation) --------
    //
    // These are STROKE-GLOBAL: the wet edge is derived from the whole
    // silhouette and the fluid lattice flows across the whole wet area, so
    // running them per segment would rim and pool each segment separately —
    // visibly wrong, and not what commits. They have to be recomputed over the
    // whole stroke so far, which at 45–143 ms on a 4K canvas cannot happen on
    // every pointer event.
    //
    // So: raw dabs go into _liveScratch immediately (2 ms, the pen never
    // lags), and a full render of the stroke-so-far lands in _livePostScratch
    // as often as its own measured cost allows. The compositor shows the
    // rendered one when it exists. The artist sees the true mark converging a
    // fraction behind the tip rather than seeing flat dabs until pen-up, which
    // is how wet media behave in every tool that has them.
    private SKBitmap? _livePostScratch;
    private SKRectI? _livePostUsed;
    /// <summary>Cost of the last pass, milliseconds — reported by the performance panel.</summary>
    private double _livePostCostMs;
    private int _livePostStampedCount = -1;
    private bool _livePostQueued;

    /// <summary>How many times the live post-process has rendered. Tests only.</summary>
    internal int LivePostPasses { get; private set; }

    /// <summary>Total milliseconds spent in those passes. Tests only.</summary>
    internal double LivePostTotalMs { get; private set; }

    /// <summary>
    /// Effects that cannot be applied per segment because they read the whole
    /// stroke. Texture and granulation are pointwise and could be incremental,
    /// but they are cheap enough to come along for the ride.
    /// </summary>
    private static bool NeedsLivePostProcess(BrushSettings brush) =>
        brush.Medium.Kind != MediumKind.None
        || brush.WetEdge > 0
        || brush.TextureSurface is not null
        || brush.Granulation > 0;

    public void BeginStroke(double x, double y, double pressure) =>
        BeginStroke(x, y, pressure, eraseWithCurrentBrush: false);

    /// <param name="eraseWithCurrentBrush">
    /// Alt was held. The stroke erases but keeps the brush's own size, shape
    /// and dynamics — unlike switching to the eraser, which brings its own.
    /// </param>
    public void BeginStroke(double x, double y, double pressure, bool eraseWithCurrentBrush)
    {
        if (ActiveTool is not (ToolId.Brush or ToolId.Eraser)) return;
        if (IsPlaying) return;
        if (!CanEdit(ActiveLayer, "draw on it")) return;
        if (PaintTargetOrKey() is not { } target) return;
        // Drawing ends any run of palette edits, so the recolour lands on the
        // undo stack before the stroke does rather than after it.
        CommitSwatchEdit();
        _stabilizer.Begin(x, y);
        _strokeBuilder.Begin(
            IsEraser || eraseWithCurrentBrush ? ToolKind.Eraser : ToolKind.Brush,
            ColorHex,
            CurrentToolSettings.Clone(),
            x, y, pressure,
            ActiveSwatchId);
        // Live preview clips to the selection too (the registry already knows
        // the region; the document copy is added at commit).
        if (PrepareClipForSelection() is { } liveClip) _strokeBuilder.Current!.ClipId = liveClip.Id;
        // Stamped onto the stroke, not read from the layer at render time, so
        // unlocking the layer later cannot repaint what is already down.
        _strokeBuilder.Current!.AlphaLocked = ActiveLayer.AlphaLocked;

        _liveComposite?.Dispose();
        _liveComposite = null;
        if (CurrentToolSettings.Kind is BrushKind.Blur or BrushKind.Smudge)
        {
            // Blur and smudge read the pixels they sit on, so they need a real
            // copy of the layer to work into. Without this a smudge preview
            // stamps plain dabs of the foreground colour for the whole drag
            // and only snaps to the real smear on pen-up.
            _liveComposite = _cache.Get(target, Scene.Width, Scene.Height).Copy();
        }
        else
        {
            EnsureLiveScratch();
            ClearLiveScratch();
        }
        ResetLivePostProcess();
        _liveStampedCount = 0;
        FlushLivePreview();
        PublishSnapshot();
    }

    /// <summary>A document-sized scratch bitmap for the live preview overlay.</summary>
    private void EnsureLiveScratch()
    {
        if (_liveScratch is not null && _liveScratch.Width == Scene.Width && _liveScratch.Height == Scene.Height)
        {
            return;
        }
        _liveScratchCanvas?.Dispose();
        _liveScratch?.Dispose();
        _liveScratch = new SKBitmap(
            new SKImageInfo(Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        _liveScratchCanvas = new SKCanvas(_liveScratch);
        _liveScratchUsed = null;
    }

    /// <summary>Wipe only the region the previous stroke actually touched.</summary>
    private void ClearLiveScratch()
    {
        if (_liveScratchCanvas is null) return;
        if (_liveScratchUsed is not { } used)
        {
            _liveScratchCanvas.Clear(SkiaSharp.SKColors.Transparent);
            return;
        }
        _liveScratchCanvas.Save();
        _liveScratchCanvas.ClipRect(SKRect.Create(used.Left, used.Top, used.Width, used.Height));
        _liveScratchCanvas.Clear(SkiaSharp.SKColors.Transparent);
        _liveScratchCanvas.Restore();
        _liveScratchUsed = null;
    }

    // ---- gradient tool ------------------------------------------------------

    /// <summary>
    /// The gradient being dragged. Two points — the axis — and no incremental
    /// state, so it stays out of the brush's stamped-so-far machinery: a
    /// gradient is not built up along the drag, it is redefined by it.
    /// </summary>
    private Stroke? _liveGradient;

    /// <summary>The axis the canvas overlay draws while dragging (document coordinates).</summary>
    public event Action<(double X, double Y)?, (double X, double Y)?>? GradientAxisChanged;

    internal Stroke? LiveGradient => _liveGradient;

    public void BeginGradient(double x, double y)
    {
        if (ActiveTool != ToolId.Gradient || IsPlaying) return;
        if (!CanEdit(ActiveLayer, "fill on it") || PaintTargetOrKey() is null) return;
        if (GradientDocker.SelectedGradient is not { } gradient)
        {
            AiStatus = "No gradient selected — add one in the Gradient docker first.";
            return;
        }
        CommitSwatchEdit();

        _liveGradient = new Stroke
        {
            Tool = ToolKind.Gradient,
            GradientId = gradient.Id,
            Color = ColorHex,
            Brush = new BrushSettings { Opacity = GradientOpacity, AntiAlias = AntiAliasing },
            Points = [new StrokePoint(x, y, 1), new StrokePoint(x, y, 1)],
            // Stamped onto the stroke like a brush stroke's, so unlocking the
            // layer later cannot repaint what is already down.
            AlphaLocked = ActiveLayer.AlphaLocked,
            Label = "gradient",
        };
        if (PrepareClipForSelection() is { } clip) _liveGradient.ClipId = clip.Id;

        EnsureLiveScratch();
        RenderGradientPreview();
        PublishSnapshot();
    }

    public void MoveGradient(double x, double y)
    {
        if (_liveGradient is not { } stroke) return;
        stroke.Points[1] = new StrokePoint(x, y, 1);
        RenderGradientPreview();
        RequestSnapshot();
    }

    public void EndGradient(double x, double y)
    {
        if (_liveGradient is not { } stroke) return;
        stroke.Points[1] = new StrokePoint(x, y, 1);
        CancelGradient(); // clears the preview; the record gets the stroke below

        if (PaintTarget() is not { } target) return;
        var dx = stroke.Points[1].X - stroke.Points[0].X;
        var dy = stroke.Points[1].Y - stroke.Points[0].Y;
        // A click with no drag has no axis. Committing it would paint a
        // degenerate shader over the whole layer, which is never the intent.
        if (dx * dx + dy * dy < 1.0)
        {
            AiStatus = "Drag to set the gradient's direction and length.";
            return;
        }

        var clip = PrepareClipForSelection();
        if (clip is not null) stroke.ClipId = clip.Value.Id;

        FrameRasterizer.Append(_cache.Get(target, Scene.Width, Scene.Height), stroke);

        var frameId = target.Id;
        var addedClip = false;
        _committingScopedEdit = true;
        try
        {
            _editor.PerformDelta(
                apply: doc =>
                {
                    if (clip is { } c) addedClip = doc.ClipRegions.TryAdd(c.Id, c.Region);
                    StrokeListIn(doc, frameId)?.Add(stroke);
                },
                revert: doc =>
                {
                    RemoveStrokeById(doc, frameId, stroke.Id);
                    if (clip is { } c && addedClip) doc.ClipRegions.Remove(c.Id);
                },
                affectedFrameId: frameId);
        }
        finally
        {
            _committingScopedEdit = false;
        }
        _dirtyThumbIds.Add(target.Id);
        InvalidateWholeCanvas();
        PublishSnapshot();
        RefreshThumbnails();
        AiStatus = $"Laid down “{GradientDocker.SelectedGradient?.Name}”.";
    }

    /// <summary>Abandon the drag — Escape, or capture lost.</summary>
    public void CancelGradient()
    {
        if (_liveGradient is null) return;
        _liveGradient = null;
        ClearLiveScratch();
        GradientAxisChanged?.Invoke(null, null);
        InvalidateWholeCanvas();
        PublishSnapshot();
    }

    /// <summary>
    /// Re-render the whole preview rather than an increment. A gradient is
    /// full-canvas by nature — one shader-filled rect, which Skia does in a
    /// single native pass — and every pointer move redefines the axis, so
    /// there is nothing from the previous frame worth keeping.
    /// </summary>
    private void RenderGradientPreview()
    {
        if (_liveGradient is not { } stroke || _liveScratchCanvas is null) return;
        ClearLiveScratch();
        var info = new SKImageInfo(Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        // Opacity and the alpha lock stay on the overlay so they are not baked
        // in twice; the scratch holds the unmodulated ramp.
        var preview = new Stroke
        {
            Tool = ToolKind.Gradient,
            GradientId = stroke.GradientId,
            Color = stroke.Color,
            ClipId = stroke.ClipId,
            Brush = new BrushSettings { Opacity = 1, AntiAlias = stroke.Brush.AntiAlias },
            Points = [.. stroke.Points],
        };
        BrushEngine.StampStroke(_liveScratchCanvas, preview, info);
        _liveScratchCanvas.Flush();
        _liveScratchUsed = new SKRectI(0, 0, Scene.Width, Scene.Height);
        GradientAxisChanged?.Invoke(
            (stroke.Points[0].X, stroke.Points[0].Y), (stroke.Points[1].X, stroke.Points[1].Y));
        InvalidateWholeCanvas();
    }

    /// <summary>All coalesced samples of one pointer event → one stamp + one (coalesced) repaint.</summary>
    public void MoveStrokeBatch(IReadOnlyList<PointerSample> samples)
    {
        if (!_strokeBuilder.IsActive) return;
        foreach (var s in samples)
        {
            var (x, y) = _stabilizer.FilterLive(s.X, s.Y);
            _strokeBuilder.Add(x, y, s.Pressure);
        }
        if (_stabilizer.BrushPosition is { } anchor) LazyBrushMoved?.Invoke(anchor.X, anchor.Y);
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
        if (_strokeBuilder.Current is not { } live) return;
        var points = live.Points;
        if (_liveStampedCount >= points.Count) return;

        var from = Math.Max(0, _liveStampedCount - 1); // overlap one point so segments connect
        var tail = new Stroke
        {
            Tool = live.Tool,
            Color = live.Color,
            SwatchId = live.SwatchId,
            Brush = live.Brush,
            ClipId = live.ClipId,
            AlphaLocked = live.AlphaLocked,
            Points = points.Skip(from).ToList(),
        };
        var info = new SKImageInfo(Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var segment = BrushEngine.DraftSegmentBounds(tail, info);

        if (_liveComposite is not null)
        {
            FrameRasterizer.AppendDraft(_liveComposite, tail); // blur path
        }
        else if (_liveScratchCanvas is not null)
        {
            // Dabs only — no opacity, no layer copy. The compositor lays the
            // scratch over the layer and applies the stroke's opacity once,
            // so self-crossings look identical live and committed.
            BrushEngine.StampDraftDabs(_liveScratchCanvas, tail);
            if (segment is { } used)
            {
                _liveScratchUsed = _liveScratchUsed is { } prior ? UnionRect(prior, used) : used;
            }
        }
        // Only the segment's neighbourhood changed on screen.
        if (segment is { } rect) MarkDirtyRegion(rect);
        else InvalidateWholeCanvas();
        _liveStampedCount = points.Count;

        if (_liveComposite is null && NeedsLivePostProcess(live.Brush)) RequestLivePostProcess();
    }

    /// <summary>
    /// Ask for a re-render of the stroke so far.
    ///
    /// Scheduling is left to the dispatcher rather than to a wall-clock
    /// throttle of our own. Background priority yields to pointer input and to
    /// the Default-priority snapshot, so during a fast drag the pass runs in
    /// whatever gaps exist and during a pause it runs immediately — which is
    /// exactly the cadence wanted, and the dispatcher already knows how busy
    /// the thread is. A cost-based throttle was tried first and was worse in
    /// the way that matters: it blocked the pass that would have settled the
    /// preview, so the mark froze part-drawn until the pen lifted.
    ///
    /// Only one pass is ever outstanding, and a pass with nothing new to draw
    /// returns immediately.
    /// </summary>
    private void RequestLivePostProcess()
    {
        if (_livePostQueued) return;
        _livePostQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(
            RenderLivePostProcess, Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// The whole stroke so far, rendered exactly as it will commit — minus the
    /// stroke opacity and the masks, which the compositor still applies once.
    /// </summary>
    private void RenderLivePostProcess()
    {
        _livePostQueued = false;
        if (_strokeBuilder.Current is not { } live || !_strokeBuilder.IsActive) return;
        if (!NeedsLivePostProcess(live.Brush)) return;
        if (_livePostStampedCount == live.Points.Count) return; // nothing new since last pass
        // The pass reads the dabs from the live scratch; the blur and smudge
        // brushes use the copy-based path instead and have none.
        if (_liveScratch is not { } dabs) return;

        var info = new SKImageInfo(Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        if (_livePostScratch is null || _livePostScratch.Width != info.Width || _livePostScratch.Height != info.Height)
        {
            _livePostScratch?.Dispose();
            _livePostScratch = new SKBitmap(info);
            _livePostUsed = null;
        }

        // The same stroke, minus the opacity the compositor applies — the
        // masks stay on the overlay so they cannot be baked in twice.
        var whole = new Stroke
        {
            Tool = live.Tool,
            Color = live.Color,
            SwatchId = live.SwatchId,
            Brush = live.Brush,
            Points = [.. live.Points],
        };

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        using (var canvas = new SKCanvas(_livePostScratch))
        {
            ClearRegion(canvas, _livePostUsed);
        }
        // The dabs are already in the live scratch, so the pass runs the
        // effects over them rather than re-stamping every dab — the cost of a
        // pass stops growing with the length of the stroke.
        // targetPixels is the committed layer: the medium re-wets what is
        // already there, exactly as it will on commit.
        var beneath = PaintTarget() is { } frame ? _cache.Get(frame, Scene.Width, Scene.Height) : null;
        var bounds = BrushEngine.PostProcessDabs(dabs, _livePostScratch, whole, info, beneath);

        _livePostCostMs = (System.Diagnostics.Stopwatch.GetTimestamp() - started)
                          * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        _livePostStampedCount = live.Points.Count;
        _livePostUsed = bounds;
        LivePostPasses++;
        LivePostTotalMs += _livePostCostMs;

        if (bounds is { } rect) MarkDirtyRegion(rect);
        else InvalidateWholeCanvas();
        // Through the coalescing path, not straight to PublishSnapshot. A
        // direct publish here put an extra frame on the wire for every pass on
        // top of the one the pointer event had already queued, and publishing
        // faster than the compositor draws is what let the canvas free an
        // image out from under it.
        RequestSnapshot();

        // Points arrived while this pass was rendering: go round again so the
        // preview settles on the whole stroke rather than stopping wherever
        // the pen happened to be when the pass started.
        if (_strokeBuilder.Current is { } now && now.Points.Count != _livePostStampedCount)
        {
            RequestLivePostProcess();
        }
    }

    private static void ClearRegion(SKCanvas canvas, SKRectI? region)
    {
        if (region is not { } used)
        {
            canvas.Clear(SKColors.Transparent);
            return;
        }
        canvas.Save();
        canvas.ClipRect(SKRect.Create(used.Left, used.Top, used.Width, used.Height));
        canvas.Clear(SKColors.Transparent);
        canvas.Restore();
    }

    private void ResetLivePostProcess()
    {
        if (_livePostScratch is not null && _livePostUsed is not null)
        {
            using var canvas = new SKCanvas(_livePostScratch);
            ClearRegion(canvas, _livePostUsed);
        }
        _livePostUsed = null;
        _livePostCostMs = 0;
        _livePostStampedCount = -1;
    }

    private static SKRectI UnionRect(SKRectI a, SKRectI b)
    {
        a.Union(b);
        return a;
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
        ResetLivePostProcess();
        if (stroke is null) return;
        var target = PaintTarget();
        if (target is null) return;

        _stabilizer.End();
        LazyBrushCleared?.Invoke();
        stroke.Points = _stabilizer.PostProcess(stroke.Points);

        // A stroke painted under a selection carries it forever (provenance).
        var clip = PrepareClipForSelection();
        if (clip is not null) stroke.ClipId = clip.Value.Id;

        // Commit the pixels incrementally: stamp the EXACT stroke onto the
        // cached frame bitmap instead of invalidating it — invalidation would
        // replay every stroke in the frame, which is why lifting the pen used
        // to pause on drawings with many strokes. Appending the exact stroke
        // to the previously exact bitmap is the same sequence Materialize
        // would run, so the pixels stay bit-identical.
        var cached = _cache.Get(target, Scene.Width, Scene.Height); // pre-stroke state (record not yet updated)
        FrameRasterizer.Append(cached, stroke);

        // Undo without snapshotting the whole document (the other pen-lift
        // pause). The frame is resolved by id at apply/revert time: a
        // snapshot-undo in between replaces the doc instance tree.
        var frameId = target.Id;
        var addedClip = false;
        _committingScopedEdit = true;
        try
        {
            _editor.PerformDelta(
                apply: doc =>
                {
                    if (clip is { } c) addedClip = doc.ClipRegions.TryAdd(c.Id, c.Region);
                    StrokeListIn(doc, frameId)?.Add(stroke);
                },
                revert: doc =>
                {
                    RemoveStrokeById(doc, frameId, stroke.Id);
                    if (clip is { } c && addedClip) doc.ClipRegions.Remove(c.Id);
                },
                affectedFrameId: frameId);
        }
        finally
        {
            _committingScopedEdit = false;
        }
        _dirtyThumbIds.Add(target.Id);
        // Only the stroke's own neighbourhood changed: the layer gained the
        // committed pixels and the live scratch stopped contributing there.
        var commitInfo = new SKImageInfo(Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        if (BrushEngine.CommitBounds(stroke, commitInfo) is { } touched) MarkDirtyRegion(touched);
        else InvalidateWholeCanvas();
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

    // ---- exposure editing + cel clipboard --------------------------------------

    private Layer? LayerOfCell(FrameCell cell) =>
        cell.LayerIndex >= 0 && cell.LayerIndex < Scene.Layers.Count ? Scene.Layers[cell.LayerIndex] : null;

    public void ExtendExposureAt(FrameCell cell)
    {
        if (LayerOfCell(cell) is not { } layer) return;
        _editor.ExtendExposure(layer.Id, cell.Index);
    }

    public void ReduceExposureAt(FrameCell cell)
    {
        if (LayerOfCell(cell) is not { } layer) return;
        _editor.ReduceExposure(layer.Id, cell.Index);
    }

    /// <summary>Frames each drawing is held for by the two re-timing commands.</summary>
    [ObservableProperty]
    private int _exposureStep = 2;

    partial void OnExposureStepChanged(int value)
    {
        if (value is < 1 or > 8) ExposureStep = Math.Clamp(value, 1, 8);
    }

    /// <summary>
    /// Hold every drawing in the range for <see cref="ExposureStep"/> frames.
    /// The range gets longer and nothing is lost — this is what "animate on
    /// 2s" means to an animator.
    /// </summary>
    public void StretchExposureAt(FrameCell cell)
    {
        if (LayerOfCell(cell) is not { } layer) return;
        if (!CanEdit(layer, "re-time it")) return;
        var (start, end) = OpRangeFor(cell);
        var grew = _editor.StretchExposure(layer.Id, start, end, ExposureStep);
        AfterRetime(layer);
        AiStatus = grew > 0
            ? $"Stretched to {ExposureStep}s — the range grew by {grew} frame{(grew == 1 ? "" : "s")}."
            : "Nothing to stretch in that range.";
    }

    /// <summary>
    /// Keep every <see cref="ExposureStep"/>-th drawing and discard the rest,
    /// holding what survives so the range keeps its length. Destructive, which
    /// is why it is a separate command rather than a mode on the first.
    /// </summary>
    public void ReduceToStepAt(FrameCell cell)
    {
        if (LayerOfCell(cell) is not { } layer) return;
        if (!CanEdit(layer, "re-time it")) return;
        var (start, end) = OpRangeFor(cell);
        var dropped = _editor.ReduceToStep(layer.Id, start, end, Math.Max(2, ExposureStep));
        AfterRetime(layer);
        AiStatus = dropped > 0
            ? $"Reduced to {Math.Max(2, ExposureStep)}s — discarded {dropped} drawing{(dropped == 1 ? "" : "s")}."
            : "Nothing to reduce in that range.";
    }

    private void AfterRetime(Layer layer)
    {
        foreach (var cel in layer.Cels)
        {
            if (cel.Frame is { } frame) _dirtyThumbIds.Add(frame.Id);
        }
        SyncLayerRows();
        InvalidateWholeCanvas();
        PublishSnapshot();
        RefreshThumbnails();
        MarkDocumentEdited();
    }

    [RelayCommand]
    private void StretchSelectedExposure()
    {
        if (CurrentCell() is { } cell) StretchExposureAt(cell);
    }

    [RelayCommand]
    private void ReduceSelectedExposure()
    {
        if (CurrentCell() is { } cell) ReduceToStepAt(cell);
    }

    /// <summary>Clear the drawing(s) at the cell — or the whole selected range when the cell is inside it.</summary>
    /// <summary>
    /// Delete the cel (or the selected range) and pull the rest of the row
    /// back. "Clear cel" blanks a drawing and keeps its slot; this removes the
    /// slot, which is the operation the timeline was missing entirely.
    /// </summary>
    public void DeleteCelAt(FrameCell cell)
    {
        if (LayerOfCell(cell) is not { } layer) return;
        if (!CanEdit(layer, "delete a cel on it")) return;
        var (start, end) = OpRangeFor(cell);
        _editor.DeleteCels(layer.Id, start, end);
        _allThumbsDirty = true;
        RefreshThumbnails();
    }

    public void ClearCelAt(FrameCell cell)
    {
        if (LayerOfCell(cell) is not { } layer) return;
        if (!CanEdit(layer, "clear a cel on it")) return;
        var (start, end) = OpRangeFor(cell);
        if (start == end && ExposureSheet.FrameAtExactIndex(layer, cell.Index) is null)
        {
            AiStatus = "That cel is a hold — there is no drawing to clear.";
            return;
        }
        _editor.ClearCels(layer.Id, start, end);
        RefreshThumbnails();
    }

    /// <summary>App-internal cel clipboard: a cel sequence (null = hold) + its source layer kind.</summary>
    private (List<Frame?> Frames, LayerKind Kind)? _celClipboard;

    public bool HasCelClipboard => _celClipboard is not null;

    /// <summary>
    /// Copy the cell — or the whole Shift+click range when the cell is inside
    /// it. A single hold cel copies the drawing it shows; ranges copy cels
    /// verbatim, holds included, so timing survives the round trip.
    /// </summary>
    public void CopyCel(FrameCell cell)
    {
        if (LayerOfCell(cell) is not { } layer) return;
        var (start, end) = OpRangeFor(cell);
        List<Frame?> frames;
        if (start == end)
        {
            var exposed = ExposureSheet.ExposedFrame(layer, cell.Index);
            if (exposed is null)
            {
                AiStatus = "Nothing to copy — the cel is empty.";
                return;
            }
            frames = [DocumentEditor.CloneFrame(exposed)];
        }
        else
        {
            frames = [];
            for (var i = start; i <= end; i++)
            {
                frames.Add(DocumentEditor.CloneFrame(ExposureSheet.FrameAtExactIndex(layer, i)));
            }
        }
        _celClipboard = (frames, layer.Kind);
        OnPropertyChanged(nameof(HasCelClipboard));
        AiStatus = frames.Count == 1 ? "Cel copied." : $"{frames.Count} cels copied.";
    }

    public void CutCel(FrameCell cell)
    {
        if (LayerOfCell(cell) is not { } layer) return;
        if (!CanEdit(layer, "cut a cel from it")) return;
        var (start, end) = OpRangeFor(cell);
        if (start == end && ExposureSheet.FrameAtExactIndex(layer, cell.Index) is null)
        {
            AiStatus = "Nothing to cut — the cel is a hold.";
            return;
        }
        CopyCel(cell);
        _editor.ClearCels(layer.Id, start, end);
        RefreshThumbnails();
    }

    /// <summary>Paste the copied cel(s) starting at the cell (holds paste as holds).</summary>
    public void PasteCel(FrameCell cell)
    {
        if (_celClipboard is not { } clip)
        {
            AiStatus = "The cel clipboard is empty.";
            return;
        }
        if (LayerOfCell(cell) is not { } layer) return;
        if (!CanEdit(layer, "paste onto it")) return;

        var frames = new List<Frame?>(clip.Frames.Count);
        foreach (var source in clip.Frames)
        {
            var frame = DocumentEditor.CloneFrame(source); // fresh id per paste
            if (frame is not null && layer.Kind != clip.Kind)
            {
                // Strokes carry over between kinds; baseline pixels cannot become vector.
                if (layer.Kind == LayerKind.Vector && frame is PaintedFrame p)
                {
                    if (!string.IsNullOrEmpty(p.PngBase64))
                    {
                        AiStatus = "Can't paste onto a vector layer: the copied cel carries baseline pixels.";
                        return;
                    }
                    frame = new VectorFrame { Role = p.Role, Strokes = p.Strokes };
                }
                else if (layer.Kind == LayerKind.Painted && frame is VectorFrame v)
                {
                    frame = new PaintedFrame { Role = v.Role, Strokes = v.Strokes };
                }
            }
            frames.Add(frame);
        }
        _editor.SetFrameRange(layer.Id, cell.Index, frames);
        ActiveLayerIndex = cell.LayerIndex;
        CurrentFrameIndex = Math.Min(cell.Index, Scene.FrameCount - 1);
    }

    /// <summary>Ctrl+C/X/V target: the active layer's cel at the playhead.</summary>
    private FrameCell? CurrentCell()
    {
        var row = LayerRows.FirstOrDefault(r => r.SceneIndex == ActiveLayerIndex);
        return row?.Cells.FirstOrDefault(c => c.Index == CurrentFrameIndex);
    }

    public void CopyCurrentCel()
    {
        if (CurrentCell() is { } cell) CopyCel(cell);
    }

    public void CutCurrentCel()
    {
        if (CurrentCell() is { } cell) CutCel(cell);
    }

    public void PasteCurrentCel()
    {
        if (CurrentCell() is { } cell) PasteCel(cell);
    }

    // ---- transform tool (Ctrl+T) ------------------------------------------------

    [ObservableProperty]
    private TransformScope _transformScope = TransformScope.ActiveCel;

    public IReadOnlyList<TransformScope> TransformScopeChoices { get; } = Enum.GetValues<TransformScope>();

    /// <summary>Pixel solver for raster baselines (strokes never resample).</summary>
    [ObservableProperty]
    private TransformSampling _transformSampling = TransformSampling.Bilinear;

    public IReadOnlyList<TransformSampling> TransformSamplingChoices { get; } = Enum.GetValues<TransformSampling>();

    [ObservableProperty]
    private bool _transformActive;

    private readonly List<Frame> _transformFrames = [];
    private Func<Stroke, bool>? _transformFilter;

    /// <summary>Session started/restarted: bounds of the transformable content (doc space).</summary>
    public event Action<double, double, double, double>? TransformBegun;

    public event Action? TransformEnded;

    /// <summary>Start (or restart) a transform session over the current scope.</summary>
    public bool BeginTransform()
    {
        if (!CanEdit(ActiveLayer, "transform it")) return false;
        var frames = CollectTransformFrames();
        Func<Stroke, bool>? filter = null;
        if (HasSelection)
        {
            int w = Scene.Width, h = Scene.Height;
            var mask = MaskFromContours(_selectionContours, w, h);
            filter = s => TransformOps.MajorityInside(s, mask, w, h);
        }
        var bounds = TransformOps.Bounds(frames, filter);
        if (frames.Count == 0 || bounds is null)
        {
            AiStatus = "Nothing to transform in this scope.";
            if (TransformActive) CancelTransform();
            return false;
        }
        _transformFrames.Clear();
        _transformFrames.AddRange(frames);
        _transformFilter = filter;
        TransformActive = true;
        var b = bounds.Value;
        TransformBegun?.Invoke(b.MinX, b.MinY, b.MaxX, b.MaxY);
        return true;
    }

    partial void OnTransformScopeChanged(TransformScope value)
    {
        // Changing scope mid-session re-collects under the new scope.
        if (TransformActive) BeginTransform();
    }

    /// <summary>Distinct drawings in scope (holds share Frame instances — dedupe by id).</summary>
    private List<Frame> CollectTransformFrames()
    {
        var frames = new List<Frame>();
        var seen = new HashSet<string>();
        void Add(Frame? f)
        {
            if (f is not null && seen.Add(f.Id)) frames.Add(f);
        }
        switch (TransformScope)
        {
            case TransformScope.ActiveCel:
                Add(ExposureSheet.ExposedFrame(ActiveLayer, CurrentFrameIndex));
                break;
            case TransformScope.AllLayersAtFrame:
                foreach (var layer in Scene.Layers)
                {
                    if (Scene.IsLayerVisible(layer)) Add(ExposureSheet.ExposedFrame(layer, CurrentFrameIndex));
                }
                break;
            case TransformScope.CelRange:
                if (_celRange is { } r && r.Layer >= 0 && r.Layer < Scene.Layers.Count)
                {
                    var layer = Scene.Layers[r.Layer];
                    for (var i = r.Start; i <= r.End; i++) Add(ExposureSheet.ExposedFrame(layer, i));
                }
                else
                {
                    Add(ExposureSheet.ExposedFrame(ActiveLayer, CurrentFrameIndex)); // no range marked
                }
                break;
            case TransformScope.EntireAnimation:
                foreach (var layer in Scene.Layers)
                {
                    foreach (var cel in layer.Cels) Add(cel.Frame);
                }
                break;
        }
        return frames;
    }

    public void CancelTransform()
    {
        if (!TransformActive) return;
        EndTransformSession();
    }

    private void EndTransformSession()
    {
        TransformActive = false;
        _transformFrames.Clear();
        _transformFilter = null;
        TransformEnded?.Invoke();
    }

    /// <summary>Commit a move/scale/rotate/mirror (mirror = negative scale) around a pivot.</summary>
    public void CommitTransformAffine(
        double pivotX, double pivotY,
        double scaleX, double scaleY,
        double angleRadians,
        double offsetX, double offsetY)
    {
        if (!TransformActive) return;
        var map = TransformOps.Affine(pivotX, pivotY, scaleX, scaleY, angleRadians, offsetX, offsetY);
        var sizeScale = Math.Sqrt(Math.Abs(scaleX * scaleY));
        var m = SKMatrix.CreateTranslation((float)-pivotX, (float)-pivotY);
        m = m.PostConcat(SKMatrix.CreateScale((float)scaleX, (float)scaleY));
        m = m.PostConcat(SKMatrix.CreateRotation((float)angleRadians));
        m = m.PostConcat(SKMatrix.CreateTranslation((float)(pivotX + offsetX), (float)(pivotY + offsetY)));
        CommitTransformCore(map, sizeScale, m);
    }

    /// <summary>Commit a four-corner perspective transform.</summary>
    public void CommitTransformPerspective(double[] srcQuad, double[] dstQuad)
    {
        if (!TransformActive) return;
        double[] h;
        try
        {
            h = TransformOps.PerspectiveCoefficients(srcQuad, dstQuad);
        }
        catch (InvalidOperationException)
        {
            AiStatus = "That corner arrangement collapses the drawing — adjust the corners and try again.";
            return;
        }
        var map = TransformOps.Perspective(srcQuad, dstQuad);
        var m = new SKMatrix(
            (float)h[0], (float)h[1], (float)h[2],
            (float)h[3], (float)h[4], (float)h[5],
            (float)h[6], (float)h[7], 1f);
        CommitTransformCore(map, 1, m);
    }

    private void CommitTransformCore(TransformOps.PointMap map, double sizeScale, SKMatrix baselineMatrix)
    {
        var frames = _transformFrames.ToList();
        var filter = _transformFilter;
        // Invalidate before the edit so the Changed refresh re-renders from
        // the transformed record.
        foreach (var frame in frames) _cache.Invalidate(frame.Id);
        _editor.Perform(_ =>
        {
            foreach (var frame in frames)
            {
                TransformOps.TransformFrame(frame, map, sizeScale, filter);
                // Raster baselines resample once per commit; a region-limited
                // transform moves strokes only (baseline pixels stay put).
                if (filter is null && frame is PaintedFrame { PngBase64.Length: > 0 } painted)
                {
                    ResampleBaseline(painted, baselineMatrix);
                }
            }
        });
        // The selection outline rides along so a follow-up transform lines up.
        if (HasSelection)
        {
            foreach (var contour in _selectionContours)
            {
                for (var i = 0; i < contour.Count; i++)
                {
                    var pt = contour[i];
                    var (x, y) = map(pt.X, pt.Y);
                    contour[i] = pt with { X = x, Y = y };
                }
            }
            NotifySelection();
        }
        EndTransformSession();
        AiStatus = $"Transformed {frames.Count} drawing{(frames.Count == 1 ? "" : "s")}.";
    }

    private SKSamplingOptions SamplingFor(TransformSampling mode) => mode switch
    {
        TransformSampling.Nearest => new SKSamplingOptions(SKFilterMode.Nearest),
        TransformSampling.Bicubic => new SKSamplingOptions(SKCubicResampler.Mitchell),
        _ => new SKSamplingOptions(SKFilterMode.Linear),
    };

    private void ResampleBaseline(PaintedFrame frame, SKMatrix matrix)
    {
        try
        {
            var bytes = Convert.FromBase64String(frame.PngBase64);
            using var src = SKBitmap.Decode(bytes);
            if (src is null) return;
            var info = new SKImageInfo(Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            if (surface is null) return;
            surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            surface.Canvas.SetMatrix(matrix);
            using var image = SKImage.FromBitmap(src);
            surface.Canvas.DrawImage(image, 0, 0, SamplingFor(TransformSampling));
            using var snap = surface.Snapshot();
            using var data = snap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            frame.PngBase64 = Convert.ToBase64String(data.ToArray());
        }
        catch (FormatException)
        {
            // Corrupt baseline: leave it untouched rather than destroy pixels.
        }
    }

    // ---- layer reordering -------------------------------------------------------

    /// <summary>Move a layer toward the viewer (+1) or away (−1), keeping it active.</summary>
    internal void MoveLayer(LayerRow row, int delta)
    {
        var id = row.Layer.Id;
        _editor.MoveLayer(id, delta);
        ActiveLayerIndex = Scene.Layers.FindIndex(l => l.Id == id);
    }

    [RelayCommand]
    private void MoveLayerUp(LayerRow row) => MoveLayer(row, +1);

    [RelayCommand]
    private void MoveLayerDown(LayerRow row) => MoveLayer(row, -1);

    // ---- layer folders ----------------------------------------------------------

    /// <summary>The docker's item list: folder headers followed by their (uncollapsed) member rows.</summary>
    public ObservableCollection<object> LayerPanelItems { get; } = [];

    private void RebuildLayerPanel()
    {
        LayerPanelItems.Clear();
        var emitted = new HashSet<string>();
        foreach (var row in LayerRows) // topmost first
        {
            var group = Scene.GroupOf(row.Layer);
            if (group is null)
            {
                LayerPanelItems.Add(row);
                continue;
            }
            if (emitted.Add(group.Id)) LayerPanelItems.Add(new GroupRow(this, group));
            if (!group.Collapsed) LayerPanelItems.Add(row);
        }
    }

    /// <summary>New folder containing the active layer.</summary>
    [RelayCommand]
    private void CreateLayerFolder()
    {
        var layer = ActiveLayer;
        _editor.Perform(doc =>
        {
            var group = new LayerGroup { Name = $"Folder {doc.Scene.LayerGroups.Count + 1}" };
            doc.Scene.LayerGroups.Add(group);
            layer.GroupId = group.Id;
        });
    }

    /// <summary>Put the active layer into this folder (moved adjacent so the folder stays one block).</summary>
    [RelayCommand]
    private void AddActiveLayerToGroup(GroupRow header)
    {
        var layer = ActiveLayer;
        if (layer.GroupId == header.Group.Id) return;
        _editor.Perform(doc =>
        {
            var layers = doc.Scene.Layers;
            layers.Remove(layer);
            var top = -1;
            for (var i = 0; i < layers.Count; i++)
            {
                if (layers[i].GroupId == header.Group.Id) top = i;
            }
            if (top < 0)
            {
                layers.Add(layer); // empty folder: just append
            }
            else
            {
                layers.Insert(top + 1, layer); // top of the folder's block
            }
            layer.GroupId = header.Group.Id;
        });
        ActiveLayerIndex = Scene.Layers.FindIndex(l => l.Id == layer.Id);
    }

    /// <summary>Take a layer out of its folder (placed just above the folder's block).</summary>
    [RelayCommand]
    private void RemoveLayerFromGroup(LayerRow row)
    {
        var layer = row.Layer;
        if (layer.GroupId is null) return;
        var groupId = layer.GroupId;
        _editor.Perform(doc =>
        {
            var layers = doc.Scene.Layers;
            layers.Remove(layer);
            var top = -1;
            for (var i = 0; i < layers.Count; i++)
            {
                if (layers[i].GroupId == groupId) top = i;
            }
            layers.Insert(top < 0 ? layers.Count : top + 1, layer);
            layer.GroupId = null;
        });
        ActiveLayerIndex = Scene.Layers.FindIndex(l => l.Id == layer.Id);
    }

    /// <summary>Dissolve the folder: its layers stay, ungrouped.</summary>
    [RelayCommand]
    private void DissolveGroup(GroupRow header)
    {
        _editor.Perform(doc =>
        {
            foreach (var layer in doc.Scene.Layers)
            {
                if (layer.GroupId == header.Group.Id) layer.GroupId = null;
            }
            doc.Scene.LayerGroups.RemoveAll(g => g.Id == header.Group.Id);
        });
    }

    internal void CommitGroupRename(LayerGroup group, string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0 || group.Name == trimmed) return;
        _editor.Perform(_ => group.Name = trimmed);
    }

    internal void SetGroupVisible(LayerGroup group, bool visible)
    {
        if (group.Visible == visible) return;
        _editor.Perform(_ => group.Visible = visible);
    }

    internal void SetGroupColor(LayerGroup group, string color)
    {
        if (group.Color == color) return;
        _editor.Perform(_ => group.Color = color);
    }

    /// <summary>Collapse is a view preference: persisted, but not an undo step.</summary>
    internal void SetGroupCollapsed(LayerGroup group, bool collapsed)
    {
        if (group.Collapsed == collapsed) return;
        group.Collapsed = collapsed;
        MarkDocumentEdited();
        RebuildLayerPanel();
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
        // An uncommitted palette edit is not on the stack yet; undoing before
        // it lands would step over it and then have it reappear.
        CommitSwatchEdit();
        ApplyEditScope(WhileApplyingScope(_editor.UndoScoped));
    }

    [RelayCommand]
    private void Redo()
    {
        CommitSwatchEdit();
        ApplyEditScope(WhileApplyingScope(_editor.RedoScoped));
    }

    private DocumentEditor.EditScope WhileApplyingScope(Func<DocumentEditor.EditScope> step)
    {
        _applyingEditScope = true;
        try
        {
            return step();
        }
        finally
        {
            _applyingEditScope = false;
        }
    }

    /// <summary>
    /// Invalidate only what an undo/redo actually touched: one frame for a
    /// stroke delta, everything for a structural snapshot. Full invalidation
    /// re-rendered every visible frame and every thumbnail — the undo lag.
    /// </summary>
    /// <summary>
    /// Undo/redo lands in two steps: the editor raises Changed while the
    /// cached bitmaps still hold the OLD pixels, and only afterwards do we
    /// learn which frame to drop. Publishing in between would show stale
    /// pixels and pay for a repaint twice, so the resync waits for this.
    /// </summary>
    private bool _applyingEditScope;

    private void ApplyEditScope(DocumentEditor.EditScope scope)
    {
        if (!scope.Any) return;
        if (scope.FrameId is { } frameId)
        {
            _cache.Invalidate(frameId);
            _dirtyThumbIds.Add(frameId);
        }
        else
        {
            _cache.Clear();
            _allThumbsDirty = true;
        }
        ClampCurrentFrame(publishIfUnchanged: false);
        PublishSnapshot();
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
    /// Undoable, like visibility — locking is a document decision an artist
    /// can change their mind about, not a view preference.
    /// </summary>
    internal void SetLayerLocked(Layer layer, bool locked)
    {
        if (layer.Locked == locked) return;
        _editor.Perform(_ => layer.Locked = locked);
        NotifyLayerGating();
    }

    internal void SetLayerAlphaLocked(Layer layer, bool locked)
    {
        if (layer.AlphaLocked == locked) return;
        _editor.Perform(_ => layer.AlphaLocked = locked);
        NotifyLayerGating();
    }

    /// <summary>Locking a folder locks every layer inside it.</summary>
    internal void SetGroupLocked(LayerGroup group, bool locked)
    {
        if (group.Locked == locked) return;
        _editor.Perform(_ => group.Locked = locked);
        SyncLayerRows();
        NotifyLayerGating();
    }

    /// <summary>Lock or unlock the active layer (keyboard and menu path).</summary>
    [RelayCommand]
    private void ToggleActiveLayerLocked()
    {
        if (ActiveLayer is { } layer) SetLayerLocked(layer, !layer.Locked);
    }

    [RelayCommand]
    private void ToggleActiveLayerAlphaLocked()
    {
        if (ActiveLayer is { } layer) SetLayerAlphaLocked(layer, !layer.AlphaLocked);
    }

    private void NotifyLayerGating()
    {
        OnPropertyChanged(nameof(ActiveLayerBlocked));
        OnPropertyChanged(nameof(ActiveLayerAlphaLocked));
    }

    /// <summary>A row needs this to dim itself without reaching into the scene.</summary>
    internal bool IsLayerLockedByFolder(Layer layer) => Scene.GroupOf(layer) is { Locked: true };

    /// <summary>Shown in the tool options so the restriction is never invisible.</summary>
    public bool ActiveLayerAlphaLocked => ActiveLayer is { AlphaLocked: true };

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

    // ---- active layer compositing (opacity + blend mode) ----------------------

    public IReadOnlyList<LayerBlendMode> BlendModeChoices { get; } = Enum.GetValues<LayerBlendMode>();

    /// <summary>
    /// Active layer's opacity, 0–100 for the docker slider. Applied live while
    /// dragging, so deliberately not an undo step (an undo snapshot per slider
    /// tick would flood the history).
    /// </summary>
    public double ActiveLayerOpacity
    {
        get => Math.Round(ActiveLayer.Opacity * 100);
        set
        {
            var clamped = Math.Clamp(value / 100.0, 0, 1);
            if (Math.Abs(ActiveLayer.Opacity - clamped) < 0.0005) return;
            ActiveLayer.Opacity = clamped;
            MarkDocumentEdited();
            OnPropertyChanged();
            PublishSnapshot();
        }
    }

    /// <summary>Active layer's blend mode — a deliberate compositing choice, one undo step.</summary>
    public LayerBlendMode ActiveLayerBlendMode
    {
        get => ActiveLayer.BlendMode;
        set
        {
            if (ActiveLayer.BlendMode == value) return;
            var layer = ActiveLayer;
            _editor.Perform(_ => layer.BlendMode = value);
            OnPropertyChanged();
        }
    }

    private void NotifyActiveLayerCompositing()
    {
        OnPropertyChanged(nameof(ActiveLayerOpacity));
        OnPropertyChanged(nameof(ActiveLayerBlendMode));
    }

    /// <summary>Delete the active layer; an empty document always regrows one blank layer.</summary>
    [RelayCommand]
    private void DeleteActiveLayer() => DeleteLayer(ActiveLayer);

    public void DeleteLayer(Layer layer)
    {
        if (!CanEdit(layer, "delete it")) return;
        var removedIndex = Scene.Layers.FindIndex(l => l.Id == layer.Id);
        if (removedIndex < 0) return;
        _editor.Perform(doc =>
        {
            var scene = doc.Scene;
            var index = scene.Layers.FindIndex(l => l.Id == layer.Id);
            if (index < 0) return;
            scene.Layers.RemoveAt(index);
            // Regrow when nothing PAINTABLE is left, not merely when nothing is
            // left: a document down to its locked paper has layers and still
            // nowhere to draw.
            if (!scene.Layers.Any(l => !l.IsBackground))
            {
                var fresh = new Layer
                {
                    Name = "Paint 1",
                    Cels = [new Cel { Frame = new PaintedFrame() }],
                };
                while (fresh.Cels.Count < scene.FrameCount) fresh.Cels.Add(new Cel());
                scene.Layers.Add(fresh);
            }
        });
        var next = Math.Clamp(removedIndex, 0, Scene.Layers.Count - 1);
        // Never land on the paper: it is locked, so the next stroke would bounce.
        ActiveLayerIndex = Scene.Layers[next].IsBackground ? FirstPaintableLayer(Doc) : next;
    }

    /// <summary>Blank the active layer: every drawing on it loses its content, the timing stays.</summary>
    [RelayCommand]
    private void ClearActiveLayer() => ClearLayerContent(ActiveLayer);

    public void ClearLayerContent(Layer layer)
    {
        // Mark before the edit so the thumbnail refresh inside Changed sees them.
        foreach (var cel in layer.Cels)
        {
            if (cel.Frame is { } frame)
            {
                _cache.Invalidate(frame.Id);
                _dirtyThumbIds.Add(frame.Id);
            }
        }
        _editor.Perform(doc =>
        {
            var target = doc.Scene.Layers.FirstOrDefault(l => l.Id == layer.Id);
            if (target is null) return;
            foreach (var cel in target.Cels)
            {
                switch (cel.Frame)
                {
                    case PaintedFrame painted:
                        painted.Strokes.Clear();
                        painted.PngBase64 = "";
                        break;
                    case VectorFrame vector:
                        vector.Strokes.Clear();
                        break;
                }
            }
        });
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
        _celAnchor = (cell.LayerIndex, cell.Index);
        ClearCelRange();
    }

    // ---- multi-cel range selection ------------------------------------------------

    private (int Layer, int Index) _celAnchor;
    private (int Layer, int Start, int End)? _celRange;

    /// <summary>The selected cel range on one layer row (Shift+click), if any.</summary>
    public (int Layer, int Start, int End)? CelRange => _celRange;

    /// <summary>Shift+click: select the contiguous range from the last clicked cel to this one.</summary>
    public void RangeSelectTo(FrameCell cell)
    {
        if (cell.IsVirtual) return;
        var anchor = _celAnchor.Layer == cell.LayerIndex ? _celAnchor : (cell.LayerIndex, cell.Index);
        _celRange = (cell.LayerIndex, Math.Min(anchor.Index, cell.Index), Math.Max(anchor.Index, cell.Index));
        RefreshCelSelectionHighlights();
    }

    public void ClearCelRange()
    {
        if (_celRange is null) return;
        _celRange = null;
        RefreshCelSelectionHighlights();
    }

    private void RefreshCelSelectionHighlights()
    {
        foreach (var row in LayerRows)
        {
            foreach (var c in row.Cells)
            {
                c.IsSelected = _celRange is { } r
                    && c.LayerIndex == r.Layer && c.Index >= r.Start && c.Index <= r.End;
            }
        }
    }

    /// <summary>The range the operation on this cell should cover: the selection when the cell is inside it, else just the cell.</summary>
    private (int Start, int End) OpRangeFor(FrameCell cell) =>
        _celRange is { } r && r.Layer == cell.LayerIndex && cell.Index >= r.Start && cell.Index <= r.End
            ? (r.Start, r.End)
            : (cell.Index, cell.Index);

    /// <summary>Drop of a dragged cel: move (or Ctrl-copy) the drawing along its row.</summary>
    public void MoveCel(FrameCell from, FrameCell to, bool copy)
    {
        if (from.LayerIndex != to.LayerIndex)
        {
            AiStatus = "Cels move along their own layer row.";
            return;
        }
        if (LayerOfCell(from) is not { } layer) return;
        _editor.MoveCel(layer.Id, from.Index, to.Index, copy);
        ActiveLayerIndex = from.LayerIndex;
        CurrentFrameIndex = Math.Min(to.Index, Scene.FrameCount - 1);
    }

    // ---- frame markers --------------------------------------------------------------

    /// <summary>Ruler tags, refreshed as a new list so the ruler re-renders.</summary>
    [ObservableProperty]
    private IReadOnlyList<FrameMarker> _markersView = [];

    public FrameMarker? MarkerAt(int frame) => Scene.Markers.FirstOrDefault(m => m.Frame == frame);

    public void SetMarkerAt(int frame, string label, string color)
    {
        _editor.Perform(doc =>
        {
            doc.Scene.Markers.RemoveAll(m => m.Frame == frame);
            doc.Scene.Markers.Add(new FrameMarker { Frame = frame, Label = label.Trim(), Color = color });
            doc.Scene.Markers.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        });
    }

    public void RemoveMarkerAt(int frame)
    {
        if (MarkerAt(frame) is null) return;
        _editor.Perform(doc => doc.Scene.Markers.RemoveAll(m => m.Frame == frame));
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

    /// <summary>
    /// Resolve a frame's stroke list by id inside a given document instance —
    /// delta undo steps must not capture object references, because a
    /// snapshot-undo in between replaces the whole instance tree.
    /// </summary>
    /// <summary>Remove a stroke by id — reference equality dies when a snapshot-undo swaps in a cloned tree.</summary>
    private static void RemoveStrokeById(Doc doc, string frameId, string strokeId)
    {
        var list = StrokeListIn(doc, frameId);
        var index = list?.FindLastIndex(s => s.Id == strokeId) ?? -1;
        if (index >= 0) list!.RemoveAt(index);
    }

    private static List<Stroke>? StrokeListIn(Doc doc, string frameId)
    {
        foreach (var layer in doc.Scene.Layers)
        {
            foreach (var cel in layer.Cels)
            {
                if (cel.Frame is { } frame && frame.Id == frameId) return StrokesOf(frame);
            }
        }
        return null;
    }

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
        if (!CanEdit(ActiveLayer, "draw on it")) return;

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
            if (!scene.IsLayerVisible(layer)) continue;
            var frame = ExposureSheet.ExposedFrame(layer, frameIndex);
            if (frame is null) continue;
            passes.Add(new RenderPass(_cache.Get(frame, scene.Width, scene.Height), null, layer.Opacity, SceneRenderer.ToSkia(layer.BlendMode)));
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
        if (!CanEdit(layer, "insert inbetweens on it")) return 0;
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
        if (!CanEdit(layer, "draw on it")) return 0;
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

    /// <summary>
    /// Set while committing an edit whose visible effect the caller already
    /// knows and will publish itself (a stroke or fill). Without this, every
    /// pen lift also ran the full document resync — a whole-canvas repaint
    /// and a thumbnail sweep on top of the bounded ones that were enough.
    /// </summary>
    private bool _committingScopedEdit;

    private void OnDocumentChanged()
    {
        if (_committingScopedEdit)
        {
            MarkDocumentEdited();
            RefreshDocumentStats(); // memory grows as frames get cached
            return;
        }
        MarkDocumentEdited();
        InvalidateWholeCanvas(); // a document-wide change can move any pixel
        _composeRing.InvalidateAll();
        BrushTipRegistry.Register(Doc.BrushTips);
        ClipRegionRegistry.Register(Doc.ClipRegions);
        RegisterResources();
        PaletteDocker.Load(Doc);
        GradientDocker.Load(Doc);
        RefreshDocumentStats();
        OnPropertyChanged(nameof(ReferenceSheetsView));
        SyncLayerChoices();
        ClampCurrentFrame(publishIfUnchanged: !_applyingEditScope);
        SyncLayerRows();
        OnPropertyChanged(nameof(FrameLabel));
        OnPropertyChanged(nameof(TimelineExtent));
        OnPropertyChanged(nameof(MaxScrubFrame));
        OnPropertyChanged(nameof(Fps));
        NotifyActiveLayerCompositing();
        MarkersView = Scene.Markers.ToList();
        RefreshCelSelectionHighlights();
        // Undo/redo publishes from ApplyEditScope instead, once the stale
        // frame bitmaps have been dropped.
        if (_applyingEditScope) return;
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

    /// <summary>
    /// Keep the playhead inside the timeline. Moving it repaints through the
    /// property setter; <paramref name="publishIfUnchanged"/> covers callers
    /// that rely on this to repaint even when nothing moved.
    /// </summary>
    private void ClampCurrentFrame(bool publishIfUnchanged = true)
    {
        var max = Math.Max(0, Scene.FrameCount - 1);
        if (CurrentFrameIndex > max) CurrentFrameIndex = max;
        else if (publishIfUnchanged) PublishSnapshot();
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
        RebuildLayerPanel();
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
        RefreshLayerThumbs();
        _dirtyThumbIds.Clear();
        _allThumbsDirty = false;
    }

    /// <summary>
    /// Layer-docker thumbnails show the exposed drawing at the playhead
    /// (holds resolve to the drawing they hold) over a checkerboard.
    /// Also called on playhead moves, where only rows whose exposed frame
    /// actually changed re-render.
    /// </summary>
    private void RefreshLayerThumbs()
    {
        foreach (var row in LayerRows)
        {
            var frame = ExposureSheet.ExposedFrame(row.Layer, CurrentFrameIndex);
            if (frame is null)
            {
                row.Thumb = null;
                row.ThumbFrameId = null;
                continue;
            }
            var stale = _allThumbsDirty
                        || row.ThumbFrameId != frame.Id
                        || _dirtyThumbIds.Contains(frame.Id);
            if (!stale && row.Thumb is not null) continue;

            var bmp = _cache.Get(frame, Scene.Width, Scene.Height);
            row.Thumb = ThumbnailRenderer.RenderChecker(bmp, 44, 26);
            row.ThumbFrameId = frame.Id;
        }
    }

    private void RefreshCellHighlights()
    {
        foreach (var row in LayerRows)
        {
            foreach (var cell in row.Cells) cell.IsCurrent = cell.Index == CurrentFrameIndex;
        }
    }

    /// <summary>
    /// The document region the last publish actually recomposited (null = the
    /// whole canvas). What the artist feels as a stutter is this rect growing,
    /// so tests assert on it rather than on wall-clock, which is unusable on a
    /// shared runner.
    /// </summary>
    internal SKRectI? LastPublishClip { get; private set; }

    /// <summary>Composite the scene for the current playhead and hand it to the view.</summary>
    public void PublishSnapshot()
    {
        var scene = Scene;
        var passes = new List<RenderPass>();

        foreach (var layer in scene.Layers)
        {
            if (!scene.IsLayerVisible(layer)) continue;

            // Ghosts go directly beneath the layer they belong to, not beneath
            // the whole stack. Queuing them all first was invisible while every
            // layer was transparent; the moment a document opened on opaque
            // paper, the paper painted over every ghost. Interleaving is also
            // what makes multi-layer onion read correctly — a layer's ghosts
            // sit under it, exactly as its own earlier frames would.
            if (OnionSkin && !IsPlaying && layer.OnionEnabled)
            {
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

            var frame = ExposureSheet.ExposedFrame(layer, CurrentFrameIndex);
            if (frame is null) continue;

            var bmp = _cache.Get(frame, scene.Width, scene.Height);

            // Live stroke: the dabs live in their own scratch and composite
            // over the layer here. The layer bitmap is never copied for a
            // preview — a full-canvas copy costs ~1 s at 4K.
            StrokeOverlay? overlay = null;
            if (_liveScratch is not null && _liveGradient is { } drag && layer.Id == ActiveLayer.Id)
            {
                // The gradient tool's drag preview. Opacity and the alpha lock
                // ride on the overlay, exactly as they do for a brush stroke,
                // so the preview and the commit agree.
                overlay = new StrokeOverlay(
                    _liveScratch,
                    drag.Brush.Opacity,
                    false,
                    drag.AlphaLocked,
                    drag.ClipId is null ? null : ClipRegionRegistry.Resolve(drag.ClipId));
            }
            else if (_liveScratch is not null && _strokeBuilder.IsActive
                && _strokeBuilder.Current is { } live && layer.Id == ActiveLayer.Id)
            {
                // Prefer the fully rendered stroke when a pass has completed —
                // medium, wet edge and texture included — and fall back to raw
                // dabs only for the first few events of a heavy brush, before
                // the first pass lands.
                var source = _livePostStampedCount > 0 && _livePostScratch is not null
                    ? _livePostScratch
                    : _liveScratch;
                // Everything the commit will mask the stroke with, applied
                // now: an artist cannot judge a mark they are not being shown.
                overlay = new StrokeOverlay(
                    source,
                    live.Brush.Opacity,
                    live.Tool == ToolKind.Eraser,
                    live.AlphaLocked,
                    live.ClipId is null ? null : ClipRegionRegistry.Resolve(live.ClipId));
            }

            passes.Add(new RenderPass(
                bmp, null, layer.Opacity, SceneRenderer.ToSkia(layer.BlendMode), overlay));
        }

        // Compose at the resolution the canvas can actually show. A 4K document
        // in a laptop window is displayed at roughly 40%, and handing the
        // renderer full detail makes it rescale 8.3 M pixels on every frame —
        // ~29 ms, which is the whole stutter budget before anything is drawn.
        var renderScale = ComposeScale;
        // Looking through the camera reframes the canvas, so the surface is
        // the camera's output rather than the document. Without one — the
        // ordinary case and every asset document — nothing here changes.
        var cameraView = CameraViewTransform(renderScale);
        var viewWidth = cameraView is null ? scene.Width : scene.Camera!.OutputWidth;
        var viewHeight = cameraView is null ? scene.Height : scene.Camera!.OutputHeight;
        var info = new SKImageInfo(
            Math.Max(1, (int)Math.Ceiling(viewWidth * renderScale)),
            Math.Max(1, (int)Math.Ceiling(viewHeight * renderScale)),
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        var dirty = _dirtyIsWholeCanvas ? null : _pendingDirty;
        _pendingDirty = null;
        _dirtyIsWholeCanvas = false;
        var seq = ++_publishSeq;
        var background = SceneRenderer.BackgroundOf(scene);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        SKRectI? usedClip = null;
        var image = _composeRing.Publish(info, dirty, (surface, clip) =>
        {
            usedClip = clip;
            SceneRenderer.ComposeInto(surface, passes, background, clip, renderScale, cameraView);
        }, renderScale, cameraView);
        sw.Stop();
        if (Environment.GetEnvironmentVariable("LIGHTBOX_PERFTRACE") is not null)
        {
            Console.Error.WriteLine($"[publish] dirty={dirty} clip={usedClip} passes={passes.Count} {sw.Elapsed.TotalMilliseconds:0.0}ms");
        }
        Performance.RecordPublish(sw.Elapsed.TotalMilliseconds);
        LastPublishClip = usedClip;
        if (SnapshotChanged is { } handler)
        {
            handler(new RenderSnapshot(image, viewWidth, viewHeight, seq));
        }
        else
        {
            // No canvas attached (headless or IPC-only): nobody would ever
            // free this image, and a live snapshot makes the next repaint
            // duplicate the whole buffer.
            image.Dispose();
        }
    }

    /// <summary>
    /// Limit the next publish to a document region. Only safe when nothing
    /// outside the region can change; every other edit path must leave the
    /// default (whole-canvas) invalidation alone, or stale pixels linger.
    /// </summary>
    private void MarkDirtyRegion(SKRectI region)
    {
        if (_dirtyIsWholeCanvas) return;
        if (_pendingDirty is { } existing)
        {
            existing.Union(region);
            _pendingDirty = existing;
        }
        else
        {
            _pendingDirty = region;
        }
    }

    /// <summary>The next publish repaints everything (the safe default).</summary>
    private void InvalidateWholeCanvas()
    {
        _dirtyIsWholeCanvas = true;
        _pendingDirty = null;
    }

    // ---- camera ---------------------------------------------------------------

    /// <summary>
    /// Whether this document has a camera at all. Everything camera-related in
    /// the UI hangs off this: a document without one shows no overlay, no
    /// controls and no ruler keys. Optional means absent, not disabled.
    /// </summary>
    public bool HasCamera => Scene.Camera is not null;

    /// <summary>
    /// The camera frame's corners in document coordinates, or null. The canvas
    /// draws this as view-only chrome — it never reaches a pixel.
    /// </summary>
    public SKPoint[]? CameraFrameCorners { get; private set; }

    /// <summary>Fired when the camera appears, disappears, or reframes.</summary>
    public event Action? CameraChanged;

    /// <summary>The framing at the playhead — what the overlay and the fields show.</summary>
    private CameraFraming FramingNow() =>
        CameraOps.At(Scene.Camera, CurrentFrameIndex, Scene.Width, Scene.Height);

    /// <summary>
    /// Give the scene a camera, framed on the whole canvas at 1:1 so the first
    /// thing the artist sees is what they already had. Output defaults to the
    /// canvas size for the same reason — a camera should start by changing
    /// nothing.
    /// </summary>
    [RelayCommand]
    private void AddCamera()
    {
        if (Scene.Camera is not null) return;
        Scene.Camera = new Camera { OutputWidth = Scene.Width, OutputHeight = Scene.Height };
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>
    /// Take the camera away entirely, keys and all, returning the document to
    /// the state it saves in when it never had one.
    /// </summary>
    [RelayCommand]
    private void RemoveCamera()
    {
        if (Scene.Camera is null) return;
        Scene.Camera = null;
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>Key the current framing at the playhead.</summary>
    [RelayCommand]
    private void SetCameraKey()
    {
        if (Scene.Camera is not { } camera) return;
        CameraOps.SetKey(camera, CurrentFrameIndex, FramingNow());
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>Remove the key at the playhead, if there is one.</summary>
    [RelayCommand]
    private void ClearCameraKey()
    {
        if (Scene.Camera is not { } camera) return;
        if (!CameraOps.ClearKey(camera, CurrentFrameIndex)) return;
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>True when the playhead sits on an authored camera key.</summary>
    public bool IsOnCameraKey => CameraOps.KeyAt(Scene.Camera, CurrentFrameIndex) is not null;

    /// <summary>Frames carrying a camera key, for the timeline ruler.</summary>
    public IReadOnlyList<int> CameraKeyFrames =>
        CameraOps.Ordered(Scene.Camera).Select(k => k.Frame).ToList();

    public int CameraOutputWidth
    {
        get => Scene.Camera?.OutputWidth ?? Scene.Width;
        set => SetCameraOutput(Math.Clamp(value, 1, 16384), CameraOutputHeight);
    }

    public int CameraOutputHeight
    {
        get => Scene.Camera?.OutputHeight ?? Scene.Height;
        set => SetCameraOutput(CameraOutputWidth, Math.Clamp(value, 1, 16384));
    }

    private void SetCameraOutput(int width, int height)
    {
        if (Scene.Camera is not { } camera) return;
        if (camera.OutputWidth == width && camera.OutputHeight == height) return;
        camera.OutputWidth = width;
        camera.OutputHeight = height;
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>
    /// The framing at the playhead, editable. Writing any of these moves the
    /// live framing; it only becomes part of the shot once keyed, which is the
    /// same bargain as a transform gizmo before it is committed.
    /// </summary>
    public double CameraX
    {
        get => FramingNow().X;
        set => SetFraming(FramingNow() with { X = value });
    }

    public double CameraY
    {
        get => FramingNow().Y;
        set => SetFraming(FramingNow() with { Y = value });
    }

    public double CameraZoom
    {
        get => FramingNow().Zoom;
        set => SetFraming(FramingNow() with { Zoom = Math.Clamp(value, 0.05, 64) });
    }

    public double CameraRotationDeg
    {
        get => FramingNow().RotationDeg;
        set => SetFraming(FramingNow() with { RotationDeg = value });
    }

    /// <summary>
    /// Editing a framing field writes it straight to the key at the playhead,
    /// creating one if there is none. A framing you cannot see keyed is a
    /// framing you will lose by scrubbing away from it.
    /// </summary>
    private void SetFraming(CameraFraming framing)
    {
        if (Scene.Camera is not { } camera) return;
        CameraOps.SetKey(camera, CurrentFrameIndex, framing);
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>
    /// Show the canvas through the camera rather than the world. Off by
    /// default: the artist draws in the world, and this is for checking the
    /// shot.
    /// </summary>
    [ObservableProperty]
    private bool _viewThroughCamera;

    partial void OnViewThroughCameraChanged(bool value)
    {
        InvalidateWholeCanvas();
        _composeRing.InvalidateAll();
        RefreshCamera();
        PublishSnapshot();
    }

    /// <summary>The matrix a publish composites through, or null for the world.</summary>
    private SKMatrix? CameraViewTransform(double renderScale) =>
        ViewThroughCamera && Scene.Camera is { } camera
            ? CameraTransform.Matrix(
                FramingNow(), camera.OutputWidth, camera.OutputHeight, renderScale)
            : null;

    private void RefreshCamera()
    {
        // No camera, or looking through it — in which case the frame IS the
        // viewport and an overlay would just outline the window.
        CameraFrameCorners = Scene.Camera is { } camera && !ViewThroughCamera
            ? CameraTransform.FrameCorners(FramingNow(), camera.OutputWidth, camera.OutputHeight)
            : null;
        CameraChanged?.Invoke();
    }

    private void NotifyCameraSurface()
    {
        OnPropertyChanged(nameof(HasCamera));
        OnPropertyChanged(nameof(IsOnCameraKey));
        OnPropertyChanged(nameof(CameraKeyFrames));
        OnPropertyChanged(nameof(CameraOutputWidth));
        OnPropertyChanged(nameof(CameraOutputHeight));
        OnPropertyChanged(nameof(CameraX));
        OnPropertyChanged(nameof(CameraY));
        OnPropertyChanged(nameof(CameraZoom));
        OnPropertyChanged(nameof(CameraRotationDeg));
        InvalidateWholeCanvas();
        _composeRing.InvalidateAll();
        PublishSnapshot();
    }

    // ---- performance preferences -----------------------------------------------

    /// <summary>
    /// How much detail to composite. Only affects what is shown while you
    /// work — the record, exports and thumbnails are always full resolution.
    /// </summary>
    [ObservableProperty]
    private CanvasQuality _canvasQuality = CanvasQuality.Display;

    public IReadOnlyList<CanvasQuality> CanvasQualityChoices { get; } = Enum.GetValues<CanvasQuality>();

    partial void OnCanvasQualityChanged(CanvasQuality value)
    {
        InvalidateWholeCanvas();
        _composeRing.InvalidateAll();
        Performance.Reset();
        PublishSnapshot();
        RefreshDocumentStats();
    }

    /// <summary>Undo steps kept. Deltas are cheap; snapshots hold a whole document each.</summary>
    public int UndoDepth
    {
        get => _editor.MaxUndo;
        set
        {
            var clamped = Math.Clamp(value, 5, 500);
            if (_editor.MaxUndo == clamped) return;
            foreach (var tab in Tabs) tab.Editor.MaxUndo = clamped;
            OnPropertyChanged();
        }
    }

    /// <summary>Ceiling for cached frame bitmaps, in megabytes.</summary>
    public int FrameCacheBudgetMb
    {
        get => (int)(FrameBitmapCache.ByteBudget / (1024 * 1024));
        set
        {
            var clamped = Math.Clamp(value, 64, 4096);
            if (FrameCacheBudgetMb == clamped) return;
            FrameBitmapCache.ByteBudget = clamped * 1024L * 1024L;
            OnPropertyChanged();
            RefreshDocumentStats();
        }
    }

    // ---- info strip ------------------------------------------------------------

    /// <summary>"3840 × 2160 px · 72 ppi" for the info strip.</summary>
    [ObservableProperty]
    private string _documentSizeLabel = "";

    /// <summary>"4 layers · 24 drawings" for the info strip.</summary>
    [ObservableProperty]
    private string _documentContentLabel = "";

    /// <summary>Approximate image memory this document is holding.</summary>
    [ObservableProperty]
    private string _memoryLabel = "";

    private void RefreshDocumentStats()
    {
        var scene = Scene;
        var drawings = new HashSet<string>();
        foreach (var layer in scene.Layers)
        {
            foreach (var cel in layer.Cels)
            {
                if (cel.Frame is { } frame) drawings.Add(frame.Id);
            }
        }

        DocumentSizeLabel = $"{scene.Width} × {scene.Height} px · {scene.Ppi} ppi";
        DocumentContentLabel =
            $"{scene.Layers.Count} layer{(scene.Layers.Count == 1 ? "" : "s")} · " +
            $"{drawings.Count} drawing{(drawings.Count == 1 ? "" : "s")}";

        var bytes = _cache.CachedBytes + _composeRing.AllocatedBytes;
        var backend = Rendering.CanvasControl.GraphicsBackend;
        MemoryLabel = (bytes >= 1024L * 1024 * 1024
            ? $"{bytes / (1024.0 * 1024 * 1024):0.0} GB images"
            : $"{bytes / (1024.0 * 1024):0} MB images")
            + (backend == "unknown" ? "" : $" · {backend}");
        Performance.DescribeDocument(scene.Width, scene.Height, scene.Layers.Count, drawings.Count, bytes);
    }
}
