using System.Diagnostics;
using Lightbox.App.Services;

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
    private readonly Tally _wait = new();

    // B321: the wait split in two at the moment the UI thread hands the draw
    // over. A frame handed to the canvas waits 70-88 ms to be drawn in every
    // capture the owner has taken, and that single number has two completely
    // different explanations with two completely different fixes: Avalonia not
    // scheduling the visual pass (the wait is before the hand-over) or the
    // render thread being slow (after it). The report could not tell them apart
    // and neither could anyone reading it.
    private readonly Tally _toEnqueue = new();

    // And the compositor's half split once more, at the moment the render
    // thread actually picks the draw up (B321). This is the step the floor
    // verdict turns on: `then in the compositor` is a queue wait plus a draw,
    // the draw is measured, and until this existed the difference between them
    // was a subtraction with nothing to attribute it to. A queue wait whose
    // BEST case is as long as its typical one is not a queue at all — it is the
    // frame being held for the next refresh, which is a cadence and takes no fix.
    private readonly Tally _queue = new();

    // And the compositor's half split again, because "in the compositor" is a
    // queue wait plus a draw, and those are not the same finding: a draw of a
    // few milliseconds sitting inside thirty is the frame waiting for vsync,
    // twice — which is a cadence, not a cost, and wants no fix at all.
    private readonly Tally _draw = new();

    // The same draw, counted only for frames that were published. The unkeyed
    // tally above also carries the cursor repaints a hovering pen provokes, and
    // those are real draws but they are not what a published frame paid.
    private readonly Tally _keyedDraw = new();

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
                _toEnqueue.Add((now - _pending[i].Ticks) * 1000.0 / Stopwatch.Frequency);
                return;
            }
        }
    }

    /// <summary>
    /// How long the draw op itself took, on the render thread (B321).
    /// </summary>
    /// <remarks>
    /// The op already times itself for the performance monitor; this is the
    /// same figure kept where the rest of the chain lives, so a reader can see
    /// the drawing against the waiting instead of against nothing. Not keyed by
    /// sequence, because a cursor repaint draws too and the question here is
    /// what a draw costs rather than which publish paid for it.
    /// </remarks>
    public void Drew(double milliseconds)
    {
        lock (_gate)
        {
            _draw.Add(milliseconds);
        }
    }

    /// <summary>That frame has been drawn.</summary>
    /// <remarks>
    /// For a caller that cannot say when the draw began — the test helper, and
    /// nothing on the real path. It times the draw as having taken no time at
    /// all, which leaves the queue wait reading as the whole of the compositor's
    /// half; that is a visible absurdity rather than a plausible wrong number,
    /// which is the point.
    /// </remarks>
    public void Rendered(long seq) => Rendered(seq, Stopwatch.GetTimestamp());

    /// <summary>
    /// That frame has been drawn, and the draw began at
    /// <paramref name="drawStartedTicks"/> (B321).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The render thread's own start stamp, taken inside the draw op before it
    /// paints anything. Passing it in rather than sampling the clock here is
    /// what makes the three phases <em>sum</em> to the total instead of merely
    /// sitting near it — and a decomposition whose parts do not add up is how
    /// this bug's first two verdicts were written.
    /// </para>
    /// <para>
    /// The draw is also tallied unkeyed by <see cref="Drew"/>, and the two are
    /// deliberately not the same number: that one counts every draw including
    /// the cursor repaints of a hovering pen, this one counts only the draws
    /// that finished a published frame. Reading them side by side says whether
    /// a session's drawing cost is about the artwork or about the chrome.
    /// </para>
    /// </remarks>
    public void Rendered(long seq, long drawStartedTicks)
    {
        var (canvas, elsewhere) = InputPulse.Read();
        lock (_gate)
        {
            for (var i = 0; i < Tracked; i++)
            {
                if (_pending[i].Seq != seq || _pending[i].Ticks == 0) continue;

                var now = Stopwatch.GetTimestamp();
                var ms = (now - _pending[i].Ticks) * 1000.0 / Stopwatch.Frequency;
                if (now >= drawStartedTicks)
                {
                    _keyedDraw.Add((now - drawStartedTicks) * 1000.0 / Stopwatch.Frequency);
                }

                // Only when the hand-over was seen for this same frame: without
                // it the subtraction would be against a publish timestamp and
                // would silently restate the whole wait as a queue.
                if (_pending[i].Enqueued != 0 && drawStartedTicks >= _pending[i].Enqueued)
                {
                    _queue.Add((drawStartedTicks - _pending[i].Enqueued)
                        * 1000.0 / Stopwatch.Frequency);
                }

                // Canvas input wins the tie: if both arrived, the canvas one is
                // the candidate explanation and attributing the frame anywhere
                // else would hide it.
                var cohort =
                    canvas != _pending[i].Canvas ? Cohort.InputOnCanvas
                    : elsewhere != _pending[i].Elsewhere ? Cohort.InputElsewhere
                    : Cohort.Quiet;

                _pending[i] = default;
                _presented++;
                _wait.Add(ms);

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
            _wait.Reset();
            Array.Clear(_cohortCount);
            Array.Clear(_cohortTotal);
            Array.Clear(_cohortWorst);
            _toEnqueue.Reset();
            _queue.Reset();
            _draw.Reset();
            _keyedDraw.Reset();
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
    /// <param name="MedianMs">
    /// The typical wait. Printed beside the mean because a mean over a latency
    /// distribution with one stall in it describes no frame that ever happened
    /// — see <see cref="Services.Tally"/>, which was written after that mistake
    /// was made twice in a day.
    /// </param>
    /// <param name="ToEnqueueMedianMs">The typical wait before hand-over.</param>
    /// <param name="Queued">
    /// Frames whose wait between the hand-over and the start of the draw was
    /// timed (B321).
    /// </param>
    /// <param name="QueueMeanMs">
    /// Of the compositor's half, how much elapsed before the render thread
    /// began drawing. The rest is <see cref="KeyedDrawMeanMs"/>, and the two
    /// account for it exactly.
    /// </param>
    /// <param name="QueueMedianMs">The typical wait to be picked up.</param>
    /// <param name="QueueBestMs">
    /// The shortest one seen, which is the discriminator this whole split was
    /// built for: near zero means the render thread was merely busy, and a best
    /// case as long as the typical one means it was waiting for a moment that
    /// comes round on its own.
    /// </param>
    /// <param name="QueueWorstMs">The worst wait to be picked up.</param>
    /// <param name="KeyedDrawMeanMs">
    /// The draw, counted only for frames that were published — unlike
    /// <see cref="DrawMeanMs"/>, which includes the cursor repaints of a
    /// hovering pen.
    /// </param>
    public readonly record struct Stats(
        int Presented, int Superseded, double MeanMs, double WorstMs,
        IReadOnlyList<CohortStats>? ByCohort = null,
        int Enqueued = 0, double ToEnqueueMeanMs = 0, double ToEnqueueWorstMs = 0,
        int Draws = 0, double DrawMeanMs = 0, double DrawWorstMs = 0,
        double MedianMs = 0, double ToEnqueueMedianMs = 0,
        int Queued = 0, double QueueMeanMs = 0, double QueueMedianMs = 0,
        double QueueBestMs = 0, double QueueWorstMs = 0,
        double KeyedDrawMeanMs = 0);

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
                    _wait.MeanMs,
                    _wait.WorstMs,
                    cohorts,
                    (int)_toEnqueue.Count,
                    _toEnqueue.MeanMs,
                    _toEnqueue.WorstMs,
                    (int)_draw.Count,
                    _draw.MeanMs,
                    _draw.WorstMs,
                    _wait.MedianMs,
                    _toEnqueue.MedianMs,
                    (int)_queue.Count,
                    _queue.MeanMs,
                    _queue.MedianMs,
                    _queue.BestMs,
                    _queue.WorstMs,
                    _keyedDraw.MeanMs);
            }
        }
    }
}
