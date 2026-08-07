namespace Lightbox.App.Services;

/// <summary>What an artist stands to lose before a scenario is allowed to run.</summary>
internal enum CrashWarning
{
    /// <summary>Nothing is lost — the application carries on.</summary>
    None,

    /// <summary>The process ends. Ask first if the drawing has edits worth keeping.</summary>
    WhenUnsaved,

    /// <summary>Always ask. The process ends without any of the usual courtesies.</summary>
    Always,
}

/// <summary>One deliberate failure, and what it proves.</summary>
internal sealed record CrashScenario(
    string Key,
    string Label,
    string Proves,
    CrashWarning Warning,
    Action Run);

/// <summary>
/// Failures you can cause on purpose, so the crash reporter can be seen working.
/// </summary>
/// <remarks>
/// <para>
/// Everything the reporter does is Windows behaviour, and the suite runs on Linux — so
/// until now the only way to check any of it was to break the installation by hand
/// (<c>MANUAL_TESTING.md</c> said "rename <c>libSkiaSharp.dll</c>"), which is destructive
/// and exercises exactly one failure mode out of six.
/// </para>
/// <para>
/// <b>Each of these travels the real path.</b> None writes its own report or fakes a
/// dialog; they throw, or fault, or log, and everything after that is the application's
/// own code. A scenario that shortcut any of it would be testing itself.
/// </para>
/// <para>
/// <b>They are absent unless diagnostics are on</b>, which is why shipping them is
/// acceptable: the menu binds its visibility to the same setting as the console, so an
/// artist who has never opened Help sees nothing. Unlike the console, the switch takes
/// effect immediately — the setter raises a change notification, so the submenu appears
/// and disappears as it is ticked rather than at the next start.
/// </para>
/// </remarks>
internal static class CrashScenarios
{
    public static IReadOnlyList<CrashScenario> All { get; } =
    [
        new("survivable",
            "A failure the app survives",
            "writes to diagnostics.log and names it in the status strip; nothing stops",
            CrashWarning.None,
            () => Lightbox.App.Rendering.CanvasControl.LogDiag(
                "test-survivable", Fail("A deliberate survivable failure."))),

        new("console",
            "Write a line to the console",
            "the console is open and reachable, with nothing failing at all",
            CrashWarning.None,
            () =>
            {
                // Separates "the console never opened" from "the console opened and
                // nothing reached it" — two faults with the same symptom.
                Console.Out.WriteLine($"[lightbox] console check at {DateTime.Now:O}");
                Console.Error.WriteLine($"[lightbox] and on stderr, {DiagnosticLog.Build}");
            }),

        new("unobserved-task",
            "A background task nobody waited for",
            "the unobserved-task handler; a report is written and the app keeps running",
            CrashWarning.None,
            () =>
            {
                // The fault is only raised when the task is collected, so a trigger that
                // just makes one and returns would look like it did nothing at all.
                RunAndAbandonFaultedTask();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }),

        new("ui-thread",
            "Crash on the drawing thread",
            "the dispatcher handler — the way this application is most likely to die",
            CrashWarning.WhenUnsaved,
            () => throw Fail("A deliberate crash on the UI thread.")),

        new("background-thread",
            "Crash on a background thread",
            "the AppDomain handler, which is a different path from the one above",
            CrashWarning.WhenUnsaved,
            () =>
            {
                var thread = new Thread(() => throw Fail("A deliberate crash off the UI thread."))
                {
                    IsBackground = false,
                    Name = "lightbox-test-crash",
                };
                thread.Start();
            }),

        new("inner-exception",
            "Crash with a cause underneath",
            "that the report keeps the inner exception, where the real reason usually is",
            CrashWarning.WhenUnsaved,
            () => throw new InvalidOperationException(
                "A deliberate crash with a cause underneath.",
                Fail("The cause underneath."))),

        new("hard-kill",
            "Kill the process outright",
            "that nothing is written — the honest limit — and that the autosave copy survives",
            CrashWarning.Always,
            () => Environment.FailFast("A deliberate hard kill from Lightbox's diagnostics menu.")),
    ];

    public static CrashScenario? ByKey(string key) =>
        All.FirstOrDefault(s => s.Key == key);

    /// <summary>An exception with a real stack, rather than one that was only constructed.</summary>
    private static Exception Fail(string message)
    {
        try { throw new InvalidOperationException(message); }
        catch (Exception e) { return e; }
    }

    /// <summary>
    /// Fault a task and drop every reference to it without ever observing the fault.
    /// </summary>
    /// <remarks>
    /// Its own method so the task is unreachable the moment this returns — a local in the
    /// caller can stay alive on the stack long enough for the collection below to miss it.
    /// </remarks>
    private static void RunAndAbandonFaultedTask() =>
        _ = Task.Run(() => throw Fail("A deliberate unobserved task fault."));
}
