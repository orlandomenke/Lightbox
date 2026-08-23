using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// Stepped bone timing (Q152): a <see cref="Easing.Hold"/> key freezes a pose
/// until the next one, and <see cref="PoseTrack.Step"/> samples the fluid
/// tween on Ns so a rig can animate on 2s. Half of what these tests protect
/// is the authoring/render split — <see cref="ArmatureOps.PoseAt"/> must stay
/// fluid because keys are seeded from it and drags measured against it — and
/// the other half is absence: a document that never steps writes no new key.
/// </summary>
public class PoseSteppingTests
{
    /// <summary>An upper arm rooted at (100, 100), with a forearm off its tip.</summary>
    private static Armature Arm() => new()
    {
        Bones =
        [
            new Bone { Id = "upper", Name = "Upper arm", X = 100, Y = 100, Length = 80 },
            new Bone { Id = "fore", Name = "Forearm", ParentId = "upper", X = 80, Length = 60 },
        ],
    };

    /// <summary>Rest at frame 0, upper arm at 40° on frame 4, linear between.</summary>
    private static PoseTrack Swing(int? step = null) => new()
    {
        Step = step,
        Keys =
        [
            new PoseKey { Frame = 0, Ease = Easing.Linear },
            new PoseKey { Frame = 4, Bones = { ["upper"] = new BonePose { RotationDeg = 40 } } },
        ],
    };

    [Fact]
    public void AHoldKeyFreezesThePoseUntilTheNextKey()
    {
        var track = new PoseTrack
        {
            Keys =
            [
                new PoseKey { Frame = 0, Ease = Easing.Hold, Bones = { ["upper"] = new BonePose { RotationDeg = 10 } } },
                new PoseKey { Frame = 4, Bones = { ["upper"] = new BonePose { RotationDeg = 50 } } },
            ],
        };

        for (var f = 0; f < 4; f++)
            Assert.Equal(10, ArmatureOps.PoseAt(track, f)["upper"].RotationDeg);
        // The next key's own frame belongs to the next key, exactly as it
        // does under every other easing.
        Assert.Equal(50, ArmatureOps.PoseAt(track, 4)["upper"].RotationDeg);
    }

    [Fact]
    public void SteppingSamplesTheRenderPoseAndLeavesAuthoringFluid()
    {
        var armature = Arm();
        var track = Swing(step: 2);

        // Authoring stays fluid — a key added at frame 1 must seed from the
        // tween, and a drag there must measure against it.
        Assert.Equal(10, ArmatureOps.PoseAt(track, 1)["upper"].RotationDeg, 6);

        // The render pose holds inside each step…
        var f2 = ArmatureOps.EffectivePoseAt(armature, track, 2)["upper"].RotationDeg;
        var f3 = ArmatureOps.EffectivePoseAt(armature, track, 3)["upper"].RotationDeg;
        Assert.Equal(20, f2, 6);
        Assert.Equal(f2, f3);

        // …the first step shows the first key — which keys nothing, and a
        // bone at rest is absent, not zeroed — and a sample frame lands
        // exactly on its key.
        Assert.DoesNotContain("upper", ArmatureOps.EffectivePoseAt(armature, track, 1).Keys);
        Assert.Equal(40, ArmatureOps.EffectivePoseAt(armature, track, 4)["upper"].RotationDeg, 6);
    }

    [Fact]
    public void TheSampleGridIsAnchoredAtFrameZero()
    {
        var track = Swing(step: 3);
        Assert.Equal(0, ArmatureOps.SampleFrame(track, 0));
        Assert.Equal(0, ArmatureOps.SampleFrame(track, 2));
        Assert.Equal(3, ArmatureOps.SampleFrame(track, 3));
        Assert.Equal(3, ArmatureOps.SampleFrame(track, 5));
        // An unstepped track — and a nonsense step — sample every frame.
        Assert.Equal(7, ArmatureOps.SampleFrame(Swing(), 7));
        Assert.Equal(7, ArmatureOps.SampleFrame(Swing(step: 1), 7));
        Assert.Equal(7, ArmatureOps.SampleFrame(Swing(step: 0), 7));
    }

    [Fact]
    public void AJigglingRigHoldsDeadStillInsideAStep()
    {
        // The spring integrates per frame, so without quantizing the whole
        // render sample two frames inside one step would disagree by the
        // jiggle alone — the boil the step exists to remove.
        var armature = Arm();
        armature.Bones[1].Jiggle = new BoneJiggle();
        var track = Swing(step: 2);

        var a = ArmatureOps.EffectivePoseAt(armature, track, 2);
        var b = ArmatureOps.EffectivePoseAt(armature, track, 3);

        Assert.Equal(a.Keys.OrderBy(k => k), b.Keys.OrderBy(k => k));
        foreach (var id in a.Keys)
        {
            Assert.Equal(a[id].RotationDeg, b[id].RotationDeg);
            Assert.Equal(a[id].X, b[id].X);
            Assert.Equal(a[id].Y, b[id].Y);
        }
    }

    [Fact]
    public void AnUnsteppedTrackWritesNoStepKeyAndAHoldRoundTrips()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armature = Arm();
        doc.Scene.PoseTrack = new PoseTrack
        {
            Keys = [new PoseKey { Frame = 0, Ease = Easing.Hold }],
        };

        var json = DocJson.Serialize(doc);
        Assert.DoesNotContain("\"step\"", json);
        Assert.Equal(Easing.Hold, ArmatureOps.KeyAt(DocJson.Deserialize(json).Scene.PoseTrack, 0)!.Ease);

        doc.Scene.PoseTrack.Step = 2;
        Assert.Equal(2, DocJson.Deserialize(DocJson.Serialize(doc)).Scene.PoseTrack!.Step);
    }
}
