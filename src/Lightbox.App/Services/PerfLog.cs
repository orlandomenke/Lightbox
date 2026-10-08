using System.Diagnostics;
using System.Globalization;
using System.Text;
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
        if (string.IsNullOrWhiteSpace(path)) return;
        // Only a log file is appended to: a swapped argument that pointed this
        // at a document would leave JSON lines on the end of a drawing that
        // then will not open (the sensitivity review).
        if (!IsLogPath(path))
        {
            Console.Error.WriteLine($"LIGHTBOX_PERF_LOG ignored: not a .jsonl or .log file: {path}");
            return;
        }
        if (!Open(path)) return;
        // Captured once, here, on the main thread before the app runs — the
        // heartbeat posts through this instance from its own thread and never
        // touches the ambient static there (B93: CurrentDispatcher off the UI
        // thread constructs a second dispatcher; UIThread read here does not).
        _ui = Dispatcher.UIThread;
        Console.Error.WriteLine($"Lightbox performance log on: {path}");
        var build = typeof(PerfLog).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
        Mark("start", build);
        // The lab reads the same counter (QueryPerformanceCounter, which is
        // what Stopwatch is on Windows), so its send times and these lines share
        // one timeline: "input to on screen" includes the time an input waited
        // behind a busy UI thread, which the app alone cannot see.
        // ElapsedTicks is in the counter's own ticks, not TimeSpan's.
        var origin = Stopwatch.GetTimestamp() - Clock.ElapsedTicks;
        Mark("clock", string.Create(CultureInfo.InvariantCulture, $"{origin} {Stopwatch.Frequency}"));
        // Every undoable edit, by its history label, in three parts (Q209).
        Lightbox.Core.Timeline.DocumentEditor.Measure = (name, label) => Begin(name, label);
        // And every frame rendered on a cache miss, by drawing.
        Lightbox.Raster.FrameBitmapCache.Measure = (name, id) => Begin(name, id);
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

    /// <summary>Whether a path is one the log may append to.</summary>
    public static bool IsLogPath(string path) =>
        path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".log", StringComparison.OrdinalIgnoreCase);

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
        // Hand-escaped rather than through the serializer: this runs per
        // publish while the log is on, and a measuring tool should not tax
        // what it measures (leak-hunter, Q209).
        var line = new StringBuilder(96)
            .Append("{\"t\":").Append(t.ToString("0.###", CultureInfo.InvariantCulture))
            .Append(",\"ev\":");
        AppendJsonString(line, name);
        line.Append(",\"ms\":").Append(ms.ToString("0.###", CultureInfo.InvariantCulture));
        if (detail is not null)
        {
            line.Append(",\"d\":");
            AppendJsonString(line, detail);
        }
        line.Append('}');
        lock (Gate) _out?.WriteLine(line.ToString());
    }

    private static void AppendJsonString(StringBuilder line, string value)
    {
        line.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': line.Append("\\\""); break;
                case '\\': line.Append("\\\\"); break;
                case < ' ': line.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); break;
                default: line.Append(c); break;
            }
        }
        line.Append('"');
    }

    private static void Flush()
    {
        lock (Gate) _out?.Flush();
    }

    /// <summary>Set by the UI thread when it gets round to the heartbeat's question.</summary>
    /// <remarks>
    /// One, reused: the heartbeat never asks again until the last question is
    /// answered, so there is never a second callback that could set it early,
    /// and nothing is allocated per beat.
    /// </remarks>
    private static readonly ManualResetEventSlim Answered = new();

    private static readonly Action Answer = () => Answered.Set();

    private static Dispatcher? _ui;

    private static void Heartbeat()
    {
        var lastFlush = Clock.Elapsed.TotalMilliseconds;
        while (true)
        {
            var asked = Clock.Elapsed.TotalMilliseconds;
            Answered.Reset();
            try
            {
                _ui!.Post(Answer, DispatcherPriority.Send);
            }
            catch (InvalidOperationException)
            {
                return; // the dispatcher is gone: the app is shutting down
            }
            // A UI thread that does not answer for seconds is noted every 5 s
            // while it lasts — a hang shows up in the log before it ends — and
            // the stall is written once, whole, when the answer finally comes.
            while (!Answered.Wait(5000))
            {
                Write(asked, "hang", Clock.Elapsed.TotalMilliseconds - asked, "still waiting");
                Flush();
            }
            var late = Clock.Elapsed.TotalMilliseconds - asked;
            if (late > StallMs) Write(asked, "stall", late, null);

            var now = Clock.Elapsed.TotalMilliseconds;
            if (now - lastFlush > 500)
            {
                Flush();
                lastFlush = now;
            }
            // A beat every 20 ms from the start of the last, not 20 ms after it
            // finished, so the cadence holds at 50 Hz.
            var spent = Clock.Elapsed.TotalMilliseconds - asked;
            if (spent < BeatMs) Thread.Sleep(TimeSpan.FromMilliseconds(BeatMs - spent));
        }
    }
}
