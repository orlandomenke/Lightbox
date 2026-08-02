using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Headless;
using Lightbox.App.Docking;
using Lightbox.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Lightbox.App.Tests;

public class TestAppBuilder
{
    /// <summary>
    /// Redirect anything that would touch the user's own settings, before a
    /// single test runs.
    /// </summary>
    /// <remarks>
    /// A module initializer rather than the Avalonia app builder, because the
    /// builder only runs when the first <c>AvaloniaFact</c> does — and a plain
    /// <c>Fact</c> that got there first would write a workspace into the real
    /// <c>%AppData%/Lightbox/workspaces.json</c> and rearrange the panels of
    /// whoever ran the suite. Which is exactly what happened.
    /// </remarks>
    [ModuleInitializer]
    internal static void IsolateUserSettings()
    {
        WorkspaceStore.Path = Path.Combine(
            Path.GetTempPath(), $"lightbox-tests-{Guid.NewGuid():N}", "workspaces.json");
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Lightbox.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
