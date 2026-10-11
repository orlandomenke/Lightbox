using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// The puppet solve (Q239): pins hold their points, and everything between
/// them moves as rigidly as it can. Each test names a way a deformer feels
/// wrong under the hand — a pin that drifts, a limb that shrinks, an arm
/// that drags the leg — and the numbers are printed so a threshold can be
/// reset against a deliberately broken solve when the real one lands.
/// </summary>
public class PinSolveTests(ITestOutputHelper output)
{
    private static PinMesh BarMesh() => PinMesh.Build([PinShapes.Bar()], cell: 20);

    private static (double X, double Y)[] Rest(PinSolve solve, PinHandle[] pins) =>
        solve.Solve(pins.Select(p => (p.X, p.Y)).ToArray());

    private static double MaxDistance(IReadOnlyList<(double X, double Y)> a, IReadOnlyList<(double X, double Y)> b) =>
        Enumerable.Range(0, a.Count).Max(i =>
            Math.Sqrt((a[i].X - b[i].X) * (a[i].X - b[i].X) + (a[i].Y - b[i].Y) * (a[i].Y - b[i].Y)));

    [Fact]
    public void APuppetNobodyHasMovedIsTheIdentityToTheLastBit()
    {
        // The cage's promise, kept here: dropping pins changes nothing until
        // one is dragged — not "nothing visible", nothing.
        var mesh = BarMesh();
        PinHandle[] pins = [new(20, 30), new(200, 30), new(380, 30)];
        var solve = PinSolve.Prepare(mesh, pins);

        var at = Rest(solve, pins);

        Assert.Equal(mesh.Vertices.Count, solve.VertexCount);
        for (var i = 0; i < at.Length; i++)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(mesh.Vertices[i].X), BitConverter.DoubleToInt64Bits(at[i].X));
            Assert.Equal(BitConverter.DoubleToInt64Bits(mesh.Vertices[i].Y), BitConverter.DoubleToInt64Bits(at[i].Y));
        }
    }

    [Fact]
    public void APinLandsExactlyWhereItIsDragged()
    {
        // A pin is a promise about one point. A soft constraint that lets it
        // trail the pointer by a pixel reads as lag in the hand.
        var mesh = BarMesh();
        PinHandle[] pins = [new(20, 30), new(203.5, 27.25), new(380, 30)];   // the middle one off any vertex
        var solve = PinSolve.Prepare(mesh, pins);
        (double X, double Y)[] targets = [(20, 30), (230, 90), (360, 140)];

        var at = solve.Solve(targets);

        for (var i = 0; i < pins.Length; i++)
        {
            var (x, y) = PinSolve.MapPoint(mesh, at, pins[i].X, pins[i].Y);
            Assert.Equal(targets[i].X, x, 6);
            Assert.Equal(targets[i].Y, y, 6);
        }
    }

    [Fact]
    public void OnePinAloneCarriesTheWholeDrawingWithIt()
    {
        var mesh = BarMesh();
        PinHandle[] pins = [new(200, 30)];
        var solve = PinSolve.Prepare(mesh, pins);

        var at = solve.Solve([(260, -45)]);

        for (var i = 0; i < at.Length; i++)
        {
            Assert.Equal(mesh.Vertices[i].X + 60, at[i].X, 6);
            Assert.Equal(mesh.Vertices[i].Y - 75, at[i].Y, 6);
        }
    }

    [Theory]
    [InlineData(40, 0, 0)]
    [InlineData(-115, 30, -80)]
    [InlineData(180, 0, 0)]
    public void TurningEveryPinTogetherTurnsTheDrawingAndBendsNothing(double degrees, double dx, double dy)
    {
        // "As rigid as possible" has an exact case: when the pins themselves
        // moved rigidly, the answer is that motion everywhere. A solve that
        // only knows translation (plain linear blending of point handles)
        // collapses the limb toward its middle here instead.
        var mesh = BarMesh();
        PinHandle[] pins = [new(20, 30), new(380, 30)];
        var solve = PinSolve.Prepare(mesh, pins);
        var r = degrees * Math.PI / 180;
        (double X, double Y) Turn(double x, double y) =>
            (200 + Math.Cos(r) * (x - 200) - Math.Sin(r) * (y - 30) + dx,
             30 + Math.Sin(r) * (x - 200) + Math.Cos(r) * (y - 30) + dy);

        var at = solve.Solve(pins.Select(p => Turn(p.X, p.Y)).ToArray());

        var expected = mesh.Vertices.Select(v => Turn(v.X, v.Y)).ToArray();
        var worst = MaxDistance(expected, at);
        output.WriteLine($"{degrees} degrees: the worst vertex is {worst:E2} from the rigid answer");
        Assert.True(worst < 1e-5, $"a rigid motion of the pins bent the drawing by {worst:F4}");
    }

    [Fact]
    public void BendingALimbKeepsItsAreaAndTurnsNoTriangleInsideOut()
    {
        // Three pins, the elbow gesture: hold the shoulder and the elbow,
        // swing the wrist 60 degrees. The forearm must arrive as a forearm.
        var mesh = BarMesh();
        PinHandle[] pins = [new(20, 30), new(200, 30), new(380, 30)];
        var solve = PinSolve.Prepare(mesh, pins);
        var r = 60 * Math.PI / 180;
        (double X, double Y) wrist = (200 + Math.Cos(r) * 180, 30 + Math.Sin(r) * 180);

        (double X, double Y)[] targets = [(20, 30), (200, 30), wrist];
        var at = solve.Solve(targets);
        var blended = PinShapes.StraightLineBlend(mesh, pins, targets);

        var before = PinShapes.Area(mesh, mesh.Vertices);
        var after = PinShapes.Area(mesh, at);
        output.WriteLine($"area {before:F0} -> {after:F0} ({after / before:P1})");
        Assert.InRange(after / before, 0.9, 1.1);
        for (var t = 0; t < mesh.Triangles.Count; t += 3)
        {
            Assert.True(PinShapes.SignedArea(mesh, at, t) > 0, $"triangle {t / 3} folded over in a 60 degree bend");
        }

        // And the two halves each moved as a piece: the upper arm stayed, the
        // forearm turned, and the bend is at the elbow rather than smeared
        // down the whole limb.
        var upper = Enumerable.Range(0, at.Length).Where(i => mesh.Vertices[i].X < 140).ToArray();
        var fore = Enumerable.Range(0, at.Length).Where(i => mesh.Vertices[i].X > 260).ToArray();
        var upperResidual = PinShapes.RigidResidual(mesh.Vertices, at, upper);
        var foreResidual = PinShapes.RigidResidual(mesh.Vertices, at, fore);
        var blendedFore = PinShapes.RigidResidual(mesh.Vertices, blended, fore);
        output.WriteLine(
            $"rigid residual: upper arm {upperResidual:F2}, forearm {foreResidual:F2}; "
            + $"a straight-line blend leaves the forearm {blendedFore:F2}");
        // Measured 2026-10-11: 2.95 and 3.00 against 24.6 for the blend. What
        // is left is the pinch around each point pin on a bar sixty thick.
        Assert.True(upperResidual < 6, $"the upper arm deformed by {upperResidual:F2} though both its pins held");
        Assert.True(foreResidual < 6, $"the forearm deformed by {foreResidual:F2} instead of turning");
        Assert.True(foreResidual * 3 < blendedFore,
            $"the forearm ({foreResidual:F2}) is no more rigid than a blend with no rigidity at all ({blendedFore:F2})");
    }

    [Fact]
    public void PullingALimbLongerDoesNotMakeItFatter()
    {
        // The test that is about the second step and nothing else. The first
        // step lets every triangle scale, so two pins pulled 30% apart are
        // answered by the whole limb growing 30% in every direction — a
        // forearm that inflates when the wrist is pulled. The rigid fit is
        // what takes that back out. (The adversary skipped the second step
        // and only a starch test noticed, by accident.)
        var mesh = BarMesh();
        PinHandle[] pins = [new(20, 30), new(380, 30)];
        var solve = PinSolve.Prepare(mesh, pins);

        var at = solve.Solve([(20, 30), (488, 30)]);   // 360 apart becomes 468

        var top = mesh.NearestVertex(200, 0);
        var bottom = mesh.NearestVertex(200, 80);
        var before = mesh.Vertices[bottom].Y - mesh.Vertices[top].Y;
        var after = at[bottom].Y - at[top].Y;
        var area = PinShapes.Area(mesh, at) / PinShapes.Area(mesh, mesh.Vertices);
        output.WriteLine($"thickness {before:F1} -> {after:F1}; area {area:P1} for a 30% pull");
        Assert.True(after < before * 1.1, $"the limb went from {before:F1} thick to {after:F1}");
        Assert.True(area < 1.45, $"the limb's area grew to {area:P0}; scaling in both directions would be 169%");
    }

    [Fact]
    public void TwoPinsAHairApartAreOnePin()
    {
        // The adversary's near-miss: a millionth of a unit apart with
        // different targets is a demand no drawing can meet, and the mesh
        // answered it by moving 7e8 units.
        var mesh = BarMesh();
        PinHandle[] pins = [new(100, 30), new(100.000001, 30), new(300, 30)];

        var at = PinSolve.Prepare(mesh, pins).Solve([(100, 30), (150, 80), (300, 30)]);

        var worst = MaxDistance(mesh.Vertices, at);
        Assert.True(worst < 1e-9, $"the second pin was not dropped: the mesh moved {worst:E2}");
    }

    [Theory]
    [InlineData(1e200, 1e200)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(double.NaN, double.NaN)]
    public void APinDroppedNowhereIsNotACrash(double x, double y)
    {
        var mesh = BarMesh();
        PinHandle[] pins = [new(20, 30), new(380, 30), new(x, y)];

        var at = PinSolve.Prepare(mesh, pins).Solve([(20, 30), (380, 90), (0, 0)]);

        Assert.All(at, v => Assert.True(double.IsFinite(v.X) && double.IsFinite(v.Y)));
        var (px, py) = PinSolve.MapPoint(mesh, at, 380, 30);
        Assert.Equal(380, px, 6);
        Assert.Equal(90, py, 6);
    }

    [Fact]
    public void PullingOneProngLeavesTheOtherAlone()
    {
        // The reason the mesh follows the silhouette. The prong tips are 180
        // apart across the gap and 580 apart through the drawing; a deformer
        // that measures straight-line distance drags the second prong along.
        //
        // The body is held by three pins, not two, and that is a finding
        // rather than a convenience: a pin is a point, a point is a pivot,
        // and a body held at two points flexes between them like a beam —
        // with two base pins the far tip moved 28 of the 80, no better than
        // the straight-line blend. Three pins, a pin at the limb's root or
        // starch on the body each bring it under a tenth.
        var mesh = PinMesh.Build([PinShapes.Fork()], cell: 20);
        PinHandle[] pins = [new(30, 230), new(150, 230), new(270, 230), new(30, 20)];
        (double X, double Y)[] targets = [(30, 230), (150, 230), (270, 230), (-50, 20)];
        var solve = PinSolve.Prepare(mesh, pins);

        var at = solve.Solve(targets);
        var blended = PinShapes.StraightLineBlend(mesh, pins, targets);

        var otherTip = mesh.NearestVertex(270, 20);
        var moved = Math.Sqrt(
            Math.Pow(at[otherTip].X - mesh.Vertices[otherTip].X, 2) + Math.Pow(at[otherTip].Y - mesh.Vertices[otherTip].Y, 2));
        var pulledTip = mesh.NearestVertex(30, 20);
        var pulled = Math.Abs(at[pulledTip].X - mesh.Vertices[pulledTip].X);
        var blind = Math.Sqrt(
            Math.Pow(blended[otherTip].X - mesh.Vertices[otherTip].X, 2)
            + Math.Pow(blended[otherTip].Y - mesh.Vertices[otherTip].Y, 2));
        output.WriteLine(
            $"dragged prong moved {pulled:F1}; the other prong's tip moved {moved:F2} "
            + $"(a straight-line blend moves it {blind:F2})");
        Assert.True(pulled > 60, $"the dragged prong only moved {pulled:F1} of 80");
        Assert.True(moved < 10, $"the other prong's tip moved {moved:F2} of an 80 drag");
        Assert.True(moved * 2 < blind,
            $"the mesh moved the other prong {moved:F2}, the blend that knows no shape {blind:F2}");
    }

    [Fact]
    public void APieceWithNoPinStaysExactlyWhereItWas()
    {
        // Two separate drawings on one layer, pins on one of them. The other
        // has nothing holding it and nothing moving it; it must not drift,
        // and a singular system must not be what decides.
        var mesh = PinMesh.Build([PinShapes.Box(0, 0, 100, 100), PinShapes.Box(200, 0, 300, 100)], cell: 20);
        PinHandle[] pins = [new(20, 50), new(80, 50)];
        var solve = PinSolve.Prepare(mesh, pins);

        var at = solve.Solve([(20, 50), (80, 120)]);

        var pinned = mesh.ComponentOf(mesh.NearestVertex(50, 50));
        var movedSomething = false;
        for (var i = 0; i < at.Length; i++)
        {
            if (mesh.ComponentOf(i) == pinned)
            {
                movedSomething |= at[i] != mesh.Vertices[i];
                continue;
            }
            Assert.Equal(mesh.Vertices[i], at[i]);
        }
        Assert.True(movedSomething, "the pinned piece did not move either");
    }

    [Fact]
    public void StarchKeepsItsPatchFromBending()
    {
        // Bend a limb in the middle, then starch the middle: the patch under
        // the starch pin should move as one piece and push the bend out to
        // either side of it.
        var mesh = BarMesh();
        PinHandle[] plain = [new(20, 30), new(380, 30)];
        PinHandle[] starched = [new(20, 30), new(380, 30), new(200, 30, PinKind.Starch, Extent: 70)];
        (double X, double Y)[] bend = [(20, 30), (330, 170)];
        var patch = Enumerable.Range(0, mesh.Vertices.Count)
            .Where(i => Math.Abs(mesh.Vertices[i].X - 200) <= 60).ToArray();

        var without = PinSolve.Prepare(mesh, plain).Solve(bend);
        var with = PinSolve.Prepare(mesh, starched).Solve([.. bend, (0, 0)]);

        var soft = PinShapes.RigidResidual(mesh.Vertices, without, patch);
        var stiff = PinShapes.RigidResidual(mesh.Vertices, with, patch);
        output.WriteLine($"rigid residual of the middle patch: {soft:F3} without starch, {stiff:F3} with");
        Assert.True(soft > 0.5, $"the bend put only {soft:F3} of deformation in the patch — nothing for starch to remove");
        Assert.True(stiff < soft * 0.5, $"starch left {stiff:F3} of {soft:F3}");
    }

    [Fact]
    public void AStarchPinHoldsNoPositionAndItsTargetIsIgnored()
    {
        var mesh = BarMesh();
        PinHandle[] pins = [new(20, 30), new(380, 30), new(200, 30, PinKind.Starch, Extent: 60)];
        var solve = PinSolve.Prepare(mesh, pins);

        var a = solve.Solve([(20, 30), (380, 100), (200, 30)]);
        var b = solve.Solve([(20, 30), (380, 100), (-9999, 12345)]);

        Assert.Equal(a, b);
    }

    [Fact]
    public void ATargetThatIsNotANumberLeavesItsPinAtRest()
    {
        // A NaN from a bad key or a hostile file must not reach the renderer
        // as four thousand NaN vertices.
        var mesh = BarMesh();
        PinHandle[] pins = [new(20, 30), new(380, 30)];
        var solve = PinSolve.Prepare(mesh, pins);

        var at = solve.Solve([(double.NaN, 30), (380, double.PositiveInfinity)]);

        Assert.All(at, v => Assert.True(double.IsFinite(v.X) && double.IsFinite(v.Y)));
        Assert.Equal(mesh.Vertices, at);
    }

    [Fact]
    public void NoPinsAtAllIsTheRestDrawing()
    {
        var mesh = BarMesh();
        var solve = PinSolve.Prepare(mesh, []);
        Assert.Equal(mesh.Vertices, solve.Solve(Array.Empty<(double, double)>()));
    }

    [Fact]
    public void TheWrongNumberOfTargetsIsRefusedRatherThanGuessedAt()
    {
        var mesh = BarMesh();
        var solve = PinSolve.Prepare(mesh, [new(20, 30), new(380, 30)]);
        Assert.Throws<ArgumentException>(() => solve.Solve([(20, 30)]));
    }

    [Fact]
    public void TheSameDragGivesTheSameVerticesToTheBit()
    {
        // Invariant 2. The pose track is replayed on load, on undo and by the
        // inbetweener; a solve that converges "close enough" along a
        // different path renders a different frame.
        var mesh = PinMesh.Build([PinShapes.Fork()], cell: 20);
        PinHandle[] pins = [new(30, 230), new(270, 230), new(30, 20), new(150, 230, PinKind.Starch, Extent: 40)];
        (double X, double Y)[] targets = [(30, 230), (285, 215), (-50, 60), (0, 0)];

        var first = PinSolve.Prepare(mesh, pins).Solve(targets);
        var again = PinSolve.Prepare(mesh, pins);
        _ = again.Solve([(30, 230), (270, 230), (30, 20), (0, 0)]);   // a different solve in between leaves no state behind
        var second = again.Solve(targets);

        for (var i = 0; i < first.Length; i++)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(first[i].X), BitConverter.DoubleToInt64Bits(second[i].X));
            Assert.Equal(BitConverter.DoubleToInt64Bits(first[i].Y), BitConverter.DoubleToInt64Bits(second[i].Y));
        }
    }
}

