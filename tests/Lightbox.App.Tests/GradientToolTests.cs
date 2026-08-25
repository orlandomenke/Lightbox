using Avalonia.Headless.XUnit;
using Lightbox.App.Docking;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The gradient tool and its editor. A gradient is a definition in the
/// document and a two-point axis on a stroke; editing the definition changes
/// the art, exactly like a palette swatch.
/// </summary>
[Collection("BrushState")]
public class GradientToolTests : BrushStateIsolated
{
    private static MainViewModel WithGradient(out Gradient gradient)
    {
        var vm = new MainViewModel(null) { ActiveTool = ToolId.Gradient };
        vm.GradientDocker.AddGradientCommand.Execute(null);
        gradient = vm.GradientDocker.SelectedGradient!;
        return vm;
    }

    private static List<Stroke> Strokes(MainViewModel vm) =>
        ((Frame)vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[0].Frame!).Strokes;

    private static SKColor At(MainViewModel vm, int x, int y)
    {
        var scene = vm.Doc.Scene;
        using var bmp = FrameRasterizer.Rasterize(Strokes(vm), scene.Width, scene.Height);
        return bmp.GetPixel(x, y);
    }

    private static void Drag(MainViewModel vm, double x0, double y0, double x1, double y1)
    {
        vm.BeginGradient(x0, y0);
        vm.MoveGradient((x0 + x1) / 2, (y0 + y1) / 2);
        vm.EndGradient(x1, y1);
    }

    [AvaloniaFact]
    public void ANewDocumentHasNoGradientsAndTheGradientTabRestsBehindColor()
    {
        var vm = new MainViewModel(null);
        Assert.Empty(vm.Doc.Gradients);
        Assert.Empty(vm.GradientDocker.Gradients);
        // The panel ships as a background tab of the colour family now, not a
        // strip of its own — present in the header, with Color in front.
        Assert.True(vm.GradientDockerVisible);
        var layout = vm.Workspace.Layout;
        Assert.Equal(DockPanelId.Color, layout.ActiveOf(layout.SlotOf(DockPanelId.Gradient)));
    }

    [AvaloniaFact]
    public void ANewGradientIsBlackToWhiteAndUndoable()
    {
        var vm = WithGradient(out var gradient);
        Assert.Equal(2, gradient.Stops.Count);
        Assert.Equal(GradientKind.Linear, gradient.Kind);
        Assert.Same(gradient, Assert.Single(vm.Doc.Gradients).Value);

        vm.UndoCommand.Execute(null);
        Assert.Empty(vm.Doc.Gradients);
        Assert.Empty(vm.GradientDocker.Gradients);
    }

    [AvaloniaFact]
    public void DraggingLaysDownAGradientStrokeWithTheDragAsItsAxis()
    {
        var vm = WithGradient(out var gradient);
        Drag(vm, 10, 50, 90, 50);

        var stroke = Assert.Single(Strokes(vm));
        Assert.Equal(ToolKind.Gradient, stroke.Tool);
        Assert.Equal(gradient.Id, stroke.GradientId);
        Assert.Equal(2, stroke.Points.Count);
        Assert.Equal(10, stroke.Points[0].X);
        Assert.Equal(90, stroke.Points[1].X);
    }

    [AvaloniaFact]
    public void TheRampRunsAlongTheDrag()
    {
        var vm = WithGradient(out _);
        var w = vm.Doc.Scene.Width;
        Drag(vm, 0, 10, w, 10);

        var near = At(vm, 4, 10);
        var far = At(vm, w - 5, 10);
        // Black at the start, white at the end, and monotonically brighter.
        Assert.True(near.Red < 40, $"start should be dark, was {near.Red}");
        Assert.True(far.Red > 215, $"end should be light, was {far.Red}");
        Assert.True(At(vm, w / 4, 10).Red < At(vm, w * 3 / 4, 10).Red);
    }

    [AvaloniaFact]
    public void AClickWithNoDragPaintsNothing()
    {
        // A degenerate axis would flood the layer with one end of the ramp,
        // which is never what a stray click meant.
        var vm = WithGradient(out _);
        vm.BeginGradient(40, 40);
        vm.EndGradient(40, 40);
        Assert.Empty(Strokes(vm));
    }

    [AvaloniaFact]
    public void EscapeDuringTheDragAbandonsIt()
    {
        var vm = WithGradient(out _);
        vm.BeginGradient(10, 10);
        vm.MoveGradient(80, 80);
        Assert.NotNull(vm.LiveGradient);

        vm.CancelGradient();

        Assert.Null(vm.LiveGradient);
        Assert.Empty(Strokes(vm));
    }

