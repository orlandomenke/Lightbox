using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Lightbox.App.Input;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Holding a drag near the top or bottom of a list scrolls the list that way.
/// </summary>
/// <remarks>
/// <para>
/// A drag can only be dropped on what is showing, so a list longer than its
/// docker needs to move under a drag or the far end of it cannot be reached.
/// The layer docker had this in name: it nudged a scroller on every pointer
/// move — the wrong scroller, one nested inside the docker's own that is handed
/// all the height it asks for and so never scrolls — and did nothing at all
/// while the pointer was held still, which is exactly when an artist is
/// waiting for the list to come to them.
/// </para>
/// <para>
/// <b>The scroll runs on a clock, not on pointer moves</b>, and the tests drive
/// the clock by hand through <c>Tick</c>: a headless test that sleeps to let a
/// timer fire is a test that fails on a busy machine.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class DragEdgeScrollTests(Xunit.ITestOutputHelper output) : BrushStateIsolated
{
    // ---- how fast, from how close ---------------------------------------------

    [Theory]
    [InlineData(100)] // the middle
    [InlineData(EdgeScroll.Band + 1)] // just clear of the top band
    [InlineData(200 - EdgeScroll.Band - 1)] // just clear of the bottom band
    public void AwayFromTheEdgesNothingMoves(double y)
    {
        Assert.Equal(0, EdgeScroll.Speed(y, 200));
    }

    [Fact]
    public void NearTheTopItScrollsUpAndNearTheBottomDown()
    {
        Assert.True(EdgeScroll.Speed(4, 200) < 0);
        Assert.True(EdgeScroll.Speed(196, 200) > 0);
    }

    [Fact]
    public void CloserToTheEdgeIsFaster()
    {
        var far = EdgeScroll.Speed(200 - EdgeScroll.Band + 2, 200);
        var mid = EdgeScroll.Speed(200 - EdgeScroll.Band / 2, 200);
        var near = EdgeScroll.Speed(199, 200);

        output.WriteLine($"entering the band {far}, half way {mid}, at the edge {near} px per tick");
        Assert.True(far >= EdgeScroll.Slowest);
        Assert.True(far < mid && mid < near);
        Assert.True(near <= EdgeScroll.Fastest);
        // The same shape at the top, mirrored.
        Assert.Equal(-near, EdgeScroll.Speed(1, 200));
    }

    [Theory]
    [InlineData(-40)] // dragged past the top of the list
    [InlineData(260)] // and past the bottom
    public void PastTheEdgeIsAsFastAsItGets(double y)
    {
        Assert.Equal(EdgeScroll.Fastest, Math.Abs(EdgeScroll.Speed(y, 200)));
    }

    [Fact]
    public void AListShorterThanTwoBandsStillHasAMiddle()
    {
        // Otherwise a small docker scrolls whichever half the pointer is in
        // and can never be dropped on.
        Assert.Equal(0, EdgeScroll.Speed(20, 40));
        Assert.True(EdgeScroll.Speed(2, 40) < 0);
        Assert.True(EdgeScroll.Speed(38, 40) > 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    public void AListWithNoHeightDoesNotScroll(double height)
    {
        Assert.Equal(0, EdgeScroll.Speed(10, height));
    }

    // ---- the layer docker ------------------------------------------------------

    private const int Layers = 40;

    private static void Pump() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow { Width = 1400, Height = 800 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Many", 400, 300, 12, 72, "#ffffff", false));
        while (vm.Doc.Scene.Layers.Count < Layers) vm.AddPaintedLayerCommand.Execute(null);
        for (var i = 0; i < vm.Doc.Scene.Layers.Count; i++) vm.Doc.Scene.Layers[i].Name = $"L{i:00}";
        vm.ActiveLayerIndex = Layers - 1;
        Pump();
        return (window, vm);
    }

    /// <summary>The scroller above a control that really has more content than room.</summary>
    private static ScrollViewer Scrolling(Visual from) =>
        from.GetVisualAncestors().OfType<ScrollViewer>().First(s => s.Extent.Height > s.Viewport.Height + 1);

    private static Point CentreOf(Control control, Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    /// <summary>Press on the first visible layer row and drag it to <paramref name="to"/>, leaving the button down.</summary>
    private static void PickUpAndHoldAt(MainWindow window, ItemsControl list, Point to)
    {
        var first = (Control)list.ContainerFromIndex(1)!;
        var from = CentreOf(first, window);
        window.MouseDown(from, MouseButton.Left);
        Pump();
        for (var i = 1; i <= 6; i++)
        {
            window.MouseMove(
                new Point(from.X + (to.X - from.X) * i / 6, from.Y + (to.Y - from.Y) * i / 6),
                RawInputModifiers.LeftMouseButton);
            Pump();
        }
    }

    [AvaloniaFact]
    public void TheLayerDockerReallyNeedsScrolling()
    {
        var (window, _) = Open();
        var scroller = Scrolling(window.FindControl<ItemsControl>("LayerList")!);

        output.WriteLine($"layers: extent {scroller.Extent.Height:0.#}, viewport {scroller.Viewport.Height:0.#}");
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height * 1.5);
    }

    [AvaloniaFact]
    public void HoldingADraggedLayerNearTheBottomKeepsScrollingDown()
    {
        var (window, _) = Open();
        var list = window.FindControl<ItemsControl>("LayerList")!;
        var scroller = Scrolling(list);
        var edge = scroller.TranslatePoint(new Point(scroller.Bounds.Width / 2, scroller.Bounds.Height - 6), window)!.Value;

        PickUpAndHoldAt(window, list, edge);
        // The pointer is now still. Nothing but the clock moves the list.
        var offsets = new List<double> { scroller.Offset.Y };
        for (var i = 0; i < 5; i++)
        {
            Assert.True(window.EdgeScroll.Tick(), $"tick {i} scrolled nothing");
            Pump();
            offsets.Add(scroller.Offset.Y);
        }

        output.WriteLine(string.Join(" → ", offsets.Select(o => o.ToString("0.#"))));
        Assert.True(window.EdgeScroll.IsScrolling);
        for (var i = 1; i < offsets.Count; i++) Assert.True(offsets[i] > offsets[i - 1], $"tick {i} did not move down");
        window.MouseUp(edge, MouseButton.Left);
        Pump();
    }

    [AvaloniaFact]
    public void ItStopsAtTheEndOfTheListAndWhenTheDragEnds()
    {
        var (window, _) = Open();
        var list = window.FindControl<ItemsControl>("LayerList")!;
        var scroller = Scrolling(list);
        var edge = scroller.TranslatePoint(new Point(scroller.Bounds.Width / 2, scroller.Bounds.Height - 6), window)!.Value;
        PickUpAndHoldAt(window, list, edge);

        var ticks = 0;
        while (window.EdgeScroll.Tick() && ticks < 2000)
        {
            Pump();
            ticks++;
        }

        var end = scroller.Extent.Height - scroller.Viewport.Height;
        output.WriteLine($"{ticks} ticks to the end: offset {scroller.Offset.Y:0.#} of {end:0.#}");
        Assert.True(ticks < 2000, "it never reached the end");
        Assert.Equal(end, scroller.Offset.Y, 1);

        window.MouseUp(edge, MouseButton.Left);
        Pump();
        Assert.False(window.EdgeScroll.IsScrolling);
        Assert.False(window.EdgeScroll.Tick());
    }

    [AvaloniaFact]
    public void NearTheTopItScrollsBackUp()
    {
        var (window, _) = Open();
        var list = window.FindControl<ItemsControl>("LayerList")!;
        var scroller = Scrolling(list);
        scroller.Offset = scroller.Offset.WithY(200);
        Pump();
        var edge = scroller.TranslatePoint(new Point(scroller.Bounds.Width / 2, 5), window)!.Value;
        // Pick up a row that is on screen after the scroll.
        var row = Enumerable.Range(0, Layers).Select(i => list.ContainerFromIndex(i)).OfType<Control>()
            .First(c => c.TranslatePoint(new Point(0, 0), scroller)!.Value.Y is var top
                && top > 26 && top + c.Bounds.Height < scroller.Viewport.Height);
        var from = CentreOf(row, window);
        window.MouseDown(from, MouseButton.Left);
        Pump();
        for (var i = 1; i <= 6; i++)
        {
            window.MouseMove(new Point(from.X, from.Y + (edge.Y - from.Y) * i / 6), RawInputModifiers.LeftMouseButton);
            Pump();
        }

        Assert.True(window.EdgeScroll.IsScrolling, "the drag never reached the top band");
        var before = scroller.Offset.Y;
        Assert.True(window.EdgeScroll.Tick());
        Assert.True(window.EdgeScroll.Tick());
        Pump();

        output.WriteLine($"{before:0.#} → {scroller.Offset.Y:0.#}");
        Assert.True(scroller.Offset.Y < before);
        window.MouseUp(edge, MouseButton.Left);
        Pump();
    }

    [AvaloniaFact]
    public void MovingBackToTheMiddleStopsIt()
    {
        var (window, _) = Open();
        var list = window.FindControl<ItemsControl>("LayerList")!;
        var scroller = Scrolling(list);
        var edge = scroller.TranslatePoint(new Point(scroller.Bounds.Width / 2, scroller.Bounds.Height - 6), window)!.Value;
        PickUpAndHoldAt(window, list, edge);
        Assert.True(window.EdgeScroll.Tick());

        var middle = scroller.TranslatePoint(new Point(scroller.Bounds.Width / 2, scroller.Bounds.Height / 2), window)!.Value;
        window.MouseMove(middle, RawInputModifiers.LeftMouseButton);
        Pump();

        var before = scroller.Offset.Y;
        Assert.False(window.EdgeScroll.Tick());
        Assert.Equal(before, scroller.Offset.Y);
        window.MouseUp(middle, MouseButton.Left);
        Pump();
    }

    [AvaloniaFact]
    public void APointerNearTheEdgeWithNothingPickedUpScrollsNothing()
    {
        // Hovering at the bottom of a list is not a request to scroll it.
        var (window, _) = Open();
        var scroller = Scrolling(window.FindControl<ItemsControl>("LayerList")!);
        var edge = scroller.TranslatePoint(new Point(scroller.Bounds.Width / 2, scroller.Bounds.Height - 6), window)!.Value;

        window.MouseMove(edge);
        Pump();

        Assert.False(window.EdgeScroll.IsScrolling);
        Assert.False(window.EdgeScroll.Tick());
        Assert.Equal(0, scroller.Offset.Y);
    }

    [AvaloniaFact]
    public void TheLayerCanBeDroppedOnARowThatWasOffTheBottom()
    {
        // The point of the whole thing: reach a row the docker was not showing.
        var (window, vm) = Open();
        var list = window.FindControl<ItemsControl>("LayerList")!;
        var scroller = Scrolling(list);
        var carried = ((LayerRow)vm.LayerPanelItems[1]).Layer;
        var edge = scroller.TranslatePoint(new Point(scroller.Bounds.Width / 2, scroller.Bounds.Height - 6), window)!.Value;
        var lastVisibleBefore = Enumerable.Range(0, vm.LayerPanelItems.Count)
            .Last(i => list.ContainerFromIndex(i) is Control c
                && c.TranslatePoint(new Point(0, 0), scroller)!.Value.Y < scroller.Viewport.Height);

        PickUpAndHoldAt(window, list, edge);
        for (var i = 0; i < 2000 && window.EdgeScroll.Tick(); i++) Pump();
        // Let go a little above the edge, on a row.
        var drop = new Point(edge.X, edge.Y - 30);
        window.MouseMove(drop, RawInputModifiers.LeftMouseButton);
        Pump();
        window.MouseUp(drop, MouseButton.Left);
        Pump();

        var landed = vm.LayerPanelItems.ToList().FindIndex(i => i is LayerRow r && ReferenceEquals(r.Layer, carried));
        output.WriteLine($"last row showing before the drag: {lastVisibleBefore}; the layer landed at row {landed}");
        Assert.True(landed > lastVisibleBefore, $"{landed} is not past {lastVisibleBefore}");
    }

    // ---- any other drag: found by where the pointer is ---------------------------

    [AvaloniaFact]
    public void ADragOverAnyScrollingListFindsThatList()
    {
        // The route operating-system drags take — swatches, cels, symbols,
        // project rows: no captured pointer, just "the drag is here".
        var (window, vm) = Open();
        vm.Workspace.Activate(Lightbox.App.Docking.DockPanelId.Xsheet);
        Pump();
        var sheet = Scrolling(window.FindControl<ItemsControl>("XsheetLayerList")!);
        var edge = sheet.TranslatePoint(new Point(sheet.Bounds.Width / 2, sheet.Bounds.Height - 5), window)!.Value;

        window.EdgeScroll.TrackUnder(window, edge);

        Assert.True(window.EdgeScroll.IsScrolling);
        Assert.Same(sheet, window.EdgeScroll.Target);
        var before = sheet.Offset.Y;
        Assert.True(window.EdgeScroll.Tick());
        Assert.True(sheet.Offset.Y > before);

        window.EdgeScroll.Stop();
        Assert.False(window.EdgeScroll.IsScrolling);
        Assert.False(window.EdgeScroll.Tick());
    }

    [AvaloniaFact]
    public void ADragOverSomethingThatDoesNotScrollMovesNothing()
    {
        var (window, _) = Open();
        var canvas = window.FindControl<Control>("Canvas")!;

        window.EdgeScroll.TrackUnder(window, CentreOf(canvas, window));

        Assert.False(window.EdgeScroll.IsScrolling);
        Assert.Null(window.EdgeScroll.Target);
    }
}
