using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// Binding and posing strokes — phase 2 of <c>docs/DESIGN-bones.md</c>. The
/// two promises under test: posing never mutates the record (invariant 1),
/// and the solve is plain deterministic arithmetic, so an identity pose is a
/// byte-for-byte no-op.
/// </summary>
public class SkinningTests
{
    /// <summary>One bone lying along +x from the origin.</summary>
    private static Armature OneBone(double rotationDeg = 0) => new()
    {
        Bones = [new Bone { Id = "b", X = 0, Y = 0, Length = 100, RotationDeg = rotationDeg }],
    };

    private static Stroke OnTheBone()
    {
        var stroke = new Stroke
        {
            Points = [new StrokePoint(10, 0, 1), new StrokePoint(50, 0, 0.8), new StrokePoint(90, 0, 1)],
        };
        Skinning.AssignAll(stroke, "b");
        return stroke;
    }

    private static Dictionary<string, BonePose> Rotate(double deg) =>
        new() { ["b"] = new BonePose { RotationDeg = deg } };

    [Fact]
    public void PosingTheRigMovesTheStrokePointsNotTheRecord()
    {
        var stroke = OnTheBone();
        var before = stroke.Points.ToList();

        var posed = Skinning.PoseStroke(stroke, OneBone(), Rotate(90));

        // The record is untouched: same points, same binding, no seed path.
        Assert.Equal(before, stroke.Points);
        Assert.NotNull(stroke.Weights);
        Assert.Null(stroke.RestPoints);

        // The posed copy is the render-ready construction: moved points, the
        // bind-pose path riding along, and no binding left to pose twice.
        Assert.NotSame(stroke, posed);
        Assert.Equal(stroke.Id, posed.Id);
        Assert.Null(posed.Weights);
        Assert.NotNull(posed.RestPoints);
        Assert.Equal(posed.Points.Count, posed.RestPoints!.Count);
        Assert.NotEqual(posed.Points[0], posed.RestPoints[0]);
    }

    [Fact]
    public void AWhollyBoundStrokeFollowsItsBoneRigidly()
    {
        var posed = Skinning.PoseStroke(OnTheBone(), OneBone(), Rotate(90));

        for (var i = 0; i < posed.Points.Count; i++)
        {
            var rest = posed.RestPoints![i];
            // A 90° turn about the bone's origin: (x, y) → (−y, x).
            Assert.Equal(-rest.Y, posed.Points[i].X, 9);
            Assert.Equal(rest.X, posed.Points[i].Y, 9);
            // Pressure is not a coordinate and does not move.
            Assert.Equal(rest.Pressure, posed.Points[i].Pressure);
        }
    }

    [Fact]
    public void AnIdentityPoseLeavesEveryDoubleWhereItWas()
    {
        var stroke = OnTheBone();
        var posed = Skinning.PoseStroke(stroke, OneBone(), pose: null);

        // Bitwise, not approximate: the identity delta is built from exact
        // constants, so the blend must return the very same doubles. This is
        // what makes "posed at rest" indistinguishable from "never posed".
        Assert.Equal(posed.RestPoints, posed.Points);
        Assert.Equal(GeometryOps.Densify(stroke.Points), posed.RestPoints);
    }

    [Fact]
    public void AHalfBoundPointMovesHalfWay()
    {
        var stroke = new Stroke { Points = [new StrokePoint(10, 0, 1), new StrokePoint(11, 0, 1)] };
        stroke.Weights = [new BoneBinding { BoneId = "b", PointWeights = [0.5, 0.5] }];

        // 180° about the origin sends (10, 0) to (−10, 0); half-bound lands
        // in the middle, which is the remainder holding the point at rest.
        var posed = Skinning.PoseStroke(stroke, OneBone(), Rotate(180));

        Assert.Equal(0, posed.Points[0].X, 9);
        Assert.Equal(0, posed.Points[0].Y, 9);
    }

    [Fact]
    public void AnUnboundStrokeComesBackUntouchedAndUnclonedFromPosing()
    {
        var stroke = new Stroke { Points = [new StrokePoint(1, 2, 1)] };
        Assert.Same(stroke, Skinning.PoseStroke(stroke, OneBone(), Rotate(45)));
    }

