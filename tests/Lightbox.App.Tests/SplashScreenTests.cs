using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Lightbox.App.Views;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The designed splash: the mark, the wordmark and the payoff on the brand
/// ground, with the background-image slot empty until the art ships.
/// </summary>
/// <remarks>
/// <b>What these assert, precisely.</b> The colours and sources the splash
/// *would* paint, not the pixels it paints — the headless platform here
/// draws without Skia. The visual check lives in <c>MANUAL_TESTING.md</c>.
/// </remarks>
public class SplashScreenTests
{
    [AvaloniaFact]
    public void TheSplashStandsOnTheBrandGround()
    {
        var splash = new SplashWindow();
        var ground = splash.FindControl<Border>("Ground");
        Assert.NotNull(ground);

        Assert.True(
            Application.Current!.TryFindResource("BrandGroundBrush", out var resource),
            "BrandGroundBrush is not in Application.Resources");
        Assert.Same(resource, ground!.Background);
    }

    [AvaloniaFact]
    public void TheMarkAndTheWordmarkAreVectors()
    {
        // The same drawings the title bar uses — one brand, one geometry.
        var splash = new SplashWindow();
        splash.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var images = splash.GetVisualDescendants().OfType<Image>()
            .Where(i => i.Source is DrawingImage).ToList();
        Assert.Equal(2, images.Count);
    }

    [AvaloniaFact]
    public void TheBackgroundImageStaysHiddenUntilTheArtShips()
    {
        // The slot exists so shipping splash art is a file drop, not a code
        // change; without the asset it must cost nothing and show nothing.
        var splash = new SplashWindow();
        Assert.False(splash.FindControl<Image>("BackgroundImage")!.IsVisible);
        Assert.False(splash.FindControl<Border>("Scrim")!.IsVisible);
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
