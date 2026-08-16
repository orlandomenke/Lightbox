using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// The weight brush's arithmetic: Blender's model — sum to at most 1 per
/// point, painting one bone up takes unlocked others down, locks hold, and
/// what is painted to nothing leaves the record.
/// </summary>
public class WeightPaintTests
{
    private static Stroke Line(int points = 5)
    {
        var stroke = new Stroke();
        for (var i = 0; i < points; i++) stroke.Points.Add(new StrokePoint(i * 10, 0, 1));
        return stroke;
    }

    [Fact]
    public void PaintingABareStrokeCreatesALocalBinding()
    {
        var stroke = Line();

        var changed = WeightPaint.Apply(stroke, "arm", cx: 20, cy: 0, radius: 12, strength: 1, WeightBrushMode.Add);

        Assert.True(changed);
        var b = Assert.Single(stroke.Weights!);
        Assert.Equal("arm", b.BoneId);
        // Full effect under the centre, nothing outside the radius.
        Assert.Equal(1, b.WeightAt(2), 9);
        Assert.Equal(0, b.WeightAt(0), 9);
        Assert.Equal(0, b.WeightAt(4), 9);
    }

    [Fact]
    public void PaintingOneBoneUpTakesTheUnlockedOtherDown()
    {
        var stroke = Line(1);
        stroke.Weights = [new BoneBinding { BoneId = "a", PointWeights = [1.0] }];

        WeightPaint.Apply(stroke, "b", cx: 0, cy: 0, radius: 5, strength: 0.4, WeightBrushMode.Add);

        var a = stroke.Weights!.First(x => x.BoneId == "a");
        var b = stroke.Weights!.First(x => x.BoneId == "b");
        Assert.Equal(0.4, b.WeightAt(0), 9);
        Assert.Equal(0.6, a.WeightAt(0), 9);
        Assert.Equal(1.0, a.WeightAt(0) + b.WeightAt(0), 9);
    }

    [Fact]
    public void ALockedBoneHoldsAndThePaintedBoneGivesTheExcessBack()
    {
        var stroke = Line(1);
        stroke.Weights = [new BoneBinding { BoneId = "a", PointWeights = [0.8] }];
        var locks = new HashSet<string> { "a" };

        WeightPaint.Apply(stroke, "b", cx: 0, cy: 0, radius: 5, strength: 1, WeightBrushMode.Add, locks);

        var a = stroke.Weights!.First(x => x.BoneId == "a");
        var b = stroke.Weights!.First(x => x.BoneId == "b");
        // The lock is a floor: a keeps 0.8, so b can only ever reach 0.2.
        Assert.Equal(0.8, a.WeightAt(0), 9);
        Assert.Equal(0.2, b.WeightAt(0), 9);
    }

    [Fact]
    public void PaintingTheLockedBoneItselfIsRefused()
    {
        var stroke = Line(1);
        stroke.Weights = [new BoneBinding { BoneId = "a", PointWeights = [0.5] }];

        var changed = WeightPaint.Apply(
            stroke, "a", cx: 0, cy: 0, radius: 5, strength: 1, WeightBrushMode.Add,
            new HashSet<string> { "a" });

        Assert.False(changed);
        Assert.Equal(0.5, stroke.Weights![0].WeightAt(0), 9);
    }

    [Fact]
    public void SubtractLeavesTheDeficitHoldingThePointAtRest()
    {
        var stroke = Line(1);
        stroke.Weights = [new BoneBinding { BoneId = "a", PointWeights = [1.0] }];

        WeightPaint.Apply(stroke, "a", cx: 0, cy: 0, radius: 5, strength: 0.3, WeightBrushMode.Subtract);

        // No other bone picks up the slack: the remainder is rest-hold, by
        // the same rule Skinning.Blend applies.
        Assert.Equal(0.7, stroke.Weights![0].WeightAt(0), 9);
        Assert.Single(stroke.Weights!);
    }

    [Fact]
    public void SubtractingEverythingRemovesTheBindingFromTheRecord()
    {
        var stroke = Line(1);
        stroke.Weights = [new BoneBinding { BoneId = "a", PointWeights = [0.4] }];

        WeightPaint.Apply(stroke, "a", cx: 0, cy: 0, radius: 5, strength: 1, WeightBrushMode.Subtract);

        // Optional means absent: painted to nothing is not bound at zero.
        Assert.Null(stroke.Weights);
    }

    [Fact]
    public void SmoothPullsAPointTowardItsNeighbours()
    {
        var stroke = Line(3);
        stroke.Weights = [new BoneBinding { BoneId = "a", PointWeights = [1.0, 0.0, 1.0] }];

        WeightPaint.Apply(stroke, "a", cx: 10, cy: 0, radius: 4, strength: 1, WeightBrushMode.Smooth);

        // The lonely zero between two ones smooths to their average.
        Assert.Equal(1.0, stroke.Weights![0].WeightAt(1), 9);
    }

    [Fact]
    public void ACoarseBindingMaterialisesOnFirstTouchAndKeepsItsMeaning()
    {
        var stroke = Line(3);
        stroke.Weights = [new BoneBinding { BoneId = "a" }]; // uniform 1.0

        WeightPaint.Apply(stroke, "a", cx: 10, cy: 0, radius: 4, strength: 0.5, WeightBrushMode.Subtract);

        var a = stroke.Weights![0];
        Assert.NotNull(a.PointWeights);
        // The touched point went down; the untouched ones still read 1.
        Assert.Equal(0.5, a.WeightAt(1), 9);
        Assert.Equal(1.0, a.WeightAt(0), 9);
        Assert.Equal(1.0, a.WeightAt(2), 9);
    }

