using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.App.Tests;

/// <summary>
/// The pen: placing nodes, curving them, and the one stroke that results.
/// </summary>
/// <remarks>
/// <para>
/// <b>Driven through the view model, never the canvas.</b> Synthetic pointer
/// input through Xvfb is unreliable here, so the canvas owns only the gesture
/// and every decision — where a node lands, whether Shift moved it, whether this
/// click closed the path, when it becomes an undo step — is asked of a method
/// with no window behind it.
/// </para>
/// <para>
/// The properties that carry the weight: <b>what the pen writes is an ordinary
/// stroke</b> (no second record, no second renderer), <b>path and points agree</b>
/// the moment it lands, <b>a whole path is one undo step</b>, and <b>nothing is
/// thrown away by a key</b> — Escape finishes rather than discards, which is the
/// one place this tool departs from the polygon selection beside it.
/// </para>
/// </remarks>
public class PenToolTests(ITestOutputHelper output)
{
    private static MainViewModel Ready()
    {
        var vm = new MainViewModel(null);
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.Count - 1;
        vm.ActiveTool = ToolId.Pen;
        return vm;
    }

    private static List<Stroke> StrokesOf(MainViewModel vm) =>
        vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[0].Frame!.Strokes;

    /// <summary>Place a corner node, with no drag.</summary>
    private static void Click(MainViewModel vm, double x, double y, bool shift = false)
    {
        vm.PenPress(x, y, tolerance: 6, shift: shift);
        vm.PenRelease();
    }

    /// <summary>Place a node and pull its handles out to a point.</summary>
    private static void ClickDrag(MainViewModel vm, double x, double y, double hx, double hy, bool alt = false)
    {
        vm.PenPress(x, y, tolerance: 6);
        vm.PenDrag(hx, hy, alt: alt);
        vm.PenRelease();
    }

    // ---- placing ---------------------------------------------------------------

    [AvaloniaFact]
    public void AClickPlacesACornerNode()
    {
        var vm = Ready();
        Click(vm, 100, 100);

        Assert.True(vm.PenActive);
        Assert.Equal(1, vm.Pen!.NodeCount);
        Assert.True(vm.Pen.Path.Nodes[0].Corner);
        Assert.False(vm.Pen.Path.Nodes[0].HasHandles);
    }

    /// <summary>
    /// The gesture the whole tool rests on: a press places, and a drag from that
    /// same press curves what it placed. There is no separate "curve" mode
    /// because there cannot be — nothing knows at the moment of the click
    /// whether a drag is coming.
    /// </summary>
    [AvaloniaFact]
    public void AClickAndDragPlacesASmoothNodeWithMirroredHandles()
    {
        var vm = Ready();
        ClickDrag(vm, 100, 100, 140, 100);

        var node = vm.Pen!.Path.Nodes[0];
        Assert.False(node.Corner);
        Assert.Equal(40, node.OutX, 3);
        Assert.Equal(0, node.OutY, 3);
        // Mirrored, because both handles are being created by this one gesture.
        Assert.Equal(-40, node.InX, 3);
        Assert.Equal(0, node.InY, 3);
    }

    /// <summary>
    /// Alt is Illustrator's break, and here it means the node keeps only the
    /// handle you dragged — a curve that arrives straight and leaves curved.
    /// </summary>
    [AvaloniaFact]
    public void AltWhileDraggingLeavesTheNodeACorner()
    {
        var vm = Ready();
        ClickDrag(vm, 100, 100, 140, 100, alt: true);

        var node = vm.Pen!.Path.Nodes[0];
        Assert.True(node.Corner);
        Assert.Equal(40, node.OutX, 3);
        Assert.Equal(0, node.InX, 3);
        Assert.Equal(0, node.InY, 3);
    }

