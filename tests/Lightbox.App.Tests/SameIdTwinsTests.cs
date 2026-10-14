using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Drawing on a hold after the document has been reopened (B467, Q260). An
/// effect baked on 2s shares one drawing between two cels. Reopened, that
/// used to be two drawings with one id, and a stroke drawn on the second cel
/// was added to the first — measured on main: the counts came back (2, 1)
/// where (1, 2) was drawn.
/// </summary>
public class SameIdTwinsTests
{
    /// <summary>A layer whose first two cels hold one drawing, saved and read back as a file would be.</summary>
    private static (MainViewModel Vm, Layer Layer) ReopenedHold(Frame? second = null)
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.AddFrameCommand.Execute(null);
        var layer = vm.Doc.Scene.Layers[vm.ActiveLayerIndex];
        var held = new Frame { Strokes = [new Stroke { Points = [new StrokePoint(50, 50, 1), new StrokePoint(80, 50, 1)] }] };
        layer.Cels[0].Frame = held;
        if (second is not null) second.Id = held.Id;
        layer.Cels[1].Frame = second ?? held;

        var reopened = DocJson.Deserialize(DocJson.Serialize(vm.Doc));
        layer.Cels[0].Frame = reopened.Scene.Layers[vm.ActiveLayerIndex].Cels[0].Frame;
        layer.Cels[1].Frame = reopened.Scene.Layers[vm.ActiveLayerIndex].Cels[1].Frame;
        return (vm, layer);
    }

    private static void Draw(MainViewModel vm)
    {
        vm.BeginStroke(200, 200, 1);
        vm.MoveStroke(250, 220, 1);
        vm.MoveStroke(300, 240, 1);
        vm.EndStroke();
    }

    [AvaloniaFact]
    public void AStrokeDrawnOnTheSecondCelOfAReopenedHoldLandsOnTheHold()
    {
        var (vm, layer) = ReopenedHold();
        Assert.Same(layer.Cels[0].Frame, layer.Cels[1].Frame);
        var before = layer.Cels[1].Frame!.Strokes.Count;
        vm.CurrentFrameIndex = 1;

        Draw(vm);

        // One drawing, so both cels show the new line, as any hold does.
        Assert.Equal(before + 1, layer.Cels[1].Frame!.Strokes.Count);
        Assert.Same(layer.Cels[0].Frame, layer.Cels[1].Frame);
    }

    [AvaloniaFact]
    public void TwoDifferentDrawingsThatOnceSharedAnIdEachKeepTheirOwnLines()
    {
        // What a bug or a hand-edited file can make: two different drawings
        // under one id. They are kept apart, and a stroke drawn on the second
        // lands on the second.
        var other = new Frame { Strokes = [new Stroke { Points = [new StrokePoint(400, 400, 1), new StrokePoint(420, 400, 1)] }] };
        var (vm, layer) = ReopenedHold(other);
        Assert.NotSame(layer.Cels[0].Frame, layer.Cels[1].Frame);
        Assert.NotEqual(layer.Cels[0].Frame!.Id, layer.Cels[1].Frame!.Id);
        var before = (layer.Cels[0].Frame!.Strokes.Count, layer.Cels[1].Frame!.Strokes.Count);
        vm.CurrentFrameIndex = 1;

        Draw(vm);

        Assert.Equal((before.Item1, before.Item2 + 1), (layer.Cels[0].Frame!.Strokes.Count, layer.Cels[1].Frame!.Strokes.Count));
    }
}
