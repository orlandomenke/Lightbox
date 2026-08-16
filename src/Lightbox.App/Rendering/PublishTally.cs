namespace Lightbox.App.Rendering;

/// <summary>
/// Who published during playback, counted per calling member — B178's
/// instrument.
/// </summary>
/// <remarks>
/// <para>
/// <b>The question this answers is "why are there two publishes per tick",
/// and it is a question for a counter rather than a grep.</b> B178's capture
/// showed publishing outrunning drawing 1.5× — 757 published against 339
/// ticks over ~35 s — and that surplus is the backlog behind the 176 ms mean
/// wait and the 256 frames replaced unseen. <c>PublishSnapshot</c> has 45
/// call sites; which of them fire during playback cannot be reasoned out
/// from prose, and that entry has already been wrong once by trying (its own
/// stale B164 diagnosis). So every publish that lands while the clock is
/// running is tallied under the member that made it, and the render report
/// prints the table.
/// </para>
/// <para>
/// <b>The caller name comes from <c>CallerMemberName</c></b>, stamped by the
/// compiler at each call site — no call site changes, no reflection, no
/// stack walk, and the string is an interned literal so the per-publish cost
/// is one dictionary bump. A lambda or a property setter reports its
/// enclosing member, which is exactly the granularity a person needs to grep
/// for the site.
/// </para>
/// <para>
/// Playback only, deliberately: while drawing, publishes are supposed to
/// track the pointer, and a table mixing those in would bury the tick's
/// surplus under thousands of legitimate pointer events. Owned by the UI
/// thread, like every other render tally.
/// </para>
/// </remarks>
public sealed class PublishTally
{
    private readonly Dictionary<string, long> _byCaller = [];

    /// <summary>Publishes recorded since the last reset.</summary>
    public long Total { get; private set; }

    /// <summary>One more publish from this member.</summary>
    public void Note(string caller)
    {
        _byCaller[caller] = _byCaller.TryGetValue(caller, out var n) ? n + 1 : 1;
        Total++;
    }

    /// <summary>The table, busiest first, for the report.</summary>
    public IReadOnlyList<(string Caller, long Count)> Snapshot()
    {
        var rows = new List<(string, long)>(_byCaller.Count);
        foreach (var (caller, count) in _byCaller) rows.Add((caller, count));
        rows.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        return rows;
    }

    /// <summary>Forget everything — a new capture starts clean.</summary>
    public void Reset()
    {
        _byCaller.Clear();
        Total = 0;
    }
}
