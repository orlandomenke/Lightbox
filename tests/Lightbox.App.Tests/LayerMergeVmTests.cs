using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Merge layer down (Ctrl+E) from the view model's side: one undo step, the
/// merged layer keeps the lower layer's identity, and layers that refuse
/// edits refuse merges. What the merged drawing contains is
/// <c>Lightbox.Raster.Tests.LayerMergeTests</c>' subject.
/// </summary>
public class LayerMergeVmTests
{
    private static MainViewModel TwoDrawnLayers()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.BeginStroke(40, 40, 1);
        vm.EndStroke();
        vm.AddPaintedLayerCommand.Execute(null); // adds on top and activates it
        vm.BeginStroke(120, 120, 1);
        vm.EndStroke();
        return vm;
    }

    [AvaloniaFact]
    public void MergeDown_JoinsTheStrokes_AndLandsOnTheLowerLayer()
    {
        var vm = TwoDrawnLayers();
        var lowerId = vm.Doc.Scene.Layers[0].Id;
        var lowerName = vm.Doc.Scene.Layers[0].Name;

        Assert.False(vm.MergeWouldBake(null));
        vm.MergeActiveLayerDownCommand.Execute(null);

        Assert.Single(vm.Doc.Scene.Layers);
        var merged = vm.Doc.Scene.Layers[0];
        Assert.Equal(lowerId, merged.Id);       // the lower layer's identity survives
        Assert.Equal(lowerName, merged.Name);
        Assert.Equal(0, vm.ActiveLayerIndex);   // the merge is where the artist now works
        Assert.Equal(2, ((Frame)merged.Cels[0].Frame!).Strokes.Count);
    }

    /// <summary>Two drawn layers, the upper one with an eraser stroke across its line (Q264).</summary>
    private static MainViewModel UpperLayerHasAnEraser()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.MergeErasersPreference = null;
        vm.BeginStroke(40, 40, 1);
        vm.MoveStroke(160, 40, 1);
        vm.EndStroke();
        vm.AddPaintedLayerCommand.Execute(null);
        vm.BeginStroke(40, 120, 1);
        vm.MoveStroke(160, 120, 1);
        vm.EndStroke();
        vm.PaintLayer().Cels[0].Frame!.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Eraser,
            Points = [new StrokePoint(100, 20, 1), new StrokePoint(100, 180, 1)],
            Brush = new BrushSettings { Size = 20, Hardness = 1 },
        });
        return vm;
    }

    [AvaloniaFact]
    public void AnEraserOnTheUpperLayerMakesItAChoice()
    {
        var vm = UpperLayerHasAnEraser();

        Assert.True(vm.MergeErasersDecide(null));
        Assert.True(vm.MergeWouldBake(null));                                         // flattened, today's answer
        Assert.False(vm.MergeWouldBake(null, Lightbox.Raster.EraserMerge.KeepStrokes)); // or kept as strokes
    }

    [AvaloniaFact]
    public void WithoutAnEraserThereIsNothingToChoose()
    {
        Assert.False(TwoDrawnLayers().MergeErasersDecide(null));
    }

    [AvaloniaFact]
    public void KeepingStrokesLeavesAStrokeRecordAndNoEraser()
    {
        var vm = UpperLayerHasAnEraser();

        vm.MergeLayerDown(null, Lightbox.Raster.EraserMerge.KeepStrokes);

        var merged = Assert.Single(vm.Doc.Scene.Layers).Cels[0].Frame!;
        Assert.False(merged.HasBaseline);
        Assert.DoesNotContain(merged.Strokes, s => s.Tool == ToolKind.Eraser);
        Assert.Equal(3, merged.Strokes.Count);   // the lower line, and the upper line in two pieces

        vm.UndoCommand.Execute(null);            // one step back to two layers and the eraser
        Assert.Equal(2, vm.Doc.Scene.Layers.Count);
        Assert.Contains(vm.Doc.Scene.Layers[1].Cels[0].Frame!.Strokes, s => s.Tool == ToolKind.Eraser);
    }

    [AvaloniaFact]
    public void FlatteningIsStillWhatHappensUnlessAsked()
    {
        var vm = UpperLayerHasAnEraser();

        vm.MergeLayerDown(null);

        Assert.True(Assert.Single(vm.Doc.Scene.Layers).Cels[0].Frame!.HasBaseline);
    }

    [AvaloniaFact]
    public void ARememberedAnswerIsWhatThePlainCommandUses()
    {
        // Ctrl+E reaches the view model directly when no window is asking; a
        // stored answer has to hold there too, or "remember" means "sometimes".
        var vm = UpperLayerHasAnEraser();
        vm.MergeErasersPreference = Lightbox.Raster.EraserMerge.KeepStrokes;
        try
        {
            vm.MergeActiveLayerDownCommand.Execute(null);

            Assert.False(Assert.Single(vm.Doc.Scene.Layers).Cels[0].Frame!.HasBaseline);
        }
        finally
        {
            vm.MergeErasersPreference = null;
        }
    }

    [AvaloniaFact]
    public void ASettingsFileWithNoRememberedAnswerDoesNotMentionIt()
    {
        // Optional means absent: nobody who never ticked the box gets the key.
        var settings = new Lightbox.App.Services.AppSettings();
        Assert.DoesNotContain("mergeEraserChoice", settings.Serialize(), StringComparison.OrdinalIgnoreCase);

        settings.MergeEraserChoice = "KeepStrokes";
        Assert.Contains("mergeEraserChoice", settings.Serialize(), StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void AStoredValueThatIsNotAChoiceMeansAsk()
    {
        var vm = VmLayers.BareVm();
        var settings = vm.Settings;
        var before = settings.MergeEraserChoice;
        try
        {
            settings.MergeEraserChoice = "7";
            Assert.Null(vm.MergeErasersPreference);
            settings.MergeEraserChoice = "Sideways";
            Assert.Null(vm.MergeErasersPreference);
        }
        finally
        {
            settings.MergeEraserChoice = before;
        }
    }

    [AvaloniaFact]
    public void TheRememberedAnswerCanBeReadBackAndTakenBack()
    {
        var vm = VmLayers.BareVm();
        try
        {
            vm.MergeErasersPreference = Lightbox.Raster.EraserMerge.Flatten;
            Assert.Equal(Lightbox.Raster.EraserMerge.Flatten, vm.MergeErasersPreference);

            vm.MergeErasersPreference = null;     // "ask every time"
            Assert.Null(vm.MergeErasersPreference);
        }
        finally
        {
            vm.MergeErasersPreference = null;
        }
    }

    [AvaloniaFact]
    public void MergeDown_IsOneUndoStep()
    {
        var vm = TwoDrawnLayers();
        var upperId = vm.PaintLayer().Id;

        vm.MergeActiveLayerDownCommand.Execute(null);
        Assert.Single(vm.Doc.Scene.Layers);

        vm.UndoCommand.Execute(null);
        Assert.Equal(2, vm.Doc.Scene.Layers.Count);
        Assert.Equal(upperId, vm.Doc.Scene.Layers[1].Id);
        Assert.Single(((Frame)vm.Doc.Scene.Layers[1].Cels[0].Frame!).Strokes);
        Assert.Single(((Frame)vm.Doc.Scene.Layers[0].Cels[0].Frame!).Strokes);
    }

    [AvaloniaFact]
    public void MergeWouldBake_SeesTheUpperLayersOpacity()
    {
        var vm = TwoDrawnLayers();
        vm.ActiveLayerOpacity = 50;
        Assert.True(vm.MergeWouldBake(null));

        vm.MergeActiveLayerDownCommand.Execute(null);
        Assert.Single(vm.Doc.Scene.Layers);
        Assert.True(((Frame)vm.Doc.Scene.Layers[0].Cels[0].Frame!).HasBaseline);
    }

    [AvaloniaFact]
    public void TheBottomLayer_HasNothingToMergeInto()
    {
        var vm = VmLayers.BareVm(); // one drawing layer, no paper
        vm.MergeActiveLayerDownCommand.Execute(null);
        Assert.Single(vm.Doc.Scene.Layers);
    }

    [AvaloniaFact]
    public void MergingIntoTheLockedPaper_Refuses()
    {
        var vm = VmLayers.PaperVm(); // locked Background under the drawing layer
        vm.SmoothStrokes = false;
        vm.BeginStroke(40, 40, 1);
        vm.EndStroke();

        vm.MergeActiveLayerDownCommand.Execute(null);

        Assert.Equal(2, vm.Doc.Scene.Layers.Count); // nothing merged
        Assert.Contains("locked", vm.AiStatus);
    }
}