/// <summary>
/// Strokes riding a deformed mesh (Q239) — the bones design's rule applied to
/// pins: the points move and the brush re-stamps them, the record is never
/// touched, and every dab still seeds from where it was drawn.
/// </summary>
public class PinSkinningTests
{
    private static (PinMesh Mesh, (double X, double Y)[] Bent) BentBar()
    {
        var mesh = PinMesh.Build([PinShapes.Bar()], cell: 20);
        PinHandle[] pins = [new(20, 30), new(200, 30), new(380, 30)];
        var bent = PinSolve.Prepare(mesh, pins).Solve([(20, 30), (200, 30), (330, 160)]);
        return (mesh, bent);
    }

    [Fact]
    public void PosingMovesACopyAndCarriesTheRestPathForSeeding()
    {
        var (mesh, bent) = BentBar();
        var stroke = new Stroke { Points = [.. Enumerable.Range(0, 39).Select(i => new StrokePoint(10 + i * 10, 30, 0.7))] };
        var before = stroke.Points.ToList();

        var posed = PinSkinning.PoseStroke(stroke, mesh, bent);

        // Invariant 1: the record is exactly as it was.
        Assert.Equal(before, stroke.Points);
        Assert.Null(stroke.RestPoints);

        // The transient is the render-ready construction the bone path makes:
        // same id, moved points, and the rest path beside them one to one —
        // which is what stops a pinned drawing boiling as it moves.
        Assert.NotSame(stroke, posed);
        Assert.Equal(stroke.Id, posed.Id);
        Assert.NotNull(posed.RestPoints);
        Assert.Equal(posed.Points.Count, posed.RestPoints!.Count);
        Assert.Equal(10, posed.RestPoints[0].X, 9);
        Assert.Equal(30, posed.RestPoints[0].Y, 9);
        Assert.True(posed.Points[^1].Y > 100, "the far end of the line did not follow the wrist");
        // Six places: the curve's own interpolation carries pressure in single precision.
        Assert.All(posed.Points, p => Assert.Equal(0.7, p.Pressure, 6));
    }

