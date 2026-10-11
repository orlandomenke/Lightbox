using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// Puppet pins in the record (Q239): handles beside the bones, absent unless
/// used, posed through the keys the bones already have — and carried through
/// every operation that copies, rescales or re-fits a rig, because a pin left
/// behind by one of those is a puppet that silently stops lining up with its
/// drawing.
/// </summary>
public class PinRecordTests(ITestOutputHelper output)
{
    private sealed class NoPixels : IPixelResampler
    {
        public string? Resample(string pngBase64, double scaleX, double scaleY) => pngBase64;
    }

    /// <summary>An arm of two bones with a wrist pin, a starch pin and a pin riding the forearm.</summary>
    private static Armature Rig() => new()
    {
        Bones =
        [
            new Bone { Id = "upper", X = 100, Y = 100, Length = 100 },
            new Bone { Id = "fore", ParentId = "upper", Connected = true, Length = 100 },
        ],
        Pins =
        [
            new Pin { Id = "pin-wrist", Name = "Wrist", X = 300, Y = 100 },
            new Pin { Id = "pin-starch", Name = "Elbow", X = 200, Y = 100, Kind = PinKind.Starch, Extent = 40 },
            new Pin { Id = "pin-cuff", Name = "Cuff", X = 250, Y = 110, ParentId = "fore" },
        ],
    };

    private static Doc DocWith(Armature rig) => new() { Armature = rig };

    // ---- absent unless used ------------------------------------------------------

