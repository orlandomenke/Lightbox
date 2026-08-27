namespace Lightbox.App.Rendering;

/// <summary>
/// Which dabs, if any, are worth drawing raw ahead of the live post-process
/// pass — decided as arithmetic, before anything is stamped (B322).
/// </summary>
/// <remarks>
/// <para>
/// <b>A separate, pure function because the fourth attempt at B322 died on
/// exactly this decision and had it buried in the code that acted on it.</b>
/// That attempt drew "every dab since the last completed pass" on the entry's
/// own measurement that the gap is about nine events. On the owner's machine the
/// gap was <b>10 dabs rendered out of 1263</b>, so it restamped 99% of the mark
/// on every publish: building each frame went 2.41 → 23.06 ms median, describing
/// it 13.47 → 215, pen to screen 62.95 → 991. That is invariant 6 — painting is
/// bounded work — broken outright.
/// </para>
/// <para>
/// <b>And it was self-amplifying, which is why a smaller constant would not have
/// saved it.</b> A bigger tip slows the publish, which starves the worker that
/// would have shrunk the tip, which leaves more dabs outstanding. Any rule of
/// the form "draw what is outstanding" feeds that loop. The rule has to be
/// "draw what is outstanding <em>only while that is small</em>, and otherwise
/// draw nothing at all".
/// </para>
/// <para>
/// <b>Falling back to nothing is deliberate and is not a cop-out.</b> When the
/// pass has fallen a long way behind, the machine is already struggling, and
/// that is the worst possible moment to add work proportional to the mark. The
/// artist gets today's behaviour — the tip missing — which is a known defect
/// rather than an unusable application. Partial tips were rejected for a
/// different reason: drawing only the newest N of 1253 outstanding dabs leaves
/// the mark between them and the processed body missing, which reads as a break
/// in the middle of the stroke rather than a short tail.
/// </para>
/// </remarks>
internal static class LiveTipPlan
{
    /// <summary>
    /// The most dabs that will ever be restamped for one publish.
    /// </summary>
    /// <remarks>
    /// <b>A starting value chosen to be safe rather than measured, and the
    /// report says what it should be.</b> Nothing in the ledger records how many
    /// dabs are typically outstanding when the pass is keeping up — the entry's
    /// "about nine events" is in events, and a fast fine-spaced stroke puts many
    /// dabs in an event. So the render report prints how often the tip was shown,
    /// how often this budget refused it, and the median range, and this constant
    /// moves once a capture says where the line actually sits. Guessing it and
    /// leaving it unmeasured is how the fourth attempt's nine-events assumption
    /// survived long enough to reach a person.
    /// </remarks>
    public const int MaxDabs = 128;

    /// <param name="From">First dab the pass has not seen.</param>
    /// <param name="To">One past the last dab stamped.</param>
    public readonly record struct Range(int From, int To)
    {
        public int Count => To - From;
    }

    /// <summary>Why no tip is being drawn, for the report to count.</summary>
    public enum Skip
    {
        /// <summary>A tip is being drawn.</summary>
        None = 0,

        /// <summary>No pass has completed, so the raw scratch is already the whole mark.</summary>
        NoPassYet,

        /// <summary>The pass has caught up; there is nothing outstanding.</summary>
        NothingOutstanding,

        /// <summary>Too far behind to draw within the budget — see the remarks above.</summary>
        TooFarBehind,
    }

    /// <summary>
    /// The dabs to draw raw, where to start stamping them, or null with a
    /// reason.
    /// </summary>
    /// <param name="postStampedCount">Dabs the last completed pass had processed.</param>
    /// <param name="dabCount">Dabs the stroke has now.</param>
    /// <param name="tipFrom">Where the tip's existing contents begin, or -1.</param>
    /// <param name="tipStampedTo">Where they run to, or -1.</param>
    /// <remarks>
    /// <para>
    /// <b>The budget bounds the WORK, not the outstanding run — and getting that
    /// wrong is why the fifth and sixth attempts both left fast strokes without
    /// a preview.</b> While the tip was rebuilt every publish the two were the
    /// same number, so refusing on the outstanding run was refusing on the work.
    /// Attempt 6 stamps only what arrived since the last publish, which on the
    /// owner's capture was 16 dabs against 57 outstanding — and the budget went
    /// on refusing 86 publishes of 186 on the strength of a cost that no longer
    /// existed. Those refusals are exactly the fast strokes.
    /// </para>
    /// <para>
    /// So the question is what THIS publish would stamp: the delta when the tip
    /// can be added to, the whole outstanding run when a completed pass has
    /// invalidated it. A rebuild is expensive only when the pass advances while
    /// a great deal has been drawn since, which is rare and is what the budget
    /// is for.
    /// </para>
    /// </remarks>
    public static (Range? Range, int StampFrom, Skip Why, int Outstanding) For(
        int postStampedCount, int dabCount, int tipFrom = -1, int tipStampedTo = -1)
    {
        // Below zero means no pass has landed. OverlayFor shows the raw scratch
        // in that case, which already carries every dab, so a tip would be the
        // same ink drawn twice.
        if (postStampedCount <= 0) return (null, 0, Skip.NoPassYet, 0);

        var outstanding = dabCount - postStampedCount;
        if (outstanding <= 0) return (null, 0, Skip.NothingOutstanding, 0);

        // Can the buffer be added to? Only if it starts where the pass now does
        // and has not run past what the stroke holds.
        var canAdd = tipFrom == postStampedCount
            && tipStampedTo >= postStampedCount
            && tipStampedTo <= dabCount;

        var stampFrom = canAdd ? tipStampedTo : postStampedCount;
        var work = dabCount - stampFrom;
        if (work > MaxDabs) return (null, 0, Skip.TooFarBehind, outstanding);

        return (new Range(postStampedCount, dabCount), stampFrom, Skip.None, outstanding);
    }
}
