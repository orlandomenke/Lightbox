using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// Constraints — one bone made to follow another. Phase 3 of
/// <c>docs/DESIGN-bones.md</c>, after IK.
/// </summary>
/// <remarks>
/// <para>
/// Three narrow kinds instead of one wide "transform copy", so every
/// constraint does a thing an artist can name and no record carries a tick
/// box sitting at false forever. Stacking two of them gives the one
/// combination people actually use.
/// </para>
/// <para>
/// The rules guarded here are the same three IK's are, because they are the
/// rig's rules rather than IK's: <b>absent unless authored</b>, <b>bind mode
/// never sees them</b>, and <b>a circle is refused rather than iterated</b>.
/// </para>
/// </remarks>
public class BoneConstraintTests
{
    /// <summary>A head bone at (100,100) pointing along +x, and something to look at.</summary>
    private static Armature Rig() => new()
    {
        Bones =
        [
            new Bone { Id = "head", Name = "Head", X = 100, Y = 100, Length = 40 },
            new Bone { Id = "look", Name = "Look at", X = 100, Y = 200, Length = 10 },
            new Bone { Id = "other", Name = "Other", X = 300, Y = 100, RotationDeg = 30, Length = 20 },
        ],
    };

    private static Dictionary<string, BonePlacement> Posed(Armature rig) =>
        ArmatureOps.Solve(rig, new Dictionary<string, BonePose>());

    [Fact]
    public void AnAimConstraintTurnsTheBoneToPointAtItsTarget()
    {
        var rig = Rig();
        rig.Constraints =
            [new BoneConstraint { Kind = BoneConstraintKind.Aim, BoneId = "head", TargetBoneId = "look" }];

        // (100,100) looking at (100,200) is straight down: +90° clockwise.
        Assert.Equal(90, Posed(rig)["head"].RotationDeg, 6);
    }

    [Fact]
    public void TheOffsetIsAddedAfterTheConstraintResolves()
    {
        var rig = Rig();
        rig.Constraints =
        [
            new BoneConstraint
            {
                Kind = BoneConstraintKind.Aim, BoneId = "head", TargetBoneId = "look", OffsetDeg = -30,
            },
        ];

        Assert.Equal(60, Posed(rig)["head"].RotationDeg, 6);
    }

    [Fact]
    public void InfluenceIsHowFarTowardTheAnswerTheBoneGoes()
    {
        var rig = Rig();
        rig.Constraints =
        [
            new BoneConstraint
            {
                Kind = BoneConstraintKind.Aim, BoneId = "head", TargetBoneId = "look", Influence = 0.25,
            },
        ];

        // A quarter of the way from rest (0°) to the aim (90°).
        Assert.Equal(22.5, Posed(rig)["head"].RotationDeg, 6);

        // Zero is off, not "aim at nothing": the bone must be exactly where it
        // was, or a slider dragged to the bottom would still have moved it.
        rig.Constraints[0].Influence = 0;
        Assert.Equal(0, Posed(rig)["head"].RotationDeg, 9);
    }

    [Fact]
    public void CopyRotationTakesTheTargetsWorldAngle()
    {
        var rig = Rig();
        rig.Constraints =
            [new BoneConstraint { Kind = BoneConstraintKind.CopyRotation, BoneId = "head", TargetBoneId = "other" }];

        var placed = Posed(rig);
        Assert.Equal(30, placed["head"].RotationDeg, 6);
        // Rotation only: the bone has not moved to the target.
        Assert.Equal(100, placed["head"].X, 6);
        Assert.Equal(100, placed["head"].Y, 6);
    }

    [Fact]
    public void CopyPositionTakesTheTargetsOriginAndNothingElse()
    {
        var rig = Rig();
        rig.Constraints =
            [new BoneConstraint { Kind = BoneConstraintKind.CopyPosition, BoneId = "head", TargetBoneId = "other" }];

        var placed = Posed(rig);
        Assert.Equal(300, placed["head"].X, 6);
        Assert.Equal(100, placed["head"].Y, 6);
        Assert.Equal(0, placed["head"].RotationDeg, 6);
    }

    [Fact]
    public void StackingTwoIsHowAFullTransformCopyIsWritten()
    {
        var rig = Rig();
        rig.Constraints =
        [
            new BoneConstraint { Kind = BoneConstraintKind.CopyPosition, BoneId = "head", TargetBoneId = "other" },
            new BoneConstraint { Kind = BoneConstraintKind.CopyRotation, BoneId = "head", TargetBoneId = "other" },
        ];

        var placed = Posed(rig);
        Assert.Equal(300, placed["head"].X, 6);
        Assert.Equal(100, placed["head"].Y, 6);
        Assert.Equal(30, placed["head"].RotationDeg, 6);
    }

    [Fact]
    public void ALaterConstraintSeesWhatAnEarlierOneDid()
    {
        var rig = Rig();
        // The "other" bone copies the head's aim, so it can only be right if
        // the head's constraint resolved before it was read — which is what
        // makes list order part of the rig.
        rig.Constraints =
        [
            new BoneConstraint { Kind = BoneConstraintKind.Aim, BoneId = "head", TargetBoneId = "look" },
            new BoneConstraint { Kind = BoneConstraintKind.CopyRotation, BoneId = "other", TargetBoneId = "head" },
        ];

        Assert.Equal(90, Posed(rig)["other"].RotationDeg, 6);
    }

