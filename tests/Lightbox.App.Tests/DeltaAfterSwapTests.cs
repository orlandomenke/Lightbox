using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.Tests;

/// <summary>
/// B413. Undoing a structural edit replaces the document instance. A delta
/// step that changes an object it captured when it was made, rather than one it
/// finds in the document it is handed, changes the old instance after that: its
/// undo silently does nothing to the drawing on screen. Nineteen deltas did, for
/// guides, reference strips and bone weights.
/// </summary>
[Collection("BrushState")]
public class DeltaAfterSwapTests : BrushStateIsolated
{
    private static MainViewModel Fresh()
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Swap", 400, 300, 12, 72, "#ffffff", false));
        return vm;
    }

    /// <summary>A structural edit, undone: afterwards the live document is another instance.</summary>
    private static void SwapTheDocument(MainViewModel vm)
    {
        var before = vm.Doc;
        vm.PanelEditor.Perform(d => d.Scene.Layers[0].Name += "!", "Rename");
        vm.UndoCommand.Execute(null);
        Assert.NotSame(before, vm.Doc);
    }

    [AvaloniaFact]
    public void AGridsSpacingUndoesAfterTheDocumentWasSwapped()
    {
        var vm = Fresh();
        var id = vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 24).Id;
        vm.SetGridSpacing(vm.Doc.Scene.Guides!.Single(g => g.Id == id), 40);

        SwapTheDocument(vm);
        vm.UndoCommand.Execute(null);

        Assert.Equal(24, vm.Doc.Scene.Guides!.Single(g => g.Id == id).Spacing, 3);
        vm.RedoCommand.Execute(null);
        Assert.Equal(40, vm.Doc.Scene.Guides!.Single(g => g.Id == id).Spacing, 3);
    }

    /// <summary>The one of the nineteen that changes how a drawing deforms.</summary>
    [AvaloniaFact]
    public void PaintedWeightsUndoAfterTheDocumentWasSwapped()
    {
        var vm = Fresh();
        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 150, 160, 150);
        var bone = vm.SelectedBoneId!;
        var frame = ExposureSheet.ExposedFrame(vm.Doc.Scene.Layers.First(l => !l.IsBackground), vm.CurrentFrameIndex)!;
        var stroke = new Stroke { Points = [new StrokePoint(110, 150, 1), new StrokePoint(150, 150, 1)] };
        frame.Strokes.Add(stroke);
        vm.WeightPainting = true;
        vm.SelectedBoneId = bone;
        vm.MirrorWeights = false;

        string? Weights() => System.Text.Json.JsonSerializer.Serialize(
            vm.Doc.Scene.Layers.SelectMany(l => l.Cels).Select(c => c.Frame).OfType<Frame>()
                .SelectMany(f => f.Strokes).Single(s => s.Id == stroke.Id).Weights);
        var before = Weights();
        vm.BeginWeightStroke(110, 150, 1);
        vm.EndWeightStroke();
        var after = Weights();
        Assert.NotEqual(before, after); // not vacuous: the dab painted something
        vm.WeightPainting = false;

        SwapTheDocument(vm);
        vm.UndoCommand.Execute(null);

        Assert.Equal(before, Weights());
        vm.RedoCommand.Execute(null);
        Assert.Equal(after, Weights());

        // The other order, from the sensitivity review: a structural edit
        // first, the weights after it, both undone and both redone. The redo
        // of the structural edit is what replaces the document here.
        vm.PanelEditor.Perform(d => d.Scene.Layers[0].Name += "?", "Rename");
        vm.WeightPainting = true;
        vm.BeginWeightStroke(150, 150, 1);
        vm.EndWeightStroke();
        vm.WeightPainting = false;
        var twice = Weights();
        Assert.NotEqual(after, twice);
        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        vm.RedoCommand.Execute(null);
        vm.RedoCommand.Execute(null);
        Assert.Equal(twice, Weights());
    }
}
