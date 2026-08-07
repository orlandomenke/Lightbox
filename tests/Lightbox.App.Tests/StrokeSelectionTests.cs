using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Picking whole lines with the black arrow — the document side, drivable with
/// no window.
/// </summary>
/// <remarks>
/// Everything here goes through the same methods the pointer handler will call.
/// Synthetic input through Xvfb is unreliable in this environment, so a gesture
/// that lives only in an event handler is one nothing can check — which is why
/// the view model owns the pick and the canvas owns only the gesture.
/// </remarks>
public class StrokeSelectionTests
{
    private static Stroke Line(double x0, double y0, double x1, double y1, double size = 10) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#000000",
        Points = [new StrokePoint(x0, y0, 1), new StrokePoint(x1, y1, 1)],
        Brush = new BrushSettings { Size = size, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
    };

    /// <summary>A view model whose current frame holds exactly these strokes.</summary>
    private static MainViewModel WithStrokes(params Stroke[] strokes)
    {
        var vm = new MainViewModel(null);
        var frame = vm.Doc.Scene.Layers[^1].Cels[0].Frame;
        switch (frame)
        {
            case PaintedFrame p: p.Strokes.AddRange(strokes); break;
            case VectorFrame v: v.Strokes.AddRange(strokes); break;
            default: Assert.Fail($"unexpected frame {frame?.GetType().Name ?? "null"}"); break;
        }
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.Count - 1;
        return vm;
    }

    // ---- picking one ---------------------------------------------------------

    [AvaloniaFact]
    public void ClickingALinePicksIt()
    {
        var line = Line(200, 300, 400, 300);
        var vm = WithStrokes(line);

        Assert.True(vm.PickStrokeAt(300, 300, tolerance: 2));
        Assert.Equal([line.Id], vm.Selection.SelectedStrokeIds);
        Assert.Equal("1 line selected", vm.StrokeSelectionSummary);
    }

    /// <summary>
    /// The one that stops every other test passing vacuously: a click on empty
    /// canvas must select nothing rather than everything the index offered.
    /// </summary>
    [AvaloniaFact]
    public void ClickingEmptyCanvasPicksNothing()
    {
        var vm = WithStrokes(Line(200, 300, 400, 300));

        Assert.False(vm.PickStrokeAt(300, 600, tolerance: 2));
        Assert.Empty(vm.Selection.SelectedStrokeIds);
        Assert.Equal("", vm.StrokeSelectionSummary);
    }

    /// <summary>Click away to let go — what every tool with an object selection does.</summary>
    [AvaloniaFact]
    public void ClickingAwayLetsGo()
    {
        var vm = WithStrokes(Line(200, 300, 400, 300));
        vm.PickStrokeAt(300, 300, tolerance: 2);
        Assert.True(vm.HasStrokeSelection);

        vm.PickStrokeAt(300, 600, tolerance: 2);
        Assert.False(vm.HasStrokeSelection);
    }

    /// <summary>
    /// But a shift-click that misses is a miss, not a reset — you are part-way
    /// through building a selection and one bad aim should not undo it.
    /// </summary>
    [AvaloniaFact]
    public void AShiftClickThatMissesKeepsTheSelection()
    {
        var vm = WithStrokes(Line(200, 300, 400, 300));
        vm.PickStrokeAt(300, 300, tolerance: 2);

        vm.PickStrokeAt(300, 600, tolerance: 2, shift: true);
        Assert.True(vm.HasStrokeSelection);
    }

    [AvaloniaFact]
    public void ShiftClickAddsAndShiftClickAgainTakesAway()
    {
        var a = Line(100, 300, 300, 300);
        var b = Line(100, 400, 300, 400);
        var vm = WithStrokes(a, b);

        vm.PickStrokeAt(200, 300, tolerance: 2);
        vm.PickStrokeAt(200, 400, tolerance: 2, shift: true);
        Assert.Equal(2, vm.Selection.SelectedStrokeIds.Count);
        Assert.Equal("2 lines selected", vm.StrokeSelectionSummary);

        vm.PickStrokeAt(200, 400, tolerance: 2, shift: true);
        Assert.Equal([a.Id], vm.Selection.SelectedStrokeIds);
    }

    [AvaloniaFact]
    public void ClickingASecondLineWithoutShiftReplacesTheSelection()
    {
        var a = Line(100, 300, 300, 300);
        var b = Line(100, 400, 300, 400);
        var vm = WithStrokes(a, b);

        vm.PickStrokeAt(200, 300, tolerance: 2);
        vm.PickStrokeAt(200, 400, tolerance: 2);

        Assert.Equal([b.Id], vm.Selection.SelectedStrokeIds);
    }