    [AvaloniaFact]
    public void ShiftConstrainsTheNextNodeToADiagonal()
    {
        var vm = Ready();
        Click(vm, 100, 100);
        // 30° off horizontal; the nearest 45° multiple is 45.
        Click(vm, 100 + 100 * Math.Cos(Math.PI / 6), 100 + 100 * Math.Sin(Math.PI / 6), shift: true);

        var placed = vm.Pen!.Path.Nodes[1];
        var dx = placed.X - 100;
        var dy = placed.Y - 100;
        output.WriteLine($"placed at ({placed.X:F2}, {placed.Y:F2}), delta ({dx:F2}, {dy:F2})");

        Assert.Equal(dx, dy, 3);
        // The angle snaps and the distance does not — a ruler's division of
        // labour, which is what makes Shift usable rather than a jump.
        Assert.Equal(100, Math.Sqrt(dx * dx + dy * dy), 3);
    }

    [AvaloniaFact]
    public void BackspaceTakesTheLastNodeBackOff()
    {
        var vm = Ready();
        Click(vm, 100, 100);
        Click(vm, 200, 100);
        Click(vm, 200, 200);

        Assert.True(vm.RemoveLastPenNode());
        Assert.Equal(2, vm.Pen!.NodeCount);

        vm.RemoveLastPenNode();
        vm.RemoveLastPenNode();
        // The session goes away with its last node rather than lingering empty,
        // so nothing is "active" with nothing in it.
        Assert.False(vm.PenActive);
        Assert.False(vm.RemoveLastPenNode());
    }

    // ---- closing ---------------------------------------------------------------

    [AvaloniaFact]
    public void ClickingTheFirstNodeClosesThePathAndFinishesIt()
    {
        var vm = Ready();
        Click(vm, 100, 100);
        Click(vm, 200, 100);
        Click(vm, 200, 200);
        vm.PenPress(101, 101, tolerance: 6);
        vm.PenRelease();

        // Closing finishes: there is nowhere left to put a node on a path that
        // has joined up.
        Assert.False(vm.PenActive);
        var stroke = Assert.Single(StrokesOf(vm));
        Assert.NotNull(stroke.Path);
        Assert.True(stroke.Path!.Closed);

        // The closing segment is in the points, not only in the flag. The
        // renderer stamps the polyline and knows nothing about Closed, so a
        // closed path that does not return to its start is a triangle drawn
        // with one side missing.
        Assert.Equal(stroke.Points[0].X, stroke.Points[^1].X, 9);
        Assert.Equal(stroke.Points[0].Y, stroke.Points[^1].Y, 9);
    }

    /// <summary>
    /// The closing click is announced before it happens: within closing
    /// distance of the first node the rubber band snaps onto it and
    /// <c>PenWouldClose</c> goes up, which the canvas draws as a ring. A pen
    /// that closes without warning makes the artist find out by clicking.
    /// </summary>
    [AvaloniaFact]
    public void HoveringTheFirstNodeAnnouncesThatAClickWouldClose()
    {
        var vm = Ready();
        Click(vm, 100, 100);
        Click(vm, 200, 100);
        Click(vm, 200, 200);

        vm.PenHover(103, 102, tolerance: 6);
        Assert.True(vm.PenWouldClose);
        // The rubber band snaps shut rather than reaching to the raw cursor:
        // the click is previewed, not described.
        Assert.Equal((100.0, 100.0), vm.Pen!.Cursor);

        vm.PenHover(150, 150, tolerance: 6);
        Assert.False(vm.PenWouldClose);

        // Two straight nodes cannot close, so nothing is announced either.
        var straight = Ready();
        Click(straight, 100, 100);
        Click(straight, 200, 100);
        straight.PenHover(101, 101, tolerance: 6);
        Assert.False(straight.PenWouldClose);
    }

