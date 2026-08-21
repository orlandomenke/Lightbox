using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The timeline's key inspector: the values of the one selected key, as
/// numbers — a camera key's framing, a bone key's departure from rest, a
/// drawing's layer depth.
/// </summary>
/// <remarks>
/// <para>
/// <b>Present only while exactly one key is selected.</b> The section is a
/// strip under the tracks that costs nothing when nothing is selected —
/// optional means absent, the docker's own rule — and a multi-selection shows
/// nothing rather than an average, because writing one number into five keys
/// is a different feature wearing this one's clothes.
/// </para>
/// <para>
/// <b>Every getter re-reads the document</b> rather than caching what was
/// selected: the key may have been retimed, deleted or undone since, and a
/// stale panel writing into a key that moved is worse than an empty one.
/// </para>
/// <para>
/// Writes follow each kind's existing door: a camera value goes through
/// <see cref="EditCameraKey"/> (the graph editor's path), a bone value
/// through the same keying the pose drags use, depth through
/// <see cref="SetLayerDepth"/> (the Scene panel's). One value, one path —
/// a second writer that disagreed on clamping would make the two surfaces
/// fight.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    /// <summary>The one selected key, or null when the selection is empty or plural.</summary>
    private TimelineKey? SoleSelectedKey =>
        _keySelection.Count == 1 ? _keySelection.First() : null;

    public bool TimelineInspectorVisible => SoleSelectedKey is not null;

    public bool InspectorIsCamera => SoleSelectedKey is { Kind: TimelineKeyKind.Camera };

    public bool InspectorIsBone => SoleSelectedKey is { Kind: TimelineKeyKind.Pose, BoneId: not null };

    /// <summary>The armature summary's key: whole-pose, so no single bone's numbers.</summary>
    public bool InspectorIsPoseSummary => SoleSelectedKey is { Kind: TimelineKeyKind.Pose, BoneId: null };

    public bool InspectorIsCel => SoleSelectedKey is { Kind: TimelineKeyKind.Cel };

    public bool InspectorHasPosition => InspectorIsCamera || InspectorIsBone;

    public bool InspectorHasRotation => InspectorIsCamera || InspectorIsBone;

    public string InspectorTitle
    {
        get
        {
            if (SoleSelectedKey is not { } key) return "";
            return key.Kind switch
            {
                TimelineKeyKind.Camera => $"Camera · frame {key.Frame + 1}",
                TimelineKeyKind.Pose when key.BoneId is { } bone =>
                    $"{NameOfBone(bone)} · frame {key.Frame + 1}",
                TimelineKeyKind.Pose => $"Pose · frame {key.Frame + 1}",
                _ => key.LayerIndex >= 0 && key.LayerIndex < Scene.Layers.Count
                    ? $"{Scene.Layers[key.LayerIndex].Name} · frame {key.Frame + 1}"
                    : "",
            };
        }
    }

    /// <summary>What the summary row's key cannot offer, said instead of shown blank.</summary>
    public string InspectorHint =>
        InspectorIsPoseSummary
            ? "A whole pose — open Bones and pick one bone's key to edit its values."
            : "";

    private CameraKey? InspectorCameraKey =>
        SoleSelectedKey is { Kind: TimelineKeyKind.Camera } key
            ? CameraOps.KeyAt(Scene.Camera, key.Frame)
            : null;

    private BonePose? InspectorBonePose =>
        SoleSelectedKey is { Kind: TimelineKeyKind.Pose, BoneId: { } bone } key
            && ArmatureOps.KeyAt(Scene.PoseTrack, key.Frame) is { } poseKey
            && poseKey.Bones.TryGetValue(bone, out var pose)
            ? pose
            : null;

    private Layer? InspectorLayer =>
        SoleSelectedKey is { Kind: TimelineKeyKind.Cel } key
            && key.LayerIndex >= 0 && key.LayerIndex < Scene.Layers.Count
            ? Scene.Layers[key.LayerIndex]
            : null;

    /// <summary>Camera: the document x the frame centres on. Bone: offset from rest x.</summary>
    public double InspectorX
    {
        get => InspectorCameraKey?.X ?? InspectorBonePose?.X ?? 0;
        set => WriteInspector("Camera X", p => p.X = value, value);
    }

    public double InspectorY
    {
        get => InspectorCameraKey?.Y ?? InspectorBonePose?.Y ?? 0;
        set => WriteInspector("Camera Y", p => p.Y = value, value);
    }

    /// <summary>Camera roll, or a bone's rotation added to rest — clockwise degrees either way.</summary>
    public double InspectorRotation
    {
        get => InspectorCameraKey?.RotationDeg ?? InspectorBonePose?.RotationDeg ?? 0;
        set => WriteInspector("Rotation", p => p.RotationDeg = value, value);
    }

    public double InspectorZoom
    {
        get => InspectorCameraKey?.Zoom ?? 1;
        set => WriteInspector("Zoom", null, value);
    }

    /// <summary>The selected drawing's layer depth — the Scene panel's number, where the key is.</summary>
    public double InspectorDepth
    {
        get => InspectorLayer?.Depth ?? 0;
        set
        {
            if (InspectorLayer is not { } layer) return;
            SetLayerDepth(layer, value);
            OnPropertyChanged(nameof(InspectorDepth));
        }
    }

    /// <summary>
    /// One write, routed by what is selected: camera values take the graph
    /// editor's door, bone values the pose drags'.
    /// </summary>
    private void WriteInspector(string cameraSeries, Action<BonePose>? boneEdit, double value)
    {
        if (SoleSelectedKey is not { } key) return;
        switch (key.Kind)
        {
            case TimelineKeyKind.Camera when InspectorCameraKey is not null:
                EditCameraKey(cameraSeries, key.Frame, key.Frame, value);
                break;
            case TimelineKeyKind.Pose when key.BoneId is { } bone && boneEdit is not null
                                           && InspectorBonePose is not null:
                _editor.Perform(doc =>
                {
                    if (ArmatureOps.KeyAt(doc.Scene.PoseTrack, key.Frame) is { } poseKey
                        && poseKey.Bones.TryGetValue(bone, out var pose))
                    {
                        boneEdit(pose);
                    }
                }, label: "Edit pose key");
                InvalidateRiggedFrames();
                RefreshArmatureAtPlayhead();
                break;
        }
        NotifyTimelineInspector();
    }

    /// <summary>
    /// Push every inspector property — called wherever the selection or the
    /// selected key's values may have changed underneath it.
    /// </summary>
    private void NotifyTimelineInspector()
    {
        OnPropertyChanged(nameof(TimelineInspectorVisible));
        OnPropertyChanged(nameof(InspectorIsCamera));
        OnPropertyChanged(nameof(InspectorIsBone));
        OnPropertyChanged(nameof(InspectorIsPoseSummary));
        OnPropertyChanged(nameof(InspectorIsCel));
        OnPropertyChanged(nameof(InspectorHasPosition));
        OnPropertyChanged(nameof(InspectorHasRotation));
        OnPropertyChanged(nameof(InspectorTitle));
        OnPropertyChanged(nameof(InspectorHint));
        OnPropertyChanged(nameof(InspectorX));
        OnPropertyChanged(nameof(InspectorY));
        OnPropertyChanged(nameof(InspectorRotation));
        OnPropertyChanged(nameof(InspectorZoom));
        OnPropertyChanged(nameof(InspectorDepth));
    }
}
