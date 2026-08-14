using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// The volume and balance checker: per-frame ink area and centroid on the
/// active layer, flags on drift past the tolerance, and the two surfaces that
/// show the readings.
/// </summary>
/// <remarks>
/// The scenario throughout is the checker's own reason to exist: a shot where
/// one drawing lost half its mass. No single frame can show that — the flag
/// has to come from comparing the shot against itself.
/// </remarks>
[Collection("BrushState")]
public sealed class VolumeCheckTests : BrushStateIsolated
{
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, (MainViewModel)window.DataContext!);
    }

    private static Stroke Bar(double x0, double y0, double x1, double y1, double size) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#000000",
        Points = [new StrokePoint(x0, y0, 1), new StrokePoint(x1, y1, 1)],
        Brush = new BrushSettings
        {
            Size = size, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.1, AntiAlias = false,
        },
    };

    private static Frame FrameWith(params Stroke[] strokes) =>
        new() { Strokes = [.. strokes] };

    /// <summary>
    /// Four frames on the active layer: two same-mass drawings, a hold, and
    /// one drawing that lost most of its ink.
    /// </summary>
    private static void Rig(MainViewModel vm)
    {
        var layer = vm.Doc.Scene.Layers[Math.Clamp(vm.ActiveLayerIndex, 0, vm.Doc.Scene.Layers.Count - 1)];
        layer.Cels =
        [
            new Cel { Frame = FrameWith(Bar(100, 100, 300, 100, 40)) },
            new Cel { Frame = FrameWith(Bar(100, 200, 300, 200, 40)) },
            new Cel(),   // a hold: frame 2 shows frame 1's drawing
            new Cel { Frame = FrameWith(Bar(100, 300, 140, 300, 12)) },
        ];
        vm.Doc.Scene.FrameCount = 4;
    }

    [AvaloniaFact]
    public void OffMeansAbsentNothingMeasuredNothingShown()
    {
        var (window, vm) = Open();
        Rig(vm);

        Assert.False(vm.VolumeCheck);
        Assert.Empty(vm.VolumeRows);
        var canvas = window.GetVisualDescendants().OfType<Rendering.CanvasControl>().First();
        Assert.Null(canvas.BalanceDots);
    }

    [AvaloniaFact]
    public void TheDrawingThatLostItsMassIsTheOneFlagged()
    {
        var (_, vm) = Open();
        Rig(vm);

        vm.VolumeCheck = true;

        Assert.Equal(4, vm.VolumeRows.Count);
        // The two full drawings agree with the median: on model.
        Assert.False(vm.VolumeRows[0].Flagged);
        Assert.False(vm.VolumeRows[1].Flagged);
        // The shrunken one is the answer, not everything around it.
        Assert.True(vm.VolumeRows[3].Flagged,
            $"areas: {string.Join(", ", vm.VolumeRows.Select(r => r.Area.ToString("F0")))}");
        Assert.True(vm.VolumeRows[3].Area < vm.VolumeRows[0].Area / 2);
    }

    [AvaloniaFact]
    public void AHoldReadsExactlyAsTheDrawingItShows()
    {
        // Frame 2 holds frame 1's drawing: same drawing, same reading — and
        // measured once, so a scene on 2s costs half its frame count.
        var (_, vm) = Open();
        Rig(vm);

        vm.VolumeCheck = true;

        Assert.Equal(vm.VolumeRows[1].Area, vm.VolumeRows[2].Area);
        Assert.Equal(vm.VolumeRows[1].CentroidX, vm.VolumeRows[2].CentroidX);
        Assert.Equal(vm.VolumeRows[1].CentroidY, vm.VolumeRows[2].CentroidY);
    }

    [AvaloniaFact]
    public void TheCentroidLandsOnTheMark()
    {
        // A horizontal bar from (100,100) to (300,100): its mass centres near
        // (200, 100) in document coordinates, whatever scale the measurement
        // rendered at.
        var (_, vm) = Open();
        Rig(vm);

        vm.VolumeCheck = true;

        var row = vm.VolumeRows[0];
        Assert.True(row.HasInk);
        Assert.True(Math.Abs(row.CentroidX - 200) < 8, $"centroid X {row.CentroidX:F1}");
        Assert.True(Math.Abs(row.CentroidY - 100) < 8, $"centroid Y {row.CentroidY:F1}");
    }

    [AvaloniaFact]
    public void TheReadingsReachBothSurfaces()
    {
        // B58's lesson, applied in advance: a checker nothing displays is a
        // checker nobody has. The band and the arc must both receive rows.
        var (window, vm) = Open();
        Rig(vm);

        vm.VolumeCheck = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var canvas = window.GetVisualDescendants().OfType<Rendering.CanvasControl>().First();
        var track = window.GetVisualDescendants().OfType<TrackView>().First();
        Assert.NotNull(canvas.BalanceDots);
        // One dot per frame with ink — the hold repeats its drawing's dot, so
        // the arc keeps its timing.
        Assert.Equal(4, canvas.BalanceDots!.Count);
        Assert.NotNull(track.VolumeBars);
        Assert.Equal(4, track.VolumeBars!.Count);
        Assert.True(track.VolumeBars[3].Flagged);

        vm.VolumeCheck = false;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Null(canvas.BalanceDots);
        Assert.Null(track.VolumeBars);
    }

    [AvaloniaFact]
    public void ATighterToleranceFlagsWhatALooserOneForgives()
    {
        var (_, vm) = Open();
        var layer = vm.Doc.Scene.Layers[Math.Clamp(vm.ActiveLayerIndex, 0, vm.Doc.Scene.Layers.Count - 1)];
        layer.Cels =
        [
            new Cel { Frame = FrameWith(Bar(100, 100, 300, 100, 40)) },
            new Cel { Frame = FrameWith(Bar(100, 200, 300, 200, 40)) },
            // ~15% short: inside a 25% tolerance, outside a 5% one.
            new Cel { Frame = FrameWith(Bar(100, 300, 270, 300, 40)) },
        ];
        vm.Doc.Scene.FrameCount = 3;

        vm.Settings.VolumeTolerance = 0.25;
        vm.VolumeCheck = true;
        Assert.False(vm.VolumeRows[2].Flagged);

        vm.Settings.VolumeTolerance = 0.05;
        vm.RecomputeVolumeCheck();
        Assert.True(vm.VolumeRows[2].Flagged);
    }
}
