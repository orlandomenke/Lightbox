using Avalonia;
using Avalonia.Headless;
using Lightbox.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Lightbox.App.Tests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Lightbox.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
