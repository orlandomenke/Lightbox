using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The timing chart through the view model (Q58): authored on the extreme
/// under the cel, one undo step, and the deterministic inbetweener obeys it
/// over the bar's count and easing.
/// </summary>
[Collection("BrushState")]
public sealed class TimingChartVmTests : BrushStateIsolated
{
    private static MainViewModel NewVm() => new();

    private static FrameCell CellFor(MainViewModel vm, int index) =>
        new(index) { LayerIndex = vm.ActiveLayerIndex };

    [AvaloniaFact]
    public void SettingAChartWritesTheExtremeAndUndoes()
    {
        var vm = NewVm();
        vm.BeginStroke(0, 0, 0.5);
        vm.MoveStroke(50, 0, 0.5);
        vm.EndStroke();

        vm.SetChartAt(CellFor(vm, 0), [0.75, 0.25]);

        // Normalised on the way in: sorted, and the record carries it.
        Assert.Equal([0.25, 0.75], vm.PaintedCel().Chart);
        Assert.Equal([0.25, 0.75], vm.ChartAt(CellFor(vm, 0)));

        vm.UndoCommand.Execute(null);
        Assert.Null(vm.PaintedCel().Chart);

        vm.RedoCommand.Execute(null);
        Assert.Equal([0.25, 0.75], vm.PaintedCel().Chart);
    }

    [AvaloniaFact]
    public void ClearingAChartRemovesTheKeyEntirely()
    {
        var vm = NewVm();
        vm.BeginStroke(0, 0, 0.5);
        vm.MoveStroke(50, 0, 0.5);
        vm.EndStroke();
        vm.SetChartAt(CellFor(vm, 0), [0.5]);

        vm.SetChartAt(CellFor(vm, 0), null);

        Assert.Null(vm.PaintedCel().Chart);
    }

    [AvaloniaFact]
    public void TheChartOnAHoldsCelReachesItsKey()
    {
        // The menu opens on frame 2 of a held drawing; the chart lands on
        // the key that owns the hold, not nowhere.
        var vm = NewVm();
        vm.BeginStroke(0, 0, 0.5);
        vm.MoveStroke(50, 0, 0.5);
        vm.EndStroke();
        vm.Doc.Scene.FrameCount = 4;

        vm.SetChartAt(CellFor(vm, 2), [0.5]);

        Assert.Equal([0.5], vm.PaintedCel().Chart);
        Assert.Equal(0, vm.ChartAnchorFrame(CellFor(vm, 2)));
    }

    [AvaloniaFact]
    public void TheInbetweenerObeysTheChartOverTheBar()
    {
        var vm = NewVm();

        // Key A at y=0, key B at y=100.
        vm.BeginStroke(0, 0, 0.5);
        vm.MoveStroke(50, 0, 0.5);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null);
        vm.BeginStroke(0, 100, 0.5);
        vm.MoveStroke(50, 100, 0.5);
        vm.EndStroke();

        vm.CurrentFrameIndex = 0;
        vm.TweenCount = 3;              // the bar asks for three, evenly
        vm.SetChartAt(CellFor(vm, 0), [0.9]);   // the chart asks for ONE, at 90%

        vm.InsertInbetweensCommand.Execute(null);

        // One inbetween, at the rung — not three at the bar's easing.
        var layer = vm.PaintLayer();
        Assert.Equal(3, vm.Doc.Scene.FrameCount);
        var tween = Assert.IsType<Frame>(layer.Cels[1].Frame);
        var stroke = Assert.Single(tween.Strokes);
        Assert.Equal(90, stroke.Points[0].Y, 1);
    }
}
