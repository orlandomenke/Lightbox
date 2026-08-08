using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Regressions from `.claude/quality/BUGS.md`. Each test is the evidence that
/// closes one entry, so deleting it reopens the bug on the next sync.
/// </summary>
[Collection("BrushState")]
public class TimelineBugTests : BrushStateIsolated
{
    private static MainViewModel Vm() => new(null)
    {
        SmoothStrokes = false,
        ColorHex = "#000000",
        BrushSize = 30,
        BrushHardness = 1,
        BrushOpacity = 1,
        BrushFlow = 1,
        BrushWetEdge = 0,
        BrushGranulation = 0,
        BrushScatter = 0,
        AntiAliasing = false,
    };

    private static SKColor PixelAt(RenderSnapshot snapshot, int x, int y)
    {
        using var bmp = SKBitmap.FromImage(snapshot.Image);
        return bmp.GetPixel(x, y);
    }

    /// <summary>B1 — the paper layer must not paint over the onion ghosts.</summary>
    [AvaloniaFact]
    public void OnionGhostsShowOverThePaper()
    {
        // The document opens on opaque white paper, which holds across frames.
        // Ghosts were queued as passes ahead of EVERY layer, so the paper —
        // bottom of the stack, fully opaque — painted over all of them.
        var vm = Vm();
        vm.OnionSkin = true;
        var paper = vm.Doc.Scene.Layers[0];
        Assert.True(paper.IsBackground, "this test is about the paper being present");

        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(160, 100, 1);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null); // playhead moves to the empty frame 2
        // The paper holds, so it is opaque on this frame too — that is the
        // condition under which the ghosts used to vanish.
        Assert.Null(paper.Cels[1].Frame);
        Assert.NotNull(Lightbox.Core.Timeline.ExposureSheet.ExposedFrame(paper, 1));

        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        vm.PublishSnapshot();

