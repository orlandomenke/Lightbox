using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The X-sheet's optional word about what the Timeline is carrying.
/// </summary>
/// <remarks>
/// <para>
/// Ink and the persistent visuals drawn on the paper are the X-sheet's; the
/// camera and the armature are animated but are not part of the exposure, so
/// they belong to the Timeline. That division is deliberate and this does not
/// weaken it — nothing gains a row, a cell or a column. What it fixes is the
/// one thing the division costs an artist: reading four drawings on the sheet
/// with no way to know that frame 9 also carries a pose.
/// </para>
/// <para>
/// <b>Off by default, and empty when off rather than hidden.</b> The ruler is
/// handed nothing to draw, so a document with no camera and no rig pays for
/// none of this and shows none of it — which is what "optional" is supposed to
/// mean here.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    /// <summary>Whether the sheet's numbers are marked where off-sheet elements are keyed.</summary>
    public bool MarkOffSheetKeys
    {
        get => Settings.MarkOffSheetKeys;
        set
        {
            if (Settings.MarkOffSheetKeys == value) return;
            Settings.MarkOffSheetKeys = value;
            Settings.Save();
            OnPropertyChanged();
            NotifyOffSheetKeys();
        }
    }

    /// <summary>Frames carrying a camera key, or nothing at all when the mark is off.</summary>
    public IReadOnlySet<int> CameraKeyedFrames =>
        MarkOffSheetKeys && Scene.Camera is { } camera
            ? CameraOps.Ordered(camera).Select(k => k.Frame).ToHashSet()
            : [];

    /// <summary>Frames carrying a pose key, or nothing at all when the mark is off.</summary>
    public IReadOnlySet<int> PoseKeyedFrames =>
        MarkOffSheetKeys && Scene.PoseTrack is { } track
            ? ArmatureOps.Ordered(track).Select(k => k.Frame).ToHashSet()
            : [];

    internal void NotifyOffSheetKeys()
    {
        OnPropertyChanged(nameof(CameraKeyedFrames));
        OnPropertyChanged(nameof(PoseKeyedFrames));
    }
}
