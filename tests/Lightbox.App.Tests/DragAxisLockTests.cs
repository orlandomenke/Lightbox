using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Shift while dragging an object holds it to the axis the drag has gone
/// furthest along — the owner's ask, and the same thing Shift already means on
/// the brush, the Move tool's content drag and the gradient.
/// </summary>
/// <remarks>
/// Driven through real pointer events on the canvas, because the lock lives in
/// the canvas's move handlers: a test that called the view model's
/// <c>DragGuide</c> directly would pass with no lock wired at all.
/// </remarks>
[Collection("BrushState")]
public sealed class DragAxisLockTests : BrushStateIsolated
{
    private static readonly Pointer Mouse = new(1, PointerType.Mouse, true);

    private static (MainWindow Window, CanvasControl Canvas, MainViewModel Vm) Open()
    {
        var window = new MainWindow { Width = 1200, Height = 900 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        vm.OpenDocumentTab(
            DocumentFactory.CreateDoc(paperColor: Scene.DefaultBackgroundColor), null);
        var canvas = window.GetVisualDescendants().OfType<CanvasControl>().First();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, canvas, vm);
    }

    private static Point Root(Window window, Visual target, Point local) =>
        target.TranslatePoint(local, window) ?? local;

    private static PointerPressedEventArgs Press(Window w, Control t, Point at) =>
        new(t, Mouse, w, Root(w, t, at), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None);

    private static PointerEventArgs Move(Window w, Control t, Point at, KeyModifiers held) =>
        new(InputElement.PointerMovedEvent, t, Mouse, w, Root(w, t, at), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other),
            held);

    private static PointerReleasedEventArgs Release(Window w, Control t, Point at) =>
        new(t, Mouse, w, Root(w, t, at), 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None, MouseButton.Left);

    /// <summary>
    /// Drag from one document point to another, holding the given modifiers on
    /// every move — the way a hand actually does it.
    /// </summary>
    private static void Drag(
        Window window, CanvasControl canvas,
        (double X, double Y) from, (double X, double Y) to, KeyModifiers held)
    {
        var (sx, sy) = canvas.DocToView(from.X, from.Y);
        var (ex, ey) = canvas.DocToView(to.X, to.Y);
        var start = new Point(sx, sy);
        var end = new Point(ex, ey);
        canvas.RaiseEvent(Press(window, canvas, start));
        // Two moves, so the lock has to hold across events rather than only
        // within one — a sum of per-event clamps is exactly the drift the
        // anchor-based lock exists to rule out.
        canvas.RaiseEvent(Move(window, canvas, (start + end) / 2, held));
        canvas.RaiseEvent(Move(window, canvas, end, held));
        canvas.RaiseEvent(Release(window, canvas, end));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>A guide the Move tool can pick up, grabbed at its anchor.</summary>
    private static (MainWindow W, CanvasControl C, MainViewModel Vm, Guide Guide) WithAGrabbableGuide()
    {
        var (window, canvas, vm) = Open();
        var guide = vm.AddGuide(GuideKind.VanishingPoint, 200, 200);
        vm.ActiveTool = ToolId.Move;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, canvas, vm, guide);
    }

    [AvaloniaFact]
    public void WithoutShiftAGuideDragMovesBothAxes()
    {
        // The sanity half: prove the drag itself lands before trusting any
        // assertion about what Shift removes from it.
        var (window, canvas, _, guide) = WithAGrabbableGuide();

        Drag(window, canvas, (200, 200), (260, 230), KeyModifiers.None);

        Assert.True(Math.Abs(guide.X - 260) < 1, $"expected x 260, got {guide.X}");
        Assert.True(Math.Abs(guide.Y - 230) < 1, $"expected y 230, got {guide.Y}");
    }

    [AvaloniaFact]
    public void ShiftHoldsAGuideDragToTheHorizontal()
    {
        var (window, canvas, _, guide) = WithAGrabbableGuide();

        // Mostly rightward: the lock keeps the x travel and zeroes the y.
        Drag(window, canvas, (200, 200), (260, 230), KeyModifiers.Shift);

        Assert.True(Math.Abs(guide.X - 260) < 1, $"expected x 260, got {guide.X}");
        Assert.True(Math.Abs(guide.Y - 200) < 1, $"expected y locked at 200, got {guide.Y}");
    }

    [AvaloniaFact]
    public void ShiftHoldsAGuideDragToTheVertical()
    {
        var (window, canvas, _, guide) = WithAGrabbableGuide();

        // Mostly downward: the other axis wins, so x stays put.
        Drag(window, canvas, (200, 200), (230, 260), KeyModifiers.Shift);

        Assert.True(Math.Abs(guide.X - 200) < 1, $"expected x locked at 200, got {guide.X}");
        Assert.True(Math.Abs(guide.Y - 260) < 1, $"expected y 260, got {guide.Y}");
    }

    [AvaloniaFact]
    public void ReleasingShiftMidDragHandsTheGuideBackToThePointer()
    {
        var (window, canvas, _, guide) = WithAGrabbableGuide();
        var (sx, sy) = canvas.DocToView(200, 200);
        var (mx, my) = canvas.DocToView(260, 230);
        var (ex, ey) = canvas.DocToView(280, 250);

        canvas.RaiseEvent(Press(window, canvas, new Point(sx, sy)));
        canvas.RaiseEvent(Move(window, canvas, new Point(mx, my), KeyModifiers.Shift));
        canvas.RaiseEvent(Move(window, canvas, new Point(ex, ey), KeyModifiers.None));
        canvas.RaiseEvent(Release(window, canvas, new Point(ex, ey)));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(Math.Abs(guide.X - 280) < 1, $"expected x 280, got {guide.X}");
        Assert.True(Math.Abs(guide.Y - 250) < 1, $"expected y 250, got {guide.Y}");
    }
}
