using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Serialization;
using Lightbox.Core.Timeline;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// Joint weighting (Q217): a drawing follows the bone it is on rigidly and
/// blends with a neighbour only inside the joint's zone — so a straight line
/// along an arm stays straight when the forearm turns, and bends at the elbow.
/// New binds use it; everything bound before keeps posing as it did.
/// </summary>
public class JointWeightingTests(ITestOutputHelper output)
{
    /// <summary>Upper arm 0..100 along +x, forearm glued to its tip, 100 long.</summary>
    private static Armature Arm(double? zone = null) => new()
    {
        Bones =
        [
            new Bone { Id = "upper", Length = 100 },
            new Bone { Id = "fore", ParentId = "upper", Connected = true, Length = 100, JointZone = zone },
        ],
    };

    private static Stroke Freehand() => new()
    {
        Points = Enumerable.Range(0, 41).Select(i => new StrokePoint(i * 5, 10, 1)).ToList(),
    };

    private static readonly Dictionary<string, BonePose> ForearmAt90 =
        new() { ["fore"] = new BonePose { RotationDeg = 90 } };

    /// <summary>Where a rest point lands when the forearm turns 90° about the elbow and carries it rigidly.</summary>
    private static (double X, double Y) RigidOnForearm(StrokePoint p) => (100 - p.Y, p.X - 100);

    private static double Moved(Stroke posed, int i) =>
        GeometryOps.Dist(posed.Points[i], posed.RestPoints![i]);

    [Fact]
    public void ANewBindKeepsALineStraightAlongEachBoneAndBendsItAtTheJoint()
    {
        var arm = Arm();
        var stroke = Freehand();
        Skinning.AutoBind(stroke, arm);
        var posed = Skinning.PoseStroke(stroke, arm, ForearmAt90);
        var rest = posed.RestPoints!;
        var zone = Skinning.JointZoneOf(arm, arm.Bones[1]);

        // Farther than the zone from the elbow on the upper arm: not moved at all.
        var upper = Enumerable.Range(0, rest.Count).Where(i => rest[i].X < 100 - zone - 12).ToList();
        var worstUpper = upper.Max(i => Moved(posed, i));
        // On the forearm past the zone: exactly the rigid rotation. The zone
        // compares the two bones' distances, so for a line drawn 10 px off
        // the bone it reaches a little farther along than its own width —
        // the same 12 px margin both sides.
        var fore = Enumerable.Range(0, rest.Count).Where(i => rest[i].X > 100 + zone + 12).ToList();
        var worstFore = fore.Max(i =>
        {
            var (x, y) = RigidOnForearm(rest[i]);
            return Math.Sqrt(Math.Pow(posed.Points[i].X - x, 2) + Math.Pow(posed.Points[i].Y - y, 2));
        });
        output.WriteLine($"zone {zone} px; upper arm moved up to {worstUpper:E2} px over {upper.Count} samples, forearm off rigid by {worstFore:E2} px");
        Assert.True(worstUpper < 1e-9, $"the upper-arm part moved {worstUpper:F3} px");
        Assert.True(worstFore < 1e-9, $"the forearm part left the rigid rotation by {worstFore:F3} px");
    }

    [Fact]
    public void TheBendIsContinuous_NoStepAnywhereAlongTheLine()
    {
        var arm = Arm();
        var stroke = Freehand();
        Skinning.AutoBind(stroke, arm);
        var posed = Skinning.PoseStroke(stroke, arm, ForearmAt90);
        var worst = Enumerable.Range(1, posed.Points.Count - 1).Max(i =>
            Math.Abs(GeometryOps.Dist(posed.Points[i], posed.Points[i - 1])
                     - GeometryOps.Dist(posed.RestPoints![i], posed.RestPoints[i - 1])));
        output.WriteLine($"largest change in a neighbour step: {worst:F2} px");
        Assert.True(worst < 2.0, $"the line steps {worst:F2} px somewhere along its length");
    }

    [Fact]
    public void ALineToolLineIsGivenTheJointPointsItNeedsWhenBound()
    {
        // Weights live on control points: a two-point line could only carry one
        // blend from end to end, however it was weighted. Binding gives the
        // span across the joint points to hold its two rigid stretches.
        var arm = Arm();
        var line = new Stroke { Points = [new StrokePoint(0, 10, 1), new StrokePoint(200, 10, 1)] };
        Skinning.AutoBind(line, arm);
        Assert.True(line.Points.Count > 2, "the pair was bound as two points");

        var posed = Skinning.PoseStroke(line, arm, ForearmAt90);
        var rest = posed.RestPoints!;
        var zone = Skinning.JointZoneOf(arm, arm.Bones[1]);
        var worstUpper = Enumerable.Range(0, rest.Count).Where(i => rest[i].X < 100 - zone - 12).Max(i => Moved(posed, i));
        output.WriteLine($"{line.Points.Count} points after binding; upper arm moved up to {worstUpper:E2} px");
        Assert.True(worstUpper < 1e-9, $"the upper-arm part of the line moved {worstUpper:F3} px");
    }

