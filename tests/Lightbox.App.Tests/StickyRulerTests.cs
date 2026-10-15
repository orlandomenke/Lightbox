using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The frame ruler stays at the top of what is visible when a docker is
/// scrolled down a long list of tracks or layers — on both tabs.
/// </summary>
/// <remarks>
/// Reported as "keep the playbar (the one with the frame numbers) sticky on
/// scroll". The owner asked for both rulers: the Timeline tab's, painted
/// inside <see cref="TrackView"/>, and the X-sheet's <see cref="TimelineRuler"/>,
/// which sits above the cel rows in the same scroller.
/// </remarks>
[Collection("BrushState")]
public class StickyRulerTests : BrushStateIsolated
{
    private static void Pump() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    // ---- the arithmetic ---------------------------------------------------------

    [Theory]
    [InlineData(-30, 500, 0)]   // not scrolled: the ruler is where it always was
    [InlineData(0, 500, 0)]
    [InlineData(120, 500, 120)] // scrolled 120 down: the ruler follows
    [InlineData(490, 500, 480)] // never past the last ruler-height of the control
    public void TheRulerSitsAtTheVisibleTopWithinTheControl(double visibleTop, double height, double expected) =>
        Assert.Equal(expected, TrackView.StickyTop(visibleTop, height, 20));

    // ---- the Timeline tab -------------------------------------------------------

    private static TrackRow Row(int i) => new($"Layer {i}", [0, 4], [3, 7], [false, false]);

    [AvaloniaFact]
    public void TheTrackViewsRulerFollowsTheScroll()
    {
        var view = new TrackView
        {
            FrameCount = 24, FrameWidth = 12,
            Tracks = Enumerable.Range(0, 40).Select(Row).ToList(),
        };
        var scroller = new ScrollViewer { Content = view, Height = 200, Width = 400 };
        var window = new Window { Content = scroller, Width = 400, Height = 200 };
        window.Show();
        Pump();
        Assert.Equal(0, view.RulerTop);

        scroller.Offset = new Vector(0, 150);
        Pump();

        Assert.Equal(150, view.RulerTop);
        // On the ruler where it is painted now — so a press there scrubs —
        // and no longer at the top strip, which is scrolled out of sight.
        Assert.True(view.OnRuler(155));
        Assert.False(view.OnRuler(5));
    }

    [AvaloniaFact]
    public void APressOnThePinnedRulerScrubsRatherThanGrabbingAKey()
    {
        var view = new TrackView
        {
            FrameCount = 24, FrameWidth = 12,
            Tracks = Enumerable.Range(0, 40).Select(Row).ToList(),
        };
        var scroller = new ScrollViewer { Content = view, Height = 200, Width = 400 };
        var window = new Window { Content = scroller, Width = 400, Height = 200 };
        window.Show();
        Pump();
        scroller.Offset = new Vector(0, 150);
        Pump();
        var grabbed = false;
        view.KeySelectRequested += (_, _, _, _) => grabbed = true;

        // Frame 4 is a key on every row, so a row under the band would grab it.
        var x = TrackView.XAtFrame(4, 12);
        var inWindow = view.TranslatePoint(new Point(x, view.RulerTop + 10), window)!.Value;
        window.MouseDown(inWindow, Avalonia.Input.MouseButton.Left);
        window.MouseUp(inWindow, Avalonia.Input.MouseButton.Left);
        Pump();

        Assert.False(grabbed);
        Assert.Equal(4, view.CurrentFrame);
    }

    // ---- the X-sheet tab --------------------------------------------------------

    [AvaloniaFact]
    public void TheXsheetsRulerFollowsTheScroll()
    {
        var window = new MainWindow { Width = 1400, Height = 900 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        while (vm.Doc.Scene.Layers.Count < 40) vm.AddPaintedLayerCommand.Execute(null);
        vm.Workspace.Activate(Lightbox.App.Docking.DockPanelId.Xsheet);
        Pump();
        var ruler = window.FindControl<TimelineRuler>("Ruler")!;
        // Whichever scroller above the ruler actually scrolls.
        var scroller = ruler.GetVisualAncestors().OfType<ScrollViewer>()
            .First(s => s.Extent.Height > s.Viewport.Height + 1);

        scroller.Offset = new Vector(0, 300);
        Pump();

        var shift = Assert.IsType<TranslateTransform>(ruler.RenderTransform);
        Assert.True(shift.Y > 0, $"ruler shifted by {shift.Y}");
        Assert.Equal(1, ruler.ZIndex);
        // Back at the top it is exactly where it always was.
        scroller.Offset = new Vector(0, 0);
        Pump();
        Assert.Null(ruler.RenderTransform);
    }

    // ---- in the real dockers ----------------------------------------------------

    private static (MainWindow Window, MainViewModel Vm) ManyLayers(Lightbox.App.Docking.DockPanelId panel)
    {
        var window = new MainWindow { Width = 1400, Height = 900 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        while (vm.Doc.Scene.Layers.Count < 40) vm.AddPaintedLayerCommand.Execute(null);
        vm.Workspace.Activate(panel);
        Pump();
        return (window, vm);
    }

    private static ScrollViewer ScrollingAbove(Visual control) =>
        control.GetVisualAncestors().OfType<ScrollViewer>().First(s => s.Extent.Height > s.Viewport.Height + 1);

    [AvaloniaFact]
    public void TheTimelineTabsRulerFollowsTheDockersOwnScroller()
    {
        // The track view sits in a sideways-only scroller inside the docker's
        // vertical one: the viewport has to come through both.
        var (window, _) = ManyLayers(Lightbox.App.Docking.DockPanelId.Timeline);
        var view = window.FindControl<TrackView>("TimelineTrackView")!;
        var scroller = ScrollingAbove(view);

        scroller.Offset = new Vector(0, 300);
        Pump();

        Assert.True(view.RulerTop > 0, $"ruler top {view.RulerTop}");
    }

    [AvaloniaFact]
    public void TheXsheetsPinnedRulerIsAsWideAsItsRows()
    {
        // The rows scroll under the band; one wider than the ruler would show
        // through at the right-hand end of it.
        var (window, _) = ManyLayers(Lightbox.App.Docking.DockPanelId.Xsheet);
        var ruler = window.FindControl<TimelineRuler>("Ruler")!;
        var rows = window.FindControl<ItemsControl>("XsheetLayerList")!;

        Assert.True(ruler.Bounds.Width >= rows.Bounds.Width - 1,
            $"ruler {ruler.Bounds.Width} px, rows {rows.Bounds.Width} px");
    }

    [AvaloniaFact]
    public void RevealingTheActiveLayerLandsItBelowThePinnedRuler()
    {
        var (window, vm) = ManyLayers(Lightbox.App.Docking.DockPanelId.Xsheet);
        var ruler = window.FindControl<TimelineRuler>("Ruler")!;
        var rows = window.FindControl<ItemsControl>("XsheetLayerList")!;
        var scroller = ScrollingAbove(ruler);
        scroller.Offset = new Vector(0, scroller.Extent.Height);
        Pump();

        // A layer near the top of the sheet, revealed from the bottom.
        var target = 2;
        var row = (Control)rows.ContainerFromIndex(target)!;
        MainWindow.RevealVertically(row, 0, row.Bounds.Height, band: ruler.Bounds.Height);
        Pump();

        var rowTop = row.TranslatePoint(new Point(0, 0), scroller)!.Value.Y;
        Assert.True(rowTop >= ruler.Bounds.Height - 0.5, $"row top {rowTop}, band {ruler.Bounds.Height}");
    }
}
