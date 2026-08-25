using Lightbox.Core.Documents;

namespace Lightbox.Core.Tests;

/// <summary>
/// Retiming and removing pose keys — the record half of the timeline's
/// armature track. Poses have been keyed automatically since the Bone tool
/// landed; what was missing is any way to see them, and then to move one.
/// </summary>
public class PoseKeyEditTests
{
    private static PoseTrack Track(params (int Frame, string[] Bones)[] keys)
    {
        var track = new PoseTrack();
        foreach (var (frame, bones) in keys)
        {
            var key = new PoseKey { Frame = frame };
            foreach (var b in bones) key.Bones[b] = new BonePose { RotationDeg = 10 * frame };
            track.Keys.Add(key);
        }
        return track;
    }

    [Fact]
    public void MovingAKeyTakesEveryBoneOnItAlong()
    {
        var track = Track((0, ["hip", "knee"]), (8, ["hip"]));

        Assert.True(ArmatureOps.MoveKey(track, 8, 12));

        Assert.Null(ArmatureOps.KeyAt(track, 8));
        Assert.Equal(["hip"], ArmatureOps.KeyAt(track, 12)!.Bones.Keys);
        // The other key is untouched — a drag moves what was dragged.
        Assert.Equal(2, ArmatureOps.KeyAt(track, 0)!.Bones.Count);
    }

    [Fact]
    public void AKeyDroppedOnAnotherReplacesIt()
    {
        // The dragged thing lands where it was dropped. A merge would have to
        // pick between two values for the same bone, invisibly.
        var track = Track((0, ["hip"]), (8, ["knee"]));

        Assert.True(ArmatureOps.MoveKey(track, 8, 0));

        Assert.Single(track.Keys);
        Assert.Equal(["knee"], ArmatureOps.KeyAt(track, 0)!.Bones.Keys);
    }

    [Fact]
    public void MovingOneBonesKeyLeavesItsNeighboursWhereTheyWere()
    {
        var track = Track((0, ["hip", "knee"]), (8, ["hip", "knee"]));

        Assert.True(ArmatureOps.MoveBoneKey(track, "knee", 8, 12));

        Assert.Equal(["hip"], ArmatureOps.KeyAt(track, 8)!.Bones.Keys);
        Assert.Contains("knee", ArmatureOps.KeyAt(track, 12)!.Bones.Keys);
        Assert.Equal(80, ArmatureOps.KeyAt(track, 12)!.Bones["knee"].RotationDeg, 6);
    }

    [Fact]
    public void AKeyMadeToReceiveABoneIsSeededFromWhatWasOnScreen()
    {
        // A bone absent from a key is AT REST on it, so a key holding one bone
        // would snap every other bone to rest at that frame. The new key says
        // what the artist was already looking at — KeyPose's rule, one
        // operation along.
        var track = Track((0, ["hip", "knee"]), (8, ["hip", "knee"]));

        ArmatureOps.MoveBoneKey(track, "knee", 8, 4);

        var made = ArmatureOps.KeyAt(track, 4)!;
        Assert.Contains("hip", made.Bones.Keys);
        // Halfway between 0 and 80 on the hip, which is what frame 4 showed.
        Assert.Equal(40, made.Bones["hip"].RotationDeg, 6);
        // And the knee carries the value that was dragged, not the interpolated one.
        Assert.Equal(80, made.Bones["knee"].RotationDeg, 6);
    }

    [Fact]
    public void AKeyLeftHoldingNothingGoesWithTheLastBoneOffIt()
    {
        // An empty key is indistinguishable from no key in every reader, so
        // leaving one would put a dot on the timeline that means nothing.
        var track = Track((0, ["hip"]), (8, ["knee"]));

        ArmatureOps.MoveBoneKey(track, "knee", 8, 0);

        Assert.Null(ArmatureOps.KeyAt(track, 8));
        Assert.Single(track.Keys);
    }

    [Fact]
    public void RemovingWorksWholeOrPerBone()
    {
        var track = Track((0, ["hip", "knee"]), (8, ["hip"]));

        Assert.True(ArmatureOps.RemoveBoneKey(track, "hip", 0));
        Assert.Equal(["knee"], ArmatureOps.KeyAt(track, 0)!.Bones.Keys);

        Assert.True(ArmatureOps.RemoveKey(track, 0));
        Assert.Null(ArmatureOps.KeyAt(track, 0));

        // The last bone off a key takes the key with it.
        Assert.True(ArmatureOps.RemoveBoneKey(track, "hip", 8));
        Assert.Empty(track.Keys);
    }

    [Fact]
    public void NothingToMoveIsNotAnError()
    {
        var track = Track((0, ["hip"]));

        Assert.False(ArmatureOps.MoveKey(track, 4, 8));
        Assert.False(ArmatureOps.MoveKey(track, 0, 0));
        Assert.False(ArmatureOps.MoveBoneKey(track, "knee", 0, 8));
        Assert.False(ArmatureOps.RemoveKey(track, 4));
        Assert.False(ArmatureOps.RemoveBoneKey(track, "knee", 0));
        Assert.False(ArmatureOps.MoveKey(null, 0, 8));
        Assert.Single(track.Keys);
    }
}
