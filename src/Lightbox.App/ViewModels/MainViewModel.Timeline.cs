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

/// <summary>How long the timeline is, transport, exposure editing, timing presets, markers and charts.</summary>
/// <remarks>
/// Split out of <c>MainViewModel.cs</c> under Q75, which was 12,749 lines across 61
/// sections. Every field this file uses is either declared here — meaning no other
/// section touches it — or in the shared-state block at the top of
/// <c>MainViewModel.cs</c>. See <c>docs/DESIGN-mainviewmodel-decomposition.md</c>.
/// </remarks>
public partial class MainViewModel
{
    // ---- how big the timeline is ---------------------------------------------

    /// <summary>
    /// How wide one frame's cell is, in pixels.
    /// </summary>
    /// <remarks>
    /// Adjustable, because how many frames you want on screen at once depends
    /// entirely on what you are doing: laying out a two-hundred-frame scene
    /// wants them narrow enough to see the shape of the timing, and working a
    /// twelve-drawing cycle wants them wide enough to read the thumbnails. A
    /// preference rather than document data — it is how you are looking at the
    /// animation, not something about it.
    /// </remarks>
    public double TimelineFrameWidth
    {
        get => Math.Clamp(Settings.TimelineFrameWidth, 14, 72);
        set
        {
            var clamped = Math.Clamp(value, 14, 72);
            if (Math.Abs(TimelineFrameWidth - clamped) < 0.5) return;
            Settings.TimelineFrameWidth = clamped;
            Settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(TimelineRulerCellWidth));
            OnPropertyChanged(nameof(TimelineThumbWidth));
        }
    }

    /// <summary>
    /// The ruler's pitch: a cell plus the gap after it.
    /// </summary>
    /// <remarks>
    /// Derived rather than set twice. The ruler numbers have to sit over the
    /// cells they name, and two independent constants is how they stop doing
    /// that the first time either one moves.
    /// </remarks>
    public double TimelineRulerCellWidth => TimelineFrameWidth + CellGap;

    private const double CellGap = 2;

    /// <summary>
    /// How tall a timeline row is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Matched to the layer rows beside it. They were 44 against the Layers
    /// docker's shorter rows, which made the two lists of the same layers read
    /// as two unrelated things.
    /// </para>
    /// <para>
    /// <b>26, matching the Layers docker again after the density retune.</b> The
    /// two lists show the same layers and have to be read across, so the number
    /// that matters is not this one on its own — it is that this one and a
    /// docker row agree. They had drifted to 28 against 33; both are 26 now,
    /// which is the icon tile with no padding either side.
    /// </para>
    /// <para>
    /// The floor is the thumbnail, not the row: <see cref="TimelineThumbHeight"/>
    /// is 16 and the cel needs a couple of pixels around it. <c>DESIGN.md</c>
    /// protects timeline cells from being shrunk — "a 12 px cell is a misdrop
    /// waiting to happen" — and 26 is nowhere near that. It is a scale entry
    /// rather than a free number, so it moves when the scale does.
    /// </para>
    /// </remarks>
    public double TimelineRowHeight => 26;

    public double TimelineThumbWidth => Math.Max(12, TimelineFrameWidth - 8);

    public double TimelineThumbHeight => 16;

    /// <summary>Cells shown per row: the real frames plus empty tail cells to insert into.</summary>
    public int TimelineExtent => Scene.FrameCount + VirtualTail;

    private const int VirtualTail = 24;

    /// <summary>Last frame the ruler may scrub to.</summary>
    public int MaxScrubFrame => Scene.FrameCount - 1;

    public string FrameLabel => $"{CurrentFrameIndex + 1} / {Scene.FrameCount}";

    partial void OnCurrentFrameIndexChanged(int value)
    {
        _lastStrokeEnd = null;   // and it stops being true on another drawing

        // Only while playing: the same path serves scrubbing, and blending a
        // budgeted path with an unbudgeted one makes every number mean nothing.
        var profiling = IsPlaying;
        if (profiling) _tickProfile.Tick();

        // A line selected on another drawing is not on this one. Left alone the
        // count keeps reporting lines nothing can show, which reads as the
        // arrow having stopped working.
        PruneStrokeSelection();

        using (Profile(profiling, Services.TickProfile.Phase.Highlights))
        {
            RefreshCellHighlights();
        }

        // B152: NOT while playing. The exposed frame changes on every tick, so
        // the id check below always misses and every tick rasterized a full
        // 1920x1080 frame per layer — from the stroke record, because the
        // compositor is reading TILES during playback and never fills this
        // cache — to make a 44x26 picture nobody is looking at. Measured at
        // 159 ms of mean tick lateness on a 16-core machine. OnIsPlayingChanged
        // catches them up when playback stops.
        // The scope sits INSIDE the guard, not around it. Around it, the phase
        // logs a zero-length call on every tick and the report says "0 ms over
        // 336 of 336 ticks" — which is true, unreadable, and indistinguishable
        // from a phase that ran and was cheap. Inside it, the phase genuinely
        // never runs and the report says so, which is the finding.
        if (!IsPlaying)
        {
            using (Profile(profiling, Services.TickProfile.Phase.Thumbnails))
            {
                RefreshLayerThumbs();
            }
        }
        using (Profile(profiling, Services.TickProfile.Phase.Bookkeeping))
        {
            RefreshCamera();
            // Whether THIS frame is pinned changes with the playhead, and the pin
            // button has to say which way it will go.
            OnPropertyChanged(nameof(CurrentFrameIsGhost));
            OnPropertyChanged(nameof(GhostPinLabel));
            // Which reference frame is showing, and therefore which cell the
            // alignment fields are editing, is a property of the playhead.
            NotifyReference();
        }
        using (Profile(profiling, Services.TickProfile.Phase.Audio))
        {
            ScrubAudioTick();
        }
        // A frame the clock has already dropped is not composited: the pixels
        // would be replaced before anything drew them, and making them is what
        // turned "catch up" into the reason it was behind (B162).
        if (_skippingFrame) return;

        // No scope around this one: PublishSnapshot times its own two halves as
        // siblings (Compose and Handoff), and a scope here would be their sum
        // counted a second time in ALL PHASES. The flag is how the method knows
        // it is on a playback tick and should measure at all — it is called on
        // every stroke too, where none of this applies.
        _profilingTick = profiling;
        try
        {
            PublishSnapshot();
        }
        finally
        {
            _profilingTick = false;
        }
    }

    internal Services.TickProfile TickProfile => _tickProfile;

    /// <summary>
    /// Time a phase, or do nothing at all when this is not a playback tick.
    /// </summary>
    /// <remarks>
    /// A nullable scope rather than a branch at each call site: the alternative
    /// is an <c>if</c> around every phase, and the one somebody forgets is the
    /// one that then reports a scrub as playback.
    /// </remarks>
    private Services.TickProfile.Scope? Profile(bool on, Services.TickProfile.Phase phase) =>
        on ? _tickProfile.Measure(phase) : null;

    // ---- commands -----------------------------------------------------------

    // ---- playback transport --------------------------------------------------

    /// <summary>Playback start (index, -1 = unset → first frame).</summary>
    [ObservableProperty]
    private int _playbackStartFrame = -1;

    /// <summary>Playback end (index, -1 = unset → last frame).</summary>
    [ObservableProperty]
    private int _playbackEndFrame = -1;

    partial void OnPlaybackStartFrameChanged(int value) => RefreshRangeHighlights();

    partial void OnPlaybackEndFrameChanged(int value) => RefreshRangeHighlights();

    internal int EffectiveStartFrame =>
        Math.Clamp(PlaybackStartFrame < 0 ? 0 : PlaybackStartFrame, 0, Math.Max(0, Scene.FrameCount - 1));

    internal int EffectiveEndFrame =>
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
        StopAudio();
        PublishSnapshot();
    }

    private void StartPlayback(int direction)
    {
        _playDirection = direction;
        if (IsPlaying) return;
        _strokeBuilder.Cancel();
        _live.ClearEffectState();
        IsPlaying = true;
        _clock.Start(Scene.Fps, PlaybackSpeedPercent);
        TickAudio();
        PublishSnapshot();
    }

    [RelayCommand]
    private void TogglePlayback()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    /// <summary>One button for both, so the shortcut bar costs one slot.</summary>
    public string PlayPauseGlyph => IsPlaying ? "⏸" : "▶";

    /// <summary>
    /// Whether transport controls are worth showing at all.
    /// </summary>
    /// <remarks>
    /// Workspace-relevant, which here means: an illustration is not going to
    /// be played, so the shortcut bar does not carry a play button on one. The
    /// rest — an animation, a game sprite, a storyboard, or a plain document
    /// with no project saying otherwise — might be.
    /// </remarks>
    public bool ShowsTransport =>
        ProjectDocker.Project?.Manifest.Type != Lightbox.Core.Projects.ProjectType.Illustration;

    /// <summary>Onion skin on the layer being drawn on — the per-layer opt-out, on the canvas.</summary>
    public bool ActiveLayerOnion
    {
        get => ActiveLayer.OnionEnabled;
        set
        {
            if (ActiveLayer.OnionEnabled == value) return;
            SetLayerOnionEnabled(ActiveLayer, value);
            OnPropertyChanged();
        }
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

    /// <summary>
    /// Whether playback wraps at the end of the range.
    /// </summary>
    /// <remarks>
    /// On, because a cycle is the thing you are usually looking at and
    /// stopping after one pass means reaching for the button every time. Off
    /// is for watching a shot end, which is the other half of the job — and a
    /// preference rather than a document property, because it is how you are
    /// reviewing right now, not something about the animation.
    /// </remarks>
    public bool LoopPlayback
    {
        get => Settings.LoopPlayback;
        set
        {
            if (Settings.LoopPlayback == value) return;
            Settings.LoopPlayback = value;
            Settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>One playback tick: advance in the play direction, looping inside the selected range.</summary>
    public void StepPlayback()
    {
        var start = EffectiveStartFrame;
        var end = EffectiveEndFrame;
        var next = CurrentFrameIndex + _playDirection;
        if (next > end || next < start)
        {
            if (!LoopPlayback)
            {
                // Stop on the last frame of the range rather than wrapping —
                // and stop, rather than sitting there still "playing", so the
                // transport button says what is true.
                CurrentFrameIndex = Math.Clamp(
                    _playDirection >= 0 ? end : start, 0, Math.Max(0, Scene.FrameCount - 1));
                Pause();
                return;
            }
            // The loop wrapped: the sound cannot wrap with it, so it stops
            // here and TickAudio restarts it at the range's start.
            next = next > end ? start : end;
            StopAudio();
        }
        CurrentFrameIndex = Math.Clamp(next, 0, Math.Max(0, Scene.FrameCount - 1));
        TickAudio();
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

    // ---- timing presets (Q11's UI half) ----------------------------------------

    /// <summary>
    /// The patterns on offer: the built-ins, then whatever the artist has saved.
    /// </summary>
    /// <remarks>
    /// Built-ins first and never stored, so a later correction to "slow in"
    /// reaches everybody instead of being frozen into their settings file the
    /// first time they opened the app.
    /// </remarks>
    public ObservableCollection<TimingPreset> TimingPresets { get; } = [];

    [ObservableProperty]
    private TimingPreset? _selectedTimingPreset;

    /// <summary>Whether the selected pattern is one of the artist's own.</summary>
    public bool CanDeleteTimingPreset =>
        SelectedTimingPreset is { } preset && !TimingPreset.BuiltIns.Contains(preset);

    /// <summary>The cel menu's re-time item, naming the pattern the bar has chosen.</summary>
    /// <remarks>
    /// Naming it rather than offering the whole list again: a submenu on the cel
    /// would be a second picker to keep in step with the first, and the two
    /// disagreeing is the kind of thing an artist notices at the worst moment.
    /// </remarks>
    public string RetimeMenuLabel =>
        SelectedTimingPreset is { } preset ? $"Re-time to {preset.Name}" : "Re-time";

    partial void OnSelectedTimingPresetChanged(TimingPreset? value)
    {
        OnPropertyChanged(nameof(CanDeleteTimingPreset));
        OnPropertyChanged(nameof(RetimeMenuLabel));
    }

    private void LoadTimingPresets()
    {
        TimingPresets.Clear();
        foreach (var preset in TimingPreset.BuiltIns) TimingPresets.Add(preset);
        foreach (var preset in TimingPresetStore.Load()) TimingPresets.Add(preset);
        SelectedTimingPreset ??= TimingPresets.FirstOrDefault(p => p.Name == "On 2s") ?? TimingPresets.FirstOrDefault();
    }

    /// <summary>
    /// Re-time the cel's range to the selected pattern, as one undoable step.
    /// </summary>
    /// <remarks>
    /// The row grows or shrinks to fit the pattern rather than the selection,
    /// because "on 2s" must never mean "throw away half my drawings". The status
    /// line says which way it went, since a silent change of length on a long
    /// row is easy to miss.
    /// </remarks>
    public void ApplyTimingAt(FrameCell cell)
    {
        if (LayerOfCell(cell) is not { } layer) return;
        if (SelectedTimingPreset is not { } preset) return;
        if (!CanEdit(layer, "re-time it")) return;

        var (start, end) = OpRangeFor(cell);
        var change = _editor.ApplyTiming(layer.Id, start, end, preset);
        if (change.Drawings == 0)
        {
            AiStatus = "Nothing to re-time there — that range holds no drawing of its own.";
            return;
        }

        AfterRetime(layer);
        var length = change.Grew switch
        {
            > 0 => $", {change.Grew} frame{(change.Grew == 1 ? "" : "s")} longer",
            < 0 => $", {-change.Grew} frame{(change.Grew == -1 ? "" : "s")} shorter",
            _ => "",
        };
        AiStatus =
            $"{preset.Name}: {change.Drawings} drawing{(change.Drawings == 1 ? "" : "s")} " +
            $"over {change.Frames} frame{(change.Frames == 1 ? "" : "s")}{length}.";
    }

    [RelayCommand]
    private void ApplySelectedTiming()
    {
        if (CurrentCell() is { } cell) ApplyTimingAt(cell);
    }

    [ObservableProperty]
    private string _newTimingPresetName = "";

    [ObservableProperty]
    private string _newTimingPresetPattern = "";

    /// <summary>
    /// Save the typed pattern under the typed name. False when it will not parse.
    /// </summary>
    /// <remarks>
    /// A name already in use replaces that preset rather than adding a second
    /// with the same label, which is the only behaviour that leaves the list
    /// usable. Built-ins cannot be shadowed — an artist who saves "On 2s" gets
    /// their own entry beside it rather than silently overriding the one the
    /// manual describes.
    /// </remarks>
    public bool SaveTimingPreset()
    {
        var name = NewTimingPresetName.Trim();
        if (name.Length == 0) name = "Custom";
        if (!TimingPreset.TryParse(name, NewTimingPresetPattern, out var preset))
        {
            AiStatus = "A timing pattern is whole numbers of frames — \"2\", or \"1, 1, 2, 3, 4\".";
            return false;
        }

        var mine = TimingPresets.Where(p => !TimingPreset.BuiltIns.Contains(p)).ToList();
        if (mine.FirstOrDefault(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            TimingPresets[TimingPresets.IndexOf(existing)] = preset;
        }
        else
        {
            TimingPresets.Add(preset);
        }

        TimingPresetStore.Save(TimingPresets.Where(p => !TimingPreset.BuiltIns.Contains(p)));
        SelectedTimingPreset = preset;
        NewTimingPresetName = "";
        NewTimingPresetPattern = "";
        AiStatus = $"Saved \"{preset.Name}\" — {preset.Pattern}.";
        return true;
    }

    /// <summary>Forget one of the artist's own patterns. Built-ins are not deletable.</summary>
    public void DeleteSelectedTimingPreset()
    {
        if (SelectedTimingPreset is not { } preset || TimingPreset.BuiltIns.Contains(preset)) return;
        TimingPresets.Remove(preset);
        TimingPresetStore.Save(TimingPresets.Where(p => !TimingPreset.BuiltIns.Contains(p)));
        SelectedTimingPreset = TimingPresets.FirstOrDefault();
        AiStatus = $"Deleted \"{preset.Name}\".";
    }

    private void AfterRetime(Layer layer)
    {
        foreach (var cel in layer.Cels)
        {
            if (cel.Frame is { } frame) _dirtyThumbIds.Add(frame.Id);
        }
        // Every re-timing operation can change the row's length, and stretching
        // already grew the scene with it. These three are derived from
        // Scene.FrameCount and have no notification of their own, so without
        // them the ruler and the scrub limit kept the old length until something
        // else happened to refresh them.
        OnPropertyChanged(nameof(TimelineExtent));
        OnPropertyChanged(nameof(MaxScrubFrame));
        OnPropertyChanged(nameof(FrameLabel));
        SyncLayerRows();
        ClampCurrentFrame(publishIfUnchanged: false);
        _publish.InvalidateWholeCanvas();
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
            // No conversion, and nothing refused. One frame class means a copied
            // cel is already the shape every layer takes — this used to rebuild
            // the frame as the other kind, and to reject a paste outright when a
            // baseline could not "become vector". Both were consequences of the
            // split rather than decisions: pixels and strokes always coexisted on
            // one frame, so there was never anything to convert.
            frames.Add(DocumentEditor.CloneFrame(source)); // fresh id per paste
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

    /// <summary>
    /// Set or replace the marker at a frame, keeping what the label does not own.
    /// </summary>
    /// <remarks>
    /// This <em>replaces</em> the marker, so its note and its event flag have to be
    /// carried across explicitly. Without that, renaming a marker would silently
    /// throw away the prose attached to it and un-export an engine event — a
    /// deletion disguised as an edit, and the kind that is only noticed later.
    /// </remarks>
    public void SetMarkerAt(int frame, string label, string color)
    {
        var existing = MarkerAt(frame);
        var note = existing?.Note;
        var isEvent = existing?.IsEvent;

        _editor.Perform(doc =>
        {
            doc.Scene.Markers.RemoveAll(m => m.Frame == frame);
            doc.Scene.Markers.Add(new FrameMarker
            {
                Frame = frame,
                Label = label.Trim(),
                Color = color,
                Note = note,
                IsEvent = isEvent,
            });
            doc.Scene.Markers.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        });
    }

    /// <summary>
    /// Attach prose to the marker at a frame, making one if there is none.
    /// </summary>
    /// <remarks>
    /// A note needs somewhere to live, and a frame the artist wants to write about
    /// is a frame worth marking — so writing a note on an unmarked frame creates
    /// the marker rather than refusing. Clearing the text back to nothing removes
    /// the note but keeps the marker, because the marker may be doing its own job.
    /// </remarks>
    public void SetMarkerNoteAt(int frame, string? note)
    {
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed is null && MarkerAt(frame) is null) return;

        _editor.Perform(doc =>
        {
            if (doc.Scene.Markers.FirstOrDefault(m => m.Frame == frame) is { } marker)
            {
                marker.Note = trimmed;
                return;
            }
            doc.Scene.Markers.Add(new FrameMarker { Frame = frame, Note = trimmed });
            doc.Scene.Markers.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        });
    }

    /// <summary>Whether the marker at a frame is exported to an engine.</summary>
    public void SetMarkerIsEventAt(int frame, bool isEvent)
    {
        if (MarkerAt(frame) is null) return;
        // Null rather than false when off, so an ordinary marker writes no key.
        _editor.Perform(doc =>
        {
            if (doc.Scene.Markers.FirstOrDefault(m => m.Frame == frame) is { } marker)
            {
                marker.IsEvent = isEvent ? true : null;
            }
        });
    }

    /// <summary>Markers carrying prose, in frame order. What a notes list shows.</summary>
    public IReadOnlyList<FrameMarker> Notes =>
        Scene.Markers.Where(m => m.HasNote).OrderBy(m => m.Frame).ToList();

    /// <summary>
    /// Jump to the next marker after the playhead. False when there is none.
    /// </summary>
    /// <remarks>
    /// What "timeline bookmarks" actually wanted, and the one thing genuinely
    /// missing: a named point you can *reach*. Markers have existed since M9c and
    /// there has never been a way to walk between them, so on a long sheet they
    /// were labels you had to hunt for by eye.
    /// </remarks>
    public bool GoToNextMarker()
    {
        var next = Scene.Markers.Where(m => m.Frame > CurrentFrameIndex).OrderBy(m => m.Frame).FirstOrDefault();
        if (next is null) return false;
        CurrentFrameIndex = Math.Clamp(next.Frame, 0, Math.Max(0, Scene.FrameCount - 1));
        return true;
    }

    /// <summary>Jump to the marker before the playhead. False when there is none.</summary>
    public bool GoToPreviousMarker()
    {
        var previous = Scene.Markers
            .Where(m => m.Frame < CurrentFrameIndex)
            .OrderByDescending(m => m.Frame)
            .FirstOrDefault();
        if (previous is null) return false;
        CurrentFrameIndex = Math.Clamp(previous.Frame, 0, Math.Max(0, Scene.FrameCount - 1));
        return true;
    }

    [RelayCommand]
    private void NextMarker() => GoToNextMarker();

    [RelayCommand]
    private void PreviousMarker() => GoToPreviousMarker();

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
        // The extreme's own timing chart wins over the bar's count and easing
        // (Q58): the ladder on the drawing is the artist's spacing for this
        // interval, and the bar is the default for extremes that have none.
        var series = layer.Cels[aIndex].Frame!.Chart is { Count: > 0 } chart
            ? Inbetweener.InbetweenSeries(a, b, chart)
            : Inbetweener.InbetweenSeries(a, b, TweenCount, TweenEasing);
        var frames = series.Select(strokes => NewFrameFor(layer, strokes, FrameRole.Inbetween)).ToList();

        _editor.InsertInbetweens(layer.Id, aIndex, frames);
        CurrentFrameIndex = Math.Min(aIndex + 1, Scene.FrameCount - 1);
    }

    // ---- timing charts (Q58) ----------------------------------------------------

    /// <summary>
    /// The timing chart on the key at or before <paramref name="cell"/>'s
    /// frame on its layer, or null — either no chart, or no key.
    /// </summary>
    public IReadOnlyList<double>? ChartAt(FrameCell cell)
    {
        if (LayerOfCell(cell) is not { } layer) return null;
        var at = ExposureSheet.KeyIndexAtOrBefore(layer, cell.Index);
        return at < 0 ? null : layer.Cels[at].Frame?.Chart;
    }

    /// <summary>The frame the chart under <paramref name="cell"/> belongs to, for the editor's title.</summary>
    public int ChartAnchorFrame(FrameCell cell) =>
        LayerOfCell(cell) is { } layer ? ExposureSheet.KeyIndexAtOrBefore(layer, cell.Index) : -1;

    /// <summary>
    /// How many drawings currently sit between the chart's extreme and the
    /// next key, or null when there is no next key yet. The editor derives
    /// its live/stale line from this — a chart whose rung count disagrees is
    /// ignored by the spacing curve, and that has to be visible where the
    /// chart is edited or the artist discovers it by counting.
    /// </summary>
    public int? ChartRunInbetweens(FrameCell cell)
    {
        if (LayerOfCell(cell) is not { } layer) return null;
        var a = ExposureSheet.KeyIndexAtOrBefore(layer, cell.Index);
        if (a < 0) return null;
        var b = ExposureSheet.NextKeyIndex(layer, a);
        if (b < 0) return null;
        var count = 0;
        for (var i = a + 1; i < b; i++)
        {
            if (ExposureSheet.FrameAtExactIndex(layer, i) is not null) count++;
        }
        return count;
    }

    /// <summary>
    /// Write (or clear, with null/empty) the timing chart on the key at or
    /// before <paramref name="cell"/>'s frame on its layer. One undo step —
    /// a chart is authored timing, the same as a re-time.
    /// </summary>
    public void SetChartAt(FrameCell cell, IEnumerable<double>? rungs)
    {
        if (LayerOfCell(cell) is not { } layer) return;
        if (!CanEdit(layer, "chart its timing")) return;
        var at = ExposureSheet.KeyIndexAtOrBefore(layer, cell.Index);
        if (at < 0 || layer.Cels[at].Frame is not { } frame) return;
        var chart = Lightbox.Core.Inbetween.TimingChart.Normalise(rungs);
        if (ChartsEqual(frame.Chart, chart)) return;

        var layerId = layer.Id;
        var frameId = frame.Id;
        _editor.Perform(doc =>
        {
            var target = doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId)?
                .Cels.FirstOrDefault(c => c.Frame?.Id == frameId)?.Frame;
            if (target is not null) target.Chart = chart is null ? null : [.. chart];
        });
        OnPropertyChanged(nameof(GraphSeriesList));   // the intended curve reads it
    }

    private static bool ChartsEqual(IReadOnlyList<double>? a, IReadOnlyList<double>? b) =>
        (a is null && b is null)
        || (a is not null && b is not null && a.SequenceEqual(b));

    private static List<Stroke> StrokesOf(Frame frame) => frame.Strokes;

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

    /// <summary>A new frame carrying the given strokes.</summary>
    /// <remarks>
    /// Took a <see cref="Layer"/> to pick a frame class from its kind, and both
    /// arms of that switch now construct the same thing. The parameter stays so
    /// every call site keeps reading as "a frame for this layer" — the layer is
    /// what a caller has to hand, and threading it through costs nothing.
    /// </remarks>
    private static Frame NewFrameFor(Layer layer, List<Stroke> strokes, FrameRole role = FrameRole.Key) =>
        new() { Strokes = strokes, Role = role };
}
