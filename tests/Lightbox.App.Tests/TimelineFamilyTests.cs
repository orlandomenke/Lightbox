using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The timeline family (Q58): the track timeline, the X-sheet and the graph
/// editor share the bottom slot as tabs, three views over one set of records.
/// </summary>
[Collection("BrushState")]
public sealed class TimelineFamilyTests : BrushStateIsolated
{
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, (MainViewModel)window.DataContext!);
    }

    [AvaloniaFact]
    public void TheFamilySharesTheBottomSlotWithTheTimelineInFront()
    {
        var layout = DockLayout.Default();

        Assert.Equal(DockSide.Bottom, layout.SideOf(DockPanelId.Timeline));
        Assert.Equal(DockSide.Bottom, layout.SideOf(DockPanelId.Xsheet));
        Assert.Equal(DockSide.Bottom, layout.SideOf(DockPanelId.GraphEditor));

        var slot = layout.SlotOf(DockPanelId.Timeline);
        Assert.Contains(DockPanelId.Xsheet, slot);
        Assert.Contains(DockPanelId.GraphEditor, slot);
        Assert.Equal(DockPanelId.Timeline, layout.ActiveOf(slot));
    }

    [AvaloniaFact]
    public void TheProjectionPutsTheCameraOnTopAndReadsTheSheet()
    {
        var (_, vm) = Open();

        // The default document: Paint (one drawing at frame 0) + Background.
        var tracks = vm.TimelineTracks;
        Assert.Equal(vm.LayerRows.Count, tracks.Count);   // no camera yet
        Assert.DoesNotContain(tracks, t => t.IsCamera);

        var paint = tracks.First(t => t.Name == "Paint");
        Assert.Contains(0, paint.Keys);

        vm.AddCameraCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        tracks = vm.TimelineTracks;
        Assert.True(tracks[0].IsCamera, "the camera track belongs on top, as the reference draws it");
        Assert.Equal(vm.LayerRows.Count + 1, tracks.Count);
    }

    [AvaloniaFact]
    public void AHoldReadsAsABarBehindItsDot()
    {
        var (_, vm) = Open();
        // Extend the Paint drawing's exposure three times: frames 1..3 hold
        // frame 0.
        var cell = vm.FrameCells.First(c => c.Index == 0);
        vm.ExtendExposureAt(cell);
        vm.ExtendExposureAt(cell);
        vm.ExtendExposureAt(cell);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var paint = vm.TimelineTracks.First(t => t.Name == "Paint");
        var at = paint.Keys.ToList().IndexOf(0);
        Assert.True(at >= 0, "the drawing's dot is gone");
        Assert.True(paint.HoldEnds[at] >= 1,
            $"a held drawing must carry its bar (hold end {paint.HoldEnds[at]})");
    }

    [AvaloniaFact]
    public void DraggingACameraDotRetimesTheKey()
    {
        var (_, vm) = Open();
        vm.AddCameraCommand.Execute(null);
        vm.SetCameraKeyCommand.Execute(null);   // a key at frame 0
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.MoveCameraKey(0, 5);

        Assert.Equal([5], vm.CameraKeyFrames);
    }

    [AvaloniaFact]
    public void TheGraphOffersTheSpacingPairEvenWithoutACamera()
    {
        var (_, vm) = Open();

        var series = vm.GraphSeriesList;
        Assert.Equal(2, series.Count);
        var measured = series.First(s => s.Name == "Spacing (measured)");
        var intended = series.First(s => s.Name == "Spacing (intended)");
        Assert.False(measured.Editable, "the spacing series is measured off the art, not editable");
        Assert.True(intended.Dashed, "the intent wears the dashed treatment, or the two are indistinguishable");
    }

    [AvaloniaFact]
    public void TheLegendTogglesACurveOffAndOn()
    {
        var (_, vm) = Open();

        _ = vm.GraphSeriesList;   // build the legend
        var row = vm.GraphLegend.First(l => l.Name == "Spacing (intended)");

        row.IsShown = false;
        Assert.DoesNotContain(vm.GraphSeriesList, s => s.Name == "Spacing (intended)");
        // The legend still offers the hidden curve — that is its whole job.
        Assert.Contains(vm.GraphLegend, l => l.Name == "Spacing (intended)");

        row.IsShown = true;
        Assert.Contains(vm.GraphSeriesList, s => s.Name == "Spacing (intended)");
    }

    [AvaloniaFact]
    public void TheLegendGrowsTheCameraRowsWhenACameraArrives()
    {
        var (_, vm) = Open();
        _ = vm.GraphSeriesList;
        Assert.DoesNotContain(vm.GraphLegend, l => l.Name == "Zoom");

        vm.AddCameraCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        _ = vm.GraphSeriesList;

        Assert.Contains(vm.GraphLegend, l => l.Name == "Zoom");
    }

    [AvaloniaFact]
    public void ADoubleClickKeysTheFramingAlreadyThere()
    {
        var (_, vm) = Open();
        // Room to author in: the default document is one frame long.
        var cell = vm.FrameCells.First(c => c.Index == 0);
        for (var i = 0; i < 7; i++) vm.ExtendExposureAt(cell);
        vm.AddCameraCommand.Execute(null);
        vm.SetCameraKeyCommand.Execute(null);
        vm.EditCameraKey("Zoom", 0, 0, 2.0);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.AddCameraKeyAt(6);

        Assert.Equal([0, 6], vm.CameraKeyFrames.Order());
        // The new key holds what was already interpolated there — authoring
        // it changed nothing visually.
        var zoom = vm.GraphSeriesList.First(s => s.Name == "Zoom");
        Assert.Equal(2.0, zoom.Samples[6], 3);
    }

    [AvaloniaFact]
    public void TheKeyMenuEditsEasingAndRemoval()
    {
        var (_, vm) = Open();
        vm.AddCameraCommand.Execute(null);
        vm.SetCameraKeyCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.SetCameraKeyEase(0, Lightbox.Core.Inbetween.Easing.EaseIn);
        Assert.Equal(Lightbox.Core.Inbetween.Easing.EaseIn, vm.CameraKeyEaseAt(0));

        vm.RemoveCameraKeyAt(0);
        Assert.Empty(vm.CameraKeyFrames);
        Assert.Null(vm.CameraKeyEaseAt(0));
    }

    [AvaloniaFact]
    public void EditingACameraKeyThroughTheGraphWritesTheChannelItNames()
    {
        var (_, vm) = Open();
        vm.AddCameraCommand.Execute(null);
        vm.SetCameraKeyCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.EditCameraKey("Zoom", fromFrame: 0, toFrame: 0, value: 2.0);

        var zoom = vm.GraphSeriesList.First(s => s.Name == "Zoom");
        Assert.Equal(2.0, zoom.Samples[0], 3);
    }
}

