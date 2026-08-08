using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// V3: doing something to the lines the arrow picked — move, delete, recolour,
/// and undo putting each one back exactly.
/// </summary>
/// <remarks>
/// <para>
/// Driven through the same methods the keyboard and the canvas call, for the
/// reason <see cref="StrokeSelectionTests"/> gives: an edit that exists only
/// inside a pointer handler is one nothing here can check.
/// </para>
/// <para>
/// <b>Undo is asserted on coordinates and colours rather than on "it ran".</b>
/// Two of these fail on an implementation that looks obviously right — undo that
/// subtracts the offset instead of restoring the points, and a recolour that sets
/// <c>Color</c> without cutting the swatch link.
/// </para>
/// </remarks>
public class StrokeActionTests(ITestOutputHelper output)
{
    private static Stroke Line(double x0, double y0, double x1, double y1) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#000000",
        Points = [new StrokePoint(x0, y0, 1), new StrokePoint(x1, y1, 1)],
        Brush = new BrushSettings { Size = 10, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
    };

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

    private static List<Stroke> StrokesNow(MainViewModel vm) =>
        vm.Doc.Scene.Layers[^1].Cels[0].Frame switch
        {
            PaintedFrame p => p.Strokes,
            VectorFrame v => v.Strokes,
            _ => [],
        };

    // ---- delete --------------------------------------------------------------

    [AvaloniaFact]
    public void DeletingTheSelectionTakesTheLineAndLetsGoOfIt()
    {
        var keep = Line(100, 100, 200, 100);
        var doomed = Line(100, 300, 200, 300);
        var vm = WithStrokes(keep, doomed);

        Assert.True(vm.PickStrokeAt(150, 300, 6));
        Assert.Equal(1, vm.DeleteSelectedStrokes());

        Assert.Equal([keep.Id], StrokesNow(vm).Select(s => s.Id));
        // The selection goes with the line. A count that names nothing reads as
        // the tool having broken.
        Assert.False(vm.HasStrokeSelection);
    }

    [AvaloniaFact]
    public void DeletingNothingIsNotAnEdit()
    {
        var vm = WithStrokes(Line(100, 100, 200, 100));
        Assert.Equal(0, vm.DeleteSelectedStrokes());
        Assert.Single(StrokesNow(vm));
    }

    /// <summary>
    /// Draw order is list order, so undo has to put a line back where it was —
    /// not on the end, where it would cover something it did not cover before.
    /// </summary>
    [AvaloniaFact]
    public void UndoingADeletePutsTheLineBackInItsOldPlaceInTheOrder()
    {
        var first = Line(100, 100, 200, 100);
        var middle = Line(100, 200, 200, 200);
        var last = Line(100, 300, 200, 300);
        var vm = WithStrokes(first, middle, last);

        Assert.True(vm.PickStrokeAt(150, 200, 6));
        Assert.Equal(1, vm.DeleteSelectedStrokes());
        Assert.Equal([first.Id, last.Id], StrokesNow(vm).Select(s => s.Id));

        vm.UndoCommand.Execute(null);

        output.WriteLine("after undo: " + string.Join(", ", StrokesNow(vm).Select(s => s.Id)));
        Assert.Equal([first.Id, middle.Id, last.Id], StrokesNow(vm).Select(s => s.Id));
    }

    [AvaloniaFact]
    public void DeletingSeveralIsOneUndoStep()
    {
        var vm = WithStrokes(Line(100, 100, 200, 100), Line(100, 200, 200, 200), Line(100, 300, 200, 300));

        Assert.Equal(3, vm.PickStrokesIn(new SkiaSharp.SKRect(50, 50, 250, 350)));
        Assert.Equal(3, vm.DeleteSelectedStrokes());
        Assert.Empty(StrokesNow(vm));

        vm.UndoCommand.Execute(null);
        Assert.Equal(3, StrokesNow(vm).Count);
    }

    // ---- move ----------------------------------------------------------------

    [AvaloniaFact]
    public void MovingTheSelectionShiftsEveryPoint()
    {
        var line = Line(100, 100, 200, 100);
        var vm = WithStrokes(line);

        Assert.True(vm.PickStrokeAt(150, 100, 6));
        Assert.Equal(1, vm.MoveSelectedStrokes(30, -12));

        var moved = StrokesNow(vm)[0].Points;
        Assert.Equal(130, moved[0].X, 9);
        Assert.Equal(88, moved[0].Y, 9);
        Assert.Equal(230, moved[1].X, 9);
        Assert.Equal(88, moved[1].Y, 9);
    }

    [AvaloniaFact]
    public void MovingNowhereIsNotAnEdit()
    {
        var vm = WithStrokes(Line(100, 100, 200, 100));
        Assert.True(vm.PickStrokeAt(150, 100, 6));
        Assert.Equal(0, vm.MoveSelectedStrokes(0, 0));
        Assert.False(vm.UndoCommand.CanExecute(null) && StrokesNow(vm)[0].Points[0].X != 100);
    }