    [Fact]
    public void ACorrectedStrokeKeepsItsPointsSoItsCorrectiveStillApplies()
    {
        // Sensitivity on Q217: a corrective stores one offset per control
        // point and stops applying the moment the count changes. Auto-bind
        // must not insert points under one — the write-back's own rule.
        var arm = Arm();
        var line = new Stroke { Points = [new StrokePoint(0, 10, 1), new StrokePoint(200, 10, 1)] };
        Skinning.AutoBind(line, arm, keepPoints: true);
        Assert.Equal(2, line.Points.Count);
        Assert.NotNull(line.Weights);
        Assert.All(line.Weights!, b => Assert.True(b.PointWeights is null || b.PointWeights.Count == 2));

        var frame = new Frame { Strokes = { line } };
        Assert.False(Skinning.IsCorrected(frame, line.Id));
        frame.Correctives =
        [
            new Corrective
            {
                DriverBoneId = "fore",
                Stops = [new CorrectiveStop { AngleDeg = 90, Strokes = [new StrokeCorrection { StrokeId = line.Id }] }],
            },
        ];
        Assert.True(Skinning.IsCorrected(frame, line.Id));
    }

    [Fact]
    public void InsertingPointsDropsAPathThatNoLongerMatches()
    {
        var arm = Arm();
        var line = new Stroke
        {
            Points = [new StrokePoint(0, 10, 1), new StrokePoint(200, 10, 1)],
            Path = new StrokePath(),
        };
        Skinning.AutoBind(line, arm);
        Assert.True(line.Points.Count > 2);
        Assert.Null(line.Path);
    }

    [Fact]
    public void AFillsEdgesStayStraight_AndItsClosingEdgeIsSubdividedToo()
    {
        // Sensitivity on Q217: a contour is drawn as straight edges, so the
        // brush's curve through inserted points would bow the fill at rest.
        var arm = Arm();
        var corners = new List<StrokePoint>
        {
            new(60, -20, 1), new(140, -20, 1), new(140, 20, 1), new(60, 20, 1),
        };
        var fill = new Stroke { Tool = ToolKind.Fill, Points = [.. corners] };
        Skinning.AutoBind(fill, arm);

        Assert.True(fill.Points.Count > corners.Count, "a fill across the joint gained no points");
        foreach (var p in fill.Points)
        {
            var onAnEdge = Enumerable.Range(0, corners.Count).Min(i =>
                GeometryOps.DistToSegment(p, corners[i], corners[(i + 1) % corners.Count]));
            Assert.True(onAnEdge < 1e-9, $"({p.X:F2},{p.Y:F2}) left the rectangle's edges by {onAnEdge:E2}");
        }
        // The closing edge (60,20)→(60,-20) lies wholly on the upper arm, so it
        // needs nothing; the two edges that cross the elbow are what gained.
        var crossing = fill.Points.Count(p => p.X is > 60 and < 140);
        output.WriteLine($"{fill.Points.Count} points, {crossing} along the crossing edges");
        Assert.True(crossing >= 2 * 10);
    }

    [Fact]
    public void ALineDrawnOnALayerBoundBeforeTakesThatLayersWeighting()
    {
        // Sensitivity on Q217: a new line continuing an old one across the
        // elbow must pose as its neighbours on that layer do.
        var doc = DocumentFactory.CreateDoc();
        doc.Armature = Arm();
        doc.Scene.PoseTrack = new PoseTrack
        {
            Keys = [new PoseKey { Frame = 0, Bones = { ["fore"] = new BonePose { RotationDeg = 90 } } }],
        };
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        layer.BoneId = "";
        var frame = layer.Cels[0].Frame!;
        var rig = RigIndex.For(doc);

        var onOld = new Stroke { Points = Enumerable.Range(0, 15).Select(i => new StrokePoint(10 + i * 5, 10, 1)).ToList() };
        Skinning.UnposeDrawnStroke(onOld, doc, frame, 0, rig);
        layer.JointWeights = true;
        var onNew = new Stroke { Points = Enumerable.Range(0, 15).Select(i => new StrokePoint(10 + i * 5, 10, 1)).ToList() };
        Skinning.UnposeDrawnStroke(onNew, doc, frame, 0, rig);

        // Far from the elbow, joint weighting gives the forearm nothing; the
        // old weighting gives it a share everywhere.
        double Fore(Stroke s) => s.Weights!.FirstOrDefault(b => b.BoneId == "fore")?.WeightAt(0) ?? 0;
        output.WriteLine($"forearm share at x 10: old layer {Fore(onOld):F3}, joint layer {Fore(onNew):F3}");
        Assert.True(Fore(onOld) > 0.01, "the line on the old layer was weighted by joint");
        Assert.Equal(0, Fore(onNew));
    }