    [Fact]
    public void AMeshThatHasNotMovedHandsBackTheStrokeItself()
    {
        // Pinned and at rest costs nothing: no clone, no seed path, no
        // re-render. The same promise an unbound stroke gets from the rig.
        var mesh = PinMesh.Build([PinShapes.Bar()], cell: 20);
        var stroke = new Stroke { Points = [new(10, 30, 1), new(390, 30, 1)] };

        var posed = PinSkinning.PoseStroke(stroke, mesh, mesh.Vertices.ToArray());

        Assert.Same(stroke, posed);
    }

    [Fact]
    public void AStraightTwoPointLineAcrossABendComesOutCurved()
    {
        // B404's lesson, written down before it can happen again: a line of
        // two control points posed by moving two control points is a chord.
        var (mesh, bent) = BentBar();
        var stroke = new Stroke { Points = [new(10, 30, 1), new(390, 30, 1)] };

        var posed = PinSkinning.PoseStroke(stroke, mesh, bent);

        Assert.True(posed.Points.Count > 2, "no points were added, so the line cannot bend");
        var (ax, ay) = (posed.Points[0].X, posed.Points[0].Y);
        var (bx, by) = (posed.Points[^1].X, posed.Points[^1].Y);
        var chord = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
        var offChord = posed.Points.Max(p => Math.Abs((bx - ax) * (ay - p.Y) - (ax - p.X) * (by - ay)) / chord);
        Assert.True(offChord > 10, $"the posed line is within {offChord:F2} of a straight chord");
    }
}
