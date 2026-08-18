using Avalonia.Input;
using Avalonia.Input.Raw;

namespace Lightbox.App.Services;

/// <summary>
/// Drops the phantom mouse a pen tablet's driver sends alongside the pen
/// (B126, B254, B255).
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured, not guessed.</b> Two traces from the reporter's Huion tablet
/// (<c>InputTrace</c>, B126's entry) show a <c>Mouse</c> pointer interleaved
/// with the pen at sub-millisecond spacing: **0% of its coordinates are
/// fractional** against the pen's 89%, its pressure is exactly Avalonia's
/// non-pen default of 0.50, and it reports no tilt. It is Windows Ink's mouse
/// emulation shadowing the pen, snapped to whole pixels — not a mouse anybody
/// is holding.
/// </para>
/// <para>
/// <b>What it costs, which is the reason this exists at all.</b> Every handoff
/// between the two streams makes Avalonia recompute pointer-over, which fires
/// <c>Exit</c> then <c>Enter</c> — 2,763 times in 57 seconds. Each of those
/// re-arms the window's arrow over the hidden brush cursor (B126) and
/// light-dismisses anything hovering (B254). Worst of all, it drives
/// <c>MenuItem</c>'s hover logic to open and close submenus about fifty times a
/// second, and building and destroying that many popup windows **blocks the UI
/// thread for up to 6.1 seconds** (B255). Every stall over three seconds in the
/// second trace is preceded by ~100 popup opens per second; the rate away from
/// any stall is zero.
/// </para>
/// <para>
/// <b>Why it filters here rather than in the canvas.</b> The obvious fix — the
/// canvas coalescing its own enter/exit — was the one first proposed and would
/// have fixed the least important symptom. The submenu thrash happens inside
/// Avalonia's own menu code, which no handler of ours is on the path of. The
/// only place ahead of all three symptoms is the raw input stream, before
/// pointer-over is recomputed, which is what <c>IInputManager.PreProcess</c>
/// offers.
/// </para>
/// <para>
/// <b>The guard that keeps a real mouse working.</b> Nothing is dropped unless
/// a pen event arrived within <see cref="EchoWindowMs"/>. A machine with no pen
/// behaves exactly as it did — the filter can never engage, because the clock
/// it reads is only ever set by a pen. Someone who puts the pen down and picks
/// up a mouse loses at most a fifth of a second of mouse movement, and no
/// button press ever: presses and releases are never dropped, so the worst case
/// is a cursor that starts moving a beat late rather than a click going
/// missing.
/// </para>
/// </remarks>
internal static class PenEchoFilter
{
    /// <summary>
    /// How long after a pen event a mouse event is treated as its echo.
    /// </summary>
    /// <remarks>
    /// The measured handoff is a **median of 0.5 ms and a p90 of 0.8 ms**, so
    /// this is more than two orders of magnitude above what it needs to cover.
    /// It is sized for the gap a person notices instead: 200 ms is about the
    /// fastest anybody swaps a pen for a mouse, and being generous here costs
    /// only that a deliberate swap starts moving slightly late.
    /// </remarks>
    internal const double EchoWindowMs = 200;

    /// <summary>Whether this event is the pen's echo and should be dropped.</summary>
    /// <param name="isMouse">The event came from a mouse device.</param>
    /// <param name="isMove">
    /// The event is a move or a hover-only update rather than a button
    /// transition. Only these are dropped — see the remarks.
    /// </param>
    /// <param name="msSincePen">
    /// Milliseconds since the last pen event, or null if no pen has ever been
    /// seen in this session.
    /// </param>
    /// <remarks>
    /// <b>Presses are never dropped, and that is a deliberate asymmetry.</b> A
    /// swallowed move costs a few milliseconds of cursor position that the next
    /// event corrects; a swallowed press costs the artist a click they made on
    /// purpose, with nothing to correct it. The churn is made entirely of moves
    /// and the enter/exit they imply, so refusing to touch buttons gives up
    /// nothing that matters.
    /// </remarks>
    internal static bool IsEcho(bool isMouse, bool isMove, double? msSincePen) =>
        isMouse && isMove && msSincePen is { } since && since <= EchoWindowMs;