    [AvaloniaFact]
    public void OnlyTheSelectedLineMoves()
    {
        var still = Line(100, 100, 200, 100);
        var going = Line(100, 300, 200, 300);
        var vm = WithStrokes(still, going);

        Assert.True(vm.PickStrokeAt(150, 300, 6));
        Assert.Equal(1, vm.MoveSelectedStrokes(40, 0));

        var untouched = StrokesNow(vm).First(s => s.Id == still.Id);
        Assert.Equal(100, untouched.Points[0].X, 9);
    }

    /// <summary>
    /// The one that fails on the obvious implementation. Subtracting the offset
    /// is not the inverse of adding it in IEEE-754, and every dab dynamic is
    /// seeded from the <em>bits</em> of a coordinate (invariant 2, `Hash01`) — so
    /// an undo that lands within a rounding error puts the line visibly back and
    /// silently changes its grain.
    /// </summary>
    [AvaloniaFact]
    public void UndoingAMoveRestoresTheExactCoordinates()
    {
        // Chosen so that x + d - d != x in double arithmetic.
        const double x = 0.1, d = 0.7;
        var vm = WithStrokes(Line(x, 200, 100, 200));
        var expected = StrokesNow(vm)[0].Points[0].X;

        Assert.True(vm.PickStrokeAt(x, 200, 8));
        Assert.Equal(1, vm.MoveSelectedStrokes(d, 0));
        vm.UndoCommand.Execute(null);

        var after = StrokesNow(vm)[0].Points[0].X;
        output.WriteLine($"before {expected:R}, after undo {after:R}, "
                         + $"naive x+d-d {(x + d - d):R}");
        Assert.Equal(expected, after);
        Assert.True(BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(after),
            "the coordinate came back to a different bit pattern, so Hash01 would re-seed and the "
            + "line would return with a different grain — restore the points, do not subtract the offset");
    }

