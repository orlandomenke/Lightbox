using System.Text.RegularExpressions;

namespace Lightbox.App.Tests;

/// <summary>
/// B93. Between two headless tests, Avalonia nulls its UI-thread binding
/// (<c>Dispatcher.ResetBeforeUnitTests</c> clears <c>s_uiThread</c>) — and the
/// static <c>Dispatcher.UIThread</c> getter, finding it null, CREATES a
/// dispatcher owned by whichever thread asked first. A stray callback on a
/// thread-pool thread — a <c>FileSystemWatcher</c> event after a test deleted
/// its temp project, a render-thread frame report, an export worker's progress
/// tick — therefore steals UI-thread ownership, and the next test dies in
/// <c>EnsureIsolatedApplication</c> with "the calling thread cannot access this
/// object": twenty sightings across fourteen unrelated classes, none of them
/// the culprit.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reproduced on demand on 2026-08-31</b> — the first reproduction in the
/// bug's twenty sightings — by a probe whose background thread read the getter
/// in a tight loop while trivial <c>[AvaloniaFact]</c>s cycled the harness: four
/// of forty died with the byte-for-byte CI stack. The mechanism is Avalonia's
/// (<c>Dispatcher.ThreadStorage.cs</c>, <c>s_uiThread ?? CurrentDispatcher</c>,
/// where <c>CurrentDispatcher</c> constructs on the calling thread); what this
/// repository owns is which of its threads can touch that getter while no
/// application is alive.
/// </para>
/// <para>
/// <b>The rule these tests hold: code that runs off the UI thread posts to a
/// dispatcher captured at construction, never through the static.</b>
/// Construction happens on the UI thread while an application is alive, so the
/// capture is a plain read; and a post to a captured, torn-down dispatcher is
/// swallowed where a read of the reset static is a poisoning. Avalonia's own
/// source says the same — "control and libraries author are encouraged to use
/// CurrentDispatcher and AvaloniaObject.Dispatcher instead".
/// </para>
/// <para>
/// Source-scanning, like <c>MonolithRatchetTests</c>, because the defect cannot
/// be asserted at runtime from here: the reset window belongs to the harness
/// and is internal to Avalonia. A scan cannot prove a new call site is
/// off-thread — that is what the allowlist review is for — but it can prove the
/// six known off-thread files stay fixed, which is the regression this guards.
/// </para>
/// </remarks>
public class DispatcherAmbientAccessTests
{
    /// <summary>
    /// <c>Dispatcher.UIThread.&lt;member&gt;</c> — using the static, as opposed
    /// to capturing it. The trailing dot is what separates the two: a capture
    /// is <c>= Dispatcher.UIThread;</c> and never dereferences the getter's
    /// result at an uncontrolled time.
    /// </summary>
    private static readonly Regex AmbientUse = new(
        @"Dispatcher\.UIThread\.", RegexOptions.Compiled);

    /// <summary>
    /// The files whose <c>Dispatcher.UIThread</c> callers run off the UI thread,
    /// each converted to a captured dispatcher on 2026-08-31 — and the thread
    /// that made each one a poisoner.
    /// </summary>
    public static TheoryData<string> OffThreadFiles() => new()
    {
        // FileSystemWatcher events; fires after tests delete watched projects.
        "src/Lightbox.App/Services/ProjectWatcher.cs",
        // The render thread reports frame times and presents.
        "src/Lightbox.App/Rendering/CanvasControl.Pacing.cs",
        // The named-pipe listener thread.
        "src/Lightbox.App/Services/IpcServer.cs",
        // The checkpoint worker posts its result back.
        "src/Lightbox.App/Services/CheckpointService.cs",
        // The live post-process worker (Work) posts its finish back.
        "src/Lightbox.App/ViewModels/MainViewModel.Painting.cs",
        // The video export worker's progress callback.
        "src/Lightbox.App/Views/VideoExportWindow.axaml.cs",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Code lines only — the doc comments above the captures name the static on purpose.</summary>
    private static IEnumerable<(int Line, string Text)> CodeLines(string relative)
    {
        var path = Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"{relative} has moved — update this test's list");
        return File.ReadLines(path)
            .Select((text, i) => (Line: i + 1, Text: text))
            .Where(l => !l.Text.TrimStart().StartsWith("//"));
    }

    /// <summary>
    /// MainViewModel.Painting is on the list for one worker body while the rest
    /// of its five thousand lines is UI-thread stroke handling, so it keeps its
    /// legitimate ambient uses. This pins how many, so a new one is a review
    /// rather than a surprise.
    /// </summary>
    private const int PaintingAmbientUses = 2;

    [Theory]
    [MemberData(nameof(OffThreadFiles))]
    public void AnOffThreadFileNeverDereferencesTheAmbientDispatcher(string relative)
    {
        var allowed = relative.EndsWith("MainViewModel.Painting.cs") ? PaintingAmbientUses : 0;
        var uses = CodeLines(relative)
            .Where(l => AmbientUse.IsMatch(l.Text))
            .Select(l => $"{relative}:{l.Line}")
            .ToList();
        Assert.True(
            uses.Count <= allowed,
            $"Dispatcher.UIThread.<member> in a file with off-thread callbacks — post through a "
            + $"dispatcher captured at construction instead (B93):\n  {string.Join("\n  ", uses)}");
    }

    /// <summary>
    /// The ratchet half: the app's total count of ambient dereferences only goes
    /// down. Every remaining site runs on the UI thread today; a new one has to
    /// say which thread it runs on before it raises this number.
    /// </summary>
    [Fact]
    public void AmbientDispatcherUseDoesNotGrow()
    {
        var root = Path.Combine(RepoRoot(), "src", "Lightbox.App");
        var uses = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => File.ReadLines(f)
                .Where(l => !l.TrimStart().StartsWith("//"))
                .Where(l => AmbientUse.IsMatch(l))
                .Select(_ => Path.GetRelativePath(root, f)))
            .ToList();
        Assert.True(
            uses.Count <= 14,
            "A new Dispatcher.UIThread.<member> call site. Fine on the UI thread; a B93 "
            + "poisoning off it. Say which in review, then lower or raise this number "
            + $"deliberately. Now at {uses.Count}:\n  "
            + string.Join("\n  ", uses.GroupBy(f => f).Select(g => $"{g.Key} ×{g.Count()}")));
    }
}