    /// <summary>Move-like raw event types — the ones safe to drop.</summary>
    /// <remarks>
    /// <c>LeaveWindow</c> is included because it is exactly what the echo makes
    /// the canvas fire, and it is the event that re-arms the window's arrow.
    /// </remarks>
    internal static bool IsMoveLike(RawPointerEventType type) => type
        is RawPointerEventType.Move
        or RawPointerEventType.LeaveWindow
        or RawPointerEventType.NonClientLeftButtonDown;

    private static long _lastPenTicks;
    private static long _dropped;
    private static bool _installed;

    /// <summary>How many echo events have been dropped, for the trace's report.</summary>
    internal static long Dropped => Interlocked.Read(ref _dropped);

    // ---- reaching the raw stream ------------------------------------------------

    /// <summary>
    /// Why this is reflection, which is not a decision anybody should be pleased
    /// with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>There is no other lever.</b> The pointer-over recomputation that fires
    /// the enter/exit pair happens inside Avalonia's input manager, ahead of any
    /// routed event — and <c>PointerEntered</c> and <c>PointerExited</c> are
    /// <see cref="Avalonia.Interactivity.RoutingStrategies.Direct"/>, so they
    /// never pass through a parent where a handler could intercept them.
    /// Swallowing <c>PointerMoved</c> on the way down does not help either,
    /// because pointer-over is not derived from the routed event.
    /// </para>
    /// <para>
    /// <b>And the one lever that exists is not compile-time accessible.</b>
    /// <c>IInputManager.PreProcess</c> is public on the runtime assembly and
    /// hidden by the reference assembly Avalonia 12.1.1 ships, so the call
    /// compiles nowhere and works everywhere. That is a fact about the package,
    /// not about the design.
    /// </para>
    /// <para>
    /// <b>The failure mode is chosen to be safe and loud.</b> If any member
    /// stops resolving — an Avalonia upgrade, a trimmed build — nothing is
    /// filtered and the application behaves exactly as it does today, which is
    /// the pre-fix behaviour rather than a broken one. Silence is the danger, so
    /// <see cref="Unavailable"/> records the reason and
    /// <c>PenEchoFilterTests.TheRawInputHooksStillResolve</c> fails the build
    /// rather than letting a version bump quietly restore the freeze.
    /// </para>
    /// </remarks>
    internal static string? Unavailable { get; private set; }

    private static System.Reflection.PropertyInfo? _deviceProp;
    private static System.Reflection.PropertyInfo? _typeProp;
    private static System.Reflection.PropertyInfo? _handledProp;

    /// <summary>
    /// Resolve the members this needs, returning the first one that is missing.
    /// </summary>
    internal static string? Resolve()
    {
        var raw = typeof(RawPointerEventArgs);
        _deviceProp = raw.GetProperty("Device");
        _typeProp = raw.GetProperty("Type");
        _handledProp = raw.GetProperty("Handled");
        if (_deviceProp is null) return "RawPointerEventArgs.Device";
        if (_typeProp is null) return "RawPointerEventArgs.Type";
        if (_handledProp is null || !_handledProp.CanWrite) return "RawPointerEventArgs.Handled";
        if (typeof(IInputManager).GetProperty("PreProcess") is null) return "IInputManager.PreProcess";
        if (AvaloniaBase.GetType("Avalonia.AvaloniaLocator") is null) return "Avalonia.AvaloniaLocator";
        return null;
    }

    /// <summary>
    /// The assembly the input types live in — <c>Avalonia.Base</c>, which is not
    /// the one <c>Application</c> lives in.
    /// </summary>
    /// <remarks>
    /// <b>This is the line the first attempt got wrong, and the reason it is a
    /// named member now.</b> The lookup was written as
    /// <c>typeof(Application).Assembly.GetType("Avalonia.AvaloniaLocator")</c>
    /// — but <c>Application</c> is in <c>Avalonia.Controls</c> and
    /// <c>AvaloniaLocator</c> is in <c>Avalonia.Base</c>, so it resolved to null,
    /// the filter stood down, and the reporter's next trace read
    /// <c>echo events dropped 0</c> with every symptom intact. Anchoring on a
    /// type that is definitionally in the right assembly removes the guess.
    /// </remarks>
    private static System.Reflection.Assembly AvaloniaBase => typeof(IInputManager).Assembly;

