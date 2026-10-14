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
