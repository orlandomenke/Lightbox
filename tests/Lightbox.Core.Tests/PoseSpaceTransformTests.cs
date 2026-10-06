using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// B381 and B382: the record keeps a rigged drawing at rest, the canvas shows
/// it posed, and anything the artist does to the posed picture has to be
/// written back through the pose — a transform of what they see, and a stroke
/// drawn where the pen was.
/// </summary>
public class PoseSpaceTransformTests(ITestOutputHelper output)
{
    /// <summary>One bone lying along +x from the origin.</summary>
    private static Armature OneBone() => new()
    {
        Bones = [new Bone { Id = "b", X = 0, Y = 0, Length = 100, RotationDeg = 0 }],
    };

    /// <summary>Two root bones end to end along +x: a at 0..100, b at 100..200.</summary>
    private static Armature TwoBones() => new()
    {
        Bones =
        [
            new Bone { Id = "a", X = 0, Y = 0, Length = 100, RotationDeg = 0 },
            new Bone { Id = "b", X = 100, Y = 0, Length = 100, RotationDeg = 0 },
        ],
    };

    private static Dictionary<string, BonePose> Rotate(string bone, double deg) =>
        new() { [bone] = new BonePose { RotationDeg = deg } };

    private static Stroke Line(params (double X, double Y)[] points) =>
        new() { Points = [.. points.Select(p => new StrokePoint(p.X, p.Y, 1))] };

    private static (double X, double Y) Shift(double dx, double dy, double x, double y) => (x + dx, y + dy);

    private static List<StrokePoint> Posed(
        Stroke stroke, Armature armature, IReadOnlyDictionary<string, BonePose> pose) =>
        Skinning.PoseControlPoints(stroke, armature, pose);