    [Fact]
    public void APinlessRigWritesNoPinsKey()
    {
        var rig = Rig();
        rig.Pins = null;

        var json = DocJson.Serialize(DocWith(rig));

        Assert.Contains("\"bones\"", json);
        Assert.DoesNotContain("\"pins\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"pinCell\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnOrdinaryPinWritesItsIdItsNameAndItsPlaceAndNothingElse()
    {
        var rig = new Armature { Pins = [new Pin { Id = "pin-a", Name = "Wrist", X = 12.5, Y = -3 }] };

        var json = DocJson.Serialize(DocWith(rig));
        output.WriteLine(json[json.IndexOf("\"pins\"", StringComparison.Ordinal)..].Split(']')[0]);

        Assert.Contains("\"pins\"", json);
        foreach (var key in new[] { "kind", "extent", "parentId", "effectiveKind", "pinCell", "unknown" })
            Assert.DoesNotContain($"\"{key}\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AStarchPinWritesItsKindAndItsReach()
    {
        var json = DocJson.Serialize(DocWith(Rig()));
        Assert.Contains("\"kind\": \"starch\"", json);
        Assert.Contains("\"extent\": 40", json);
        Assert.Contains("\"parentId\": \"fore\"", json);
        Assert.DoesNotContain("\"effectiveKind\"", json, StringComparison.OrdinalIgnoreCase);
    }

    // ---- nothing lost --------------------------------------------------------------

    [Fact]
    public void PinsRoundTripThroughAFile()
    {
        var rig = Rig();
        rig.PinCell = 16;

        var json = DocJson.Serialize(DocWith(rig));
        var loaded = DocJson.Deserialize(json);
        var back = loaded.Armature!;

        Assert.Equal(16, back.PinCell);
        Assert.Equal(3, back.Pins!.Count);
        var wrist = back.PinById("pin-wrist")!;
        Assert.Equal(("Wrist", 300.0, 100.0), (wrist.Name, wrist.X, wrist.Y));
        Assert.Null(wrist.Kind);
        Assert.Null(wrist.Extent);
        Assert.Null(wrist.ParentId);
        Assert.Equal(PinKind.Position, wrist.EffectiveKind);
        var starch = back.PinById("pin-starch")!;
        Assert.Equal(PinKind.Starch, starch.Kind);
        Assert.Equal(40, starch.Extent);
        Assert.Equal("fore", back.PinById("pin-cuff")!.ParentId);
        // And a second trip writes the same bytes: nothing is added on load.
        Assert.Equal(json, DocJson.Serialize(loaded));
    }

    [Fact]
    public void ARigOfPinsAndNoBonesSurvivesASaveAndALoad()
    {
        // Three pins and a drag, no skeleton — the whole point of pins. A rig
        // like that must not read as "no rig" to the file.
        var rig = new Armature { Name = "Blob", Pins = [new Pin { Id = "p1", X = 1, Y = 2 }, new Pin { Id = "p2", X = 3, Y = 4 }] };

        var back = DocJson.Deserialize(DocJson.Serialize(DocWith(rig)));

        Assert.Equal("Blob", back.Armature!.Name);
        Assert.Equal(2, back.Armature.Pins!.Count);
        Assert.Empty(back.Armature.Bones);
    }

    [Fact]
    public void WhatANewerBuildWroteOnAPinIsKeptThroughASaveHere()
    {
        var json = DocJson.Serialize(DocWith(Rig())).Replace(
            "\"name\": \"Wrist\"", "\"name\": \"Wrist\", \"bendLimitDeg\": 35");
        Assert.Contains("bendLimitDeg", json);

        var again = DocJson.Serialize(DocJson.Deserialize(json));

        Assert.Contains("\"bendLimitDeg\": 35", again);
    }

    [Fact]
    public void CloningARigCopiesItsPinsRatherThanSharingThem()
    {
        var rig = Rig();
        var copy = rig.Clone();

        Assert.NotSame(rig.Pins, copy.Pins);
        copy.Pins![0].X = 999;
        copy.Pins.RemoveAt(1);
        Assert.Equal(300, rig.Pins![0].X);
        Assert.Equal(3, rig.Pins.Count);

        // And through the document's own clone, which undo snapshots take.
        var doc = DocWith(Rig());
        var snapshot = doc.Clone();
        snapshot.Armature!.Pins![0].Y = -1;
        Assert.Equal(100, doc.Armature!.Pins![0].Y);
    }

    // ---- posed through the keys the bones have ---------------------------------------

    [Fact]
    public void APinKeyIsAnOrdinaryPoseKey()
    {
        var doc = DocWith(Rig());
        doc.Scene.PoseTrack = new PoseTrack
        {
            Keys =
            [
                new PoseKey { Frame = 0, Ease = Easing.Linear },
                new PoseKey { Frame = 10, Bones = { ["pin-wrist"] = new BonePose { X = 40, Y = -20 } } },
            ],
        };

        var back = DocJson.Deserialize(DocJson.Serialize(doc));
        var halfway = ArmatureOps.PoseAt(back.Scene.PoseTrack, 5);
        var targets = PinOps.TargetsAt(back.Armature!, halfway);

        // The track tweens a pin as it tweens a bone, with no pin code in it.
        Assert.Equal(320, targets[0].X, 9);
        Assert.Equal(90, targets[0].Y, 9);
        // The starch pin was not keyed and is where it was dropped.
        Assert.Equal((200.0, 100.0), targets[1]);
    }

    [Fact]
    public void AnUnposedPuppetIsAtRestToTheLastBit()
    {
        var rig = Rig();
        foreach (var pose in new IReadOnlyDictionary<string, BonePose>?[] { null, new Dictionary<string, BonePose>() })
        {
            var targets = PinOps.TargetsAt(rig, pose);
            Assert.Equal(rig.Pins!.Count, targets.Length);
            for (var i = 0; i < targets.Length; i++)
            {
                Assert.Equal(BitConverter.DoubleToInt64Bits(rig.Pins[i].X), BitConverter.DoubleToInt64Bits(targets[i].X));
                Assert.Equal(BitConverter.DoubleToInt64Bits(rig.Pins[i].Y), BitConverter.DoubleToInt64Bits(targets[i].Y));
            }
        }
    }

    [Fact]
    public void APinOnABoneGoesWhereTheBoneTakesIt()
    {
        // The cuff rides the forearm. Turn the forearm a quarter turn about
        // the elbow at (200, 100) and the cuff, 50 along and 10 off the bone,
        // ends up 50 down and 10 back — while the wrist pin, on no bone, stays.
        var rig = Rig();
        var pose = new Dictionary<string, BonePose> { ["fore"] = new() { RotationDeg = 90 } };

        var targets = PinOps.TargetsAt(rig, pose);

        Assert.Equal(190, targets[2].X, 9);
        Assert.Equal(150, targets[2].Y, 9);
        Assert.Equal((300.0, 100.0), targets[0]);

        // Its own key is in the bone's frame, like a child bone's: ten along
        // the forearm is ten further down once the forearm points down.
        pose["pin-cuff"] = new BonePose { X = 10 };
        var nudged = PinOps.TargetsAt(rig, pose);
        Assert.Equal(190, nudged[2].X, 9);
        Assert.Equal(160, nudged[2].Y, 9);
    }

    [Fact]
    public void APinWhoseBoneIsGoneIsAPinOnNoBone()
    {
        var rig = Rig();
        rig.Pins![2].ParentId = "deleted";
        var targets = PinOps.TargetsAt(rig, new Dictionary<string, BonePose> { ["fore"] = new() { RotationDeg = 90 } });
        Assert.Equal((250.0, 110.0), targets[2]);
    }

    [Fact]
    public void AKeyThatIsNotANumberLeavesThePinWhereItWas()
    {
        var rig = Rig();
        var pose = new Dictionary<string, BonePose> { ["pin-wrist"] = new() { X = double.NaN, Y = double.PositiveInfinity } };
        Assert.Equal((300.0, 100.0), PinOps.TargetsAt(rig, pose)[0]);
    }

    [Fact]
    public void TheSolveIsHandedThePinsInTheRecordsOrderWithTheirKinds()
    {
        var handles = PinOps.HandlesOf(Rig());

        Assert.Equal(3, handles.Length);
        Assert.Equal(new PinHandle(300, 100), handles[0]);
        Assert.Equal(new PinHandle(200, 100, PinKind.Starch, 40), handles[1]);
        Assert.Equal(new PinHandle(250, 110), handles[2]);
        Assert.Empty(PinOps.HandlesOf(new Armature()));
        Assert.Empty(PinOps.TargetsAt(new Armature(), null));
    }

    [Fact]
    public void TheRecordDrivesTheSolveEndToEnd()
    {
        // The seam between this file and the geometry: pins read off the
        // record, targets read off a pose, and the mesh moves.
        var rig = new Armature
        {
            Pins = [new Pin { Id = "a", X = 20, Y = 30 }, new Pin { Id = "b", X = 380, Y = 30 }],
        };
        var mesh = PinMesh.Build([PinShapes.Bar()], cell: 20);
        var solve = PinSolve.Prepare(mesh, PinOps.HandlesOf(rig));

        var rest = solve.Solve(PinOps.TargetsAt(rig, null));
        var posed = solve.Solve(PinOps.TargetsAt(rig, new Dictionary<string, BonePose> { ["b"] = new() { Y = 60 } }));

        Assert.Equal(mesh.Vertices, rest);
        var (x, y) = PinSolve.MapPoint(mesh, posed, 380, 30);
        Assert.Equal(380, x, 6);
        Assert.Equal(90, y, 6);
    }

    // ---- carried by everything that moves a rig ---------------------------------------

    [Fact]
    public void ResizingTheImageMovesThePinsWithTheDrawing()
    {
        var doc = DocWith(Rig());
        doc.Armature!.PinCell = 24;
        var (w, h) = (doc.Scene.Width, doc.Scene.Height);

        Assert.True(ImageResize.Apply(doc, w * 2, h * 2, new NoPixels()));

        var wrist = doc.Armature.PinById("pin-wrist")!;
        Assert.Equal((600.0, 200.0), (wrist.X, wrist.Y));
        // A starch pin's reach and the mesh's spacing are distances on the
        // drawing, so they grow with it: the same pin stiffens the same patch.
        Assert.Equal(80, doc.Armature.PinById("pin-starch")!.Extent!.Value, 9);
        Assert.Equal(48, doc.Armature.PinCell!.Value, 9);
    }

    [Fact]
    public void ResizingLeavesAPinlessRigWithoutAPinKey()
    {
        var doc = DocWith(Rig());
        doc.Armature!.Pins = null;
        ImageResize.Apply(doc, doc.Scene.Width * 2, doc.Scene.Height * 2, new NoPixels());
        Assert.Null(doc.Armature.Pins);
        Assert.Null(doc.Armature.PinCell);
    }

    [Fact]
    public void FittingARigToAnotherDrawingTakesItsPinsAlong()
    {
        var rig = Rig();
        rig.PinCell = 24;

        ArmatureFit.Scale(rig, 0.5);
        Assert.Equal((150.0, 50.0), (rig.Pins![0].X, rig.Pins[0].Y));
        Assert.Equal(20, rig.Pins[1].Extent!.Value, 9);
        Assert.Equal(12, rig.PinCell!.Value, 9);

        // Every pin moves, parented or not: a pin's place is on the paper,
        // where a child bone's is in its parent.
        ArmatureFit.MoveBy(rig, 10, -5);
        Assert.Equal((160.0, 45.0), (rig.Pins[0].X, rig.Pins[0].Y));
        Assert.Equal((135.0, 50.0), (rig.Pins[2].X, rig.Pins[2].Y));
    }
}