    [AvaloniaFact]
    public void EditingAStopRepaintsTheGradientAlreadyLaidDown()
    {
        var vm = WithGradient(out var gradient);
        var w = vm.Doc.Scene.Width;
        Drag(vm, 0, 10, w, 10);
        Assert.True(At(vm, 4, 10).Red < 40);

        // The "black" end becomes red — the stroke record does not change.
        vm.GradientDocker.SelectedStop = vm.GradientDocker.Stops[0];
        vm.GradientDocker.SelectedStop!.Color = "#ff0000";

        var start = At(vm, 4, 10);
        Assert.True(start.Red > 215, $"start should now be red, was {start}");
        Assert.True(start.Green < 40);
        Assert.Equal(gradient.Id, Strokes(vm)[0].GradientId);
    }

    [AvaloniaFact]
    public void SwitchingToRadialChangesTheShapeOfWhatIsPainted()
    {
        var vm = WithGradient(out _);
        var w = vm.Doc.Scene.Width;
        var h = vm.Doc.Scene.Height;
        Drag(vm, w / 2, h / 2, w / 2 + 20, h / 2);

        // Two points the same distance from the centre, one along the drag and
        // one across it. Their pixel centres are equidistant by construction —
        // (+9.5, +0.5) and (+0.5, +9.5) from the corner the drag started on.
        // Linear only reads the along-axis component, so they differ wildly;
        // radial reads distance, so they agree.
        var along = At(vm, w / 2 + 9, h / 2);
        var across = At(vm, w / 2, h / 2 + 9);
        Assert.True(Math.Abs(along.Red - across.Red) > 60, $"linear: {along.Red} vs {across.Red}");

        vm.GradientDocker.Kind = GradientKind.Radial;
        Assert.Equal(At(vm, w / 2 + 9, h / 2).Red, At(vm, w / 2, h / 2 + 9).Red);
    }

    [AvaloniaFact]
    public void AddingAStopInsertsItBetweenTheSelectedOneAndTheNext()
    {
        var vm = WithGradient(out var gradient);
        vm.GradientDocker.SelectedStop = vm.GradientDocker.Stops[0];
        vm.GradientDocker.AddStopCommand.Execute(null);

        Assert.Equal(3, gradient.Stops.Count);
        var inserted = GradientOps.Ordered(gradient)[1];
        Assert.Equal(0.5, inserted.Position, 3);
        // Its colour is sampled from the ramp, so inserting one changes nothing.
        Assert.InRange(GimpPalette.Rgb(inserted.Color).R, 185, 191);
    }

    [AvaloniaFact]
    public void TheLastTwoStopsCannotBeRemoved()
    {
        // One stop is not a ramp, and the tool would silently start painting a
        // flat colour with no way back except undo.
        var vm = WithGradient(out var gradient);
        vm.GradientDocker.SelectedStop = vm.GradientDocker.Stops[0];
        vm.GradientDocker.RemoveStopCommand.Execute(null);
        Assert.Equal(2, gradient.Stops.Count);
    }

    [AvaloniaFact]
    public void AGradientStrokeSurvivesAReload()
    {
        var vm = WithGradient(out var gradient);
        var w = vm.Doc.Scene.Width;
        Drag(vm, 0, 10, w, 10);

        var reloaded = Lightbox.Core.Serialization.DocJson.Deserialize(vm.SerializeDocument());
        var back = Assert.Single(reloaded.Gradients).Value;
        var stroke = Assert.Single(
            ((Frame)reloaded.Scene.Layers[vm.ActiveLayerIndex].Cels[0].Frame!).Strokes);

        Assert.Equal(gradient.Id, back.Id);
        Assert.Equal(back.Id, stroke.GradientId);

        // And it renders the same after the reload, which is the whole point
        // of the definition living in the document.
        PaletteRegistry.Reset(reloaded.Palettes, reloaded.Gradients);
        using var bmp = FrameRasterizer.Rasterize([stroke], reloaded.Scene.Width, reloaded.Scene.Height);
        Assert.True(bmp.GetPixel(4, 10).Red < 40);
        Assert.True(bmp.GetPixel(reloaded.Scene.Width - 5, 10).Red > 215);
    }

    [AvaloniaFact]
    public void UndoingRemovesTheGradientStroke()
    {
        var vm = WithGradient(out _);
        Drag(vm, 0, 10, vm.Doc.Scene.Width, 10);
        Assert.Single(Strokes(vm));

        vm.UndoCommand.Execute(null);
        Assert.Empty(Strokes(vm));
    }

    [AvaloniaFact]
    public void TheToolMakesABlackToWhiteGradientIfThereIsNone()
    {
        // It used to refuse and point at the Gradient panel, which is a dead
        // end: someone who just picked the gradient tool wants a gradient, and
        // black to white is the ramp they would have made by hand anyway.
        var vm = new MainViewModel(null) { ActiveTool = ToolId.Gradient };
        Assert.Empty(vm.Doc.Gradients);

        Drag(vm, 0, 10, vm.Doc.Scene.Width, 10);

        Assert.Single(vm.Doc.Gradients);
        Assert.Single(Strokes(vm));
        Assert.True(At(vm, 4, 10).Red < 40);
        Assert.True(At(vm, vm.Doc.Scene.Width - 5, 10).Red > 215);
    }

