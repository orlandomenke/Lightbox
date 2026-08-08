using System.Diagnostics;
using Avalonia.Threading;

namespace Lightbox.App.Services;

/// <summary>
/// Timeline playback ticker: advances the playhead against a wall clock at the
/// scene fps scaled by a playback-speed percentage (100 = real time).
/// </summary>
/// <remarks>
/// <para>
/// <b>This used to be a bare <see cref="DispatcherTimer"/> re-armed at the frame
/// interval, and that is slow-and-stuttery by construction on a document with
/// nothing in it.</b> Two separate defects, both independent of how much work a
/// frame costs — which is why "it stutters at FullHD" was not a rendering
/// problem and would not have been fixed by moving compositing to the GPU:
/// </para>
/// <list type="number">
/// <item>
/// <b>The interval was a delay between ticks, not a frame period.</b> The tick
/// handler advances the playhead, which recomposites and publishes
/// synchronously — so the real period was <c>interval + however long that
/// took</c>. At 12 fps with a 20 ms frame, playback ran at 9.8 fps and never
/// recovered, because nothing measured the shortfall. The scene simply played
/// slow, which is what "running the animation frames low" is.
/// </item>
/// <item>
/// <b>Errors accumulated instead of cancelling.</b> A timer is a lower bound:
/// the OS delivers it late by a scheduling quantum, a GC pause, or the
/// compositor. Advancing exactly one frame per tick means every late tick
/// permanently displaces every frame after it, and the displacement varies —
/// which is *uneven frame duration*, and uneven frame duration is precisely
/// what the eye reads as stutter. A rock-steady 9.8 fps looks smooth; 12 fps
/// with a wandering period does not.
/// </item>
/// </list>
/// <para>
/// <b>Fixed by pacing against elapsed time rather than counting ticks.</b> Each
/// frame has a wall-clock due time; the next delay is whatever remains until
/// it, so the cost of the tick comes *out* of the interval instead of being
/// added to it, and a late tick shortens the next one rather than pushing it.
/// When the machine genuinely cannot keep up, frames are dropped to stay in
/// time — which matters here beyond smoothness, because the scratch track does
/// not drop with them and drift against audio is the thing an animator is
/// listening for.
/// </para>
/// <para>
/// <b>What is measured and what is not.</b> The pacing arithmetic is pure and
/// unit-tested (<c>PlaybackPacingTests</c>). The magnitude on real hardware is
/// not tested and cannot be here: the headless dispatcher does not deliver
/// timer ticks in real time, so a test that appeared to measure jitter would be
/// measuring the harness. The defects above are structural — read off the code
/// rather than inferred from a number.
/// </para>
/// </remarks>
public sealed class PlaybackClock
{
    /// <summary>
    /// The most frames a single tick may skip to catch up.
    /// </summary>
    /// <remarks>
    /// A cap rather than "advance to wherever the clock says", because the
    /// unbounded form turns a one-off stall — a breakpoint, a save dialog, a
    /// laptop lid — into a burst that races through the scene to get back on
    /// schedule. Falling behind by a second and skipping a second of animation
    /// are both wrong; skipping is merely the one that also looks broken. Past
    /// this many, the clock resynchronises to now and accepts the slip.
    /// </remarks>
    public const int MaxCatchUpFrames = 4;

    private readonly DispatcherTimer _timer = new();
    private readonly Stopwatch _elapsed = new();

    private TimeSpan _frameDuration = TimeSpan.FromSeconds(1.0 / 12);
    private TimeSpan _nextFrameDue;

    /// <summary>
    /// Raised when the playhead should advance, carrying how many frames.
    /// </summary>
    /// <remarks>
    /// The count is the whole point: a consumer that ignores it and always
    /// advances one is back to the drift this class exists to remove.
    /// </remarks>
    public event Action<int>? Tick;

    public bool IsRunning => _timer.IsEnabled;

    /// <summary>The interval the timer is currently waiting out.</summary>
    public TimeSpan Interval => _timer.Interval;

