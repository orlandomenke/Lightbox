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
    /// The SDK already stamps <c>AssemblyInformationalVersion</c> as
    /// <c>1.0.0+&lt;git sha&gt;</c>, so a log can name the commit it came from
    /// without any version machinery being invented for it. That matters more
    /// than the marketing version: "1.0.0" identifies nothing when every build
    /// says it.
    /// </remarks>
    public static string Build =>
        typeof(DiagnosticLog).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

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

    /// <summary>Test seam: forget which contexts have been logged.</summary>
    internal static void ResetForTests()
    {
        lock (Gate) AlreadyLogged.Clear();
    }
}