    [AvaloniaFact]
    public void ALockedLayerRefusesAGradient()
    {
        var vm = WithGradient(out _);
        vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Locked = true;
        Drag(vm, 0, 10, 80, 10);
        Assert.Empty(Strokes(vm));
    }

    [AvaloniaFact]
    public void TheRampIsVisibleWhileDragging_AndSurvivesThePenLift()
    {
        // The rule the brush work established: what you are shown while
        // dragging is what you get when you let go. A gradient that only
        // appeared on release would be guesswork.
        var vm = WithGradient(out _);
        var w = vm.Doc.Scene.Width;
        Lightbox.App.Rendering.RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        vm.BeginGradient(0, 10);
        vm.MoveGradient(w, 10);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // flush the coalesced publish

        Assert.NotNull(latest);
        using (var live = SKBitmap.FromImage(latest!.Image))
        {
            Assert.NotNull(live);
            Assert.True(live!.GetPixel(4, 10).Red < 40, "the drag preview did not show the dark end");
            Assert.True(live.GetPixel(w - 5, 10).Red > 215, "the drag preview did not show the light end");
        }

        vm.EndGradient(w, 10);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using var committed = SKBitmap.FromImage(latest!.Image);
        Assert.NotNull(committed);
        Assert.True(committed!.GetPixel(4, 10).Red < 40, "the ramp vanished on release");
        Assert.True(committed.GetPixel(w - 5, 10).Red > 215);
    }

    [AvaloniaFact]
    public void OpacityIsRecordedOnTheStrokeNotReadAtRenderTime()
    {
        // Invariant 4: changing the tool's opacity afterwards must not repaint
        // what is already down.
        var vm = WithGradient(out _);
        vm.GradientOpacity = 0.5;
        Drag(vm, 0, 10, vm.Doc.Scene.Width, 10);

        Assert.Equal(0.5, Assert.Single(Strokes(vm)).Brush.Opacity, 3);
        var before = At(vm, vm.Doc.Scene.Width - 5, 10);

        vm.GradientOpacity = 1.0;
        Assert.Equal(before, At(vm, vm.Doc.Scene.Width - 5, 10));
    }

    // ---- B7: the transform tool and gradients -------------------------------

    [AvaloniaFact]
    public void TransformingAGradient_MovesItsAxis()
    {
        // The plain case, so the regression test says what "works" means
        // before the selection case says what used to break.
        var vm = WithGradient(out _);
        Drag(vm, 100, 20, 100, 120);
        // Below the ramp, so it is the clamped end colour — white.
        Assert.True(At(vm, 100, 200).Red > 250);

        Assert.True(vm.BeginTransform());
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 0, 150);

        var stroke = Assert.Single(Strokes(vm));
        Assert.Equal(170, stroke.Points[0].Y, 3);
        Assert.Equal(270, stroke.Points[^1].Y, 3);
        // The ramp went with it: that same point is now inside it.
        Assert.InRange(At(vm, 100, 200).Red, 40, 140);
    }

    [AvaloniaFact]
    public void ASelectionOverAGradient_FindsIt()
    {
        // B7. A gradient's two points are the ends of its axis, not a
        // centreline, and the ramp colours the whole layer regardless of where
        // they sit. Judging the selection by those two points meant a marquee
        // drawn straight over a visible gradient reported an empty scope.
        var vm = WithGradient(out _);
        Drag(vm, 100, 20, 100, 120);

        vm.ApplySelectionShape(                 // well clear of both axis points
            [new(20, 140, 1), new(280, 140, 1), new(280, 240, 1), new(20, 240, 1)],
            add: false, subtract: false);

        Assert.True(vm.BeginTransform(), vm.AiStatus);
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 0, 150);

        Assert.Equal(170, Assert.Single(Strokes(vm)).Points[0].Y, 3);
    }

    [AvaloniaFact]
    public void ASelectionElsewhereStillLeavesOrdinaryStrokesAlone()
    {
        // The counter-test: widening the filter for gradients must not widen
        // it for anything else.
        var vm = WithGradient(out _);
        vm.ActiveTool = ToolId.Brush;
        vm.SmoothStrokes = false;
        vm.BeginStroke(40, 40, 1);
        vm.MoveStroke(80, 40, 1);
        vm.EndStroke();

        vm.ApplySelectionShape(
            [new(200, 200, 1), new(280, 200, 1), new(280, 280, 1), new(200, 280, 1)],
            add: false, subtract: false);

        Assert.False(vm.BeginTransform());
        Assert.Contains("Nothing to transform", vm.AiStatus);
    }
}
