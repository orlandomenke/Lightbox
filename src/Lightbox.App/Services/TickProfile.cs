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

        /// <summary>Compositing the frame and handing it to the canvas.</summary>
        Publish,
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