    [Fact]
    public void AConstraintResolvesAfterIkAndWins()
    {
        var rig = new Armature
        {
            Bones =
            [
                new Bone { Id = "upper", X = 100, Y = 100, Length = 50 },
                new Bone { Id = "fore", ParentId = "upper", X = 50, Connected = true, Length = 50 },
                new Bone { Id = "goal", X = 160, Y = 160, Length = 10 },
                new Bone { Id = "look", X = 100, Y = 300, Length = 10 },
            ],
            Chains = [new IkChain { TipBoneId = "fore", TargetBoneId = "goal", Bones = 2 }],
        };
        var withIkAlone = Posed(rig)["fore"].RotationDeg;

        rig.Constraints =
            [new BoneConstraint { Kind = BoneConstraintKind.Aim, BoneId = "fore", TargetBoneId = "look" }];
        var withBoth = Posed(rig)["fore"].RotationDeg;

        // The order is deliberate: constraints run after IK, so an aim can
        // override a solved elbow rather than being quietly undone by it.
        Assert.NotEqual(withIkAlone, withBoth, 6);
        var placed = Posed(rig);
        var expected = Math.Atan2(300 - placed["fore"].Y, 100 - placed["fore"].X) * 180 / Math.PI;
        Assert.Equal(expected, placed["fore"].RotationDeg, 4);
    }

    [Fact]
    public void ACircleIsRefusedRatherThanIterated()
    {
        var rig = Rig();
        rig.Bones.Add(new Bone { Id = "child", ParentId = "head", X = 40, Length = 10 });

        // Aiming at your own child: the target's placement depends on the
        // rotation the constraint is about to write, so there is no answer.
        rig.Constraints =
            [new BoneConstraint { Kind = BoneConstraintKind.Aim, BoneId = "head", TargetBoneId = "child" }];
        Assert.Equal(0, Posed(rig)["head"].RotationDeg, 9);

        // And aiming at yourself.
        rig.Constraints[0].TargetBoneId = "head";
        Assert.Equal(0, Posed(rig)["head"].RotationDeg, 9);

        // And a target that is not there at all.
        rig.Constraints[0].TargetBoneId = "nobody";
        Assert.Equal(0, Posed(rig)["head"].RotationDeg, 9);
    }

    [Fact]
    public void BindModeNeverSeesConstraints()
    {
        var rig = Rig();
        rig.Constraints =
            [new BoneConstraint { Kind = BoneConstraintKind.Aim, BoneId = "head", TargetBoneId = "look" }];

        // A null pose is the rest: the skeleton an artist edits in bind mode
        // is the one they authored, not the one the solver would like.
        Assert.Equal(0, ArmatureOps.Solve(rig)["head"].RotationDeg, 9);
        Assert.Equal(90, Posed(rig)["head"].RotationDeg, 6);
    }

    [Fact]
    public void ARigWithNoConstraintsWritesNoKeys()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armature = Rig();

        var json = DocJson.Serialize(doc);

        Assert.DoesNotContain("\"constraints\"", json);
        Assert.DoesNotContain("\"strength\"", json);   // the derived getter must not leak
    }

    [Fact]
    public void AnOrdinaryConstraintWritesNeitherOffsetNorInfluence()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armature = Rig();
        doc.Armature.Constraints =
            [new BoneConstraint { Kind = BoneConstraintKind.Aim, BoneId = "head", TargetBoneId = "look" }];

        var json = DocJson.Serialize(doc);

        // All the way on, no offset: the common case pays for neither.
        Assert.DoesNotContain("\"offsetDeg\"", json);
        Assert.DoesNotContain("\"influence\"", json);
        Assert.Contains("\"kind\": \"aim\"", json);
    }

    [Fact]
    public void ConstraintsSurviveASaveAndLoadAndSolveTheSame()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armature = Rig();
        doc.Armature.Constraints =
        [
            new BoneConstraint
            {
                Kind = BoneConstraintKind.CopyRotation, BoneId = "head", TargetBoneId = "other",
                OffsetDeg = 12, Influence = 0.5,
            },
        ];

        var back = DocJson.Deserialize(DocJson.Serialize(doc));

        var c = Assert.Single(back.Armature!.Constraints!);
        Assert.Equal(BoneConstraintKind.CopyRotation, c.Kind);
        Assert.Equal(12, c.OffsetDeg);
        Assert.Equal(0.5, c.Influence);
        Assert.Equal(Posed(doc.Armature)["head"].RotationDeg, Posed(back.Armature!)["head"].RotationDeg);
    }

    [Fact]
    public void CloningARigCopiesItsConstraints()
    {
        var rig = Rig();
        rig.Constraints =
            [new BoneConstraint { Kind = BoneConstraintKind.Aim, BoneId = "head", TargetBoneId = "look" }];

        var copy = rig.Clone();
        copy.Constraints![0].TargetBoneId = "other";

        Assert.Equal("look", rig.Constraints![0].TargetBoneId);
    }
}