    [Fact]
    public void WeightsInterpolateAlongTheDensifiedCurve()
    {
        // Two control points 40 px apart force densification; the ramp from
        // 1 to 0 must arrive on the inserted points, not jump at the end.
        var stroke = new Stroke { Points = [new StrokePoint(0, 0, 1), new StrokePoint(40, 0, 1), new StrokePoint(80, 0, 1)] };
        stroke.Weights = [new BoneBinding { BoneId = "b", PointWeights = [1, 1, 0] }];

        var posed = Skinning.PoseStroke(stroke, OneBone(), Rotate(180));
        var rest = posed.RestPoints!;
        Assert.True(rest.Count > 3, $"densify produced {rest.Count} points");

        // Between x=40 and x=80 the weight ramps 1→0, so the posed x must
        // walk monotonically from fully-flipped back to rest.
        for (var i = 0; i < rest.Count; i++)
        {
            if (rest[i].X < 40 - 1e-9) continue;
            var w = 1 - (rest[i].X - 40) / 40;
            Assert.Equal(rest[i].X * (1 - w) + -rest[i].X * w, posed.Points[i].X, 6);
        }
    }

    [Fact]
    public void AutoBindFavoursTheNearBoneAndSumsToOne()
    {
        var armature = new Armature
        {
            Bones =
            [
                new Bone { Id = "left", X = 0, Y = 0, Length = 100 },
                new Bone { Id = "right", X = 0, Y = 200, Length = 100, RotationDeg = 0 },
            ],
        };
        var stroke = new Stroke { Points = [new StrokePoint(50, 2, 1), new StrokePoint(50, 198, 1)] };

        Skinning.AutoBind(stroke, armature);

        Assert.NotNull(stroke.Weights);
        var left = stroke.Weights!.First(b => b.BoneId == "left");
        var right = stroke.Weights!.First(b => b.BoneId == "right");
        Assert.True(left.WeightAt(0) > 0.9, $"near-left point: left {left.WeightAt(0):F3}");
        Assert.True(right.WeightAt(1) > 0.9, $"near-right point: right {right.WeightAt(1):F3}");
        for (var i = 0; i < 2; i++)
        {
            var sum = left.WeightAt(i) + right.WeightAt(i);
            Assert.True(sum is > 0.9 and <= 1.0 + 1e-9, $"point {i} sums to {sum:F4}");
        }
    }

    [Fact]
    public void BakingAPoseWritesOrdinaryStrokes()
    {
        var bound = OnTheBone();
        var plain = new Stroke { Points = [new StrokePoint(5, 5, 1)] };
        var frame = new Frame { Strokes = [bound, plain] };

        var baked = Skinning.BakeFrame(frame, OneBone(), Rotate(90));

        Assert.Equal(1, baked);
        // The bound stroke was replaced by its posed self: no link to the
        // rig, a seed path, and the id it always had.
        var result = frame.Strokes[0];
        Assert.Equal(bound.Id, result.Id);
        Assert.Null(result.Weights);
        Assert.NotNull(result.RestPoints);
        // The unbound one is exactly the object it was.
        Assert.Same(plain, frame.Strokes[1]);
    }

    [Fact]
    public void AnUnboundStrokeWritesNoRigKeys()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Scene.Layers.Add(new Layer
        {
            Cels = [new Cel { Frame = new Frame { Strokes = [new Stroke { Points = [new StrokePoint(1, 1, 1)] }] } }],
        });
        var json = DocJson.Serialize(doc);
        Assert.DoesNotContain("\"weights\"", json);
        Assert.DoesNotContain("\"restPoints\"", json);
    }

    [Fact]
    public void ABoundStrokeRoundTripsItsBindingAndSeedPath()
    {
        var doc = DocumentFactory.CreateDoc();
        var stroke = OnTheBone();
        stroke.Weights![0].PointWeights = [1, 0.5, 0.25];
        stroke.RestPoints = [new StrokePoint(1, 2, 1), new StrokePoint(3, 4, 1), new StrokePoint(5, 6, 1)];
        doc.Scene.Layers.Add(new Layer { Cels = [new Cel { Frame = new Frame { Strokes = [stroke] } }] });

        var back = DocJson.Deserialize(DocJson.Serialize(doc));
        var s = back.Scene.Layers[^1].Cels[0].Frame!.Strokes[0];
        Assert.Equal("b", s.Weights![0].BoneId);
        Assert.Equal([1, 0.5, 0.25], s.Weights[0].PointWeights!);
        Assert.Equal(stroke.RestPoints, s.RestPoints!);
    }

    [Fact]
    public void CloningABoundStrokeSharesNoWeights()
    {
        var stroke = OnTheBone();
        stroke.Weights![0].PointWeights = [1, 1, 1];
        stroke.RestPoints = [new StrokePoint(0, 0, 1)];

        var copy = stroke.Clone();
        copy.Weights![0].PointWeights![0] = 0;
        copy.RestPoints!.Add(new StrokePoint(9, 9, 1));

        Assert.Equal(1, stroke.Weights[0].PointWeights![0]);
        Assert.Single(stroke.RestPoints);
    }
}
