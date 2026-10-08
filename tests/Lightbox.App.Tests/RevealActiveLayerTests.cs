using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.Input;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Choosing a layer brings its row into view in the timeline and the X-sheet.
/// </summary>
/// <remarks>
/// <para>
/// The layer docker, the timeline and the X-sheet are three lists of the same
/// layers, each scrolling on its own. On a document with more layers than fit,
/// picking one in the docker left the other two showing whatever they showed
/// before — the layer the artist had just chosen was somewhere off the edge.
/// </para>
/// <para>
/// <b>The scroll is the smallest one that shows the row</b>, and none at all
/// when it is already showing: a list that re-centres on every click is worse
/// than one that never moves. <see cref="ScrollReveal"/> is that arithmetic on
/// its own, tested without a window; the rest drives the real window with
/// enough layers to need scrolling.
/// </para>
/// </remarks>
public class RevealActiveLayerTests(Xunit.ITestOutputHelper output) : BrushStateIsolated
{
    // ---- the arithmetic --------------------------------------------------------

    [Theory]
    // offset, viewport, top, bottom → new offset
    [InlineData(0, 100, 20, 40, 0)] // already showing: nothing moves
    [InlineData(50, 100, 60, 150, 50)] // exactly filling to the bottom edge: nothing moves
    [InlineData(0, 100, 150, 170, 70)] // below: scroll down until its bottom is on the edge
    [InlineData(200, 100, 150, 170, 150)] // above: scroll up until its top is on the edge
    [InlineData(0, 100, 90, 110, 10)] // half off the bottom: just enough
    [InlineData(100, 100, 90, 110, 90)] // half off the top: just enough
    [InlineData(0, 100, 300, 500, 300)] // taller than the view: its top wins
    public void TheSmallestScrollThatShowsTheRow(double offset, double viewport, double top, double bottom, double expected)
    {
        Assert.Equal(expected, ScrollReveal.Offset(offset, viewport, top, bottom));
    }

    [Fact]
    public void AViewWithNoSizeYetIsLeftAlone()
    {
        // Before the first layout the viewport is zero; "scrolling to show"
        // against that would throw the list to the row's top for no reason.
        Assert.Equal(40, ScrollReveal.Offset(40, 0, 500, 520));
    }

    // ---- the window ------------------------------------------------------------

    private const int Layers = 40;

