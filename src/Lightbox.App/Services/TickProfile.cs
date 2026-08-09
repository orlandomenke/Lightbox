using System.Diagnostics;

namespace Lightbox.App.Services;

/// <summary>
/// Where a playback tick's time actually goes.
/// </summary>
/// <remarks>
/// <para>
/// <b>The measurement that turns "the clock is late" into "the clock is late
/// because X took Y ms".</b> B150 established that the frame clock arrives late
/// and that frames reach the screen promptly, which localises the cost to the
/// tick handler and stops there. B152 then guessed <em>which part</em> of the
/// handler, filed the guess, and could not prove it — because the machine that
/// shows the symptom is not the machine the suite runs on. This is what closes
/// that gap: the next report from the artist names the phase.
/// </para>
/// <para>
/// <b>Only while playing.</b> The same code runs when an artist scrubs, and
/// mixing the two would make every number a blend of a path that has a frame
/// budget and a path that does not. The question this exists to answer is about
/// playback.
/// </para>
/// <para>
/// <b>Cheap enough to leave on.</b> One <see cref="Stopwatch.GetTimestamp"/> pair
/// and an array add per phase per tick — nanoseconds against a budget of tens of
/// milliseconds. A profiler you have to switch on is a profiler that is off when
/// the report gets written, which is the only moment it matters.
/// </para>
/// </remarks>
internal sealed class TickProfile
{
    /// <summary>The parts of a tick worth telling apart.</summary>
    /// <remarks>
    /// Named for what an artist's report would need to distinguish rather than
    /// for the call graph: each one has a different fix behind it, and a
    /// breakdown whose buckets share a remedy tells you nothing you can act on.
    /// </remarks>
    public enum Phase
    {
        /// <summary>Re-rendering the layer thumbnails (B152).</summary>
        Thumbnails,

        /// <summary>Marking which timeline cell the playhead is on.</summary>
        Highlights,

        /// <summary>Camera, reference and selection bookkeeping.</summary>
        Bookkeeping,

        /// <summary>Keeping the scratch track in step.</summary>
        Audio,

        /// <summary>Compositing the frame.</summary>
        Compose,

        /// <summary>
        /// Handing the finished frame to the canvas — the snapshot swap, the
        /// retired-image disposal, and the invalidate.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Split from <see cref="Compose"/> because one number for both was
        /// hiding which of them costs the frame (B157).</b> They were measured
        /// together as "Publish", the report showed ~35 ms/tick, and that got
        /// attributed to the CPU composite at 1080p on the strength of B125 and
        /// B144 saying compositing is expensive at that size.
        /// </para>
        /// <para>
        /// <b>Four captures on one machine refuted it.</b> A run at a 1440×810
        /// compose surface — 1.8× less area than the 1920×1080 runs beside it —
        /// spent <em>more</em> time here, 40.22 ms against 34.83/36.64/35.52. A
        /// cost that does not fall when the surface shrinks by nearly half is
        /// not paid per pixel, so whatever dominates this phase is not the
        /// composite. Two phases rather than one is what makes the next report
        /// able to say which.
        /// </para>
        /// </remarks>
        Handoff,
    }

    private static readonly int Count = Enum.GetValues<Phase>().Length;

    private readonly long[] _calls = new long[Count];
    private readonly double[] _total = new double[Count];
    private readonly double[] _worst = new double[Count];

    private int _ticks;

    /// <summary>Time one phase, and fold it in.</summary>
    /// <remarks>
    /// A returned scope rather than a start/stop pair, so a phase that returns
    /// early — and several of them do — cannot silently stop being measured.
    /// </remarks>
    public Scope Measure(Phase phase) => new(this, phase);

    /// <summary>One tick began. Counted separately so a mean per tick is honest.</summary>
    /// <remarks>
    /// Not derived from any phase's call count: a phase that is skipped on most
    /// ticks — which is the entire point of B152's fix — would otherwise make
    /// its own average look like the tick's.
    /// </remarks>
    public void Tick() => _ticks++;

    public void Reset()
    {
        Array.Clear(_calls);
        Array.Clear(_total);
        Array.Clear(_worst);
        _ticks = 0;
    }

    /// <param name="Calls">How many ticks actually ran this phase.</param>
    /// <param name="TotalMs">Time spent in it across the run.</param>
    /// <param name="WorstMs">The worst single visit.</param>
    public readonly record struct PhaseStats(Phase Phase, long Calls, double TotalMs, double WorstMs);

    public int Ticks => _ticks;

    public IReadOnlyList<PhaseStats> Snapshot()
    {
        var stats = new List<PhaseStats>(Count);
        for (var i = 0; i < Count; i++)
        {
            stats.Add(new PhaseStats((Phase)i, _calls[i], _total[i], _worst[i]));
        }
        return stats;
    }

    private void Add(Phase phase, double ms)
    {
        var i = (int)phase;
        _calls[i]++;
        _total[i] += ms;
        if (ms > _worst[i]) _worst[i] = ms;
    }

    public readonly struct Scope(TickProfile profile, Phase phase) : IDisposable
    {
        private readonly long _from = Stopwatch.GetTimestamp();

        public void Dispose() =>
            profile.Add(phase, (Stopwatch.GetTimestamp() - _from) * 1000.0 / Stopwatch.Frequency);
    }
}
