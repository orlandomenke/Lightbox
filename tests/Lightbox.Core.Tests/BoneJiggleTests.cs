using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// Secondary motion — phase 5 of <c>docs/DESIGN-bones.md</c>: a jiggling bone
/// follows the pose through a spring, one fixed step per frame, seeded
/// settled from geometry. Visual life without logical randomness.
/// </summary>
public class BoneJiggleTests
{
    /// <summary>A parent swinging 90° at frame 1, with a jiggling child on its tip.</summary>
    private static (Armature Armature, PoseTrack Track, string Parent, string Child) Rig(
        double stiffness = 0.2, double damping = 0.15)
    {
        var parent = new Bone { Name = "tail.base", X = 100, Y = 100, Length = 50 };
        var child = new Bone
        {
            Name = "tail.tip",
            ParentId = parent.Id,
            X = 50,
            Length = 40,
            Jiggle = new BoneJiggle { Stiffness = stiffness, Damping = damping },
        };
        var armature = new Armature { Bones = [parent, child] };
        var track = new PoseTrack
        {
            Keys =
            [
                new PoseKey { Frame = 0 },
                new PoseKey { Frame = 1, Bones = { [parent.Id] = new BonePose { RotationDeg = 90 } } },
            ],
        };
        return (armature, track, parent.Id, child.Id);
    }

    private static double ChildWorldAngle(Armature armature, PoseTrack track, string child, int frame)
    {
        var pose = ArmatureOps.EffectivePoseAt(armature, track, frame);
        return ArmatureOps.Solve(armature, pose)[child].RotationDeg;
    }

    [Fact]
    public void TheJiggledBoneLagsTheStepAndSettlesOnIt()
    {
        var (armature, track, _, child) = Rig();

        // The parent snaps to 90° at frame 1. A rigid child is there at once;
        // the jiggling child is left behind and catches up over the hold.
        var atStep = ChildWorldAngle(armature, track, child, 1);
        Assert.True(atStep > 0.5 && atStep < 60,
            $"the child should lag the 90° step, got {atStep:F2}°");

        // Settled to within half a degree — an underdamped spring rings for a
        // long tail mathematically; what matters is that nothing visible moves.
        var settled = ChildWorldAngle(armature, track, child, 80);
        Assert.InRange(settled, 89.5, 90.5);

        // And without the jiggle record the same rig is rigid — absent means
        // absent, not a spring at some default.
        armature.BoneById(child)!.Jiggle = null;
        Assert.Equal(90, ChildWorldAngle(armature, track, child, 1), 6);
    }

    [Fact]
    public void LowDampingOvershootsLikeATailShould()
    {
        var (armature, track, _, child) = Rig(stiffness: 0.25, damping: 0.03);

        var peak = double.MinValue;
        for (var f = 2; f <= 50; f++)
            peak = Math.Max(peak, ChildWorldAngle(armature, track, child, f));

        // The swing carries past the target before settling back — that
        // overshoot IS the secondary motion; a filter that only lags would
        // read as drag, not life.
        Assert.True(peak > 92, $"no overshoot: peak {peak:F2}°");
    }

    [Fact]
    public void TheSpringIsBitIdenticalAcrossEvaluationsAndAReload()
    {
        var (armature, track, _, child) = Rig(stiffness: 0.31, damping: 0.07);

        var once = ChildWorldAngle(armature, track, child, 17);
        var twice = ChildWorldAngle(armature, track, child, 17);
        Assert.Equal(once, twice);   // exact, not within tolerance

        // Through the file: the spring is a pure function of the record, so a
        // reload solves to the same bits (determinism rules, DESIGN-bones.md).
        var doc = new Doc { Armature = armature };
        doc.Scene.PoseTrack = track;
        var reloaded = DocJson.Deserialize(DocJson.Serialize(doc));
        var after = ChildWorldAngle(reloaded.Armature!, reloaded.Scene.PoseTrack!, child, 17);
        Assert.Equal(once, after);
    }

    [Fact]
    public void ChildrenRideTheSwing()
    {
        var (armature, track, _, child) = Rig();
        var grandchild = new Bone { Name = "tail.end", ParentId = child, X = 40, Length = 30 };
        armature.Bones.Add(grandchild);

        var pose = ArmatureOps.EffectivePoseAt(armature, track, 1);
        var placements = ArmatureOps.Solve(armature, pose);

        // The grandchild's world angle carries the child's lag: the spring
        // wrote a local rotation, and locals compose down the chain.
        Assert.Equal(
            placements[child].RotationDeg, placements[grandchild.Id].RotationDeg, 6);
        Assert.True(placements[grandchild.Id].RotationDeg < 89,
            "the grandchild should share the lag, not snap to the target");
    }

    [Fact]
    public void AStillRigNeverWobblesOnItsOwn()
    {
        var (armature, track, parent, child) = Rig();
        // A track whose keys ask for nothing: the spring is seeded settled,
        // so nothing ever moves — visual life needs motion to react to.
        var still = new PoseTrack { Keys = [new PoseKey { Frame = 0 }, new PoseKey { Frame = 20 }] };
        for (var f = 0; f <= 30; f += 5)
            Assert.Equal(0, ChildWorldAngle(armature, still, child, f), 9);
        _ = parent;
        _ = track;
    }

    [Fact]
    public void AnUnjiggledRigWritesNoJiggleKeyAndTheEffectivePoseIsTheBasePose()
    {
        var (armature, track, _, child) = Rig();
        armature.BoneById(child)!.Jiggle = null;

        var doc = new Doc { Armature = armature };
        doc.Scene.PoseTrack = track;
        Assert.DoesNotContain("\"jiggle\"", DocJson.Serialize(doc), StringComparison.OrdinalIgnoreCase);

        var basePose = ArmatureOps.PoseAt(track, 1);
        var effective = ArmatureOps.EffectivePoseAt(armature, track, 1);
        Assert.Equal(basePose.Keys.Order(), effective.Keys.Order());
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void TheSpringWalkCostsTheWindowNotTheDocument()
    {
        // Invariant 6 for the spring: evaluating deep into a long timeline
        // replays at most the settle window, never the distance back to the
        // first key — this call sits inside per-pointer-event paths (the
        // weight dab, the heat view). Deliberately loose, like every budget:
        // it catches the walk becoming unbounded, not drift.
        var (armature, track, _, child) = Rig();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 50; i++)
            _ = ArmatureOps.EffectivePoseAt(armature, track, 100_000 + i);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 2000,
            $"50 deep evaluations took {sw.ElapsedMilliseconds} ms — the walk is scaling with the document");

        // And deep past the last key the spring has settled: the window edge
        // seeds from geometry, so a long-held pose is exactly the pose.
        Assert.Equal(90, ChildWorldAngle(armature, track, child, 100_000), 6);
    }

    [Fact]
    public void CloningABoneCopiesItsSpringRatherThanSharingIt()
    {
        var bone = new Bone { Jiggle = new BoneJiggle { Stiffness = 0.5 } };
        var copy = bone.Clone();
        copy.Jiggle!.Stiffness = 0.9;
        Assert.Equal(0.5, bone.Jiggle!.Stiffness, 9);
    }
}
