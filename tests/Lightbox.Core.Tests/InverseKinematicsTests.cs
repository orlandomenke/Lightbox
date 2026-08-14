using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// IK — phase 3 of <c>docs/DESIGN-bones.md</c>. A limb reaches for a target
/// bone instead of being rotated joint by joint.
/// </summary>
/// <remarks>
/// <para>
/// Three things are guarded here and each of them is a rule rather than a
/// number: the solve is <b>deterministic</b> (a fixed pass count, no
/// tolerance loop, so a reload and an inbetween agree to the bit), the chain
/// is <b>absent</b> from a rig that has none, and <b>bind mode never sees
/// IK</b> — a null pose is the rest, and an artist editing the rest must not
/// have the solver pulling the joint back out from under the drag.
/// </para>
/// <para>
/// Angles are read rather than joint positions wherever possible: a two-bone
/// chain reaching an in-range target has one answer per bend side, and
/// asserting the tip landed on the target is the assertion that would also
/// pass on a solver that just teleported the last bone.
/// </para>
/// </remarks>
public class InverseKinematicsTests
{
    /// <summary>Two 50px bones lying along +x from (100,100), tip at (200,100).</summary>
    private static Armature Limb(double targetX, double targetY, string? pole = null) => new()
    {
        Bones =
        [
            new Bone { Id = "upper", Name = "Upper", X = 100, Y = 100, Length = 50 },
            new Bone { Id = "fore", Name = "Fore", ParentId = "upper", X = 50, Connected = true, Length = 50 },
            new Bone { Id = "goal", Name = "Goal", X = targetX, Y = targetY, Length = 10 },
            new Bone { Id = "pole", Name = "Pole", X = 150, Y = 0, Length = 10 },
        ],
        Chains = [new IkChain { Id = "ik", TipBoneId = "fore", TargetBoneId = "goal", Bones = 2, PoleBoneId = pole }],
    };

    private static (double X, double Y) TipOf(Armature rig, IReadOnlyDictionary<string, BonePlacement> p) =>
        p["fore"].Tip(rig.BoneById("fore")!.Length);

    [Fact]
    public void TheChainReachesATargetItCanReach()
    {
        var rig = Limb(160, 160);
        var placed = ArmatureOps.Solve(rig, new Dictionary<string, BonePose>());

        var (tx, ty) = TipOf(rig, placed);
        Assert.Equal(160, tx, 3);
        Assert.Equal(160, ty, 3);
        // And the chain did not come apart doing it: the forearm still starts
        // where the upper arm ends.
        var elbow = placed["upper"].Tip(50);
        Assert.Equal(elbow.X, placed["fore"].X, 6);
        Assert.Equal(elbow.Y, placed["fore"].Y, 6);
    }

    [Fact]
    public void OutOfReachPointsStraightAtTheTargetInsteadOfFlailing()
    {
        var rig = Limb(500, 100);
        var placed = ArmatureOps.Solve(rig, new Dictionary<string, BonePose>());

        // Fully extended along the line to the target — 100px of bone from
        // (100,100), so the tip stops at 200 rather than pretending to arrive.
        var (tx, ty) = TipOf(rig, placed);
        Assert.Equal(200, tx, 3);
        Assert.Equal(100, ty, 3);
        Assert.Equal(0, placed["upper"].RotationDeg, 3);
        Assert.Equal(0, placed["fore"].RotationDeg, 3);
    }

    [Fact]
    public void ThePoleDecidesWhichWayTheKneeBends()
    {
        // The same target, reachable, with the pole above and below. A knee
        // that ignores the pole gives the same answer twice, which is the
        // failure this catches.
        var up = ArmatureOps.Solve(Limb(190, 100, pole: "pole"), new Dictionary<string, BonePose>());

        var rigDown = Limb(190, 100, pole: "pole");
        rigDown.BoneById("pole")!.Y = 200;
        var down = ArmatureOps.Solve(rigDown, new Dictionary<string, BonePose>());

        // Elbow above the line one way, below it the other.
        Assert.True(up["fore"].Y < 100, $"pole above should lift the elbow, got {up["fore"].Y}");
        Assert.True(down["fore"].Y > 100, $"pole below should drop the elbow, got {down["fore"].Y}");
    }

