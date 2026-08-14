using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// Spline chains — a run of bones laid along a curve through handles. The
/// bendy-bone deformer, and the last slice of phase 3.
/// </summary>
/// <remarks>
/// <para>
/// The claim worth guarding hardest is that a spline <b>bends</b> a limb and
/// never <b>stretches</b> it. Every bone keeps its own length; the curve
/// decides direction only. A solver that fitted the run to the curve by
/// scaling would look right in a screenshot and be wrong in the way that
/// matters — a scaled bone scales the drawing bound to it, and every dab
/// dynamic re-rolls off the moved coordinates.
/// </para>
/// <para>
/// The rest is the rig's usual three: absent unless authored, bind mode never
/// sees it, and a circle is refused rather than iterated.
/// </para>
/// </remarks>
public class SplineChainTests
{
    /// <summary>Three 50px bones along +x from (100,100), plus three handles.</summary>
    private static Armature Tail(params (double X, double Y)[] handles)
    {
        var rig = new Armature
        {
            Bones =
            [
                new Bone { Id = "a", Name = "a", X = 100, Y = 100, Length = 50 },
                new Bone { Id = "b", Name = "b", ParentId = "a", X = 50, Connected = true, Length = 50 },
                new Bone { Id = "c", Name = "c", ParentId = "b", X = 50, Connected = true, Length = 50 },
            ],
        };
        var ids = new List<string>();
        for (var i = 0; i < handles.Length; i++)
        {
            var id = $"h{i}";
            rig.Bones.Add(new Bone { Id = id, Name = id, X = handles[i].X, Y = handles[i].Y, Length = 8 });
            ids.Add(id);
        }
        rig.Splines = [new SplineChain { TipBoneId = "c", Bones = 3, HandleBoneIds = ids }];
        return rig;
    }

    private static Dictionary<string, BonePlacement> Posed(Armature rig) =>
        ArmatureOps.Solve(rig, new Dictionary<string, BonePose>());

