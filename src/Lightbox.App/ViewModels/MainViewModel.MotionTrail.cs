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
    /// The trail's ticks changed: the window pushes them to the canvas. Null
    /// means no trail — off, or nothing in range has a locatable subject.
    /// </summary>
    public event Action<IReadOnlyList<TrailPoint>?>? MotionTrailChanged;

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
        if (MotionTrailChanged is null) return;
        // Off during playback, like onion ghosts and for their reason — and
        // that guard is also what keeps the bounds walk off the tick path
        // (B152's lesson): OnIsPlayingChanged catches up when playback stops.
        if (!Settings.Trail.Enabled || IsPlaying || ActiveLayer is not { } layer)
        {
            MotionTrailChanged.Invoke(null);
            return;
        }
        var points = Core.Timeline.MotionTrail.PointsAround(
            Doc.Scene, layer, CurrentFrameIndex, Settings.Trail.Before, Settings.Trail.After);
        // One tick is not a motion; the painter agrees, and null keeps the
        // canvas's "absent, not merely invisible" rule.
        MotionTrailChanged.Invoke(points.Count > 1 ? points : null);
    }
}
