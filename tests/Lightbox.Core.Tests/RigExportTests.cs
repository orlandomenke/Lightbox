using Lightbox.Core.Documents;
using Lightbox.Core.Export;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// The rig export's own schema — phase 6's source of truth. The claims worth
/// pinning: the skeleton and its authoring survive verbatim, the baked block
/// replays the solve exactly, names are unique, and a document without a rig
/// has nothing here.
/// </summary>
public class RigExportTests
{
    /// <summary>An IK-free two-bone chain with a jiggling child and two keys.</summary>
    private static Doc Rigged()
    {
        var parent = new Bone { Name = "tail.base", X = 100, Y = 100, Length = 50 };
        var child = new Bone
        {
            Name = "tail.tip",
            ParentId = parent.Id,
            X = 50,
            Length = 40,
            Jiggle = new BoneJiggle { Stiffness = 0.3, Damping = 0.1 },
        };
        var doc = new Doc { Armature = new Armature { Bones = [parent, child] } };
        doc.Scene.Fps = 12;
        doc.Scene.FrameCount = 6;
        doc.Scene.PoseTrack = new PoseTrack
        {
            Keys =
            [
                new PoseKey { Frame = 0 },
                new PoseKey { Frame = 3, Bones = { [parent.Id] = new BonePose { RotationDeg = 90 } } },
            ],
        };
        return doc;
    }

    [Fact]
    public void TheSkeletonAndItsAuthoringSurviveVerbatim()
    {
        var rig = RigExport.Build(Rigged());

        Assert.Equal(1, rig.Schema);
        Assert.Equal(12, rig.Fps);
        Assert.Equal(6, rig.FrameCount);

        var baseBone = rig.Bones.Single(b => b.Name == "tail.base");
        var tip = rig.Bones.Single(b => b.Name == "tail.tip");
        Assert.Null(baseBone.Parent);
        Assert.Equal("tail.base", tip.Parent);
        Assert.Equal(50, tip.X, 9);
        Assert.Equal(40, tip.Length, 9);
        // The spring settings ride along, so a round trip loses nothing.
        Assert.NotNull(tip.Jiggle);
        Assert.Equal(0.3, tip.Jiggle!.Stiffness, 9);

        // The authored keys are the record, not a render of it: sparse, with
        // their easing named.
        Assert.Equal(2, rig.Keys.Count);
        Assert.Empty(rig.Keys[0].Bones);
        Assert.Equal(90, rig.Keys[1].Bones["tail.base"].RotationDeg, 9);
    }

    [Fact]
    public void TheBakedBlockReplaysTheSolveExactly()
    {
        var doc = Rigged();
        var rig = RigExport.Build(doc);
        Assert.Equal(6, rig.Baked.Count);

        // Composing the exported locals down the hierarchy must land every
        // bone exactly where the solver put it — springs included. This is
        // the whole claim of the baked block: an engine that plays it plays
        // what the artist saw, with no solver of its own.
        for (var f = 0; f < 6; f++)
        {
            var pose = ArmatureOps.EffectivePoseAt(doc.Armature!, doc.Scene.PoseTrack, f);
            var solved = ArmatureOps.Solve(doc.Armature!, pose);
            var names = RigExport.UniqueNames(doc.Armature!);

            foreach (var bone in doc.Armature!.Bones)
            {
                var world = Compose(rig, rig.Baked[f], names[bone.Id]);
                Assert.Equal(solved[bone.Id].X, world.X, 6);
                Assert.Equal(solved[bone.Id].Y, world.Y, 6);
                Assert.Equal(solved[bone.Id].RotationDeg, world.Rot, 6);
            }
        }

        // And the jiggle is visibly in the samples: at the step frame the
        // child lags the parent's 90° swing.
        var tip = rig.Baked[3].Bones["tail.tip"];
        Assert.True(Math.Abs(tip.RotationDeg) > 1,
            $"the baked child shows no lag: {tip.RotationDeg:F2}°");
    }

    private static (double X, double Y, double Rot) Compose(RigDocument rig, RigFrame frame, string name)
    {
        var bone = rig.Bones.Single(b => b.Name == name);
        var local = frame.Bones[name];
        if (bone.Parent is null) return (local.X, local.Y, local.RotationDeg);
        var (px, py, pr) = Compose(rig, frame, bone.Parent);
        var rad = pr * Math.PI / 180.0;
        return (
            px + Math.Cos(rad) * local.X - Math.Sin(rad) * local.Y,
            py + Math.Sin(rad) * local.X + Math.Cos(rad) * local.Y,
            pr + local.RotationDeg);
    }

    [Fact]
    public void TwoBonesSharingANameExportAsTwoNames()
    {
        var doc = Rigged();
        doc.Armature!.Bones[1].Name = "tail.base";   // now both are tail.base

        var rig = RigExport.Build(doc);

        Assert.Equal(["tail.base", "tail.base#2"], rig.Bones.Select(b => b.Name).ToList());
        // The child's parent reference follows the renamed set.
        Assert.Equal("tail.base", rig.Bones[1].Parent);
    }

    [Fact]
    public void TheExportIsDeterministic()
    {
        var doc = Rigged();
        Assert.Equal(RigExport.ExportJson(doc), RigExport.ExportJson(doc));
    }

    [Fact]
    public void AnUnriggedDocumentHasNothingHere()
    {
        Assert.False(RigExport.HasRig(new Doc()));
        Assert.Throws<InvalidOperationException>(() => RigExport.Build(new Doc()));
    }
}
