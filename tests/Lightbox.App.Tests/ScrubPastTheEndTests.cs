using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Q90 — the playhead may stand past the end of the scene, and the scene grows
/// when an edit lands there.
/// </summary>
/// <remarks>
/// <para>
/// The scene's length was a gate you had to open before working: to animate at
/// frame twenty you first had to add frames or a hold, because the playhead was
/// pinned inside <c>Scene.FrameCount</c>. On paper you write on row forty and
/// the sheet is forty long. So scrubbing is bounded by what the X-sheet draws,
/// and the length follows the work.
/// </para>
/// <para>
/// The line this must not cross is the one B206 and B207 drew: <b>navigation
/// authors nothing, an edit that lands authors</b>. Scrubbing to frame twenty
/// leaves a five-frame document a five-frame document until something is drawn
/// there — otherwise a mis-drag would silently make a very long scene.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class ScrubPastTheEndTests : BrushStateIsolated
{
    private static MainViewModel Vm()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(200, 160, 1);
        vm.EndStroke();
        return vm;
    }

    private static void Draw(MainViewModel vm)
    {
        vm.BeginStroke(40, 40, 1);
        vm.MoveStroke(80, 80, 1);
        vm.EndStroke();
    }

    [AvaloniaFact]
    public void ThePlayheadReachesTheHatchedCells()
    {
        var vm = Vm();
        var beyond = vm.Doc.Scene.FrameCount + 6;
        Assert.True(vm.MaxScrubFrame >= beyond, "the sheet draws cells the playhead cannot reach");

        vm.CurrentFrameIndex = beyond;

        Assert.Equal(beyond, vm.CurrentFrameIndex);
        Assert.True(vm.PlayheadPastTheEnd);
    }

    [AvaloniaFact]
    public void AnEditElsewhereDoesNotYankThePlayheadBack()
    {
        // What the clamp is actually for. Every document change runs
        // ClampCurrentFrame, so a clamp that still pinned to the scene would
        // drag the playhead home the instant anything else was touched — the
        // artist would park past the end, toggle a layer, and find themselves
        // somewhere else.
        var vm = Vm();
        var parked = vm.Doc.Scene.FrameCount + 4;
        vm.CurrentFrameIndex = parked;

        vm.ToggleActiveLayerVisibleCommand.Execute(null);

        Assert.Equal(parked, vm.CurrentFrameIndex);
    }

    [AvaloniaFact]
    public void ShrinkingTheSheetStillPullsThePlayheadIn()
    {
        // The other half: the bound is the sheet, so a playhead beyond what the
        // sheet can draw is still brought back. Otherwise "no clamp at all"
        // would be indistinguishable from this change.
        var vm = Vm();
        vm.CurrentFrameIndex = vm.MaxScrubFrame;
        var beyond = vm.MaxScrubFrame + 50;

        vm.CurrentFrameIndex = beyond;
        vm.ToggleActiveLayerVisibleCommand.Execute(null);

        Assert.True(vm.CurrentFrameIndex <= vm.MaxScrubFrame,
            $"the playhead sat at {vm.CurrentFrameIndex}, past the sheet's {vm.MaxScrubFrame}");
    }

    [AvaloniaFact]
    public void ScrubbingThereAuthorsNothing()
    {
        // The whole safety property. A mis-drag must cost nothing.
        var vm = Vm();
        var frames = vm.Doc.Scene.FrameCount;
        var undos = vm.UndoDepth;

        vm.CurrentFrameIndex = frames + 10;

        Assert.Equal(frames, vm.Doc.Scene.FrameCount);
        Assert.Equal(undos, vm.UndoDepth);
    }

    [AvaloniaFact]
    public void ClickingAHatchedCellStandsOnIt()
    {
        var vm = Vm();
        var row = vm.LayerRows.First(r => r.SceneIndex == vm.ActiveLayerIndex);
        var hatched = row.Cells.First(c => c.IsVirtual);

        vm.SelectFrameCommand.Execute(hatched);

        Assert.Equal(hatched.Index, vm.CurrentFrameIndex);
        Assert.Equal(vm.Doc.Scene.FrameCount, vm.Doc.Scene.FrameCount); // and nothing was authored
        Assert.True(vm.PlayheadPastTheEnd);
    }

    [AvaloniaFact]
    public void DrawingThereGrowsTheScene_AndTheGapBecomesHolds()
    {
        // The example that prompted this: one drawing, and I want it held for
        // ten frames — drag the playhead out and draw.
        var vm = Vm();
        var first = (Frame)vm.PaintLayer().Cels[0].Frame!;
        vm.CurrentFrameIndex = 10;

        Draw(vm);

        Assert.Equal(11, vm.Doc.Scene.FrameCount);
        var layer = vm.PaintLayer();
        Assert.NotNull(layer.Cels[10].Frame);
        // Everything between is a hold of the drawing that was there, so the
        // first drawing stays up across the gap rather than the scene going
        // blank in the middle.
        for (var i = 1; i < 10; i++) Assert.Null(layer.Cels[i].Frame);
        Assert.Same(first, Lightbox.Core.Timeline.ExposureSheet.ExposedFrame(layer, 5));
    }

    [AvaloniaFact]
    public void TheGrowthAndTheMarkAreSeparateUndoSteps()
    {
        // Same shape as keying a hold: one undo takes the mark back and leaves
        // the drawing and the longer scene, then the key, then the length.
        var vm = Vm();
        var carried = ((Frame)vm.PaintLayer().Cels[0].Frame!).Strokes.Count;
        vm.CurrentFrameIndex = 8;
        Draw(vm);
        Assert.Equal(9, vm.Doc.Scene.FrameCount);
        var keyed = (Frame)vm.PaintLayer().Cels[8].Frame!;
        // B204 still holds out here: the cel this growth keyed carries what the
        // gap was holding, so the mark is the only visible difference.
        Assert.Equal(carried + 1, keyed.Strokes.Count);

        vm.UndoCommand.Execute(null);
        Assert.Equal(9, vm.Doc.Scene.FrameCount);
        Assert.Equal(carried, ((Frame)vm.PaintLayer().Cels[8].Frame!).Strokes.Count);

        vm.UndoCommand.Execute(null);   // the key
        vm.UndoCommand.Execute(null);   // the growth
        Assert.Equal(1, vm.Doc.Scene.FrameCount);
    }

    [AvaloniaFact]
    public void PastTheEndTheCanvasShowsNoDrawing()
    {
        // The owner's choice: empty out there, so a hatched cell never looks
        // like a hold. Onion and the rig still show — they resolve through the
        // exposure on purpose — but the drawing itself does not.
        var vm = Vm();
        vm.Onion.Enabled = false;

        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        vm.CurrentFrameIndex = vm.Doc.Scene.FrameCount + 3;
        vm.PublishSnapshot();

        Assert.NotNull(latest);
        using var bmp = SKBitmap.FromImage(latest!.Image);
        Assert.Equal(0, bmp.GetPixel(150, 130).Alpha);
    }

    [AvaloniaFact]
    public void PlaybackIsStillBoundedByTheScene()
    {
        // The second half of the two paths: scrubbing is free, playing is not.
        var vm = Vm();
        vm.AddFrameCommand.Execute(null);

        Assert.Equal(vm.Doc.Scene.FrameCount - 1, vm.EffectiveEndFrame);

        vm.CurrentFrameIndex = vm.Doc.Scene.FrameCount + 5;
        Assert.Equal(vm.Doc.Scene.FrameCount - 1, vm.EffectiveEndFrame);
    }

    [AvaloniaFact]
    public void PosingARigPastTheEndDoesNotWriteOntoTheLastDrawing()
    {
        // The trap this change would otherwise have opened: Anchors.SetAcross
        // clamped its index to the last cel, so a pose made out here would have
        // landed on the final drawing — silently, on a frame nobody is looking
        // at. It refuses now, and the edit paths grow the scene first.
        var vm = Vm();
        var layer = vm.PaintLayer();
        var lastDrawing = (Frame)layer.Cels[0].Frame!;

        var wrote = Lightbox.Core.Documents.Anchors.SetAcross(
            layer, layer.Cels.Count + 4, 1, "hip", new AnchorPoint(10, 10));

        Assert.Equal(0, wrote);
        Assert.Null(lastDrawing.Anchors);
    }
}
