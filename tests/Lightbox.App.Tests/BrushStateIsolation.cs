using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// Brush settings are global: every edit is written to a shared store and
/// every new view model loads from it. A test that switches the brush to
/// watercolour therefore hands watercolour to every test that constructs a
/// view model afterwards — and with xUnit running classes in parallel, to
/// tests running at the same time too.
///
/// That is a product smell as much as a test one (see the note in
/// <c>.claude/quality/QUESTIONS.md</c>), but until brush state belongs to a
/// document rather than to the process, any test that changes a brush must
/// take this collection so it does not run beside one that assumes defaults.
/// </summary>
[CollectionDefinition("BrushState", DisableParallelization = true)]
public class BrushStateCollection;

/// <summary>
/// Restores the process-wide state a test may mutate: the brush store, and the
/// application settings.
/// </summary>
/// <remarks>
/// Settings are here for the same reason brushes are. Onion skin persists
/// across sessions by design, which means a test that turns draw-over on hands
/// draw-over to every view model constructed afterwards. Each test gets its
/// own file so "persists" can be asserted without "persists into the next
/// test" coming with it.
/// </remarks>
public abstract class BrushStateIsolated : IDisposable
{
    private readonly string _previousBrushes = MainViewModel.BrushStorePath ?? "";
    private readonly string _previousSettings = Lightbox.App.Services.AppSettings.Path;
    private readonly string _previousWorkspaces = Lightbox.App.Docking.WorkspaceStore.Path;

    protected BrushStateIsolated()
    {
        MainViewModel.BrushStorePath = Path.Combine(
            Path.GetTempPath(), $"lightbox-brushes-{Guid.NewGuid():N}.json");
        Lightbox.App.Services.AppSettings.Path = Path.Combine(
            Path.GetTempPath(), $"lightbox-settings-{Guid.NewGuid():N}.json");
        // Workspaces for the same reason, and this one bites hardest: creating
        // a project switches to that project type's workspace and saves the
        // choice, so one test that opens a project silently rearranges the
        // panels every later test starts from.
        Lightbox.App.Docking.WorkspaceStore.Path = Path.Combine(
            Path.GetTempPath(), $"lightbox-workspaces-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        var mine = MainViewModel.BrushStorePath;
        var mySettings = Lightbox.App.Services.AppSettings.Path;
        var myWorkspaces = Lightbox.App.Docking.WorkspaceStore.Path;
        MainViewModel.BrushStorePath = _previousBrushes;
        Lightbox.App.Services.AppSettings.Path = _previousSettings;
        Lightbox.App.Docking.WorkspaceStore.Path = _previousWorkspaces;
        if (mine is not null && File.Exists(mine)) File.Delete(mine);
        if (File.Exists(mySettings)) File.Delete(mySettings);
        if (File.Exists(myWorkspaces)) File.Delete(myWorkspaces);
        GC.SuppressFinalize(this);
    }
}
