using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.ViewModels;

/// <summary>
/// Selecting keys on the timeline's tracks — camera, bones and cels alike —
/// and retiming them together.
/// </summary>
/// <remarks>
/// <para>
/// <b>One selection, shared with the X-sheet.</b> The two are described in the
/// manual as "two views over the same animation, and nothing you do in one is
/// invisible in another", and a selection that emptied itself on a tab switch
/// would be the exception nobody expects. So this file and the cel selection
/// write to the same <c>_keySelection</c>; the X-sheet simply has no row to
/// show a camera or bone key on.
/// </para>
/// <para>
/// <b>The selection spans kinds; only retiming does.</b> A drag already retimes
/// whatever it grabs, on all three kinds, which is what makes a mixed selection
/// meaningful — "shift this beat two frames later" is one gesture over a camera
/// move, two bone keys and a drawing. The other verbs are not one verb with
/// three names: removing a camera key, unkeying a bone and clearing a cel leave
/// different things behind, so they stay with their own kind.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    /// <summary>Where a Shift+click on a track ranges from.</summary>
    private TimelineKey? _trackAnchor;

    /// <summary>
    /// The identity of the key at a frame on a track row, or null when the row
    /// index does not name a track.
    /// </summary>
    /// <remarks>
    /// The one place row arithmetic happens. <see cref="TracksAboveLayers"/>
    /// already says how many rows sit above the layers and exists for exactly
    /// this reason — a second count that disagreed would select one thing and
    /// retime another.
    /// </remarks>
    public TimelineKey? TrackKeyAt(int trackIndex, int frame)
    {
        if (trackIndex < 0 || frame < 0) return null;
        if (Scene.Camera is not null && trackIndex == 0) return TimelineKey.Camera(frame);
        if (IsPoseTrack(trackIndex)) return TimelineKey.Pose(frame, BoneOfTrack(trackIndex));
        var rowIndex = trackIndex - TracksAboveLayers;
        if (rowIndex < 0 || rowIndex >= LayerRows.Count) return null;
        return TimelineKey.Cel(LayerRows[rowIndex].SceneIndex, frame);
    }

    /// <summary>The row a key is drawn on, or -1 when it is not on screen.</summary>
    private int TrackRowOf(TimelineKey key)
    {
        switch (key.Kind)
        {
            case TimelineKeyKind.Camera:
                return Scene.Camera is not null ? 0 : -1;
            case TimelineKeyKind.Pose:
            {
                var first = Scene.Camera is not null ? 1 : 0;
                if (Doc.Armature is not { Bones.Count: > 0 }) return -1;
                if (key.BoneId is null) return first;
                if (!PoseRowsExpanded) return -1; // folded away; the summary row stands for it
                var offset = VisiblePoseBones().FindIndex(v => v.Bone.Id == key.BoneId);
                return offset < 0 ? -1 : first + 1 + offset;
            }
            default:
            {
                var row = LayerRows.ToList().FindIndex(r => r.SceneIndex == key.LayerIndex);
                return row < 0 ? -1 : TracksAboveLayers + row;
            }
        }
    }

    /// <summary>
    /// The selected keys as (row, frame) pairs, for the track control to paint.
    /// </summary>
    /// <remarks>
    /// Rows rather than <see cref="TimelineKey"/>s on purpose: <c>TrackView</c>
    /// is deliberately ignorant of what a key means — its own comment says so —
    /// and telling it which dots are lit keeps it that way.
    /// </remarks>
    public IReadOnlySet<(int Row, int Frame)> TimelineSelectedDots
    {
        get
        {
            var dots = new HashSet<(int Row, int Frame)>();
            foreach (var key in _keySelection)
            {
                var row = TrackRowOf(key);
                if (row >= 0) dots.Add((row, key.Frame));
            }
            return dots;
        }
    }

    /// <summary>
    /// A click on a track key. <paramref name="toggle"/> is Ctrl,
    /// <paramref name="range"/> is Shift, neither is a plain click.
    /// </summary>
    public void SelectTrackKey(TimelineKey key, bool toggle, bool range)
    {
        if (range && _trackAnchor is { } anchor && anchor.Track == key.Track)
        {
            _keySelection.RemoveWhere(k => k.Track == key.Track);
            foreach (var frame in FramesOn(key.Track, Math.Min(anchor.Frame, key.Frame),
                         Math.Max(anchor.Frame, key.Frame)))
            {
                _keySelection.Add(key with { Frame = frame });
            }
        }
        else if (toggle)
        {
            if (!_keySelection.Add(key)) _keySelection.Remove(key);
            _trackAnchor = key;
        }
        else if (_keySelection.Count > 1 && _keySelection.Contains(key))
        {
            // A plain press on a key that is already selected leaves the
            // selection alone, because the press is almost always the start of
            // a drag and collapsing here would retime one key instead of the
            // five that were highlighted. Clicking a key outside the selection
            // is how you narrow it back down.
            _trackAnchor = key;
        }
        else
        {
            _keySelection.Clear();
            _keySelection.Add(key);
            _trackAnchor = key;
        }
        RefreshTimelineSelection();
    }

    /// <summary>
    /// The frames actually carrying a key on this track, between two bounds.
    /// </summary>
    /// <remarks>
    /// A range on a track is the keys in it, not every frame it spans. Selecting
    /// the empty frames between two camera keys would put things in the
    /// selection that cannot be retimed, and a drag would then silently do
    /// nothing for most of what was highlighted.
    /// </remarks>
    private IEnumerable<int> FramesOn((TimelineKeyKind Kind, int LayerIndex, string? BoneId) track, int from, int to)
    {
        IEnumerable<int> frames = track.Kind switch
        {
            TimelineKeyKind.Camera => CameraOps.Ordered(Scene.Camera).Select(k => k.Frame),
            TimelineKeyKind.Pose => ArmatureOps.Ordered(Scene.PoseTrack)
                .Where(k => track.BoneId is null || k.Bones.ContainsKey(track.BoneId))
                .Select(k => k.Frame),
            _ => track.LayerIndex >= 0 && track.LayerIndex < Scene.Layers.Count
                ? Scene.Layers[track.LayerIndex].Cels
                    .Select((c, i) => (c.Frame, i))
                    .Where(x => x.Frame is not null)
                    .Select(x => x.i)
                : [],
        };
        return frames.Where(f => f >= from && f <= to);
    }

    public bool IsTrackKeySelected(TimelineKey key) => _keySelection.Contains(key);

    /// <summary>Push the selection at every surface that draws it — the dots, the cel highlights, and the key inspector.</summary>
    private void RefreshTimelineSelection()
    {
        RefreshCelSelectionHighlights();
        OnPropertyChanged(nameof(TimelineSelectedDots));
        NotifyTimelineInspector();
    }

    /// <summary>
    /// Retime everything selected by the same number of frames, as one undo
    /// step. When the grabbed key is not in the selection, only it moves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One <c>Perform</c> around the lot</b>, which is why this reaches for
    /// the pure record operations — <c>ArmatureOps.MoveKey</c>,
    /// <c>CameraOps.MoveKey</c>, <c>DocumentEditor.MoveCelIn</c> — rather than
    /// the view-model methods beside them. Those each take their own step, and
    /// a drag that moved four keys would leave four things to undo.
    /// </para>
    /// <para>
    /// <b>Ordered by direction.</b> Moving right is applied from the rightmost
    /// key back, moving left from the leftmost forward, so a key never lands on
    /// one that has not moved out of the way yet. Without it a run shifted right
    /// by one would refuse every move but the last, because each destination is
    /// still occupied.
    /// </para>
    /// </remarks>
    public int RetimeSelection(TimelineKey grabbed, int delta)
    {
        if (delta == 0) return 0;
        var keys = _keySelection.Contains(grabbed) ? _keySelection.ToList() : [grabbed];
        // Nothing may be dragged off the front of the sheet; refusing the whole
        // gesture is kinder than moving some of it and clamping the rest.
        if (keys.Any(k => k.Frame + delta < 0)) return 0;
        var ordered = delta > 0
            ? keys.OrderByDescending(k => k.Frame).ToList()
            : keys.OrderBy(k => k.Frame).ToList();

        var moved = 0;
        var revision = _editor.NextRevision;
        _editor.Perform(doc =>
        {
            foreach (var key in ordered)
            {
                if (MoveOne(doc, key, key.Frame + delta)) moved++;
            }
        }, label: keys.Count == 1 ? "Retime key" : "Retime keys");

        if (moved == 0)
        {
            // B236's door: a drag that turned out to move nothing takes its own
            // step back rather than leaving an empty one for Ctrl+Z to find.
            _editor.DiscardStep(revision);
            return 0;
        }

        // The selection follows what it moved, so a second drag continues from
        // where the first left off rather than from where the keys used to be.
        var after = ordered.Select(k => k with { Frame = k.Frame + delta }).ToHashSet();
        _keySelection.Clear();
        foreach (var key in after) _keySelection.Add(key);
        if (_trackAnchor is { } anchor) _trackAnchor = anchor with { Frame = anchor.Frame + delta };

        AfterRetimeSelection(ordered);
        AiStatus = moved == 1
            ? $"Key retimed by {delta:+#;-#;0} frame{(Math.Abs(delta) == 1 ? "" : "s")}."
            : $"{moved} keys retimed by {delta:+#;-#;0} frame{(Math.Abs(delta) == 1 ? "" : "s")}.";
        return moved;
    }

    private bool MoveOne(Doc doc, TimelineKey key, int to) => key.Kind switch
    {
        TimelineKeyKind.Camera => CameraOps.MoveKey(doc.Scene.Camera, key.Frame, to),
        TimelineKeyKind.Pose => key.BoneId is null
            ? ArmatureOps.MoveKey(doc.Scene.PoseTrack, key.Frame, to)
            : ArmatureOps.MoveBoneKey(doc.Scene.PoseTrack, key.BoneId, key.Frame, to),
        _ => key.LayerIndex >= 0 && key.LayerIndex < doc.Scene.Layers.Count
            && DocumentEditor.MoveCelIn(doc, doc.Scene.Layers[key.LayerIndex].Id, key.Frame, to),
    };

    /// <summary>Refresh only what the kinds actually moved need.</summary>
    private void AfterRetimeSelection(IReadOnlyList<TimelineKey> moved)
    {
        if (moved.Any(k => k.Kind == TimelineKeyKind.Camera))
        {
            RefreshCamera();
            NotifyCameraSurface();
        }
        if (moved.Any(k => k.Kind == TimelineKeyKind.Pose)) AfterPoseTrackEdit();
        if (moved.Any(k => k.IsCel))
        {
            _allThumbsDirty = true;
            RefreshThumbnails();
        }
        SyncLayerRows();
        RefreshTimelineSelection();
    }

    /// <summary>
    /// The pose keys in the selection, for the one menu that acts on a kind
    /// rather than on the whole selection.
    /// </summary>
    public IReadOnlyList<TimelineKey> SelectedPoseKeys(TimelineKey clicked) =>
        _keySelection.Contains(clicked)
            ? _keySelection.Where(k => k.Kind == TimelineKeyKind.Pose).ToList()
            : [clicked];
}
