using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// The armature record — phase 1 of <c>docs/DESIGN-bones.md</c>. Half of what
/// these tests protect is the FK arithmetic; the other half is the record's
/// <em>absence</em>, because a document that never rigs must save, load and
/// render exactly as it did before the feature existed — the camera's rule,
/// and the camera's tests are the template.
/// </summary>
public class ArmatureTests
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

    [Fact]
    public void AnUnriggedDocumentWritesNoRigKeys()
    {
        var json = DocJson.Serialize(DocumentFactory.CreateDoc());
        Assert.DoesNotContain("\"armature\"", json);
        Assert.DoesNotContain("\"poseTrack\"", json);
        Assert.DoesNotContain("\"hasArmature\"", json);
    }

    [Fact]
    public void ARiggedDocumentRoundTrips()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armature = Arm();
        doc.Scene.PoseTrack = new PoseTrack
        {
            Keys =
            [
                new PoseKey { Frame = 0, Ease = Easing.Linear },
                new PoseKey
                {
                    Frame = 8,
                    Bones = { ["upper"] = new BonePose { RotationDeg = 45, X = 3, Y = -2 } },
                },
            ],
        };

        var back = DocJson.Deserialize(DocJson.Serialize(doc));

        Assert.NotNull(back.Armature);
        Assert.Equal(2, back.Armature!.Bones.Count);
        Assert.Equal("upper", back.Armature.BoneById("fore")!.ParentId);
        Assert.Equal(80, back.Armature.BoneById("fore")!.X);
        var key = ArmatureOps.KeyAt(back.Scene.PoseTrack, 8)!;
        Assert.Equal(45, key.Bones["upper"].RotationDeg);
        Assert.Equal(Easing.Linear, ArmatureOps.KeyAt(back.Scene.PoseTrack, 0)!.Ease);
    }

    [Fact]
    public void TheBindPoseSolvesWithNoPoseAtAll()
    {
        var placements = ArmatureOps.Solve(Arm());
        Assert.Equal(new BonePlacement(100, 100, 0), placements["upper"]);
        // The forearm's origin sits 80 px along the unrotated upper arm.
        Assert.Equal(new BonePlacement(180, 100, 0), placements["fore"]);
    }

    [Fact]
    public void PosingTheParentCarriesTheChildWithIt()
    {
        var pose = new Dictionary<string, BonePose> { ["upper"] = new BonePose { RotationDeg = 90 } };
        var placements = ArmatureOps.Solve(Arm(), pose);

        // The parent turned in place…
        Assert.Equal(100, placements["upper"].X, 12);
        Assert.Equal(100, placements["upper"].Y, 12);
        Assert.Equal(90, placements["upper"].RotationDeg, 12);
        // …and the child rode it: its origin swung from (180,100) to below the shoulder.
        Assert.Equal(100, placements["fore"].X, 12);
        Assert.Equal(180, placements["fore"].Y, 12);
        Assert.Equal(90, placements["fore"].RotationDeg, 12);
        // The forearm's tip continues down the rotated chain.
        var (tx, ty) = placements["fore"].Tip(60);
        Assert.Equal(100, tx, 12);
        Assert.Equal(240, ty, 12);
    }

    [Fact]
    public void APoseInterpolatesBetweenKeysAndHoldsOutsideThem()
    {
        var track = new PoseTrack
        {
            Keys =
            [
                new PoseKey { Frame = 10, Ease = Easing.Linear, Bones = { ["upper"] = new BonePose { RotationDeg = 40 } } },
                new PoseKey { Frame = 0, Ease = Easing.Linear },
            ],
        };

        // Keys were authored out of order on purpose — Ordered sorts.
        Assert.Equal(20, ArmatureOps.PoseAt(track, 5)["upper"].RotationDeg, 12);
        // Outside the range the pose holds rather than extrapolating.
        Assert.Equal(40, ArmatureOps.PoseAt(track, 99)["upper"].RotationDeg, 12);
        Assert.Empty(ArmatureOps.PoseAt(track, -5));
    }

    [Fact]
    public void ABoneMissingFromAKeyInterpolatesFromRest()
    {
        var track = new PoseTrack
        {
            Keys =
            [
                new PoseKey { Frame = 0, Ease = Easing.Linear },
                new PoseKey { Frame = 4, Ease = Easing.Linear, Bones = { ["fore"] = new BonePose { RotationDeg = -60, X = 8 } } },
            ],
        };

        var mid = ArmatureOps.PoseAt(track, 2)["fore"];
        Assert.Equal(-30, mid.RotationDeg, 12);
        Assert.Equal(4, mid.X, 12);
    }

    [Fact]
    public void TheSolveIsBitIdenticalAcrossAReload()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armature = Arm();
        doc.Scene.PoseTrack = new PoseTrack
        {
            Keys =
            [
                new PoseKey { Frame = 0, Bones = { ["upper"] = new BonePose { RotationDeg = 13.7, X = 0.125 } } },
                new PoseKey { Frame = 7, Bones = { ["upper"] = new BonePose { RotationDeg = 61.3, Y = -4.25 }, ["fore"] = new BonePose { RotationDeg = -22.9 } } },
            ],
        };

        var back = DocJson.Deserialize(DocJson.Serialize(doc));

        for (var frame = 0; frame <= 7; frame++)
        {
            var a = ArmatureOps.Solve(doc.Armature, ArmatureOps.PoseAt(doc.Scene.PoseTrack, frame));
            var b = ArmatureOps.Solve(back.Armature!, ArmatureOps.PoseAt(back.Scene.PoseTrack, frame));
            foreach (var (id, placement) in a)
            {
                // Exact equality on purpose: a reload must solve to the bit,
                // or replay and inbetweens diverge (invariant 2's shape).
                Assert.Equal(placement, b[id]);
            }
        }
    }

    [Fact]
    public void ABrokenHierarchySolvesRatherThanHanging()
    {
        // A parent id pointing at nothing, and a two-bone cycle.
        var armature = new Armature
        {
            Bones =
            [
                new Bone { Id = "orphan", ParentId = "gone", X = 10 },
                new Bone { Id = "a", ParentId = "b", X = 1 },
                new Bone { Id = "b", ParentId = "a", X = 2 },
            ],
        };

        var placements = ArmatureOps.Solve(armature);

        Assert.Equal(3, placements.Count);
        // The orphan solves as a root.
        Assert.Equal(new BonePlacement(10, 0, 0), placements["orphan"]);
    }

    [Fact]
    public void CloningARiggedDocumentSharesNothing()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armature = Arm();
        doc.Scene.PoseTrack = new PoseTrack
        {
            Keys = [new PoseKey { Frame = 3, Bones = { ["upper"] = new BonePose { RotationDeg = 10 } } }],
        };

        var copy = doc.Clone();
        copy.Armature!.Bones[0].RotationDeg = 45;
        copy.Scene.PoseTrack!.Keys[0].Bones["upper"].RotationDeg = 99;

        Assert.Equal(0, doc.Armature!.Bones[0].RotationDeg);
        Assert.Equal(10, doc.Scene.PoseTrack!.Keys[0].Bones["upper"].RotationDeg);

        // And a document that never rigged clones to one that still hasn't.
        var plain = DocumentFactory.CreateDoc().Clone();
        Assert.Null(plain.Armature);
        Assert.Null(plain.Scene.PoseTrack);
    }
}
