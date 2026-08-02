using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Lightbox.App.Views;

namespace Lightbox.App;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            // The start screen is offered from here rather than from the window
            // itself, so that a window built directly — every headless test —
            // never has a modal dialog appear over it.
            window.Opened += (_, _) => _ = window.OfferStartScreenAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
