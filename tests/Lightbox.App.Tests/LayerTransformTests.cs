using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Transforming every drawing on a layer at once, as a tool of its own (Q216):
/// resizing a character across a whole cycle round one pivot.
/// </summary>
[Collection("BrushState")]
public class LayerTransformTests : BrushStateIsolated
{
    /// <summary>
    /// One layer, three frames: a drawing at x=200, a drawing at x=400, and a
    /// hold of the second — the shape of a cycle on 2s, in miniature.
    /// </summary>
    private static MainViewModel CycleVm()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.BrushSize = 10;
        vm.BeginStroke(200, 200, 1);
        vm.MoveStroke(240, 200, 1);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null);
        vm.CurrentFrameIndex = 1;
        vm.BeginStroke(400, 200, 1);
        vm.MoveStroke(440, 200, 1);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null);
        vm.ClearCelAt(vm.LayerRows[^1].Cells.First(c => c.Index == 2)); // frame 2 holds frame 1
        vm.CurrentFrameIndex = 0;
        return vm;
    }

    private static Frame Drawing(MainViewModel vm, int cel) =>
        (Frame)vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[cel].Frame!;

    [AvaloniaFact]
    public void ItScalesEveryDrawingOnTheLayerRoundOnePivot()
    {
        // The pivot is shared: the drawing at 400 lands at 800 under a 2× round
        // the origin, which is the motion scaling with the figure. Each drawing
        // round its own centre would have left it near 400.
        var vm = CycleVm();
        var size = Drawing(vm, 0).Strokes[0].Brush.Size;

        Assert.True(vm.BeginLayerTransform());
        vm.CommitTransformAffine(0, 0, 2, 2, 0, 0, 0);

        Assert.Equal(400, Drawing(vm, 0).Strokes[0].Points[0].X, 3);
        Assert.Equal(800, Drawing(vm, 1).Strokes[0].Points[0].X, 3); // once, though held twice
        Assert.Equal(size * 2, Drawing(vm, 1).Strokes[0].Brush.Size, 3);

        vm.UndoCommand.Execute(null); // one step for the lot
        Assert.Equal(200, Drawing(vm, 0).Strokes[0].Points[0].X, 3);
        Assert.Equal(400, Drawing(vm, 1).Strokes[0].Points[0].X, 3);
    }

    [AvaloniaFact]
    public void TheNextCtrlTIsBackOnTheScopeTheArtistSet()
    {
        // B403's lesson, applied to the second gesture that carries a scope.
        var vm = CycleVm();
        Assert.True(vm.BeginLayerTransform());
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 10, 0);

        Assert.Equal(TransformScope.ActiveCel, vm.TransformScope);
        Assert.True(vm.BeginTransform());
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 10, 0);

        Assert.Equal(220, Drawing(vm, 0).Strokes[0].Points[0].X, 3); // frame 0 — on screen, twice
        Assert.Equal(410, Drawing(vm, 1).Strokes[0].Points[0].X, 3); // frame 1 — only the first
    }

    [AvaloniaFact]
    public void TheScopeControlShowsWhatTheSessionIsReallyUsing()
    {
        // The page used to say "this drawing" while every drawing moved.
        var vm = CycleVm();
        Assert.True(vm.BeginLayerTransform());
        Assert.Equal(TransformScope.ActiveLayerAllFrames, vm.SessionTransformScope);
        vm.CancelTransform();
        Assert.Equal(TransformScope.ActiveCel, vm.SessionTransformScope);
    }

    [AvaloniaFact]
    public void PickingAScopeMidSessionTakesTheSessionOver()
    {
        // Back to "this drawing" — which is already the setting, so no change
        // event fires and the session must still be re-collected.
        var vm = CycleVm();
        Assert.True(vm.BeginLayerTransform());
        vm.SessionTransformScope = TransformScope.ActiveCel;
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 30, 0);

        Assert.Equal(230, Drawing(vm, 0).Strokes[0].Points[0].X, 3);
        Assert.Equal(400, Drawing(vm, 1).Strokes[0].Points[0].X, 3);
    }

    [AvaloniaFact]
    public void PickedLinesRefuseItRatherThanQuietlyMovingOneDrawing()
    {
        // Adversary's finding: picked lines pin a session to their drawing, so
        // the command held one drawing while the Scope combo said every frame.
        var vm = CycleVm();
        Assert.True(vm.PickStrokeAt(220, 200, tolerance: 6));
        Assert.True(vm.HasStrokeSelection);

        Assert.False(vm.BeginLayerTransform());
        Assert.False(vm.TransformActive);
        Assert.Contains("deselect", vm.AiStatus, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void ThePinnedScopeIsWhatTheScopeControlShows()
    {
        var vm = CycleVm();
        vm.TransformScope = TransformScope.ActiveLayerAllFrames;
        Assert.True(vm.PickStrokeAt(220, 200, tolerance: 6));
        Assert.Equal(TransformScope.ActiveCel, vm.SessionTransformScope);
    }

    [AvaloniaFact]
    public void TheGhostsShowWhereTheDragIsTakingTheirDrawings()
    {
        // The published frame, not the pass list: the ghosts are composited into
        // one sheet for the drag (the perf-warden's finding), and this is what
        // says the sheet lands where the drawings will.
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 16;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        void Line(double x)
        {
            vm.BeginStroke(x, 100, 1);
            vm.MoveStroke(x + 60, 100, 1);
            vm.EndStroke();
        }
        Line(60);
        vm.AddFrameCommand.Execute(null);
        vm.CurrentFrameIndex = 1;
        Line(200);
        vm.AddFrameCommand.Execute(null);
        vm.CurrentFrameIndex = 2;
        Line(340);
        vm.CurrentFrameIndex = 1;
        vm.Onion.Enabled = true;
        vm.Onion.Before = 1;
        vm.Onion.After = 1;

        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        Assert.True(vm.BeginLayerTransform());
        vm.PreviewTransform(SkiaSharp.SKMatrix.CreateTranslation(0, 150));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(latest);
        using var bmp = SkiaSharp.SKBitmap.FromImage(latest!.Image)!;
        // No paper on this document, so empty canvas is transparent: ink is
        // anything with alpha that is not white.
        static bool Inked(SkiaSharp.SKColor c) => c.Alpha > 20 && (c.Red < 235 || c.Green < 235 || c.Blue < 235);
        Assert.True(Inked(bmp.GetPixel(90, 250)), "the previous drawing's ghost did not follow the drag");
        Assert.True(Inked(bmp.GetPixel(370, 250)), "the next drawing's ghost did not follow the drag");
        Assert.False(Inked(bmp.GetPixel(90, 100)), $"a ghost was left behind where its drawing used to be: {bmp.GetPixel(90, 100)}");
        vm.CancelTransform();
    }

    [AvaloniaFact]
    public void TheStatusLineCountsDrawingsNotFrames()
    {
        // Three frames, two drawings: the hold is not a drawing of its own.
        var vm = CycleVm();
        Assert.True(vm.BeginLayerTransform());
        Assert.Contains("2 drawings", vm.AiStatus);
        Assert.Contains(vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Name, vm.AiStatus);
        vm.CancelTransform();
    }

    [AvaloniaFact]
    public void ADrawingTransformSaysNothingAboutCounts()
    {
        var vm = CycleVm();
        vm.AiStatus = "";
        Assert.True(vm.BeginTransform());
        Assert.DoesNotContain("drawings", vm.AiStatus);
        vm.CancelTransform();
    }

    [Fact]
    public void ItIsRegisteredSoItCanBeFoundAndRebound()
    {
        var map = new ShortcutMap();
        var def = map.Definitions.Single(d => d.Id == "canvas.transformLayer");
        Assert.Contains("every frame", def.Name, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(def.Current);
        Assert.Null(map.ConflictWith(def.Id, def.Current));
    }

    [Fact]
    public void TheScopeComboShowsTheSessionsScopeByName()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src"))) dir = dir.Parent;
        var xaml = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Lightbox.App", "Views", "MainWindow.axaml"));
        var at = xaml.IndexOf("x:Name=\"TransformScopeCombo\"", StringComparison.Ordinal);
        Assert.True(at > 0, "the Scope combo is gone or renamed");
        var combo = xaml[at..xaml.IndexOf("</ComboBox>", at, StringComparison.Ordinal)];

        Assert.Contains("SelectedItem=\"{Binding SessionTransformScope}\"", combo);
        Assert.Contains("TransformScopeText.Converter", combo);
        Assert.Contains("Click=\"OnMenuBeginLayerTransform\"", xaml);
    }

    [Fact]
    public void EveryScopeHasANameAnArtistReads()
    {
        foreach (var scope in Enum.GetValues<TransformScope>())
        {
            Assert.NotEqual(scope.ToString(), TransformScopeText.Label(scope));
        }
        Assert.Equal("This layer, every frame", TransformScopeText.Label(TransformScope.ActiveLayerAllFrames));
    }
}
