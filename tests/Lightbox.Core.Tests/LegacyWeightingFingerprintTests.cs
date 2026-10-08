using System.Security.Cryptography;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// A layer bound to the whole skeleton before joint weighting (Q217) poses
/// exactly as it did — pinned, not argued.
/// </summary>
/// <remarks>
/// <para>
/// The owner chose that nothing already rigged poses differently, which
/// means the inverse-square weighting is kept forever beside the new one.
/// The risk is the next edit to <c>Skinning.AutoWeights</c> — hoisting a
/// table, adjusting the renormalisation — changing old rigs while every other
/// test stays green (sensitivity on Q217, W8).
/// </para>
/// <para>
/// The fingerprints were <b>recorded on <c>fix/canvas/bones-straight-lines</c></b>,
/// the last code before joint weighting existed, from this same scene: a
/// three-bone chain posed at a bend, and a freehand line, a line-tool line, a
/// sparse polyline and a fill rectangle on a layer that follows the whole
/// skeleton with no marker. They hash the IEEE bits of every posed point,
/// live and baked, so any change at all to how such a layer poses fails here.
/// </para>
/// </remarks>
public class LegacyWeightingFingerprintTests(ITestOutputHelper output)
{
    private const string RecordedRender = "BAF2E9BB2FF52ACDD61A1300C672E10AF29023F1EEC6AE0B822ECC6F811B8B5F";
    private const string RecordedBake = "BAF2E9BB2FF52ACDD61A1300C672E10AF29023F1EEC6AE0B822ECC6F811B8B5F";

    private static (Doc Doc, Frame Frame) Scene()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armature = new Armature
        {
            Bones =
            [
                new Bone { Id = "upper", X = 40, Y = 120, Length = 100 },
                new Bone { Id = "fore", ParentId = "upper", Connected = true, Length = 90 },
                new Bone { Id = "hand", ParentId = "fore", Connected = true, Length = 40 },
            ],
        };
        doc.Scene.PoseTrack = new PoseTrack
        {
            Keys =
            [
                new PoseKey
                {
                    Frame = 0,
                    Bones =
                    {
                        ["upper"] = new BonePose { RotationDeg = 15 },
                        ["fore"] = new BonePose { RotationDeg = 70 },
                        ["hand"] = new BonePose { RotationDeg = -35 },
                    },
                },
            ],
        };
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        layer.BoneId = ""; // the whole skeleton, bound before Q217: no marker
        var frame = layer.Cels[0].Frame!;
        frame.Strokes.Add(new Stroke
        {
            Points = Enumerable.Range(0, 47).Select(i => new StrokePoint(40 + i * 5, 128 + Math.Sin(i * 0.3) * 4, 0.5 + i % 5 * 0.1)).ToList(),
        });
        frame.Strokes.Add(new Stroke { Points = [new StrokePoint(45, 110, 1), new StrokePoint(265, 110, 1)] });
        frame.Strokes.Add(new Stroke
        {
            Points = [new StrokePoint(60, 140, 1), new StrokePoint(150, 150, 1), new StrokePoint(170, 132, 1), new StrokePoint(250, 138, 1)],
        });
        frame.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Fill,
            Points = [new StrokePoint(110, 100, 1), new StrokePoint(180, 100, 1), new StrokePoint(180, 140, 1), new StrokePoint(110, 140, 1)],
        });
        return (doc, frame);
    }

    private static string Fingerprint(IEnumerable<Stroke> strokes)
    {
        using var sha = SHA256.Create();
        var bytes = new List<byte>();
        foreach (var stroke in strokes)
        {
            foreach (var p in stroke.Points)
            {
                bytes.AddRange(BitConverter.GetBytes(BitConverter.DoubleToInt64Bits(p.X)));
                bytes.AddRange(BitConverter.GetBytes(BitConverter.DoubleToInt64Bits(p.Y)));
            }
            bytes.Add(0xFF);
        }
        return Convert.ToHexString(sha.ComputeHash(bytes.ToArray()));
    }

    [Fact]
    public void ALayerBoundBeforeJointWeightingPosesAsItDid()
    {
        var (doc, frame) = Scene();
        var rig = RigIndex.For(doc);
        var render = Fingerprint(Skinning.PoseFrameForRender(doc, frame, 0, rig).Strokes);

        var baked = frame.Clone();
        Skinning.BakeFrame(baked, doc.Armature!, ArmatureOps.EffectivePoseAt(doc.Armature!, doc.Scene.PoseTrack, 0), "");
        var bake = Fingerprint(baked.Strokes);

        output.WriteLine($"render {render}");
        output.WriteLine($"bake   {bake}");
        Assert.Equal(RecordedRender, render);
        Assert.Equal(RecordedBake, bake);
    }
}