/// <summary>The track view's geometry, held still for the pointer's sake.</summary>
public class TrackViewGeometryTests
{
    [Theory]
    [InlineData(0, 14)]
    [InlineData(11, 14)]
    [InlineData(47, 22)]
    public void TheFrameUnderADotIsTheFrameTheDotWasDrawnAt(int frame, double width)
    {
        var x = TrackView.XAtFrame(frame, width);
        Assert.Equal(frame, TrackView.FrameAtX(x, width, 240));
    }

    [Fact]
    public void TheRowUnderADotIsTheRowItWasDrawnIn()
    {
        for (var row = 0; row < 6; row++)
        {
            Assert.Equal(row, TrackView.RowAtY(TrackView.YAtRow(row), 6));
        }
    }

    [Fact]
    public void TheCameraAlwaysWearsTheCameraColour()
    {
        Assert.Equal(TrackView.ColourOf(0, isCamera: true), TrackView.ColourOf(5, isCamera: true));
        Assert.NotEqual(TrackView.ColourOf(0, false), TrackView.ColourOf(1, false));
    }
}

/// <summary>The graph view's value mapping is its own inverse.</summary>
public class GraphViewGeometryTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.37)]
    [InlineData(1.0)]
    public void ValueAndPixelAreInverses(double t)
    {
        var range = (Min: -40.0, Max: 200.0);
        var value = range.Min + t * (range.Max - range.Min);
        var y = GraphView.YAtValue(value, range, plotHeight: 160);
        Assert.Equal(value, GraphView.ValueAtY(y, range, plotHeight: 160), 6);
    }

    [Fact]
    public void AFlatSeriesStillGetsARangeToLiveIn()
    {
        var flat = new GraphSeries("x", Avalonia.Media.Colors.Red, [3.0, 3.0, 3.0], [], false);
        var (min, max) = GraphView.RangeOf(flat);
        Assert.True(min < 3 && max > 3, $"a flat line at 3 got the degenerate range ({min}, {max})");
    }

    [Fact]
    public void AnAllNaNSeriesDoesNotPoisonTheRange()
    {
        var empty = new GraphSeries("x", Avalonia.Media.Colors.Red, [double.NaN, double.NaN], [], false);
        var (min, max) = GraphView.RangeOf(empty);
        Assert.True(max > min);
    }
}
