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
            // Eight fixed shapes with no input, ~160 ms to bake. Started here so
            // the first artist to open the tip library is not the one who waits
            // for them.
            Lightbox.Raster.Tips.TipCatalogue.Warm();

            var window = new MainWindow();
            desktop.MainWindow = window;
            // The start screen is offered from here rather than from the window
            // itself, so that a window built directly — every headless test —
            // never has a modal dialog appear over it.
            window.Opened += (_, _) => _ = window.OfferStartScreenAsync();

            // If the last run ended badly, say so once. The dialog at crash time
            // is the first attempt and the better one; this is the fallback for
            // when the UI was too far gone to show it, and it consumes the
            // marker so the same crash is not reported every launch from now on.
            if (Lightbox.App.Services.DiagnosticLog.TakePendingCrash() is { } crash)
            {
                window.Opened += (_, _) => window.NotePreviousCrash(crash);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