    // ---- the marquee ---------------------------------------------------------

    [AvaloniaFact]
    public void AMarqueePicksEveryLineItTouches()
    {
        var a = Line(100, 300, 300, 300);
        var b = Line(100, 400, 300, 400);
        var far = Line(100, 520, 300, 520);
        var vm = WithStrokes(a, b, far);

        var count = vm.PickStrokesIn(SKRect.Create(150, 250, 100, 250));

        Assert.Equal(2, count);
        Assert.Contains(a.Id, vm.Selection.SelectedStrokeIds);
        Assert.Contains(b.Id, vm.Selection.SelectedStrokeIds);
        Assert.DoesNotContain(far.Id, vm.Selection.SelectedStrokeIds);
    }

    [AvaloniaFact]
    public void AMarqueeOverNothingLetsGo()
    {
        var vm = WithStrokes(Line(100, 300, 300, 300));
        vm.PickStrokeAt(200, 300, tolerance: 2);

        // On canvas (960x540) and genuinely empty — an off-canvas rect would
        // catch nothing for the wrong reason.
        Assert.Equal(0, vm.PickStrokesIn(SKRect.Create(600, 100, 100, 100)));
        Assert.False(vm.HasStrokeSelection);
    }

    /// <summary>
    /// Shift-marquee adds to what is already picked rather than replacing it.
    /// </summary>
    [AvaloniaFact]
    public void AShiftMarqueeAddsToTheSelection()
    {
        // Inside 960x540 — a stroke whose bounds fall off the surface is
        // indexed as reaching nothing, so an off-canvas line is not a marquee
        // miss, it is invisible to the picker entirely.
        var a = Line(100, 100, 300, 100);
        var b = Line(100, 400, 300, 400);
        var vm = WithStrokes(a, b);

        vm.PickStrokeAt(200, 100, tolerance: 2);
        vm.PickStrokesIn(SKRect.Create(150, 350, 100, 100), add: true);

        Assert.Equal(2, vm.Selection.SelectedStrokeIds.Count);
    }

    /// <summary>
    /// The arrow's marquee is not the pixel selection, and must not become one.
    /// </summary>
    /// <remarks>
    /// The two live side by side and mean different things — an area you paint
    /// inside versus a set of records you can move. If picking lines also set
    /// the clip region, every stroke drawn afterwards would be confined to
    /// whatever box you last dragged, which is Q48's confusion arriving as a
    /// bug rather than as a UI question.
    /// </remarks>
    [AvaloniaFact]
    public void PickingLinesDoesNotTouchThePixelSelection()
    {
        var vm = WithStrokes(Line(100, 300, 300, 300));

        vm.PickStrokesIn(SKRect.Create(50, 250, 400, 100));

        Assert.True(vm.HasStrokeSelection);
        Assert.False(vm.HasSelection);          // the pixel region
        Assert.Empty(vm.SelectionContours);
    }

    // ---- what the selection survives -----------------------------------------

    /// <summary>
    /// The selection holds ids, not record positions — so deleting an earlier
    /// stroke must not silently re-point it at a different line. This is the
    /// whole reason `SelectedStrokeIds` is a set of strings.
    /// </summary>
    [AvaloniaFact]
    public void DeletingAnEarlierLineDoesNotRepointTheSelection()
    {
        var first = Line(100, 300, 300, 300);
        var second = Line(100, 400, 300, 400);
        var vm = WithStrokes(first, second);

        vm.PickStrokeAt(200, 400, tolerance: 2);
        Assert.Equal([second.Id], vm.Selection.SelectedStrokeIds);

        // Position 0 goes; the selected stroke slides from index 1 to index 0.
        StrokesOfCurrentFrame(vm).Remove(first);
        vm.PruneStrokeSelection();

        Assert.Equal([second.Id], vm.Selection.SelectedStrokeIds);
        Assert.Equal(second.Id, Assert.Single(vm.SelectedStrokes).Id);
    }

    /// <summary>
    /// A selected line that is no longer there is dropped. A selection that
    /// reports a count and resolves to nothing reads as the tool having broken.
    /// </summary>
    [AvaloniaFact]
    public void ALineThatIsGoneIsDroppedFromTheSelection()
    {
        var line = Line(100, 300, 300, 300);
        var vm = WithStrokes(line);
        vm.PickStrokeAt(200, 300, tolerance: 2);

        StrokesOfCurrentFrame(vm).Remove(line);
        vm.PruneStrokeSelection();

        Assert.False(vm.HasStrokeSelection);
        Assert.Empty(vm.SelectedStrokes);
    }