    /// <summary>
    /// Two straight nodes closed would be a line drawn there and back — a flag
    /// in the file that changes no pixel. Two curved ones make a lens, which is
    /// a shape somebody means.
    /// </summary>
    [AvaloniaFact]
    public void TwoStraightNodesDoNotClose()
    {
        var vm = Ready();
        Click(vm, 100, 100);
        Click(vm, 200, 100);
        Assert.False(vm.Pen!.ClosesAt(100, 100, 6));

        var curved = Ready();
        ClickDrag(curved, 100, 100, 100, 60);
        ClickDrag(curved, 200, 100, 200, 140);
        Assert.True(curved.Pen!.ClosesAt(100, 100, 6));
    }

    // ---- what lands in the document -------------------------------------------

    /// <summary>
    /// <b>Invariant 1, and the reason this design needed no argument for it.</b>
    /// A pen line is the same record the brush writes — the path is a
    /// description added beside the points, not a second kind of artwork.
    /// </summary>
    [AvaloniaFact]
    public void FinishingWritesOneOrdinaryStrokeWithPathAndPointsAgreeing()
    {
        var vm = Ready();
        ClickDrag(vm, 100, 100, 140, 60);
        Click(vm, 300, 200);
        Assert.True(vm.FinishPen());

        var stroke = Assert.Single(StrokesOf(vm));
        Assert.Equal(ToolKind.Brush, stroke.Tool);
        Assert.NotNull(stroke.Path);
        Assert.Equal(2, stroke.Path!.Nodes.Count);

        // The invariant StrokePath creates: flattening the recorded path must
        // reproduce the recorded points exactly, or the two can drift apart.
        var reflattened = PathFlattener.Flatten(stroke.Path);
        Assert.Equal(stroke.Points.Count, reflattened.Count);
        var worst = 0.0;
        for (var i = 0; i < reflattened.Count; i++)
        {
            worst = Math.Max(worst, Math.Abs(reflattened[i].X - stroke.Points[i].X));
            worst = Math.Max(worst, Math.Abs(reflattened[i].Y - stroke.Points[i].Y));
        }
        output.WriteLine($"{stroke.Path.Nodes.Count} nodes -> {stroke.Points.Count} points, worst {worst:F4} px");
        Assert.True(worst < 1e-9, $"path and points disagree by {worst} px");
    }

    /// <summary>
    /// A pen line is the one stroke in the document that never needed a fit —
    /// reaching for the white arrow on it opens the nodes the pen authored,
    /// rather than an approximation of the polyline they produced.
    /// </summary>
    [AvaloniaFact]
    public void TheWhiteArrowOpensThePenSOwnNodesRatherThanAFit()
    {
        var vm = Ready();
        ClickDrag(vm, 100, 100, 140, 60);
        Click(vm, 300, 200);
        Click(vm, 400, 120);
        vm.FinishPen();

        var stroke = Assert.Single(StrokesOf(vm));
        var authored = stroke.Path!.Nodes.Count;

        vm.ActiveTool = ToolId.Arrow;
        Assert.True(vm.BeginPathEdit(stroke.Id));
        output.WriteLine($"authored {authored} nodes, isolation opened {vm.PathEdit!.NodeCount}");
        Assert.Equal(authored, vm.PathEdit!.NodeCount);
    }

    /// <summary>
    /// A dozen clicks and one history entry — the transform session's rule, and
    /// phase 2's. Undoing a pen line removes the line, not its last node.
    /// </summary>
    [AvaloniaFact]
    public void AWholePathIsOneUndoStep()
    {
        var vm = Ready();
        Click(vm, 100, 100);
        Click(vm, 200, 100);
        Click(vm, 200, 200);
        Click(vm, 100, 200);
        vm.FinishPen();
        Assert.Single(StrokesOf(vm));

        vm.UndoCommand.Execute(null);
        Assert.Empty(StrokesOf(vm));
    }

    [AvaloniaFact]
    public void OneNodeIsNotALineAndWritesNothing()
    {
        var vm = Ready();
        Click(vm, 100, 100);

        Assert.False(vm.FinishPen());
        Assert.Empty(StrokesOf(vm));
        Assert.False(vm.PenActive);
    }

