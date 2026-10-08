using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// B415. A delta that puts an object into the document — a new drawing, a
/// stroke — put back the very object it was made with on every redo. That
/// object can have changed since: after a structural undo swaps the document,
/// later steps act on copies, and the original keeps whatever it held. A redo
/// then restores a drawing that already contains the strokes the next redo
/// adds again.
/// </summary>
[Collection("BrushState")]
public class RedoInsertsACopyTests : BrushStateIsolated
{
    [AvaloniaFact]
    public void RedoingANewDrawingAndItsStrokeDoesNotDrawTheStrokeTwice()
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Hold", 400, 300, 12, 72, "#ffffff", false));
        vm.SmoothStrokes = false;
        vm.CurrentFrameIndex = 1; // a hold: drawing here starts a new drawing

        vm.BeginStroke(50, 50, 1);
        vm.MoveStroke(120, 90, 1);
        vm.EndStroke();
        var layer = vm.PaintLayer();
        Assert.Single(layer.Cels[1].Frame!.Strokes);

        vm.PanelEditor.Perform(d => d.Scene.Layers[0].Name += "!", "Rename");
        vm.UndoCommand.Execute(null); // the rename: the document is swapped
        vm.UndoCommand.Execute(null); // the stroke
        vm.UndoCommand.Execute(null); // the new drawing
        vm.RedoCommand.Execute(null);
        vm.RedoCommand.Execute(null);

        Assert.Single(vm.PaintLayer().Cels[1].Frame!.Strokes);
    }
}
