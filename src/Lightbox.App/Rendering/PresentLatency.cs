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

    private readonly (long Seq, long Ticks)[] _pending = new (long, long)[Tracked];
    private readonly Lock _gate = new();

    private int _next;
    private int _presented;
    private int _superseded;
    private double _totalMs;
    private double _worstMs;

    /// <summary>A frame has been handed to the canvas.</summary>
    public void Published(long seq)
    {
        lock (_gate)
        {
            var slot = _next++ % Tracked;
            // Whatever was in this slot never got drawn — the ring is deeper
            // than the compositor's queue, so it was superseded rather than
            // still in flight.
            if (_pending[slot].Ticks != 0) _superseded++;
            _pending[slot] = (seq, Stopwatch.GetTimestamp());
        }
    }

    /// <summary>That frame has been drawn.</summary>
    public void Rendered(long seq)
    {
        lock (_gate)
        {
            for (var i = 0; i < Tracked; i++)
            {
                if (_pending[i].Seq != seq || _pending[i].Ticks == 0) continue;

                var ms = (Stopwatch.GetTimestamp() - _pending[i].Ticks)
                    * 1000.0 / Stopwatch.Frequency;
                _pending[i] = default;
                _presented++;
                _totalMs += ms;
                if (ms > _worstMs) _worstMs = ms;
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
        }
    }

    /// <param name="Presented">Frames that were published and then drawn.</param>
    /// <param name="Superseded">Frames replaced before anything drew them.</param>
    /// <param name="MeanMs">Average wait between publish and draw.</param>
    /// <param name="WorstMs">The worst single wait.</param>
    public readonly record struct Stats(
        int Presented, int Superseded, double MeanMs, double WorstMs);

    public Stats Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new Stats(
                    _presented,
                    _superseded,
                    _presented == 0 ? 0 : _totalMs / _presented,
                    _worstMs);
            }
        }
    }
}
