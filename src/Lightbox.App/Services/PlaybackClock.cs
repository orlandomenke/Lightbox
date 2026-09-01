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
/// <para>
/// <b>B150 added the third structural defect and the measurement that was
/// missing with it.</b> The timer ran at the dispatcher's <em>lowest foreground
/// priority</em> — see <see cref="Priority"/> — so a frame already due was served
/// after everything else the application had queued. And because
/// <see cref="Pace"/> absorbs lateness by design, nothing anywhere said how late
/// the ticks were arriving: a clock being delivered on time and one being
/// delivered 40 ms late look identical from the playhead's side, which is why
/// the symptom could be reported and not found. <see cref="Stats"/> is that
/// number, printed in <c>Help ▸ Write a render report</c>.
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

    /// <summary>
    /// The dispatcher band the frame clock runs in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Named, because the default was wrong and silently so (B150).</b> A bare
    /// <c>new DispatcherTimer()</c> runs at <see cref="DispatcherPriority.Background"/>,
    /// documented as <em>"processed after other non-idle operations have
    /// completed"</em> — the lowest foreground band there is. That is the wrong
    /// place for a clock whose entire job is to be on time: every queued layout,
    /// data binding, render and ordinary post in the application is served ahead
    /// of the frame that is already due.
    /// </para>
    /// <para>
    /// <b>B150 then overshot, and <see cref="DispatcherPriority.Render"/> is the
    /// one band this must not be in.</b> The argument for it was that the tick
    /// "belongs with the render cycle" — which is true of what it <em>does</em>
    /// and exactly wrong about where it should <em>queue</em>, because Avalonia
    /// commits a frame to the screen at that same priority. Equal priority is
    /// FIFO, and the tick re-arms itself from inside its own handler while
    /// already overdue (the report has never once shown a tick arriving on
    /// time), so the next tick queues ahead of the commit that the previous tick
    /// asked for. The clock competes with the thing whose job is to show the
    /// frame the clock just produced.
    /// </para>
    /// <para>
    /// <b>The ordering follows from which lateness is absorbed.</b> A late tick
    /// is absorbed by design — that is what <see cref="Pace"/> is, and the whole
    /// first half of these remarks is about it. A late <em>commit</em> is
    /// absorbed by nothing: it is the stutter, directly. So the commit must
    /// outrank the clock, which means the clock has to sit below
    /// <c>Render</c> — not above it, and not level with it.
    /// </para>
    /// <para>
    /// <see cref="DispatcherPriority.Input"/> is where it lands. Still far above
    /// the <c>Background</c> band B150 rescued it from, and now behind the
    /// commit. Sharing a band with input means a hard stream of pointer events
    /// can delay a tick — and that is the trade this makes knowingly, because
    /// <see cref="Pace"/> takes that lateness out of the following interval and
    /// nothing takes it out of a frame that never got drawn.
    /// </para>
    /// </remarks>
    public static readonly DispatcherPriority Priority = PriorityFromEnvironment();

    /// <summary>
    /// The band this build actually runs in, overridable for one session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>LIGHTBOX_CLOCK_PRIORITY=Background</c> puts the defect back, on
    /// purpose.</b> B150's fix is a hypothesis about a symptom nobody can
    /// reproduce off the reporter's machine, and "I installed a new build and it
    /// feels better" is not evidence — a new build changes a dozen things and a
    /// hand is a poor stopwatch. This makes the one variable switchable inside a
    /// single binary and a single session, which is the difference between an
    /// impression and a comparison.
    /// </para>
    /// <para>
    /// An unrecognised value is the default rather than a failure: this exists to
    /// be typed at a command prompt by somebody chasing a stutter, and a typo
    /// that stops the application starting would be a worse bug than the one it
    /// is diagnosing. What it chose is printed in the render report, so a run can
    /// never be attributed to a setting it did not have.
    /// </para>
    /// </remarks>
    internal static DispatcherPriority PriorityFromEnvironment()
    {
        var asked = Environment.GetEnvironmentVariable("LIGHTBOX_CLOCK_PRIORITY");
        return asked?.Trim().ToLowerInvariant() switch
        {
            "background" => DispatcherPriority.Background,
            "input" => DispatcherPriority.Input,
            // Between Input and Render. Here so the four bands either side of the
            // choice can all be tried from one binary: if Input is late enough to
            // be felt but Render brings the stutter back, this is the answer and
            // there would otherwise be no way to find that out.
            "loaded" => DispatcherPriority.Loaded,
            "normal" => DispatcherPriority.Normal,
            "render" => DispatcherPriority.Render,
            _ => DispatcherPriority.Input,
        };
    }

    private readonly DispatcherTimer _timer = new(Priority);
    private readonly Stopwatch _elapsed = new();

    private TimeSpan _frameDuration = TimeSpan.FromSeconds(1.0 / 12);
    private TimeSpan _nextFrameDue;

    private PacingTally _tally;

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
        // The tick holds this clock WEAKLY, and that is B281's whole fix.
        // A running DispatcherTimer is rooted by the dispatcher, so a tick
        // closure that captured `this` kept the service - and through
        // its Tick subscribers, the whole MainViewModel - reachable for the life of the
        // dispatcher. In the app that is one instance and harmless; in the
        // test suite it was every MainViewModel ever constructed, ~2 MB each,
        // ~8 GB of gen2 heap across a 4,200-test run, which is what was
        // OOM-killing 16 GB CI runners (the B269 wedge) and this entry's own
        // local exit-137 kills. Proven by A/B: 15/15 dropped view models
        // survived a full GC with the strong tick, 1/15 with the timer
        // stopped. When the owner is collected the tick stops the timer, so
        // the dispatcher is left rooting only the timer object itself.
        var weakSelf = new WeakReference<PlaybackClock>(this);
        var timer = _timer;
        _timer.Tick += (_, _) =>
        {
            if (weakSelf.TryGetTarget(out var self)) self.OnTimer();
            else timer.Stop();
        };
    }

    public void Start(int fps, int speedPercent = 100)
    {
        _frameDuration = IntervalFor(fps, speedPercent);
        _elapsed.Restart();
        _nextFrameDue = _frameDuration;
        _timer.Interval = _frameDuration;
        _tally = default;
        _timer.Start();
    }

    /// <summary>
    /// How well the clock actually kept time, for the render report.
    /// </summary>
    /// <param name="Ticks">Ticks that advanced the playhead.</param>
    /// <param name="LateTicks">Of those, how many arrived after the frame was due.</param>
    /// <param name="DroppedFrames">Frames skipped to stay in time.</param>
    /// <param name="Resyncs">Times the catch-up cap was hit and the clock re-based on now.</param>
    /// <param name="MeanLatenessMs">Average lateness across every advancing tick.</param>
    /// <param name="WorstLatenessMs">The worst single one.</param>
    public readonly record struct Pacing(
        int Ticks, int LateTicks, int DroppedFrames, int Resyncs,
        double MeanLatenessMs, double WorstLatenessMs);

    /// <summary>
    /// What the last run of playback actually did, as opposed to what it asked
    /// for.
    /// </summary>
    /// <remarks>
    /// <b>Recorded because the interesting failure is invisible from inside.</b>
    /// <see cref="Pace"/> absorbs a late tick by shortening the next interval and
    /// by dropping frames, which is right and is also why a clock that is being
    /// delivered late looks, from the playhead's side, exactly like one that is
    /// not. The lateness is the only place the difference shows, and it is
    /// per-machine — so it is measured on the artist's and printed, rather than
    /// argued about.
    /// </remarks>
    public Pacing Stats => _tally.Snapshot;

    /// <summary>
    /// The running totals behind <see cref="Stats"/>.
    /// </summary>
    /// <remarks>
    /// A separate value rather than six fields on the clock, so the accounting
    /// is reachable by a test without a dispatcher — the same reason
    /// <see cref="Pace"/> is pure. A tally that can only be exercised by running
    /// a real timer would be tested by the harness rather than by the suite,
    /// which is exactly what this file's own header says not to do.
    /// </remarks>
    internal struct PacingTally
    {
        private int _ticks;
        private int _lateTicks;
        private int _droppedFrames;
        private int _resyncs;
        private double _latenessTotalMs;
        private double _worstLatenessMs;

        /// <summary>Fold in one tick that advanced the playhead.</summary>
        /// <param name="lateMs">
        /// How late the tick was against the due time it answered. Never
        /// negative: a tick that fires early waits out the rest of the period
        /// rather than advancing, so it never reaches here.
        /// </param>
        public void Record(double lateMs, int frames)
        {
            if (frames <= 0) return;
            lateMs = Math.Max(0, lateMs);

            _ticks++;
            _latenessTotalMs += lateMs;
            if (lateMs > _worstLatenessMs) _worstLatenessMs = lateMs;
            if (lateMs > 0) _lateTicks++;

            // One frame is the tick doing its job; everything past it was
            // skipped to stay in time, which is the cost of being late rather
            // than a second kind of tick.
            _droppedFrames += frames - 1;
            if (frames >= MaxCatchUpFrames) _resyncs++;
        }

        public readonly Pacing Snapshot => new(
            _ticks, _lateTicks, _droppedFrames, _resyncs,
            _ticks == 0 ? 0 : _latenessTotalMs / _ticks,
            _worstLatenessMs);
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
        var elapsed = _elapsed.Elapsed;
        var plan = Pace(elapsed, _nextFrameDue, _frameDuration);

        // Lateness is measured against the due time this tick was answering, so
        // it has to be read before the plan moves it on.
        _tally.Record((elapsed - _nextFrameDue).TotalMilliseconds, plan.Frames);

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
