using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// The real window's answer to losing focus (B185). CanvasInputTests proves
/// <c>CancelPointerGestures</c> does its job when called; this file proves the
/// window actually calls it — the wiring a headless canvas rig cannot see, and
/// the one line that answers "what happens when Lightbox is unfocused with a
/// stroke in flight". Deleting the <c>Deactivated</c> subscription in
/// MainWindow reopens the bug and fails here, nowhere else.
/// </summary>
public class WindowDeactivationTests
{
    private static (MainWindow Window, MainViewModel Vm, CanvasControl Canvas) Open()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Untitled-1", 960, 540, 12, 72, "#ffffff", false));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var canvas = window.FindControl<CanvasControl>("Canvas")!;
        return (window, vm, canvas);
    }

    /// <summary>
    /// Runs the private handler Avalonia's platform impl invokes when the OS
    /// takes the focus — the only headless route to a real <c>Deactivated</c>,
    /// so the test exercises the window's actual subscription rather than
    /// calling what the subscription calls. The assert is what fails loudly
    /// if Avalonia ever renames the path.
    /// </summary>
    private static void Deactivate(Window window)
    {
        var handle = typeof(WindowBase).GetMethod(
            "HandleDeactivated", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(handle);
        handle!.Invoke(window, null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static readonly Avalonia.Input.Pointer PenPointer = new(11, PointerType.Pen, true);

    [AvaloniaFact]
    public void LosingTheWindowEndsTheStrokeInFlight()
    {
        var (window, vm, canvas) = Open();
        var at = canvas.TranslatePoint(
            new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2), window) ?? default;

        canvas.RaiseEvent(new PointerPressedEventArgs(
            canvas, PenPointer, window, at, 0,
            new PointerPointProperties(
                RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed,
                0, 0.5f, 0, 0), // twist, pressure, xTilt, yTilt
            KeyModifiers.None));

        // The pen is down and the focus goes elsewhere: the release will be
        // delivered to whichever window took it, never to us.
        Deactivate(window);

        var strokes = ((Frame)vm.PaintLayer().Cels[0].Frame!).Strokes;
        Assert.Single(strokes);

        // Moves arriving after the window came back resume nothing — this is
        // the "draws from the stored point the moment I return" half of B185.
        canvas.RaiseEvent(new PointerEventArgs(
            InputElement.PointerMovedEvent, canvas, PenPointer, window,
            new Point(at.X + 100, at.Y + 100), 0,
            new PointerPointProperties(
                RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other, 0, 0.5f, 0, 0),
            KeyModifiers.None));

        Assert.Single(strokes);
        Assert.True(strokes[0].Points.Count <= 2,
            $"the abandoned stroke kept growing: {strokes[0].Points.Count} points");
        window.Close();
    }
}
