using System.Diagnostics;

namespace Lightbox.App.Rendering;

/// <summary>
/// How long a published frame waits before it is drawn.
/// </summary>
/// <remarks>
/// <para>
/// <b>The second half of "playback stutters", and the half nothing could
/// see.</b> B150 added the measurement for whether the frame <em>clock</em>
/// arrives on time; this measures whether the frame the clock asked for actually
/// reaches the screen. They are different failures and they need different
/// fixes, and a report that only carries the first will read clean on a machine
/// where the second is the problem.
/// </para>
/// <para>
/// The distinguishing signature, which is why both numbers exist: if publishes
/// are evenly spaced and presents are not, the clock is innocent and something
/// downstream is only being pumped when the dispatcher happens to wake — which
/// is exactly what "smooth while I move the mouse, stuttery when I stop" would
/// look like from in here.
/// </para>
/// <para>
/// <b>Two threads touch this</b> — publishes come from the UI thread and
/// presents from the render thread — so the ring is behind a lock rather than
/// interlocked fields. It is taken once per published frame and once per
/// rendered one, which at any frame rate a person can watch is nothing.
/// </para>
/// </remarks>
internal sealed class PresentLatency
{
    /// <summary>
    /// How many recent publishes to keep waiting for their render.
    /// </summary>
    /// <remarks>
    /// A ring rather than a dictionary, because the interesting case is a
    /// publish that is <em>never</em> drawn — superseded by a newer one before
    /// the render thread got to it — and a dictionary would grow by one entry
    /// for every one of those. Sixteen is far more than the compositor's own
    /// queue depth, so an entry that falls out of the ring unmatched really was
    /// dropped rather than merely late.
    /// </remarks>
    public const int Tracked = 16;

    private readonly (long Seq, long Ticks, long Canvas, long Elsewhere, long Enqueued)[] _pending =
        new (long, long, long, long, long)[Tracked];

    private readonly Lock _gate = new();

    private int _next;
    private int _presented;
    private int _superseded;
    private double _totalMs;
    private double _worstMs;

    // B321: the wait split in two at the moment the UI thread hands the draw
    // over. A frame handed to the canvas waits 70-88 ms to be drawn in every
    // capture the owner has taken, and that single number has two completely
    // different explanations with two completely different fixes: Avalonia not
    // scheduling the visual pass (the wait is before the hand-over) or the
    // render thread being slow (after it). The report could not tell them apart
    // and neither could anyone reading it.
    private int _enqueuedCount;
    private double _toEnqueueTotalMs;
    private double _toEnqueueWorstMs;

    // The same three numbers again, split by what happened while the frame was
    // waiting. See Cohort.
    private readonly int[] _cohortCount = new int[3];
    private readonly double[] _cohortTotal = new double[3];
    private readonly double[] _cohortWorst = new double[3];

    /// <summary>What reached the window while a frame was waiting to be drawn.</summary>
    public enum Cohort
    {
        /// <summary>Nothing. This is the cohort playback has to be fast in.</summary>
        Quiet = 0,

        /// <summary>Pointer events, but none of them on the canvas.</summary>
        InputElsewhere = 1,

        /// <summary>Pointer events on the canvas, which invalidate it directly.</summary>
        InputOnCanvas = 2,
    }

    /// <summary>A frame has been handed to the canvas.</summary>
    public void Published(long seq)
    {
        var (canvas, elsewhere) = InputPulse.Read();
        lock (_gate)
        {
            var slot = _next++ % Tracked;
            // Whatever was in this slot never got drawn — the ring is deeper
            // than the compositor's queue, so it was superseded rather than
            // still in flight.
            if (_pending[slot].Ticks != 0) _superseded++;
            _pending[slot] = (seq, Stopwatch.GetTimestamp(), canvas, elsewhere, 0);
        }
    }

    /// <summary>
    /// The UI thread has built the draw and handed it to the compositor (B321).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The midpoint of the wait, and the only place it can honestly be taken:
    /// before this the frame is waiting for Avalonia to run a visual pass at
    /// all, after it the frame is in the compositor's hands. Recorded per
    /// sequence rather than as a running average so it stays comparable with the
    /// total in the same row.
    /// </para>
    /// <para>
    /// Called from the render override, which may run for a frame this ring has
    /// already forgotten, or for one that was never published (a cursor repaint
    /// re-draws the same snapshot). Both are ignored rather than counted — a
    /// midpoint attributed to the wrong publish is worse than a missing one.
    /// </para>
    /// </remarks>
    public void Enqueued(long seq)
    {
        lock (_gate)
        {
            for (var i = 0; i < Tracked; i++)
            {
                if (_pending[i].Seq != seq || _pending[i].Ticks == 0) continue;
                // Only the first hand-over of a given publish: a repaint of the
                // same snapshot would otherwise restate the midpoint as though
                // the frame had been re-published.
                if (_pending[i].Enqueued != 0) return;

                var now = Stopwatch.GetTimestamp();
                _pending[i].Enqueued = now;
                var ms = (now - _pending[i].Ticks) * 1000.0 / Stopwatch.Frequency;
                _enqueuedCount++;
                _toEnqueueTotalMs += ms;
                if (ms > _toEnqueueWorstMs) _toEnqueueWorstMs = ms;
                return;
            }
        }
    }

