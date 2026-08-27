using System.Diagnostics;

namespace Lightbox.App.Rendering;

/// <summary>
/// How long the ink takes to follow the pen: pointer event → stamp → publish →
/// drawn, measured on the machine where drawing feels slow.
/// </summary>
/// <remarks>
/// <para>
/// <b>B189, and why the existing numbers cannot see it.</b> Every budget the
/// suite owns times one piece in isolation — the stamp is milliseconds, the
/// publish is milliseconds, and drawing still trails the pen. The lag an artist
/// feels is the <em>sum plus the waiting between the pieces</em>, and the
/// waiting only exists on a real dispatcher under real input, which the
/// headless suite deliberately does not have (the same reason
/// <c>PlaybackClock</c> gives for not measuring its own magnitude in tests).
/// So this instrument rides along in the shipped build, like
/// <see cref="PresentLatency"/> does for playback, and the render report prints
/// what it saw.
/// </para>
/// <para>
/// <b>The chain it measures, and the one seam it cannot.</b> An event is clocked
/// when <c>MoveStrokeBatch</c> receives it, again when the live stamp for it has
/// run, again when the next snapshot is handed to the canvas, and finally when
/// the render thread draws that snapshot. Queueing in the operating system or
/// the tablet driver <em>before</em> the event reaches the application is
/// invisible from in here, and the report says so rather than letting the total
/// pass for the whole story.
/// </para>
/// <para>
/// <b>Coalescing is the point, not noise to be averaged away.</b> One publish
/// covers a burst of events by design (B73), so latency is anchored to the
/// <em>oldest</em> event a publish carries — that is the mark furthest behind
/// the pen, which is the one the artist is looking at. Events per publish is
/// printed too: a healthy stroke coalesces a little, and a ratio that climbs is
/// publishes falling behind input.
/// </para>
/// <para>
/// <b>The newest event is anchored beside the oldest, because one number was
/// carrying two meanings.</b> B189's second capture read <em>worse</em> after
/// the pacing landed — 89.8 ms event → publish — and a third of that was
/// arithmetic, not lag: pacing coalesces more events per publish (4.7 → 11.4),
/// and eleven events gathered over ~90 ms make the oldest old even when the
/// pen tip is fresh. The oldest anchor is coalescing depth <em>times</em>
/// staleness; the newest is staleness alone — how far the tip itself runs
/// behind the hand. Reading them together is what tells "publishes are
/// falling behind" apart from "publishes are batching well".
/// </para>
/// <para>
/// <b>Two threads touch this</b> — events, stamps and publishes on the UI
/// thread, draws on the render thread — so everything is behind one lock, taken
/// a handful of times per pointer event, which at hand speed is nothing.
/// </para>
/// </remarks>
internal sealed class StrokeToScreen
{
    /// <summary>The app-wide instance the report reads. Tests build their own.</summary>
    public static StrokeToScreen Shared { get; } = new();

    /// <summary>
    /// How many recent ink-carrying publishes to keep waiting for their draw.
    /// A ring for <see cref="PresentLatency"/>'s reason: the interesting case is
    /// a publish superseded before anything drew it, and a dictionary would grow
    /// by one entry for every one of those.
    /// </summary>
    public const int Tracked = 16;

    private readonly (long Seq, long EventTicks, long NewestTicks, long PublishTicks, int Events)[]
        _pending = new (long, long, long, long, int)[Tracked];

    private readonly Lock _gate = new();

    private int _next;

    // The oldest and newest events stamped since the last ink-carrying
    // publish, and how many the next publish will cover. Zero oldest ticks
    // means nothing is waiting.
    private long _oldestTicks;
    private long _newestTicks;
    private int _batched;

    private int _events;
    private int _publishes;
    private int _drawn;
    private int _superseded;

    // Sum and worst per segment. Counts differ: stamps are per event, the
    // waits are per publish, and pen→screen only exists for drawn frames.
    // B330: a Tally each, rather than a total and a worst. Every one of these
    // phases is a latency distribution with stalls in it, and a mean over one of
    // those describes no event that ever happened — which is the whole reason
    // `Tally` exists, and it had been applied everywhere except the chain an
    // artist's lag is actually read from.
    private readonly Services.Tally _stamp = new();
    private readonly Services.Tally _toPublish = new();
    private readonly Services.Tally _tipToPublish = new();
    private readonly Services.Tally _toDraw = new();
    private readonly Services.Tally _total = new();
    private readonly Services.Tally _tip = new();

    /// <summary>A pointer event has reached the stroke handler. UI thread.</summary>
    /// <returns>The timestamp to hand back to <see cref="Stamped"/>.</returns>
    public static long EventArrived() => Stopwatch.GetTimestamp();

    /// <summary>The live stamp for that event has run. UI thread.</summary>
    public void Stamped(long eventTicks)
    {
        var ms = Ms(Stopwatch.GetTimestamp() - eventTicks);
        lock (_gate)
        {
            _events++;
            _stamp.Add(ms);
            // The oldest waiting event wins: it is the mark furthest behind
            // the pen, so it is the honest anchor for the publish's latency.
            // The newest is kept beside it — staleness without the coalescing
            // term — so the two stop sharing one number (see the remarks).
            if (_oldestTicks == 0) _oldestTicks = eventTicks;
            _newestTicks = eventTicks;
            _batched++;
        }
    }

