using System.Diagnostics;
using Lightbox.Core.Documents;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// What posing the existing bone rig costs, before anything is drawn — the
/// baseline the puppet-pin work (Q239) is measured against, and a guard the
/// rig did not have: until now its only budget was the jiggle walk's.
/// </summary>
/// <remarks>
/// <para>
/// A posed frame is built in three steps, each on a path that runs per
/// pointer event during a pose drag and per frame during playback: the pose
/// is evaluated, the skeleton is solved, and every bound stroke is densified
/// and blended. Each step has its own test so a regression says which one.
/// None of them stamps a dab; the re-stamp is the raster suite's and the
/// performance lab's to measure (<c>perf/scenarios/pose-drag.json</c>).
/// </para>
/// <para>
/// Timings are the <b>minimum</b> of several runs and every absolute is
/// printed. <b>The ceilings were written before the tests were first run</b>
/// (2026-10-10 — the machine was busy with a lab run), so they are one
/// refresh or a stated fraction of it, not numbers tuned to pass. The first
/// run decides whether each is a guard or a finding.
/// </para>
/// </remarks>
public class BoneRigPerformanceTests(ITestOutputHelper output)
{
    /// <summary>One screen refresh at 60 Hz.</summary>
    private const double RefreshMs = 1000.0 / 60;

    private static double MinMs(int runs, Action action)
    {
        action();
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
    public void EvaluatingAndSolvingASkeletonIsAHundredthOfARefresh()
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
        Assert.True(poseMs + solveMs < RefreshMs / 100,
            $"evaluating and solving 30 bones costs {poseMs + solveMs:F3} ms");
        Assert.True(deltaMs < RefreshMs / 50, $"the skinning deltas for 30 bones cost {deltaMs:F3} ms");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void PosingADrawingFitsInsideOneRefreshBeforeAnythingIsStamped()
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
        Assert.True(ms < RefreshMs, $"posing one drawing's geometry costs {ms:F1} ms — the frame is gone before a dab lands");
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

        var a = MinMs(10, () => _ = Skinning.PoseFrameForRender(docA, small, 7));
        var b = MinMs(10, () => _ = Skinning.PoseFrameForRender(docB, large, 7));

        output.WriteLine($"50 strokes {a:F2} ms, 200 strokes {b:F2} ms, ratio {b / a:F2}x for 4x the drawing");
        Assert.True(b < a * 8, $"4x the strokes cost {b / a:F1}x the time");
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

        var a = MinMs(10, () => _ = Skinning.PoseFrameForRender(still, frameA, 40));
        var b = MinMs(10, () => _ = Skinning.PoseFrameForRender(sprung, frameB, 40));

        output.WriteLine($"posed frame {a:F2} ms; with six sprung bones {b:F2} ms ({b / a:F2}x)");
        Assert.True(b < a * 2 + 1, $"springs took a posed frame from {a:F2} ms to {b:F2} ms");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void AutoBindingADrawingIsAHitchNotAWait()
    {
        // Paid once, when the artist presses Auto-bind — and again on every
        // frame of a layer that follows the whole skeleton, which is why it
        // has a budget at all: 100 lines against 30 bones inside three
        // refreshes, the lab's own definition of a stall.
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
        Assert.True(ms < 50, $"auto-binding one drawing took {ms:F0} ms");
    }
}