    private static void Pump() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    private static (MainWindow Window, MainViewModel Vm) Open(Lightbox.App.Docking.DockPanelId front)
    {
        var window = new MainWindow { Width = 1400, Height = 800 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Many", 400, 300, 12, 72, "#ffffff", false));
        while (vm.Doc.Scene.Layers.Count < Layers) vm.AddPaintedLayerCommand.Execute(null);
        vm.Workspace.Activate(front);
        Pump();
        return (window, vm);
    }

    /// <summary>
    /// Every scroller above a control that can scroll vertically, nearest first.
    /// </summary>
    /// <remarks>
    /// More than one, and that is the point: the sheet has a scroller of its
    /// own <em>inside</em> the docker's, and the docker's hands it all the
    /// height it asks for — so the inner one never scrolls vertically and the
    /// outer one does. The first draft of these tests measured the nearest and
    /// passed on a window where nothing had moved. A row is showing only if it
    /// is inside the viewport of every one of them.
    /// </remarks>
    private static List<ScrollViewer> Scrollers(Visual from) =>
        [.. from.GetVisualAncestors().OfType<ScrollViewer>()
            .Where(s => s.VerticalScrollBarVisibility != Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled)];

    /// <summary>The one that actually has more content than room.</summary>
    private static ScrollViewer Scrolling(Visual from) =>
        Scrollers(from).First(s => s.Extent.Height > s.Viewport.Height + 1);

    private void AssertShowing(Visual control, double y, double height, string what)
    {
        var scrollers = Scrollers(control);
        Assert.NotEmpty(scrollers);
        foreach (var scroller in scrollers)
        {
            var top = control.TranslatePoint(new Point(0, y), scroller)!.Value.Y;
            var viewport = scroller.Viewport.Height;
            output.WriteLine(
                $"{what}: {top:0.#}..{top + height:0.#} in a viewport of {viewport:0.#} "
                + $"(extent {scroller.Extent.Height:0.#}, offset {scroller.Offset.Y:0.#})");
            Assert.True(viewport > 0, $"{what}: a scroller has no viewport");
            Assert.True(top >= -0.5 && top + height <= viewport + 0.5,
                $"{what}: {top:0.#}..{top + height:0.#} is not inside 0..{viewport:0.#}");
        }
    }

    private void AssertSheetRowShowing(MainWindow window, MainViewModel vm, string what)
    {
        var list = window.FindControl<ItemsControl>("XsheetLayerList")!;
        var index = vm.LayerRows.ToList().FindIndex(r => ReferenceEquals(r.Layer, vm.Doc.Scene.Layers[vm.ActiveLayerIndex]));
        var container = (Control)list.ContainerFromIndex(index)!;
        AssertShowing(container, 0, container.Bounds.Height, $"{what} (x-sheet row {index})");
    }

    private void AssertTimelineRowShowing(MainWindow window, MainViewModel vm, string what)
    {
        var view = window.FindControl<TrackView>("TimelineTrackView")!;
        var row = MainWindow.TimelineRowOfActiveLayer(vm);
        // Held to the track list itself, not only to the function that counted:
        // the row it names has to be the active layer's own track.
        Assert.Equal(vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Name, view.Tracks![row].Name);
        AssertShowing(view, TrackView.RulerHeight + row * TrackView.RowPitch, TrackView.RowPitch, $"{what} (timeline row {row})");
    }

    [AvaloniaFact]
    public void TheFixtureReallyNeedsScrolling()
    {
        // Otherwise every test below passes on a window that shows all forty
        // rows whatever the code does.
        var (window, vm) = Open(Lightbox.App.Docking.DockPanelId.Xsheet);
        var sheet = Scrolling(window.FindControl<ItemsControl>("XsheetLayerList")!);
        output.WriteLine($"x-sheet: extent {sheet.Extent.Height:0.#}, viewport {sheet.Viewport.Height:0.#}");
        Assert.True(sheet.Extent.Height > sheet.Viewport.Height * 1.5);

        vm.Workspace.Activate(Lightbox.App.Docking.DockPanelId.Timeline);
        Pump();
        var timeline = Scrolling(window.FindControl<TrackView>("TimelineTrackView")!);
        output.WriteLine($"timeline: extent {timeline.Extent.Height:0.#}, viewport {timeline.Viewport.Height:0.#}");
        Assert.True(timeline.Extent.Height > timeline.Viewport.Height * 1.5);
    }

    [AvaloniaTheory]
    [InlineData(0)] // the bottom of the stack, which is the last row
    [InlineData(Layers - 1)] // the top
    [InlineData(Layers / 2)]
    public void ChoosingALayerShowsItsRowInTheXsheet(int layerIndex)
    {
        var (window, vm) = Open(Lightbox.App.Docking.DockPanelId.Xsheet);
        // Start as far from it as the list goes, so there is something to do.
        vm.ActiveLayerIndex = layerIndex == 0 ? Layers - 1 : 0;
        Pump();

        vm.ActiveLayerIndex = layerIndex;
        Pump();

        AssertSheetRowShowing(window, vm, $"layer {layerIndex}");
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(Layers - 1)]
    [InlineData(Layers / 2)]
    public void ChoosingALayerShowsItsRowInTheTimeline(int layerIndex)
    {
        var (window, vm) = Open(Lightbox.App.Docking.DockPanelId.Timeline);
        vm.ActiveLayerIndex = layerIndex == 0 ? Layers - 1 : 0;
        Pump();

        vm.ActiveLayerIndex = layerIndex;
        Pump();

        AssertTimelineRowShowing(window, vm, $"layer {layerIndex}");
    }

    [AvaloniaFact]
    public void ALayerAlreadyShowingMovesNothing()
    {
        var (window, vm) = Open(Lightbox.App.Docking.DockPanelId.Xsheet);
        vm.ActiveLayerIndex = Layers - 1;
        Pump();
        var scroller = Scrolling(window.FindControl<ItemsControl>("XsheetLayerList")!);
        var before = scroller.Offset;

        // The neighbour is the next row down and is on screen with it.
        vm.ActiveLayerIndex = Layers - 2;
        Pump();

        AssertSheetRowShowing(window, vm, "the neighbouring layer");
        Assert.Equal(before, scroller.Offset);
    }

    [AvaloniaFact]
    public void RevealingARowNeverMovesTheFrames()
    {
        // The sheet scrolls both ways. Finding a layer is a vertical question;
        // it must not throw away where the artist was along the timeline.
        var (window, vm) = Open(Lightbox.App.Docking.DockPanelId.Xsheet);
        // Wide cells, so the frames run off the right of the docker.
        vm.TimelineFrameWidth = 96;
        Pump();
        vm.ActiveLayerIndex = Layers - 1;
        Pump();
        // The horizontal scroll is the sheet's own, the nearest one.
        var scroller = Scrollers(window.FindControl<ItemsControl>("XsheetLayerList")!)[0];
        scroller.Offset = scroller.Offset.WithX(Math.Min(120, Math.Max(0, scroller.Extent.Width - scroller.Viewport.Width)));
        Pump();
        var x = scroller.Offset.X;

        vm.ActiveLayerIndex = 0;
        Pump();

        AssertSheetRowShowing(window, vm, "the bottom layer");
        Assert.True(x > 0, "the fixture never scrolled sideways, so this proves nothing");
        Assert.Equal(x, scroller.Offset.X);
    }

    [AvaloniaFact]
    public void ALayerChosenWhileTheSheetWasBehindIsShowingWhenTheSheetComesForward()
    {
        // The timeline and the sheet share a docker as tabs. Choosing a layer
        // with the timeline in front must not leave the sheet stale for when
        // it is brought forward.
        var (window, vm) = Open(Lightbox.App.Docking.DockPanelId.Timeline);
        vm.ActiveLayerIndex = Layers - 1;
        Pump();
        vm.ActiveLayerIndex = 0;
        Pump();

        vm.Workspace.Activate(Lightbox.App.Docking.DockPanelId.Xsheet);
        Pump();

        AssertSheetRowShowing(window, vm, "the bottom layer, after switching tabs");
    }

    [AvaloniaFact]
    public void WithACameraTheLayersRowIsOneFurtherDown()
    {
        // The camera's track is the timeline's first row. A row index that
        // forgot it would reveal the layer above the one chosen.
        var (window, vm) = Open(Lightbox.App.Docking.DockPanelId.Timeline);
        vm.AddCameraCommand.Execute(null);
        Pump();
        var view = window.FindControl<TrackView>("TimelineTrackView")!;
        Assert.Equal("Camera", view.Tracks![0].Name);
        vm.ActiveLayerIndex = Layers - 1;
        Pump();

        vm.ActiveLayerIndex = 3;
        Pump();

        var index = vm.LayerRows.ToList().FindIndex(r => ReferenceEquals(r.Layer, vm.Doc.Scene.Layers[3]));
        Assert.Equal(index + 1, MainWindow.TimelineRowOfActiveLayer(vm));
        AssertTimelineRowShowing(window, vm, "layer 3 under a camera");
    }

    [AvaloniaFact]
    public void WhenTwoListsAboveARowBothScrollBothAreMovedRight()
    {
        // Today's dockers never have this — the inner list is handed all the
        // height it asks for — but nothing forbids it, and the arithmetic has
        // a trap in it: scrolling the inner list does not move the row until
        // the next layout, so the outer one would be worked out from where the
        // row used to be.
        var row = new Border { Height = 20 };
        var innerContent = new StackPanel();
        innerContent.Children.Add(new Border { Height = 900 });
        innerContent.Children.Add(row);
        innerContent.Children.Add(new Border { Height = 300 });
        var inner = new ScrollViewer { Height = 200, Content = innerContent };
        var outerContent = new StackPanel();
        outerContent.Children.Add(new Border { Height = 700 });
        outerContent.Children.Add(inner);
        outerContent.Children.Add(new Border { Height = 700 });
        var outer = new ScrollViewer { Content = outerContent };
        var window = new Window { Width = 300, Height = 150, Content = outer };
        window.Show();
        Pump();

        MainWindow.RevealVertically(row, 0, 20);
        Pump();

        foreach (var (name, scroller) in new[] { ("inner", inner), ("outer", outer) })
        {
            var top = row.TranslatePoint(new Point(0, 0), scroller)!.Value.Y;
            output.WriteLine($"{name}: row at {top:0.#}..{top + 20:0.#} of {scroller.Viewport.Height:0.#}, offset {scroller.Offset.Y:0.#}");
            Assert.True(top >= -0.5 && top + 20 <= scroller.Viewport.Height + 0.5, $"{name}: {top:0.#}");
        }
    }
}
