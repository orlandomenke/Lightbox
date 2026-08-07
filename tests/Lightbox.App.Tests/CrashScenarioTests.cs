using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Lightbox.App.Services;
using Lightbox.App.Views;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The deliberate failures exist, are absent unless asked for, and each one
/// travels a different path.
/// </summary>
/// <remarks>
/// <para>
/// None of these can prove a crash report appears — that is Windows, and the
/// suite is Linux. What they close is everything up to it: the menu matches the
/// list, the gating holds, and the reporter listens on all three channels a
/// failure can arrive by.
/// </para>
/// <para>
/// Two of the scenarios are safe to actually run here, and are. The rest end the
/// process by design, which is the one thing a test cannot do to itself.
/// </para>
/// </remarks>
public class CrashScenarioTests : IDisposable
{
    private readonly string? _previous = DiagnosticLog.DirectoryOverride;
    private readonly string _scratch =
        Path.Combine(Path.GetTempPath(), $"lightbox-scen-{Guid.NewGuid():N}");

    public void Dispose()
    {
        DiagnosticLog.DirectoryOverride = _previous;
        DiagnosticLog.ResetForTests();
        try { Directory.Delete(_scratch, recursive: true); } catch { /* scratch */ }
    }

    [Fact]
    public void EveryScenarioSaysWhatItProvesAndWhatItCosts()
    {
        Assert.NotEmpty(CrashScenarios.All);
        foreach (var s in CrashScenarios.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Key));
            Assert.False(string.IsNullOrWhiteSpace(s.Label));
            // The tooltip is the only place the tester learns why a scenario is
            // in the list rather than what it is called.
            Assert.False(string.IsNullOrWhiteSpace(s.Proves));
        }

        Assert.Distinct(CrashScenarios.All.Select(s => s.Key));
        Assert.Distinct(CrashScenarios.All.Select(s => s.Label));
    }

    [Fact]
    public void TheDestructiveOnesAskFirstAndTheHarmlessOnesDoNot()
    {
        // The rule the owner asked for: different scenarios, different warnings.
        // Anything that ends the process has to ask; anything that does not must
        // not, or the tester learns to click through the prompt.
        Assert.Equal(CrashWarning.None, CrashScenarios.ByKey("survivable")!.Warning);
        Assert.Equal(CrashWarning.None, CrashScenarios.ByKey("console")!.Warning);
        Assert.Equal(CrashWarning.None, CrashScenarios.ByKey("unobserved-task")!.Warning);

        Assert.Equal(CrashWarning.WhenUnsaved, CrashScenarios.ByKey("ui-thread")!.Warning);
        Assert.Equal(CrashWarning.WhenUnsaved, CrashScenarios.ByKey("background-thread")!.Warning);
        Assert.Equal(CrashWarning.WhenUnsaved, CrashScenarios.ByKey("inner-exception")!.Warning);

        // The one that takes the process without running a single handler.
        Assert.Equal(CrashWarning.Always, CrashScenarios.ByKey("hard-kill")!.Warning);
    }

    /// <remarks>
    /// <c>[AvaloniaFact]</c> and not <c>[Fact]</c>, and the reason is worth more
    /// than the test. This scenario goes through <c>CanvasControl.LogDiag</c> —
    /// the real path — and touching <c>CanvasControl</c> runs its static
    /// initializer, which needs an Avalonia platform. Run as a plain fact it
    /// throws <c>TypeInitializationException</c>, and .NET <b>caches a failed
    /// type initializer for the life of the process</b>: every later test that
    /// so much as mentions <c>CanvasControl</c> then fails with the same stale
    /// exception, including ones that would pass on their own. That is how one
    /// misplaced attribute took three tests down here.
    /// </remarks>
    [AvaloniaFact]
    public void TheSurvivableFailureIsSurvivableAndLandsInTheLog()
    {
        // Actually run, because this one is safe to: it is the scenario whose
        // whole claim is that the app carries on.
        DiagnosticLog.DirectoryOverride = _scratch;
        DiagnosticLog.ResetForTests();

        CrashScenarios.ByKey("survivable")!.Run();

        var log = Path.Combine(_scratch, "diagnostics.log");
        Assert.True(File.Exists(log), "the survivable failure wrote nothing");
        Assert.Contains("test-survivable", File.ReadAllText(log));
    }

    [Fact]
    public void WritingToTheConsoleDoesNotFailWhenThereIsNoConsole()
    {
        // On a headless runner there is nowhere for this to go, and it still must
        // not throw — otherwise the scenario meant to prove the console works
        // becomes a crash of its own on every machine without one.
        var record = new StringWriter();
        var outWas = Console.Out;
        var errWas = Console.Error;
        try
        {
            Console.SetOut(record);
            Console.SetError(record);
            CrashScenarios.ByKey("console")!.Run();
        }
        finally
        {
            Console.SetOut(outWas);
            Console.SetError(errWas);
        }

        Assert.Contains("lightbox", record.ToString());
    }

    [Fact]
    public void TheReporterListensOnAllThreeChannelsAFailureCanArriveBy()
    {
        // The gap this branch closes, and the reason it is a fix rather than
        // scaffolding. A UI-thread exception runs under Avalonia's dispatcher,
        // which has its own unhandled-exception machinery — so registering only
        // AppDomain and TaskScheduler left the commonest crash in the
        // application depending on what Avalonia's internal handler happens to
        // do with it.
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Lightbox.App", "Services", "CrashReporter.cs"));

        Assert.Contains("AppDomain.CurrentDomain.UnhandledException", source);
        Assert.Contains("TaskScheduler.UnobservedTaskException", source);
        Assert.Contains("Dispatcher.UIThread.UnhandledException", source);
    }

    [Fact]
    public void TheDispatcherHandlerRecordsWithoutChangingWhatTheAppDoes()
    {
        // Marking the exception handled would make Lightbox survive crashes it
        // currently dies from — a change to behaviour smuggled in under a change
        // to diagnostics. The report is added; the outcome is left alone.
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Lightbox.App", "Services", "CrashReporter.cs"));

        Assert.DoesNotContain("e.Handled = true", source);
        Assert.DoesNotContain("Handled = true", source);
    }

    [AvaloniaFact]
    public void TheTriggersAreAbsentUntilDiagnosticsAreTurnedOn()
    {
        // The whole reason shipping deliberate crash buttons is acceptable.
        var window = new MainWindow();
        var help = HelpMenu(window);

        var triggers = help.Items.OfType<MenuItem>()
            .FirstOrDefault(m => (m.Header as string)?.Contains("test failure") == true);
        Assert.NotNull(triggers);
        Assert.False(triggers!.IsVisible, "the crash triggers are visible by default");
    }

    [AvaloniaFact]
    public void EveryMenuEntryNamesAScenarioThatExists()
    {
        // The menu is spelled out rather than generated, so this is what stops
        // the two drifting — a Tag with no scenario behind it would do nothing
        // at all when clicked, silently.
        var window = new MainWindow();
        var triggers = HelpMenu(window).Items.OfType<MenuItem>()
            .First(m => (m.Header as string)?.Contains("test failure") == true);

        var tags = triggers.Items.OfType<MenuItem>()
            .Select(m => m.Tag as string ?? "")
            .ToList();

        Assert.NotEmpty(tags);
        foreach (var tag in tags)
        {
            Assert.True(CrashScenarios.ByKey(tag) is not null, $"menu names an unknown scenario: {tag}");
        }

        // And the other direction: nothing in the list is unreachable.
        foreach (var scenario in CrashScenarios.All)
        {
            Assert.Contains(scenario.Key, tags);
        }
    }

    private static MenuItem HelpMenu(MainWindow window)
    {
        var menu = window.GetLogicalDescendants().OfType<Menu>().FirstOrDefault();
        Assert.NotNull(menu);
        var help = menu!.Items.OfType<MenuItem>()
            .FirstOrDefault(m => m.Header as string == "_Help");
        Assert.NotNull(help);
        return help!;
    }

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
}
