using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Tests;

/// <summary>
/// Ctrl+Shift+click on the canvas makes the layer you clicked on the active one.
/// </summary>
/// <remarks>
/// <para>
/// Asked for 2026-10-12: "a quick find from canvas to layer". With a dozen layers
/// the one a mark lives on is found by eye in the Layers docker, by toggling
/// visibility until the mark blinks. The canvas already knows.
/// </para>
/// <para>
/// <b>The layer that is picked is the topmost one with something visible there</b>:
/// what the eye is on. A hidden layer is not under the pointer as far as the artist
/// can see, and the background is paper, not a place a mark lives.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class LayerPickFromCanvasTests(Xunit.ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>
    /// Two painted layers: "lower" with a line along y=50 and one along y=150,
    /// "upper" with a line along y=150 (over the lower one) and one along y=250.
    /// </summary>
    private static (MainViewModel Vm, Layer Lower, Layer Upper) TwoLayers()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.NewDocument(new NewDocumentSettings("Pick", 400, 300, 12, 72, "#ffffff", false));
        var lower = Active(vm);
        Line(vm, 50, 50, 150, 50);
        Line(vm, 50, 150, 150, 150);

        vm.AddPaintedLayerCommand.Execute(null);
        var upper = Active(vm);
        Assert.NotSame(lower, upper);
        Line(vm, 50, 150, 150, 150);
        Line(vm, 50, 250, 150, 250);
        return (vm, lower, upper);
    }

    private static Layer Active(MainViewModel vm) => vm.Doc.Scene.Layers[vm.ActiveLayerIndex];

    private static void Line(MainViewModel vm, double x0, double y0, double x1, double y1)
    {
        vm.BeginStroke(x0, y0, 1);
        vm.MoveStroke((x0 + x1) / 2, (y0 + y1) / 2, 1);
        vm.MoveStroke(x1, y1, 1);
        vm.EndStroke();
    }

    [AvaloniaFact]
    public void AClickOnALayersInkMakesThatLayerActive()
    {
        var (vm, lower, upper) = TwoLayers();
        Assert.Same(upper, Active(vm));

        Assert.True(vm.PickLayerAt(100, 50, tolerance: 0));
        Assert.Same(lower, Active(vm));

        Assert.True(vm.PickLayerAt(100, 250, tolerance: 0));
        Assert.Same(upper, Active(vm));
    }

    [AvaloniaFact]
    public void WhereTwoLayersOverlapTheUpperOneIsPicked()
    {
        var (vm, lower, upper) = TwoLayers();
        vm.PickLayerAt(100, 50, tolerance: 0);
        Assert.Same(lower, Active(vm));

        Assert.True(vm.PickLayerAt(100, 150, tolerance: 0));

        Assert.Same(upper, Active(vm));
    }

    [AvaloniaFact]
    public void AClickOnBarePaperChangesNothing()
    {
        var (vm, _, upper) = TwoLayers();

        Assert.False(vm.PickLayerAt(300, 100, tolerance: 4));

        Assert.Same(upper, Active(vm));
    }

    /// <summary>
    /// A hidden layer is not under the pointer as far as the artist can see: the
    /// click goes through it to what is.
    /// </summary>
    [AvaloniaFact]
    public void AHiddenLayerIsLookedThrough()
    {
        var (vm, lower, upper) = TwoLayers();
        vm.SetLayerVisible(upper, false);

        Assert.True(vm.PickLayerAt(100, 150, tolerance: 0));
        Assert.Same(lower, Active(vm));

        // And where only the hidden layer has ink, there is nothing to pick.
        Assert.False(vm.PickLayerAt(100, 250, tolerance: 0));
        Assert.Same(lower, Active(vm));
    }

    /// <summary>
    /// A thin line does not have to be hit to the pixel — and a near miss on an upper
    /// layer does not take the click from a lower layer the pointer is squarely on.
    /// </summary>
    [AvaloniaFact]
    public void ANearMissCountsButADirectHitWins()
    {
        var (vm, lower, upper) = TwoLayers();

        // 6 px above the upper layer's line at y=250, past the 3 px half-width of the mark.
        Assert.False(vm.PickLayerAt(100, 244, tolerance: 0));
        Assert.True(vm.PickLayerAt(100, 244, tolerance: 5));
        Assert.Same(upper, Active(vm));

        // Now put the upper layer's ink 6 px from a spot squarely on the lower layer's.
        Line(vm, 50, 56, 150, 56);
        Assert.True(vm.PickLayerAt(100, 49, tolerance: 8));
        output.WriteLine($"on the lower line, 7 px from the upper one: picked {(ReferenceEquals(Active(vm), lower) ? "lower" : "upper")}");
        Assert.Same(lower, Active(vm));
    }

    /// <summary>Finding a layer is looking, not editing: the document is untouched.</summary>
    [AvaloniaFact]
    public void PickingDoesNotTouchTheDocument()
    {
        var (vm, _, _) = TwoLayers();
        var before = DocJson.Serialize(vm.Doc);
        var undoable = vm.CanUndo;

        vm.PickLayerAt(100, 50, tolerance: 4);
        vm.PickLayerAt(300, 100, tolerance: 4);

        Assert.Equal(before, DocJson.Serialize(vm.Doc));
        Assert.Equal(undoable, vm.CanUndo);
    }

    [AvaloniaFact]
    public void OffThePaperIsNothing()
    {
        var (vm, _, upper) = TwoLayers();

        Assert.False(vm.PickLayerAt(-50, -50, tolerance: 4));
        Assert.False(vm.PickLayerAt(5000, 5000, tolerance: 4));
        Assert.Same(upper, Active(vm));
    }

    // ---- the gesture ----------------------------------------------------------------

    private static readonly Pointer Mouse = new(1, PointerType.Mouse, true);

    private static PointerPressedEventArgs Press(Window w, Control t, Point at, KeyModifiers held) =>
        new(t, Mouse, w, t.TranslatePoint(at, w) ?? at, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            held);

    /// <summary>
    /// With the brush in hand, Ctrl+Shift+press asks for the layer — and neither
    /// paints nor fetches a colour, which is what it fell into before.
    /// </summary>
    [AvaloniaFact]
    public void CtrlShiftPressAsksForTheLayerAndNothingElse()
    {
        var window = new MainWindow { Width = 1200, Height = 900 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        vm.OpenDocumentTab(DocumentFactory.CreateDoc(paperColor: Scene.DefaultBackgroundColor), null);
        var canvas = window.GetVisualDescendants().OfType<CanvasControl>().First();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        int layerAsked = 0, colourAsked = 0, painted = 0;
        canvas.LayerPickClicked += (_, _, _) => layerAsked++;
        canvas.PickClicked += (_, _) => colourAsked++;
        canvas.PaintStarted += (_, _, _, _, _) => painted++;
        var at = new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2);

        canvas.RaiseEvent(Press(window, canvas, at, KeyModifiers.Control | KeyModifiers.Shift));
        output.WriteLine($"Ctrl+Shift: layer {layerAsked}, colour {colourAsked}, paint {painted}");
        Assert.Equal((1, 0, 0), (layerAsked, colourAsked, painted));

        // Ctrl alone is still the eyedropper.
        canvas.RaiseEvent(Press(window, canvas, at, KeyModifiers.Control));
        output.WriteLine($"Ctrl: layer {layerAsked}, colour {colourAsked}, paint {painted}");
        Assert.Equal((1, 1, 0), (layerAsked, colourAsked, painted));
    }

    private static PointerReleasedEventArgs Release(Window w, Control t, Point at, KeyModifiers held) =>
        new(t, Mouse, w, t.TranslatePoint(at, w) ?? at, 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            held, MouseButton.Left);

    private static PointerEventArgs Move(Window w, Control t, Point at, KeyModifiers held) =>
        new(InputElement.PointerMovedEvent, t, Mouse, w, t.TranslatePoint(at, w) ?? at, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other),
            held);

    /// <summary>
    /// Q256. With the Move tool Ctrl already drags the whole layer and Shift holds
    /// the drag to an axis, so there the press starts the move as it always did: a
    /// release that went nowhere is the click that finds the layer, and a drag is
    /// still a move and finds nothing.
    /// </summary>
    [AvaloniaFact]
    public void WithTheMoveToolAClickPicksAndADragStillMoves()
    {
        var window = new MainWindow { Width = 1200, Height = 900 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        vm.OpenDocumentTab(DocumentFactory.CreateDoc(paperColor: Scene.DefaultBackgroundColor), null);
        var canvas = window.GetVisualDescendants().OfType<CanvasControl>().First();
        vm.ActiveTool = ToolId.Move;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(CanvasControl.CanvasToolMode.Move, canvas.ToolMode);

        int picked = 0, started = 0, ended = 0, cancelled = 0;
        canvas.LayerPickClicked += (_, _, _) => picked++;
        canvas.ContentMoveStarted += (_, _, _) => started++;
        canvas.ContentMoveEnded += () => ended++;
        canvas.ContentMoveCancelled += () => cancelled++;
        var both = KeyModifiers.Control | KeyModifiers.Shift;
        var at = new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2);

        // A click: press and release in the same place.
        canvas.RaiseEvent(Press(window, canvas, at, both));
        Assert.Equal(0, picked);                       // not yet: it might be a drag
        canvas.RaiseEvent(Release(window, canvas, new Point(at.X + 1, at.Y), both));
        output.WriteLine($"click: picked {picked}, move started {started}, ended {ended}, cancelled {cancelled}");
        Assert.Equal(1, picked);
        Assert.Equal(0, ended);                        // the move it began is taken back, not committed
        Assert.Equal(started, cancelled);

        // A drag: the whole-layer move, and no pick.
        (picked, started, ended, cancelled) = (0, 0, 0, 0);
        canvas.RaiseEvent(Press(window, canvas, at, both));
        canvas.RaiseEvent(Move(window, canvas, new Point(at.X + 40, at.Y + 2), both));
        canvas.RaiseEvent(Release(window, canvas, new Point(at.X + 40, at.Y + 2), both));
        output.WriteLine($"drag: picked {picked}, move started {started}, ended {ended}, cancelled {cancelled}");
        Assert.Equal(0, picked);
        Assert.Equal(started, ended);
        Assert.Equal(0, cancelled);
    }

    /// <summary>
    /// The lock is on the active layer, and the pick is about to change which layer
    /// that is: the pointer must not forbid a click that works.
    /// </summary>
    [Fact]
    public void ALockedActiveLayerDoesNotForbidThePick()
    {
        var locked = new CanvasTarget { LayerLocked = true };
        var hidden = new CanvasTarget { LayerHidden = true };
        var both = KeyModifiers.Control | KeyModifiers.Shift;

        Assert.Equal(CanvasCursorKind.Forbidden, CanvasCursor.For(ToolId.Brush, locked));
        Assert.Equal(CanvasCursorKind.Default, CanvasCursor.For(ToolId.Brush, locked, both));
        Assert.Equal(CanvasCursorKind.Default, CanvasCursor.For(ToolId.Fill, hidden, both));
        Assert.Null(CanvasCursor.Refusal(ToolId.Brush, locked, both));
        Assert.NotNull(CanvasCursor.Refusal(ToolId.Brush, locked));
    }

    /// <summary>
    /// Where the press will start a move, the pointer says move: with the Move tool,
    /// and inside a marquee with any tool.
    /// </summary>
    [Fact]
    public void WhereCtrlShiftStartsAMoveThePointerSaysMove()
    {
        var both = KeyModifiers.Control | KeyModifiers.Shift;
        var insideAMarquee = new CanvasTarget { SelectionToMove = true };

        Assert.Equal(CanvasCursorKind.Move, CanvasCursor.For(ToolId.Move, default, both));
        Assert.Equal(CanvasCursorKind.Move, CanvasCursor.For(ToolId.Brush, insideAMarquee, both));
        Assert.False(CanvasCursor.PicksLayer(ToolId.Move, both));
        Assert.False(CanvasCursor.PicksLayer(ToolId.Brush, both, insideAMarquee));
        Assert.True(CanvasCursor.PicksLayer(ToolId.Brush, both));
        Assert.False(CanvasCursor.PicksLayer(ToolId.Brush, KeyModifiers.Control));
    }

    /// <summary>
    /// The pointer does not promise an eyedropper for a click that will not fetch a
    /// colour.
    /// </summary>
    [Fact]
    public void ThePointerDoesNotPromiseAnEyedropperForCtrlShift()
    {
        Assert.Equal(ToolId.Picker, CanvasCursor.Effective(ToolId.Brush, KeyModifiers.Control));
        Assert.NotEqual(
            ToolId.Picker,
            CanvasCursor.Effective(ToolId.Brush, KeyModifiers.Control | KeyModifiers.Shift));
    }
}
