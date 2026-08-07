using Lightbox.App.Services;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// A failure leaves a file behind, in one known place, and never takes the
/// application down with it.
/// </summary>
/// <remarks>
/// Plain <see cref="FactAttribute"/> — this is file I/O with an injectable
/// directory and touches no Avalonia. Each test points the log at scratch of its
/// own rather than sharing the suite-wide override, so they can run in any order
/// and still assert on an empty folder.
/// </remarks>
public class DiagnosticLogTests(ITestOutputHelper output) : IDisposable
{
    private readonly string? _previous = DiagnosticLog.DirectoryOverride;
    private readonly string _scratch =
        Path.Combine(Path.GetTempPath(), $"lightbox-diag-{Guid.NewGuid():N}");

    private void Redirect()
    {
        DiagnosticLog.DirectoryOverride = _scratch;
        DiagnosticLog.ResetForTests();
    }

    public void Dispose()
    {
        DiagnosticLog.DirectoryOverride = _previous;
        DiagnosticLog.ResetForTests();
        try { Directory.Delete(_scratch, recursive: true); } catch { /* scratch */ }
    }

    private static Exception Thrown(string message, Exception? inner = null)
    {
        // Thrown rather than constructed, so there is a real stack to assert on.
        try { throw new InvalidOperationException(message, inner); }
        catch (Exception e) { return e; }
    }

    [Fact]
    public void ACrashIsWrittenDownWithTheBuildItCameFrom()
    {
        Redirect();
        var path = DiagnosticLog.WriteCrash(Thrown("the canvas exploded"), "unhandled");

        Assert.NotNull(path);
        var text = File.ReadAllText(path!);
        output.WriteLine(text);

        Assert.Contains("the canvas exploded", text);
        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("unhandled", text);
        // The build stamp is what makes a report actionable — "1.0.0" identifies
        // nothing when every build says it, so the sha has to be in there.
        Assert.Contains(DiagnosticLog.Build, text);
        // And a real stack, not just the message. The frame is Thrown() rather
        // than this test: a stack spans the throw site to the catch, so the
        // caller that asked for the exception is not in it.
        Assert.Contains("DiagnosticLogTests.Thrown", text);
    }

    [Fact]
    public void TheCauseUnderneathIsNotLost()
    {
        // The interesting half of a crash is usually the inner exception, and
        // losing it turns a diagnosis into a guess.
        Redirect();
        var path = DiagnosticLog.WriteCrash(
            Thrown("outer", Thrown("the real reason")), "unhandled");

        var text = File.ReadAllText(path!);
        Assert.Contains("outer", text);
        Assert.Contains("the real reason", text);
    }

    [Fact]
    public void TheNextRunIsToldOnceAndThenNotAgain()
    {
        Redirect();
        var path = DiagnosticLog.WriteCrash(Thrown("boom"), "unhandled");

        Assert.Equal(path, DiagnosticLog.TakePendingCrash());
        // Consumed. Reporting the same crash at every launch from now on would
        // be worse than not reporting it — it stops meaning anything.
        Assert.Null(DiagnosticLog.TakePendingCrash());
    }

    [Fact]
    public void ACleanRunHasNothingToReport()
    {
        Redirect();
        Assert.Null(DiagnosticLog.TakePendingCrash());
    }

    [Fact]
    public void ASurvivableFailureIsRecordedOncePerContext()
    {
        // A render or input failure repeats at pointer rate. A log growing by a
        // megabyte a second is a second fault, not a record of the first.
        Redirect();
        for (var i = 0; i < 50; i++) DiagnosticLog.WriteOnce("render", Thrown("bad frame"));
        DiagnosticLog.WriteOnce("input", Thrown("bad press"));

        var text = File.ReadAllText(Path.Combine(_scratch, "diagnostics.log"));
        Assert.Equal(1, Occurrences(text, "[render]"));
        Assert.Equal(1, Occurrences(text, "[input]"));
    }

    [Fact]
    public void AnUnwritableFolderIsSurvivedRatherThanThrown()
    {
        // The whole promise of this class. A log that can break the application
        // is worse than no log: the failure it causes is certain, the one it
        // records is hypothetical.
        DiagnosticLog.DirectoryOverride = Path.Combine(_scratch, "\0 impossible");
        DiagnosticLog.ResetForTests();

        Assert.Null(DiagnosticLog.WriteCrash(Thrown("boom"), "unhandled"));
        Assert.Null(DiagnosticLog.TakePendingCrash());
        DiagnosticLog.WriteOnce("render", Thrown("bad frame"));
    }

    [Fact]
    public void TheLogSitsBesideTheOtherAppData()
    {
        // Not %TEMP%, which the operating system may empty and which nobody
        // would think to look in. Beside the autosave copy, which is where the
        // manual already sends people.
        DiagnosticLog.DirectoryOverride = null;
        var settings = Path.GetDirectoryName(Lightbox.Ai.ApiKeyProvider.SettingsPath)!;
        Assert.Equal(Path.Combine(settings, "logs"), DiagnosticLog.Directory);
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }
}