    [Fact]
    public void MirroredBonePairsByNameSuffix()
    {
        var armature = new Armature
        {
            Bones =
            [
                new Bone { Id = "1", Name = "hip.l", X = 100, Y = 200 },
                new Bone { Id = "2", Name = "hip.r", X = 300, Y = 200 },
                new Bone { Id = "3", Name = "spine", X = 200, Y = 100 },
            ],
        };

        Assert.Equal("2", WeightPaint.MirroredBone(armature, "1")!.Id);
        Assert.Equal("1", WeightPaint.MirroredBone(armature, "2")!.Id);
        // A bone that declares no side has no pair.
        Assert.Null(WeightPaint.MirroredBone(armature, "3"));
    }

    [Fact]
    public void MirrorReflectsAcrossThePairsOwnAxisNotTheCanvas()
    {
        var armature = new Armature
        {
            Bones =
            [
                new Bone { Id = "1", Name = "hip.l", X = 100, Y = 200 },
                new Bone { Id = "2", Name = "hip.r", X = 300, Y = 200 },
            ],
        };
        var (a, b) = (armature.Bones[0], armature.Bones[1]);

        // The pair's axis is the vertical line x = 200 — the character's own
        // spine, wherever it sits on the paper.
        var m = WeightPaint.Mirror(armature, a, b, 120, 250)!.Value;
        Assert.Equal(280, m.X, 9);
        Assert.Equal(250, m.Y, 9);
    }

    // ---- painting on the posed drawing --------------------------------------------

    [Fact]
    public void ADabHitsThePointWhereItIsDrawnNotWhereItRests()
    {
        var stroke = Line();
        // The drawing on screen is posed: every point shifted (+100, +50).
        var posed = stroke.Points.Select(p => p with { X = p.X + 100, Y = p.Y + 50 }).ToList();

        // A dab where the artist sees point 2 — its POSED position.
        var changed = WeightPaint.Apply(
            stroke, "arm", cx: 120, cy: 50, radius: 12, strength: 1, WeightBrushMode.Add,
            hitPoints: posed);

        Assert.True(changed);
        // The weight lands on the same index the artist aimed at, and the
        // rest-position neighbourhood of that dab (points 0..4 at y=0) is
        // untouched by the dab's rest-space coordinates.
        Assert.Equal(1, stroke.Weights![0].WeightAt(2), 9);
        Assert.Equal(0, stroke.Weights![0].WeightAt(0), 9);

        // And the same dab against the rest positions misses everything —
        // which is what painting on a posed drawing used to do.
        var untouched = Line();
        Assert.False(WeightPaint.Apply(
            untouched, "arm", cx: 120, cy: 50, radius: 12, strength: 1, WeightBrushMode.Add));
    }

    [Fact]
    public void AStaleHitListIsIgnoredRatherThanMisread()
    {
        var stroke = Line(5);
        // Three positions for a five-point stroke: the caller's pose is stale
        // (a point was added mid-gesture). Index arithmetic against it would
        // paint the wrong points, so the brush falls back to rest.
        var stale = Line(3).Points;

        var changed = WeightPaint.Apply(
            stroke, "arm", cx: 20, cy: 0, radius: 12, strength: 1, WeightBrushMode.Add,
            hitPoints: stale);

        Assert.True(changed);
        Assert.Equal(1, stroke.Weights![0].WeightAt(2), 9);
    }

    [Fact]
    public void TheMirroredDabLandsOnThePairsPosedLimb()
    {
        // Two mirrored bones about x=100: hip.l at (60,0), hip.r at (140,0).
        var armature = new Armature();
        var left = new Bone { Name = "hip.l", X = 60, Y = 0, Length = 30 };
        var right = new Bone { Name = "hip.r", X = 140, Y = 0, Length = 30 };
        armature.Bones.Add(left);
        armature.Bones.Add(right);

        // Identity deltas: posed mirroring must be exactly the rest mirror.
        var identity = new Dictionary<string, RigidDelta>();
        var atRest = WeightPaint.Mirror(armature, left, right, 70, 10)!.Value;
        var posedSame = WeightPaint.MirrorPosed(armature, left, right, identity, 70, 10)!.Value;
        Assert.Equal(atRest.X, posedSame.X, 9);
        Assert.Equal(atRest.Y, posedSame.Y, 9);

        // Pose the pair apart: left carried +0+20, right carried +0-40. A dab
        // made at the LEFT limb's posed position must come out at the RIGHT
        // limb's posed position — the rest mirror, ridden out on the pair's
        // own motion.
        var deltas = new Dictionary<string, RigidDelta>
        {
            [left.Id] = new RigidDelta(1, 0, 0, 20),
            [right.Id] = new RigidDelta(1, 0, 0, -40),
        };
        var m = WeightPaint.MirrorPosed(armature, left, right, deltas, 70, 10 + 20)!.Value;
        Assert.Equal(130, m.X, 9);          // rest mirror of x=70 about x=100
        Assert.Equal(10 - 40, m.Y, 9);      // the rest y, carried by the pair's delta
    }

    [Fact]
    public void UnapplyInvertsApplyExactly()
    {
        var delta = new RigidDelta(Math.Cos(0.7), Math.Sin(0.7), 12.5, -3.25);
        var (px, py) = delta.Apply(41.5, -17.25);
        var (x, y) = delta.Unapply(px, py);
        Assert.Equal(41.5, x, 12);
        Assert.Equal(-17.25, y, 12);
    }
}
