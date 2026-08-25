using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The timeline's key clipboard: copy whatever the dot selection holds —
/// camera keys, whole pose keys, single bones' entries, cels — and paste the
/// lot at another frame, relative offsets kept.
/// </summary>
/// <remarks>
/// <para>
/// <b>Copying crosses kinds for retiming's reason.</b> "Put this beat there
/// too" means the camera move, the pose keys and the drawings together, the
/// same lot a retime drag already moves together. What lands differs per kind
/// — that stays true and is why paste is written per kind below — but the
/// gesture is one gesture, so the clipboard is one clipboard.
/// </para>
/// <para>
/// <b>Separate from the cel clipboard on purpose.</b> <c>_celClipboard</c> is
/// one row's worth of drawings and pastes consecutively, holes closed — a
/// sequence. This clipboard is a set of keys at their distances — a beat.
/// Merging them would make Ctrl+V mean two things depending on what was
/// copied last, which is the kind of surprise a clipboard must not spring.
/// </para>
/// <para>
/// Everything stored is a clone, and paste clones again: the clipboard must
/// survive the document changing under it, and pasting twice must not hand
/// two frames the same drawing instance.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    private sealed record TimelineKeyClipboard(
        IReadOnlyList<(int Offset, CameraKey Key)> CameraKeys,
        IReadOnlyList<(int Offset, PoseKey Key)> PoseKeys,
        IReadOnlyList<(int Offset, string BoneId, BonePose Pose)> BoneKeys,
        IReadOnlyList<(int Offset, string LayerId, Frame Frame)> Cels)
    {
        public int Count => CameraKeys.Count + PoseKeys.Count + BoneKeys.Count + Cels.Count;
    }

    private TimelineKeyClipboard? _keyClipboard;

    public bool HasTimelineKeyClipboard => _keyClipboard is not null;

    /// <summary>
    /// Copy the selection when the clicked key is in it, the clicked key alone
    /// when it is not — the same rule every other selection verb follows.
    /// </summary>
    public int CopyTimelineKeys(TimelineKey clicked)
    {
        var keys = _keySelection.Contains(clicked) ? _keySelection.ToList() : [clicked];
        return CopyKeys(keys);
    }

    /// <summary>Copy whatever is selected — the shortcut's aim, with no key under a pointer.</summary>
    public int CopySelectedTimelineKeys()
    {
        if (_keySelection.Count == 0)
        {
            AiStatus = "Nothing selected to copy — Ctrl+click keys on the timeline first.";
            return 0;
        }
        return CopyKeys(_keySelection.ToList());
    }

    private int CopyKeys(IReadOnlyList<TimelineKey> keys)
    {
        var camera = new List<(int, CameraKey)>();
        var poses = new List<(int, PoseKey)>();
        var bones = new List<(int, string, BonePose)>();
        var cels = new List<(int, string, Frame)>();

        foreach (var key in keys)
        {
            switch (key.Kind)
            {
                case TimelineKeyKind.Camera:
                    if (CameraOps.KeyAt(Scene.Camera, key.Frame) is { } ck)
                        camera.Add((key.Frame, ck.Clone()));
                    break;
                case TimelineKeyKind.Pose when key.BoneId is null:
                    if (ArmatureOps.KeyAt(Scene.PoseTrack, key.Frame) is { } pk)
                        poses.Add((key.Frame, pk.Clone()));
                    break;
                case TimelineKeyKind.Pose:
                    if (ArmatureOps.KeyAt(Scene.PoseTrack, key.Frame) is { } bk
                        && bk.Bones.TryGetValue(key.BoneId, out var pose))
                        bones.Add((key.Frame, key.BoneId, pose.Clone()));
                    break;
                default:
                    if (key.LayerIndex >= 0 && key.LayerIndex < Scene.Layers.Count
                        && Scene.Layers[key.LayerIndex] is { } layer
                        && ExposureSheet.FrameAtExactIndex(layer, key.Frame) is { } drawing)
                        cels.Add((key.Frame, layer.Id, DocumentEditor.CloneFrame(drawing)!));
                    break;
            }
        }

        var count = camera.Count + poses.Count + bones.Count + cels.Count;
        if (count == 0)
        {
            AiStatus = "Nothing to copy — the selection holds no keys.";
            return 0;
        }

        // Offsets from the earliest key, so a paste lands the beat with its
        // internal timing intact wherever it is aimed.
        var origin = new[]
        {
            camera.Select(c => c.Item1), poses.Select(p => p.Item1),
            bones.Select(b => b.Item1), cels.Select(c => c.Item1),
        }.SelectMany(f => f).Min();

        _keyClipboard = new TimelineKeyClipboard(
            camera.Select(c => (c.Item1 - origin, c.Item2)).ToList(),
            poses.Select(p => (p.Item1 - origin, p.Item2)).ToList(),
            bones.Select(b => (b.Item1 - origin, b.Item2, b.Item3)).ToList(),
            cels.Select(c => (c.Item1 - origin, c.Item2, c.Item3)).ToList());
        OnPropertyChanged(nameof(HasTimelineKeyClipboard));
        AiStatus = count == 1 ? "Key copied." : $"{count} keys copied.";
        return count;
    }

    /// <summary>
    /// Paste the copied keys with the earliest landing on
    /// <paramref name="frame"/>. One undo step for the lot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What landing means is each kind's own replace: a camera key or a whole
    /// pose key on the destination gives way outright (the retime drag's
    /// "moved key wins" rule); a single bone's entry joins the key already
    /// there, seeding a new key from the interpolated pose first so pasting
    /// one bone never snaps its neighbours to rest; a cel replaces the
    /// drawing on its frame, extending the scene to reach it.
    /// </para>
    /// <para>
    /// A kind whose home is gone — the camera removed since the copy, a bone
    /// deleted, a layer gone or locked — is skipped and counted, not an error:
    /// the rest of the beat still means what it meant.
    /// </para>
    /// </remarks>
    public int PasteTimelineKeysAt(int frame)
    {
        if (_keyClipboard is not { } clip || clip.Count == 0)
        {
            AiStatus = "The key clipboard is empty — right-click a key and Copy first.";
            return 0;
        }
        if (frame < 0) return 0;

        var pasted = 0;
        var revision = _editor.NextRevision;
        _editor.Perform(doc =>
        {
            foreach (var (offset, key) in clip.CameraKeys)
            {
                if (doc.Scene.Camera is not { } camera) continue;
                var to = frame + offset;
                camera.Keys.RemoveAll(k => k.Frame == to);
                var clone = key.Clone();
                clone.Frame = to;
                camera.Keys.Add(clone);
                pasted++;
            }
            foreach (var (offset, key) in clip.PoseKeys)
            {
                if (doc.Armature is not { Bones.Count: > 0 }) continue;
                var track = doc.Scene.PoseTrack ??= new PoseTrack();
                var to = frame + offset;
                track.Keys.RemoveAll(k => k.Frame == to);
                var clone = key.Clone();
                clone.Frame = to;
                track.Keys.Add(clone);
                pasted++;
            }
            foreach (var (offset, boneId, pose) in clip.BoneKeys)
            {
                if (doc.Armature?.BoneById(boneId) is null) continue;
                var track = doc.Scene.PoseTrack ??= new PoseTrack();
                var to = frame + offset;
                var key = ArmatureOps.KeyAt(track, to);
                if (key is null)
                {
                    key = new PoseKey { Frame = to };
                    foreach (var (id, p) in ArmatureOps.PoseAt(track, to)) key.Bones[id] = p;
                    track.Keys.Add(key);
                }
                key.Bones[boneId] = pose.Clone();
                pasted++;
            }
            foreach (var (offset, layerId, source) in clip.Cels)
            {
                if (doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId) is not { Locked: false }) continue;
                if (DocumentEditor.SetCelIn(doc, layerId, frame + offset,
                        DocumentEditor.CloneFrame(source))) pasted++;
            }
        }, label: "Paste keys");

        if (pasted == 0)
        {
            _editor.DiscardStep(revision);
            AiStatus = "Nothing pasted — what the keys belonged to is gone.";
            return 0;
        }

        if (clip.CameraKeys.Count > 0)
        {
            RefreshCamera();
            NotifyCameraSurface();
        }
        if (clip.PoseKeys.Count > 0 || clip.BoneKeys.Count > 0) AfterPoseTrackEdit();
        if (clip.Cels.Count > 0)
        {
            _allThumbsDirty = true;
            RefreshThumbnails();
        }
        SyncLayerRows();
        OnPropertyChanged(nameof(TimelineTracks));
        var skipped = clip.Count - pasted;
        AiStatus = (pasted == 1 ? "Key pasted" : $"{pasted} keys pasted")
            + $" at frame {frame + 1}."
            + (skipped > 0 ? $" {skipped} skipped — what they belonged to is gone or locked." : "");
        return pasted;
    }

    /// <summary>The shortcut's paste: the copied beat lands at the playhead.</summary>
    public void PasteTimelineKeysAtPlayhead() => PasteTimelineKeysAt(CurrentFrameIndex);

    /// <summary>
    /// Cut: copy, then remove exactly what was copied, as one undo step.
    /// </summary>
    /// <remarks>
    /// Delete stays per kind — the manual's argument that removing a camera
    /// key, unkeying a bone and clearing a drawing are different verbs. Cut is
    /// not delete: it is "move this beat elsewhere", and it removes only what
    /// it just put on the clipboard, so nothing is lost that paste cannot put
    /// back.
    /// </remarks>
    public int CutTimelineKeys(TimelineKey clicked)
    {
        var keys = _keySelection.Contains(clicked) ? _keySelection.ToList() : [clicked];
        if (CopyKeys(keys) == 0) return 0;

        var removed = 0;
        var revision = _editor.NextRevision;
        _editor.Perform(doc =>
        {
            foreach (var key in keys)
            {
                switch (key.Kind)
                {
                    case TimelineKeyKind.Camera:
                        if (doc.Scene.Camera is { } camera && CameraOps.ClearKey(camera, key.Frame))
                            removed++;
                        break;
                    case TimelineKeyKind.Pose:
                        if (key.BoneId is null
                                ? ArmatureOps.RemoveKey(doc.Scene.PoseTrack, key.Frame)
                                : ArmatureOps.RemoveBoneKey(doc.Scene.PoseTrack, key.BoneId, key.Frame))
                            removed++;
                        break;
                    default:
                        if (key.LayerIndex >= 0 && key.LayerIndex < doc.Scene.Layers.Count
                            && doc.Scene.Layers[key.LayerIndex] is { Locked: false } layer
                            && key.Frame >= 0 && key.Frame < layer.Cels.Count
                            && layer.Cels[key.Frame].Frame is not null)
                        {
                            // Clear, not ripple-delete: cut keeps the timing the
                            // way copying a hold-included range keeps it.
                            layer.Cels[key.Frame].Frame = null;
                            removed++;
                        }
                        break;
                }
            }
        }, label: "Cut keys");

        if (removed == 0)
        {
            _editor.DiscardStep(revision);
            return 0;
        }

        if (keys.Any(k => k.Kind == TimelineKeyKind.Camera))
        {
            RefreshCamera();
            NotifyCameraSurface();
        }
        if (keys.Any(k => k.Kind == TimelineKeyKind.Pose)) AfterPoseTrackEdit();
        if (keys.Any(k => k.IsCel))
        {
            _allThumbsDirty = true;
            RefreshThumbnails();
        }
        _keySelection.Clear();
        SyncLayerRows();
        RefreshTimelineSelection();
        OnPropertyChanged(nameof(TimelineTracks));
        AiStatus = removed == 1 ? "Key cut." : $"{removed} keys cut.";
        return removed;
    }
}