    [Fact]
    public void AFreehandStrokeIsNotGivenExtraPoints()
    {
        var arm = Arm();
        var stroke = Freehand();
        var count = stroke.Points.Count;
        Skinning.AutoBind(stroke, arm);
        Assert.Equal(count, stroke.Points.Count);
    }

    [Fact]
    public void TheZoneIsTheBonesOwn_ZeroIsAHinge()
    {
        // A point 8 px inside the upper arm from the elbow, on the line.
        double ForeWeightNearTheElbow(double? zone)
        {
            var arm = Arm(zone);
            var stroke = new Stroke { Points = [new StrokePoint(92, 10, 1), new StrokePoint(93, 10, 1)] };
            Skinning.AutoBind(stroke, arm);
            return stroke.Weights!.FirstOrDefault(b => b.BoneId == "fore")?.WeightAt(0) ?? 0;
        }
        var hinge = ForeWeightNearTheElbow(0);
        var standard = ForeWeightNearTheElbow(null);
        var soft = ForeWeightNearTheElbow(60);
        output.WriteLine($"forearm share 8 px from the elbow: hinge {hinge:F3}, default {standard:F3}, 60 px zone {soft:F3}");
        Assert.Equal(0, hinge);
        Assert.True(standard > 0, "the default zone blends this close to the joint");
        Assert.True(soft > standard, "a wider zone blends more");
        Assert.Equal(25, Skinning.JointZoneOf(Arm(), Arm().Bones[1]));
    }

    [Fact]
    public void ALayerBoundBeforeKeepsTheOldWeighting_ANewOneUsesJoints()
    {
        // Q217 answer 2: nothing already rigged poses differently.
        var arm = Arm();
        var stroke = Freehand();
        var legacy = Skinning.PoseStroke(stroke, arm, ForearmAt90, wholeSkeleton: true, jointWeights: false);
        var joints = Skinning.PoseStroke(stroke, arm, ForearmAt90, wholeSkeleton: true, jointWeights: true);
        var at40 = Enumerable.Range(0, legacy.RestPoints!.Count).MinBy(i => Math.Abs(legacy.RestPoints[i].X - 40));
        output.WriteLine($"at rest x 40 on the upper arm: legacy moved {Moved(legacy, at40):F2} px, joints {Moved(joints, at40):E2} px");
        Assert.True(Moved(legacy, at40) > 0.5, "the legacy weighting was changed");
        Assert.True(Moved(joints, at40) < 1e-9, "joint weighting moved the upper arm");
    }

    [Fact]
    public void TheMarkerTravelsWithTheBindingThroughALink()
    {
        var scene = new Scene();
        var lines = new Layer { Name = "Lines", BoneId = "", JointWeights = true, LinkId = "link-1" };
        var colour = new Layer { Name = "Colour", LinkId = "link-1" };
        var old = new Layer { Name = "Old", BoneId = "" };
        scene.Layers.AddRange([lines, colour, old]);
        scene.LayerLinks = [new LayerLink { Id = "link-1", Bones = true }];

        Assert.True(scene.UsesJointWeights(lines));
        Assert.True(scene.UsesJointWeights(colour));
        Assert.False(scene.UsesJointWeights(old));
    }

    [Fact]
    public void NeitherSettingIsWrittenUntilUsed()
    {
        var doc = new Doc();
        doc.Armature = Arm();
        doc.Scene.Layers.Add(new Layer { Name = "Rigged", BoneId = "" });
        var json = DocJson.Serialize(doc);
        Assert.DoesNotContain("\"jointZone\"", json);
        Assert.DoesNotContain("\"jointWeights\"", json);

        doc.Armature.Bones[1].JointZone = 12;
        doc.Scene.Layers[^1].JointWeights = true;
        var back = DocJson.Deserialize(DocJson.Serialize(doc));
        Assert.Equal(12, back.Armature!.Bones[1].JointZone);
        Assert.True(back.Scene.Layers[^1].JointWeights);
    }

    [Fact]
    public void JointWeightingIsDeterministic()
    {
        var a = Freehand();
        var b = Freehand();
        Skinning.AutoBind(a, Arm());
        Skinning.AutoBind(b, Arm());
        string Of(Stroke s) => System.Text.Json.JsonSerializer.Serialize(new { s.Points, s.Weights }, DocJson.Options);
        Assert.Equal(Of(a), Of(b));
    }
}