    /// <summary>That frame has been drawn.</summary>
    public void Rendered(long seq)
    {
        var (canvas, elsewhere) = InputPulse.Read();
        lock (_gate)
        {
            for (var i = 0; i < Tracked; i++)
            {
                if (_pending[i].Seq != seq || _pending[i].Ticks == 0) continue;

                var ms = (Stopwatch.GetTimestamp() - _pending[i].Ticks)
                    * 1000.0 / Stopwatch.Frequency;

                // Canvas input wins the tie: if both arrived, the canvas one is
                // the candidate explanation and attributing the frame anywhere
                // else would hide it.
                var cohort =
                    canvas != _pending[i].Canvas ? Cohort.InputOnCanvas
                    : elsewhere != _pending[i].Elsewhere ? Cohort.InputElsewhere
                    : Cohort.Quiet;

                _pending[i] = default;
                _presented++;
                _totalMs += ms;
                if (ms > _worstMs) _worstMs = ms;

                var c = (int)cohort;
                _cohortCount[c]++;
                _cohortTotal[c] += ms;
                if (ms > _cohortWorst[c]) _cohortWorst[c] = ms;
                return;
            }
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            Array.Clear(_pending);
            _next = 0;
            _presented = 0;
            _superseded = 0;
            _totalMs = 0;
            _worstMs = 0;
            Array.Clear(_cohortCount);
            Array.Clear(_cohortTotal);
            Array.Clear(_cohortWorst);
            _enqueuedCount = 0;
            _toEnqueueTotalMs = 0;
            _toEnqueueWorstMs = 0;
        }
        InputPulse.Reset();
    }

    /// <param name="Presented">Frames that were published and then drawn.</param>
    /// <param name="Superseded">Frames replaced before anything drew them.</param>
    /// <param name="MeanMs">Average wait between publish and draw.</param>
    /// <param name="WorstMs">The worst single wait.</param>
    /// <param name="ByCohort">
    /// The same, split by what arrived while it waited. Defaulted so a test that
    /// only cares about the totals can still say what it means in four numbers;
    /// the report treats absent and "not three cohorts" the same way, which is
    /// to print nothing rather than to guess.
    /// </param>
    /// <param name="Enqueued">
    /// Frames whose hand-over to the compositor was timed (B321).
    /// </param>
    /// <param name="ToEnqueueMeanMs">
    /// Of <see cref="MeanMs"/>, how much elapsed before the UI thread even built
    /// the draw. The rest is the compositor's half. Split because 88 ms of
    /// waiting has two explanations that want opposite fixes, and one number
    /// cannot choose between them.
    /// </param>
    /// <param name="ToEnqueueWorstMs">The worst single wait before hand-over.</param>
    public readonly record struct Stats(
        int Presented, int Superseded, double MeanMs, double WorstMs,
        IReadOnlyList<CohortStats>? ByCohort = null,
        int Enqueued = 0, double ToEnqueueMeanMs = 0, double ToEnqueueWorstMs = 0);

    /// <param name="Which">What arrived while these frames were waiting.</param>
    /// <param name="Count">How many frames.</param>
    /// <param name="MeanMs">Their average wait.</param>
    /// <param name="WorstMs">Their worst.</param>
    public readonly record struct CohortStats(
        Cohort Which, int Count, double MeanMs, double WorstMs);

    public Stats Snapshot
    {
        get
        {
            lock (_gate)
            {
                var cohorts = new CohortStats[3];
                for (var i = 0; i < 3; i++)
                {
                    cohorts[i] = new CohortStats(
                        (Cohort)i,
                        _cohortCount[i],
                        _cohortCount[i] == 0 ? 0 : _cohortTotal[i] / _cohortCount[i],
                        _cohortWorst[i]);
                }
                return new Stats(
                    _presented,
                    _superseded,
                    _presented == 0 ? 0 : _totalMs / _presented,
                    _worstMs,
                    cohorts,
                    _enqueuedCount,
                    _enqueuedCount == 0 ? 0 : _toEnqueueTotalMs / _enqueuedCount,
                    _toEnqueueWorstMs);
            }
        }
    }
}
