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

    /// <summary>
    /// How far any point of <paramref name="curve"/> sits from the translated
    /// reference curve — measured to the reference's segments, not its
    /// vertices, so two samplings of the same curve read as zero.
    /// </summary>
    private static double Departure(
        IReadOnlyList<StrokePoint> curve, IReadOnlyList<StrokePoint> reference, double dx, double dy)
    {
        var shifted = reference.Select(q => q with { X = q.X + dx, Y = q.Y + dy }).ToList();
        var worst = 0.0;
        foreach (var p in curve)
        {
            var nearest = double.MaxValue;
            for (var i = 0; i + 1 < shifted.Count; i++)
            {
                var d = GeometryOps.DistToSegment(p, shifted[i], shifted[i + 1]);
                if (d < nearest) nearest = d;
            }
            worst = Math.Max(worst, nearest);
        }
        return worst;
    }

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
        // The rendered curves — densified, as the canvas draws them — rather
        // than the control points: the write-back inserts points on a sparse
        // stroke, so indices no longer correspond, and the curve between the
        // points is what the artist sees anyway.
        var before = Skinning.PoseStroke(stroke, arm, pose).Points;

        // The old way, on a copy: the rest points through the map, then posed.
        var naive = stroke.Clone(newId: false);
        TransformOps.TransformStroke(naive, (x, y) => Shift(20, 0, x, y));
        var naivePosed = Skinning.PoseStroke(naive, arm, pose).Points;

        Skinning.TransformInPose(stroke, arm, pose, null, null, (x, y) => Shift(20, 0, x, y));
        var after = Skinning.PoseStroke(stroke, arm, pose).Points;

        var worstNaive = Departure(naivePosed, before, 20, 0);
        var worstWritten = Departure(after, before, 20, 0);
        output.WriteLine($"worst departure from a true translation: naive {worstNaive:F3} px, pose-space {worstWritten:E2} px");
        // The naive route visibly bends the line — several pixels at a 45° joint.
        Assert.True(worstNaive > 2, $"the control did not reproduce the kink ({worstNaive:F3} px)");
        // Exact at every control point the write-back inserted, and within a
        // fraction of a pixel on the curve between them.
        Assert.True(worstWritten < 1.0, $"the pose-space write-back drifted by {worstWritten:F3} px");
    }

    /// <summary>
    /// The adversary's case: the inverse is exact at the control points, and
    /// a sparse line across a bent joint used to land well off at its middle,
    /// because the render blends weights by arc fraction between the points
    /// and the fractions moved with them. The write-back now densifies such
    /// a stroke first, so the rendered curve follows the translation all the
    /// way along. (Three points, not two: a two-point line is rendered as one
    /// straight segment between its posed ends and was exact already.)
    /// </summary>
    [Fact]
    public void ASparseLineAcrossABentJointFollowsTheTranslationBetweenItsPointsToo()
    {
        var arm = TwoBones();
        var pose = Rotate("b", 90);
        var stroke = Line((0, 0), (100, 0), (200, 0));
        stroke.Weights =
        [
            new BoneBinding { BoneId = "a", PointWeights = [1.0, 0.5, 0.0] },
            new BoneBinding { BoneId = "b", PointWeights = [0.0, 0.5, 1.0] },
        ];
        var before = Skinning.PoseStroke(stroke, arm, pose).Points;   // the rendered curve, dense

        // The control: the same write-back without the densification is what
        // the node-only inverse gives, and it bows between the nodes.
        var sparse = stroke.Clone(newId: false);
        var sparseCurveAfter = SparseWriteBack(sparse, arm, pose, 0, 30);

        Assert.True(Skinning.TransformInPose(stroke, arm, pose, null, null, (x, y) => Shift(0, 30, x, y)));
        var after = Skinning.PoseStroke(stroke, arm, pose).Points;

        var worstSparse = Departure(sparseCurveAfter, before, 0, 30);
        var worst = Departure(after, before, 0, 30);
        output.WriteLine($"record grew from 3 to {stroke.Points.Count} points; departure at the nodes only {worstSparse:F2} px, densified {worst:F3} px");
        Assert.True(stroke.Points.Count > 3, "the sparse stroke was not densified");
        Assert.True(worstSparse > 1.0, $"the control did not reproduce the bow ({worstSparse:F2} px)");
        Assert.True(worst < 1.0, $"the rendered curve departs from the translation by {worst:F2} px");
    }

    /// <summary>The node-only inverse, by hand: each control point posed, shifted, and inverted through its own weights.</summary>
    private static IReadOnlyList<StrokePoint> SparseWriteBack(
        Stroke stroke, Armature arm, IReadOnlyDictionary<string, BonePose> pose, double dx, double dy)
    {
        var posed = Skinning.PoseControlPoints(stroke, arm, pose);
        var target = posed.Select(p => p with { X = p.X + dx, Y = p.Y + dy }).ToList();
        var offsets = Skinning.RestOffsetsFor(stroke, arm, pose, target);
        for (var i = 0; i < stroke.Points.Count; i++)
        {
            stroke.Points[i] = stroke.Points[i] with
            {
                X = stroke.Points[i].X + offsets[i].X,
                Y = stroke.Points[i].Y + offsets[i].Y,
            };
        }
        return Skinning.PoseStroke(stroke, arm, pose).Points;
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
        // At the pinch: untouched. The end, not index 1: a two-point line whose
        // weights vary is densified for the write-back now (B404), as the
        // write-back chord always meant it to be — the pair used to slip
        // through Densify untouched.
        Assert.Equal(100, stroke.Points[^1].X, 9);
        Assert.Equal(0, stroke.Points[^1].Y, 9);
        Assert.All(stroke.Points, p => Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y)));
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

    /// <summary>A document whose drawing layer follows the whole skeleton, with b swung 90° at frame 0.</summary>
    private static (Doc Doc, Frame Frame, RigIndex Rig) WholeSkeletonBentAtTheJoint()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armature = TwoBones();
        doc.Scene.PoseTrack = new PoseTrack
        {
            Keys = [new PoseKey { Frame = 0, Bones = { ["b"] = new BonePose { RotationDeg = 90 } } }],
        };
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        layer.BoneId = "";
        return (doc, layer.Cels[0].Frame!, RigIndex.For(doc));
    }

    [Fact]
    public void ALineToolLineDrawnAcrossAPosedJointStaysUnderThePenAlongItsLength()
    {
        // Sensitivity on B404: the drawn pair kept two points and two weights,
        // and once the render subdivided pairs its middle posed as one blend
        // of its ends — bowing away from where the pen drew it. The ends
        // alone cannot see that; every rendered point is held to the segment.
        var (doc, frame, rig) = WholeSkeletonBentAtTheJoint();
        var drawn = Line((50, 2), (102, 80)); // from the arm, across the joint, down the swung b
        var pen = drawn.Points.ToList();
        Skinning.UnposeDrawnStroke(drawn, doc, frame, 0, rig);
        frame.Strokes.Add(drawn);

        var shown = Skinning.PoseFrameForRender(doc, frame, 0, rig).Strokes.Single(s => s.Id == drawn.Id);
        var off = Departure(shown.Points, pen, 0, 0);
        Assert.True(off < 1.5, $"the committed line sits {off:F2} px off where the pen drew it");
    }

    [Fact]
    public void AWholeSkeletonNudgeMovesTheWholeLineNotJustItsEnds()
    {
        // Sensitivity on B404: the transform weighted a weightless pair at its
        // two control points and the write-back stored that ramp, so the line
        // took a new shape on top of the nudge — and kept it.
        var (doc, frame, rig) = WholeSkeletonBentAtTheJoint();
        var stroke = Line((50, 2), (190, 2)); // across the joint at rest
        frame.Strokes.Add(stroke);
        var before = Skinning.PoseFrameForRender(doc, frame, 0, rig).Strokes.Single().Points.ToList();

        var mover = Skinning.PoseSpaceMover(doc, frame, 0, rig, (x, y) => Shift(0, 40, x, y));
        TransformOps.TransformFrame(frame, (x, y) => Shift(0, 40, x, y), mover: mover);

        var after = Skinning.PoseFrameForRender(doc, frame, 0, rig).Strokes.Single().Points;
        var off = Departure(after, before, 0, 40);
        Assert.True(off < 1.5, $"after the nudge the line departs {off:F2} px from where it was, moved 40 px down");
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
