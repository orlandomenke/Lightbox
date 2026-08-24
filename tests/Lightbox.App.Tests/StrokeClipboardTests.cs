using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Copying and pasting lines through the selection tools. The rule the whole
/// feature is built on: <b>a copy takes exactly what a transform would move</b>
/// — same precedence (Q97, marquee over picked lines), same visible-ink test
/// (B297), so there is only ever one answer to "what is selected" and the
/// clipboard cannot become a fifth door into the erased-ink bug.
/// </summary>
[Collection("BrushState")]
public class StrokeClipboardTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel Painted()
    {
        StrokeClipboard.Clear();
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 30;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        vm.AntiAliasing = false;
        return vm;
    }

    private static void Drag(MainViewModel vm, params (double X, double Y)[] points)
    {
        vm.BeginStroke(points[0].X, points[0].Y, 1);
        for (var i = 1; i < points.Length; i++) vm.MoveStroke(points[i].X, points[i].Y, 1);
        vm.EndStroke();
    }

    private static SKColor PixelAt(MainViewModel vm, int x, int y)
    {
        RenderSnapshot? latest = null;
        void Capture(RenderSnapshot s) => latest = s;
        vm.SnapshotChanged += Capture;
        vm.PublishSnapshot();
        vm.SnapshotChanged -= Capture;
        using var bmp = SKBitmap.FromImage(latest!.Image);
        return bmp.GetPixel(x, y);
    }

    private static void Box(MainViewModel vm, double x0, double y0, double x1, double y1) =>
        vm.ApplySelectionShape(
            [new(x0, y0, 1), new(x1, y0, 1), new(x1, y1, 1), new(x0, y1, 1)],
            add: false, subtract: false);

    private static List<Stroke> StrokesOf(MainViewModel vm, int layerIndex) =>
        ((Frame)vm.Doc.Scene.Layers[layerIndex].Cels[vm.CurrentFrameIndex].Frame!).Strokes;

    // ---- what a copy takes -------------------------------------------------

    [AvaloniaFact]
    public void PastingPutsTheLinesOnANewLayerAboveTheActiveOne()
    {
        var vm = Painted();
        Drag(vm, (60, 60), (240, 60));
        var layersBefore = vm.Doc.Scene.Layers.Count;
        var active = vm.ActiveLayerIndex;

        Box(vm, 20, 20, 280, 120);
        Assert.True(vm.CopySelectedLines());
        Assert.True(vm.PasteLinesAsLayer());

        Assert.Equal(layersBefore + 1, vm.Doc.Scene.Layers.Count);
        Assert.Equal(active + 1, vm.ActiveLayerIndex);
        // In place: the pasted copy sits exactly where the original was, so the
        // ink is still there and the original layer still has its own.
        Assert.True(PixelAt(vm, 150, 60).Alpha > 0);
        Assert.Single(StrokesOf(vm, active));
        Assert.Single(StrokesOf(vm, active + 1));
    }

    [AvaloniaFact]
    public void APastedLineIsItsOwnCopy()
    {
        // Fresh ids, and no shared geometry: editing or deleting the original
        // afterwards must not reach into the paste.
        var vm = Painted();
        Drag(vm, (60, 60), (240, 60));
        var original = StrokesOf(vm, vm.ActiveLayerIndex)[0];

        Box(vm, 20, 20, 280, 120);
        Assert.True(vm.CopySelectedLines());
        var sourceLayer = vm.ActiveLayerIndex;
        Assert.True(vm.PasteLinesAsLayer());

        var pasted = StrokesOf(vm, sourceLayer + 1)[0];
        Assert.NotEqual(original.Id, pasted.Id);
        Assert.NotSame(original.Points, pasted.Points);
    }

    [AvaloniaFact]
    public void TwoPastesAreTwoIndependentDrawings()
    {
        var vm = Painted();
        Drag(vm, (60, 60), (240, 60));
        Box(vm, 20, 20, 280, 120);
        Assert.True(vm.CopySelectedLines());

        Assert.True(vm.PasteLinesAsLayer());
        var first = StrokesOf(vm, vm.ActiveLayerIndex)[0];
        Assert.True(vm.PasteLinesAsLayer());
        var second = StrokesOf(vm, vm.ActiveLayerIndex)[0];

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotSame(first.Points, second.Points);
    }

    // ---- the doctrine ------------------------------------------------------

    [AvaloniaFact]
    public void ACopyNeverTakesInkThatWasErased()
    {
        // The clipboard reads the drawing, not the record (B232/B297). A line
        // rubbed out along its whole length is not on the canvas, so a box
        // drawn over where it used to be copies nothing at all.
        var vm = Painted();
        Drag(vm, (60, 60), (240, 60));
        vm.ActiveTool = ToolId.Eraser;
        vm.BrushSize = 80;
        Drag(vm, (40, 60), (150, 60), (260, 60));
        vm.ActiveTool = ToolId.Brush;
        Assert.Equal(0, PixelAt(vm, 150, 60).Alpha);

        Box(vm, 20, 20, 280, 120);
        var copied = vm.CopySelectedLines();

        output.WriteLine($"copy of a wholly erased area returned {copied}");
        Assert.False(copied);
        Assert.False(vm.PasteLinesAsLayer());
    }

    [AvaloniaFact]
    public void PastingDoesNotBringBackInkTheCopyCouldNotSee()
    {
        // The same rule at the other end: a partial copy must paste the pixels
        // that were visible, not the whole stroke the record still holds.
        var vm = Painted();
        Drag(vm, (60, 60), (240, 60));
        vm.ActiveTool = ToolId.Eraser;
        vm.BrushSize = 60;
        Drag(vm, (200, 60), (250, 60));
        vm.ActiveTool = ToolId.Brush;
        Assert.Equal(0, PixelAt(vm, 225, 60).Alpha);

        Box(vm, 20, 20, 280, 120);
        Assert.True(vm.CopySelectedLines());
        // Hide the original layer, so what is on screen is the paste alone.
        Assert.True(vm.PasteLinesAsLayer());
        var pastedLayer = vm.ActiveLayerIndex;
        for (var i = 0; i < pastedLayer; i++) vm.Doc.Scene.Layers[i].Visible = false;

        Assert.True(PixelAt(vm, 100, 60).Alpha > 0, "the visible part did not paste");
        Assert.Equal(0, PixelAt(vm, 225, 60).Alpha);
    }

    // ---- which selection a copy means --------------------------------------

    [AvaloniaFact]
    public void ABoxedPartOfALinePastesAsTheBoxedPartOnly()
    {
        // The headline behaviour: box half a line and the paste shows that
        // half. The stroke is not torn to do it — it travels whole under the
        // selection as its clip (invariant 3), so the record keeps a line
        // rather than two fragments, and the pixels are exactly what was boxed.
        var vm = Painted();
        Drag(vm, (60, 60), (400, 60));

        Box(vm, 40, 20, 200, 120);              // the left half only
        Assert.True(vm.CopySelectedLines());
        Assert.True(vm.PasteLinesAsLayer());
        var pastedLayer = vm.ActiveLayerIndex;
        for (var i = 0; i < pastedLayer; i++) vm.Doc.Scene.Layers[i].Visible = false;

        var inside = PixelAt(vm, 120, 60).Alpha;
        var outside = PixelAt(vm, 320, 60).Alpha;
        output.WriteLine($"inside the box {inside}, outside it {outside}");
        Assert.True(inside > 0, "the boxed half did not paste");
        Assert.Equal(0, outside);
        // Whole stroke, one clip — not a severed fragment.
        var pasted = Assert.Single(StrokesOf(vm, pastedLayer));
        Assert.NotNull(pasted.ClipId);
        Assert.Equal(400, pasted.Points[^1].X, 0);
    }

    [AvaloniaFact]
    public void APickedLineCopiesWholeAndUnclipped()
    {
        // The other gesture says something different: picking a line with the
        // Arrow means that line, all of it, so no clip is attached.
        var vm = Painted();
        Drag(vm, (60, 60), (400, 60));
        var line = StrokesOf(vm, vm.ActiveLayerIndex)[0];
        vm.Selection.SelectStroke(line.Id);

        Assert.True(vm.CopySelectedLines());
        Assert.True(vm.PasteLinesAsLayer());

        var pasted = Assert.Single(StrokesOf(vm, vm.ActiveLayerIndex));
        Assert.Null(pasted.ClipId);
        Assert.Equal(line.Points.Count, pasted.Points.Count);
    }

    [AvaloniaFact]
    public void CuttingARegionTakesThePixelsOutAndLeavesTheLineWhole()
    {
        // Cut is copy plus DeleteSelectionContents, which for a region records
        // a ClearRegion — so a line crossing the edge keeps the part outside
        // the box. Deleting the strokes the copy took would have removed the
        // whole line instead.
        var vm = Painted();
        Drag(vm, (60, 60), (400, 60));

        Box(vm, 40, 20, 200, 120);
        Assert.True(vm.CutSelectedLines());

        Assert.Equal(0, PixelAt(vm, 120, 60).Alpha);          // the boxed part is gone
        Assert.True(PixelAt(vm, 320, 60).Alpha > 0, "cut took ink from outside the box");
        Assert.True(StrokeClipboard.HasContent);
    }

    [AvaloniaFact]
    public void PasteTakesWhicheverClipboardWasFilledLast()
    {
        // Both clipboards can hold something at once, and the artist means the
        // last thing they copied — not whichever the key happens to ask first.
        var vm = Painted();
        Drag(vm, (60, 60), (240, 60));
        Box(vm, 20, 20, 280, 120);
        Assert.True(vm.CopySelectedLines());
        Assert.True(vm.LinesAreTheFresherClipboard);

        vm.CopyCurrentCel();
        Assert.False(vm.LinesAreTheFresherClipboard);

        Assert.True(vm.CopySelectedLines());
        Assert.True(vm.LinesAreTheFresherClipboard);
    }

    [AvaloniaFact]
    public void WithNoSelectionACopyTakesNothingSoTheCelKeyStillWorks()
    {
        // The false return is load-bearing: it is what lets Ctrl+C fall through
        // to the cel clipboard when no lines are selected.
        var vm = Painted();
        Drag(vm, (60, 60), (240, 60));

        Assert.False(vm.CopySelectedLines());
        Assert.False(vm.CutSelectedLines());
    }
}
