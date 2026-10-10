using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// A pen dragged inside a scrolling list drags; it does not pan the list.
/// </summary>
/// <remarks>
/// <para>
/// Every scroller comes with a pan gesture meant for a finger, and the
/// framework hands it a pen as well. In a docker that is the wrong reading of
/// nearly every pen drag: reordering a layer, sweeping a block of cels,
/// dragging a swatch. Worse than scrolling when it should not, the gesture
/// takes the pointer for itself once the pen has travelled far enough, so the
/// drag that was under way is cancelled half way down the list.
/// </para>
/// <para>
/// <b>These raise pointer events by hand, with a pen for a pointer</b>, because
/// the headless platform only has a mouse. That is good enough here and it is
/// worth saying why: the gesture is fed from the routed events themselves, so
/// an event raised on a row reaches it exactly as a real one does. The touch
/// test is the control — the same events with a finger still pan, so the
/// harness is not simply failing to reach the gesture.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class PenDoesNotPanTests(Xunit.ITestOutputHelper output) : BrushStateIsolated
{
    private static void Pump() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    private static ulong _clock = 1000;

    private static PointerPointProperties Down =>
        new(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);

    private static PointerPointProperties Held =>
        new(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other);

    private static PointerPointProperties Up =>
        new(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);

    /// <summary>Press on <paramref name="target"/> and drag by <paramref name="dy"/> in steps, without letting go.</summary>
    private static Pointer DragBy(Window window, Control target, PointerType type, double dy, int clickCount = 1)
    {
        var pointer = new Pointer(Pointer.GetNextFreeId(), type, isPrimary: true);
        var start = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        target.RaiseEvent(new PointerPressedEventArgs(
            target, pointer, window, start, _clock += 16, Down, KeyModifiers.None, clickCount));
        Pump();
        const int steps = 12;
        for (var i = 1; i <= steps; i++)
        {
            var at = new Point(start.X, start.Y + dy * i / steps);
            // To whoever holds the pointer, as the platform would send it.
            var to = (pointer.Captured as Interactive) ?? target;
            to.RaiseEvent(new PointerEventArgs(
                InputElement.PointerMovedEvent, to, pointer, window, at, _clock += 16, Held, KeyModifiers.None));
            Pump();
        }
        return pointer;
    }

    /// <summary>A short window whose one scroller holds forty rows.</summary>
    private static (Window Window, ScrollViewer Scroller, Control Row) List()
    {
        var rows = new StackPanel();
        for (var i = 0; i < 40; i++)
        {
            rows.Children.Add(new Border { Height = 30, Background = Avalonia.Media.Brushes.Gray, Margin = new Thickness(0, 0, 0, 2) });
        }
        var scroller = new ScrollViewer { Content = rows };
        var window = new Window { Width = 300, Height = 200, Content = scroller };
        window.Show();
        Pump();
        return (window, scroller, (Control)rows.Children[3]);
    }

    // ---- any scroller -----------------------------------------------------------

    [AvaloniaFact]
    public void AFingerDraggedUpAListStillPansIt()
    {
        // The control: if this does not scroll, the events are not reaching the
        // gesture at all and the pen test below proves nothing.
        var (window, scroller, row) = List();
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height * 2);

        DragBy(window, row, PointerType.Touch, -90);

        // Only the start of the pan: once the gesture has taken the pointer the
        // platform feeds it directly, which hand-raised events cannot do. That
        // it started at all is the whole of what a control has to show.
        output.WriteLine($"touch: offset {scroller.Offset.Y:0.#}");
        Assert.True(scroller.Offset.Y > 0, "a finger did not pan the list");
    }

    [AvaloniaFact]
    public void APenDraggedUpAListDoesNotPanIt()
    {
        var (window, scroller, row) = List();

        var pen = DragBy(window, row, PointerType.Pen, -90);

        output.WriteLine($"pen: offset {scroller.Offset.Y:0.#}, captured by {pen.Captured?.GetType().Name ?? "nothing"}");
        Assert.Equal(0, scroller.Offset.Y);
        // And the gesture has not taken the pointer for itself.
        Assert.Null(pen.Captured);
    }

    [AvaloniaFact]
    public void AScrollerMadeAfterStartupIsCoveredToo()
    {
        // Dockers are built late and rebuilt on a workspace switch; whatever
        // installs the rule cannot be a walk of the tree at startup.
        var (window, _, _) = List();
        var rows = new StackPanel();
        for (var i = 0; i < 40; i++) rows.Children.Add(new Border { Height = 30 });
        var late = new ScrollViewer { Content = rows };
        window.Content = late;
        Pump();

        DragBy(window, (Control)rows.Children[2], PointerType.Pen, -90);

        Assert.Equal(0, late.Offset.Y);
    }

    // ---- the X-sheet: a pen's double tap is a double click ------------------------

    [AvaloniaFact]
    public void APenDoubleTapOnACelSwitchesLayerLikeADoubleClick()
    {
        // The X-sheet's handler does not ask what kind of pointer it was. What
        // this cannot show is that a real pen's two taps are counted as one
        // double tap — that is the framework's pen device and the tablet's
        // tolerance, and it needs the tablet.
        var window = new MainWindow { Width = 1400, Height = 1600 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Sheet", 400, 300, 12, 72, "#ffffff", false));
        while (vm.Doc.Scene.Layers.Count < 4) vm.AddPaintedLayerCommand.Execute(null);
        for (var i = 1; i < 5; i++) vm.AddFrameCommand.Execute(null);
        vm.ActiveLayerIndex = 3;
        vm.Workspace.Activate(Lightbox.App.Docking.DockPanelId.Xsheet);
        Pump();
        var cell = vm.LayerRows.First(r => r.SceneIndex == 1).Cells.First(c => c.Index == 2);
        var button = window.GetVisualDescendants().OfType<Button>().First(b => ReferenceEquals(b.DataContext, cell));
        var at = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        var pen = new Pointer(Pointer.GetNextFreeId(), PointerType.Pen, isPrimary: true);

        button.RaiseEvent(new PointerPressedEventArgs(button, pen, window, at, _clock += 16, Down, KeyModifiers.None, 2));
        Pump();

        Assert.Equal(1, vm.ActiveLayerIndex);
        Assert.Equal(2, vm.CurrentFrameIndex);
    }
}
