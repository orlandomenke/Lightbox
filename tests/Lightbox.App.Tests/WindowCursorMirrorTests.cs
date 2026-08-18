using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// The window wears the canvas's cursor while the pointer is on the canvas
/// (B126).
/// </summary>
/// <remarks>
/// <para>
/// <b>The flicker survived the leave grace, and this is why.</b> The grace
/// stops the canvas tearing its own hover state down 39 times a second, so the
/// ring holds — but it cannot stop Avalonia recomputing pointer-over, and the
/// instant the pointer counts as outside the canvas the cursor applied is the
/// window's, which is the default arrow. That swap at the churn's rate is the
/// flicker, and it is why every trace reported <c>cursor assignments 0</c>: the
/// canvas never changed its mind, the fallback did.
/// </para>
/// <para>
/// Making the fallback agree removes the swap. These hold that it is borrowed
/// while the pointer is here and given back when it really leaves — the second
/// half mattering more than the first, because a window left wearing a hidden
/// cursor would be a worse bug than the flicker.
/// </para>
/// </remarks>
public class WindowCursorMirrorTests
{
    private static (MainWindow Window, CanvasControl Canvas) Open()
    {
        var window = new MainWindow { Width = 1000, Height = 700 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Untitled-1", 640, 360, 12, 72, "#ffffff", false));
        vm.ActiveTool = ToolId.Brush;
        Pump();
        return (window, window.GetVisualDescendants().OfType<CanvasControl>().First());
    }

    private static void Pump()
    {
        for (var i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static Point Centre(CanvasControl canvas, MainWindow window) =>
        canvas.TranslatePoint(
            new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2), window)!.Value;

    [AvaloniaFact]
    public void TheWindowWearsTheCanvasCursorWhileThePointerIsOnIt()
    {
        var (window, canvas) = Open();

        window.MouseMove(Centre(canvas, window));
        Pump();

        // Same cursor on both, so the hand-off between them is invisible —
        // which is the whole fix.
        Assert.Same(canvas.Cursor, window.Cursor);
    }

    [AvaloniaFact]
    public void TheWindowGetsItsCursorBackWhenThePointerReallyLeaves()
    {
        var (window, canvas) = Open();
        var before = window.Cursor;

        window.MouseMove(Centre(canvas, window));
        Pump();
        window.MouseMove(new Point(Centre(canvas, window).X, 2));
        Pump();

        // Past the leave grace: a real departure, not the churn.
        Thread.Sleep(120);
        Assert.True(canvas.SettleHover());

        // A window left wearing the canvas's hidden cursor would leave the
        // artist with no pointer anywhere in the chrome.
        Assert.Same(before, window.Cursor);
    }

    [AvaloniaFact]
    public void ABriefDepartureDoesNotHandTheCursorBack()
    {
        var (window, canvas) = Open();

        window.MouseMove(Centre(canvas, window));
        Pump();
        var borrowed = window.Cursor;

        // The measured churn: out and straight back inside the grace.
        window.MouseMove(new Point(Centre(canvas, window).X, 2));
        Pump();

        // Still borrowed — handing it back here is exactly the flicker.
        Assert.Same(borrowed, window.Cursor);
    }
}