    /// <summary>
    /// Avalonia's input manager, or null with the reason recorded.
    /// </summary>
    /// <remarks>
    /// <b>Lives here rather than at the call site, because the call site was
    /// not tested and this is.</b> The bug above was in three lines of
    /// reflection sitting in <c>App.axaml.cs</c>, which no test reaches — the
    /// filter's own policy was covered in full while the thing that decides
    /// whether the policy ever runs was not. <see cref="Resolve"/> now covers
    /// it, so the same mistake fails the build.
    /// </remarks>
    internal static object? FindInputManager()
    {
        try
        {
            // The direct route first: a public static on an internal type.
            if (AvaloniaBase.GetType("Avalonia.Input.InputManager")
                    ?.GetProperty("Instance")?.GetValue(null) is { } instance)
            {
                return instance;
            }

            // The service locator, for a build where that moves.
            var locator = AvaloniaBase.GetType("Avalonia.AvaloniaLocator");
            var current = locator?.GetProperty("Current")?.GetValue(null);
            var get = current?.GetType().GetMethod("GetService", [typeof(Type)]);
            return get?.Invoke(current, [typeof(IInputManager)]);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Start filtering. Idempotent, and does nothing where the input manager or
    /// the members cannot be reached — a missing filter must never stop the
    /// application starting.
    /// </summary>
    public static void Install(object? manager = null)
    {
        if (_installed) return;
        _installed = true;

        if (Resolve() is { } missing)
        {
            Unavailable = $"{missing} could not be resolved";
            return;
        }

        manager ??= FindInputManager();
        if (manager is null)
        {
            Unavailable = "no input manager (headless)";
            return;
        }

        try
        {
            var observable = typeof(IInputManager).GetProperty("PreProcess")!.GetValue(manager);
            var subscribe = observable?.GetType().GetMethod(
                "Subscribe", [typeof(IObserver<RawInputEventArgs>)]);
            if (observable is null || subscribe is null)
            {
                Unavailable = "PreProcess is not subscribable";
                return;
            }
            subscribe.Invoke(observable, [new Sink()]);
        }
        catch (Exception ex)
        {
            Unavailable = ex.GetType().Name;
        }
    }

    /// <summary>The decision, applied to one raw event.</summary>
    internal static void Consider(RawInputEventArgs e)
    {
        if (e is not RawPointerEventArgs pointer) return;
        if (_deviceProp is null || _typeProp is null || _handledProp is null) return;

        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_deviceProp.GetValue(pointer) is not IMouseDevice)
        {
            // A pen (or touch) event: remember when, and never filter it.
            Interlocked.Exchange(ref _lastPenTicks, now);
            return;
        }

        var last = Interlocked.Read(ref _lastPenTicks);
        double? since = last == 0
            ? null
            : (now - last) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        var moveLike = _typeProp.GetValue(pointer) is RawPointerEventType type && IsMoveLike(type);
        if (!IsEcho(isMouse: true, moveLike, since)) return;

        _handledProp.SetValue(pointer, true);
        Interlocked.Increment(ref _dropped);
    }

    /// <summary>
    /// A plain observer rather than a lambda subscription, so a fault in the
    /// stream cannot tear the filter down silently and leave the churn back.
    /// </summary>
    private sealed class Sink : IObserver<RawInputEventArgs>
    {
        public void OnCompleted() { }

        public void OnError(Exception error) { }

        public void OnNext(RawInputEventArgs value)
        {
            try { Consider(value); }
            catch { /* input must never break on the way in */ }
        }
    }

    /// <summary>Test seam: forget the pen, the count and the installation.</summary>
    internal static void ResetForTests()
    {
        Interlocked.Exchange(ref _lastPenTicks, 0);
        Interlocked.Exchange(ref _dropped, 0);
        _installed = false;
        Unavailable = null;
    }
}
