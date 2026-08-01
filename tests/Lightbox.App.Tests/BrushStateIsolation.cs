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

/// <summary>Restores the brush store around a test that mutates brush settings.</summary>
public abstract class BrushStateIsolated : IDisposable
{
    private readonly string _previous = MainViewModel.BrushStorePath ?? "";

    protected BrushStateIsolated()
    {
        MainViewModel.BrushStorePath = Path.Combine(
            Path.GetTempPath(), $"lightbox-brushes-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        var mine = MainViewModel.BrushStorePath;
        MainViewModel.BrushStorePath = _previous;
        if (mine is not null && File.Exists(mine)) File.Delete(mine);
        GC.SuppressFinalize(this);
    }
}
