using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Serialization;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests.Inbetween;

/// <summary>
/// The pose guide (Q274): the joints of each part of the subject in both keys,
/// kept on key A for the gap, and the pose part of the way between them, each
/// bone swung on its arc, with every part's volume measured from the keys' ink.
/// A guide to draw over, never a drawing.
/// </summary>
public class PoseGuideTests(ITestOutputHelper output)
{
    /// <summary>One leg: hip, knee, ankle. Straight down in key A, the shin swung forward in key B.</summary>
    private static PoseGuide Leg() => new()
    {
        Parts =
        [
            new PosePart
            {
                Name = "leg",
                Joints =
                [
                    new PoseJoint { Name = "hip", A = new(100, 100), B = new(100, 100) },
                    new PoseJoint { Name = "knee", A = new(100, 200), B = new(100, 200) },
                    new PoseJoint { Name = "ankle", A = new(100, 300), B = new(200, 200) },
                ],
            },
        ],
    };

    [Fact]
    public void ADocumentWithoutAPoseWritesNoKey()
    {
        var doc = DocumentFactory.CreateDoc(64, 48, 12);

        Assert.DoesNotContain("\"pose\"", DocJson.Serialize(doc));
    }

    [Fact]
    public void APoseRoundTripsAndACloneIsItsOwn()
    {
        var doc = DocumentFactory.CreateDoc(64, 48, 12);
        var frame = doc.Scene.Layers[^1].Cels[0].Frame!;
        frame.Pose = Leg();

        var back = DocJson.Deserialize(DocJson.Serialize(doc)).Scene.Layers[^1].Cels[0].Frame!.Pose!;
        Assert.Equal("ankle", back.Parts[0].Joints[2].Name);
        Assert.Equal(new PosePoint(200, 200), back.Parts[0].Joints[2].B);

        var copy = frame.Clone();
        copy.Pose!.Parts[0].Joints[0].A = new PosePoint(0, 0);
        Assert.Equal(new PosePoint(100, 100), frame.Pose.Parts[0].Joints[0].A);
    }

    [Fact]
    public void ResizingTheImageScalesThePose()
    {
        var doc = DocumentFactory.CreateDoc(400, 400, 12);
        doc.Scene.Layers[^1].Cels[0].Frame!.Pose = Leg();

        ImageResize.Apply(doc, 800, 200, new NoPixels());

        var ankle = doc.Scene.Layers[^1].Cels[0].Frame!.Pose!.Parts[0].Joints[2];
        Assert.Equal(new PosePoint(400, 100), ankle.B);   // x doubled, y halved
    }

    [Fact]
    public void AtTheKeysThePoseIsTheKeysPose()
    {
        var leg = Leg();

        var start = PoseGuides.At(leg, 0)[0].Joints;
        var end = PoseGuides.At(leg, 1)[0].Joints;

        Assert.True(GeometryOps.Dist(start[2].At, new StrokePoint(100, 300, 0)) < 1e-6);
        Assert.True(GeometryOps.Dist(end[2].At, new StrokePoint(200, 200, 0)) < 1e-6);
    }

    [Fact]
    public void EachBoneSwingsOnItsArcAndKeepsItsLength()
    {
        var half = PoseGuides.At(Leg(), 0.5)[0].Joints;

        // The knee does not move; the shin swings a quarter turn about it, halfway.
        var shin = GeometryOps.Dist(half[1].At, half[2].At);
        var expected = new StrokePoint(100 + 100 * Math.Sin(Math.PI / 4), 200 + 100 * Math.Cos(Math.PI / 4), 0);
        output.WriteLine($"ankle at ({half[2].At.X:0.0}, {half[2].At.Y:0.0}), shin {shin:0.0}");
        Assert.InRange(shin, 99.9, 100.1);
        Assert.True(GeometryOps.Dist(half[2].At, expected) < 0.5);
    }

    [Fact]
    public void AVolumeIsMeasuredFromTheKeysInkAndBlended()
    {
        // The shin drawn as two lines either side of the bone: 30 wide in key
        // A, 50 wide in key B (it is nearer the camera), so 40 halfway.
        var leg = Leg();
        List<Stroke> keyA =
        [
            Line((85, 200), (85, 300)), Line((115, 200), (115, 300)),
        ];
        // In key B the shin lies horizontal, from the knee (100, 200) to the ankle (200, 200).
        List<Stroke> keyB =
        [
            Line((100, 175), (200, 175)), Line((100, 225), (200, 225)),
        ];

        var bones = PoseGuides.Volumes(leg, keyA, keyB, 0.5)[0];

        var shin = bones[1];
        output.WriteLine($"shin width {shin.Width:0.0} (thigh {bones[0].Width:0.0})");
        Assert.InRange(shin.Width, 37, 43);
    }

    [Fact]
    public void ABoneWithNoInkInOneKeyTakesItsWidthFromTheOther()
    {
        var leg = Leg();
        List<Stroke> keyA = [Line((85, 200), (85, 300)), Line((115, 200), (115, 300))];

        var shin = PoseGuides.Volumes(leg, keyA, [], 0.5)[0][1];

        Assert.InRange(shin.Width, 27, 33);
    }

