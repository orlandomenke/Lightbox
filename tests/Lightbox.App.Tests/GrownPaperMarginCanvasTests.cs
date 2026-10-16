using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The grown margin of a locked Background, as the canvas shows it and as
/// undo takes it back (Q283).
/// </summary>
/// <remarks>
/// The rule itself is pinned in <c>GrownPaperMarginTests</c> in the Core
/// suite; this is the half only the view model can answer — that the fill
/// arrives in the same undo step as the resize, so one undo leaves neither
/// behind, and that the margin really composites as paper rather than as
/// checkerboard. The paper grows right and down so the origin stays at zero
/// and the test reads the fill, not B479's anchor handling.
/// </remarks>
[Collection("BrushState")]
public class GrownPaperMarginCanvasTests(Xunit.ITestOutputHelper output) : BrushStateIsolated
{
    private static void Pump() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    private static (MainViewModel Vm, Func<SKBitmap> Shown) Open(string paper = "#808080")
    {
        var vm = new MainViewModel();
        vm.NewDocument(new NewDocumentSettings("Margin", 64, 64, 12, 72, paper, false));
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        return (vm, () =>
        {
            Pump();
            Assert.NotNull(latest);
            return SKBitmap.FromImage(latest!.Image);
        });
    }

    private static bool GrowRightAndDown(MainViewModel vm)
    {
        var choice = new ResizeDialogViewModel(vm.Doc.Scene, ResizeMode.Canvas)
        {
            Anchor = ResizeAnchor.TopLeft,
        };
        choice.Width = 96;
        choice.Height = 80;
        return vm.ApplyResize(choice);
    }

    private static Layer Paper(MainViewModel vm) => vm.Doc.Scene.Layers.Single(l => l.IsBackground);

    [AvaloniaFact]
    public void TheGrownMarginOfALockedBackgroundIsPaperOnTheCanvas()
    {
        var (vm, shown) = Open();
        Assert.True(Paper(vm).Locked);

        Assert.True(GrowRightAndDown(vm));

        using var bmp = shown();
        var margin = bmp.GetPixel(88, 74);
        var old = bmp.GetPixel(10, 10);
        output.WriteLine($"margin {margin}, old paper {old}, image {bmp.Width}x{bmp.Height}");
        Assert.Equal(255, margin.Alpha);
        Assert.Equal(old, margin);
    }

    [AvaloniaFact]
    public void AnUnlockedBackgroundLeavesTheMarginTransparent()
    {
        var (vm, shown) = Open();
        Paper(vm).Locked = false;

        Assert.True(GrowRightAndDown(vm));

        using var bmp = shown();
        var margin = bmp.GetPixel(88, 74);
        output.WriteLine($"margin {margin}");
        Assert.Equal(0, margin.Alpha);
        Assert.Single(Paper(vm).Cels[0].Frame!.Strokes);
    }

    [AvaloniaFact]
    public void ASeeThroughBackgroundStaysSeeThroughInsideTheOldPaper()
    {
        // Unlocked, the paper taken off, and locked again: the margin is
        // paper, but what was transparent before the grow still is (the
        // guardian's review — the fill is the margin, never the whole sheet).
        var (vm, shown) = Open();
        Paper(vm).Cels[0].Frame = new Frame();

        Assert.True(GrowRightAndDown(vm));

        using var bmp = shown();
        var inside = bmp.GetPixel(10, 10);
        var margin = bmp.GetPixel(88, 74);
        output.WriteLine($"inside {inside}, margin {margin}");
        Assert.Equal(0, inside.Alpha);
        Assert.Equal(255, margin.Alpha);
    }

    [AvaloniaFact]
    public void OneUndoTakesBackTheResizeAndTheFillTogether()
    {
        var (vm, _) = Open();
        var undoableBefore = vm.CanUndo;

        Assert.True(GrowRightAndDown(vm));
        Assert.Equal(2, Paper(vm).Cels[0].Frame!.Strokes.Count);

        vm.UndoCommand.Execute(null);

        Assert.Equal((64, 64), (vm.Doc.Scene.Width, vm.Doc.Scene.Height));
        Assert.Single(Paper(vm).Cels[0].Frame!.Strokes);
        // One step: whatever was undoable before the resize is all that is left.
        Assert.Equal(undoableBefore, vm.CanUndo);

        vm.RedoCommand.Execute(null);

        Assert.Equal((96, 80), (vm.Doc.Scene.Width, vm.Doc.Scene.Height));
        Assert.Equal(2, Paper(vm).Cels[0].Frame!.Strokes.Count);
    }
}
