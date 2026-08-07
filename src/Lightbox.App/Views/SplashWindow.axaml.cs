using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Lightbox.App.Views;

/// <summary>
/// A flat panel shown while the main window is built.
/// </summary>
/// <remarks>
/// <para>
/// A placeholder, and the orange says so. It exists to be replaced by a
/// designed splash, which is why the colour lives in one resource in
/// <c>App.axaml</c> rather than here.
/// </para>
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
    public SplashWindow() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
