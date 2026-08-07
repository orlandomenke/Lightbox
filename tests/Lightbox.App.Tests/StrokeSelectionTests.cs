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
        var far = Line(100, 900, 300, 900);
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

        Assert.Equal(0, vm.PickStrokesIn(SKRect.Create(600, 600, 100, 100)));
        Assert.False(vm.HasStrokeSelection);
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

    private static List<Stroke> StrokesOfCurrentFrame(MainViewModel vm) =>
        vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[0].Frame switch
        {
            PaintedFrame p => p.Strokes,
            VectorFrame v => v.Strokes,
            _ => [],
        };
}
