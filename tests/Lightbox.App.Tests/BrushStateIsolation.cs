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
/// <see cref="Lightbox.App.Rendering.FrameBitmapCache.ByteBudget"/> is a
/// process-wide preference, so a class that lowers it to force eviction lowers
/// it for every class running beside it.
/// </summary>
/// <remarks>
/// The arithmetic, so the reason survives: the eviction-policy tests set the
/// budget to 320x180x4x3 = 691,200 bytes, and the frame-floor test asserts six
/// 200x200 frames stay cached — 960,000 bytes. Run in parallel, the small
/// budget trims the floor test's cache below its floor and it fails with
/// "only N frames cached", intermittently and only under full-solution load.
/// A try/finally restore does not help: the window is while the small budget
/// is set, not after. Any test that reads or writes the budget takes this
/// collection.
/// </remarks>
[CollectionDefinition("FrameCacheBudget", DisableParallelization = true)]
public class FrameCacheBudgetCollection;

/// <summary>
/// Tests that redirect <c>ExportPresetStore.PathOverride</c>.
/// </summary>
/// <remarks>
/// The third instance of the same hazard, after the frame-cache budget and the AI
/// settings path: a process-wide static that xUnit's class-level parallelism lets two
/// classes fight over. A try/finally restore does not help, because the damage happens
/// <em>while</em> the other value is set.
/// </remarks>
[CollectionDefinition("ExportPresetFile", DisableParallelization = true)]
public class ExportPresetFileCollection;

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
    private readonly string? _previousAi = Lightbox.Ai.AiSettings.PathOverride;

    protected BrushStateIsolated()
    {
        // The AI provider too: a test that opens Configure ▸ AI writes the
        // chosen provider, and without this it would write the developer's own.
        Lightbox.Ai.AiSettings.PathOverride = Path.Combine(
            Path.GetTempPath(), $"lightbox-ai-{Guid.NewGuid():N}.json");
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

    /// <summary>Virtual so a fixture with its own temp files can chain onto it.</summary>
    public virtual void Dispose()
    {
        var mine = MainViewModel.BrushStorePath;
        var mySettings = Lightbox.App.Services.AppSettings.Path;
        var myWorkspaces = Lightbox.App.Docking.WorkspaceStore.Path;
        var myAi = Lightbox.Ai.AiSettings.PathOverride;
        MainViewModel.BrushStorePath = _previousBrushes;
        Lightbox.App.Services.AppSettings.Path = _previousSettings;
        Lightbox.App.Docking.WorkspaceStore.Path = _previousWorkspaces;
        Lightbox.Ai.AiSettings.PathOverride = _previousAi;
        if (mine is not null && File.Exists(mine)) File.Delete(mine);
        if (File.Exists(mySettings)) File.Delete(mySettings);
        if (File.Exists(myWorkspaces)) File.Delete(myWorkspaces);
        if (myAi is not null && File.Exists(myAi)) File.Delete(myAi);
        GC.SuppressFinalize(this);
    }
}
