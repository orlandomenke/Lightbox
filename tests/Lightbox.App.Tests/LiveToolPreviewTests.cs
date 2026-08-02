using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Brushwork must look, while you are making it, the way it will look when you
/// let go. Anything that only resolves on pen-up is guesswork — the rule the
/// wet-media work established, applied to the tools that still broke it.
/// </summary>
[Collection("BrushState")]
public class LiveToolPreviewTests : BrushStateIsolated
{
    private static MainViewModel Painted()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 40;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        vm.BrushWetEdge = 0;
        vm.BrushGranulation = 0;
        vm.BrushScatter = 0;
        vm.AntiAliasing = false;

        // A hard black bar to smear or blur.
        vm.BeginStroke(60, 100, 1);
        vm.MoveStroke(300, 100, 1);
        vm.EndStroke();
        return vm;
    }

    /// <summary>Select a built-in preset by name — how the brush kind is chosen in the app.</summary>
    private static void Pick(MainViewModel vm, string preset)
    {
        vm.SelectedBrushPreset = vm.BrushPresetChoices.First(p => p.Name == preset);
        vm.BrushSize = 40;
        vm.BrushHardness = 1;
        vm.BrushFlow = 1;
        vm.BrushSpacing = 0.08;
    }

    private static SKColor PixelAt(RenderSnapshot snapshot, int x, int y)
    {
        using var bmp = SKBitmap.FromImage(snapshot.Image);
        return bmp.GetPixel(x, y);
    }

    /// <summary>B4 — the smear has to appear during the drag, not on release.</summary>
    [AvaloniaFact]
    public void SmudgeShowsMidDrag()
    {
        var vm = Painted();
        Pick(vm, "Smudge");
        vm.BrushSmudgeLength = 0.9;

        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        vm.PublishSnapshot();
        using (var before = SKBitmap.FromImage(latest!.Image))
        {
            // Empty just under the bar, which is where the smear will drag ink.
            Assert.Equal(0, before.GetPixel(180, 150).Alpha);
        }

        // Drag downward out of the bar and STOP — no EndStroke.
        vm.BeginStroke(180, 100, 1);
        vm.MoveStroke(180, 125, 1);
        vm.MoveStroke(180, 150, 1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // flush the coalesced publish

        using var during = SKBitmap.FromImage(latest!.Image);
        Assert.True(during!.GetPixel(180, 150).Alpha > 0,
            "the smudge did not reach the canvas until the pen lifted");
    }

    /// <summary>B4 — and what it showed is what the commit produces.</summary>
    [AvaloniaFact]
    public void TheSmudgePreviewMatchesTheCommit()
    {
        var vm = Painted();
        Pick(vm, "Smudge");
        vm.BrushSmudgeLength = 0.9;

        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        vm.BeginStroke(180, 100, 1);
        vm.MoveStroke(180, 125, 1);
        vm.MoveStroke(180, 150, 1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var preview = PixelAt(latest!, 180, 140);

        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var committed = PixelAt(latest!, 180, 140);

        // Both inked, and close enough that the artist was not being shown a
        // different mark from the one they were making.
        Assert.True(preview.Alpha > 0 && committed.Alpha > 0);
        Assert.InRange(Math.Abs(preview.Alpha - committed.Alpha), 0, 12);
    }

    /// <summary>B4 — blur is the same class of tool and the same failure.</summary>
    [AvaloniaFact]
    public void BlurShowsMidDrag()
    {
        var vm = Painted();
        Pick(vm, "Blur");

        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        vm.PublishSnapshot();
        // The bar's edge is hard: one row is opaque, the row below is empty.
        using (var before = SKBitmap.FromImage(latest!.Image))
        {
            Assert.Equal(0, before.GetPixel(180, 128).Alpha);
        }

        vm.BeginStroke(180, 118, 1);
        vm.MoveStroke(182, 120, 1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        using var during = SKBitmap.FromImage(latest!.Image);
        Assert.True(during!.GetPixel(180, 128).Alpha > 0,
            "blur did not soften the edge until the pen lifted");
    }

    /// <summary>
    /// A smudge abandoned mid-drag must leave nothing behind.
    ///
    /// Showing the composite instead of the layer is a new way to be wrong:
    /// a stale copy left on screen after the stroke went away would look like
    /// paint the record does not contain. Playback is the real path that
    /// abandons a stroke, so it is what this drives.
    /// </summary>
    [AvaloniaFact]
    public void AnAbandonedSmudgeLeavesNoTrace()
    {
        var vm = Painted();
        var strokes = ((PaintedFrame)vm.PaintLayer().Cels[0].Frame!).Strokes.Count;
        Pick(vm, "Smudge");
        vm.BrushSmudgeLength = 0.9;

        vm.BeginStroke(180, 100, 1);
        vm.MoveStroke(180, 150, 1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.PlayCommand.Execute(null);  // abandons the stroke
        vm.PauseCommand.Execute(null);

        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        vm.PublishSnapshot();

        Assert.Equal(strokes, ((PaintedFrame)vm.PaintLayer().Cels[0].Frame!).Strokes.Count);
        Assert.Equal(0, PixelAt(latest!, 180, 150).Alpha);
    }
}
