using System.Diagnostics;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// What a puppet drag is allowed to cost (Q239). The solve runs once per
/// pointer event and once per rendered frame, in front of the re-stamp that
/// actually fills the frame's budget — so the budgets here are about leaving
/// that budget alone, and each one names the implementation it rules out.
/// </summary>
/// <remarks>
/// Every timing is the <b>minimum</b> of several runs: contention only ever
/// adds, so the minimum is the cost and the median is the cost plus the
/// machine's afternoon. Absolutes are printed and nothing here renders a
/// pixel. <b>Measured 2026-10-11</b> on the owner's machine, alone: the
/// largest mesh solves in 1.3 ms and an ordinary one in 0.33 ms; placing pins
/// on the largest costs 112 ms; 20,000 points map in 2.4 ms; a 4K drawing
/// meshes in 8.6 ms; posing strokes through pins costs half what a bone
/// does. The ceilings sit well above those because this suite runs beside
/// others — the bone baseline measured a tenfold swing in that company
/// (B442) — and each still rules out the implementation its test names.
/// </remarks>
public class PinSolvePerformanceTests(ITestOutputHelper output)
{
    /// <summary>One screen refresh at 60 Hz.</summary>
    private const double RefreshMs = 1000.0 / 60;

    /// <remarks>
    /// <b>Warmed until it stops getting faster.</b> The runtime compiles a
    /// method cheaply at first and properly only once it has been called
    /// enough and a background thread has caught up. Warming with one call
    /// measured the cheap compile on the bone rig's baseline and reported a
    /// posed drawing at nine times its cost (B442). Batches run until three
    /// in a row fail to beat the best by 5%, or two seconds.
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

