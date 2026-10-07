using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;

namespace Lightbox.App.Services;

/// <summary>
/// What the app did and how long each thing took, one JSON line per action,
/// for the performance lab in <c>perf/</c> (Q209). Off — one static check per
/// call site — unless <c>LIGHTBOX_PERF_LOG</c> names a file.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the app measures itself.</b> An outside profiler sees threads and
/// stacks, not actions: the capture of 2026-10-07 needed a CPU trace, a
/// render report and a person's memory of what they had clicked to say that
/// an undo cost seven seconds. A span names the action at the moment it
/// happens, and the lab lines spans up with the screen (PresentMon) rather than
/// guessing.
/// </para>
/// <para>
/// <b>The heartbeat is the other half.</b> A background thread asks the UI
/// thread to answer every 20 ms; when the answer is more than 50 ms late —
/// three screen refreshes, what the eye can see — that is a stall, and it is
/// logged with its start and end. The lab attributes it to whichever spans
/// overlap it, which is the question "measure absence, not slowness" asks:
/// the moments the app stopped answering, whatever it was doing.
/// </para>
/// <para>
/// Each line: <c>{"t": ms since start, "ev": name, "ms": duration, "d": detail}</c>.
/// <c>t</c> is when the action began, so spans and stalls can be overlapped.
/// </para>
/// </remarks>
public static class PerfLog
{
    private const double StallMs = 50;
    private const int BeatMs = 20;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly object Gate = new();
    private static StreamWriter? _out;

    /// <summary>Whether anything is being recorded. Read before doing any work for a span.</summary>
    public static bool On { get; private set; }

    /// <summary>
    /// Open the log named by <c>LIGHTBOX_PERF_LOG</c>, if any, and start the
    /// heartbeat. Called once, first thing in <c>Main</c>.
    /// </summary>
    public static void Start()
    {
        var path = Environment.GetEnvironmentVariable("LIGHTBOX_PERF_LOG");
        if (string.IsNullOrWhiteSpace(path) || !Open(path)) return;
        var build = typeof(PerfLog).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
        Mark("start", build);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
        new Thread(Heartbeat) { IsBackground = true, Name = "perf heartbeat" }.Start();
    }

    private static bool Open(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            lock (Gate) _out = new StreamWriter(path, append: true, new UTF8Encoding(false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
        On = true;
        return true;
    }

    /// <summary>Record to <paramref name="path"/> with no heartbeat. Tests only.</summary>
    internal static void StartForTests(string path) => Open(path);

    /// <summary>Close the log and turn recording off. Tests only.</summary>
    internal static void StopForTests()
    {
        lock (Gate)
        {
            _out?.Dispose();
            _out = null;
        }
        On = false;
    }

    /// <summary>Time an action. Dispose to record it; free when the log is off.</summary>
    public static Span Begin(string name, string? detail = null) =>
        On ? new Span(name, detail, Clock.Elapsed.TotalMilliseconds) : default;

    /// <summary>Something happened at a moment, with no duration of its own.</summary>
    public static void Mark(string name, string? detail = null)
    {
        if (!On) return;
        Write(Clock.Elapsed.TotalMilliseconds, name, 0, detail);
    }

    /// <summary>A timed action. A default instance — what an off log hands out — records nothing.</summary>
    public readonly struct Span : IDisposable
    {
        private readonly string? _name;
        private readonly string? _detail;
        private readonly double _start;

        internal Span(string name, string? detail, double start)
        {
            _name = name;
            _detail = detail;
            _start = start;
        }

        public void Dispose()
        {
            if (_name is null) return;
            Write(_start, _name, Clock.Elapsed.TotalMilliseconds - _start, _detail);
        }
    }

    private static void Write(double t, string name, double ms, string? detail)
    {
        var line = new StringBuilder(96)
            .Append("{\"t\":").Append(t.ToString("0.###", CultureInfo.InvariantCulture))
            .Append(",\"ev\":").Append(JsonSerializer.Serialize(name))
            .Append(",\"ms\":").Append(ms.ToString("0.###", CultureInfo.InvariantCulture));
        if (detail is not null) line.Append(",\"d\":").Append(JsonSerializer.Serialize(detail));
        line.Append('}');
        lock (Gate) _out?.WriteLine(line.ToString());
    }

    private static void Flush()
    {
        lock (Gate) _out?.Flush();
    }

    private static void Heartbeat()
    {
        var lastFlush = Clock.Elapsed.TotalMilliseconds;
        while (true)
        {
            var asked = Clock.Elapsed.TotalMilliseconds;
            // Not disposed: after a 5 s timeout the posted callback still holds
            // it, and setting a disposed handle on the UI thread would throw
            // there — a measuring tool must never be what crashes the app.
            var answered = new ManualResetEventSlim();
            try
            {
                Dispatcher.UIThread.Post(answered.Set, DispatcherPriority.Send);
            }
            catch (InvalidOperationException)
            {
                return; // the dispatcher is gone: the app is shutting down
            }
            // Waited on here rather than measured inside the post, so a UI
            // thread that never answers at all is still logged — as one long
            // stall, every 5 s, rather than as silence.
            if (!answered.Wait(5000))
            {
                Write(asked, "stall", Clock.Elapsed.TotalMilliseconds - asked, "no answer");
                continue;
            }
            var late = Clock.Elapsed.TotalMilliseconds - asked;
            if (late > StallMs) Write(asked, "stall", late, null);

            var now = Clock.Elapsed.TotalMilliseconds;
            if (now - lastFlush > 500)
            {
                Flush();
                lastFlush = now;
            }
            Thread.Sleep(BeatMs);
        }
    }
}