    /// <summary>
    /// A fill is a contour plus its even-odd holes. Moving one and not the other
    /// puts the gaps somewhere else, which reads as corruption rather than a move.
    /// </summary>
    [AvaloniaFact]
    public void MovingAFillTakesItsHolesWithIt()
    {
        var fill = new Stroke
        {
            Tool = ToolKind.Fill,
            Color = "#ff0000",
            Points =
            [
                new StrokePoint(100, 100, 1), new StrokePoint(300, 100, 1),
                new StrokePoint(300, 300, 1), new StrokePoint(100, 300, 1),
            ],
            Holes =
            [
                [
                    new StrokePoint(150, 150, 1), new StrokePoint(250, 150, 1),
                    new StrokePoint(250, 250, 1), new StrokePoint(150, 250, 1),
                ],
            ],
            Brush = new BrushSettings { Size = 1, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
        };
        var vm = WithStrokes(fill);

        // Inside the outer contour and outside the hole, so the fill is picked.
        Assert.True(vm.PickStrokeAt(120, 120, 4));
        Assert.Equal(1, vm.MoveSelectedStrokes(25, 25));

        var moved = StrokesNow(vm)[0];
        Assert.Equal(125, moved.Points[0].X, 9);
        Assert.NotNull(moved.Holes);
        Assert.Equal(175, moved.Holes![0][0].X, 9);
        Assert.Equal(175, moved.Holes[0][0].Y, 9);
    }

    [AvaloniaFact]
    public void ANudgeIsOnePixelAndShiftMakesItTen()
    {
        var vm = WithStrokes(Line(100, 100, 200, 100));
        Assert.True(vm.PickStrokeAt(150, 100, 6));

        vm.NudgeSelectedStrokes(1, 0);
        Assert.Equal(101, StrokesNow(vm)[0].Points[0].X, 9);

        vm.NudgeSelectedStrokes(1, 0, coarse: true);
        Assert.Equal(111, StrokesNow(vm)[0].Points[0].X, 9);
    }

    // ---- recolour ------------------------------------------------------------

    [AvaloniaFact]
    public void RecolouringTheSelectionChangesItsColour()
    {
        var vm = WithStrokes(Line(100, 100, 200, 100));
        Assert.True(vm.PickStrokeAt(150, 100, 6));

        Assert.Equal(1, vm.RecolourSelectedStrokes("#ff8800"));
        Assert.Equal("#ff8800", StrokesNow(vm)[0].Color);

        vm.UndoCommand.Execute(null);
        Assert.Equal("#000000", StrokesNow(vm)[0].Color);
    }

    /// <summary>
    /// The invisible trap. <see cref="Stroke.SwatchId"/> wins at render time, so
    /// a recolour that only writes <see cref="Stroke.Color"/> changes a field
    /// nothing reads and the line stays the colour it was.
    /// </summary>
    [AvaloniaFact]
    public void RecolouringALineFromAPaletteCutsItLooseFromTheSwatch()
    {
        var line = Line(100, 100, 200, 100);
        line.SwatchId = "sw-1";
        line.PaletteId = "pal-1";
        var vm = WithStrokes(line);

        Assert.True(vm.PickStrokeAt(150, 100, 6));
        Assert.Equal(1, vm.RecolourSelectedStrokes("#00aa55"));

        var after = StrokesNow(vm)[0];
        output.WriteLine($"colour {after.Color}, swatch {after.SwatchId ?? "none"}");
        Assert.Equal("#00aa55", after.Color);
        Assert.Null(after.SwatchId);
        Assert.Null(after.PaletteId);

        // And undo puts the palette link back, or the line stops following the
        // swatch forever because of an edit that was taken back.
        vm.UndoCommand.Execute(null);
        var restored = StrokesNow(vm)[0];
        Assert.Equal("sw-1", restored.SwatchId);
        Assert.Equal("pal-1", restored.PaletteId);
    }

    [AvaloniaFact]
    public void RecolouringToTheColourItAlreadyIsDoesNothing()
    {
        var vm = WithStrokes(Line(100, 100, 200, 100));
        Assert.True(vm.PickStrokeAt(150, 100, 6));
        Assert.Equal(0, vm.RecolourSelectedStrokes("#000000"));
    }

    // ---- how the keyboard reaches it ----------------------------------------

    /// <summary>
    /// The arrow keys already nudge the pixel selection over the canvas. The line
    /// selection is asked first rather than given keys of its own, so an artist
    /// who rebinds one does not find the other still on the old key.
    /// </summary>
    [AvaloniaFact]
    public void TheCanvasNudgeMovesLinesWhenTheArrowIsHoldingSome()
    {
        var vm = WithStrokes(Line(100, 100, 200, 100));
        vm.SelectToolCommand.Execute(ToolId.Arrow);
        Assert.True(vm.PickStrokeAt(150, 100, 6));

        // Exactly what the `canvas.nudgeRight` dispatch calls.
        vm.NudgeSelection(1, 0);

        Assert.Equal(101, StrokesNow(vm)[0].Points[0].X, 9);
    }

    [AvaloniaFact]
    public void TheNudgeIsIgnoredUnlessTheArrowIsTheToolInHand()
    {
        var vm = WithStrokes(Line(100, 100, 200, 100));
        vm.SelectToolCommand.Execute(ToolId.Arrow);
        Assert.True(vm.PickStrokeAt(150, 100, 6));

        // Another tool: the selection is still recorded but the keys are not the
        // arrow's any more, so frame stepping keeps them.
        vm.SelectToolCommand.Execute(ToolId.Brush);
        Assert.False(vm.NudgeSelectionFromKeyboard(1, 0, coarse: false));
        Assert.Equal(100, StrokesNow(vm)[0].Points[0].X, 9);
    }

    /// <summary>
    /// Registration, which is the half that makes a capability findable. A command
    /// nothing can reach is a feature an artist cannot use however well it works.
    /// </summary>
    [AvaloniaFact]
    public void TheActionsAreRegisteredWhereTheConfigurationWindowCanSeeThem()
    {
        var map = new Lightbox.App.Services.ShortcutMap();
        var ids = map.Definitions.Select(d => d.Id).ToList();

        Assert.Contains("lines.delete", ids);
        Assert.Contains("lines.recolour", ids);

        // Delete has a default; recolour deliberately does not, and being listed
        // is what lets an artist give it one.
        var delete = map.Definitions.First(d => d.Id == "lines.delete");
        output.WriteLine($"lines.delete -> {delete.GestureText}");
        Assert.NotNull(delete.Default);
        Assert.Null(map.Definitions.First(d => d.Id == "lines.recolour").Default);
    }

    [AvaloniaFact]
    public void TheCommandsTheOptionsBarBindsToExist()
    {
        var vm = WithStrokes(Line(100, 100, 200, 100));
        Assert.True(vm.PickStrokeAt(150, 100, 6));

        vm.RecolourSelectedLinesCommand.Execute(null);
        Assert.Equal(vm.ForegroundColorHex, StrokesNow(vm)[0].Color);

        vm.DeleteSelectedLinesCommand.Execute(null);
        Assert.Empty(StrokesNow(vm));
    }

    // ---- the gate ------------------------------------------------------------

    /// <summary>
    /// A locked layer refuses all three, and says so rather than doing nothing.
    /// </summary>
    [AvaloniaFact]
    public void ALockedLayerRefusesEveryAction()
    {
        var vm = WithStrokes(Line(100, 100, 200, 100));
        Assert.True(vm.PickStrokeAt(150, 100, 6));
        vm.Doc.Scene.Layers[^1].Locked = true;

        Assert.Equal(0, vm.DeleteSelectedStrokes());
        Assert.Equal(0, vm.MoveSelectedStrokes(10, 10));
        Assert.Equal(0, vm.RecolourSelectedStrokes("#ff0000"));

        var untouched = StrokesNow(vm)[0];
        Assert.Equal(100, untouched.Points[0].X, 9);
        Assert.Equal("#000000", untouched.Color);
        Assert.Single(StrokesNow(vm));
    }
}