    /// <summary>The frame period this clock is pacing to.</summary>
    public TimeSpan FrameDuration => _frameDuration;

    public PlaybackClock()
    {
        _timer.Tick += (_, _) => OnTimer();
    }

    public void Start(int fps, int speedPercent = 100)
    {
        _frameDuration = IntervalFor(fps, speedPercent);
        _elapsed.Restart();
        _nextFrameDue = _frameDuration;
        _timer.Interval = _frameDuration;
        _timer.Start();
    }

    public static TimeSpan IntervalFor(int fps, int speedPercent) =>
        TimeSpan.FromSeconds(1.0 / (Math.Max(1, fps) * Math.Max(1, speedPercent) / 100.0));

    public void Stop()
    {
        _timer.Stop();
        _elapsed.Stop();
    }

    private void OnTimer()
    {
        var plan = Pace(_elapsed.Elapsed, _nextFrameDue, _frameDuration);
        _nextFrameDue = plan.NextFrameDue;

        // Re-arm BEFORE the handler runs. The handler recomposites and publishes
        // synchronously, so arming afterwards would put its cost back into the
        // period — the original defect, reintroduced one line lower down.
        _timer.Interval = plan.Delay;

        if (plan.Frames > 0) Tick?.Invoke(plan.Frames);
    }

    /// <summary>What a tick should do, given where the wall clock actually is.</summary>
    /// <param name="Frames">Frames to advance now; 0 when the tick arrived early.</param>
    /// <param name="NextFrameDue">Elapsed time the following frame is due at.</param>
    /// <param name="Delay">How long to wait before looking again.</param>
    public readonly record struct Plan(int Frames, TimeSpan NextFrameDue, TimeSpan Delay);

    /// <summary>
    /// The pacing arithmetic, pure so it can be tested without a dispatcher.
    /// </summary>
    /// <remarks>
    /// Split out because the interesting behaviour — late ticks shortening the
    /// next interval, a stall dropping frames rather than replaying them, the
    /// catch-up cap — is arithmetic, and arithmetic tested through a real timer
    /// is a test of the operating system's scheduler.
    /// </remarks>
    public static Plan Pace(TimeSpan elapsed, TimeSpan nextFrameDue, TimeSpan frameDuration)
    {
        if (frameDuration <= TimeSpan.Zero) frameDuration = TimeSpan.FromSeconds(1.0 / 12);

        // Early: the timer fired before the frame was due. Wait out the rest
        // rather than advancing, or a fast-delivering timer runs the scene fast.
        if (elapsed < nextFrameDue)
        {
            return new Plan(0, nextFrameDue, Clamp(nextFrameDue - elapsed));
        }

        // How far past due we are decides how many frames this tick owes. One
        // for the frame that came due, plus one for each whole period since.
        var behind = elapsed - nextFrameDue;
        var extra = (int)(behind.Ticks / frameDuration.Ticks);
        var frames = 1 + extra;

        if (frames > MaxCatchUpFrames)
        {
            // Too far gone to catch up honestly: accept the slip and re-base on
            // now, so one stall does not spend the next second racing.
            return new Plan(
                MaxCatchUpFrames,
                elapsed + frameDuration,
                Clamp(frameDuration));
        }

        // Due time advances by whole frames from where it WAS, never from now —
        // that is what makes a late tick shorten the next interval instead of
        // shifting every frame after it. Errors cancel rather than accumulate.
        var due = nextFrameDue + frames * frameDuration;
        return new Plan(frames, due, Clamp(due - elapsed));
    }

    /// <summary>
    /// A delay the dispatcher will honour: never zero or negative, and never
    /// longer than a frame.
    /// </summary>
    /// <remarks>
    /// The floor is not cosmetic — <see cref="DispatcherTimer"/> with a
    /// non-positive interval starves the UI thread, so a badly behind playhead
    /// would freeze the window it was trying to keep smooth.
    /// </remarks>
    private static TimeSpan Clamp(TimeSpan delay)
    {
        var floor = TimeSpan.FromMilliseconds(1);
        return delay < floor ? floor : delay;
    }
}