    private static double Len(BonePlacement from, BonePlacement to) =>
        Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y));

    [Fact]
    public void BonesFollowTheCurveAndKeepTheirOwnLength()
    {
        // Handles bending up and over: the run has to curve to follow.
        var rig = Tail((100, 100), (200, 40), (280, 140));
        var placed = Posed(rig);

        // Every joint sits exactly one bone length from the last — the whole
        // claim. A solver that fitted the run to the curve would fail here.
        Assert.Equal(50, Len(placed["a"], placed["b"]), 6);
        Assert.Equal(50, Len(placed["b"], placed["c"]), 6);

        // And it did bend: the three bones do not share one angle any more.
        Assert.NotEqual(placed["a"].RotationDeg, placed["b"].RotationDeg, 3);
        Assert.NotEqual(placed["b"].RotationDeg, placed["c"].RotationDeg, 3);

        // The run's own root has not moved: the curve steers the shape, it
        // does not teleport the shoulder.
        Assert.Equal(100, placed["a"].X, 6);
        Assert.Equal(100, placed["a"].Y, 6);
    }

    [Fact]
    public void AStraightLineOfHandlesLeavesTheRunStraight()
    {
        var rig = Tail((100, 100), (200, 100), (300, 100));
        var placed = Posed(rig);

        foreach (var id in new[] { "a", "b", "c" })
            Assert.Equal(0, placed[id].RotationDeg, 4);
        Assert.Equal(250, placed["c"].Tip(50).X, 3);
    }

    [Fact]
    public void TheCurvePassesThroughEveryHandleAnArtistPlaced()
    {
        // Catmull-Rom rather than a Bézier for exactly this: a handle is
        // somewhere the artist put the curve, not somewhere it leans toward.
        var rig = Tail((100, 100), (150, 180), (250, 100));
        var placed = Posed(rig);

        // The run walks the curve by arc length, so no joint is guaranteed to
        // land exactly on a handle — but one of them must come very close,
        // because the curve goes THROUGH (150,180). An interpolating spline
        // reaches it; a Bézier treating the same point as a control would
        // stay tens of pixels short, which is what this number separates.
        var nearest = new[] { placed["a"], placed["b"], placed["c"] }
            .Min(p => Math.Sqrt((p.X - 150) * (p.X - 150) + (p.Y - 180) * (p.Y - 180)));
        Assert.True(nearest < 12, $"no joint came near the handle: nearest {nearest:F1}px");
    }

    [Fact]
    public void MovingAHandleIsHowTheTailIsAnimated()
    {
        var rig = Tail((100, 100), (200, 100), (300, 100));
        // Handles are ordinary bones, so an ordinary pose translation on one
        // is the whole gesture — the reason they are bones at all.
        var pose = new Dictionary<string, BonePose> { ["h2"] = new BonePose { Y = -120 } };

        var placed = ArmatureOps.Solve(rig, pose);

        Assert.True(placed["c"].RotationDeg < -10,
            $"the tail tip should have swung up, got {placed["c"].RotationDeg}");
        Assert.Equal(50, Len(placed["b"], placed["c"]), 6);
    }

    [Fact]
    public void ARunLongerThanItsCurveTrailsStraightRatherThanKnotting()
    {
        // 150px of bone on a 60px curve.
        var rig = Tail((100, 100), (130, 100), (160, 100));
        var placed = Posed(rig);

        // Past the end it carries on along the curve's last direction, so the
        // joints stay a bone length apart instead of piling onto one point.
        Assert.Equal(50, Len(placed["a"], placed["b"]), 6);
        Assert.Equal(50, Len(placed["b"], placed["c"]), 6);
        Assert.Equal(0, placed["c"].RotationDeg, 3);
    }

    [Fact]
    public void FewerThanTwoHandlesIsNoCurveAndIsLeftAlone()
    {
        var rig = Tail((100, 100));
        Assert.Equal(0, Posed(rig)["b"].RotationDeg, 9);

        rig.Splines![0].HandleBoneIds = [];
        Assert.Equal(0, Posed(rig)["b"].RotationDeg, 9);

        // And a handle that is not there at all.
        rig.Splines![0].HandleBoneIds = ["h0", "nobody"];
        Assert.Equal(0, Posed(rig)["b"].RotationDeg, 9);
    }

    [Fact]
    public void AHandleCarriedByTheRunIsRefusedRatherThanIterated()
    {
        var rig = Tail((100, 100), (200, 40), (280, 140));
        // A handle hanging off the bones it steers: it moves because the run
        // moved, and the run moves because it did.
        rig.BoneById("h1")!.ParentId = "b";
        Assert.Equal(0, Posed(rig)["b"].RotationDeg, 9);

        // And a handle that IS one of the bones.
        rig.BoneById("h1")!.ParentId = null;
        rig.Splines![0].HandleBoneIds = ["h0", "b", "h2"];
        Assert.Equal(0, Posed(rig)["b"].RotationDeg, 9);
    }

    [Fact]
    public void BindModeNeverSeesSplines()
    {
        var rig = Tail((100, 100), (200, 40), (280, 140));

        var rest = ArmatureOps.Solve(rig);
        Assert.Equal(0, rest["b"].RotationDeg, 9);
        Assert.Equal(250, rest["c"].Tip(50).X, 9);

        Assert.NotEqual(0, Posed(rig)["b"].RotationDeg);
    }

    [Fact]
    public void ConstraintsStillWinBecauseTheyResolveLast()
    {
        var rig = Tail((100, 100), (200, 40), (280, 140));
        rig.Bones.Add(new Bone { Id = "look", X = 100, Y = 400, Length = 8 });
        var splineOnly = Posed(rig)["c"].RotationDeg;

        rig.Constraints =
            [new BoneConstraint { Kind = BoneConstraintKind.Aim, BoneId = "c", TargetBoneId = "look" }];
        var withBoth = Posed(rig);

        Assert.NotEqual(splineOnly, withBoth["c"].RotationDeg, 3);
        var expected = Math.Atan2(400 - withBoth["c"].Y, 100 - withBoth["c"].X) * 180 / Math.PI;
        Assert.Equal(expected, withBoth["c"].RotationDeg, 4);
    }

    [Fact]
    public void TheSolveIsBitIdenticalOnReload()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armature = Tail((100, 100), (200, 40), (280, 140));
        var back = DocJson.Deserialize(DocJson.Serialize(doc)).Armature!;

        var a = Posed(doc.Armature);
        var b = Posed(back);
        foreach (var id in new[] { "a", "b", "c" })
        {
            Assert.Equal(a[id].X, b[id].X);
            Assert.Equal(a[id].Y, b[id].Y);
            Assert.Equal(a[id].RotationDeg, b[id].RotationDeg);
        }
    }

    [Fact]
    public void ARigWithNoSplineWritesNoKeys()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armature = new Armature { Bones = [new Bone { Id = "only" }] };

        var json = DocJson.Serialize(doc);

        Assert.DoesNotContain("\"splines\"", json);
        Assert.DoesNotContain("\"handleBoneIds\"", json);
    }

    [Fact]
    public void CloningARigCopiesItsSplinesDeeply()
    {
        var rig = Tail((100, 100), (200, 40));
        var copy = rig.Clone();

        copy.Splines![0].HandleBoneIds.Add("h9");
        copy.Splines![0].Bones = 9;

        Assert.Equal(2, rig.Splines![0].HandleBoneIds.Count);
        Assert.Equal(3, rig.Splines![0].Bones);
    }
}
