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