    /// <summary>
    /// <b>The one place this tool departs from the polygon selection beside
    /// it.</b> A selection in progress is not artwork and cancelling it costs
    /// nothing; a dozen placed nodes are, and a key that silently throws a
    /// minute of authoring away is the wrong default whatever its neighbour
    /// does. Ctrl+Z is how you lose it, deliberately.
    /// </summary>
    [AvaloniaFact]
    public void FinishingKeepsTheLineRatherThanDiscardingIt()
    {
        var vm = Ready();
        Click(vm, 100, 100);
        Click(vm, 240, 180);

        // What the key handler does for both Escape and Enter.
        Assert.True(vm.FinishPen());
        Assert.Single(StrokesOf(vm));
    }

    /// <summary>
    /// Reaching for the eyedropper mid-path is not "I am done". The session
    /// survives the switch — nothing is committed, nothing is lost — and the
    /// pen resumes exactly where it parked. Enter and Escape remain the
    /// deliberate finish.
    /// </summary>
    [AvaloniaFact]
    public void SwitchingToolsParksThePathAndThePenResumesIt()
    {
        var vm = Ready();
        Click(vm, 100, 100);
        Click(vm, 240, 180);

        vm.ActiveTool = ToolId.Brush;

        // Still in progress: not written, not dropped.
        Assert.True(vm.PenActive);
        Assert.Empty(StrokesOf(vm));

        vm.ActiveTool = ToolId.Pen;
        Click(vm, 300, 100);
        Assert.Equal(3, vm.Pen!.NodeCount);

        Assert.True(vm.FinishPen());
        var stroke = Assert.Single(StrokesOf(vm));
        Assert.Equal(3, stroke.Path!.Nodes.Count);
    }

    /// <summary>The explicit discard, which no key is bound to.</summary>
    [AvaloniaFact]
    public void CancellingWritesNothing()
    {
        var vm = Ready();
        Click(vm, 100, 100);
        Click(vm, 240, 180);

        vm.CancelPen();

        Assert.False(vm.PenActive);
        Assert.Empty(StrokesOf(vm));
    }