    /// <summary>
    /// Moving to another layer lets go of what was picked on the old one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>PruneStrokeSelection</c> existed with no caller until this. A method
    /// that is right and never runs is the same defect as one that is wrong.
    /// </para>
    /// <para>
    /// The layer rather than the frame, because a new document has one frame and
    /// the interesting frame case is the opposite of what it looks like: on a
    /// <em>held</em> cel the same drawing is still exposed, so the selection is
    /// still valid and pruning it would be the bug. The hook is shared, so
    /// proving it fires once proves it fires.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void MovingToAnotherLayerLetsGoOfTheSelection()
    {
        var vm = WithStrokes(Line(100, 300, 300, 300));
        vm.PickStrokeAt(200, 300, tolerance: 2);
        Assert.True(vm.HasStrokeSelection);

        vm.ActiveLayerIndex = 0;

        Assert.False(vm.HasStrokeSelection);
    }

    /// <summary>
    /// But a held cel keeps it — the same drawing is still on screen, and
    /// letting go of a line because the playhead moved one frame over a hold
    /// would be the opposite mistake.
    /// </summary>
    [AvaloniaFact]
    public void AHeldCelKeepsTheSelection()
    {
        var vm = WithStrokes(Line(100, 300, 300, 300));
        vm.PickStrokeAt(200, 300, tolerance: 2);

        vm.CurrentFrameIndex = vm.CurrentFrameIndex;   // re-runs the hook
        vm.PruneStrokeSelection();

        Assert.True(vm.HasStrokeSelection);
    }

    // ---- the layer gate ------------------------------------------------------

