using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// The whole of what binds a painting to its rig, through the file: the
/// armature, the layer's binding, the pose track and per-stroke weights all
/// come back from a save exactly as written, and the rig index still sees the
/// layer binding afterwards. Written against the owner's report of a binding
/// not surviving open — this pins the record's half of that promise, so if
/// the report reproduces, the fault is upstream of the file format.
/// </summary>
public class RigBindingRoundTripTests
{
    [Fact]
    public void TheRigItsBindingsAndItsPosesSurviveSaveAndLoad()
    {
        var doc = DocumentFactory.CreateDoc(paperColor: Scene.DefaultBackgroundColor);
        doc.Armature = new Armature { Bones = [new Bone { Id = "b1", Length = 100 }] };
        doc.Scene.PoseTrack = new PoseTrack
        {
            Keys =
            [
                new PoseKey { Frame = 0, Bones = { ["b1"] = new BonePose() } },
                new PoseKey { Frame = 5, Bones = { ["b1"] = new BonePose { RotationDeg = 45 } } },
            ],
        };
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        layer.BoneId = "";  // the whole skeleton

        var stroke = new Stroke { Points = [new StrokePoint(10, 0, 1), new StrokePoint(90, 0, 1)] };
        stroke.Weights = [new BoneBinding { BoneId = "b1", PointWeights = [1.0, 0.5] }];
        layer.Cels[0].Frame!.Strokes.Add(stroke);

        var path = Path.Combine(Path.GetTempPath(), $"rig-roundtrip-{Guid.NewGuid():N}.lightbox.json");
        try
        {
            DocJson.Save(doc, path);
            var back = DocJson.Load(path);

            Assert.NotNull(back.Armature);
            Assert.Equal("b1", back.Armature!.Bones.Single().Id);
            Assert.Equal(2, back.Scene.PoseTrack!.Keys.Count);

            var backLayer = back.Scene.Layers.First(l => !l.IsBackground);
            Assert.Equal("", backLayer.BoneId);

            var backStroke = backLayer.Cels[0].Frame!.Strokes.Single();
            var binding = Assert.Single(backStroke.Weights!);
            Assert.Equal("b1", binding.BoneId);
            Assert.Equal([1.0, 0.5], binding.PointWeights!);

            // And the index every render gate reads still counts the layer as
            // rigged on the loaded document.
            Assert.False(RigIndex.For(back).IsEmpty);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
