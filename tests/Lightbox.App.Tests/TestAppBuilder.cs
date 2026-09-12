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
    /// <para>
    /// A module initializer rather than the Avalonia app builder, because the
    /// builder only runs when the first <c>AvaloniaFact</c> does — and a plain
    /// <c>Fact</c> that got there first would write a workspace into the real
    /// <c>%AppData%/Lightbox/workspaces.json</c> and rearrange the panels of
    /// whoever ran the suite. Which is exactly what happened.
    /// </para>
    /// <para>
    /// <b>The workspace saves nowhere at all, which is stronger than saving
    /// somewhere harmless (B376).</b> Pointing it at a scratch file stopped it
    /// reaching the developer and left every test in the suite sharing one — and
    /// <c>WorkspaceViewModel</c> persists the session on <em>every</em> layout
    /// change, so one test that hid a panel decided the layout every later test
    /// started from. That is order-dependent, and xUnit does not promise an
    /// order, so it surfaced as two tests failing in one run of three and
    /// passing in isolation.
    /// </para>
    /// <para>
    /// An empty path is the store's own way of saying "nowhere" — <c>Save</c>
    /// already returns early on one, and <c>WorkspaceStore.File</c>'s remark
    /// describes exactly this state. So every test now starts from the shipped
    /// defaults, and a test that wants persistence asks for it by setting a path
    /// of its own, which <c>BrushStateIsolated</c> does.
    /// </para>
    /// </remarks>
    [ModuleInitializer]
    internal static void IsolateUserSettings()
    {
        var scratch = Path.Combine(Path.GetTempPath(), $"lightbox-tests-{Guid.NewGuid():N}");
        WorkspaceStore.Path = "";
        Lightbox.App.Services.AppSettings.Path = Path.Combine(scratch, "settings.json");
        // Diagnostics too, or a test that exercises the crash path writes a
        // crash report into whoever ran the suite — and leaves a marker that
        // tells their next real launch it had crashed.
        Lightbox.App.Services.DiagnosticLog.DirectoryOverride = Path.Combine(scratch, "logs");
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Lightbox.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