    /// <summary>
    /// A locked layer refuses, and says so — reaching for a line and getting
    /// silence is the failure mode B123 is filed for elsewhere.
    /// </summary>
    [AvaloniaFact]
    public void ALockedLayerRefusesAndSaysWhy()
    {
        var vm = WithStrokes(Line(100, 300, 300, 300));
        vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Locked = true;

        Assert.False(vm.PickStrokeAt(200, 300, tolerance: 2));
        Assert.False(vm.HasStrokeSelection);
        Assert.Contains("locked", vm.AiStatus, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// But clicking empty canvas over that same locked layer stays quiet. The
    /// message is for reaching at something, not for every click that lands.
    /// </summary>
    [AvaloniaFact]
    public void ALockedLayerIsQuietWhenYouClickNothing()
    {
        var vm = WithStrokes(Line(100, 300, 300, 300));
        vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Locked = true;
        vm.AiStatus = "";

        Assert.False(vm.PickStrokeAt(700, 700, tolerance: 2));
        Assert.Equal("", vm.AiStatus);
    }

    /// <summary>
    /// Picking never keys a cel. Clicking about is how an artist looks around,
    /// and looking around must not author anything.
    /// </summary>
    [AvaloniaFact]
    public void PickingOnAnEmptyLayerCreatesNothing()
    {
        var vm = new MainViewModel(null);
        var layer = vm.Doc.Scene.Layers[^1];
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.Count - 1;
        foreach (var cel in layer.Cels) cel.Frame = null;

        Assert.False(vm.PickStrokeAt(300, 300, tolerance: 2));
        Assert.All(layer.Cels, c => Assert.Null(c.Frame));
    }

    // ---- the selection is visible --------------------------------------------

    /// <summary>
    /// Picking a line publishes its outline for the canvas to trace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the test the feature shipped without, and the gap it left was
    /// the whole point of the tool.</b> Every assertion above passed on a build
    /// where clicking a line changed nothing on screen: the id went into
    /// <c>SelectedStrokeIds</c>, the summary said "1 line selected", and nothing
    /// drew it. A test that only reads model state cannot tell those apart.
    /// </para>
    /// <para>
    /// <b>Why this and not a pixel check.</b> The highlight is canvas chrome,
    /// drawn in the render op rather than into the published
    /// <c>RenderSnapshot</c>, and this suite runs on Avalonia's headless
    /// <em>software</em> drawing where a rendered frame cannot be captured at
    /// all — the same limit <c>BrushGizmoTests</c> records for the brush ring.
    /// So the wiring is asserted here, where it is exact, and that it *looks*
    /// right is a <c>MANUAL_TESTING.md</c> check.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void PickingALinePublishesItsOutline()
    {
        var line = Line(200, 300, 400, 300);
        var vm = WithStrokes(line);

        IReadOnlyList<(IReadOnlyList<StrokePoint> Points, bool Closed)>? published = null;
        var calls = 0;
        vm.SelectedLinesChanged += l => { published = l; calls++; };

        vm.PickStrokeAt(300, 300, tolerance: 2);

        Assert.Equal(1, calls);
        var outline = Assert.Single(published!);
        Assert.False(outline.Closed);
        Assert.Equal(line.Points.Count, outline.Points.Count);
        Assert.Equal(200, outline.Points[0].X);
        Assert.Equal(400, outline.Points[^1].X);
    }

    /// <summary>Letting go takes the highlight away, or it hangs about after the selection.</summary>
    [AvaloniaFact]
    public void LettingGoTakesTheOutlineAway()
    {
        var vm = WithStrokes(Line(200, 300, 400, 300));
        vm.PickStrokeAt(300, 300, tolerance: 2);

        IReadOnlyList<(IReadOnlyList<StrokePoint> Points, bool Closed)>? published = null;
        var called = false;
        vm.SelectedLinesChanged += l => { published = l; called = true; };

        vm.PickStrokeAt(300, 600, tolerance: 2);

        Assert.True(called);
        Assert.Null(published);
    }

    /// <summary>
    /// A fill's outline is closed, so the trace joins up rather than leaving the
    /// shape open along one edge.
    /// </summary>
    [AvaloniaFact]
    public void AFillsOutlineIsClosed()
    {
        var box = new Stroke
        {
            Tool = ToolKind.Fill,
            Color = "#ff0000",
            Points =
            [
                new StrokePoint(200, 200, 1), new StrokePoint(400, 200, 1),
                new StrokePoint(400, 400, 1), new StrokePoint(200, 400, 1),
            ],
            Brush = new BrushSettings { Size = 1, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
        };
        var vm = WithStrokes(box);

        IReadOnlyList<(IReadOnlyList<StrokePoint> Points, bool Closed)>? published = null;
        vm.SelectedLinesChanged += l => published = l;

        vm.PickStrokeAt(300, 300, tolerance: 2);

        Assert.True(Assert.Single(published!).Closed);
    }

    /// <summary>The canvas accepts what the view model publishes, including nothing.</summary>
    [AvaloniaFact]
    public void TheCanvasTakesTheOutlinesItIsHanded()
    {
        var canvas = new Lightbox.App.Rendering.CanvasControl();

        canvas.SetSelectedLines([([new StrokePoint(0, 0, 1), new StrokePoint(10, 10, 1)], false)]);
        canvas.SetSelectedLines(null);
        // A one-point stroke has no line to trace and must be dropped rather
        // than reaching the renderer as a degenerate path.
        canvas.SetSelectedLines([([new StrokePoint(5, 5, 1)], false)]);
    }

    // ---- the tool is actually reachable --------------------------------------

    /// <summary>
    /// The registration checklist, asserted rather than remembered. A tool that
    /// works and cannot be reached is the failure this project has hit twice —
    /// the shape options group invisible for want of one notify attribute, and
    /// `CanvasToolMode.Select` sitting in the enum with a full hit-test chain
    /// that nothing ever assigned.
    /// </summary>
    [AvaloniaFact]
    public void TheArrowToolIsReachableAndBindable()
    {
        var vm = new MainViewModel(null);

        vm.SelectToolCommand.Execute(ToolId.Arrow);
        Assert.Equal(ToolId.Arrow, vm.ActiveTool);
        Assert.True(vm.IsArrowTool);

        // …and switching away turns the toolbar button off again, which is what
        // the missing NotifyPropertyChangedFor broke last time.
        vm.SelectToolCommand.Execute(ToolId.Brush);
        Assert.False(vm.IsArrowTool);
    }

    /// <summary>
    /// Its key is in the one registry the shortcut editor reads. A gesture wired
    /// straight to a command works and cannot be seen, searched or rebound —
    /// and `tool.shape` is still in that state, advertising a U that does
    /// nothing.
    /// </summary>
    [AvaloniaFact]
    public void TheArrowToolHasABindableShortcut()
    {
        var map = new Lightbox.App.Services.ShortcutMap();
        var entry = map.Definitions.SingleOrDefault(d => d.Id == "tool.arrow");

        Assert.NotNull(entry);
        Assert.Equal("Tools", entry!.Category);
        Assert.NotNull(entry.Default);
    }

    private static List<Stroke> StrokesOfCurrentFrame(MainViewModel vm) =>
        vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[0].Frame switch
        {
            PaintedFrame p => p.Strokes,
            VectorFrame v => v.Strokes,
            _ => [],
        };
}
