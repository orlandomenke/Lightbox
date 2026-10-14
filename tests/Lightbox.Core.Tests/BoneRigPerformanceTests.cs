using System.Diagnostics;
using Lightbox.Core.Documents;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// What posing the bone rig costs before anything is drawn — a guard the rig
/// did not have (its only budget was the jiggle walk's), and the baseline
/// any other way of deforming a drawing gets measured against (B442).
/// </summary>
/// <remarks>
/// <para>
/// A posed frame is built in three steps, each on a path that runs per
/// pointer event during a pose drag and per frame during playback: the pose
/// is evaluated, the skeleton is solved, and every bound stroke is densified
/// and blended. Each step has its own test so a regression says which one.
/// None of them stamps a dab; the re-stamp is the raster suite's and the
/// performance lab's to measure.
/// </para>
/// <para>
/// <b>The ratios are the guards; the ceilings only catch a collapse.</b> This
/// suite runs beside five others on a loaded machine — the Core project is a
/// single leg, so its timed tests are not held back for a quiet one — and a
/// posed drawing that costs 5 ms alone was measured at 59 ms in that company.
/// So each absolute limit is set about thirty times above what the code
/// costs, and what actually holds the design is a ratio between two things
/// measured in the same moment: the same drawing on a bigger rig, the same
/// bind against more bones. Every absolute is printed.
/// </para>
/// </remarks>
public class BoneRigPerformanceTests(ITestOutputHelper output)
{
    /// <remarks>
    /// <b>Warmed until it stops getting faster.</b> The runtime compiles a
    /// method cheaply at first and properly only once it has been called
    /// enough and a background thread has caught up. The first version of
    /// this helper warmed with one call and so measured the cheap compile:
    /// it reported 45.9 ms for a posed drawing that costs under 10. Batches
    /// run until three in a row fail to beat the best by 5%, or two seconds.
    /// </remarks>
    private static double MinMs(int runs, Action action)
    {
        var warm = Stopwatch.StartNew();
        var settled = double.MaxValue;
        for (var flat = 0; flat < 3 && warm.ElapsedMilliseconds < 2000;)
        {
            var batch = double.MaxValue;
            for (var i = 0; i < 10; i++)
            {
                var t = Stopwatch.GetTimestamp();
                action();
                batch = Math.Min(batch, Stopwatch.GetElapsedTime(t).TotalMilliseconds);
            }
            flat = batch < settled * 0.95 ? 0 : flat + 1;
            settled = Math.Min(settled, batch);
        }
        var best = double.MaxValue;
        for (var i = 0; i < runs; i++)
        {
            var start = Stopwatch.GetTimestamp();
            action();
            best = Math.Min(best, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        return best;
    }

    /// <summary>
    /// Two costs for a ratio, measured turn and turn about so that whatever
    /// else the machine is doing lands on both. Measured one after the other,
    /// a fixed 4x came out as 9.4x in the middle of the full suite.
    /// </summary>
    private static (double A, double B) MinMsPair(int rounds, Action a, Action b)
    {
        var (bestA, bestB) = (MinMs(1, a), MinMs(1, b));
        for (var i = 0; i < rounds; i++)
        {
            var t = Stopwatch.GetTimestamp();
            a();
            bestA = Math.Min(bestA, Stopwatch.GetElapsedTime(t).TotalMilliseconds);
            t = Stopwatch.GetTimestamp();
            b();
            bestB = Math.Min(bestB, Stopwatch.GetElapsedTime(t).TotalMilliseconds);
        }
        return (bestA, bestB);
    }

    /// <summary>
    /// A character-sized skeleton: a spine of connected bones with a limb of
    /// three hanging off every other one. Thirty bones at the default size.
    /// </summary>
    private static Armature Skeleton(int spine = 6)
    {
        var rig = new Armature();
        for (var i = 0; i < spine; i++)
        {
            rig.Bones.Add(i == 0
                ? new Bone { Id = "s0", X = 200, Y = 500, Length = 60, RotationDeg = -90 }
                : new Bone { Id = $"s{i}", ParentId = $"s{i - 1}", Connected = true, Length = 60 });
            if (i % 2 == 1) continue;
            foreach (var side in new[] { "l", "r" })
            {
                var parent = $"s{i}";
                for (var j = 0; j < 4; j++)
                {
                    var id = $"{side}{i}_{j}";
                    rig.Bones.Add(new Bone
                    {
                        Id = id, ParentId = parent, Connected = true, Length = 50,
                        RotationDeg = j == 0 ? (side == "l" ? -80 : 80) : 5,
                    });
                    parent = id;
                }
            }
        }
        return rig;
    }

    /// <summary>Every bone turned a little, differently on each of two keys twelve frames apart.</summary>
    private static PoseTrack Swing(Armature rig)
    {
        PoseKey Key(int frame, double sign) => new()
        {
            Frame = frame,
            Bones = rig.Bones.Select((b, i) => (b.Id, Pose: new BonePose { RotationDeg = sign * (4 + i % 7) }))
                .ToDictionary(e => e.Id, e => e.Pose),
        };
        return new PoseTrack { Keys = [Key(0, 1), Key(12, -1), Key(24, 1)] };
    }

    /// <summary>
    /// A drawing over the skeleton: <paramref name="count"/> pen lines of
    /// forty control points each, spread across the figure.
    /// </summary>
    private static List<Stroke> Drawing(int count)
    {
        var strokes = new List<Stroke>(count);
        for (var s = 0; s < count; s++)
        {
            var points = new List<StrokePoint>(40);
            var (ox, oy) = (40 + s * 37 % 320, 120 + s * 53 % 380);
            for (var p = 0; p < 40; p++)
                points.Add(new StrokePoint(ox + p * 2.5, oy + Math.Sin(p * 0.3 + s) * 8, 0.8));
            strokes.Add(new Stroke { Points = points });
        }
        return strokes;
    }

    private static (Doc Doc, Frame Frame) Bound(int strokes, bool jiggle = false)
    {
        var rig = Skeleton();
        if (jiggle)
        {
            foreach (var bone in rig.Bones.Where(b => b.Id.EndsWith("_3", StringComparison.Ordinal)))
                bone.Jiggle = new BoneJiggle();
        }
        var doc = new Doc { Armature = rig };
        doc.Scene.PoseTrack = Swing(rig);
        var frame = new Frame { Strokes = Drawing(strokes) };
        foreach (var stroke in frame.Strokes) Skinning.AutoBind(stroke, rig);
        return (doc, frame);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void EvaluatingAndSolvingASkeletonIsFarBelowAMillisecond()
    {
        // The part of a pose drag that is pure rig: interpolate the keys,
        // walk the hierarchy. Thirty bones is arithmetic on thirty rows, and
        // it runs again for every overlay, hit test and heat view that asks
        // where a bone is.
        var rig = Skeleton();
        var track = Swing(rig);
        Assert.Equal(30, rig.Bones.Count);

        var poseMs = MinMs(50, () => _ = ArmatureOps.PoseAt(track, 7));
        var pose = ArmatureOps.PoseAt(track, 7);
        var solveMs = MinMs(50, () => _ = ArmatureOps.Solve(rig, pose));
        var deltaMs = MinMs(50, () => _ = Skinning.Deltas(rig, pose));

        output.WriteLine($"30 bones: PoseAt {poseMs:F4} ms, Solve {solveMs:F4} ms, Deltas {deltaMs:F4} ms");
        Assert.True(poseMs + solveMs < 1, $"evaluating and solving 30 bones costs {poseMs + solveMs:F3} ms");
        Assert.True(deltaMs < 1, $"the skinning deltas for 30 bones cost {deltaMs:F3} ms");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void PosingADrawingsGeometryStaysAFewMilliseconds()
    {
        // The geometry half of a posed frame: 100 lines, 4,000 control
        // points, every one densified and blended. Whatever this costs is
        // paid before the brush engine starts, on every pose-drag event.
        var (doc, frame) = Bound(100);

        Frame? posed = null;
        var ms = MinMs(10, () => posed = Skinning.PoseFrameForRender(doc, frame, 7));

        var points = posed!.Strokes.Sum(s => s.Points.Count);
        output.WriteLine($"100 strokes, {points} densified points, 30 bones: {ms:F2} ms ({ms / points * 1e6:F0} ns a point)");
        Assert.NotSame(frame, posed);
        Assert.True(ms < 150, $"posing one drawing's geometry costs {ms:F1} ms against about 5");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void PosingCostsTheStrokesItMovesAndNotTheSquareOfThem()
    {
        // Four times the drawing may cost four times the posing and a little
        // more; sixteen times is something being recomputed per stroke that
        // belongs to the frame (the deltas, the correctives, the binding).
        // Absolutes sit under the ceiling in the test above; this one is
        // about the shape of the curve.
        var (docA, small) = Bound(50);
        var (docB, large) = Bound(200);

        var (a, b) = MinMsPair(
            12, () => _ = Skinning.PoseFrameForRender(docA, small, 7), () => _ = Skinning.PoseFrameForRender(docB, large, 7));

        output.WriteLine($"50 strokes {a:F2} ms, 200 strokes {b:F2} ms, ratio {b / a:F2}x for 4x the drawing");
        Assert.True(b < a * 10, $"4x the strokes cost {b / a:F1}x the time");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void TheSkeletonIsSolvedOncePerFrameNotOncePerStroke()
    {
        // Every stroke here is bound whole to one bone, so posing it is one
        // rigid map per point whatever the size of the rig. If the cost
        // follows the bone count, the skeleton is being re-solved per stroke
        // — which it was, measured 2026-10-11. A ratio, because both sides
        // ride the same machine.
        (Doc, Frame) Cutout(int spine)
        {
            var rig = Skeleton(spine);
            var doc = new Doc { Armature = rig };
            doc.Scene.PoseTrack = Swing(rig);
            var frame = new Frame { Strokes = Drawing(200) };
            foreach (var stroke in frame.Strokes) Skinning.AssignAll(stroke, "s0");
            return (doc, frame);
        }
        var (smallDoc, smallFrame) = Cutout(6);     // 30 bones
        var (bigDoc, bigFrame) = Cutout(24);        // 120 bones
        Assert.Equal(120, bigDoc.Armature!.Bones.Count);

        // Solved once: 0.95x. Solved per stroke: 4.9x. The limit sits between.
        var (small, big) = MinMsPair(
            12,
            () => _ = Skinning.PoseFrameForRender(smallDoc, smallFrame, 7),
            () => _ = Skinning.PoseFrameForRender(bigDoc, bigFrame, 7));

        output.WriteLine($"200 strokes on one bone: 30-bone rig {small:F2} ms, 120-bone rig {big:F2} ms ({big / small:F2}x)");
        Assert.True(big < small * 2.2, $"4x the bones made a one-bone drawing cost {big / small:F1}x");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void AutoBindGrowsWithTheBonesNotWithTheirCube()
    {
        // Distance to every bone is bones x points and has to be. The joint
        // zones are a table of bone pairs, and looking each pair's parent up
        // by walking the bone list made that table bones-cubed, per stroke.
        // Four times the bones may cost about four times the bind.
        var strokes = Drawing(60);
        var small = Skeleton(6);
        var big = Skeleton(24);

        Action Bind(Armature rig) => () =>
        {
            foreach (var stroke in strokes)
            {
                stroke.Weights = null;
                Skinning.AutoBind(stroke, rig, keepPoints: true);
            }
        };
        // Four times the bones is sixteen times the bone pairs, and the zone
        // table is one entry per pair — so even the fixed bind may cost up to
        // about 16x here, and measured 10.6-12.3x on a loaded machine (4x on a
        // quiet one). Built through a walk of the bone list per pair, it is
        // 64x by the arithmetic and measured 30-45x. The limit sits between
        // the two, where 14 sat on top of the right answer and flaked.
        var (a, b) = MinMsPair(8, Bind(small), Bind(big));

        output.WriteLine($"auto-bind 60 strokes: 30 bones {a:F2} ms, 120 bones {b:F2} ms ({b / a:F1}x for 4x the bones)");
        Assert.True(b < a * 24, $"4x the bones cost {b / a:F1}x the bind");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void AFrameNothingIsBoundOnCostsNothingToPose()
    {
        // A rigged document is mostly unrigged drawings. Asking for one of
        // them posed must hand it straight back — no clone, no pose
        // evaluation, no garbage — or having a rig anywhere taxes every
        // frame everywhere.
        var (doc, _) = Bound(1);
        var plain = new Frame { Strokes = Drawing(100) };
        _ = Skinning.PoseFrameForRender(doc, plain, 7);

        var before = GC.GetAllocatedBytesForCurrentThread();
        Frame? result = null;
        for (var i = 0; i < 100; i++) result = Skinning.PoseFrameForRender(doc, plain, 7);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        output.WriteLine($"100 calls on an unbound frame allocated {allocated} bytes");
        Assert.Same(plain, result);
        Assert.Equal(0, allocated);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void SpringsOnTheRigDoNotMultiplyTheCostOfAPosedFrame()
    {
        // Jiggle replays a window of history to find this frame's swing.
        // That is per frame, not per stroke, so six sprung bones may add a
        // fixed amount to a posed drawing and must not double it.
        var (still, frameA) = Bound(100);
        var (sprung, frameB) = Bound(100, jiggle: true);

        // Interleaved, not one after the other: measured in two blocks, a
        // runner that got busier between them read as springs doubling the
        // cost (22 ms against 50 on a CI run where neither number was this
        // test's own).
        var (a, b) = MinMsPair(
            10,
            () => _ = Skinning.PoseFrameForRender(still, frameA, 40),
            () => _ = Skinning.PoseFrameForRender(sprung, frameB, 40));

        output.WriteLine($"posed frame {a:F2} ms; with six sprung bones {b:F2} ms ({b / a:F2}x)");
        Assert.True(b < a * 2 + 1, $"springs took a posed frame from {a:F2} ms to {b:F2} ms");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void AutoBindingADrawingIsAHitchNotAWait()
    {
        // Paid once, when the artist presses Auto-bind — and again on every
        // frame of a layer that follows the whole skeleton, which is why it
        // has a budget at all.
        var rig = Skeleton();
        var strokes = Drawing(100);

        var ms = MinMs(6, () =>
        {
            foreach (var stroke in strokes)
            {
                stroke.Weights = null;
                Skinning.AutoBind(stroke, rig);
            }
        });

        output.WriteLine($"auto-bind, 100 strokes x 30 bones: {ms:F2} ms");
        Assert.True(ms < 150, $"auto-binding one drawing took {ms:F0} ms against about 5");
    }
}
