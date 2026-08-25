namespace Lightbox.App.ViewModels;

/// <summary>What a selected thing on the timeline is.</summary>
public enum TimelineKeyKind
{
    /// <summary>A drawing on a layer's row — the only kind the X-sheet shows.</summary>
    Cel,

    /// <summary>A camera key.</summary>
    Camera,

    /// <summary>
    /// A pose key: the whole key when <see cref="TimelineKey.BoneId"/> is null
    /// (the armature's summary row), one bone's entry otherwise.
    /// </summary>
    Pose,
}

/// <summary>
/// One selectable thing on the timeline, whatever kind it is.
/// </summary>
/// <remarks>
/// <para>
/// <b>The selection spans kinds, and exactly one verb spans them with it.</b>
/// An artist shifting a beat two frames later means the camera move, the bone
/// keys and the drawings together, and retiming is the one operation all three
/// share — a drag on any of them already retimes it. Everything else stays with
/// its own kind, because the other verbs are not the same verb wearing three
/// names: removing a camera key, unkeying a bone and clearing a cel differ in
/// what they leave behind.
/// </para>
/// <para>
/// <b>Cels are addressed by layer index and camera keys by nothing</b>, which
/// looks lopsided until you ask what identifies each. There is one camera, so a
/// frame names its key. There is one pose track, so a frame plus a bone names
/// its key. A cel needs its row as well, and that row is the scene index rather
/// than the docker's, because the docker's order is a view of the stack and the
/// selection outlives a scroll.
/// </para>
/// </remarks>
/// <param name="Kind">Which of the three this is.</param>
/// <param name="Frame">The timeline position it sits on.</param>
/// <param name="LayerIndex">Index into <c>Scene.Layers</c>; -1 for the kinds that have no layer.</param>
/// <param name="BoneId">The bone, for a pose key on a bone row; null for the summary row.</param>
public readonly record struct TimelineKey(
    TimelineKeyKind Kind,
    int Frame,
    int LayerIndex = -1,
    string? BoneId = null)
{
    public static TimelineKey Cel(int layerIndex, int frame) =>
        new(TimelineKeyKind.Cel, frame, layerIndex);

    public static TimelineKey Camera(int frame) => new(TimelineKeyKind.Camera, frame);

    public static TimelineKey Pose(int frame, string? boneId = null) =>
        new(TimelineKeyKind.Pose, frame, -1, boneId);

    public bool IsCel => Kind == TimelineKeyKind.Cel;

    /// <summary>
    /// The row a Shift+click ranges along. Two keys share a track when they
    /// would be dragged by the same gesture — the same layer, the same bone, or
    /// the one camera.
    /// </summary>
    public (TimelineKeyKind Kind, int LayerIndex, string? BoneId) Track =>
        (Kind, LayerIndex, BoneId);
}
