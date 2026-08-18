using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Input;

namespace Lightbox.App.Services;

/// <summary>
/// What the pointer actually delivered, event by event, for the pen problems
/// nothing in this repository can reproduce (B126, B254).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> B126 — the OS pointer flickering over the brush ring
/// while a Huion pen hovers — and B254 — hover popups collapsing the moment
/// they open on the same machine — both live between the tablet driver and the
/// app, on hardware this repository has not got. Q115 chose to instrument
/// before fixing: every candidate fix needs the reporter's machine to verify
/// anyway, so the one required round-trip is spent identifying the mechanism.
/// This is the instrument — armed from Help, fed by the canvas's pointer
/// handlers and the popup layer, written where the crash reports go.
/// </para>
/// <para>
/// <b>The report discriminates the three candidate mechanisms</b> rather than
/// merely logging: two pointer ids interleaving is a driver-emulated mouse
/// beside the pen; enter/exit churn on a pen that never left is proximity
/// flapping; and a trace where Lightbox held one cursor throughout while the
/// arrow still flickered on screen puts the fault below the app, where the fix
/// is an upstream report rather than a patch here. Each verdict line names its
/// evidence, and the wording is pinned by <c>InputTraceTests</c> — the tests
/// prove the instrument, not the fix, the same split the render report's
/// <c>StrokeToScreenTests</c> made.
/// </para>
/// <para>
/// <b>It also turns a fix into numbers.</b> Cursor flips, enter/exit churn,
/// stream alternations and sub-500 ms popup lives are all rates per traced
/// minute, so "did the fix work" is the same one-minute hover ritual before
/// and after, with the two reports' counters side by side in the bug entry.
/// </para>
/// <para>
/// <b>Costs nothing until armed.</b> Every hook is a single volatile read when
/// the trace is off — this sits on per-pointer-event paths (invariant 6), so
/// the disarmed branch is the whole of the disarmed cost. Armed, an event is
/// one struct into a preallocated ring under a lock nothing else contends.
/// <b>Nothing here throws</b>, for <see cref="DiagnosticLog"/>'s reason: a
/// diagnostic that can break drawing is worse than none.
/// </para>
/// </remarks>
internal static class InputTrace
{
    internal enum Kind : byte
    {
        Move,
        Enter,
        Exit,
        Press,
        Release,
        CaptureLost,
        /// <summary>The canvas decided what the pointer should be (deduplicated: only changes are recorded).</summary>
        CursorDecided,
        /// <summary>The platform cursor the canvas assigns actually changed identity.</summary>
        CursorAssigned,
        PopupOpened,
        PopupClosed,

        /// <summary>The UI thread stopped answering for longer than <see cref="StallMs"/>.</summary>
        Stall,

        /// <summary>Named in the event list, counted by nothing.</summary>
        Note,
    }

    /// <summary>One traced event. Detail carries the non-positional kinds' payload.</summary>
    internal readonly record struct Entry(
        double Seconds,
        Kind Kind,
        PointerType Device,
        int DeviceId,
        float X,
        float Y,
        float Pressure,
        float TiltX,
        float TiltY,
        KeyModifiers Modifiers,
        string? Detail);

    /// <summary>
    /// A minute of a 240 Hz pen plus a mouse stream and the popup chatter fits
    /// with room; wrapping is survivable and the report says when it happened.
    /// </summary>
    private const int Capacity = 65536;

    private static readonly Entry[] Ring = new Entry[Capacity];
    private static readonly object Gate = new();
    private static readonly List<double> PopupLives = [];
    private static readonly Dictionary<object, double> OpenPopups = new(ReferenceEqualityComparer.Instance);

    private static long _count;
    private static long _armedAtTicks;
    private static bool _armed;
    private static string? _lastDecided;
    private static string? _lastAssigned;

    public static bool Armed => Volatile.Read(ref _armed);

