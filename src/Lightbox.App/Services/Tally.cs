namespace Lightbox.App.Services;

/// <summary>
/// A session's worth of one timing, kept so the report can say what a
/// <em>typical</em> one costs and not only what they averaged.
/// </summary>
/// <remarks>
/// <para>
/// <b>Written after a mean lied twice in one day, both times persuasively.</b>
/// A capture of 2026-08-26 read <c>building each frame 8.27 ms</c> with
/// <c>describing it 5.34 ms</c>, and the report concluded "describing the frame
/// is 65% of the build, so the cost is in the pass list, the stack fold or the
/// cel fetches". It is not: the same session recorded a single build of
/// <b>2062.57 ms</b>, and over 381 publishes that one stall is 5.4 ms of mean —
/// the entire figure. The true cost is 0.16 ms. A second capture minutes later
/// showed exactly that.
/// </para>
/// <para>
/// A mean over a latency distribution with stalls in it describes no frame that
/// ever happened. The median does, so both are printed and the report says
/// which to believe when they disagree. <b>The worst is kept too</b> and is not
/// redundant: B314 was found because a mean of 7.5 ms sat beside a worst of
/// 3,126 ms, and nobody experiences an average.
/// </para>
/// <para>
/// <b>Bounded, because this runs per publish for the life of a session.</b>
/// Above <see cref="Capacity"/> samples it keeps a uniform random subset
/// (Vitter's reservoir), so the median stays an unbiased estimate of the whole
/// session rather than of its first few seconds — which a simple "stop
/// recording when full" would give, and which would hide exactly the late-session
/// degradation this is read to find.
/// </para>
/// </remarks>
internal sealed class Tally
{
    /// <summary>
    /// Samples kept. Eight thousand is minutes of drawing at the measured
    /// publish rate and about 64 KB — small enough not to think about, large
    /// enough that the median is not an estimate anybody need distrust.
    /// </summary>
    private const int Capacity = 8192;

    private readonly double[] _samples = new double[Capacity];
    private readonly Random _rng = new(20260826);
    private int _kept;

    /// <summary>How many were recorded, not how many were kept.</summary>
    public long Count { get; private set; }

    /// <summary>Total of everything recorded, so the mean stays exact.</summary>
    public double TotalMs { get; private set; }

    /// <summary>The single worst, which no summary statistic can stand in for.</summary>
    public double WorstMs { get; private set; }

    public double MeanMs => Count == 0 ? 0 : TotalMs / Count;

    public void Add(double ms)
    {
        if (double.IsNaN(ms) || double.IsInfinity(ms)) return;
        Count++;
        TotalMs += ms;
        if (ms > WorstMs) WorstMs = ms;

        if (_kept < Capacity)
        {
            _samples[_kept++] = ms;
            return;
        }
        // Reservoir: the nth sample replaces a random earlier one with
        // probability Capacity/n, which leaves every sample of the session
        // equally likely to be held.
        var slot = (long)(_rng.NextDouble() * Count);
        if (slot < Capacity) _samples[(int)slot] = ms;
    }

    /// <summary>
    /// The middle sample — what a typical one cost. Zero when nothing has been
    /// recorded, which callers must treat as "no measurement" rather than "fast".
    /// </summary>
    public double MedianMs
    {
        get
        {
            if (_kept == 0) return 0;
            var copy = new double[_kept];
            Array.Copy(_samples, copy, _kept);
            Array.Sort(copy);
            return _kept % 2 == 1
                ? copy[_kept / 2]
                : (copy[(_kept / 2) - 1] + copy[_kept / 2]) / 2;
        }
    }

    /// <summary>
    /// Whether the mean is being pulled somewhere no sample went — the signal
    /// that a stall is doing the talking rather than the typical frame.
    /// </summary>
    /// <remarks>
    /// Twice the median is the line. Below it the two agree closely enough that
    /// either can be quoted; above it, quoting the mean is quoting an event
    /// rather than a cost, and the report says so instead of leaving the reader
    /// to notice.
    /// </remarks>
    public bool MeanIsDistorted => Count >= 20 && MedianMs > 0 && MeanMs > MedianMs * 2;

    public void Reset()
    {
        Count = 0;
        TotalMs = 0;
        WorstMs = 0;
        _kept = 0;
    }
}
