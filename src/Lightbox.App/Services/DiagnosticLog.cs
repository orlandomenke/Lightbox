using System.Reflection;

namespace Lightbox.App.Services;

/// <summary>
/// Where Lightbox writes down what went wrong.
/// </summary>
/// <remarks>
/// <para>
/// One folder, beside the autosave copy in the app data folder, because the
/// point of this is that there is a single place to look. It replaces the
/// canvas's own breadcrumb, which wrote to <c>%TEMP%</c> — a second location
/// nobody would think of, and one the operating system is entitled to empty.
/// </para>
/// <para>
/// <b>Nothing here throws.</b> A log that can break the application is worse
/// than no log: the failure it would cause is guaranteed, and the failure it
/// records is hypothetical. Every path swallows, and callers are written on the
/// assumption that a write may simply not have happened.
/// </para>
/// </remarks>
internal static class DiagnosticLog
{
    private static readonly object Gate = new();
    private static readonly HashSet<string> AlreadyLogged = [];

    /// <summary>Test seam. Null means the real folder beside the other settings.</summary>
    public static string? DirectoryOverride { get; set; }

    public static string Directory =>
        DirectoryOverride ?? Path.Combine(
            Path.GetDirectoryName(Lightbox.Ai.ApiKeyProvider.SettingsPath)!, "logs");

    /// <summary>The file naming the crash from a previous run, if one is waiting.</summary>
    private static string PendingMarker => Path.Combine(Directory, "last-crash.txt");

    /// <summary>
    /// Which build this is, exactly.
    /// </summary>
    /// <remarks>
    /// Both halves, and both are needed. The SDK appends <c>+&lt;git sha&gt;</c>
    /// to <c>AssemblyInformationalVersion</c>, which names the exact commit —
    /// the part that makes a report answerable, and the part that was doing all
    /// the work back when every build called itself <c>1.0.0</c>. In front of it
    /// is the release version from <c>Directory.Build.props</c>, or the tag that
    /// overrode it, so a stamp now reads <c>0.1.0-alpha.42+9f3c1ab</c>: which
    /// build a person thinks they are running, and which commit they actually
    /// are.
    /// </remarks>
    public static string Build =>
        typeof(DiagnosticLog).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    // ---- render breadcrumb (B170) --------------------------------------------

    private static bool _renderStrokeLive;
    private static string _renderRoute = "none yet";

    /// <summary>
    /// What the render pipeline was doing, for the crash report.
    /// </summary>
    /// <remarks>
    /// B170 — "Lightbox sometimes dies while erasing" — has no repro, and its
    /// diagnosis names a hazard that only opens on unusual publishes: a live
    /// stroke whose scratch crosses to the render thread. The two facts that
    /// would turn the next sighting into evidence are whether a stroke was
    /// live and which route the last publish took, so the publish path leaves
    /// them here, as plain field writes (this runs per pointer event — no
    /// formatting, no allocation until a crash actually happens).
    /// </remarks>
    public static void NoteRender(bool strokeLive, string route)
    {
        _renderStrokeLive = strokeLive;
        _renderRoute = route;
    }

    /// <summary>
    /// Record a crash, and leave a marker so the next run can mention it.
    /// </summary>
    /// <returns>The file written, or null if it could not be written.</returns>
    public static string? WriteCrash(Exception ex, string context)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            // Sortable, filename-safe, and unambiguous across time zones.
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var path = Path.Combine(Directory, $"crash-{stamp}.log");

            // ToString() on an exception already walks the inner chain and
            // carries every stack, so there is nothing to flatten by hand.
            File.WriteAllText(path, string.Join(Environment.NewLine,
            [
                $"Lightbox crash report",
                $"when    {DateTime.UtcNow:O} (UTC)",
                $"build   {Build}",
                $"os      {Environment.OSVersion}",
                $"runtime {Environment.Version}",
                $"where   {context}",
                $"render  last publish took the {_renderRoute} route, "
                    + (_renderStrokeLive ? "with a stroke in flight" : "no stroke in flight"),
                "",
                ex.ToString(),
                "",
            ]));

            // Written after the log, so the marker never points at a file that
            // failed to appear.
            File.WriteAllText(PendingMarker, path);
            return path;
        }
        catch
        {
            // A crash report that crashes is not worth having.
            return null;
        }
    }

    /// <summary>
    /// Take the crash left by a previous run, if there was one. Consumes it, so
    /// the same crash is reported once rather than at every launch from now on.
    /// </summary>
    public static string? TakePendingCrash()
    {
        try
        {
            if (!File.Exists(PendingMarker)) return null;
            var path = File.ReadAllText(PendingMarker).Trim();
            File.Delete(PendingMarker);
            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Record a survivable failure, once per context for the life of the process.
    /// </summary>
    /// <remarks>
    /// The de-duplication is deliberate and predates this class: a failure in
    /// the render or input loop repeats at pointer rate, and a log that grows by
    /// a megabyte a second is a second fault rather than a record of the first.
    /// The consequence worth knowing is that this is a breadcrumb — it says
    /// something broke here, not how often.
    /// </remarks>
    public static void WriteOnce(string context, Exception ex)
    {
        try
        {
            lock (Gate)
            {
                if (!AlreadyLogged.Add(context)) return;
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(
                    Path.Combine(Directory, "diagnostics.log"),
                    $"{DateTime.Now:O} [{context}] {Build}{Environment.NewLine}{ex}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never break drawing.
        }
    }

    /// <summary>
    /// Record something that went wrong but threw nothing — a fact, not a stack.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written every time rather than once per context, unlike
    /// <see cref="WriteOnce"/>. The failures this is for are the ones a person
    /// causes one at a time — a drop that read as empty (B294) — where the
    /// second attempt with a different browser is exactly the line that would
    /// name the difference, and de-duplicating would throw it away.
    /// </para>
    /// <para>
    /// Callers pass what they observed, never what the artist was looking at:
    /// this file is written to be attached to a bug report.
    /// </para>
    /// </remarks>
    public static void WriteNote(string context, string note)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(
                    Path.Combine(Directory, "diagnostics.log"),
                    $"{DateTime.Now:O} [{context}] {Build}{Environment.NewLine}{note}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never break drawing.
        }
    }

    /// <summary>Test seam: forget which contexts have been logged.</summary>
    internal static void ResetForTests()
    {
        lock (Gate) AlreadyLogged.Clear();
    }
}
