using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Lightbox.App.Views;

/// <summary>
/// The brand panel shown while the main window is built: the mark, the
/// wordmark and the payoff on the brand ground — with an optional
/// background image (ship <c>Assets/splash-background.png</c> and it
/// appears under a scrim; ship nothing and the ground is plain).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two things the replacement must not get wrong.</b> First, this cannot
/// animate. Building <see cref="MainWindow"/> blocks the UI thread — window
/// construction is UI-thread-only and cannot be moved off it — so anything
/// moving would visibly stutter for the length of the load. A still panel is
/// invisible to that, because the compositor keeps presenting the last frame.
/// </para>
/// <para>
/// Second, this covers less than it looks like it does. The process start, the
/// runtime load, <c>UsePlatformDetect</c> bringing up Win32 and Skia, and
/// <c>App.Initialize()</c> parsing the themes all happen before any window can
/// exist — a splash is not reachable until Avalonia is already up. So this
/// shortens the part of a cold start that *looks* broken; it does not shorten
/// the cold start. If the bundle ever moves to a single-file publish, the
/// native extraction happens before a line of managed code runs and this
/// cannot cover that either.
/// </para>
/// </remarks>
public partial class SplashWindow : Window
{
    private static readonly Uri BackgroundAsset =
        new("avares://Lightbox.App/Assets/splash-background.png");

    public SplashWindow()
    {
        InitializeComponent();
        // The image slot fills only when the art exists. Everything else
        // about the splash is identical either way, so shipping the art is
        // a file drop rather than a code change.
        if (AssetLoader.Exists(BackgroundAsset))
        {
            var image = this.FindControl<Image>("BackgroundImage")!;
            image.Source = new Bitmap(AssetLoader.Open(BackgroundAsset));
            image.IsVisible = true;
            this.FindControl<Border>("Scrim")!.IsVisible = true;
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
