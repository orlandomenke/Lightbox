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

    // ---- B33 · the live blur covered more ground than the commit ---------------

    /// <summary>Pixels with any ink in them.</summary>
    /// <remarks>
    /// Coordinate-free on purpose. Two earlier probes measured the wrong thing
    /// — one walked sideways along the opaque bar and measured the bar, the
    /// other dragged along the bar's middle where blurring uniform black is a
    /// no-op by construction. Both passed against the unfixed code. Counting
    /// covered pixels asks the artist's question directly: how much ground does
    /// the mark cover?
    /// </remarks>
    private static int InkedPixels(RenderSnapshot snapshot)
    {
        using var bmp = SKBitmap.FromImage(snapshot.Image);
        var n = 0;
        for (var y = 0; y < bmp.Height; y++)
            for (var x = 0; x < bmp.Width; x++)
                if (bmp.GetPixel(x, y).Alpha > 8) n++;
        return n;
    }

    /// <summary>
    /// A long blur drag must not cover visibly more ground on screen than the
    /// mark that commits.
    /// </summary>
    /// <remarks>
    /// The artist's report was that the affected area is bigger than the brush
    /// while dragging. It was: the live composite was handed to the engine as
    /// both the surface to write and the pixels to read, so each pointer event
    /// blurred the previous event's blur. The exact render gives every dab of a
    /// stroke the same pre-stroke pixels — one pass per stroke, not one per
    /// event — and N passes of sigma s reach like sigma*sqrt(N).
    ///
    /// Measured over 60 events on a 40 px brush at flow 0.6: the preview
    /// covered **672 px more** than the commit before the pristine-base fix,
    /// **88 px more** after it, and **-2 px** — noise — once B37 stopped the
    /// blur compositing a second copy of its snapshot over the first. Both
    /// were the same accumulation seen from different ends.
    ///
    /// The threshold is 20 rather than 200 for that reason. It was loosened to
    /// 200 to accommodate a residual that has since turned out not to exist,
    /// and a threshold with 200 px of slack in it would not notice either bug
    /// coming back.
    ///
    /// Note the drag runs along the bar's EDGE. Along its middle there is no
    /// gradient and blur is a no-op, which is how two earlier versions of this
    /// test managed to pass against the bug.
    /// </remarks>
    [AvaloniaFact]
    public void ALiveBlurDoesNotCoverMoreGroundThanTheMarkThatCommits()
    {
        var vm = Painted();
        Pick(vm, "Blur");
        vm.BrushFlow = 0.6;   // high enough that compounding is unmistakable

        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        vm.BeginStroke(120, 118, 1);
        for (var x = 122; x <= 240; x += 2) vm.MoveStroke(x, 118, 1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var preview = InkedPixels(latest!);

        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var committed = InkedPixels(latest!);

        // Both numbers, always. An assertion that passes tells you nothing
        // about how close it came.
        Assert.True(
            preview - committed <= 20,
            $"the live blur covered {preview} px against the commit's {committed} "
            + $"({preview - committed} more) — the preview is applying the blur more times "
            + "than the commit does");
    }

    /// <summary>
    /// The defaults for the three effect brushes are low, because flow on these
    /// is how hard each dab pulls and the dabs overlap ten deep.
    /// </summary>
    /// <remarks>
    /// Guarded rather than merely set: a default nudged back up would be
    /// invisible in every other test, and it is the one number that decides
    /// whether these tools are steerable at all.
    /// </remarks>
    [AvaloniaFact]
    public void TheEffectBrushesShipWithAFlowAnArtistCanSteer()
    {
        var vm = VmLayers.BareVm();
        foreach (var name in new[] { "Smudge", "Blender", "Blur" })
        {
            var preset = vm.BrushPresetChoices.First(p => p.Name == name);
            Assert.True(
                preset.Settings.Flow <= 0.1,
                $"{name} ships with flow {preset.Settings.Flow:F2}; effect brushes need 0.1 or less");
            Assert.True(preset.Settings.Flow > 0, $"{name} ships with no flow at all");
        }
    }
}
