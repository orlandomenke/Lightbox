using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.App.Tests;

/// <summary>
/// Reshaping a line: the session, the mode it lives in, and the promise the
/// mode makes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything here drives the view model, not the canvas.</b> Synthetic
/// pointer input through Xvfb is unreliable in this environment, so a gesture
/// that lives only in an event handler is one nothing can check — which is why
/// the canvas owns only the gesture and every decision (what was grabbed, where
/// it may go, when it becomes an undo step) is asked of methods with no window
/// behind them.
/// </para>
/// <para>
/// The properties that carry the weight: <b>isolation actually isolates</b>
/// (Illustrator's mode "locks all other objects", and one that only looks
/// isolating is worse than none), <b>the record and the path stay in
/// agreement</b> after every edit, and <b>the line keeps the mark it was drawn
/// with</b> — which includes its weight, not just its shape.
/// </para>
/// </remarks>
public class PathEditingTests(ITestOutputHelper output)
{
    /// <summary>A drawn arc with a pressure taper at each end, like a real one.</summary>
    private static Stroke Drawn(int seed = 0, double weight = 1.0)
    {
        var points = new List<StrokePoint>();
        for (var i = 0; i <= 60; i++)
        {
            var t = i / 60.0;
            // Ends light, middle heavy — the profile an artist would notice
            // losing.
            var pressure = weight * (0.15 + 0.85 * Math.Sin(t * Math.PI));
            points.Add(new StrokePoint(
                100 + seed * 40 + 200 * t,
                200 + 80 * Math.Sin(t * Math.PI),
                pressure));
        }
        return new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#000000",
            Points = points,
            Brush = new BrushSettings { Size = 10, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
        };
    }

    private static MainViewModel WithStrokes(params Stroke[] strokes)
    {
        var vm = new MainViewModel(null);
        var frame = vm.Doc.Scene.Layers[^1].Cels[0].Frame;
        Assert.NotNull(frame);
        frame.Strokes.AddRange(strokes);
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.Count - 1;
        return vm;
    }

    private static Stroke StrokeOf(MainViewModel vm, string id) =>
        vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[0].Frame!.Strokes.First(s => s.Id == id);

    // ---- getting in and out ---------------------------------------------------

    /// <summary>
    /// Q50: a line drawn before any of this existed is editable, and gets its
    /// path fitted the moment somebody asks.
    /// </summary>
    [AvaloniaFact]
    public void EnteringIsolationFitsAPathToALineThatHasNone()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        Assert.Null(line.Path);

        Assert.True(vm.BeginPathEdit(line.Id));