    [Fact]
    public void MovingTheTargetBoneIsHowTheLimbIsPosed()
    {
        var rig = Limb(160, 160);
        // The target is an ordinary bone, so an ordinary pose translation on
        // it is the whole gesture — that is what buying a target bone bought.
        var pose = new Dictionary<string, BonePose> { ["goal"] = new BonePose { X = -20, Y = -40 } };

        var placed = ArmatureOps.Solve(rig, pose);

        var (tx, ty) = TipOf(rig, placed);
        Assert.Equal(140, tx, 3);
        Assert.Equal(120, ty, 3);
    }

    [Fact]
    public void AFixedIterationIkSolveIsBitIdenticalOnReload()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armature = Limb(140, 170);
        var rig = doc.Armature;
        var reloaded = DocJson.Deserialize(DocJson.Serialize(doc)).Armature!;

        var a = ArmatureOps.Solve(rig, new Dictionary<string, BonePose>());
        var b = ArmatureOps.Solve(reloaded, new Dictionary<string, BonePose>());

        // Bit-identical, not merely close: a reload, an undo and an inbetween
        // all re-solve, and a tolerance loop would let them disagree.
        foreach (var id in new[] { "upper", "fore" })
        {
            Assert.Equal(a[id].X, b[id].X);
            Assert.Equal(a[id].Y, b[id].Y);
            Assert.Equal(a[id].RotationDeg, b[id].RotationDeg);
        }
    }

    [Fact]
    public void BindModeNeverSeesIk()
    {
        var rig = Limb(160, 160);

        // A null pose is the rest: the limb lies along +x exactly as authored,
        // whatever the chain would like. Without this an artist dragging a
        // joint in bind mode fights the solver for it.
        var rest = ArmatureOps.Solve(rig);
        Assert.Equal(0, rest["upper"].RotationDeg, 9);
        Assert.Equal(0, rest["fore"].RotationDeg, 9);
        Assert.Equal(200, rest["fore"].Tip(50).X, 9);

        // An empty pose is a pose, and does solve.
        var posed = ArmatureOps.Solve(rig, new Dictionary<string, BonePose>());
        Assert.NotEqual(0, posed["upper"].RotationDeg);
    }

    [Fact]
    public void ATargetInsideItsOwnChainIsRefusedRatherThanIterated()
    {
        var rig = Limb(160, 160);
        rig.Chains![0].TargetBoneId = "fore";           // the chain's own tip
        Assert.Equal(0, ArmatureOps.Solve(rig, new Dictionary<string, BonePose>())["upper"].RotationDeg, 9);

        // And a bone hanging off the chain is the same circle one step out.
        rig.Chains[0].TargetBoneId = "goal";
        rig.BoneById("goal")!.ParentId = "fore";
        Assert.Equal(0, ArmatureOps.Solve(rig, new Dictionary<string, BonePose>())["upper"].RotationDeg, 9);
    }

    [Fact]
    public void AMissingTargetLeavesTheChainAlone()
    {
        var rig = Limb(160, 160);
        rig.Chains![0].TargetBoneId = "nobody";
        var placed = ArmatureOps.Solve(rig, new Dictionary<string, BonePose>());
        Assert.Equal(0, placed["upper"].RotationDeg, 9);
    }

    [Fact]
    public void ARigWithNoIkWritesNoChainKey()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armature = new Armature { Bones = [new Bone { Id = "only" }] };

        var json = DocJson.Serialize(doc);

        // The camera's rule again: unused means absent, not present-and-empty.
        Assert.DoesNotContain("\"chains\"", json);
        Assert.DoesNotContain("\"poleBoneId\"", json);
    }

    [Fact]
    public void AChainSurvivesASaveAndLoad()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armature = Limb(160, 160, pole: "pole");

        var back = DocJson.Deserialize(DocJson.Serialize(doc));

        var chain = Assert.Single(back.Armature!.Chains!);
        Assert.Equal("fore", chain.TipBoneId);
        Assert.Equal("goal", chain.TargetBoneId);
        Assert.Equal("pole", chain.PoleBoneId);
        Assert.Equal(2, chain.Bones);

        // And it solves to the same place it did before the round trip, which
        // is invariant 1's promise applied to the rig.
        var before = ArmatureOps.Solve(doc.Armature, new Dictionary<string, BonePose>());
        var after = ArmatureOps.Solve(back.Armature!, new Dictionary<string, BonePose>());
        Assert.Equal(before["fore"].RotationDeg, after["fore"].RotationDeg);
    }

    [Fact]
    public void CloningARigCopiesItsChains()
    {
        var rig = Limb(160, 160);
        var copy = rig.Clone();
        copy.Chains![0].TargetBoneId = "pole";
        Assert.Equal("goal", rig.Chains![0].TargetBoneId);
    }
}