    /// <summary>Start a fresh trace, discarding any previous one.</summary>
    /// <remarks>
    /// Wires the popup layer here rather than at startup: it is idempotent, it
    /// costs nothing until somebody actually chases a pen problem, and it
    /// removes the one call site a new window or a headless run could forget.
    /// </remarks>
    public static void Arm()
    {
        WirePopups();
        lock (Gate)
        {
            _count = 0;
            PopupLives.Clear();
            OpenPopups.Clear();
            _lastDecided = null;
            _lastAssigned = null;
            _armedAtTicks = Stopwatch.GetTimestamp();
            _gcAtArm = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
                GC.GetTotalPauseDuration());
            Volatile.Write(ref _armed, true);
        }
        StartHeartbeat();
    }

    public static void Disarm()
    {
        Volatile.Write(ref _armed, false);
        StopHeartbeat();
    }

    /// <summary>
    /// Collections and total pause time when the trace was armed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Added because the residual UI-thread stalls survived every fix aimed
    /// at what was drawing them.</b> The sixth trace showed 43 stalls totalling
    /// 30% of the session with <em>zero</em> popups open, so the cause is not
    /// the popup churn that produced the original 18.9-second freezes.
    /// </para>
    /// <para>
    /// <b>Garbage collection is the hypothesis this measures, and the shape of
    /// the code invites it.</b> <c>CanvasControl.DrawOp</c> is an
    /// <c>ICustomDrawOperation</c> whose primary constructor captures about
    /// thirty-five arguments as fields — a <c>RenderSnapshot</c> owning a
    /// full-canvas image, a texture cache, a presented frame, several paths and
    /// a dozen lists — and <c>Dispose</c> releases three of them. One is
    /// allocated per repaint, and the enter/exit churn was driving ~97 repaints
    /// a second.
    /// </para>
    /// <para>
    /// <b><see cref="GC.GetTotalPauseDuration"/> settles it rather than
    /// suggesting it.</b> If the pause time the runtime reports is close to the
    /// stall time the heartbeat measured, the thread was blocked collecting and
    /// the fix is allocation; if it is a small fraction, garbage collection is
    /// exonerated and the next hypothesis gets its turn. Either way the answer
    /// arrives as a number rather than an argument.
    /// </para>
    /// </remarks>
    private static (int Gen0, int Gen1, int Gen2, TimeSpan Pause) _gcAtArm;

    private static double Now() =>
        (Stopwatch.GetTimestamp() - _armedAtTicks) / (double)Stopwatch.Frequency;

    /// <summary>A pointer event on the canvas, with everything the device said.</summary>
    public static void Pointer(Kind kind, PointerEventArgs e, Visual relativeTo)
    {
        if (!Volatile.Read(ref _armed)) return;
        try
        {
            var pp = e.GetCurrentPoint(relativeTo);
            Note(new Entry(
                Now(), kind, e.Pointer.Type, e.Pointer.Id,
                (float)pp.Position.X, (float)pp.Position.Y,
                pp.Properties.Pressure, pp.Properties.XTilt, pp.Properties.YTilt,
                e.KeyModifiers, null));
        }
        catch
        {
            // Tracing must never break input.
        }
    }

    /// <summary>Capture lost carries a pointer and no position.</summary>
    public static void CaptureLost(IPointer pointer)
    {
        if (!Volatile.Read(ref _armed)) return;
        Note(new Entry(Now(), Kind.CaptureLost, pointer.Type, pointer.Id, 0, 0, 0, 0, 0, KeyModifiers.None, null));
    }

    /// <summary>
    /// The canvas decided the pointer's look. Deduplicated here so a 240 Hz
    /// hover writes one entry per change of mind rather than per event.
    /// </summary>
    public static void CursorDecided(string kind)
    {
        if (!Volatile.Read(ref _armed)) return;
        lock (Gate)
        {
            if (kind == _lastDecided) return;
            NoteLocked(new Entry(
                Now(), Kind.CursorDecided, PointerType.Mouse, -1, 0, 0, 0, 0, 0, KeyModifiers.None,
                $"{_lastDecided ?? "start"}→{kind}"));
            _lastDecided = kind;
        }
    }

    /// <summary>The assigned platform cursor changed identity, named by its decided kind.</summary>
    public static void CursorAssigned(string kind)
    {
        if (!Volatile.Read(ref _armed)) return;
        lock (Gate)
        {
            NoteLocked(new Entry(
                Now(), Kind.CursorAssigned, PointerType.Mouse, -1, 0, 0, 0, 0, 0, KeyModifiers.None,
                $"{_lastAssigned ?? "start"}→{kind}"));
            _lastAssigned = kind;
        }
    }

    /// <summary>
    /// A popup opened or closed anywhere in the app — flyouts and tooltips
    /// alike, keyed by instance so a close can say how long it lived.
    /// </summary>
    public static void Popup(object key, string what, bool open)
    {
        if (!Volatile.Read(ref _armed)) return;
        lock (Gate)
        {
            var now = Now();
            if (open)
            {
                OpenPopups[key] = now;
                NoteLocked(new Entry(now, Kind.PopupOpened, PointerType.Mouse, -1, 0, 0, 0, 0, 0, KeyModifiers.None, what));
                return;
            }
            string detail = what;
            if (OpenPopups.Remove(key, out var openedAt))
            {
                var ms = (now - openedAt) * 1000;
                PopupLives.Add(ms);
                detail = $"{what} after {ms:F0} ms";
            }
            NoteLocked(new Entry(now, Kind.PopupClosed, PointerType.Mouse, -1, 0, 0, 0, 0, 0, KeyModifiers.None, detail));
        }
    }

    private static void Note(Entry entry)
    {
        lock (Gate) NoteLocked(entry);
    }

    /// <summary>
    /// Put something in the event list that no counter is derived from.
    /// </summary>
    /// <remarks>
    /// For facts that name what happened without being one of the things being
    /// counted — a submenu opening, which is already counted as the popup it
    /// opens. Recording it twice was how "2300 popups" came to mean 1,150.
    /// </remarks>
    private static void Annotate(string what)
    {
        if (!Volatile.Read(ref _armed)) return;
        lock (Gate)
        {
            NoteLocked(new Entry(
                Now(), Kind.Note, PointerType.Mouse, -1, 0, 0, 0, 0, 0, KeyModifiers.None, what));
        }
    }

    private static void NoteLocked(Entry entry)
    {
        Ring[_count % Capacity] = entry;
        _count++;
    }

    // ---- the stall watch --------------------------------------------------------

    /// <summary>
    /// A tick later than this means the UI thread was blocked, not idle.
    /// </summary>
    /// <remarks>
    /// Generous on purpose: the timer asks for 100 ms, and an ordinary busy
    /// frame can miss one. A quarter of a second is past anything the artist
    /// would call responsive, so what this records is a freeze rather than a
    /// hiccup.
    /// </remarks>
    internal const double StallMs = 250;

    private const double HeartbeatMs = 100;

    private static Avalonia.Threading.DispatcherTimer? _heartbeat;
    private static double _lastBeat;
    private static double _worstStall;
    private static double _blockedMs;
    private static int _stalls;

    /// <summary>
    /// Watch for the UI thread going away, because silence in the event stream
    /// has two completely different meanings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The first real trace could not answer the question it most needed
    /// to.</b> It contained 16.4 seconds in which no event of any kind reached
    /// the canvas, and the reporter said the application had frozen around
    /// then. Both readings fit the same silence exactly: a blocked UI thread
    /// delivers nothing, and a pointer that is over a menu — outside the canvas
    /// — also delivers nothing. The trace watches the canvas, so it cannot tell
    /// those apart, and guessing between them is how B126 got its first wrong
    /// cause.
    /// </para>
    /// <para>
    /// A heartbeat can. It ticks on the dispatcher whether or not anything is
    /// pointed at, so a late tick is the UI thread being blocked and nothing
    /// else. The report then says which of the two a silence was, rather than
    /// leaving the reporter's memory to arbitrate.
    /// </para>
    /// </remarks>
    private static void StartHeartbeat()
    {
        try
        {
            _lastBeat = Now();
            _worstStall = 0;
            _blockedMs = 0;
            _stalls = 0;
            _heartbeat ??= new Avalonia.Threading.DispatcherTimer(
                TimeSpan.FromMilliseconds(HeartbeatMs),
                Avalonia.Threading.DispatcherPriority.Background,
                (_, _) => Beat());
            _heartbeat.Start();
        }
        catch
        {
            // No dispatcher (a headless or non-UI caller): the pointer stream
            // is still worth recording without the stall watch.
        }
    }

    private static void Beat()
    {
        var now = Now();
        var since = (now - _lastBeat) * 1000;
        _lastBeat = now;
        if (since < StallMs) return;
        lock (Gate)
        {
            _stalls++;
            _worstStall = Math.Max(_worstStall, since);
            _blockedMs += since;
            NoteLocked(new Entry(
                now, Kind.Stall, PointerType.Mouse, -1, 0, 0, 0, 0, 0, KeyModifiers.None,
                $"the UI thread was blocked for {since:F0} ms"));
        }
    }

    private static void StopHeartbeat()
    {
        try { _heartbeat?.Stop(); }
        catch { /* nothing to stop */ }
    }

    // ---- wiring the popup layer ------------------------------------------------

    private static bool _popupsWired;

    /// <summary>
    /// Observe every popup in the process, once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This watched the wrong two properties and reported "popups opened 0"
    /// for a session that opened two.</b> The owner right-clicked the Layers
    /// docker, opened the context menu's fly-out from a menu item, and the
    /// trace recorded neither — while B254, the bug the popup half exists for,
    /// is precisely about menus that collapse. A false zero is worse than no
    /// counter: it reads as evidence of absence.
    /// </para>
    /// <para>
    /// The cause is that neither of those is a <c>Flyout</c>.
    /// <see cref="Avalonia.Controls.ContextMenu"/> derives from
    /// <c>MenuBase</c> and has no <c>IsOpenProperty</c> of its own; a menu
    /// item's fly-out is <see cref="Avalonia.Controls.MenuItem.IsSubMenuOpenProperty"/>.
    /// <see cref="Avalonia.Controls.Primitives.Popup"/> is the primitive every
    /// one of them is hosted in, so it is the hook that cannot miss a case,
    /// and it is what should have been used first.
    /// </para>
    /// <para>
    /// <b><see cref="Avalonia.Controls.Primitives.FlyoutBase"/> is deliberately
    /// no longer watched.</b> A flyout's presenter lives in a <c>Popup</c>, so
    /// watching both counted one flyout twice — and the count that would have
    /// inflated is the collapsed-popup ratio, which is the number a fix for
    /// B254 gets judged by. <see cref="Avalonia.Controls.ToolTip"/> stays,
    /// because it can render into an overlay layer instead of a popup and is
    /// the one case <c>Popup</c> does not cover.
    /// </para>
    /// </remarks>
    public static void WirePopups()
    {
        if (_popupsWired) return;
        _popupsWired = true;
        try
        {
            Avalonia.Controls.Primitives.Popup.IsOpenProperty.Changed
                .AddClassHandler<Avalonia.Controls.Primitives.Popup>(
                    (popup, args) => Popup(popup, DescribePopup(popup), args.GetNewValue<bool>()));
            // Named but not counted. A submenu opens a Popup, so the hook above
            // already counts it — the reporter's second trace read "2300 popups"
            // for about 1,150, because every submenu was tallied twice. The
            // inflated number is the collapsed-popup ratio, which is exactly
            // what a fix for B254 gets judged by, so the label is worth keeping
            // and the second tally is not: "Submenu of “Follows the rig”" is
            // what identified the menu that was thrashing.
            Avalonia.Controls.MenuItem.IsSubMenuOpenProperty.Changed
                .AddClassHandler<Avalonia.Controls.MenuItem>(
                    (item, args) => Annotate(
                        $"Submenu of “{item.Header}” {(args.GetNewValue<bool>() ? "opened" : "closed")}"));
            Avalonia.Controls.ToolTip.IsOpenProperty.Changed
                .AddClassHandler<Avalonia.Controls.Control>(
                    (owner, args) => Popup(
                        owner, $"ToolTip on {owner.GetType().Name}", args.GetNewValue<bool>()));
        }
        catch
        {
            // A trace that cannot see popups still sees the pointer stream.
        }
    }

    /// <summary>
    /// A popup named by what it is hosting, because "Popup" on its own does not
    /// tell the reporter which thing collapsed.
    /// </summary>
    private static string DescribePopup(Avalonia.Controls.Primitives.Popup popup)
    {
        try
        {
            var child = popup.Child?.GetType().Name;
            var host = popup.Parent?.GetType().Name ?? popup.TemplatedParent?.GetType().Name;
            return (host, child) switch
            {
                (null, null) => "Popup",
                (null, _) => $"Popup ({child})",
                (_, null) => $"Popup in {host}",
                _ => $"Popup in {host} ({child})",
            };
        }
        catch
        {
            return "Popup";
        }
    }

    // ---- the report ------------------------------------------------------------

    /// <summary>Everything the summary asserts, computed once, for the report and the tests.</summary>
    internal sealed record DeviceSeen(
        PointerType Type, int Id, int Events, float MaxPressure, bool TiltSeen);

    internal sealed record Summary(
        double Seconds,
        int Events,
        bool Wrapped,
        IReadOnlyList<DeviceSeen> Devices,
        int Alternations,
        int Moves,
        int ShiftReported,
        int Enters,
        int Exits,
        int CursorDecisionChanges,
        int CursorAssignments,
        int PopupsOpened,
        int PopupsCollapsed,
        double? ShortestPopupMs,
        int Stalls,
        double WorstStallMs,
        double BlockedMs,
        double LongestSilenceMs,
        IReadOnlyList<string> Verdicts);

    /// <summary>A popup that lived shorter than this collapsed rather than closed.</summary>
    internal const double CollapsedPopupMs = 500;

    /// <summary>
    /// Below this, the trace reports counts and refuses to conclude anything.
    /// </summary>
    /// <remarks>
    /// <b>Found by reading a report rather than by reasoning.</b> A 0.6-second
    /// trace of twelve alternations extrapolated to "1241/min" and pronounced
    /// on the mechanism — arithmetically correct and worthless, which is
    /// <c>docs/DESIGN-performance.md</c>'s lesson exactly: the number was real
    /// and the attribution was not. Five seconds is not a statistical
    /// threshold, it is the point below which the reporter has plainly not
    /// performed the ritual yet, and telling them so beats handing them a rate
    /// with two significant figures of noise in it.
    /// </remarks>
    internal const double MinimumUsefulSeconds = 5;

    internal static Summary Summarize()
    {
        lock (Gate)
        {
            var kept = (int)Math.Min(_count, Capacity);
            var entries = new Entry[kept];
            // Oldest first, wherever the ring's write head is.
            var start = _count > Capacity ? _count % Capacity : 0;
            for (var i = 0; i < kept; i++)
            {
                entries[i] = Ring[(start + i) % Capacity];
            }

            var devices = new Dictionary<(PointerType, int), (int Events, float MaxP, bool Tilt)>();
            int alternations = 0, moves = 0, enters = 0, exits = 0, decided = 0, assigned = 0, opened = 0, shifted = 0;
            var lastId = int.MinValue;
            double seconds = 0;

            foreach (var e in entries)
            {
                seconds = Math.Max(seconds, e.Seconds);
                switch (e.Kind)
                {
                    case Kind.Move or Kind.Enter or Kind.Exit or Kind.Press or Kind.Release or Kind.CaptureLost:
                        var key = (e.Device, e.DeviceId);
                        var d = devices.TryGetValue(key, out var seen) ? seen : (0, 0f, false);
                        devices[key] = (d.Item1 + 1,
                            Math.Max(d.Item2, e.Pressure),
                            d.Item3 || e.TiltX != 0 || e.TiltY != 0);
                        if (lastId != int.MinValue && e.DeviceId != lastId) alternations++;
                        lastId = e.DeviceId;
                        if (e.Kind == Kind.Move) moves++;
                        if (e.Modifiers.HasFlag(KeyModifiers.Shift)) shifted++;
                        if (e.Kind == Kind.Enter) enters++;
                        if (e.Kind == Kind.Exit) exits++;
                        break;
                    case Kind.CursorDecided: decided++; break;
                    case Kind.CursorAssigned: assigned++; break;
                    case Kind.PopupOpened: opened++; break;
                }
            }

            var collapsed = PopupLives.Count(ms => ms < CollapsedPopupMs);
            var shortest = PopupLives.Count > 0 ? PopupLives.Min() : (double?)null;

            // The longest run with nothing at all in it. Reported beside the
            // stall count because the pair is what makes a silence readable:
            // a long silence with no stall in it is the pointer having been
            // somewhere else, and the same silence with a stall in it is a
            // freeze.
            double silence = 0;
            for (var i = 1; i < entries.Length; i++)
            {
                silence = Math.Max(silence, (entries[i].Seconds - entries[i - 1].Seconds) * 1000);
            }

            var deviceList = devices
                .Select(kv => new DeviceSeen(kv.Key.Item1, kv.Key.Item2, kv.Value.Events, kv.Value.MaxP, kv.Value.Tilt))
                .OrderByDescending(d => d.Events)
                .ToList();

            return new Summary(
                seconds, kept, _count > Capacity, deviceList,
                alternations, moves, shifted, enters, exits, decided, assigned,
                opened, collapsed, shortest, _stalls, _worstStall, _blockedMs, silence,
                Verdicts(
                    seconds, deviceList, alternations, enters, exits, assigned,
                    opened, collapsed, _stalls, _worstStall, silence));
        }
    }

    /// <summary>
    /// The lines that discriminate the hypotheses. Every line names its
    /// evidence, so a verdict can be argued with rather than trusted.
    /// </summary>
    private static List<string> Verdicts(
        double seconds,
        List<DeviceSeen> devices,
        int alternations,
        int enters,
        int exits,
        int assigned,
        int opened,
        int collapsed,
        int stalls,
        double worstStall,
        double silence)
    {
        var verdicts = new List<string>();

        if (seconds < MinimumUsefulSeconds)
        {
            return
            [
                $"Only {seconds:F1} s recorded — too short to conclude anything. Arm the trace, then "
                + "hover, draw and open a flyout for about a minute before stopping it.",
            ];
        }

        var perMinute = 60 / seconds;

        if (devices.Count > 1 && alternations > 0)
        {
            var names = string.Join(" and ", devices.Select(d => $"{d.Type} id {d.Id}"));
            verdicts.Add(
                $"Two pointer streams interleave ({names}; {alternations} switches, "
                + $"{alternations * perMinute:F0}/min) — a second, driver-emulated device beside the pen "
                + "is the working explanation, and suppressing the echo stream is the fix to try.");
        }

        if (enters + exits > 0 && seconds > 0 && (enters + exits) * perMinute > 6)
        {
            verdicts.Add(
                $"The pointer left and re-entered the canvas {exits}+{enters} times "
                + $"({(enters + exits) * perMinute:F0}/min) — enter/exit churn; every exit re-arms the "
                + "window's default cursor and dismisses hover UI.");
        }

        if (assigned <= 1 && devices.Count > 0)
        {
            verdicts.Add(
                $"Lightbox assigned the canvas cursor {assigned} time(s) in {seconds:F0} s and never "
                + "changed its mind, so nothing here is flipping it deliberately. If the arrow still "
                + "flickered, it is the enter/exit churn above doing it: the canvas's hidden cursor "
                + "only applies while the pointer is over the canvas, so every exit hands the window's "
                + "arrow back for as long as the gap lasts. The cause is below the app; the cure need "
                + "not be.");
        }

        // Said before the popup and churn lines, because a freeze changes what
        // every other number in the report means.
        if (stalls > 0)
        {
            verdicts.Add(
                $"The UI thread was blocked {stalls} time(s), worst {worstStall / 1000:F1} s — the "
                + "application really did stop answering, and any silence in the event list around "
                + "that point is the freeze rather than the pointer being elsewhere.");
        }
        else if (silence > 2000)
        {
            verdicts.Add(
                $"The longest silence was {silence / 1000:F1} s with no UI-thread stall in it — so "
                + "nothing was frozen; the pointer was somewhere this trace does not watch (a menu, "
                + "a docker, another window). Only the canvas is instrumented.");
        }

        if (opened > 0 && collapsed > 0)
        {
            verdicts.Add(
                $"{collapsed} of {opened} popups closed within {CollapsedPopupMs:F0} ms of opening — "
                + "collapsed rather than dismissed.");
        }

        if (verdicts.Count == 0)
        {
            verdicts.Add("Nothing in this trace distinguishes the hypotheses — "
                + "hover longer, and cross onto a docker and back while tracing.");
        }
        return verdicts;
    }

    /// <summary>
    /// Stop the trace and write the report beside the crash logs. Null when it
    /// could not be written, and never a throw.
    /// </summary>
    public static string? WriteReport()
    {
        Disarm();
        try
        {
            var summary = Summarize();
            var sb = new StringBuilder();
            sb.AppendLine("Lightbox input trace (B126/B254)");
            sb.AppendLine($"when    {DateTime.UtcNow:O} (UTC)");
            sb.AppendLine($"build   {DiagnosticLog.Build}");
            sb.AppendLine($"os      {Environment.OSVersion}");
            sb.AppendLine($"traced  {summary.Seconds:F1} s, {summary.Events} events"
                + (summary.Wrapped ? " (ring wrapped — the earliest events were dropped)" : ""));
            sb.AppendLine();

            sb.AppendLine("devices");
            if (summary.Devices.Count == 0) sb.AppendLine("  none — nothing crossed the canvas while armed");
            foreach (var d in summary.Devices)
            {
                sb.AppendLine($"  {d.Type} id {d.Id}: {d.Events} events, max pressure {d.MaxPressure:F2}, "
                    + (d.TiltSeen ? "tilt reported" : "no tilt"));
            }
            sb.AppendLine();

            sb.AppendLine("counters");
            // The move rate is the device's real report rate as the app sees it:
            // a pen that says it polls at 240 Hz and arrives at 60 has had its
            // stream coalesced or throttled somewhere, which is worth knowing
            // before anybody blames the brush engine for feeling laggy (B189).
            sb.AppendLine($"  moves                 {summary.Moves}"
                + (summary.Seconds > 0 ? $" ({summary.Moves / summary.Seconds:F0}/s)" : ""));
            sb.AppendLine($"  stream alternations   {summary.Alternations}");
            // B256. A stroke is constrained to one axis while Shift is held, and
            // "it only draws horizontal lines after the pen has been away" is
            // exactly what that constraint looks like. Nothing recorded whether
            // Shift was actually reported, so this says so rather than leaving it
            // to be deduced from the shape of the mark.
            sb.AppendLine($"  events claiming Shift {summary.ShiftReported}"
                + (summary.ShiftReported > 0 ? "  <-- axis-lock would engage" : ""));
            sb.AppendLine($"  canvas enter/exit     {summary.Enters}/{summary.Exits}");
            sb.AppendLine($"  cursor decisions      {summary.CursorDecisionChanges} changes");
            sb.AppendLine($"  cursor assignments    {summary.CursorAssignments}");
            sb.AppendLine($"  popups opened         {summary.PopupsOpened}"
                + (summary.ShortestPopupMs is { } s ? $", shortest life {s:F0} ms" : ""));
            sb.AppendLine($"  popups collapsed      {summary.PopupsCollapsed} (under {CollapsedPopupMs:F0} ms)");
            var gc = (
                GC.CollectionCount(0) - _gcAtArm.Gen0,
                GC.CollectionCount(1) - _gcAtArm.Gen1,
                GC.CollectionCount(2) - _gcAtArm.Gen2,
                GC.GetTotalPauseDuration() - _gcAtArm.Pause);
            sb.AppendLine($"  GC collections        gen0 {gc.Item1}, gen1 {gc.Item2}, gen2 {gc.Item3}");
            sb.AppendLine($"  GC pause total        {gc.Item4.TotalMilliseconds:F0} ms"
                + (summary.Stalls > 0
                    ? $"  ({gc.Item4.TotalMilliseconds / Math.Max(1, summary.BlockedMs) * 100:F0}% of the blocked time)"
                    : ""));
            sb.AppendLine($"  UI-thread stalls      {summary.Stalls}"
                + (summary.Stalls > 0 ? $", worst {summary.WorstStallMs:F0} ms" : "")
                + $" (over {StallMs:F0} ms)");
            // The filter's own count, so a trace says whether it engaged rather
            // than leaving "the churn is gone" and "the pen was not used" to
            // look identical.
            sb.AppendLine($"  popups are            {(PopupsAreOverlays ? "in-window overlays" : "native windows")}");
            // The grace's own count, so a trace says whether it engaged rather
            // than leaving a fallen popup rate and an idle guard looking alike.
            sb.AppendLine($"  submenu closes refused {SubmenuCloseGrace.Refused}"
                + (SubmenuCloseGrace.Installed ? "" : "  (grace not installed)"));
            sb.AppendLine($"  longest silence       {summary.LongestSilenceMs:F0} ms"
                + (summary.Stalls == 0 && summary.LongestSilenceMs > 2000
                    ? " — no stall in it, so the pointer was off the canvas"
                    : ""));
            sb.AppendLine();

            sb.AppendLine("verdicts");
            foreach (var v in summary.Verdicts) sb.AppendLine($"  {v}");
            sb.AppendLine();

            sb.AppendLine("events (oldest first)");
            AppendEvents(sb);

            Directory.CreateDirectory(DiagnosticLog.Directory);
            var path = Path.Combine(
                DiagnosticLog.Directory, $"input-trace-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, sb.ToString());
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static void AppendEvents(StringBuilder sb)
    {
        lock (Gate)
        {
            var kept = (int)Math.Min(_count, Capacity);
            var start = _count > Capacity ? _count % Capacity : 0;
            for (var i = 0; i < kept; i++)
            {
                var e = Ring[(start + i) % Capacity];
                sb.Append($"  {e.Seconds,9:F4}  {e.Kind,-14}");
                if (e.DeviceId >= 0)
                {
                    sb.Append($"{e.Device} id {e.DeviceId}  ({e.X:F1}, {e.Y:F1})  p {e.Pressure:F2}");
                    if (e.TiltX != 0 || e.TiltY != 0) sb.Append($"  tilt {e.TiltX:F0}/{e.TiltY:F0}");
                }
                if (e.Modifiers != KeyModifiers.None) sb.Append($"  [{e.Modifiers}]");
                if (e.Detail is { } detail) sb.Append($"  {detail}");
                sb.AppendLine();
            }
        }
    }

    // ---- test seams ------------------------------------------------------------

    /// <summary>Inject an entry at a chosen time — the tests own the clock.</summary>
    internal static void NoteForTests(
        double seconds, Kind kind, PointerType device, int deviceId,
        float pressure = 0, float tiltX = 0, float tiltY = 0, string? detail = null)
    {
        if (!Volatile.Read(ref _armed)) return;
        Note(new Entry(seconds, kind, device, deviceId, 0, 0, pressure, tiltX, tiltY, KeyModifiers.None, detail));
    }

    /// <summary>
    /// Which kinds of event the trace actually saw.
    /// </summary>
    /// <remarks>
    /// The summary counts moves, enters and exits because the verdicts need
    /// those; presses, releases and capture losses only ever appear in the
    /// event list, and a test asserting on a hook has to be able to see that
    /// the hook fired. Deleting one of those hooks was silent until this
    /// existed.
    /// </remarks>
    internal static HashSet<Kind> KindsForTests()
    {
        lock (Gate)
        {
            var kept = (int)Math.Min(_count, Capacity);
            var start = _count > Capacity ? _count % Capacity : 0;
            var kinds = new HashSet<Kind>();
            for (var i = 0; i < kept; i++) kinds.Add(Ring[(start + i) % Capacity].Kind);
            return kinds;
        }
    }

    /// <summary>
    /// Whether popups are rendered inside the window rather than as native
    /// windows — the setting B255's freeze turns on.
    /// </summary>
    /// <remarks>
    /// In the report because it is the one fact that decides how to read the
    /// stall count: the freeze tracked native popup creation at ~100/s, so a
    /// trace still showing stalls with popups as overlays means the cause is
    /// something else, and the same trace with them as native windows means the
    /// setting did not take.
    /// </remarks>
    internal static bool PopupsAreOverlays { get; set; }

    /// <summary>Back to cold: disarmed, empty, no remembered cursor.</summary>
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            Volatile.Write(ref _armed, false);
            _count = 0;
            PopupLives.Clear();
            OpenPopups.Clear();
            _lastDecided = null;
            _lastAssigned = null;
            _stalls = 0;
            _worstStall = 0;
            _blockedMs = 0;
        }
        StopHeartbeat();
    }

    /// <summary>Record a stall without waiting for one — the tests own the clock.</summary>
    internal static void NoteStallForTests(double seconds, double blockedMs)
    {
        if (!Volatile.Read(ref _armed)) return;
        lock (Gate)
        {
            _stalls++;
            _worstStall = Math.Max(_worstStall, blockedMs);
            _blockedMs += blockedMs;
            NoteLocked(new Entry(
                seconds, Kind.Stall, PointerType.Mouse, -1, 0, 0, 0, 0, 0, KeyModifiers.None,
                $"the UI thread was blocked for {blockedMs:F0} ms"));
        }
    }
}