    /// <summary>
    /// A snapshot has been handed to the canvas. Attaches the waiting events to
    /// it; a publish with no stroke events pending records nothing, which is
    /// what keeps playback and thumbnails out of these numbers. UI thread.
    /// </summary>
    public void Published(long seq)
    {
        lock (_gate)
        {
            if (_oldestTicks == 0) return;

            var now = Stopwatch.GetTimestamp();
            var wait = Ms(now - _oldestTicks);
            var tipWait = Ms(now - _newestTicks);

            var slot = _next++ % Tracked;
            // Whatever this overwrites was replaced before anything drew it —
            // ink that never reached the screen at all.
            if (_pending[slot].EventTicks != 0) _superseded++;
            _pending[slot] = (seq, _oldestTicks, _newestTicks, now, _batched);

            _publishes++;
            _toPublish.Add(wait);
            _tipToPublish.Add(tipWait);

            _oldestTicks = 0;
            _newestTicks = 0;
            _batched = 0;
        }
    }

    /// <summary>That snapshot has been drawn. Render thread.</summary>
    public void Rendered(long seq)
    {
        var now = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            for (var i = 0; i < Tracked; i++)
            {
                if (_pending[i].Seq != seq || _pending[i].EventTicks == 0) continue;

                var draw = Ms(now - _pending[i].PublishTicks);
                var total = Ms(now - _pending[i].EventTicks);
                var tip = Ms(now - _pending[i].NewestTicks);
                _pending[i] = default;

                _drawn++;
                _toDraw.Add(draw);
                _total.Add(total);
                _tip.Add(tip);
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
            _oldestTicks = 0;
            _newestTicks = 0;
            _batched = 0;
            _events = 0;
            _publishes = 0;
            _drawn = 0;
            _superseded = 0;
            _stamp.Reset();
            _toPublish.Reset();
            _tipToPublish.Reset();
            _toDraw.Reset();
            _total.Reset();
            _tip.Reset();
        }
    }

    /// <param name="Count">How many times this segment was measured.</param>
    /// <param name="MeanMs">Its average.</param>
    /// <param name="WorstMs">Its worst single case.</param>
    /// <param name="MedianMs">
    /// What a typical one cost (B330). Printed beside the mean because a mean
    /// over a latency distribution with stalls in it describes no event that
    /// ever happened — the report already said so about the frame build, which
    /// had a median, while every phase of the pen-to-screen chain had only a
    /// mean and could therefore name the wrong one.
    /// </param>
    /// <param name="MeanIsDistorted">
    /// Whether the mean is more than twice the median, i.e. whether quoting it
    /// is quoting an event rather than a cost.
    /// </param>
    public readonly record struct Segment(
        int Count, double MeanMs, double WorstMs,
        double MedianMs = 0, bool MeanIsDistorted = false);

    /// <param name="Events">Pointer events stamped during live strokes.</param>
    /// <param name="Publishes">Publishes that carried fresh ink.</param>
    /// <param name="Drawn">Of those, how many were actually drawn.</param>
    /// <param name="Superseded">Replaced before anything drew them.</param>
    /// <param name="Stamp">Event arrival → live stamp done, per event.</param>
    /// <param name="WaitToPublish">Oldest event → its publish, per publish.</param>
    /// <param name="WaitToDraw">Publish → drawn, per drawn frame.</param>
    /// <param name="PenToScreen">Oldest event → drawn — the artist's number.</param>
    /// <param name="TipToPublish">
    /// Newest event → its publish, per publish. Staleness without the
    /// coalescing term: how long the freshest ink waited, however many events
    /// the publish gathered.
    /// </param>
    /// <param name="TipToScreen">
    /// Newest event → drawn. Read against <paramref name="PenToScreen"/>: a
    /// wide gap between them is deep coalescing, not deep lag.
    /// </param>
    public readonly record struct Stats(
        int Events, int Publishes, int Drawn, int Superseded,
        Segment Stamp, Segment WaitToPublish, Segment WaitToDraw, Segment PenToScreen,
        Segment TipToPublish = default, Segment TipToScreen = default);

    public Stats Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new Stats(
                    _events, _publishes, _drawn, _superseded,
                    Seg(_events, _stamp),
                    Seg(_publishes, _toPublish),
                    Seg(_drawn, _toDraw),
                    Seg(_drawn, _total),
                    Seg(_publishes, _tipToPublish),
                    Seg(_drawn, _tip));
            }
        }
    }

    /// <summary>
    /// One phase's numbers. <paramref name="count"/> stays the caller's own —
    /// events, publishes or drawn frames — because a phase can be sampled fewer
    /// times than it happened and the report's denominators are about the chain
    /// rather than about the tally.
    /// </summary>
    private static Segment Seg(int count, Services.Tally t) =>
        new(count, t.MeanMs, t.WorstMs, t.MedianMs, t.MeanIsDistorted);

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}
