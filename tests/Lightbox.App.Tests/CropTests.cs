using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// The two Image-menu crops: the paper moved to a rectangle, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q126 is what most of this file is guarding</b>, and it is the half that
/// would ship green if it were wrong. A crop that also deleted the marks
/// outside the new edge would look correct in every screenshot: the canvas is
/// the right size, the drawing is where it was, nothing is red. The loss only
/// shows up when the canvas is grown again and the ink does not come back —
/// or, worse, comes back with different grain, because <c>Hash01</c> seeds
/// every dab dynamic from the bits of its position.
/// </para>
/// <para>
/// The other half is the trim's subject: <em>every</em> frame of every layer,
/// and never the paper. Both are mistakes that produce a plausible number.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class CropTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel Vm()
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        return vm;
    }

    /// <summary>A rectangular marquee, in document coordinates.</summary>
    private static void Marquee(MainViewModel vm, double x, double y, double w, double h) =>
        vm.ApplySelectionShape(
        [
            new StrokePoint(x, y, 1), new StrokePoint(x + w, y, 1),
            new StrokePoint(x + w, y + h, 1), new StrokePoint(x, y + h, 1),
        ], add: false, subtract: false);

    private static Stroke Ink(double x1, double y1, double x2, double y2) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#2050b0",
        Points = [new StrokePoint(x1, y1, 1), new StrokePoint(x2, y2, 1)],
        Brush = new BrushSettings { Size = 4, Opacity = 1, Flow = 1 },
    };

    private static string Rect(Scene s) => $"{s.Width}×{s.Height} at ({s.Left}, {s.Top})";

    // ---- crop to selection ---------------------------------------------------------

    [AvaloniaFact]
    public void CroppingTakesThePaperToTheMarqueesBounds()
    {
        var vm = Vm();
        Marquee(vm, 100, 60, 400, 300);

        Assert.True(vm.CropToSelection());

        output.WriteLine($"after the crop: {Rect(vm.Doc.Scene)}");
        Assert.Equal(400, vm.Doc.Scene.Width);
        Assert.Equal(300, vm.Doc.Scene.Height);
        Assert.Equal(100, vm.Doc.Scene.Left);
        Assert.Equal(60, vm.Doc.Scene.Top);
    }

    /// <summary>
    /// A marquee dragged off the paper crops to the paper that is there.
    /// </summary>
    /// <remarks>
    /// Nothing stops a selection reaching past the canvas — the contours are
    /// document coordinates — and honouring it literally would <em>grow</em>
    /// the canvas, which is the one thing the word "crop" promises not to do.
    /// Growing is <c>Resize canvas</c>, one item up the same menu.
    /// </remarks>
    [AvaloniaFact]
    public void AMarqueeHangingOffThePaperCropsToWhatIsActuallyThere()
    {
        var vm = Vm();
        Marquee(vm, -200, -150, 700, 500);

        Assert.True(vm.CropToSelection());

        output.WriteLine($"after the crop: {Rect(vm.Doc.Scene)}");
        // Left and top clamp to the corner; the right and bottom edges of the
        // marquee (500, 350) are inside the 960×540 page and are kept.
        Assert.Equal(0, vm.Doc.Scene.Left);
        Assert.Equal(0, vm.Doc.Scene.Top);
        Assert.Equal(500, vm.Doc.Scene.Width);
        Assert.Equal(350, vm.Doc.Scene.Height);
    }

    [AvaloniaFact]
    public void WithNothingSelectedTheCropRefusesAndSaysWhy()
    {
        var vm = Vm();

        Assert.False(vm.CropToSelection());

        output.WriteLine(vm.AiStatus);
        Assert.Equal(960, vm.Doc.Scene.Width);
        Assert.Contains("Select an area", vm.AiStatus);
    }

    /// <summary>
    /// Cropping to the page it already is changes nothing — and, crucially,
    /// leaves the redo stack alone.
    /// </summary>
    /// <remarks>
    /// The obvious implementation performs the edit and discards the step when
    /// it turns out to have done nothing. That is honest about the undo entry
    /// and still destroys work: <c>PushStep</c> clears the redo stack, so a
    /// crop that changed nothing would silently throw away everything the
    /// artist could have redone. Hence the no-op is caught before the push,
    /// and this is the assertion that says so.
    /// </remarks>
    [AvaloniaFact]
    public void ACropThatChangesNothingKeepsTheRedoStack()
    {
        var vm = Vm();
        vm.PaintStrokes().Add(Ink(20, 20, 60, 60));
        vm.AddFrameCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        Assert.True(vm.CanRedo);

        Marquee(vm, 0, 0, 960, 540);
        Assert.False(vm.CropToSelection());

        output.WriteLine($"{vm.AiStatus} — canRedo={vm.CanRedo}");
        Assert.Contains("already fits", vm.AiStatus);
        Assert.True(vm.CanRedo);
    }

    // ---- what a crop must not touch -------------------------------------------------

    /// <summary>
    /// Q126: the marks outside the new edge stay exactly where they were drawn.
    /// </summary>
    [AvaloniaFact]
    public void MarksOutsideTheNewEdgeAreKeptWhereTheyWere()
    {
        var vm = Vm();
        var strokes = vm.PaintStrokes();
        strokes.Add(Ink(20, 30, 80, 90));    // outside the crop
        strokes.Add(Ink(250, 200, 300, 260)); // inside it
        var before = strokes.SelectMany(s => s.Points).Select(p => (p.X, p.Y)).ToList();

        Marquee(vm, 200, 150, 200, 150);
        Assert.True(vm.CropToSelection());

        var after = vm.PaintStrokes().SelectMany(s => s.Points).Select(p => (p.X, p.Y)).ToList();
        output.WriteLine($"{vm.PaintStrokes().Count} strokes, {after.Count} points, {Rect(vm.Doc.Scene)}");
        // Both strokes still there, every coordinate untouched. Cropping and
        // growing back is therefore lossless, which a destructive crop could
        // never be — a mark deleted and drawn again is a different mark.
        Assert.Equal(2, vm.PaintStrokes().Count);
        Assert.Equal(before, after);
    }

    [AvaloniaFact]
    public void TheCropIsOneUndoStepAndUndoPutsThePaperBack()
    {
        var vm = Vm();
        Marquee(vm, 100, 60, 400, 300);
        Assert.True(vm.CropToSelection());

        vm.UndoCommand.Execute(null);

        output.WriteLine($"after undo: {Rect(vm.Doc.Scene)}");
        Assert.Equal(960, vm.Doc.Scene.Width);
        Assert.Equal(540, vm.Doc.Scene.Height);
        Assert.Equal(0, vm.Doc.Scene.Left);
    }

    // ---- trim to drawing ------------------------------------------------------------

    /// <summary>
    /// The trim measures every frame, not the one on screen.
    /// </summary>
    /// <remarks>
    /// The unit of work here is a sequence. A trim that measured the current
    /// cel would cut the ink off frames 2 to 200, and it would do it silently,
    /// because nothing about the drawing in front of you says how far the
    /// others reach.
    /// </remarks>
    [AvaloniaFact]
    public void TheTrimMeasuresEveryFrameRatherThanTheOneOnScreen()
    {
        var vm = Vm();
        var layer = vm.PaintLayer();
        layer.Cels[0].Frame!.Strokes.Add(Ink(300, 300, 320, 320));
        layer.Cels.Add(new Cel { Frame = new Frame { Strokes = { Ink(700, 100, 720, 120) } } });

        Assert.True(vm.TrimToDrawing());

        var scene = vm.Doc.Scene;
        output.WriteLine($"after the trim: {Rect(scene)}");
        // The union of both cels, plus each brush's own reach past its path.
        // Asserted as a containment rather than an exact rectangle: the margin
        // is the brush's business and pinning it here would make this test fail
        // for a change in dab reach that is nothing to do with cropping.
        Assert.True(scene.Left <= 300 && scene.Top <= 100, $"left/top too tight: {Rect(scene)}");
        Assert.True(scene.Right >= 720 && scene.Bottom >= 320, $"right/bottom too tight: {Rect(scene)}");
        // And it is genuinely a trim: smaller than the page it started on.
        Assert.True(scene.Width < 960 && scene.Height < 540, $"nothing was trimmed: {Rect(scene)}");
    }

    /// <summary>
    /// The paper does not count as drawing, and without that the command does
    /// nothing on any document anybody makes.
    /// </summary>
    /// <remarks>
    /// <c>DocumentFactory.BackgroundLayer</c> is a real fill stroke whose
    /// contour is the four corners of the canvas — that is what makes unlocking
    /// the paper and painting on it work. It also means a trim that counted it
    /// would measure the paper, find it exactly as large as the paper, and
    /// report that there was nothing to do, on every papered document.
    /// </remarks>
    [AvaloniaFact]
    public void ThePaperIsNotTheDrawing()
    {
        var vm = Vm();
        Assert.Contains(vm.Doc.Scene.Layers, l => l.IsBackground);
        // Nothing but paper: the command has nothing to measure and says so
        // rather than reporting a successful no-op.
        Assert.False(vm.HasAnyContent);
        Assert.False(vm.TrimToDrawing());
        output.WriteLine(vm.AiStatus);
        Assert.Contains("Nothing is drawn", vm.AiStatus);

        vm.PaintStrokes().Add(Ink(400, 250, 440, 290));
        Assert.True(vm.HasAnyContent);

        Assert.True(vm.TrimToDrawing());
        output.WriteLine($"one mark on a papered document: {Rect(vm.Doc.Scene)}");
        Assert.True(vm.Doc.Scene.Width < 200, $"the paper was measured too: {Rect(vm.Doc.Scene)}");
    }
}
