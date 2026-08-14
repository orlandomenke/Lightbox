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
    // ---- pinned ghosts ------------------------------------------------------

    /// <summary>Whether the playhead's frame is pinned as a ghost.</summary>
    public bool CurrentFrameIsGhost =>
        Scene.GhostFrames?.Contains(CurrentFrameIndex) == true;

    public string GhostPinLabel => CurrentFrameIsGhost ? "Unpin ghost" : "Pin as ghost";

    /// <summary>
    /// Pin or unpin the playhead's frame, so it stays ghosted wherever the
    /// playhead goes — the two sheets you leave on the pegs while drawing the
    /// breakdown between them.
    /// </summary>
    [RelayCommand]
    public void ToggleGhostFrame()
    {
        var index = CurrentFrameIndex;
        var scene = Scene;
        var pinned = scene.GhostFrames ?? [];
        if (!pinned.Remove(index)) pinned.Add(index);
        // Absent unless used, so a document that never pins writes no key.
        scene.GhostFrames = pinned.Count > 0 ? pinned : null;
        NotifyGhostPins();
    }

    [RelayCommand]
    public void ClearGhostFrames()
    {
        if (Scene.GhostFrames is null) return;
        Scene.GhostFrames = null;
        NotifyGhostPins();
    }

    private void NotifyGhostPins()
    {
        OnPropertyChanged(nameof(CurrentFrameIsGhost));
        OnPropertyChanged(nameof(GhostPinLabel));
        OnPropertyChanged(nameof(HasGhostFrames));
        MarkDocumentEdited();
        _publish.InvalidateWholeCanvas();
        PublishSnapshot();
    }

    public bool HasGhostFrames => Scene.HasGhostFrames;

    // ---- stroke stabilizer (input smoothing) -----------------------------------



    public IReadOnlyList<SmoothingMode> SmoothingChoices { get; } = Enum.GetValues<SmoothingMode>();

    /// <summary>
    /// Steadies the hand along the stroke in flight. Reset by <c>Begin</c> at every
    /// pen-down, so nothing carries between strokes.
    /// </summary>
    private readonly StrokeStabilizer _stabilizer = new();

    /// <summary>
    /// The settings the stabilizer should run with for the brush in hand.
    /// </summary>
    private BrushStabilisation EffectiveStabilisation =>
        CurrentToolSettings.Stabilisation ?? _appStabilisation;

    /// <summary>
    /// Does this brush steady the hand its own way, or follow the application?
    /// </summary>
    /// <remarks>
    /// Turning it on copies whatever is currently in effect, so ticking the box
    /// never changes how the brush draws — it only changes what the sliders are
    /// now editing. Turning it off drops the brush's copy and hands the sliders
    /// back to the application's, which is the way back to absent.
    /// </remarks>
    public bool BrushHasOwnStabilisation
    {
        get => CurrentToolSettings.Stabilisation is not null;
        set
        {
            if (value == BrushHasOwnStabilisation) return;
            CurrentToolSettings.Stabilisation = value ? EffectiveStabilisation.Clone() : null;
            OnPropertyChanged();
            NotifySmoothingProperties();
            NotifyPresetProperties();
            PersistBrushState();
        }
    }

    private void NotifySmoothingProperties()
    {
        OnPropertyChanged(nameof(SmoothingMode));
        OnPropertyChanged(nameof(SmoothingWindow));
        OnPropertyChanged(nameof(SmoothingStrength));
        OnPropertyChanged(nameof(LazyRadius));
        OnPropertyChanged(nameof(LazyRadiusForCursor));
        OnPropertyChanged(nameof(BrushHasOwnStabilisation));
    }

    // Each of these edits whichever settings are in effect — the brush's own
    // when it has them, the application's otherwise. One set of controls, and
    // the checkbox beside them says which they are pointed at.

    public SmoothingMode SmoothingMode
    {
        get => EffectiveStabilisation.Mode;
        set
        {
            if (EffectiveStabilisation.Mode == value) return;
            EffectiveStabilisation.Mode = value;
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
        get => EffectiveStabilisation.Window;
        set
        {
            var window = Math.Clamp((int)Math.Round(value) | 1, 3, 25);
            if (EffectiveStabilisation.Window == window) return;
            EffectiveStabilisation.Window = window;
            OnPropertyChanged();
            PersistBrushState();
        }
    }

    public double SmoothingStrength
    {
        get => EffectiveStabilisation.Strength;
        set
        {
            var strength = Math.Clamp(value, 0, 0.95);
            if (Math.Abs(EffectiveStabilisation.Strength - strength) < 0.001) return;
            EffectiveStabilisation.Strength = strength;
            OnPropertyChanged();
            PersistBrushState();
        }
    }

    public double LazyRadius
    {
        get => EffectiveStabilisation.LazyRadius;
        set
        {
            var radius = Math.Clamp(value, 4, 200);
            if (Math.Abs(EffectiveStabilisation.LazyRadius - radius) < 0.5) return;
            EffectiveStabilisation.LazyRadius = radius;
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
    [NotifyPropertyChangedFor(nameof(PlayPauseGlyph))]
    private bool _isPlaying;

    /// <summary>
    /// The playhead is being dragged along the ruler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Separate from <see cref="IsPlaying"/> rather than folded into it,
    /// because only one thing is shared.</b> Both mean "the sequence is moving",
    /// which is all the tile gate asks (<c>ScenePassBuilder</c>). Everything else
    /// differs: a scrub draws onion ghosts and folds the layer stack, playback
    /// does neither, and the transport, the audio tick and the profiling all
    /// belong to playback alone. A single flag would have made every one of those
    /// a scrub too.
    /// </para>
    /// <para>
    /// <b>Set by the ruler's drag, and it must fall back to false.</b> The ruler
    /// clears it on release <em>and</em> on capture-lost — a drag that ends
    /// because the window lost the pointer is the case that would otherwise leave
    /// the canvas composing through tiles indefinitely.
    /// </para>
    /// </remarks>
    [ObservableProperty]
    private bool _isScrubbing;

    /// <summary>
    /// Compose the still picture through the bounded route the moment the drag
    /// ends.
    /// </summary>
    /// <remarks>
    /// <b>This is what keeps the tile route's own justification true.</b> Tiles
    /// are licensed while the sequence is moving because the pyramid's resample
    /// difference below 100% zoom only exists for as long as the motion does. The
    /// last publish of a drag is a tiled one, and nothing else republishes when
    /// the pointer is released — the playhead has not moved, so no frame change
    /// fires. Without this the artist is left looking at a pyramid-resampled
    /// still, which is precisely the thing the gate promises never to leave on
    /// screen.
    /// </remarks>
    partial void OnIsScrubbingChanged(bool value)
    {
        if (!value) PublishSnapshot();
    }

    /// <summary>
    /// Playback walks the sheet in order, which is the one access pattern an
    /// LRU is worst at — it evicts the frames at the start to make room for
    /// the ones at the end, so coming round the loop finds everything it is
    /// about to need has just been thrown away (B28). Evicting the most recent
    /// instead keeps the head of the sheet resident and turns a zero hit rate
    /// into about half.
    /// </summary>
    /// <remarks>
    /// Only for the duration of the scan. While drawing, the frames an artist
    /// returns to are the ones they touched last, which is the opposite
    /// prediction and the reason LRU is the default.
    /// </remarks>
    /// <summary>What the frame cache is currently evicting by. Test seam.</summary>
    internal FrameBitmapCache.EvictionOrder FrameCacheEviction => _cache.Eviction;

    partial void OnIsPlayingChanged(bool value)
    {
        // Both caches: playback publishes through the tile store since Q62,
        // so the scan protection has to follow the frames wherever they live
        // (B182 — the tile half went unflipped for a while, and thrashed).
        var order = value
            ? FrameBitmapCache.EvictionOrder.MostRecent
            : FrameBitmapCache.EvictionOrder.LeastRecent;
        _cache.Eviction = order;
        _tileFrames.Eviction = order;

        // Cleared at the START of a run rather than the end, so a report always
        // describes the last thing the artist actually watched. Accumulating
        // across runs would average a cold first pass with warm later ones,
        // which is exactly the distinction B148 exists to make.
        if (value)
        {
            _tickProfile.Reset();
            _cache.ResetCounters();
        }

        // B152: the layer thumbnails were skipped for the whole run, so they are
        // showing whatever frame playback started on. Catching up once on the
        // stop costs one sweep; catching up per tick cost the frame rate.
        if (!value) RefreshLayerThumbs();
    }


    [ObservableProperty]
    private bool _sidebarVisible = true;

    // Which panels are open is the workspace's business now, not a set of
    // loose booleans. These stay because the View menu and a good deal of the
    // test suite already speak them, and forwarding is cheaper than renaming
    // both — but the layout is the single source of truth underneath.

    public bool ColorDockerVisible
    {
        get => Workspace.ColorDockerVisible;
        set => Workspace.ColorDockerVisible = value;
    }

    public bool SheetsDockerVisible
    {
        get => Workspace.SheetsDockerVisible;
        set => Workspace.SheetsDockerVisible = value;
    }

    public bool PaletteDockerVisible
    {
        get => Workspace.PaletteDockerVisible;
        set => Workspace.PaletteDockerVisible = value;
    }

    public bool GradientDockerVisible
    {
        get => Workspace.GradientDockerVisible;
        set => Workspace.GradientDockerVisible = value;
    }

    public bool ReferenceDockerVisible
    {
        get => Workspace.ReferenceDockerVisible;
        set => Workspace.ReferenceDockerVisible = value;
    }

    [RelayCommand]
    private void ToggleReferenceDocker() => ReferenceDockerVisible = !ReferenceDockerVisible;

    [RelayCommand]
    private void TogglePaletteDocker() => PaletteDockerVisible = !PaletteDockerVisible;

    [RelayCommand]
    private void ToggleGradientDocker() => GradientDockerVisible = !GradientDockerVisible;

    [RelayCommand]
    private void ToggleColorDocker() => ColorDockerVisible = !ColorDockerVisible;

    [RelayCommand]
    private void ToggleChannelsDocker() =>
        Workspace.ChannelsDockerVisible = !Workspace.ChannelsDockerVisible;

    [RelayCommand]
    private void ToggleSheetsDocker() => SheetsDockerVisible = !SheetsDockerVisible;

    [RelayCommand]
    private void ToggleProjectPanel() =>
        Workspace.SetVisible(Docking.DockPanelId.Project, !Workspace.ProjectPanelVisible);

    [RelayCommand]
    private void ToggleSymbolsPanel() =>
        Workspace.SetVisible(Docking.DockPanelId.Symbols, !Workspace.SymbolsPanelVisible);

    [RelayCommand]
    private void ToggleHistoryPanel() =>
        Workspace.SetVisible(Docking.DockPanelId.History, !Workspace.HistoryPanelVisible);

    [RelayCommand]
    private void ToggleLayersPanel() =>
        Workspace.SetVisible(Docking.DockPanelId.Layers, !Workspace.LayersPanelVisible);

    /// <summary>The toolbar's gear: always OPENS — a gear that closed the
    /// panel you were looking at would read as a broken button.</summary>
    [RelayCommand]
    private void OpenToolOptions() =>
        Workspace.SetVisible(Docking.DockPanelId.ToolOptions, true);

    [RelayCommand]
    private void ToggleToolOptionsDocker() =>
        Workspace.SetVisible(Docking.DockPanelId.ToolOptions, !Workspace.ToolOptionsDockerVisible);

    [RelayCommand]
    private void ToggleXsheetDocker() =>
        Workspace.SetVisible(Docking.DockPanelId.Xsheet, !Workspace.XsheetDockerVisible);

    [RelayCommand]
    private void ToggleGraphEditorDocker() =>
        Workspace.SetVisible(Docking.DockPanelId.GraphEditor, !Workspace.GraphEditorDockerVisible);

    /// <summary>Which side the docker sidebar collapses to / sits on.</summary>
    [ObservableProperty]
    private bool _sidebarOnRight = true;

    public bool TimelineVisible
    {
        get => Workspace.TimelineVisible;
        set => Workspace.TimelineVisible = value;
    }

    public ObservableCollection<Layer> LayerChoices { get; } = [];

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


    partial void OnActiveLayerIndexChanged(int value)
    {
        // "Carry on from where I stopped" stops being true on another layer.
        _lastStrokeEnd = null;
        PruneStrokeSelection();   // and neither is a line picked on the old one
        foreach (var row in LayerRows) row.IsActive = row.SceneIndex == value;
        SyncLayerSelectionToActive(value);
        OnPropertyChanged(nameof(FrameCells));
        OnPropertyChanged(nameof(TimelineTracks));
        OnPropertyChanged(nameof(TimelineFrameCount));
        OnPropertyChanged(nameof(GraphSeriesList));
        OnPropertyChanged(nameof(ActiveLayerOnion));
        // A different layer can refuse the tool the last one accepted.
        RefreshPointerIntent();
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

    /// <summary>How many frames the track timeline spans.</summary>
    public int TimelineFrameCount => Scene.FrameCount;

    /// <summary>
    /// The graph editor's curves: the camera's framing when one exists, and
    /// the measured spacing of the active layer's drawings — the chart the
    /// stroke record makes possible (Q58). Same lifetime as
    /// <see cref="TimelineTracks"/>: a fresh list on every relevant change.
    /// </summary>
    public IReadOnlyList<Lightbox.App.Controls.GraphSeries> GraphSeriesList
    {
        get
        {
            var list = new List<Lightbox.App.Controls.GraphSeries>();
            var n = Scene.FrameCount;

            if (Scene.Camera is { } camera)
            {
                var x = new double[n];
                var y = new double[n];
                var zoom = new double[n];
                var rot = new double[n];
                for (var f = 0; f < n; f++)
                {
                    var framing = CameraOps.At(camera, f, Scene.Width, Scene.Height);
                    x[f] = framing.X;
                    y[f] = framing.Y;
                    zoom[f] = framing.Zoom;
                    rot[f] = framing.RotationDeg;
                }
                var keys = CameraKeyFrames;
                list.Add(new("Camera X", Avalonia.Media.Color.Parse("#FF9F45"), x, keys, Editable: true));
                list.Add(new("Camera Y", Avalonia.Media.Color.Parse("#E8C55F"), y, keys, Editable: true));
                list.Add(new("Zoom", Avalonia.Media.Color.Parse("#4FA3FF"), zoom, keys, Editable: true));
                list.Add(new("Rotation", Avalonia.Media.Color.Parse("#E85FBE"), rot, keys, Editable: true));
            }

            var spacing = new double[n];
            Array.Fill(spacing, double.NaN);
            var spans = Lightbox.Core.Timeline.SpacingChart.Measure(ActiveLayer);
            var spanFrames = new List<int>();
            foreach (var span in spans)
            {
                if (span.Frame >= n) continue;
                spacing[span.Frame] = span.Distance;
                spanFrames.Add(span.Frame);
            }
            list.Add(new("Spacing (measured)", Avalonia.Media.Color.Parse("#2FD1B9"),
                spacing, spanFrames, Editable: false));

            // The intent laid over the measurement: the same travel,
            // redistributed by the easing picked on the X-sheet bar. The gap
            // between the hollow dots and the filled ones is the drawing
            // that misses the ease.
            var intended = new double[n];
            Array.Fill(intended, double.NaN);
            var wantFrames = new List<int>();
            foreach (var span in Lightbox.Core.Timeline.SpacingChart.Intended(ActiveLayer, TweenEasing))
            {
                if (span.Frame >= n) continue;
                intended[span.Frame] = span.Distance;
                wantFrames.Add(span.Frame);
            }
            list.Add(new("Spacing (intended)", Avalonia.Media.Color.Parse("#8FE8DC"),
                intended, wantFrames, Editable: false, Dashed: true));

            SyncGraphLegend(list);
            return list.Where(s => !_hiddenGraphSeries.Contains(s.Name)).ToList();
        }
    }

    /// <summary>
    /// Series the artist switched off in the graph's legend. By name, which
    /// survives the projection rebuilding its lists on every change.
    /// </summary>
    private readonly HashSet<string> _hiddenGraphSeries = [];

    /// <summary>The legend's rows — stable instances, synced by name.</summary>
    public ObservableCollection<GraphLegendItem> GraphLegend { get; } = [];

    internal void SetGraphSeriesShown(string name, bool shown)
    {
        if (shown ? _hiddenGraphSeries.Remove(name) : _hiddenGraphSeries.Add(name))
        {
            OnPropertyChanged(nameof(GraphSeriesList));
        }
    }

    /// <summary>
    /// Keep the legend's rows matching the series that exist, without
    /// replacing instances — a toggle mid-click must not be swapped out from
    /// under the pointer.
    /// </summary>
    private void SyncGraphLegend(IReadOnlyList<Lightbox.App.Controls.GraphSeries> all)
    {
        for (var i = GraphLegend.Count - 1; i >= 0; i--)
        {
            if (all.All(s => s.Name != GraphLegend[i].Name)) GraphLegend.RemoveAt(i);
        }
        foreach (var s in all)
        {
            if (GraphLegend.All(l => l.Name != s.Name))
            {
                GraphLegend.Add(new GraphLegendItem(this, s.Name,
                    new Avalonia.Media.SolidColorBrush(s.Colour), !_hiddenGraphSeries.Contains(s.Name)));
            }
        }
    }

    /// <summary>
    /// The graph editor's dot drag, applied: retime the key when the frame
    /// changed (refusing an occupied destination), then write the dragged
    /// value into whichever channel the series names.
    /// </summary>
    public void EditCameraKey(string series, int fromFrame, int toFrame, double value)
    {
        if (Scene.Camera is not { } camera) return;
        if (CameraOps.KeyAt(camera, fromFrame) is not { } key) return;

        if (toFrame != fromFrame && CameraOps.KeyAt(camera, toFrame) is null)
        {
            key.Frame = toFrame;
        }
        switch (series)
        {
            case "Camera X": key.X = value; break;
            case "Camera Y": key.Y = value; break;
            case "Zoom": key.Zoom = Math.Clamp(value, 0.05, 32); break;
            case "Rotation": key.RotationDeg = value; break;
            default: return;
        }
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>
    /// The exposure sheet and the camera, projected for the track timeline:
    /// the camera on top (as the reference draws it), then the layers,
    /// topmost first. A fresh list every read — assigning it is what tells
    /// the TrackView to re-render, so it is raised wherever the cels or the
    /// camera change.
    /// </summary>
    public IReadOnlyList<Lightbox.App.Controls.TrackRow> TimelineTracks
    {
        get
        {
            var tracks = new List<Lightbox.App.Controls.TrackRow>();
            if (Scene.Camera is not null)
            {
                var frames = CameraKeyFrames;
                tracks.Add(new Lightbox.App.Controls.TrackRow(
                    "Camera", frames, frames, frames.Select(_ => false).ToList(), IsCamera: true));
            }
            foreach (var row in LayerRows)
            {
                var keys = new List<int>();
                var holdEnds = new List<int>();
                var breakdowns = new List<bool>();
                for (var i = 0; i < row.Cells.Count; i++)
                {
                    var cell = row.Cells[i];
                    if (!cell.IsKeyed || cell.IsVirtual) continue;
                    keys.Add(cell.Index);
                    breakdowns.Add(cell.IsBreakdown);
                    // The hold runs until the next drawing (or the sheet's end).
                    var end = cell.Index;
                    for (var j = i + 1; j < row.Cells.Count; j++)
                    {
                        if (row.Cells[j].IsKeyed || row.Cells[j].IsVirtual) break;
                        end = row.Cells[j].Index;
                    }
                    holdEnds.Add(end);
                }
                tracks.Add(new Lightbox.App.Controls.TrackRow(row.Name, keys, holdEnds, breakdowns, IsCamera: false));
            }
            return tracks;
        }
    }

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
        if (ActiveLayer is not { } layer || layer.Cels.Count == 0) return null;
        var here = Math.Clamp(CurrentFrameIndex, 0, layer.Cels.Count - 1);
        // A cel that holds an earlier drawing is not a drawing of its own. What
        // happens when you mark on one is the single most consequential
        // decision the timeline makes, so it is a setting rather than a
        // hard-coded answer — see DrawingOnAHold.
        var holding = layer.Cels[here].Frame is null;
        if (!holding || DrawingOnAHold == HoldDrawing.EditTheHeldDrawing)
        {
            if (PaintTarget() is { } existing) return existing;
        }
        var index = here;
        var layerId = layer.Id;
        // The key must not change the picture. The new drawing carries a copy
        // of what the hold was showing, so the mark (or move, or erase) that
        // prompted the key is the only visible change — a cel that went blank
        // under the first touch read as the app losing the drawing. A layer
        // with no key at all still starts from nothing, which is the ordinary
        // way to start one.
        var fresh = KeyedCopyOf(PaintTarget()); // one class, so the layer's kind decides nothing here
        _editor.PerformDelta(
            apply: doc =>
            {
                if (CelIn(doc, layerId, index) is { } cel) cel.Frame = fresh;
            },
            revert: doc =>
            {
                if (CelIn(doc, layerId, index) is { } cel) cel.Frame = null;
            },
            label: "New drawing");
        return PaintTarget();
    }

    private static Cel? CelIn(Doc doc, string layerId, int index)
    {
        var layer = doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId);
        return layer is not null && index < layer.Cels.Count ? layer.Cels[index] : null;
    }

    /// <summary>
    /// The drawing a keyed hold starts from: everything the hold was showing —
    /// strokes, baseline pixels and placements — under a fresh frame id.
    /// </summary>
    /// <remarks>
    /// Not <c>DocumentEditor.CloneFrame</c>, which leaves placements behind (a
    /// recorded decision about duplicating a frame in time). Keying a hold is
    /// a promise that the picture does not change, and a placement that
    /// vanished from the cel under the first mark would break it. Fresh ids
    /// on the frame and its strokes, because two frames sharing them would
    /// cross their cached renders; placement ids survive the copy
    /// (<see cref="SymbolPlacement.Clone"/>), which is what lets a drag that
    /// keyed the cel keep hold of the placement it grabbed.
    /// </remarks>
    private static Frame KeyedCopyOf(Frame? held) => held is null ? new Frame() : new Frame
    {
        Role = held.Role,
        PngBase64 = held.PngBase64,
        Strokes = held.Strokes.Select(s => s.Clone()).ToList(),
        Placements = held.Placements?.Select(p => p.Clone()).ToList(),
    };

    /// <summary>
    /// Key a held cel because an edit is about to land on it, translating
    /// stroke ids from the drawing it was borrowing to the copy's own. Returns
    /// the drawing to write to, and rewrites <paramref name="ids"/> in place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Reading a stroke and writing one are different questions, and B207
    /// was the gap between them.</b> Picking must never author a cel —
    /// <see cref="PickableStrokes"/> says so and is right — but an edit that
    /// actually lands has to key, or it rewrites the drawing the hold borrows
    /// and the change appears on the earlier frame. So the resolution happens
    /// here, at the commit, rather than at the pick.
    /// </para>
    /// <para>
    /// <b>The translation is needed because the copy's strokes carry fresh
    /// ids</b>, which is the rule <see cref="Frame.Clone"/> documents: a
    /// snapshot keeps ids so undo can restore what was there, and a duplicate
    /// of a drawing an artist can see gets new ones. Position in the list is
    /// the bridge — <c>KeyedCopyOf</c> copies in order — and it is exact
    /// rather than a heuristic for the same reason.
    /// </para>
    /// <para>
    /// Returns the held drawing unchanged when the cel already has one of its
    /// own, which is the ordinary case and costs one reference comparison.
    /// </para>
    /// </remarks>
    private Frame? KeyHeldCelForStrokeEdit(IList<string> ids)
    {
        if (PaintTarget() is not { } held) return null;
        var indices = new int[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            indices[i] = held.Strokes.FindIndex(s => s.Id == ids[i]);
        }
        if (PaintTargetOrKey() is not { } target) return null;
        if (ReferenceEquals(target, held)) return target; // not a hold: nothing to translate
        for (var i = 0; i < ids.Count; i++)
        {
            var at = indices[i];
            if (at >= 0 && at < target.Strokes.Count) ids[i] = target.Strokes[at].Id;
        }
        return target;
    }

    /// <summary>Get placements from the current frame for selection feedback.</summary>
    public IReadOnlyList<SymbolPlacement>? GetCurrentFramePlacements()
    {
        if (PaintTargetOrKey() is Frame frame && frame.Placements is not null)
            return frame.Placements.AsReadOnly();
        return null;
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

    // Region of the scratch actually touched by the current stroke, so
    // pen-up can clear just that much instead of the whole canvas.

    // Blur brushes read the canvas underneath them, so they cannot be
    // composited from a separate scratch — they keep the copy-based path.












    // ---- live post-processing (medium, wet edge, texture, granulation) --------
    //
    // These are STROKE-GLOBAL: the wet edge is derived from the whole
    // silhouette and the fluid lattice flows across the whole wet area, so
    // running them per segment would rim and pool each segment separately —
    // visibly wrong, and not what commits. They have to be recomputed over the
    // whole stroke so far, which at 45–143 ms on a 4K canvas cannot happen on
    // every pointer event.
    //
    // So: raw dabs go into _live.Scratch immediately (2 ms, the pen never
    // lags), and a full render of the stroke-so-far lands in _live.PostScratch
    // as often as its own measured cost allows. The compositor shows the
    // rendered one when it exists. The artist sees the true mark converging a
    // fraction behind the tip rather than seeing flat dabs until pen-up, which
    // is how wet media behave in every tool that has them.

    /// <summary>How many times the live post-process has rendered. Tests only.</summary>
    internal int LivePostPasses { get; private set; }

    /// <summary>Total milliseconds spent in those passes. Tests only.</summary>
    internal double LivePostTotalMs { get; private set; }

    /// <summary>The most expensive single pass, for the render report (B189).</summary>
    internal double LivePostWorstMs { get; private set; }

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


    internal (double X, double Y)? LastStrokeEndForTests => _lastStrokeEnd;

    /// <param name="eraseWithCurrentBrush">
    /// Alt was held. The stroke erases but keeps the brush's own size, shape
    /// and dynamics — unlike switching to the eraser, which brings its own.
    /// </param>
    public void BeginStroke(double x, double y, double pressure, bool eraseWithCurrentBrush) =>
        BeginStroke(x, y, pressure, eraseWithCurrentBrush, joinFromLast: false);

    /// <param name="joinFromLast">
    /// Shift was held at the press. The stroke starts at the previous one's
    /// end and runs straight to here, which is how Photoshop draws a long
    /// straight without a ruler — and, chained, how a polyline gets drawn.
    /// </param>
    public void BeginStroke(
        double x, double y, double pressure, bool eraseWithCurrentBrush, bool joinFromLast)
    {
        // A mode that has taken the canvas takes it from every tool, not from
        // the ones the canvas control happens to route through itself. Half a
        // mark made while adjusting a grid is one you then have to find.
        if (SuppressesPainting) return;
        if (ActiveTool is not (ToolId.Brush or ToolId.Eraser)) return;
        if (IsPlaying) return;
        if (!CanEdit(ActiveLayer, "draw on it")) return;
        if (PaintTargetOrKey() is not { } target) return;
        // Drawing ends any run of palette edits, so the recolour lands on the
        // undo stack before the stroke does rather than after it.
        CommitSwatchEdit();
        // A stroke's guide is chosen once, from a direction it has committed
        // to. The anchor is where that direction is measured from, so it is
        // the unsnapped start — snapping the anchor first would measure the
        // heading from a point the hand never visited.
        _guideSnap.Begin(x, y);
        if (SnapToGuides && Scene.Guides is { Count: > 0 } startGuides)
        {
            (x, y) = Snapper.Point(startGuides, x, y, SnapTolerance);
            _guideSnap.Anchor(x, y);
        }
        // Shift+click: begin at the end of the last stroke and run straight to
        // the click. The segment is stamped now rather than on release, so the
        // mark is complete even if the artist never drags at all — which is
        // the whole gesture.
        var join = joinFromLast ? _lastStrokeEnd : null;
        var startX = join?.X ?? x;
        var startY = join?.Y ?? y;

        // Whichever brush is in hand decides how the hand is steadied, and it
        // is decided here rather than when a slider moves — switching brushes
        // mid-drawing has to change the smoothing with them.
        _stabilizer.Settings = EffectiveStabilisation;
        _stabilizer.Begin(startX, startY);
        _strokeBuilder.Begin(
            IsEraser || eraseWithCurrentBrush ? ToolKind.Eraser : ToolKind.Brush,
            ColorHex,
            CurrentToolSettings.Clone(),
            startX, startY, pressure,
            ActiveSwatchId);
        if (join is not null)
        {
            // Straight to the click, past the stabiliser: a segment the artist
            // asked to be straight must not be rounded off by smoothing.
            _strokeBuilder.Add(x, y, pressure);
            // It has a direction already; no guide may re-aim it.
            _guideSnap.HoldDirection(startX, startY);
        }
        // Live preview clips to the selection too (the registry already knows
        // the region; the document copy is added at commit).
        if (PrepareClipForSelection() is { } liveClip) _strokeBuilder.Current!.ClipId = liveClip.Id;
        // Stamped onto the stroke, not read from the layer at render time, so
        // unlocking the layer later cannot repaint what is already down.
        _strokeBuilder.Current!.AlphaLocked = ActiveLayer.AlphaLocked;

        _live.Composite?.Dispose();
        _live.Composite = null;
        _live.EffectBase?.Dispose();
        _live.EffectBase = null;
        if (CurrentToolSettings.Kind is BrushKind.Blur or BrushKind.Smudge)
        {
            // Blur and smudge read the pixels they sit on, so they need a real
            // copy of the layer to work into. Without this a smudge preview
            // stamps plain dabs of the foreground colour for the whole drag
            // and only snaps to the real smear on pen-up.
            //
            // Two copies, not one, and the second is the fix for B33. The
            // composite is written into; the base is never written and is what
            // every dab reads. The exact render gives all of a stroke's dabs
            // the same pre-stroke pixels, so a preview that sampled the
            // composite would re-apply the effect once per pointer event — a
            // blur of a blur of a blur, forty deep by the end of a drag.
            _live.EffectBase = _cache.Get(target, Scene.Width, Scene.Height).Copy();
            _live.Composite = _live.EffectBase.Copy();
        }
        else
        {
            _live.EnsureScratch(Scene.Width, Scene.Height);
            _live.ClearScratch();
        }
        _live.ResetPostProcess();
        _live.StampedCount = 0;
        _live.DabCount = 0;
        _live.StableDabs = 0;
        _live.TailRegion = null;
        _live.Dabs = null;
        _live.EffectDabs = null;
        _live.EffectSettled = 0;
        _live.SmudgeCarry = default;
        _live.SmudgeRegion = null;
        FlushLivePreview();
        PublishSnapshot();
    }

    // ---- the shape tool ------------------------------------------------------------

    /// <summary>Which shape the shape tool draws.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPolygonShape))]
    [NotifyPropertyChangedFor(nameof(ShapeGlyph))]
    private ShapeKind _activeShape = ShapeKind.Rectangle;

    /// <summary>Corners, when the shape is a polygon.</summary>
    [ObservableProperty]
    private int _polygonSides = 5;

    public bool IsPolygonShape => ActiveShape == ShapeKind.Polygon;

    /// <summary>The tool button's icon, so it says which shape is loaded.</summary>
    /// <remarks>
    /// A geometry from <see cref="Rendering.IconSet"/> rather than the box-drawing
    /// characters this used to return. Those were the system font's idea of a
    /// rectangle sitting next to eight hand-drawn monoline glyphs, at whatever
    /// weight and baseline that font happened to have.
    /// </remarks>
    public Avalonia.Media.Geometry? ShapeGlyph =>
        Rendering.IconSet.Resolve(Rendering.IconSet.ForShape(ActiveShape));

    /// <summary>Pick a shape, and make the shape tool active while you are at it.</summary>
    /// <remarks>
    /// Same bargain as the select variants: choosing one from the hold-list is
    /// a statement that you want to draw it, and making you click the tool
    /// again afterwards is a step with no decision in it.
    /// </remarks>
    [RelayCommand]
    private void SelectShape(ShapeKind kind)
    {
        ActiveShape = kind;
        ActiveTool = ToolId.Shape;
    }

    public IReadOnlyList<ShapeKind> ShapeChoices { get; } =
        [ShapeKind.Line, ShapeKind.Rectangle, ShapeKind.Ellipse, ShapeKind.Polygon];

    public bool IsShapeTool => ActiveTool == ToolId.Shape;


    private (double X, double Y) _shapeStart;

    /// <summary>
    /// Start a shape.
    /// </summary>
    /// <remarks>
    /// The corners are snapped like any other point, so a rectangle dropped on
    /// a grid lands on the grid — which is most of why anybody turns a grid on.
    /// </remarks>
    public void BeginShape(double x, double y)
    {
        if (ActiveTool != ToolId.Shape || IsPlaying) return;
        if (!CanEdit(ActiveLayer, "draw on it") || PaintTargetOrKey() is null) return;
        CommitSwatchEdit();

        if (SnapToGuides && Scene.Guides is { Count: > 0 } guides)
        {
            (x, y) = Snapper.Point(guides, x, y, SnapTolerance);
        }
        _shapeStart = (x, y);
        _liveShape = new Stroke
        {
            Tool = IsEraser ? ToolKind.Eraser : ToolKind.Brush,
            Color = ColorHex,
            SwatchId = ActiveSwatchId,
            PaletteId = ActivePaletteId,
            Brush = CurrentToolSettings.Clone(),
            Points = ShapeBuilder.Outline(ActiveShape, x, y, x, y, sides: PolygonSides),
            AlphaLocked = ActiveLayer.AlphaLocked,
            Label = ActiveShape.ToString().ToLowerInvariant(),
        };
        if (PrepareClipForSelection() is { } clip) _liveShape.ClipId = clip.Id;

        _live.EnsureScratch(Scene.Width, Scene.Height);
        RenderShapePreview();
        PublishSnapshot();
    }

    /// <param name="fromCentre">Alt: grow from the first corner rather than to it.</param>
    /// <param name="regular">Shift: a square, a circle, a regular polygon.</param>
    public void MoveShape(double x, double y, bool fromCentre = false, bool regular = false)
    {
        if (_liveShape is not { } stroke) return;
        if (SnapToGuides && Scene.Guides is { Count: > 0 } guides)
        {
            (x, y) = Snapper.Point(guides, x, y, SnapTolerance);
        }
        stroke.Points = ShapeBuilder.Outline(
            ActiveShape, _shapeStart.X, _shapeStart.Y, x, y, fromCentre, regular, PolygonSides);
        RenderShapePreview();
        RequestSnapshot();
    }

    public void EndShape(double x, double y, bool fromCentre = false, bool regular = false)
    {
        if (_liveShape is not { } stroke) return;
        if (SnapToGuides && Scene.Guides is { Count: > 0 } guides)
        {
            (x, y) = Snapper.Point(guides, x, y, SnapTolerance);
        }
        stroke.Points = ShapeBuilder.Outline(
            ActiveShape, _shapeStart.X, _shapeStart.Y, x, y, fromCentre, regular, PolygonSides);
        CancelShape();

        if (PaintTarget() is not { } target) return;
        // A click with no drag is not a shape. Committing it would leave a
        // single dab where the artist expected a rectangle.
        var dx = x - _shapeStart.X;
        var dy = y - _shapeStart.Y;
        if (dx * dx + dy * dy < 1.0)
        {
            AiStatus = "Drag to size the shape.";
            return;
        }

        var clip = PrepareClipForSelection();
        if (clip is not null) stroke.ClipId = clip.Value.Id;
        FreezeSampledBackdrop(stroke);
        RememberDocumentBrush();
        AppendToFrameRender(target, stroke);

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
        _publish.InvalidateWholeCanvas();
        PublishSnapshot();
    }

    public void CancelShape()
    {
        if (_liveShape is null) return;
        _liveShape = null;
        _live.ClearScratch();
        _publish.InvalidateWholeCanvas();
        PublishSnapshot();
    }

    private void RenderShapePreview()
    {
        if (_liveShape is not { } stroke || _live.ScratchCanvas is null) return;
        _live.ClearScratch();
        var info = new SKImageInfo(Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        // The same stroke the commit will record, at full opacity — the
        // overlay applies the brush's own, so baking it here would double it.
        var preview = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = stroke.Color,
            SwatchId = stroke.SwatchId,
            PaletteId = stroke.PaletteId,
            ClipId = stroke.ClipId,
            Brush = stroke.Brush.Clone(),
            Points = [.. stroke.Points],
        };
        preview.Brush.Opacity = 1;
        BrushEngine.StampStroke(_live.ScratchCanvas, preview, info);
        _live.ScratchCanvas.Flush();
        _live.ScratchUsed = new SKRectI(0, 0, Scene.Width, Scene.Height);
        _publish.InvalidateWholeCanvas();
    }

    public void CancelGradient()
    {
        if (_liveGradient is null) return;
        _liveGradient = null;
        _live.ClearScratch();
        GradientAxisChanged?.Invoke(null, null);
        _publish.InvalidateWholeCanvas();
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
        if (_liveGradient is not { } stroke || _live.ScratchCanvas is null) return;
        _live.ClearScratch();
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
        BrushEngine.StampStroke(_live.ScratchCanvas, preview, info);
        _live.ScratchCanvas.Flush();
        _live.ScratchUsed = new SKRectI(0, 0, Scene.Width, Scene.Height);
        GradientAxisChanged?.Invoke(
            (stroke.Points[0].X, stroke.Points[0].Y), (stroke.Points[1].X, stroke.Points[1].Y));
        _publish.InvalidateWholeCanvas();
    }

    /// <summary>All coalesced samples of one pointer event → one stamp + one (coalesced) repaint.</summary>
    public void MoveStrokeBatch(IReadOnlyList<PointerSample> samples)
    {
        if (!_strokeBuilder.IsActive) return;
        // B189: clocked on arrival so the render report can price the whole
        // pen→screen chain on the artist's machine, not just the stamp.
        var arrived = Rendering.StrokeToScreen.EventArrived();
        foreach (var s in samples)
        {
            var (fx, fy) = _stabilizer.FilterLive(s.X, s.Y);
            var (x, y) = Guided(fx, fy);
            _strokeBuilder.Add(x, y, s.Pressure);
        }
        if (_stabilizer.BrushPosition is { } anchor) LazyBrushMoved?.Invoke(anchor.X, anchor.Y);
        FlushLivePreview();
        Rendering.StrokeToScreen.Shared.Stamped(arrived);
        RequestSnapshot();
    }

    public void MoveStroke(double x, double y, double pressure) =>
        MoveStrokeBatch([new PointerSample(x, y, pressure)]);

    /// <summary>
    /// Stamp the dabs of the live stroke that are not in the preview yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The whole stroke goes to the engine every time, and only the new dabs are
    /// drawn.</b> This used to hand over a two-point <c>tail</c> instead, which was the
    /// cause of B45: the dab walk carries a spacing phase, a travelled distance, a
    /// heading and a step size, and a tail restarts all four. An artist saw that as the
    /// mark changing the instant they let go — denser live than committed, paint load that
    /// only depleted on release, and a tip texture that jumped because dabs landing
    /// elsewhere are seeded elsewhere.
    /// </para>
    /// <para>
    /// The tail survives for <em>bounds</em>, which is a different question: what changed
    /// on screen since the last event is genuinely the segment, and repainting the whole
    /// stroke's region every event would break invariant 6.
    /// </para>
    /// </remarks>
    private void FlushLivePreview()
    {
        if (_strokeBuilder.Current is not { } live) return;
        var points = live.Points;
        if (_live.StampedCount >= points.Count) return;

        var from = Math.Max(0, _live.StampedCount - 1); // overlap one point so segments connect
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

        if (_live.Composite is not null)
        {
            // The pristine base is for BLUR only, and the distinction is the
            // physics rather than a detail. A blur re-derives its dab from the
            // pre-stroke pixels, so every dab of a stroke must see the same
            // source or the effect compounds once per pointer event (B33). A
            // smudge *carries*: each dab has to see the deposits the previous
            // ones left, which is how a smear travels at all — the committed
            // path gives it the very bitmap it is writing, and the draft has to
            // match or the preview stops matching the commit.
            // The whole stroke, walked with the shared densify cache, so the dabs land where the
            // commit will put them — a per-segment walk restarts the spacing phase and Densify sees
            // two points, which was 1148 px of over-coverage on the blur (B54) and the same defect
            // on the smudge (B69/B89). Only the dabs that are not settled yet are stamped.
            var walk = BrushEngine.WalkDabs(live, _live.Densify);
            var settled = BrushEngine.StableCount(walk, _live.EffectDabs);

            if (live.Brush.Kind == BrushKind.Blur)
            {
                // A blur's dabs are independent — each reads the pre-stroke pixels — so the range
                // is all it needs, and StampBlurDraft restores under the tail itself.
                FrameRasterizer.AppendDraft(_live.Composite, live, _live.EffectBase, walk, _live.EffectSettled);
            }
            else
            {
                StampLiveSmudge(live, walk, settled);
            }

            _live.EffectSettled = settled;
            _live.EffectDabs = walk;
        }
        else if (_live.ScratchCanvas is not null)
        {
            // Dabs only — no opacity, no layer copy. The compositor lays the
            // scratch over the layer and applies the stroke's opacity once,
            // so self-crossings look identical live and committed.
            //
            // The whole stroke, with the dabs already in the scratch skipped: the walk
            // has to run from the start for its phase, travel and heading to match the
            // commit, and only the drawing is incremental.
            StampLiveDabs(live, info);
            if (segment is { } used)
            {
                _live.ScratchUsed = _live.ScratchUsed is { } prior ? LivePaintSession.UnionRect(prior, used) : used;
            }
        }
        // Only the segment's neighbourhood changed on screen.
        if (segment is { } rect) _publish.MarkDirty(rect);
        else _publish.InvalidateWholeCanvas();
        _live.StampedCount = points.Count;

        if (_live.Composite is null && NeedsLivePostProcess(live.Brush)) RequestLivePostProcess();
    }

    /// <summary>
    /// Bring the smudge composite up to date: settled dabs permanently, the provisional tail on
    /// loan, and the carried colour checkpointed at the boundary between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B69/B89.</b> This used to be a per-segment <c>AppendDraft</c>, which restarted the dab
    /// walk's spacing phase, the carried colour and the heading on every pointer event, and
    /// re-smeared the one-point overlap where segments join. All four differences appeared at once
    /// when the pen lifted and the whole stroke was re-rendered from the record.
    /// </para>
    /// <para>
    /// The three steps are ordered exactly as <see cref="StampLiveDabs"/>'s are, and for the same
    /// reason — dabs have to reach the surface in index order for the accumulation to match a
    /// single pass. Take back the old tail, extend the settled prefix, lend the new tail.
    /// </para>
    /// <para>
    /// <b>Why this is exact rather than merely closer.</b> After the restore the composite holds
    /// precisely "pre-stroke pixels + dabs 0..settled-1", and <see cref="_live.SmudgeCarry"/> is the
    /// colour a single pass would be carrying at that index. Replaying the rest is then the same
    /// sequence the commit runs. Reads that reach outside the restored region — a smudge samples up
    /// to <c>radius × SmudgeRadius</c> away — can only touch settled pixels, which are already
    /// final.
    /// </para>
    /// <para>
    /// <b>And why not simply replay every dab each event</b>, which is exact by construction: the
    /// blur measured that at 194 ms an event by the 300th point against 20.8 ms for the settled
    /// range, and <c>LerpDab</c> is a per-pixel loop with the same shape of cost. Invariant 6 says
    /// no.
    /// </para>
    /// </remarks>
    private void StampLiveSmudge(Stroke live, IReadOnlyList<BrushEngine.Dab> dabs, int settled)
    {
        if (_live.Composite is not { } composite) return;
        var info = new SKImageInfo(
            composite.Width, composite.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(composite);

        // 1. Take back the tail lent out last time, so the composite is the settled prefix again.
        //    Only the part of the buffer that tail used — the backup is sized to the largest seen,
        //    so drawing all of it would scale a bigger image into a smaller rect.
        if (_live.SmudgeRegion is { } lent && _live.SmudgeBackup is not null)
        {
            using var restore = SKImage.FromBitmap(_live.SmudgeBackup);
            using var src = new SKPaint { BlendMode = SKBlendMode.Src };
            canvas.DrawImage(
                restore,
                new SKRect(0, 0, lent.Width, lent.Height),
                new SKRect(lent.Left, lent.Top, lent.Right, lent.Bottom),
                src);
            canvas.Flush();
            _live.SmudgeRegion = null;
        }

        // 2. Everything whose position has stopped moving, permanently — and the carry it ends on
        //    becomes the checkpoint the next event resumes from.
        if (settled > _live.EffectSettled)
        {
            _live.SmudgeCarry = BrushEngine.StampSmudgeRange(
                canvas, composite, live, dabs, _live.EffectSettled, settled, _live.SmudgeCarry);
        }

        // 3. The rest on loan, so the smear reaches the pen tip. Backed up first, because a smudge
        //    reads what it writes: re-stamping these next event without taking them back would
        //    compound the smear instead of replacing it.
        if (BrushEngine.RangeBounds(dabs, settled, live.Brush, info) is { } tail)
        {
            canvas.Flush();
            if (_live.SmudgeBackup is null
                || _live.SmudgeBackup.Width < tail.Width || _live.SmudgeBackup.Height < tail.Height)
            {
                _live.SmudgeBackup?.Dispose();
                _live.SmudgeBackup = new SKBitmap(new SKImageInfo(
                    Math.Max(tail.Width, 64), Math.Max(tail.Height, 64),
                    SKColorType.Rgba8888, SKAlphaType.Premul));
            }
            // A real copy, not a subset view: SKBitmap.ExtractSubset SHARES the source's pixels, so
            // using it as the backup would make it track the composite and the rollback a no-op.
            // The subset is taken first and only then wrapped, so no full-canvas SKImage is built.
            using (var region = new SKBitmap())
            {
                if (composite.ExtractSubset(region, tail))
                {
                    using var pixels = region.PeekPixels();
                    using var view = pixels is null ? null : SKImage.FromPixels(pixels);
                    if (view is not null)
                    {
                        using var into = new SKCanvas(_live.SmudgeBackup);
                        using var src = new SKPaint { BlendMode = SKBlendMode.Src };
                        into.DrawImage(view, 0, 0, src);
                        into.Flush();
                        _live.SmudgeRegion = tail;
                    }
                }
            }

            // The returned carry is deliberately dropped: these dabs are provisional, so the
            // checkpoint must stay at the settled boundary.
            BrushEngine.StampSmudgeRange(
                canvas, composite, live, dabs, settled, dabs.Count, _live.SmudgeCarry);
            canvas.Flush();
        }
    }

    /// <summary>
    /// Bring the scratch up to date with the stroke: settled dabs permanently, the
    /// provisional tail on loan.
    /// </summary>
    /// <remarks>
    /// The three steps are ordered the way they are because the dabs have to reach the
    /// scratch in index order for the accumulation to match a single-pass render: take
    /// back the old tail, add whatever became settled, then lend the new tail.
    /// </remarks>
    private void StampLiveDabs(Stroke live, SKImageInfo info)
    {
        if (_live.ScratchCanvas is null) return;

        // One walk, then every question answered from its result.
        // The walk reuses the densified prefix rather than rebuilding it, which is B46: the whole
        // stroke has to be walked every pointer event (BR1) and re-densifying it was 0.84 ms of a
        // 1.15 ms walk at 600 points, all but a fraction of it recomputing spans that cannot have
        // changed.
        var dabs = BrushEngine.WalkDabs(live, _live.Densify);
        var stable = BrushEngine.StableCount(dabs, _live.Dabs);
        _live.Dabs = dabs;

        // 1. Take back the tail lent out last time. Only the part of the buffer this
        // tail actually used: the backup is sized to the largest tail seen, so drawing
        // the whole thing would scale a bigger image into a smaller rect.
        if (_live.TailRegion is { } lent && _live.TailBackup is not null)
        {
            using var restore = SKImage.FromBitmap(_live.TailBackup);
            using var src = new SKPaint { BlendMode = SKBlendMode.Src };
            _live.ScratchCanvas.DrawImage(
                restore,
                new SKRect(0, 0, lent.Width, lent.Height),
                new SKRect(lent.Left, lent.Top, lent.Right, lent.Bottom),
                src);
            _live.ScratchCanvas.Flush();
            _live.TailRegion = null;
        }

        // 2. Everything whose position has stopped moving, permanently.
        BrushEngine.StampDabRange(_live.ScratchCanvas, live, dabs, _live.StableDabs, stable);
        _live.StableDabs = Math.Max(_live.StableDabs, Math.Min(stable, dabs.Count));

        // 3. The rest on loan, so the mark reaches the pen tip.
        if (BrushEngine.RangeBounds(dabs, _live.StableDabs, live.Brush, info) is { } tail
            && _live.Scratch is not null)
        {
            _live.ScratchCanvas.Flush();
            if (_live.TailBackup is null
                || _live.TailBackup.Width < tail.Width || _live.TailBackup.Height < tail.Height)
            {
                _live.TailBackup?.Dispose();
                _live.TailBackup = new SKBitmap(new SKImageInfo(
                    Math.Max(tail.Width, 64), Math.Max(tail.Height, 64),
                    SKColorType.Rgba8888, SKAlphaType.Premul));
            }
            // A real copy, not a subset view. SKBitmap.ExtractSubset hands back a bitmap
            // that SHARES the source's pixels, so using it as the backup made it track the
            // scratch and the rollback a no-op — the tail accumulated instead of being
            // taken back, which measured as the live mark 9% heavier than the commit.
            //
            // The subset is taken FIRST and only then wrapped, which is the same trap
            // PostProcessDabs records: SKImage.FromBitmap on the whole scratch sets up a
            // 33 MB image at 4K, every pointer event, to read back a region a few hundred
            // pixels across.
            using (var region = new SKBitmap())
            {
                if (_live.Scratch.ExtractSubset(region, tail))
                {
                    using var pixels = region.PeekPixels();
                    using var view = pixels is null ? null : SKImage.FromPixels(pixels);
                    if (view is not null)
                    {
                        using var into = new SKCanvas(_live.TailBackup);
                        using var src = new SKPaint { BlendMode = SKBlendMode.Src };
                        into.DrawImage(view, 0, 0, src);
                        into.Flush();
                        _live.TailRegion = tail;
                    }
                }
            }

            BrushEngine.StampDabRange(_live.ScratchCanvas, live, dabs, _live.StableDabs, dabs.Count);
            _live.ScratchCanvas.Flush();
        }
        _live.DabCount = dabs.Count;
    }

    /// <summary>
    /// Ask for a re-render of the stroke so far.
    ///
    /// Scheduling is left to the dispatcher rather than to a wall-clock
    /// throttle of our own. Background priority yields to pointer input and to
    /// the Default-priority snapshot, so during a fast drag the pass starts in
    /// whatever gaps exist and during a pause it starts immediately — which is
    /// exactly the cadence wanted, and the dispatcher already knows how busy
    /// the thread is. A cost-based throttle was tried first and was worse in
    /// the way that matters: it blocked the pass that would have settled the
    /// preview, so the mark froze part-drawn until the pen lifted.
    ///
    /// Only one pass is ever outstanding — the flag now spans the whole
    /// start → worker → finish arc, not just the dispatcher post — and a pass
    /// with nothing new to draw returns immediately.
    /// </summary>
    private void RequestLivePostProcess()
    {
        if (_live.PostQueued) return;
        _live.PostQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(
            StartLivePostProcess, Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// Runs a live post-process work item off the UI thread. A seam because
    /// the tests need the pass deterministic — they swap in a synchronous
    /// runner, for the same reason the pacing tests drive
    /// <see cref="NoteFramePresented"/> by hand.
    /// </summary>
    internal Func<Action, System.Threading.Tasks.Task> LivePostRunner { get; set; }
        = work => System.Threading.Tasks.Task.Run(work);

    /// <summary>
    /// Snapshot the pass's inputs and hand the effects to a worker (B189).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The second capture is why this is asynchronous.</b> The pass ran on
    /// the UI thread and measured 20.13 ms mean, 196.6 ms worst, 307 times in
    /// one wet drawing session — six seconds of blocked input, sitting exactly
    /// in the event → publish segment the pen-to-screen instrument showed
    /// growing. The simulation itself is untouched: the same
    /// <c>PostProcessRegion</c> arithmetic runs over copies, and
    /// <c>ACroppedRegionPassIsBitIdenticalToTheFullOne</c> holds the output to
    /// bit-equality with the synchronous path, because live-versus-commit
    /// equality is the bar the effect-brush work settled on (B69/B89).
    /// </para>
    /// <para>
    /// <b>Everything shared is copied here, on the thread that owns it</b> —
    /// the stroke (points and brush both; the artist can retune a slider
    /// mid-stroke), the stroke's region of the dab scratch, and the region of
    /// the layer beneath that re-wetting samples
    /// (<see cref="Media.MediumSimulator.ExistingRegionNeeded"/> is the
    /// contract for how much). The worker touches only its copies, so the UI
    /// thread keeps stamping dabs and the frame cache keeps evicting without
    /// a lock anywhere.
    /// </para>
    /// </remarks>
    private void StartLivePostProcess()
    {
        if (_strokeBuilder.Current is not { } live || !_strokeBuilder.IsActive
            || !NeedsLivePostProcess(live.Brush)
            || _live.PostStampedCount == live.Points.Count // nothing new since last pass
            // The pass reads the dabs from the live scratch; the blur and
            // smudge brushes use the copy-based path instead and have none.
            || _live.Scratch is not { } dabs)
        {
            _live.PostQueued = false;
            return;
        }

        var info = new SKImageInfo(Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);

        // The same stroke, minus the opacity the compositor applies — the
        // masks stay on the overlay so they cannot be baked in twice.
        var whole = new Stroke
        {
            Tool = live.Tool,
            Color = live.Color,
            SwatchId = live.SwatchId,
            Brush = live.Brush.Clone(),
            Points = [.. live.Points],
        };
        var count = whole.Points.Count;

        if (BrushEngine.PostProcessBounds(whole, info) is not { } rect
            || CopyRegion(dabs, rect) is not { } dabsCrop)
        {
            _live.PostQueued = false;
            return;
        }

        // targetPixels is the committed layer: the medium re-wets what is
        // already there, exactly as it will on commit. Copied because the
        // frame cache owns the original and may evict it mid-pass.
        SKBitmap? beneathCrop = null;
        var beneathOrigin = default(SKPointI);
        if (PaintTarget() is { } frame
            && _cache.Get(frame, Scene.Width, Scene.Height) is { } beneath)
        {
            var needed = Lightbox.Raster.Media.MediumSimulator.ExistingRegionNeeded(
                rect, Scene.Width, Scene.Height);
            beneathCrop = CopyRegion(beneath, needed);
            if (beneathCrop is not null) beneathOrigin = needed.Location;
        }

        var generation = _live.PostGeneration;
        try
        {
            LivePostRunner(Work);
        }
        catch
        {
            // A runner that cannot even schedule must not wedge the
            // single-flight flag — that would end wet previews for the rest of
            // the session (found by the adversarial pass). Disposing here can
            // double up with the worker's own finally when a synchronous
            // runner ran the work before throwing; SKBitmap.Dispose is
            // idempotent, so that costs nothing.
            _live.PostQueued = false;
            dabsCrop.Dispose();
            beneathCrop?.Dispose();
        }
        return;

        void Work()
        {
            SKImage? processed = null;
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                // The dabs are already stamped, so the pass runs the effects
                // over them rather than re-stamping every dab — the cost stops
                // growing with the length of the stroke.
                processed = BrushEngine.PostProcessRegion(
                    dabsCrop, whole, rect, beneathCrop, rect.Location, beneathOrigin);
            }
            catch
            {
                // A preview must never take the application down; the raw dabs
                // stay visible and the next pass tries again.
            }
            finally
            {
                dabsCrop.Dispose();
                beneathCrop?.Dispose();
            }
            var costMs = (System.Diagnostics.Stopwatch.GetTimestamp() - started)
                         * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            // Input priority, not Background: the finish is a few blits, and a
            // finish starved by continuous pointer input would hold the
            // single-flight flag and stop the next pass from ever starting.
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => FinishLivePostProcess(processed, rect, count, generation, costMs, info),
                Avalonia.Threading.DispatcherPriority.Input);
        }
    }

    /// <summary>
    /// Take the worker's result back onto the preview, or drop it if the
    /// stroke it belongs to is already over.
    /// </summary>
    private void FinishLivePostProcess(
        SKImage? processed, SKRectI rect, int count, int generation, double costMs,
        SKImageInfo computedAgainst)
    {
        _live.PostQueued = false;
        using var image = processed;
        if (image is null) return;
        if (generation != _live.PostGeneration || !_strokeBuilder.IsActive) return;
        // The document changed size while the worker ran — a mid-stroke canvas
        // resize does not go through ClearLiveEffectState, so the generation
        // stamp cannot see it (found by the adversarial pass, reproduced with
        // image.resizeCanvas fired mid-stroke). The result was computed from
        // copies of the old-sized canvas and its rect describes a position in
        // it; pasting that into the new one puts the preview at the wrong
        // place. Checked against the size the copies were TAKEN at, so it
        // catches every size-changing mutation whatever path it took.
        if (Scene.Width != computedAgainst.Width || Scene.Height != computedAgainst.Height) return;

        var info = new SKImageInfo(Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        if (_live.PostScratch is null || _live.PostScratch.Width != info.Width || _live.PostScratch.Height != info.Height)
        {
            _live.PostScratch?.Dispose();
            _live.PostScratch = new SKBitmap(info);
            _live.PostUsed = null;
        }

        using (var canvas = new SKCanvas(_live.PostScratch))
        {
            LivePaintSession.ClearRegion(canvas, _live.PostUsed);
            using var replace = new SKPaint { BlendMode = SKBlendMode.Src };
            canvas.DrawImage(image, rect.Left, rect.Top, replace);
            canvas.Flush();
        }

        _live.PostCostMs = costMs;
        _live.PostStampedCount = count;
        _live.PostUsed = rect;
        LivePostPasses++;
        LivePostTotalMs += costMs;
        if (costMs > LivePostWorstMs) LivePostWorstMs = costMs;

        _publish.MarkDirty(rect);
        // Through the coalescing path, not straight to PublishSnapshot. A
        // direct publish here put an extra frame on the wire for every pass on
        // top of the one the pointer event had already queued, and publishing
        // faster than the compositor draws is what let the canvas free an
        // image out from under it.
        RequestSnapshot();

        // Points arrived while this pass was rendering: go round again so the
        // preview settles on the whole stroke rather than stopping wherever
        // the pen happened to be when the pass started.
        if (_strokeBuilder.Current is { } now && now.Points.Count != count)
        {
            RequestLivePostProcess();
        }
    }

    /// <summary>A real copy — <c>ExtractSubset</c> alone shares the pixels.</summary>
    private static SKBitmap? CopyRegion(SKBitmap src, SKRectI r)
    {
        if (r.Width <= 0 || r.Height <= 0) return null;
        var bmp = new SKBitmap(new SKImageInfo(r.Width, r.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var sub = new SKBitmap();
        if (!src.ExtractSubset(sub, r)) { bmp.Dispose(); return null; }
        using var px = sub.PeekPixels();
        using var view = px is null ? null : SKImage.FromPixels(px);
        if (view is null) { bmp.Dispose(); return null; }
        // Scoped after the early returns above: a `using var` here would
        // dispose the canvas AFTER those paths had already disposed its
        // bitmap, which is a use-after-free inside Skia's teardown.
        using (var canvas = new SKCanvas(bmp))
        {
            using var replace = new SKPaint { BlendMode = SKBlendMode.Src };
            canvas.DrawImage(view, 0, 0, replace);
            canvas.Flush();
        }
        return bmp;
    }

    /// <summary>
    /// Coalesce repaints: at most one queued snapshot at a time, published after the pointer events
    /// already waiting rather than in between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Input priority, and B73 is why.</b> Avalonia runs
    /// <c>Render &gt; Loaded &gt; Default &gt; Input &gt; Background</c> — <c>Default</c> is
    /// <em>above</em> <c>Input</c> here, which is the reverse of WPF, where it sits below. This was
    /// posted at <c>Default</c>, so a publish jumped ahead of every pen event already in the queue.
    /// Two consequences, and the artist feels them as one thing:
    /// </para>
    /// <para>
    /// The frame drawn was <b>already behind</b> — published before a single queued event had been
    /// handled, so the ink on screen stopped where the pen had been several events ago. And because
    /// the publish ran between events instead of after them, a burst of <em>n</em> events produced
    /// <em>n</em> publishes rather than one: measured at <b>11 events → 11 publishes</b>. A publish
    /// is the expensive half, so the faster the stroke the more the work multiplied, and the lag
    /// compounded along it. That is why the report was about <em>fast</em> strokes specifically.
    /// </para>
    /// <para>
    /// <c>Input</c> is right rather than merely lower because that queue is FIFO: this lands behind
    /// the events already waiting, so one frame covers the burst and is current — and ahead of
    /// events that arrive afterwards, so a continuous drag still renders. <c>Background</c> also
    /// drains the burst and can be starved by continuous input, which is the state an artist is in
    /// for the whole of a long stroke.
    /// </para>
    /// <para>
    /// <b>Never Render priority</b>, whatever the latency argument: jobs in the dispatcher's render
    /// phase swallow the <c>InvalidateVisual</c> they trigger, which leaves the canvas permanently
    /// un-scheduled — strokes appeared only after the next unrelated event, the "frozen cursor, no
    /// lines" bug. <c>StrokeLatencyTests</c> guards the priority from the other side.
    /// </para>
    /// </remarks>
    /// <summary>A coalesced publish is already posted, so this event joins it.</summary>
    private bool _snapshotQueued;

    private void RequestSnapshot()
    {
        if (_snapshotQueued) return;
        // B189's other half: pace the publish to the canvas's consumption.
        // B73 made one publish cover a burst of events; this makes the bursts
        // themselves wait for the screen. Without it the first capture showed
        // publishing outrunning drawing about 2:1 at 4K — 935 of 1921 ink
        // publishes replaced before anything drew them, each having spent
        // ~27 ms of UI thread composing a frame nobody saw, while drawn
        // frames queued a mean of 93 ms. Deferring until the last publish is
        // actually on screen keeps at most one frame in flight, so a drawn
        // frame waits about one compositor pass, and the compose time of the
        // frames that would have been thrown away is handed back to the
        // dispatcher — which is what was making everything else late.
        if (_publish.CanvasIsBehind(PublishDamMs))
        {
            _publish.WaitingForPresent = true;
            ArmPublishDam();
            return;
        }
        _snapshotQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _snapshotQueued = false;
            PublishSnapshot();
        }, Avalonia.Threading.DispatcherPriority.Input);
    }

    /// <summary>
    /// Milliseconds a deferred publish will wait on a canvas that has stopped
    /// drawing before going ahead anyway.
    /// </summary>
    /// <remarks>
    /// The liveness bound, not the pacing: a hidden or minimised window draws
    /// nothing, and a publish dammed behind it forever would mean the live
    /// stroke never reaches the document's own snapshot pipeline again until
    /// pen-up. A quarter second is far above any real present interval, so it
    /// only fires when presentation has genuinely stopped.
    /// </remarks>
    internal const double PublishDamMs = 250;

    /// <summary>
    /// The canvas has drawn a published frame (see
    /// <see cref="Rendering.CanvasControl.SnapshotPresented"/>). Releases the
    /// deferred publish, if one is waiting and this is the frame it waited on.
    /// </summary>
    internal void NoteFramePresented(long seq)
    {
        if (!_publish.NotePresented(seq)) return;
        // Through RequestSnapshot rather than straight to PublishSnapshot, so
        // the released publish still lands behind whatever pointer events are
        // already queued — B73's ordering, preserved under pacing.
        RequestSnapshot();
    }

    /// <summary>
    /// Make the dam self-releasing: a one-shot timer that flushes a deferral
    /// the canvas never comes back for.
    /// </summary>
    /// <remarks>
    /// The adversarial pass on the first draft found the hole this closes: the
    /// dam was only ever <em>checked</em>, inside <see cref="RequestSnapshot"/>,
    /// so a deferral whose interaction produced no further event — the last
    /// pointer move of a drag over a canvas that then stopped presenting — was
    /// released by nothing at all, and "250 ms at most" was quietly "until the
    /// next event, however long that is". The timer is armed once per deferral
    /// spell and re-arms itself only if the release finds the canvas behind
    /// again, so an idle application holds no ticking timer.
    /// </remarks>
    private void ArmPublishDam()
    {
        if (_publish.DamArmed) return;
        _publish.DamArmed = true;
        // A shade past the dam, so the release finds it expired rather than
        // racing it. Input priority for the reason SnapshotPresented gives.
        Avalonia.Threading.DispatcherTimer.RunOnce(
            ReleaseOverduePublish,
            TimeSpan.FromMilliseconds(PublishDamMs + 15),
            Avalonia.Threading.DispatcherPriority.Input);
    }

    /// <summary>
    /// The timer's half of the dam, a seam of its own because whether a
    /// <c>DispatcherTimer</c> ticks under a headless pump is a fact about the
    /// harness (see <c>ProjectDockerTests</c> on B61's debounce) —
    /// <c>PublishPacingTests</c> calls this directly and the one-line arming
    /// above stays untested wiring.
    /// </summary>
    internal void ReleaseOverduePublish()
    {
        _publish.DamArmed = false;
        if (!_publish.TakeDeferral()) return;
        // Through RequestSnapshot so a canvas that is merely slow (published
        // again inside the dam window) re-defers and re-arms rather than
        // stacking a second frame in flight.
        RequestSnapshot();
    }



    public void EndStroke()
    {
        var stroke = _strokeBuilder.End();
        _live.ClearEffectState();
        if (stroke is null) return;
        var target = PaintTarget();
        if (target is null) return;

        _stabilizer.End();
        LazyBrushCleared?.Invoke();
        stroke.Points = _stabilizer.PostProcess(stroke.Points);

        // Remembered for the next Shift+click. The post-processed end, not the
        // raw one, so the next segment starts exactly where this mark stops.
        _lastStrokeEnd = stroke.Points.Count > 0
            ? (stroke.Points[^1].X, stroke.Points[^1].Y)
            : null;

        // A stroke painted under a selection carries it forever (provenance).
        var clip = PrepareClipForSelection();
        if (clip is not null) stroke.ClipId = clip.Value.Id;

        // Both of these were wired into EndGradient and EndShape and missed
        // here, which is the path a pen actually takes. The freeze mattered:
        // an all-layers-BAKED smudge drawn by hand never froze anything and
        // silently fell back to reading its own layer. Live hid it, because
        // the re-bake runs off the edit funnel and covered for the missing
        // call — so the half that was tested end to end worked and the half
        // that was not did not.
        FreezeSampledBackdrop(stroke);
        RememberDocumentBrush();

        // Commit the pixels incrementally: stamp the EXACT stroke onto the
        // cached frame bitmap instead of invalidating it — invalidation would
        // replay every stroke in the frame, which is why lifting the pen used
        // to pause on drawings with many strokes. Appending the exact stroke
        // to the previously exact bitmap is the same sequence Materialize
        // would run, so the pixels stay bit-identical.
        AppendToFrameRender(target, stroke); // pre-stroke state (record not yet updated)

        // Undo without snapshotting the whole document (the other pen-lift
        // pause). The frame is resolved by id at apply/revert time: a
        // snapshot-undo in between replaces the doc instance tree.
        var frameId = target.Id;
        var addedClip = false;
        _committingScopedEdit = true;
        try
        {
            _editor.PerformDelta(
                label: "Stroke",
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
        if (BrushEngine.CommitBounds(stroke, commitInfo) is { } touched) _publish.MarkDirty(touched);
        else _publish.InvalidateWholeCanvas();
        PublishSnapshot();
        RefreshThumbnails();
    }
}
