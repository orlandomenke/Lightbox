using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// A document holds any number of rigs, all equal — Q182, and the record half
/// of it.
/// </summary>
/// <remarks>
/// <para>
/// The owner's correction to Q181: a rig you place to draw over wants to be
/// <em>animated</em>, which makes it a second character rather than a drawing
/// aid. Two characters interacting in one shot are two rigs with art bound to
/// both, so the second rig is a peer and not a reference.
/// </para>
/// <para>
/// <b>The animation half needed no new record.</b> <see cref="PoseKey.Bones"/>
/// is keyed by bone id and is sparse — a bone absent from a key is at rest on
/// it — so one <see cref="PoseTrack"/> has always been able to carry several
/// rigs' poses without ambiguity. <see cref="OnePoseTrackCarriesTwoRigsWithoutAmbiguity"/>
/// is that claim, held rather than assumed, because the whole design rests on
/// it.
/// </para>
/// </remarks>
public class ManyRigsTests(ITestOutputHelper output)
{
    private static Armature Rig(string name, string boneId) => new()
    {
        Name = name,
        Bones = [new Bone { Id = boneId, Name = $"{name} spine", Length = 100 }],
    };

    [Fact]
    public void OnePoseTrackCarriesTwoRigsWithoutAmbiguity()
    {
        var knight = Rig("Knight", "knight-spine");
        var dog = Rig("Dog", "dog-spine");
        var track = new PoseTrack
        {
            Keys =
            [
                new PoseKey
                {
                    Frame = 0,
                    Bones = new Dictionary<string, BonePose>
                    {
                        ["knight-spine"] = new() { RotationDeg = 30 },
                        ["dog-spine"] = new() { RotationDeg = -45 },
                    },
                },
            ],
        };

        var pose = ArmatureOps.PoseAt(track, 0);
        var knightSolved = ArmatureOps.Solve(knight, pose);
        var dogSolved = ArmatureOps.Solve(dog, pose);

        // Each rig reads its own bone out of the shared key and ignores the
        // other's, because a bone it does not own is simply absent to it.
        Assert.Equal(30, knightSolved["knight-spine"].RotationDeg, 6);
        Assert.Equal(-45, dogSolved["dog-spine"].RotationDeg, 6);
        Assert.DoesNotContain("dog-spine", knightSolved.Keys);
        output.WriteLine("one track, two rigs, no new record needed");
    }

    [Fact]
    public void ADocumentWrittenBeforeManyRigsOpensWithItsRigIntact()
    {
        // Exactly what a rigged document on disk looks like today: one
        // "armature" object, no list.
        var legacy = """
        {
          "scene": { "name": "Scene 1", "width": 100, "height": 100, "layers": [] },
          "armature": { "bones": [ { "id": "spine", "name": "Spine", "length": 120 } ] }
        }
        """;

        var doc = JsonSerializer.Deserialize<Doc>(legacy, DocJson.Options)!;

        Assert.True(doc.HasArmature);
        var rig = Assert.Single(doc.Rigs);
        Assert.Equal("spine", Assert.Single(rig.Bones).Id);
        Assert.Equal(120, rig.Bones[0].Length, 6);
        // It gets an id and a name it never had, which is honest: it had one
        // rig and never needed to name it.
        Assert.NotEmpty(rig.Id);
        Assert.Equal("Skeleton", rig.Name);
    }

    [Fact]
    public void ADocumentThatHasRigsWritesThemOnceAndNotUnderTheOldKey()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armatures = [Rig("Knight", "knight-spine"), Rig("Dog", "dog-spine")];

        var json = DocJson.Serialize(doc);

        output.WriteLine(json[..Math.Min(400, json.Length)]);
        Assert.Contains("\"armatures\"", json);
        // The derived accessor is a property to System.Text.Json unless it is
        // told otherwise, and without the attribute every rig would be written
        // twice — the second time under the very key this retires.
        Assert.DoesNotContain("\"armature\":", json);
        Assert.DoesNotContain("\"legacyArmature\"", json);
        Assert.DoesNotContain("\"hasArmature\"", json);
        Assert.DoesNotContain("\"rigs\"", json);
    }

    [Fact]
    public void AnUnriggedDocumentStillWritesNoRigKeysAtAll()
    {
        var json = DocJson.Serialize(DocumentFactory.CreateDoc());

        Assert.DoesNotContain("\"armature", json);   // covers both keys
        Assert.DoesNotContain("\"poseTrack\"", json);
    }

    [Fact]
    public void ManyRigsRoundTripWithTheirNames()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armatures = [Rig("Knight", "knight-spine"), Rig("Dog", "dog-spine")];

        var back = DocJson.Deserialize(DocJson.Serialize(doc));

        Assert.Equal(2, back.Rigs.Count);
        Assert.Equal(["Knight", "Dog"], back.Rigs.Select(r => r.Name));
        Assert.Equal(doc.Armatures[0].Id, back.Rigs[0].Id);
    }

    [Fact]
    public void AssigningTheFirstRigLeavesTheOtherCharactersAlone()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armatures = [Rig("Knight", "knight-spine"), Rig("Dog", "dog-spine")];

        // "Give this document a skeleton" must not mean "delete the dog".
        doc.Armature = Rig("Goblin", "goblin-spine");

        Assert.Equal(2, doc.Rigs.Count);
        Assert.Equal("Goblin", doc.Rigs[0].Name);
        Assert.Equal("Dog", doc.Rigs[1].Name);
    }

    [Fact]
    public void ADocumentWithNoRigsReportsNoneRatherThanAnEmptyList()
    {
        var doc = DocumentFactory.CreateDoc();

        Assert.Null(doc.Armature);
        Assert.False(doc.HasArmature);
        Assert.Empty(doc.Rigs);

        doc.Armature = Rig("Knight", "knight-spine");
        Assert.True(doc.HasArmature);
        doc.Armature = null;
        // Absent again, not an empty list sitting in the file.
        Assert.Null(doc.Armatures);
    }

    [Fact]
    public void RigOfBoneFindsTheCharacterAPoseKeyIsTalkingAbout()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armatures = [Rig("Knight", "knight-spine"), Rig("Dog", "dog-spine")];

        Assert.Equal("Dog", doc.RigOfBone("dog-spine")?.Name);
        Assert.Equal("Knight", doc.RigOfBone("knight-spine")?.Name);
        Assert.Null(doc.RigOfBone("nobody"));
        Assert.Null(doc.RigOfBone(null));
    }

    [Fact]
    public void CloningADocumentCopiesEveryRigAndSharesNoBones()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armatures = [Rig("Knight", "knight-spine"), Rig("Dog", "dog-spine")];

        var copy = doc.Clone();
        copy.Rigs[1].Bones[0].Length = 999;

        Assert.Equal(2, copy.Rigs.Count);
        Assert.Equal(100, doc.Rigs[1].Bones[0].Length, 6);
    }
}