    [Fact]
    public void TranslatingInPoseSpaceTranslatesThePosedImage()
    {
        var arm = OneBone();
        var pose = Rotate("b", 90);
        var stroke = Line((10, 0), (50, 0), (90, 0));
        Skinning.AssignAll(stroke, "b");
        var before = Posed(stroke, arm, pose);

        var moved = Skinning.TransformInPose(
            stroke, arm, pose, fallback: null, correction: null, (x, y) => Shift(30, 10, x, y));

        Assert.True(moved);
        var after = Posed(stroke, arm, pose);
        for (var i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i].X + 30, after[i].X, 9);
            Assert.Equal(before[i].Y + 10, after[i].Y, 9);
        }
        // And the record moved by the translation turned back through the
        // bone: a 90° bone carries a posed (+30,+10) as a rest (+10,−30).
        Assert.Equal(20, stroke.Points[0].X, 9);
        Assert.Equal(-30, stroke.Points[0].Y, 9);
        // Pressure and the rest of the point ride along untouched.
        Assert.Equal(1, stroke.Points[0].Pressure, 12);
    }

    /// <summary>
    /// The kink the owner saw. A line weighted across two bones is straight
    /// at the pose it was drawn for; moving its rest points by the gizmo's
    /// matrix and re-posing bends it, because each point's bone delta is
    /// applied after the move. Moving the posed image and writing back keeps
    /// it exactly where a translation would put it.
    /// </summary>
    [Fact]
    public void AStraightLineWeightedAcrossTwoBonesStaysStraightUnderATranslation()
    {
        var arm = TwoBones();
        var pose = Rotate("b", 45);
        var stroke = Line(Enumerable.Range(0, 9).Select(i => (i * 25.0, 0.0)).ToArray());
        var ramp = Enumerable.Range(0, 9).Select(i => i / 8.0).ToList();
        stroke.Weights =
        [
            new BoneBinding { BoneId = "a", PointWeights = ramp.Select(w => 1 - w).ToList() },
            new BoneBinding { BoneId = "b", PointWeights = ramp },
        ];
        var before = Posed(stroke, arm, pose);

        // The old way, on a copy: the rest points through the map, then posed.
        var naive = stroke.Clone(newId: false);
        TransformOps.TransformStroke(naive, (x, y) => Shift(20, 0, x, y));
        var naivePosed = Posed(naive, arm, pose);

        Skinning.TransformInPose(stroke, arm, pose, null, null, (x, y) => Shift(20, 0, x, y));
        var after = Posed(stroke, arm, pose);

        double worstNaive = 0, worstWritten = 0;
        for (var i = 0; i < before.Count; i++)
        {
            worstNaive = Math.Max(worstNaive, Math.Abs(naivePosed[i].X - (before[i].X + 20)) + Math.Abs(naivePosed[i].Y - before[i].Y));
            worstWritten = Math.Max(worstWritten, Math.Abs(after[i].X - (before[i].X + 20)) + Math.Abs(after[i].Y - before[i].Y));
        }
        output.WriteLine($"worst departure from a true translation: naive {worstNaive:F3} px, pose-space {worstWritten:E2} px");
        // The naive route visibly bends the line — several pixels at a 45° joint.
        Assert.True(worstNaive > 2, $"the control did not reproduce the kink ({worstNaive:F3} px)");
        Assert.True(worstWritten < 1e-9, $"the pose-space write-back drifted by {worstWritten:E2} px");
    }

    [Fact]
    public void AnUnboundStrokeIsLeftToThePlainMap()
    {
        var stroke = Line((10, 0), (90, 0));
        var before = stroke.Points.ToList();

        var moved = Skinning.TransformInPose(
            stroke, OneBone(), Rotate("b", 90), fallback: null, correction: null, (x, y) => Shift(5, 5, x, y));

        Assert.False(moved);
        Assert.Equal(before, stroke.Points);
    }

    [Fact]
    public void TheMoverPosesTheBoundStrokesAndMapsTheRestPlainly()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armature = OneBone();
        doc.Scene.PoseTrack = new PoseTrack
        {
            Keys = [new PoseKey { Frame = 0, Bones = { ["b"] = new BonePose { RotationDeg = 90 } } }],
        };
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        var frame = layer.Cels[0].Frame!;
        var bound = Line((10, 0), (90, 0));
        Skinning.AssignAll(bound, "b");
        var loose = Line((300, 300), (320, 300));
        frame.Strokes.Add(bound);
        frame.Strokes.Add(loose);
        var rig = RigIndex.For(doc);
        var pose = ArmatureOps.EffectivePoseAt(doc.Armature, doc.Scene.PoseTrack, 0);
        var posedBefore = Posed(bound, doc.Armature, pose);

        var mover = Skinning.PoseSpaceMover(doc, frame, 0, rig, (x, y) => Shift(0, 40, x, y));
        Assert.NotNull(mover);
        TransformOps.TransformFrame(frame, (x, y) => Shift(0, 40, x, y), mover: mover);

        var posedAfter = Posed(bound, doc.Armature, pose);
        Assert.Equal(posedBefore[0].Y + 40, posedAfter[0].Y, 9);
        Assert.Equal(posedBefore[0].X, posedAfter[0].X, 9);
        // The loose line took the map as it is.
        Assert.Equal(340, loose.Points[0].Y, 12);
        Assert.Equal(300, loose.Points[0].X, 12);
    }

    [Fact]
    public void AFrameTheRigDoesNotPoseGetsNoMover()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armature = OneBone();
        var frame = doc.Scene.Layers.First(l => !l.IsBackground).Cels[0].Frame!;
        frame.Strokes.Add(Line((10, 0), (90, 0)));

        Assert.Null(Skinning.PoseSpaceMover(doc, frame, 0, RigIndex.For(doc), (x, y) => (x, y)));
    }

    /// <summary>
    /// B382. The pen drew on the posed picture; the record keeps rest geometry;
    /// the render of what was committed must land under where the pen was.
    /// </summary>
    [Fact]
    public void AStrokeDrawnOnAPosedLayerRendersWhereThePenWas()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armature = OneBone();
        doc.Scene.PoseTrack = new PoseTrack
        {
            Keys = [new PoseKey { Frame = 0, Bones = { ["b"] = new BonePose { RotationDeg = 90 } } }],
        };
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        layer.BoneId = "b";
        var frame = layer.Cels[0].Frame!;
        var rig = RigIndex.For(doc);
        Assert.True(rig.IsPosed(frame));

        var drawn = Line((200, 100), (260, 100));
        Skinning.UnposeDrawnStroke(drawn, doc, frame, 0, rig);
        frame.Strokes.Add(drawn);

        // The record is no longer where the pen was …
        Assert.NotEqual(200, drawn.Points[0].X, 6);
        // … the layer's binding is still read, never written …
        Assert.Null(drawn.Weights);
        // … and the render puts the mark back under the pen.
        var rendered = Skinning.PoseFrameForRender(doc, frame, 0, rig);
        var shown = rendered.Strokes.Single(s => s.Id == drawn.Id);
        Assert.Equal(200, shown.Points[0].X, 9);
        Assert.Equal(100, shown.Points[0].Y, 9);
        Assert.Equal(260, shown.Points[^1].X, 9);
        Assert.Equal(100, shown.Points[^1].Y, 9);
    }

    /// <summary>
    /// A layer bound to the whole skeleton weights a drawn stroke by where the
    /// bones stand, and keeps those weights — the render's own auto-bind reads
    /// rest geometry and would pick differently.
    /// </summary>
    [Fact]
    public void AStrokeDrawnOnAWholeSkeletonLayerKeepsTheWeightsItWasAimedWith()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armature = TwoBones();
        doc.Scene.PoseTrack = new PoseTrack
        {
            Keys = [new PoseKey { Frame = 0, Bones = { ["b"] = new BonePose { RotationDeg = 90 } } }],
        };
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        layer.BoneId = "";
        var frame = layer.Cels[0].Frame!;
        var rig = RigIndex.For(doc);

        // Drawn along the POSED b, which now runs down from (100,0) to (100,100).
        var drawn = Line((102, 20), (102, 80));
        Skinning.UnposeDrawnStroke(drawn, doc, frame, 0, rig);
        frame.Strokes.Add(drawn);

        Assert.NotNull(drawn.Weights);
        var onB = drawn.Weights!.Single(w => w.BoneId == "b");
        Assert.True(onB.WeightAt(0) > 0.9, $"the stroke aimed at the posed b weighs {onB.WeightAt(0):F3} on it");

        var shown = Skinning.PoseFrameForRender(doc, frame, 0, rig).Strokes.Single(s => s.Id == drawn.Id);
        Assert.Equal(102, shown.Points[0].X, 6);
        Assert.Equal(20, shown.Points[0].Y, 6);
        Assert.Equal(102, shown.Points[^1].X, 6);
        Assert.Equal(80, shown.Points[^1].Y, 6);
    }

    /// <summary>
    /// Where the inverse cannot be trusted the point stays put. Two bones
    /// folded to 180° at equal weight have no rest point that lands anywhere;
    /// at 179° one exists and a drag would be magnified a hundredfold into
    /// every other frame of the cycle. Both are refused; the points wholly on
    /// one bone in the same stroke still move.
    /// </summary>
    [Theory]
    [InlineData(180.0)]
    [InlineData(179.0)]
    public void APointAtAFoldedJointIsLeftWhereItWas(double fold)
    {
        var arm = TwoBones();
        var pose = Rotate("b", fold);
        var stroke = Line((50, 0), (100, 0));
        stroke.Weights =
        [
            new BoneBinding { BoneId = "a", PointWeights = [1.0, 0.5] },
            new BoneBinding { BoneId = "b", PointWeights = [0.0, 0.5] },
        ];

        Assert.True(Skinning.TransformInPose(stroke, arm, pose, null, null, (x, y) => Shift(20, 0, x, y)));

        // Wholly on a: a plain translation at rest, since a did not move.
        Assert.Equal(70, stroke.Points[0].X, 9);
        Assert.Equal(0, stroke.Points[0].Y, 9);
        // At the pinch: untouched.
        Assert.Equal(100, stroke.Points[1].X, 9);
        Assert.Equal(0, stroke.Points[1].Y, 9);
    }

    [Fact]
    public void ANonFiniteMapLeavesThePointAlone()
    {
        var stroke = Line((10, 0), (90, 0));
        Skinning.AssignAll(stroke, "b");
        var before = stroke.Points.ToList();

        Assert.True(Skinning.TransformInPose(
            stroke, OneBone(), Rotate("b", 90), null, null, (x, y) => (double.NaN, double.PositiveInfinity)));

        Assert.Equal(before, stroke.Points);
        Assert.All(stroke.Points, p => Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y)));
    }

    /// <summary>
    /// A whole-skeleton layer auto-binds a weightless stroke from its rest
    /// geometry, which the transform moves — so the weights the inverse went
    /// through are kept, or the next render would bind it again, differently,
    /// and land the line somewhere other than the preview put it.
    /// </summary>
    [Fact]
    public void AWholeSkeletonTransformKeepsTheWeightsItWasInvertedThrough()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armature = TwoBones();
        doc.Scene.PoseTrack = new PoseTrack
        {
            Keys = [new PoseKey { Frame = 0, Bones = { ["b"] = new BonePose { RotationDeg = 90 } } }],
        };
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        layer.BoneId = "";
        var frame = layer.Cels[0].Frame!;
        // Along b at rest, so the auto-bind leans on b and the pose swings it.
        var stroke = Line((150, 2), (190, 2));
        frame.Strokes.Add(stroke);
        var rig = RigIndex.For(doc);
        var before = Skinning.PoseFrameForRender(doc, frame, 0, rig).Strokes.Single().Points;

        var mover = Skinning.PoseSpaceMover(doc, frame, 0, rig, (x, y) => Shift(0, 40, x, y));
        Assert.NotNull(mover);
        TransformOps.TransformFrame(frame, (x, y) => Shift(0, 40, x, y), mover: mover);

        Assert.NotNull(stroke.Weights);
        var after = Skinning.PoseFrameForRender(doc, frame, 0, rig).Strokes.Single().Points;
        Assert.Equal(before[0].X, after[0].X, 6);
        Assert.Equal(before[0].Y + 40, after[0].Y, 6);
        Assert.Equal(before[^1].X, after[^1].X, 6);
        Assert.Equal(before[^1].Y + 40, after[^1].Y, 6);
    }

    [Fact]
    public void AnUnriggedLayerLeavesADrawnStrokeAlone()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armature = OneBone();
        var frame = doc.Scene.Layers.First(l => !l.IsBackground).Cels[0].Frame!;
        var drawn = Line((200, 100), (260, 100));
        var before = drawn.Points.ToList();

        Skinning.UnposeDrawnStroke(drawn, doc, frame, 0, RigIndex.For(doc));

        Assert.Equal(before, drawn.Points);
        Assert.Null(drawn.Weights);
    }
}