        Assert.NotNull(latest);
        var ghost = PixelAt(latest!, 130, 100);
        Assert.NotEqual(SKColors.White, ghost);
        // The previous frame's tint is red-biased, so the ghost reads warm.
        Assert.True(ghost.Red > ghost.Blue, $"expected a warm previous-frame ghost, got {ghost}");
    }

    /// <summary>B9 — adding a frame must not blank the paper.</summary>
    [AvaloniaFact]
    public void AddingAFrame_HoldsThePaperRatherThanBlankingIt()
    {
        var vm = Vm();
        var paper = vm.Doc.Scene.Layers[0];
        Assert.True(paper.IsBackground);

        vm.AddFrameCommand.Execute(null);

        // A hold, not a blank key: an empty drawing on the paper layer shadows
        // the paper and leaves the new frame transparent.
        Assert.Null(paper.Cels[1].Frame);

        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        vm.PublishSnapshot();
        Assert.Equal(SKColors.White, PixelAt(latest!, 200, 200));
    }

    /// <summary>B2 — painting where no cel is keyed must key one, not no-op.</summary>
    [AvaloniaFact]
    public void PaintingWithNoKeyAtThePlayhead_CreatesOne()
    {
        var vm = Vm();
        var layer = vm.PaintLayer();
        foreach (var cel in layer.Cels) cel.Frame = null; // every cel cleared
        Assert.All(layer.Cels, c => Assert.Null(c.Frame));

        vm.BeginStroke(40, 40, 1);
        vm.MoveStroke(90, 90, 1);
        vm.EndStroke();

        var frame = Assert.IsType<Frame>(vm.PaintLayer().Cels[vm.CurrentFrameIndex].Frame);
        Assert.Single(frame.Strokes);
    }

    /// <summary>B2 — a fill on an unkeyed cel keys it too, not only the brush.</summary>
    [AvaloniaFact]
    public void FillingWithNoKeyAtThePlayhead_CreatesOne()
    {
        var vm = Vm();
        vm.ActiveTool = ToolId.Fill;
        foreach (var cel in vm.PaintLayer().Cels) cel.Frame = null;

        vm.FillAt(50, 50);

        var frame = Assert.IsType<Frame>(vm.PaintLayer().Cels[vm.CurrentFrameIndex].Frame);
        Assert.Single(frame.Strokes);
    }

    /// <summary>
    /// B2 — keying by drawing is undoable, as its own step.
    ///
    /// Two steps rather than one, deliberately: creating the cel and marking it
    /// are separate changes, and the cel outlives a cancelled stroke. One undo
    /// takes the mark back and leaves an empty drawing where the artist just
    /// made one; a second removes the drawing. Nothing is ever stranded.
    /// </summary>
    [AvaloniaFact]
    public void KeyingByDrawing_IsUndoable()
    {
        var vm = Vm();
        foreach (var cel in vm.PaintLayer().Cels) cel.Frame = null;

        vm.BeginStroke(40, 40, 1);
        vm.MoveStroke(90, 90, 1);
        vm.EndStroke();
        Assert.NotNull(vm.PaintLayer().Cels[vm.CurrentFrameIndex].Frame);

        vm.UndoCommand.Execute(null); // the stroke
        var kept = vm.PaintLayer().Cels[vm.CurrentFrameIndex].Frame;
        Assert.NotNull(kept);
        Assert.Empty(((Frame)kept!).Strokes);

        vm.UndoCommand.Execute(null); // the cel the stroke created
        Assert.Null(vm.PaintLayer().Cels[vm.CurrentFrameIndex].Frame);
    }

    /// <summary>B3 — the timeline thumbnail comes back when a cleared cel is redrawn.</summary>
    [AvaloniaFact]
    public void RedrawingAClearedCel_BringsTheThumbnailBack()
    {
        var vm = Vm();
        vm.BeginStroke(40, 40, 1);
        vm.MoveStroke(90, 90, 1);
        vm.EndStroke();

        var cell = vm.LayerRows.First(r => r.SceneIndex == vm.ActiveLayerIndex)
            .Cells.First(c => c.Index == 0);
        Assert.NotNull(cell.Thumb);

        vm.ClearCelAt(cell);
        vm.BeginStroke(60, 60, 1);
        vm.MoveStroke(120, 120, 1);
        vm.EndStroke();

        var after = vm.LayerRows.First(r => r.SceneIndex == vm.ActiveLayerIndex)
            .Cells.First(c => c.Index == 0);
        Assert.NotNull(after.Thumb);
        Assert.Single(((Frame)vm.PaintLayer().Cels[0].Frame!).Strokes);
    }

    /// <summary>B6 — deleting a cel pulls the following cels back, in one undo step.</summary>
    [AvaloniaFact]
    public void DeleteCel_RipplesTheRest()
    {
        var vm = Vm();
        var layer = vm.PaintLayer();
        for (var i = 1; i < 4; i++)
        {
            vm.InsertFrameAt(
                vm.LayerRows.First(r => r.SceneIndex == vm.ActiveLayerIndex).Cells.First(c => c.Index == i),
                FrameRole.Key);
        }
        var ids = layer.Cels.Select(c => c.Frame?.Id).ToList();
        Assert.Equal(4, ids.Count);

        var second = vm.LayerRows.First(r => r.SceneIndex == vm.ActiveLayerIndex)
            .Cells.First(c => c.Index == 1);
        vm.DeleteCelAt(second);

        var after = vm.PaintLayer();
        // Same length — the row is padded at the end, so the timeline keeps its
        // shape while everything after the hole moves up one.
        Assert.Equal(4, after.Cels.Count);
        Assert.Equal(ids[0], after.Cels[0].Frame?.Id);
        Assert.Equal(ids[2], after.Cels[1].Frame?.Id);
        Assert.Equal(ids[3], after.Cels[2].Frame?.Id);
        Assert.Null(after.Cels[3].Frame);

        vm.UndoCommand.Execute(null);
        Assert.Equal(ids, vm.PaintLayer().Cels.Select(c => c.Frame?.Id).ToList());
    }

    /// <summary>B6 — deleting a cel touches one layer, never the others.</summary>
    [AvaloniaFact]
    public void DeleteCel_LeavesOtherLayersAlone()
    {
        var vm = Vm();
        vm.AddPaintedLayerCommand.Execute(null);
        vm.AddFrameCommand.Execute(null);
        var untouched = vm.Doc.Scene.Layers[1].Cels.Select(c => c.Frame?.Id).ToList();

        vm.DeleteCelAt(vm.LayerRows.First(r => r.SceneIndex == vm.ActiveLayerIndex)
            .Cells.First(c => c.Index == 0));

        Assert.Equal(untouched, vm.Doc.Scene.Layers[1].Cels.Select(c => c.Frame?.Id).ToList());
        Assert.Equal(2, vm.Doc.Scene.FrameCount);
    }
}
