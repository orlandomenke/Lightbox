using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Lightbox.App.Views;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The splash is a flat placeholder, and looks like a splash rather than a window.
/// </summary>
/// <remarks>
/// <b>What these assert, precisely.</b> The colour the splash *would* paint,
/// not the pixels it paints. The headless platform here draws without Skia, so
/// there is no frame to capture, and changing that to prove one rectangle is
/// orange would alter the platform for the whole suite — a bad trade for a
/// placeholder. The visual check lives in <c>MANUAL_TESTING.md</c>.
/// </remarks>
public class SplashScreenTests
{
    [AvaloniaFact]
    public void TheSplashIsOrange()
    {
        var splash = new SplashWindow();
        var backdrop = splash.FindControl<Border>("Backdrop");
        Assert.NotNull(backdrop);
        var brush = Assert.IsType<SolidColorBrush>(backdrop!.Background);
        Assert.Equal(Color.Parse("#FF7A00"), brush.Color);
    }

    [AvaloniaFact]
    public void TheSplashTakesItsColourFromOnePlace()
    {
        // The one that earns its keep: it makes a second hardcoded orange
        // impossible to add quietly, which is the whole reason the brush is a
        // resource rather than a literal on the border.
        var splash = new SplashWindow();
        var backdrop = splash.FindControl<Border>("Backdrop");
        Assert.NotNull(backdrop);

        Assert.True(
            Application.Current!.TryFindResource("SplashBackgroundBrush", out var resource),
            "SplashBackgroundBrush is not in Application.Resources");
        Assert.Same(resource, backdrop!.Background);
    }

    [AvaloniaFact]
    public void TheSplashLooksLikeASplashRatherThanAWindow()
    {
        var splash = new SplashWindow();

        Assert.Equal(WindowDecorations.None, splash.WindowDecorations);
        Assert.False(splash.ShowInTaskbar, "a splash in the taskbar reads as the app opening twice");
        Assert.False(splash.CanResize);

        // Not merely the default. A topmost splash would sit over the start
        // screen, which is a modal centred on the main window — an orange panel
        // over a dialog nobody can dismiss, which looks like a hang.
        Assert.False(splash.Topmost, "a topmost splash covers the start screen");
    }
}