        Assert.True(vm.PathEditActive);
        Assert.Equal(line.Id, vm.IsolatedStrokeId);
        output.WriteLine($"{line.Points.Count} drawn points -> {vm.PathEdit!.NodeCount} nodes");
        Assert.True(vm.PathEdit.NodeCount >= 2);
    }

    /// <summary>
    /// Entering changes nothing about the drawing. Fitting adds a description;
    /// it does not replace the line, and an artist who double-clicks to look and
    /// then leaves must get their pixels back untouched.
    /// </summary>
    [AvaloniaFact]
    public void EnteringAndLeavingWithoutTouchingAnythingLeavesTheLineAlone()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        var before = line.Points.ToList();

        vm.BeginPathEdit(line.Id);
        vm.EndPathEdit();

        Assert.False(vm.PathEditActive);
        Assert.Equal(before, StrokeOf(vm, line.Id).Points);
        // And no path was written either — the fit lived in the session.
        Assert.Null(StrokeOf(vm, line.Id).Path);
    }

    [AvaloniaFact]
    public void ALineTooShortToReshapeSaysSoRatherThanOpeningAnEmptySession()
    {
        var dot = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#000000",
            Points = [new StrokePoint(50, 50, 1)],
        };
        var vm = WithStrokes(dot);

        Assert.False(vm.BeginPathEdit(dot.Id));
        Assert.False(vm.PathEditActive);
        Assert.Contains("too short", vm.AiStatus);
    }

    // ---- isolation actually isolates ------------------------------------------

    /// <summary>
    /// <b>The promise the mode makes.</b> Illustrator's isolation "automatically
    /// locks all other objects", and a mode that only looks isolating is worse
    /// than none: the artist trusts it and then edits the wrong line.
    /// </summary>
    [AvaloniaFact]
    public void WithALineIsolatedAClickOnAnotherChangesNothing()
    {
        var isolated = Drawn(seed: 0);
        var other = Drawn(seed: 3);
        var vm = WithStrokes(isolated, other);

        vm.BeginPathEdit(isolated.Id);
        var onOther = other.Points[30];

        Assert.False(vm.PickStrokeAt(onOther.X, onOther.Y, tolerance: 4));
        Assert.Empty(vm.Selection.SelectedStrokeIds);
        Assert.Equal(isolated.Id, vm.IsolatedStrokeId);
    }

    /// <summary>And picking works again the moment isolation ends.</summary>
    [AvaloniaFact]
    public void LeavingIsolationGivesTheOtherLinesBack()
    {
        var isolated = Drawn(seed: 0);
        var other = Drawn(seed: 3);
        var vm = WithStrokes(isolated, other);
        var onOther = other.Points[30];

        vm.BeginPathEdit(isolated.Id);
        vm.EndPathEdit();

        Assert.True(vm.PickStrokeAt(onOther.X, onOther.Y, tolerance: 4));
        Assert.Equal([other.Id], vm.Selection.SelectedStrokeIds);
    }

    /// <summary>
    /// Entering drops the whole-stroke selection, or two highlights fight over
    /// the same line — the outline around it and the nodes inside it.
    /// </summary>
    [AvaloniaFact]
    public void EnteringIsolationLetsGoOfTheWholeLineSelection()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        vm.PickStrokeAt(line.Points[30].X, line.Points[30].Y, tolerance: 4);
        Assert.True(vm.HasStrokeSelection);

        vm.BeginPathEdit(line.Id);

        Assert.False(vm.HasStrokeSelection);
    }

    // ---- hit testing ----------------------------------------------------------

    [AvaloniaFact]
    public void AClickOnANodeGrabsThatNode()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        vm.BeginPathEdit(line.Id);
        var node = vm.PathEdit!.Path.Nodes[1];

        var hit = vm.GrabPathPart(node.X, node.Y, tolerance: 6);

        Assert.Equal(PathPart.Node, hit.Part);
        Assert.Equal(1, hit.Node);
        Assert.True(vm.PathEdit.IsNodeSelected(1));
    }

    /// <summary>
    /// Handles belong to selected nodes only, and win over nodes when they are
    /// shown. Both halves are load-bearing — see <c>PathEditSession.HitTest</c>.
    /// </summary>
    [AvaloniaFact]
    public void AHandleIsOnlyGrabbableOnceItsNodeIsSelected()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        vm.BeginPathEdit(line.Id);

        var index = 1;
        var node = vm.PathEdit!.Path.Nodes[index];
        Assert.True(node.OutX != 0 || node.OutY != 0, "the fitted node has no out handle to test with");
        var (hx, hy) = (node.X + node.OutX, node.Y + node.OutY);

        // Nothing selected: the handle is not drawn, so it must not be grabbable.
        var before = vm.PathEditHitTest(hx, hy, tolerance: 6);
        Assert.NotEqual(PathPart.Out, before.Part);

        vm.GrabPathPart(node.X, node.Y, tolerance: 6);
        var after = vm.PathEditHitTest(hx, hy, tolerance: 6);

        Assert.Equal(PathPart.Out, after.Part);
        Assert.Equal(index, after.Node);
    }

    [AvaloniaFact]
    public void AClickOnNothingLetsTheNodesGoWithoutLeavingTheMode()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        vm.BeginPathEdit(line.Id);
        vm.GrabPathPart(vm.PathEdit!.Path.Nodes[1].X, vm.PathEdit.Path.Nodes[1].Y, tolerance: 6);
        Assert.NotEmpty(vm.PathEdit.SelectedNodes);

        var miss = vm.GrabPathPart(900, 500, tolerance: 6);

        Assert.False(miss.IsHit);
        Assert.Empty(vm.PathEdit.SelectedNodes);
        Assert.True(vm.PathEditActive);
    }

    // ---- pinching a segment (phase 4) ------------------------------------------

    /// <summary>
    /// Nodes and handles are both <em>on</em> the curve, so a segment that won
    /// the hit test would make them unclickable. Last in the enum, last in the
    /// test, and asserted rather than assumed.
    /// </summary>
    [AvaloniaFact]
    public void ANodeStillWinsOverTheCurveItSitsOn()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        vm.BeginPathEdit(line.Id);
        var node = vm.PathEdit!.Path.Nodes[1];

        var hit = vm.PathEditHitTest(node.X, node.Y, tolerance: 6);

        Assert.Equal(PathPart.Node, hit.Part);
    }

    [AvaloniaFact]
    public void AClickOnTheCurveBetweenNodesGrabsTheSegment()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        vm.BeginPathEdit(line.Id);
        var path = vm.PathEdit!.Path;
        var (x, y) = Lightbox.Core.Geometry.PathFlattener.PointOn(path.Nodes[0], path.Nodes[1], 0.5);

        var hit = vm.GrabPathPart(x, y, tolerance: 6);
        output.WriteLine($"grabbed the curve at ({x:F2}, {y:F2}) -> segment {hit.Node}, t {hit.T:F3}");

        Assert.Equal(PathPart.Segment, hit.Part);
        Assert.Equal(0, hit.Node);
        Assert.InRange(hit.T, 0.3, 0.7);

        // And it selects nothing: the gesture is "put the line here", so an
        // artist can pull a segment without losing the points they had picked.
        Assert.Empty(vm.PathEdit.SelectedNodes);
    }

    /// <summary>
    /// <b>The promise of the gesture, through the real drag path.</b> The line
    /// arrives where it was pulled and the nodes either side stay exactly where
    /// they were put — which is what separates this from dragging a node, and
    /// the reason an artist reaches for it.
    /// </summary>
    [AvaloniaFact]
    public void PullingTheCurveMovesItWithoutMovingTheNodes()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        vm.BeginPathEdit(line.Id);
        var path = vm.PathEdit!.Path;
        var (before, after) = (path.Nodes[0], path.Nodes[1]);
        var (x, y) = Lightbox.Core.Geometry.PathFlattener.PointOn(before, after, 0.5);

        var grab = vm.GrabPathPart(x, y, tolerance: 6);
        for (var i = 0; i < 20; i++) vm.DragPathPart(grab, x, y + i + 1, 0, 1);
        Assert.True(vm.CommitPathEdit());

        var moved = Lightbox.Core.Geometry.PathFlattener.PointOn(
            vm.PathEdit.Path.Nodes[0], vm.PathEdit.Path.Nodes[1], grab.T);
        output.WriteLine($"curve at t={grab.T:F3}: ({x:F2}, {y:F2}) -> ({moved.X:F2}, {moved.Y:F2})");

        Assert.Equal(y + 20, moved.Y, 3);
        Assert.Equal(before.X, vm.PathEdit.Path.Nodes[0].X, 6);
        Assert.Equal(before.Y, vm.PathEdit.Path.Nodes[0].Y, 6);
        Assert.Equal(after.X, vm.PathEdit.Path.Nodes[1].X, 6);
        Assert.Equal(after.Y, vm.PathEdit.Path.Nodes[1].Y, 6);
    }

    /// <summary>
    /// A pinch writes through the same one committer as every other edit, so
    /// <see cref="StrokePath"/>'s invariant holds here without a second rule to
    /// remember.
    /// </summary>
    [AvaloniaFact]
    public void APinchLeavesThePathAndThePointsAgreeing()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        vm.BeginPathEdit(line.Id);
        var path = vm.PathEdit!.Path;
        var (x, y) = Lightbox.Core.Geometry.PathFlattener.PointOn(path.Nodes[0], path.Nodes[1], 0.5);

        var grab = vm.GrabPathPart(x, y, tolerance: 6);
        vm.DragPathPart(grab, x, y + 25, 0, 25);
        Assert.True(vm.CommitPathEdit());

        var stroke = StrokeOf(vm, line.Id);
        var reflattened = Lightbox.Core.Geometry.PathFlattener.Flatten(stroke.Path!);
        var weighted = Lightbox.Core.Geometry.PressureProfile.Of(reflattened);
        Assert.NotNull(weighted);
        Assert.Equal(stroke.Points.Count, reflattened.Count);

        var worst = 0.0;
        for (var i = 0; i < reflattened.Count; i++)
        {
            worst = Math.Max(worst, Math.Abs(reflattened[i].X - stroke.Points[i].X));
            worst = Math.Max(worst, Math.Abs(reflattened[i].Y - stroke.Points[i].Y));
        }
        output.WriteLine($"path and points differ by at most {worst:F4} px after a pinch");
        Assert.True(worst < 1e-9, $"path and points disagree by {worst} px");
    }

    /// <summary>
    /// A drag is one undo step whatever it was dragging — the transform
    /// session's rule, and a pinch is hundreds of pointer moves like any other.
    /// </summary>
    [AvaloniaFact]
    public void APinchIsOneUndoStepAndUndoingItRestoresTheLineExactly()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        var original = line.Points.Select(p => (p.X, p.Y, p.Pressure)).ToList();
        vm.BeginPathEdit(line.Id);
        var path = vm.PathEdit!.Path;
        var (x, y) = Lightbox.Core.Geometry.PathFlattener.PointOn(path.Nodes[0], path.Nodes[1], 0.5);

        var grab = vm.GrabPathPart(x, y, tolerance: 6);
        for (var i = 0; i < 30; i++) vm.DragPathPart(grab, x, y + i + 1, 0, 1);
        vm.CommitPathEdit();
        Assert.NotEqual(original[0].Y + 0, StrokeOf(vm, line.Id).Points.Max(p => p.Y));

        vm.UndoCommand.Execute(null);

        var back = StrokeOf(vm, line.Id).Points;
        Assert.Equal(original.Count, back.Count);
        for (var i = 0; i < back.Count; i++)
        {
            // Bit-exact, not close: every dab dynamic is seeded from the bits of
            // a coordinate, so a line restored to visibly the right place with
            // recomputed numbers comes back with a different grain.
            Assert.Equal(original[i].X, back[i].X);
            Assert.Equal(original[i].Y, back[i].Y);
            Assert.Equal(original[i].Pressure, back[i].Pressure);
        }
    }

    // ---- simplify (phase 4c) ---------------------------------------------------

    /// <summary>
    /// <b>The fitter with its tolerance turned up, and the count is the feature
    /// as much as the refit is.</b> "Simplify" with no number is a button an
    /// artist presses and then squints at the canvas to find out what it did.
    /// </summary>
    [AvaloniaFact]
    public void SimplifyingLeavesFewerPointsAndSaysHowMany()
    {
        // A wobbly line, so there is something to take out. A clean arc already
        // fits in four nodes and would make this test pass for the wrong reason.
        var points = new List<StrokePoint>();
        for (var i = 0; i <= 200; i++)
        {
            var t = i / 200.0;
            points.Add(new StrokePoint(
                100 + 300 * t,
                200 + 60 * Math.Sin(t * Math.PI * 3) + 3 * Math.Sin(t * 90),
                1));
        }
        var line = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#000000",
            Points = points,
            Brush = new BrushSettings { Size = 10, Opacity = 1, Flow = 1, Spacing = 0.2 },
        };

        var vm = WithStrokes(line);
        vm.BeginPathEdit(line.Id);
        var before = vm.PathEdit!.NodeCount;

        vm.SimplifyLineCommand.Execute(null);
        var after = vm.PathEdit!.NodeCount;
        output.WriteLine($"{before} points -> {after};  status: {vm.AiStatus}");

        Assert.True(after < before, $"simplify left {after} of {before}");
        Assert.Contains(before.ToString(), vm.AiStatus);
        Assert.Contains(after.ToString(), vm.AiStatus);
    }

    /// <summary>
    /// Each press is its own undo step, so one too many costs one Ctrl+Z rather
    /// than the whole line — and the undo restores the drawn points exactly.
    /// </summary>
    [AvaloniaFact]
    public void EachSimplifyIsItsOwnUndoStep()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        var original = line.Points.Select(p => (p.X, p.Y)).ToList();
        vm.BeginPathEdit(line.Id);

        vm.SimplifyLineCommand.Execute(null);
        var afterOne = StrokeOf(vm, line.Id).Points.Count;
        vm.SimplifyLineCommand.Execute(null);
        output.WriteLine($"after one press {afterOne} points, after two {StrokeOf(vm, line.Id).Points.Count}");

        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null);

        var back = StrokeOf(vm, line.Id).Points;
        Assert.Equal(original.Count, back.Count);
        for (var i = 0; i < back.Count; i++)
        {
            Assert.Equal(original[i].X, back[i].X);
            Assert.Equal(original[i].Y, back[i].Y);
        }
    }

    /// <summary>
    /// A shape that cannot be described with fewer nodes says so rather than
    /// silently doing nothing, and does not spend an undo step on it.
    /// </summary>
    [AvaloniaFact]
    public void AShapeThatCannotBeSimplifiedFurtherSaysSo()
    {
        var straight = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#000000",
            Points = [.. Enumerable.Range(0, 40).Select(i => new StrokePoint(100 + i * 6, 200, 1))],
            Brush = new BrushSettings { Size = 10, Opacity = 1, Flow = 1, Spacing = 0.2 },
        };
        var vm = WithStrokes(straight);
        vm.BeginPathEdit(straight.Id);
        Assert.Equal(2, vm.PathEdit!.NodeCount);

        vm.SimplifyLineCommand.Execute(null);

        output.WriteLine(vm.AiStatus);
        Assert.Contains("Cannot simplify", vm.AiStatus);
        Assert.Equal(2, vm.PathEdit!.NodeCount);
        Assert.Null(StrokeOf(vm, straight.Id).Path);
    }

    /// <summary>
    /// <b>Simplify must not also mean "flatten the pressure".</b> The weight is
    /// a function of arc length rather than of node count, so a path with fewer
    /// nodes covering the same line reads the same taper off it.
    /// </summary>
    [AvaloniaFact]
    public void SimplifyingKeepsTheWeightTheLineWasDrawnWith()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        var peak = line.Points.Max(p => p.Pressure);
        vm.BeginPathEdit(line.Id);

        vm.SimplifyLineCommand.Execute(null);

        var after = StrokeOf(vm, line.Id).Points.Max(p => p.Pressure);
        output.WriteLine($"peak pressure {peak:F3} before, {after:F3} after");
        Assert.Equal(peak, after, 2);
    }

    /// <summary>
    /// Refits the line as it is now, not the points it was drawn from — by the
    /// time somebody simplifies, the line may have been reshaped, and going back
    /// to the drawn points would silently undo that under a button labelled
    /// "simplify".
    /// </summary>
    [AvaloniaFact]
    public void SimplifyingRefitsTheLineAsItIsNowRatherThanAsItWasDrawn()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        vm.BeginPathEdit(line.Id);

        // Move a node a long way, commit, then simplify.
        var node = vm.PathEdit!.Path.Nodes[1];
        var grab = vm.GrabPathPart(node.X, node.Y, tolerance: 6);
        vm.DragPathPart(grab, node.X, node.Y - 120, 0, -120);
        vm.CommitPathEdit();
        var movedTo = vm.PathEdit!.Path.Nodes[1].Y;

        vm.SimplifyLineCommand.Execute(null);

        var top = StrokeOf(vm, line.Id).Points.Min(p => p.Y);
        output.WriteLine($"node dragged to y {movedTo:F1}; after simplify the line still reaches y {top:F1}");
        Assert.True(top < node.Y - 60, $"the reshape was undone by the simplify (top {top}, node was {node.Y})");
    }

    [AvaloniaFact]
    public void SimplifyIsRegisteredSoItCanBeBound()
    {
        var map = new Lightbox.App.Services.ShortcutMap();
        var entry = map.Definitions.SingleOrDefault(d => d.Id == "lines.simplify");

        Assert.NotNull(entry);
        Assert.Equal("Tools", entry!.Category);
        // Unbound by default is allowed and is not an oversight — the sensible
        // letters are taken. Being in the registry is what makes it bindable.
        Assert.Null(entry.Default);
    }

    // ---- width along the line (phase 4b) ---------------------------------------

    /// <summary>
    /// <b>Illustrator's Width tool over an array the format has always had.</b>
    /// A Lightbox stroke is a centreline with a width at every point, so the
    /// tool edits a number that is already there rather than adding one — and
    /// what it must edit is the <em>weight</em>, because the flatten regenerates
    /// the points on every commit and would throw away anything written into
    /// them.
    /// </summary>
    [AvaloniaFact]
    public void DraggingOffTheLineMakesItHeavierAndTowardsItLighter()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        vm.BeginPathEdit(line.Id);
        var session = vm.PathEdit!;

        // A place on the line that is not already at full weight, or the clamp
        // would be what the test measured.
        var points = Lightbox.Core.Geometry.PathFlattener.Flatten(session.Path);
        var grabbed = points[points.Count / 6];
        var at = vm.GrabWidthAt(grabbed.X, grabbed.Y, tolerance: 8);
        Assert.True(at >= 0, "the line could not be grabbed");

        var before = session.Weight!.At(at);
        vm.DragWidth(at, grabbed.X, grabbed.Y - 30);
        var after = session.Weight!.At(at);
        output.WriteLine($"weight at {at:F3}: {before:F3} -> {after:F3}");

        Assert.True(after > before, $"dragging off the line did not fatten it ({before} -> {after})");
    }

    /// <summary>
    /// Measured against where the press landed rather than against the line, so
    /// grabbing from a few pixels away does not jump the weight before the hand
    /// has moved.
    /// </summary>
    [AvaloniaFact]
    public void TheWeightDoesNotJumpWhenTheLineIsGrabbedFromASmallDistance()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        vm.BeginPathEdit(line.Id);
        var session = vm.PathEdit!;

        var points = Lightbox.Core.Geometry.PathFlattener.Flatten(session.Path);
        var grabbed = points[points.Count / 3];
        var at = vm.GrabWidthAt(grabbed.X, grabbed.Y + 5, tolerance: 8);
        Assert.True(at >= 0);

        var before = (session.Weight ?? Lightbox.Core.Geometry.PressureProfile.Uniform()).At(at);
        vm.DragWidth(at, grabbed.X, grabbed.Y + 5);

        Assert.Equal(before, session.Weight!.At(at), 6);
    }

    /// <summary>
    /// A width drag is hundreds of pointer moves and one undo step, and undoing
    /// it restores the original pressures bit for bit — a recomputed undo would
    /// bring the line back with a different grain.
    /// </summary>
    [AvaloniaFact]
    public void AWidthDragIsOneUndoStepAndRestoresTheOriginalWeight()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        var original = line.Points.Select(p => p.Pressure).ToList();
        vm.BeginPathEdit(line.Id);

        var points = Lightbox.Core.Geometry.PathFlattener.Flatten(vm.PathEdit!.Path);
        var grabbed = points[points.Count / 4];
        var at = vm.GrabWidthAt(grabbed.X, grabbed.Y, tolerance: 8);
        for (var i = 1; i <= 20; i++) vm.DragWidth(at, grabbed.X, grabbed.Y - i);
        Assert.True(vm.EndWidthDrag());

        var fattened = StrokeOf(vm, line.Id).Points.Max(p => p.Pressure);
        output.WriteLine($"peak pressure after the drag {fattened:F3}, originally {original.Max():F3}");

        vm.UndoCommand.Execute(null);
        var back = StrokeOf(vm, line.Id).Points;
        Assert.Equal(original.Count, back.Count);
        for (var i = 0; i < back.Count; i++) Assert.Equal(original[i], back[i].Pressure);
    }

    /// <summary>
    /// A pen line was never pressed, so there is no profile to read. Starting it
    /// flat at full weight makes the first drag an edit rather than a jump from
    /// nothing.
    /// </summary>
    [AvaloniaFact]
    public void AnAuthoredLineWithNoPressureCanStillBeGivenWidth()
    {
        var line = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#000000",
            Brush = new BrushSettings { Size = 10, Opacity = 1, Flow = 1, Spacing = 0.2 },
            Path = new StrokePath
            {
                Nodes = [PathNode.At(100, 200), PathNode.At(400, 200)],
            },
        };
        line.Points = Lightbox.Core.Geometry.PathFlattener.Flatten(line.Path);

        var vm = WithStrokes(line);
        vm.BeginPathEdit(line.Id);
        // A two-point authored line does have a profile — a flat one, read off
        // its own points. What it does not have is anywhere to put a local
        // change, which is what the resample-up is for.
        Assert.Equal(2, vm.PathEdit!.Weight!.SampleCount);

        var at = vm.GrabWidthAt(250, 200, tolerance: 8);
        Assert.True(at >= 0);
        for (var i = 1; i <= 15; i++) vm.DragWidth(at, 250, 200 - i);
        Assert.True(vm.EndWidthDrag());

        var pressures = StrokeOf(vm, line.Id).Points.Select(p => p.Pressure).ToList();
        output.WriteLine($"authored line pressures now {pressures.Min():F3}..{pressures.Max():F3}");
        // The ends are untouched at full weight and the middle was pushed up
        // against the clamp, so what shows is the *ends* having been left alone
        // — an edit that leaked would have moved them.
        Assert.Equal(1.0, pressures[0], 3);
        Assert.Equal(1.0, pressures[^1], 3);
    }

    /// <summary>
    /// The width tool's key is in the one registry the shortcut editor reads,
    /// and does not shadow anything already bound.
    /// </summary>
    [AvaloniaFact]
    public void TheWidthToolHasABindableShortcutThatCollidesWithNothing()
    {
        var map = new Lightbox.App.Services.ShortcutMap();
        var entry = map.Definitions.SingleOrDefault(d => d.Id == "tool.width");

        Assert.NotNull(entry);
        Assert.Equal("Tools", entry!.Category);
        Assert.NotNull(entry.Default);

        var clashes = map.Definitions
            .Where(d => d.Id != entry.Id && d.Default is { } g
                && g.Key == entry.Default!.Key && g.KeyModifiers == entry.Default.KeyModifiers)
            .Select(d => d.Id)
            .ToList();
        Assert.True(clashes.Count == 0, $"tool.width collides with {string.Join(", ", clashes)}");
    }

    /// <summary>
    /// The width tool works inside the isolation session, so reaching for it
    /// from the white arrow must not throw the session away — the same pairing
    /// rule B149 established for the two arrows.
    /// </summary>
    [AvaloniaFact]
    public void ReachingForTheWidthToolKeepsTheIsolationSession()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        vm.ActiveTool = ToolId.Arrow;
        Assert.True(vm.BeginPathEdit(line.Id));

        vm.ActiveTool = ToolId.Width;
        Assert.True(vm.PathEditActive);

        vm.ActiveTool = ToolId.Brush;
        Assert.False(vm.PathEditActive);
    }

    // ---- editing --------------------------------------------------------------

    /// <summary>
    /// The point of the whole phase: drag a node and the drawing follows.
    /// </summary>
    [AvaloniaFact]
    public void DraggingANodeMovesTheLineAndCommitsOnce()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        vm.BeginPathEdit(line.Id);
        var before = StrokeOf(vm, line.Id).Points.ToList();
        var node = vm.PathEdit!.Path.Nodes[1];

        var grab = vm.GrabPathPart(node.X, node.Y, tolerance: 6);
        // Three pointer moves, as a real drag would be.
        vm.DragPathPart(grab, node.X + 10, node.Y - 20, 10, -20);
        vm.DragPathPart(grab, node.X + 20, node.Y - 40, 10, -20);
        vm.DragPathPart(grab, node.X + 30, node.Y - 60, 10, -20);
        Assert.True(vm.CommitPathEdit());

        var after = StrokeOf(vm, line.Id);
        Assert.NotEqual(before, after.Points);
        Assert.NotNull(after.Path);
        output.WriteLine($"{before.Count} points before, {after.Points.Count} after");

        // One undo step for the whole drag, not three.
        vm.UndoCommand.Execute(null);
        Assert.Equal(before, StrokeOf(vm, line.Id).Points);
    }

    /// <summary>
    /// Alt+drag on a point is the pen's drag-to-curve gesture, offered again
    /// inside isolation: the point stays put and mirrored handles reach for the
    /// pointer, so a corner becomes a curve in one drag with no menu.
    /// </summary>
    [AvaloniaFact]
    public void AltDraggingAPointPullsACurveOutOfItInsteadOfMovingIt()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        vm.BeginPathEdit(line.Id);
        var node = vm.PathEdit!.Path.Nodes[1];

        var grab = vm.GrabPathPart(node.X, node.Y, tolerance: 6);
        Assert.Equal(PathPart.Node, grab.Part);
        vm.DragPathPart(grab, node.X + 30, node.Y - 40, 30, -40, breakPair: true);

        var curved = vm.PathEdit.Path.Nodes[1];
        output.WriteLine(
            $"node stayed at ({curved.X:F1}, {curved.Y:F1}), out ({curved.OutX:F1}, {curved.OutY:F1})");

        // The point did not move — the handles did.
        Assert.Equal(node.X, curved.X, 6);
        Assert.Equal(node.Y, curved.Y, 6);
        // Reaching for the pointer, mirrored, because both handles are being
        // created by this one gesture — the same rule the pen's drag uses.
        Assert.Equal(30, curved.OutX, 3);
        Assert.Equal(-40, curved.OutY, 3);
        Assert.Equal(-curved.OutX, curved.InX, 3);
        Assert.Equal(-curved.OutY, curved.InY, 3);
        Assert.False(curved.Corner);
        Assert.True(vm.PathEdit.Dirty);
    }

    /// <summary>
    /// <b>The record's invariant, at the one place phase 2 can break it.</b> A
    /// commit writes the path and the points together, so flattening what was
    /// stored has to reproduce what was stored.
    /// </summary>
    [AvaloniaFact]
    public void AfterACommitThePathAndThePointsStillAgree()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        vm.BeginPathEdit(line.Id);
        var node = vm.PathEdit!.Path.Nodes[1];
        var grab = vm.GrabPathPart(node.X, node.Y, tolerance: 6);
        vm.DragPathPart(grab, node.X + 30, node.Y - 60, 30, -60);
        vm.CommitPathEdit();

        var stroke = StrokeOf(vm, line.Id);
        var reflattened = PathFlattener.Flatten(stroke.Path!);

        var worst = stroke.Points.Max(p => reflattened.Min(q =>
            Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y))));
        output.WriteLine($"path and points differ by at most {worst:0.0000} px");
        Assert.True(worst < 1e-6, $"path and points disagree by {worst} px");
    }

    /// <summary>
    /// Undo restores the path as well as the points. Restoring one and not the
    /// other leaves them disagreeing, which renders correctly and then jumps the
    /// next time a node is dragged.
    /// </summary>
    [AvaloniaFact]
    public void UndoPutsBackThePathAsWellAsThePoints()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        vm.BeginPathEdit(line.Id);
        var node = vm.PathEdit!.Path.Nodes[1];
        var grab = vm.GrabPathPart(node.X, node.Y, tolerance: 6);
        vm.DragPathPart(grab, node.X + 30, node.Y - 60, 30, -60);
        vm.CommitPathEdit();

        vm.UndoCommand.Execute(null);

        var stroke = StrokeOf(vm, line.Id);
        Assert.Null(stroke.Path);
        Assert.Equal(line.Points.Count, stroke.Points.Count);
    }

    /// <summary>
    /// <b>Reshaping keeps the weight, not only the shape.</b> The roadmap item
    /// this serves says "keeps the mark it was drawn with", and pressure is part
    /// of the mark — the part an animator notices first, because it is what makes
    /// a line look drawn rather than plotted.
    /// </summary>
    /// <remarks>
    /// Without <c>PressureProfile</c> this fails loudly: the fit samples pressure
    /// only where its handful of nodes landed, so re-flattening turns a taper
    /// into three straight ramps. Measured as the spread of the profile rather
    /// than point-by-point, because the points move — that is the whole exercise.
    /// </remarks>
    [AvaloniaFact]
    public void ReshapingKeepsTheWeightTheLineWasDrawnWith()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        var beforeMin = line.Points.Min(p => p.Pressure);
        var beforeMax = line.Points.Max(p => p.Pressure);

        vm.BeginPathEdit(line.Id);
        var node = vm.PathEdit!.Path.Nodes[1];
        var grab = vm.GrabPathPart(node.X, node.Y, tolerance: 6);
        vm.DragPathPart(grab, node.X + 15, node.Y - 25, 15, -25);
        vm.CommitPathEdit();

        var after = StrokeOf(vm, line.Id).Points;
        var afterMin = after.Min(p => p.Pressure);
        var afterMax = after.Max(p => p.Pressure);

        output.WriteLine(
            $"pressure before {beforeMin:0.00}-{beforeMax:0.00}, after {afterMin:0.00}-{afterMax:0.00}");

        // The taper survives: still light at the ends, still heavy in the middle.
        Assert.InRange(afterMin, beforeMin - 0.05, beforeMin + 0.05);
        Assert.InRange(afterMax, beforeMax - 0.05, beforeMax + 0.05);
        // And it is not merely constant — a flat profile would also satisfy a
        // sloppier assertion on the mean.
        Assert.True(afterMax - afterMin > 0.5, "the pressure taper collapsed");
    }

    /// <summary>
    /// A press that grabs nothing writes no history. Without this, clicking
    /// about inside isolation fills the undo stack with identity edits.
    /// </summary>
    [AvaloniaFact]
    public void APressThatMovedNothingCommitsNothing()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        vm.BeginPathEdit(line.Id);
        var node = vm.PathEdit!.Path.Nodes[1];

        vm.GrabPathPart(node.X, node.Y, tolerance: 6);

        Assert.False(vm.CommitPathEdit());
        Assert.Null(StrokeOf(vm, line.Id).Path);
    }

    // ---- corners and handles --------------------------------------------------

    /// <summary>
    /// A smooth node's other handle keeps its own length and only shares the
    /// direction — which is what "smooth" means. Mirroring outright moves the
    /// half of the curve the artist was not adjusting.
    /// </summary>
    [AvaloniaFact]
    public void DraggingOneHandleOfASmoothNodeSwingsTheOtherWithoutResizingIt()
    {
        var session = SessionOnASmoothNode(out var index);
        var node = session.Path.Nodes[index];
        var inLengthBefore = Math.Sqrt(node.InX * node.InX + node.InY * node.InY);

        Assert.True(session.MoveHandleTo(index, PathPart.Out, node.X + 40, node.Y + 40));

        var after = session.Path.Nodes[index];
        var inLengthAfter = Math.Sqrt(after.InX * after.InX + after.InY * after.InY);
        var outLength = Math.Sqrt(after.OutX * after.OutX + after.OutY * after.OutY);

        // Length preserved on the far side...
        Assert.Equal(inLengthBefore, inLengthAfter, 6);
        // ...and the two are opposite, so the curve does not kink.
        var dot = (after.InX * after.OutX + after.InY * after.OutY) / (inLengthAfter * outLength);
        Assert.True(dot < -0.999, $"handles are not opposite: dot {dot:0.0000}");
    }

    [AvaloniaFact]
    public void AltOnAHandleBreaksThePairAndMakesTheNodeACorner()
    {
        var session = SessionOnASmoothNode(out var index);
        var node = session.Path.Nodes[index];
        var inBefore = (node.InX, node.InY);

        session.MoveHandleTo(index, PathPart.Out, node.X + 40, node.Y + 40, breakPair: true);

        var after = session.Path.Nodes[index];
        Assert.True(after.Corner);
        Assert.Equal(inBefore, (after.InX, after.InY));
    }

    /// <summary>
    /// A handle cannot be flung across the canvas. The flatten's sample count
    /// grows with the square root of a handle's reach, so an accident turns one
    /// segment into its ceiling and a visibly faceted curve.
    /// </summary>
    [AvaloniaFact]
    public void AHandleCannotBeDraggedArbitrarilyFarFromItsNode()
    {
        var session = SessionOnASmoothNode(out var index);
        var node = session.Path.Nodes[index];

        session.MoveHandleTo(index, PathPart.Out, node.X + 100000, node.Y);

        var after = session.Path.Nodes[index];
        var reach = Math.Sqrt(after.OutX * after.OutX + after.OutY * after.OutY);
        output.WriteLine($"handle clamped to {reach:0.0} px");
        Assert.True(reach < 100000, "the handle was not clamped at all");
        Assert.True(double.IsFinite(reach));
    }

    [AvaloniaFact]
    public void MovingANodeCarriesItsHandlesWithIt()
    {
        var session = SessionOnASmoothNode(out var index);
        var before = session.Path.Nodes[index];

        session.MoveNode(index, 25, -15);

        var after = session.Path.Nodes[index];
        Assert.Equal(before.X + 25, after.X, 6);
        Assert.Equal(before.InX, after.InX, 6);
        Assert.Equal(before.OutY, after.OutY, 6);
    }

    [AvaloniaFact]
    public void EscapeAfterACommitKeepsTheCommittedShape()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        vm.BeginPathEdit(line.Id);
        var node = vm.PathEdit!.Path.Nodes[1];
        var grab = vm.GrabPathPart(node.X, node.Y, tolerance: 6);
        vm.DragPathPart(grab, node.X + 30, node.Y - 60, 30, -60);
        vm.CommitPathEdit();
        var committed = StrokeOf(vm, line.Id).Points.ToList();

        vm.AbandonPathEdit();

        Assert.Equal(committed, StrokeOf(vm, line.Id).Points);
    }

    // ---- registration ---------------------------------------------------------

    /// <summary>
    /// The white arrow's key is in the one registry the shortcut editor reads. A
    /// tool wired straight to a key works and cannot be seen, searched or
    /// rebound — which is the failure that registry exists for.
    /// </summary>
    [AvaloniaFact]
    public void TheWhiteArrowHasABindableShortcut()
    {
        var map = new Lightbox.App.Services.ShortcutMap();
        var entry = map.Definitions.SingleOrDefault(d => d.Id == "tool.directselect");

        Assert.NotNull(entry);
        Assert.Equal("Tools", entry!.Category);
        Assert.NotNull(entry.Default);

        // And it does not shadow the black arrow, which is the collision the
        // design's own key table would have caused.
        var arrow = map.Definitions.Single(d => d.Id == "tool.arrow");
        Assert.NotEqual(arrow.Default!.Key, entry.Default!.Key);
    }

    // ---- B172: the white arrow on its own -------------------------------------

    /// <summary>
    /// A single click with the white arrow enters the line under it.
    /// </summary>
    /// <remarks>
    /// It could not, and nothing else in that tool's path could either: the
    /// only route into isolation was the <em>black</em> arrow's double-click,
    /// so the white arrow was inert unless another tool had opened the line
    /// first. This drives <c>GrabPathPart</c> because that is what the canvas
    /// calls — testing <c>BeginPathEdit</c> instead would pass on the broken
    /// build, which is the whole point of the anchor being here.
    /// </remarks>
    [AvaloniaFact]
    public void TheWhiteArrowEntersAPathByClickingIt()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        vm.ActiveTool = ToolId.DirectSelect;
        Assert.False(vm.PathEditActive);

        var on = line.Points[30];
        var hit = vm.GrabPathPart(on.X, on.Y, tolerance: 6);

        Assert.True(vm.PathEditActive);
        Assert.Equal(line.Id, vm.IsolatedStrokeId);
        // …and the same click takes hold of what was under it, so the first
        // drag is not swallowed by the click that made it possible.
        Assert.True(hit.IsHit);
    }

    [AvaloniaFact]
    public void ClickingEmptyCanvasWithTheWhiteArrowEntersNothing()
    {
        var vm = WithStrokes(Drawn());
        vm.ActiveTool = ToolId.DirectSelect;

        var hit = vm.GrabPathPart(20, 20, tolerance: 6);

        Assert.False(hit.IsHit);
        Assert.False(vm.PathEditActive);
    }

    [AvaloniaFact]
    public void TheWhiteArrowPicksAPointWithoutLosingTheOthers()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        vm.ActiveTool = ToolId.DirectSelect;

        var on = line.Points[30];
        vm.GrabPathPart(on.X, on.Y, tolerance: 6);
        var session = vm.PathEdit;
        Assert.NotNull(session);

        var node = session!.Path.Nodes[0];
        vm.GrabPathPart(node.X, node.Y, tolerance: 6);

        // Narrowing the selection to one point must not take the others off
        // screen: the overlay draws the isolated path's whole node list, and
        // an artist reshaping a curve is looking at its neighbours.
        Assert.True(session.IsNodeSelected(0));
        Assert.True(session.NodeCount > 1);
        output.WriteLine($"{session.NodeCount} nodes, selected 0");
    }

    // ---- roadmap: hover previews the geometry ---------------------------------

    /// <summary>
    /// Hovering a line with the white arrow shows its points before any click.
    /// </summary>
    /// <remarks>
    /// The first piece of the vector-selection roadmap item, and first because
    /// it is what makes the rest discoverable: until the geometry is visible
    /// before a click, every other gesture in that item is guesswork.
    /// </remarks>
    [AvaloniaFact]
    public void HoveringALinePreviewsItsPointsAndHandles()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        vm.ActiveTool = ToolId.DirectSelect;

        var on = line.Points[30];
        Assert.True(vm.HoverPathAt(on.X, on.Y, tolerance: 6));

        Assert.NotNull(vm.PathHover);
        Assert.Equal(line.Id, vm.PathHover!.StrokeId);
        Assert.True(vm.PathHover.Path.Nodes.Count > 1);
        // A preview only. Hovering must not enter the line — that is what the
        // click is for, and a mode you fall into by moving the mouse is the
        // "mushy" tool Q53 rejected.
        Assert.False(vm.PathEditActive);
    }

    [AvaloniaFact]
    public void HoveringAwayFromEveryLineShowsNothing()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        vm.ActiveTool = ToolId.DirectSelect;

        var on = line.Points[30];
        vm.HoverPathAt(on.X, on.Y, tolerance: 6);
        Assert.NotNull(vm.PathHover);

        Assert.True(vm.HoverPathAt(20, 20, tolerance: 6));
        Assert.Null(vm.PathHover);
    }

    /// <summary>
    /// Moving along a line the preview already shows costs nothing.
    /// </summary>
    /// <remarks>
    /// A hover fires on every pointer event and the fit runs over every point
    /// of the stroke, so refitting per event would be work proportional to a
    /// stroke's length in a per-event path — the shape invariant 6 rules out.
    /// The report is the changed flag, which is what the canvas repaints on.
    /// </remarks>
    [AvaloniaFact]
    public void HoveringAlongTheSameLineDoesNotRefitIt()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        vm.ActiveTool = ToolId.DirectSelect;

        Assert.True(vm.HoverPathAt(line.Points[30].X, line.Points[30].Y, 6));
        var first = vm.PathHover!.Path;

        for (var i = 25; i < 36; i++)
        {
            Assert.False(
                vm.HoverPathAt(line.Points[i].X, line.Points[i].Y, 6),
                "moving along the same line reported a change");
        }
        Assert.Same(first, vm.PathHover!.Path);
    }

    [AvaloniaFact]
    public void IsolationOwnsTheOverlayRatherThanTheHover()
    {
        // One channel, one owner: previewing a neighbour while its nodes are
        // being dragged would draw two sets of points and say nothing about
        // which the next click belongs to.
        var a = Drawn(seed: 0);
        var b = Drawn(seed: 3);
        var vm = WithStrokes(a, b);
        vm.ActiveTool = ToolId.DirectSelect;

        Assert.True(vm.BeginPathEdit(a.Id));
        Assert.False(vm.HoverPathAt(b.Points[30].X, b.Points[30].Y, 6));
        Assert.Null(vm.PathHover);
    }

    [AvaloniaFact]
    public void LeavingTheWhiteArrowDropsThePreview()
    {
        var line = Drawn();
        var vm = WithStrokes(line);
        vm.ActiveTool = ToolId.DirectSelect;
        vm.HoverPathAt(line.Points[30].X, line.Points[30].Y, 6);
        Assert.NotNull(vm.PathHover);

        // Drawn state the artist can no longer act on must not outlive its
        // tool, or the last-hovered line keeps its points on screen while the
        // brush paints over them.
        vm.ActiveTool = ToolId.Brush;
        Assert.Null(vm.PathHover);
    }

    private static PathEditSession SessionOnASmoothNode(out int index)
    {
        var line = Drawn();
        var session = PathEditSession.Open(line);
        Assert.NotNull(session);

        // An interior node, which is where a fit puts handles on both sides.
        index = 1;
        Assert.True(session!.NodeCount > 2, "the fit produced no interior node to test with");
        session.SetCorner(index, false);
        return session;
    }
}
