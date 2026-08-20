using Lightbox.Core.Timeline;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The motion trail: Pillar 4's motion path and spacing visualization, one
/// overlay (Q98). The view model computes the ticks from the record and hands
/// them to the window; nothing here touches the document or a pixel.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// The trail's ticks — or the analysis riding them (Q134) — changed: the
    /// window pushes the snapshot to the canvas. Null means nothing to draw —
    /// off, or nothing in range has a locatable subject.
    /// </summary>
    public event Action<Rendering.TrailOverlay?>? MotionTrailChanged;

    /// <summary>
    /// The arc overlay changed: the fitted arc, its off-arc ticks and the
    /// predictions, or null when the arc and prediction are both off — or
    /// there are too few ticks to fit. Fired with, and only with,
    /// <see cref="MotionTrailChanged"/>: the arc is read off the trail.
    /// </summary>
    public event Action<MotionArcOverlay?>? MotionArcChanged;

    /// <summary>Settings forwarded the way <see cref="OnionSkin"/>'s are.</summary>
    public bool MotionTrail
    {
        get => Settings.Trail.Enabled;
        set
        {
            if (Settings.Trail.Enabled == value) return;
            Settings.Trail.Enabled = value;
            OnPropertyChanged();
            AfterTrailChange();
        }
    }

    public int MotionTrailBefore
    {
        get => Settings.Trail.Before;
        set
        {
            var depth = Math.Clamp(value, 0, 30);
            if (Settings.Trail.Before == depth) return;
            Settings.Trail.Before = depth;
            OnPropertyChanged();
            AfterTrailChange();
        }
    }

    /// <summary>
    /// The fitted arc and its off-arc judgement. Switching it on switches the
    /// trail on too: the arc is read off the ticks, and a toggle that shows
    /// nothing until a second toggle is found reads as broken, not as off.
    /// </summary>
    public bool MotionArcs
    {
        get => Settings.Trail.Arc;
        set
        {
            if (Settings.Trail.Arc == value) return;
            Settings.Trail.Arc = value;
            OnPropertyChanged();
            if (value && !MotionTrail) MotionTrail = true;   // saves and refreshes
            else AfterTrailChange();
        }
    }

    /// <summary>Predicted positions on the arc — see <see cref="MotionArcs"/>.</summary>
    public bool ArcPrediction
    {
        get => Settings.Trail.Predict;
        set
        {
            if (Settings.Trail.Predict == value) return;
            Settings.Trail.Predict = value;
            OnPropertyChanged();
            if (value && !MotionTrail) MotionTrail = true;
            else AfterTrailChange();
        }
    }

    public int MotionTrailAfter
    {
        get => Settings.Trail.After;
        set
        {
            var depth = Math.Clamp(value, 0, 30);
            if (Settings.Trail.After == depth) return;
            Settings.Trail.After = depth;
            OnPropertyChanged();
            AfterTrailChange();
        }
    }

    /// <summary>
    /// Remember, and repaint. Unlike <c>AfterOnionChange</c> this publishes
    /// nothing: ghosts are composited into the frame, the trail is chrome the
    /// draw op paints over it, so a settings change costs an overlay refresh
    /// and not a canvas rebuild.
    /// </summary>
    private void AfterTrailChange()
    {
        RefreshMotionTrail();
        Settings.Save();
    }

    /// <summary>
    /// Recompute the ticks and tell the window. Cheap enough to call from
    /// every hook that can move them — playhead, active layer, any document
    /// change — because when the trail is off it is one boolean.
    /// </summary>
    public void RefreshMotionTrail()
    {
        // Off during playback, like onion ghosts and for their reason — and
        // that guard is also what keeps the bounds walk off the tick path
        // (B152's lesson): OnIsPlayingChanged catches up when playback stops.
        var active = !Settings.Trail.Enabled || IsPlaying ? null : ActiveLayer;
        // Once per refresh, for the overlay AND the readout — the readout
        // re-running the analysers its overlay had just run was a doubled
        // record walk per playhead move.
        RecomputeAnalysis(active);
        OnPropertyChanged(nameof(AnalysisReadout));
        OnPropertyChanged(nameof(HasAnalysisReadout));
        if (MotionTrailChanged is null && MotionArcChanged is null) return;
        if (active is not { } layer)
        {
            MotionTrailChanged?.Invoke(null);
            MotionArcChanged?.Invoke(null);
            return;
        }
        // When predicting, look one drawing past the window: a predicted tick
        // must never cover a real drawing, and the fit cannot know the layer.
        var probeAfter = Settings.Trail.After + (Settings.Trail.Predict ? 1 : 0);
        var points = Core.Timeline.MotionTrail.PointsAround(
            Doc.Scene, layer, CurrentFrameIndex, Settings.Trail.Before, probeAfter);
        var afterTicks = 0;
        foreach (var p in points)
        {
            if (!p.Before && !p.Current) afterTicks++;
        }
        var nextDrawingExists = afterTicks > Settings.Trail.After;
        // The probe tick is not the artist's window — it decided the
        // prediction and leaves before anything is displayed or fitted.
        if (nextDrawingExists) points.RemoveAt(points.Count - 1);
        // One tick is not a motion; the painter agrees, and null keeps the
        // canvas's "absent, not merely invisible" rule.
        MotionTrailChanged?.Invoke(BuildTrailOverlay(points));
        MotionArcChanged?.Invoke(ComputeArc(points, nextDrawingExists));
    }

    /// <summary>
    /// The arc overlay for the trail just computed: everything the fit reads
    /// off the ticks, stripped to the halves the artist switched on — the arc
    /// and its off-arc judgement, the predictions, either, or null.
    /// </summary>
    private MotionArcOverlay? ComputeArc(List<TrailPoint> points, bool nextDrawingExists)
    {
        if (!Settings.Trail.Arc && !Settings.Trail.Predict) return null;
        var overlay = Core.Timeline.MotionArc.Fit(
            points, CurrentFrameIndex,
            predictNext: Settings.Trail.Predict && !nextDrawingExists);
        if (overlay is null) return null;
        if (!Settings.Trail.Arc)
            overlay = overlay with { Path = [], OffArc = [] };
        if (!Settings.Trail.Predict)
            overlay = overlay with { Extension = [], Next = null, Current = null };
        return overlay;
    }
}