    /// <summary>
    /// A file is not trusted (Q274 review): a pose with holes in it, a number
    /// too big to write back, or more joints than any subject has, opens as
    /// the part of it that is usable — and the document still clones, edits
    /// and saves, which a null list or an infinity would have stopped.
    /// </summary>
    [Fact]
    public void AHostilePoseInAFileOpensAsWhatIsUsableAndStillSaves()
    {
        var doc = DocumentFactory.CreateDoc(64, 48, 12);
        doc.Scene.Layers[^1].Cels[0].Frame!.Pose = Leg();
        var json = DocJson.Serialize(doc);
        // The pose's whole object, from its key to its closing brace.
        var start = json.IndexOf("\"pose\"", StringComparison.Ordinal);
        Assert.True(start > 0);
        var end = json.IndexOf('{', start);
        for (var depth = 0; ; end++)
        {
            if (json[end] == '{') depth++;
            else if (json[end] == '}' && --depth == 0) { end++; break; }
        }
        var many = string.Join(",", Enumerable.Range(0, 500).Select(i => $"{{\"name\":\"j{i}\",\"a\":{{\"x\":{i},\"y\":0}},\"b\":{{\"x\":{i},\"y\":1}}}}"));
        var hostile = "\"pose\": {\"parts\": [null, {\"name\": \"tail\", \"joints\": null}, {\"name\": \"leg\", \"joints\": ["
                      + "null, {\"name\": \"hip\", \"a\": {\"x\": 1e400, \"y\": 0}, \"b\": {\"x\": 0, \"y\": 0}},"
                      + "{\"name\": \"knee\", \"a\": {\"x\": 10, \"y\": 20}, \"b\": {\"x\": 10, \"y\": 20}},"
                      + "{\"name\": \"ankle\", \"a\": {\"x\": 10, \"y\": 40}, \"b\": {\"x\": 30, \"y\": 30}}]},"
                      + $"{{\"name\": \"rope\", \"joints\": [{many}]}}]}}";
        json = json[..start] + hostile + json[end..];

        var opened = DocJson.Deserialize(json);
        var frame = opened.Scene.Layers[^1].Cels[0].Frame!;
        var pose = frame.Pose!;

        Assert.All(pose.Parts.SelectMany(p => p.Joints), j => Assert.True(double.IsFinite(j.A.X) && double.IsFinite(j.A.Y)));
        Assert.Contains(pose.Parts, p => p.Joints.Select(j => j.Name).SequenceEqual(["knee", "ankle"]));
        Assert.All(pose.Parts, p => Assert.InRange(p.Joints.Count, 2, PoseGuide.MaxJoints));
        _ = frame.Clone();
        Assert.Contains("\"knee\"", DocJson.Serialize(opened));
    }

    /// <summary>
    /// A short bone in a wide part — a toe, a hand — still has a volume (Q274
    /// review): the reach is never less than a part's plausible half-width, so
    /// a foot drawn 40 wide around a 10-long toe is measured, not lost.
    /// </summary>
    [Fact]
    public void AShortBoneInAWidePartStillHasAVolume()
    {
        var foot = new PoseGuide { Parts = [new PosePart { Name = "foot", Joints =
        [
            new PoseJoint { Name = "ball", A = new(100, 100), B = new(100, 100) },
            new PoseJoint { Name = "toe", A = new(110, 100), B = new(110, 100) },
        ] }] };
        List<Stroke> key = [Line((90, 80), (120, 80)), Line((90, 120), (120, 120))];

        var toe = PoseGuides.Volumes(foot, key, key, 0.5)[0][0];

        output.WriteLine($"toe width {toe.Width:0.0}");
        Assert.InRange(toe.Width, 36, 44);
    }

    /// <summary>
    /// The keys' ink is gathered once and reused (Q274 review): the guide is
    /// asked for on every playhead step and every move of a dragged joint, and
    /// measuring a key's every stroke each time was most of its cost.
    /// </summary>
    [Fact]
    public void TheKeysInkIsGatheredOnceAndMeasuresTheSame()
    {
        var leg = Leg();
        List<Stroke> keyA = [Line((85, 200), (85, 300)), Line((115, 200), (115, 300))];
        List<Stroke> keyB = [Line((100, 185), (200, 185)), Line((100, 215), (200, 215))];

        var inkA = PoseGuides.Ink(keyA);
        var inkB = PoseGuides.Ink(keyB);
        var fromInk = PoseGuides.Volumes(leg, inkA, inkB, 0.5)[0][1];
        var fromStrokes = PoseGuides.Volumes(leg, keyA, keyB, 0.5)[0][1];

        Assert.Equal(fromStrokes.Width, fromInk.Width, 6);
        Assert.InRange(fromInk.Width, 27, 33);
    }

    private static Stroke Line((double X, double Y) from, (double X, double Y) to) => new()
    {
        Points = [new StrokePoint(from.X, from.Y, 0.8), new StrokePoint(to.X, to.Y, 0.8)],
        Brush = new BrushSettings { Size = 4 },
    };

    private sealed class NoPixels : IPixelResampler
    {
        public string? Resample(string pngBase64, double scaleX, double scaleY) => pngBase64;
    }
}
