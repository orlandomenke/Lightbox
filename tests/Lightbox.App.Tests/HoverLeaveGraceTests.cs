using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// The canvas ignores a departure that does not last (B126).
/// </summary>
/// <remarks>
/// <para>
/// On a pen tablet the pointer leaves and re-enters the canvas up to 39 times
/// a second without going anywhere — Windows Ink's phantom mouse trading the
/// canvas with the pen, measured across three traces as 663, 2,763 and 1,448
/// exits of which <b>not one</b> was followed by the same device re-entering.
/// Tearing the hover state down each time is what makes the brush ring strobe.
/// </para>
/// <para>
/// <b>What these do not claim.</b> They hold that a brief departure is ignored
/// and a real one is honoured. Whether that stops the flicker on the
/// reporter's screen is their trace to answer, and the menu freeze is a
/// different mechanism entirely (B255).
/// </para>
/// </remarks>
public class HoverLeaveGraceTests
{
    private static (MainWindow Window, CanvasControl Canvas) Open()
    {
        var window = new MainWindow { Width = 1000, Height = 700 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Untitled-1", 640, 360, 12, 72, "#ffffff", false));
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
    public void ADepartureThatHasNotLastedIsNotHonouredYet()
    {
        var (window, canvas) = Open();
        window.MouseMove(Centre(canvas, window));
        Pump();

        // Leave, and ask immediately — which is what the churn does, 39 times
        // a second, at a median gap of half a millisecond.
        window.MouseMove(new Point(Centre(canvas, window).X, 2));
        Pump();

        Assert.False(canvas.HoverIsStale);
        Assert.False(canvas.SettleHover());
    }

    [AvaloniaFact]
    public void ADepartureThatLastsIsHonoured()
    {
        var (window, canvas) = Open();
        window.MouseMove(Centre(canvas, window));
        Pump();
        window.MouseMove(new Point(Centre(canvas, window).X, 2));
        Pump();

        // Long enough that the artist really did move the pointer away. The
        // grace is 50 ms; this waits well past it rather than racing it.
        Thread.Sleep(120);

        Assert.True(canvas.HoverIsStale);
        Assert.True(canvas.SettleHover());
        // And it settles once, not on every subsequent ask.
        Assert.False(canvas.SettleHover());
    }

    [AvaloniaFact]
    public void ComingBackInsideTheGraceCancelsTheDeparture()
    {
        var (window, canvas) = Open();
        var centre = Centre(canvas, window);
        window.MouseMove(centre);
        Pump();

        // The measured shape: out and straight back, faster than any hand.
        window.MouseMove(new Point(centre.X, 2));
        Pump();
        window.MouseMove(centre);
        Pump();

        // Even after the grace has elapsed, nothing is stale — the return
        // cancelled the departure rather than merely postponing it.
        Thread.Sleep(120);
        Assert.False(canvas.HoverIsStale);
        Assert.False(canvas.SettleHover());
    }
}
