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
    // ---- internals ----------------------------------------------------------

    /// <summary>
    /// Advance by what the clock says, not by one.
    /// </summary>
    /// <remarks>
    /// The count is the whole of <see cref="PlaybackClock"/>'s contract: a tick
    /// that arrived a frame and a half late owes two frames, and honouring only
    /// one is the accumulating drift the paced clock exists to remove. Written
    /// as a loop rather than an index jump so a wrap, a range end and the audio
    /// resync all happen exactly as they do at speed.
    /// </remarks>
    /// <summary>
    /// Advance the playhead by the frames this tick owes, compositing only the
    /// one that will actually be seen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B162. This loop rendered every frame it had just decided to skip, so
    /// dropping frames to keep up cost four times as much as keeping them.</b>
    /// <c>PlaybackClock.Pace</c> returns a frame count — 1 normally, up to
    /// <c>MaxCatchUpFrames</c> when the tick is late — and "drop 3 to catch up"
    /// was implemented as three full <c>StepPlayback</c> calls, each of which
    /// writes <c>CurrentFrameIndex</c> and therefore runs a whole publish.
    /// </para>
    /// <para>
    /// <b>It is a feedback loop, not a constant overhead, which is why it ran
    /// away.</b> A tick that is late asks for more frames; more frames cost
    /// proportionally more; the extra cost makes the next tick later still. The
    /// capture that found it: <b>178 ticks advanced the playhead and 568
    /// composites came out of them</b> — 3.2 per tick — with mean lateness
    /// climbing to 232 ms and <b>104 occasions where the clock gave up and
    /// re-based</b>, on a five-frame scene.
    /// </para>
    /// <para>
    /// <b>It also made the report lie in the reader's favour</b>, which is why
    /// four rounds of captures looked survivable. The tick breakdown divides by
    /// profiled ticks — one per <c>StepPlayback</c> — so it reported 38 ms
    /// against an 83 ms budget and called it comfortable, when a single timer
    /// fire was doing 3.2 of them: <b>138 ms of an 83.3 ms period, 165%.</b> The
    /// per-tick number was true and the per-<em>frame-period</em> number, which
    /// is the one that decides whether a clock can keep time, was never shown.
    /// </para>
    /// <para>
    /// A skipped frame still moves the playhead, still wraps the loop, still
    /// stops at the end of a range and still tracks audio — everything except
    /// the composite, which is the one part nobody was ever going to see.
    /// </para>
    /// </remarks>
    // The clock's own handler, reachable by the tests: the catch-up path is
    // driven by a real DispatcherTimer being late, which the headless harness
    // cannot produce — the same reason PlaybackClock's docs give for testing
    // Pace as pure arithmetic rather than through a timer.
    internal void HandlePlaybackTickForTests(int frames) => OnPlaybackTick(frames);

    private void OnPlaybackTick(int frames)
    {
        for (var i = 0; i < frames - 1 && IsPlaying; i++)
        {
            _skippingFrame = true;
            try { StepPlayback(); }
            finally { _skippingFrame = false; }
        }
        if (IsPlaying) StepPlayback();
    }



    private void OnDocumentChanged()
    {
        // B151: the swatch's frame list describes a tree that just moved. An undo
        // or a structural edit can add, remove or replace the frames it names, and
        // a stale list would repaint the wrong ones — or, worse, none of them.
        _swatchRepaint = null;
        // Which drawings the rig moves is derived from the layer stack, so an
        // undo that took a link away — or a load that brought one in — has to
        // land here before anything renders. Rebuilt even on the scoped-edit
        // fast path: the index is a dictionary of a handful of ids, and a
        // stale one is wrong pixels rather than a slow repaint.
        RebuildRigIndex();

        if (_committingScopedEdit)
        {
            MarkDocumentEdited();
            RefreshDocumentStats(); // memory grows as frames get cached
            return;
        }
        MarkDocumentEdited();
        _publish.InvalidateWholeCanvas(); // a document-wide change can move any pixel
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
        NotifyAudioSurface();
        NotifyArmatureSurface();
        NotifyActiveLayerCompositing();
        MarkersView = Scene.Markers.ToList();
        RefreshCelSelectionHighlights();
        ScheduleVolumeCheck();
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

        // Math.Max, because a document with no layers is loadable and `Clamp(_, 0, -1)` throws.
        // `new Doc()` has an empty layer list and nothing in `DocJson` backfills one, so a
        // `.lightbox.json` with `"layers": []` — hand-edited, machine-written, or from a version
        // that allowed it — took down File → Open with an ArgumentException about 0 and -1. B56.
        var active = Math.Clamp(ActiveLayerIndex, 0, Math.Max(0, layers.Count - 1));
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
        // After the rows point at the current stack, not before: the refresh
        // drops ids whose layer has gone, and a row still holding a deleted
        // layer would keep one alive for a frame.
        RefreshLayerSelectionHighlights();
        OnPropertyChanged(nameof(FrameCells));
        OnPropertyChanged(nameof(TimelineTracks));
        OnPropertyChanged(nameof(TimelineFrameCount));
        OnPropertyChanged(nameof(GraphSeriesList));
        RefreshRangeHighlights();
    }

    // ---- performance preferences -----------------------------------------------


    public IReadOnlyList<CanvasQuality> CanvasQualityChoices { get; } = Enum.GetValues<CanvasQuality>();

    partial void OnCanvasQualityChanged(CanvasQuality value)
    {
        Settings.CanvasQuality = value.ToString();
        Settings.Save();
        _publish.InvalidateWholeCanvas();
        _composeRing.InvalidateAll();
        Performance.Reset();
        PublishSnapshot();
        RefreshDocumentStats();
    }

    /// <summary>The artist chose this quality, so nothing may revise it again.</summary>
    public void ChooseCanvasQuality(CanvasQuality value)
    {
        Settings.CanvasQualityChosen = true;
        CanvasQuality = value;   // its handler saves
    }

    /// <summary>
    /// React to finding out the frame is being presented without a GPU.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The label alone was a diagnosis with no treatment. On the software
    /// rasteriser, presenting the canvas — rescaling the whole document for
    /// every frame — overtakes editing it as the dominant cost, and
    /// <see cref="CanvasQuality.Half"/> is the one setting that changes how
    /// many pixels that is. Turning it down is the difference between a
    /// laggy canvas and a usable one on exactly the machines that cannot fix
    /// it any other way.
    /// </para>
    /// <para>
    /// It only ever revises a default. Once somebody has picked a quality
    /// themselves the app has no business overruling it, however slow the
    /// machine — the whole point of a preference is that it is theirs. And it
    /// is announced rather than done quietly: a canvas that silently got
    /// softer is a bug report.
    /// </para>
    /// </remarks>
    public void NoteGraphicsBackend()
    {
        if (Rendering.CanvasControl.SoftwareRendering is not true) return;
        RefreshDocumentStats();
        if (!TurnTheCanvasQualityDown()) return;
        AiStatus =
            "No GPU here, so the canvas is being drawn in software — quality lowered to Half "
            + "while you work. Exports are unaffected. Edit ▸ Configure ▸ Performance changes it.";
    }

    /// <summary>
    /// The one lever, pulled once, and never over the artist.
    /// </summary>
    /// <remarks>
    /// Shared by the two things that can decide the canvas needs help — the
    /// backend coming back as software, and the measurement saying so — because
    /// the conditions under which it is allowed to happen at all are the same
    /// and must not drift apart.
    /// </remarks>
    private bool TurnTheCanvasQualityDown()
    {
        // Two rules, and between them they also carry "only once": the first
        // pull leaves the quality at Half, which is no longer the default.
        if (Settings.CanvasQualityChosen || CanvasQuality != CanvasQuality.Display) return false;
        CanvasQuality = CanvasQuality.Half;
        return true;
    }

    /// <summary>
    /// Turn the quality down when the canvas is <em>measurably</em> not keeping
    /// up, whatever the graphics backend says.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The software-rendering path above acts on a well-founded guess: no GPU
    /// context means presenting the frame will dominate, and that is true
    /// before a single frame has been measured. What it cannot do is help the
    /// machine that <em>has</em> a GPU and is still too slow — an integrated
    /// chip on a 4K canvas with a dozen onion ghosts is slower than a software
    /// rasteriser on a sprite sheet, and it was getting a label saying
    /// everything was fine.
    /// </para>
    /// <para>
    /// So the backend is an input rather than the trigger. Software renderers
    /// get help at the first sign of trouble because there is no other lever on
    /// those machines; a GPU has to be visibly struggling first, since the more
    /// likely fix there is something about the document and the advice already
    /// names it.
    /// </para>
    /// <para>
    /// Once per session, only over a default, and announced — the same three
    /// rules the backend path follows. A canvas that silently got softer is a
    /// bug report.
    /// </para>
    /// </remarks>
    public void ConsiderCanvasRelief()
    {
        if (Settings.CanvasQualityChosen || CanvasQuality != CanvasQuality.Display) return;
        if (!Performance.HasSettled) return;

        var software = Rendering.CanvasControl.SoftwareRendering is true;
        var threshold = software ? 40 : 20;
        if (Performance.HeadroomPercent > threshold) return;
        if (!TurnTheCanvasQualityDown()) return;

        AiStatus =
            $"The canvas is taking {Performance.FrameMs:0} ms a frame to show, so quality has been "
            + "lowered to Half while you work. The drawing, exports and thumbnails are unaffected. "
            + "Edit ▸ Configure ▸ Performance changes it.";
    }

    /// <summary>
    /// How many steps are actually on the undo stack right now.
    /// </summary>
    /// <remarks>
    /// <b>B224, and the name is the whole point of it.</b> Four tests asking
    /// "did this gesture record an edit?" reached for <see cref="UndoDepth"/>,
    /// which is <c>MaxUndo</c> — the *setting* for how many steps are kept, not
    /// how many there are. So they captured a constant, performed a gesture,
    /// and asserted the constant had not changed: green whether the gesture
    /// recorded a step or not. A test that cannot fail is worse than no test,
    /// because it is counted as coverage.
    ///
    /// <para>
    /// Undone steps do not count — they are on the redo side and a redo would
    /// bring them back, so "how many edits are behind me" is the undo line
    /// alone. Reading it allocates a small list of labels and touches no
    /// document, which is why a test may call it freely.
    /// </para>
    /// </remarks>
    internal int RecordedStepCount
    {
        get
        {
            var steps = 0;
            foreach (var entry in _editor.History)
            {
                if (!entry.IsUndone) steps++;
            }
            return steps;
        }
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
    /// <remarks>
    /// <para>
    /// <b>The artist's setting stays the final word</b> — the derivation in
    /// <see cref="Services.MemoryBudget"/> decides the <em>default</em>, which is
    /// the part that was wrong.
    /// </para>
    /// <para>
    /// <b>Its floor is deliberately below the derived one</b> and that is not an
    /// inconsistency. The derived floor is what a minimum-spec machine needs for
    /// the cache to be worth having; this floor is how far somebody may go when
    /// they have decided they would rather have the memory — which is what the
    /// Configure page's own text offers ("lowering it frees memory on a large
    /// canvas"). The ceiling is shared, because above it the cache is holding
    /// bytes it will never spend whoever asked for them.
    /// </para>
    /// </remarks>
    public int FrameCacheBudgetMb
    {
        get => (int)(FrameBitmapCache.ByteBudget / (1024 * 1024));
        set
        {
            var clamped = Math.Clamp(
                value, 64, (int)(Services.MemoryBudget.FrameCacheCeilingBytes / (1024 * 1024)));
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

    /// <summary>
    /// Apply feature defaults to a new document based on the project's type.
    /// If no project is open, no features are set (document uses implicit defaults).
    /// </summary>
    private void ApplyFeatureDefaults(Doc doc)
    {
        if (ProjectDocker.Project?.Manifest.Type is not { } projectType) return;

        var defaults = new Lightbox.Core.Projects.FeatureDefaults();
        var features = Enum.GetValues<Lightbox.Core.Projects.FeatureKey>();

        // Build a dictionary of features that differ from false (our implicit default).
        // Only write overrides; absent features use their defaults.
        var overrides = new Dictionary<string, bool>();
        foreach (var feature in features)
        {
            var defaultValue = defaults.GetDefault(projectType, feature);
            if (defaultValue)
            {
                overrides[feature.ToString()] = true;
            }
        }

        // Only set Features if there are any overrides
        if (overrides.Count > 0)
        {
            doc.Features = overrides;
        }
    }

    /// <summary>Update a document feature toggle (Configure → Features page).</summary>
    public void SetDocumentFeature(Lightbox.Core.Projects.FeatureKey feature, bool value, bool projectDefault)
    {
        if (ActiveTab?.Doc is not { } doc) return;

        doc.Features ??= [];

        if (value == projectDefault)
        {
            // Value matches default; remove from overrides
            doc.Features.Remove(feature.ToString());
            if (doc.Features.Count == 0) doc.Features = null;
        }
        else
        {
            // Override the default
            doc.Features[feature.ToString()] = value;
        }

        // A feature can change how frames are rendered, so every render made
        // under the old setting is stale the moment the toggle lands.
        ClearFrameRenders();
        PublishSnapshot();

        _autosave.MarkDirty();
    }
}