    [AvaloniaFact]
    public void ThePenIsRefusedOnALockedLayer()
    {
        var vm = Ready();
        vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Locked = true;

        Assert.False(vm.PenPress(100, 100, tolerance: 6));
        Assert.False(vm.PenActive);
        Assert.Contains("lock", vm.AiStatus, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Another tool's press must not reach the pen, or reaching for the brush
    /// and clicking would silently start a path.
    /// </summary>
    [AvaloniaFact]
    public void APressWithAnotherToolActiveDoesNothing()
    {
        var vm = Ready();
        vm.ActiveTool = ToolId.Brush;

        Assert.False(vm.PenPress(100, 100, tolerance: 6));
        Assert.False(vm.PenActive);
    }

    // ---- the preview -----------------------------------------------------------

    /// <summary>
    /// The rubber band is the real curve, not a straight line to the cursor. A
    /// pen whose preview is straight and whose result is curved makes you place
    /// a node to find out what you drew.
    /// </summary>
    [AvaloniaFact]
    public void TheRubberBandFollowsTheCurveTheNextClickWouldMake()
    {
        var vm = Ready();
        ClickDrag(vm, 100, 100, 100, 20);
        vm.PenHover(300, 100);

        var preview = vm.Pen!.Preview();
        Assert.True(preview.Count > 2, "a curved rubber band should be more than two points");

        // The out handle points straight up, so the curve must leave the first
        // node upwards before it comes back down to the cursor.
        var apex = preview.Min(p => p.Y);
        output.WriteLine($"{preview.Count} preview points, apex y {apex:F2}");
        Assert.True(apex < 95, $"the preview did not follow the handle (apex {apex})");
    }

    [AvaloniaFact]
    public void ThePreviewIsEmptyUntilThereIsSomethingToShow()
    {
        var vm = Ready();
        Assert.Null(vm.Pen);

        Click(vm, 100, 100);
        // One node and nowhere to reach yet.
        Assert.Empty(vm.Pen!.Preview());

        vm.PenHover(200, 150);
        Assert.NotEmpty(vm.Pen.Preview());
    }

    /// <summary>
    /// While a handle is being pulled the pointer <em>is</em> the handle, so a
    /// rubber band to it would draw a segment the next click is not going to
    /// make.
    /// </summary>
    [AvaloniaFact]
    public void ThePointerDoesNotAlsoBecomeARubberBandWhileShapingAHandle()
    {
        var vm = Ready();
        Click(vm, 100, 100);
        vm.PenHover(300, 100);
        Assert.NotEmpty(vm.Pen!.Preview());

        vm.PenPress(200, 100, tolerance: 6);
        vm.PenDrag(240, 60);

        Assert.Null(vm.Pen.Cursor);
        Assert.Equal(2, vm.Pen.NodeCount);
    }

    // ---- registration ----------------------------------------------------------

    /// <summary>
    /// The pen's key is in the one registry the shortcut editor reads, and does
    /// not shadow anything already bound. A tool wired straight to a key works
    /// and cannot be seen, searched or rebound.
    /// </summary>
    [AvaloniaFact]
    public void ThePenHasABindableShortcutThatCollidesWithNothing()
    {
        var map = new Lightbox.App.Services.ShortcutMap();
        var entry = map.Definitions.SingleOrDefault(d => d.Id == "tool.pen");

        Assert.NotNull(entry);
        Assert.Equal("Tools", entry!.Category);
        Assert.NotNull(entry.Default);

        var clashes = map.Definitions
            .Where(d => d.Id != entry.Id && d.Default is { } g
                && g.Key == entry.Default!.Key && g.KeyModifiers == entry.Default.KeyModifiers)
            .Select(d => d.Id)
            .ToList();
        Assert.True(clashes.Count == 0, $"tool.pen collides with {string.Join(", ", clashes)}");
    }

    /// <summary>
    /// <b>B147's shape, one tool along, and phase 2 shipped it.</b> The node
    /// overlay is drawn whatever the tool is, so leaving isolation for the brush
    /// left glyphs on screen over a line nothing could reshape any more. Only
    /// the tools that can still work the session keep it — the white arrow and
    /// the width tool. The black arrow used to keep it too, and that read as
    /// stuck: nodes on screen it could not drag, other lines its clicks would
    /// not select. Choosing a tool that cannot work the session is leaving it.
    /// </summary>
    [AvaloniaFact]
    public void OnlyTheToolsThatWorkTheSessionKeepIsolation()
    {
        var vm = new MainViewModel(null);
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.Count - 1;
        var line = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#000000",
            Points = [.. Enumerable.Range(0, 40).Select(i => new StrokePoint(100 + i * 4, 200 + i * i * 0.05, 1))],
            Brush = new BrushSettings { Size = 8, Opacity = 1, Flow = 1, Spacing = 0.2 },
        };
        StrokesOf(vm).Add(line);

        vm.ActiveTool = ToolId.Arrow;
        Assert.True(vm.BeginPathEdit(line.Id));

        // The white arrow works the session; the width tool shares it.
        vm.ActiveTool = ToolId.DirectSelect;
        Assert.True(vm.PathEditActive);
        vm.ActiveTool = ToolId.Width;
        Assert.True(vm.PathEditActive);

        // The black arrow cannot work it, so choosing it lets the session go.
        vm.ActiveTool = ToolId.Arrow;
        Assert.False(vm.PathEditActive);

        Assert.True(vm.BeginPathEdit(line.Id));
        vm.ActiveTool = ToolId.Brush;
        Assert.False(vm.PathEditActive);
    }
}