    /// <summary>A figure-sized drawing meshed as finely as the cap allows: the worst case a drag can meet.</summary>
    private static (PinMesh Mesh, PinHandle[] Pins, (double X, double Y)[] Targets) Largest()
    {
        var mesh = PinMesh.Build([PinShapes.Box(0, 0, 1200, 1600)], PinMesh.MinCell);
        PinHandle[] pins =
        [
            new(600, 100), new(600, 800), new(200, 500), new(1000, 500),
            new(300, 1500), new(900, 1500), new(600, 400, PinKind.Starch, Extent: 150),
        ];
        var targets = pins.Select((p, i) => (p.X + (i % 3 - 1) * 40.0, p.Y + (i % 2) * 30.0)).ToArray();
        return (mesh, pins, targets);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void TheLargestMeshSolvesInsideOneRefresh()
    {
        // Rules out a dense solve: 4,000 vertices is an 8,000-unknown system,
        // and a dense back-substitution alone is ~64 million multiplies.
        var (mesh, pins, targets) = Largest();
        var solve = PinSolve.Prepare(mesh, pins);
        var deformed = new (double X, double Y)[solve.VertexCount];

        var ms = MinMs(12, () => solve.Solve(targets, deformed));

        output.WriteLine($"{mesh.Vertices.Count} vertices, {pins.Length} pins: solve {ms:F3} ms (one refresh is {RefreshMs:F2})");
        Assert.True(mesh.Vertices.Count > PinMesh.MaxVertices / 2, "the fixture is not near the cap, so this measured nothing");
        Assert.True(ms < RefreshMs, $"a drag on the largest mesh costs {ms:F2} ms before a single dab is stamped");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void ADefaultMeshSolvesInWellUnderARefresh()
    {
        // The ordinary case: one character at the default cell. A third of a
        // millisecond measured, so a puppet on every layer of a shot still
        // leaves the frame to the brush.
        var mesh = PinMesh.Build([PinShapes.Box(0, 0, 500, 900)]);
        PinHandle[] pins = [new(250, 60), new(250, 450), new(80, 300), new(420, 300), new(150, 850), new(350, 850)];
        var targets = pins.Select((p, i) => (p.X + i * 7.0, p.Y - i * 5.0)).ToArray();
        var solve = PinSolve.Prepare(mesh, pins);
        var deformed = new (double X, double Y)[solve.VertexCount];

        var ms = MinMs(20, () => solve.Solve(targets, deformed));

        output.WriteLine($"{mesh.Vertices.Count} vertices at the default cell: solve {ms:F4} ms");
        Assert.True(ms < RefreshMs / 2, $"an ordinary puppet costs {ms:F3} ms a drag");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void ADragIsNotARefactorisation()
    {
        // Rules out the easy mistake: rebuilding the system on every pointer
        // event because the targets changed. Placing pins is allowed to be
        // slow; moving them is not. A ratio, because both sides scale with
        // the machine together.
        var (mesh, pins, targets) = Largest();
        var solve = PinSolve.Prepare(mesh, pins);
        var deformed = new (double X, double Y)[solve.VertexCount];

        var prepare = MinMs(5, () => PinSolve.Prepare(mesh, pins));
        var drag = MinMs(12, () => solve.Solve(targets, deformed));

        output.WriteLine($"prepare {prepare:F2} ms, solve {drag:F3} ms, ratio {prepare / drag:F1}x");
        Assert.True(drag * 4 < prepare, $"a drag ({drag:F3} ms) costs more than a quarter of placing the pins ({prepare:F2} ms)");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void SolvingIntoTheCallersBufferAllocatesNothing()
    {
        // A drag is hundreds of solves a second on the UI thread; garbage
        // there comes back as a pause the artist feels and the average hides.
        var (mesh, pins, targets) = Largest();
        var solve = PinSolve.Prepare(mesh, pins);
        var deformed = new (double X, double Y)[solve.VertexCount];
        solve.Solve(targets, deformed);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 50; i++) solve.Solve(targets, deformed);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        output.WriteLine($"50 solves allocated {allocated} bytes");
        Assert.Equal(0, allocated);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void FindingAPointsTriangleDoesNotSearchTheMesh()
    {
        // Every control point of every stroke is mapped every frame. On a
        // regular grid that is a division — 2.4 ms for these — where a scan
        // over ~8,000 triangles for each of 20,000 points is 160 million
        // tests and several hundred milliseconds.
        var (mesh, pins, targets) = Largest();
        var deformed = PinSolve.Prepare(mesh, pins).Solve(targets);
        var points = Enumerable.Range(0, 20_000)
            .Select(i => (X: 10 + i * 0.059 % 1180, Y: 10 + i * 0.079 % 1580)).ToArray();

        double sink = 0;
        var ms = MinMs(8, () =>
        {
            foreach (var (x, y) in points)
            {
                var (mx, my) = PinSolve.MapPoint(mesh, deformed, x, y);
                sink += mx + my;
            }
        });

        output.WriteLine($"20,000 points through {mesh.Triangles.Count / 3} triangles: {ms:F2} ms ({sink:E1})");
        Assert.True(ms < 60, $"mapping a drawing's points costs {ms:F1} ms a frame");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void MeshingADrawingIsQuickEnoughToDoWhenTheDrawingChanges()
    {
        // The mesh is derived, so it is rebuilt whenever a stroke is added to
        // a pinned drawing. That has to be a hitch nobody notices, and the
        // vertex cap has to hold at 4K as it does at 1080p.
        var strokes = Enumerable.Range(0, 60)
            .Select(i => PinShapes.Box(100 + i * 50, 100 + i * 20, 400 + i * 50, 900 + i * 20)).ToArray();

        PinMesh? mesh = null;
        var ms = MinMs(5, () => mesh = PinMesh.Build(strokes, PinMesh.MinCell));

        output.WriteLine($"60 strokes over ~3400x2000 at the finest cell: {mesh!.Vertices.Count} vertices in {ms:F1} ms");
        Assert.InRange(mesh.Vertices.Count, 1, PinMesh.MaxVertices);
        Assert.True(ms < 100, $"re-meshing after a stroke took {ms:F0} ms");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void PosingAPinnedDrawingCostsNoMoreThanPosingABonedOne()
    {
        // The comparison that matters to the artist: pins must not be the
        // slow way to rig. Same strokes, one posed through the mesh and one
        // through a bone, and the pin path may cost at most three times the
        // bone path it stands beside.
        var strokes = Enumerable.Range(0, 200).Select(i => new Stroke
        {
            Points = [.. Enumerable.Range(0, 40).Select(p => new StrokePoint(20 + p * 9, 5 + i * 0.25, 1))],
        }).ToArray();

        var mesh = PinMesh.Build([PinShapes.Bar()], cell: 20);
        PinHandle[] pins = [new(20, 30), new(200, 30), new(380, 30)];
        var bent = PinSolve.Prepare(mesh, pins).Solve([(20, 30), (200, 30), (330, 160)]);
        var pinMs = MinMs(8, () =>
        {
            foreach (var s in strokes) _ = PinSkinning.PoseStroke(s, mesh, bent);
        });

        var armature = new Armature
        {
            Bones =
            [
                new Bone { Id = "upper", X = 0, Y = 30, Length = 200 },
                new Bone { Id = "fore", ParentId = "upper", Connected = true, Length = 200 },
            ],
        };
        var bound = strokes.Select(s =>
        {
            var copy = s.Clone(newId: false);
            Skinning.AutoBind(copy, armature);
            return copy;
        }).ToArray();
        var pose = new Dictionary<string, BonePose> { ["fore"] = new() { RotationDeg = 45 } };
        var boneMs = MinMs(8, () =>
        {
            foreach (var s in bound) _ = Skinning.PoseStroke(s, armature, pose);
        });

        output.WriteLine($"200 strokes: pins {pinMs:F2} ms, bones {boneMs:F2} ms, ratio {pinMs / boneMs:F2}x");
        Assert.True(pinMs < boneMs * 3, $"posing through pins ({pinMs:F2} ms) is more than three times the bone path ({boneMs:F2} ms)");
    }
}
