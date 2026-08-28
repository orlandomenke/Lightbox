namespace Lightbox.App.Rendering;

/// <summary>
/// Whether a publish is made straight from the pointer event that asked for it,
/// or posted to the dispatcher first (B335).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two pacers were stacked on the live preview and only one of them was ever
/// chosen.</b> The dam is deliberate: it holds a publish until the canvas is
/// within <c>InFlightDepth</c> frames, and its depth was settled by measurement
/// (2026-08-26, one to two, cycle 35.44 → 17.16 ms). The post was not a pacer by
/// intent — it is B73's coalescing, meant to make one publish cover a burst of
/// events — but a job posted at <c>Input</c> priority behind a continuous stream
/// of pointer input is a rate limit whether or not anybody meant it as one, and
/// while it is pending <c>RequestSnapshot</c> refuses every further event at its
/// first line.
/// </para>
/// <para>
/// <b>Measured as an A/B on the owner's machine, 2026-08-28, same build, same
/// brush, same fast strokes:</b>
/// </para>
/// <list type="bullet">
/// <item>the publish cycle <b>28.84 → 22.12 ms</b>, and ink arriving
///   <b>5.4 pen events at a time → 2.0</b></item>
/// <item><c>PEN -&gt; SCREEN</c> mean <b>103.68 → 40.49 ms</b>, and — the number
///   that matters — <b>worst 479.05 → 79.15 ms</b></item>
/// <item>the refusals: <b>179</b> events turned away by a pending post, against
///   <b>0</b> once there was no post to be pending</item>
/// <item>the owner's verdict: <i>"ik kan geen happeringen meer zien"</i> — no
///   stuttering left to see, on a fault reported across four builds</item>
/// </list>
/// <para>
/// <b>And it is NOT the default, on evidence that arrived after the A/B.</b>
/// Switching it wholesale fails eight tests, and two of them are guarantees this
/// branch had no business overruling:
/// </para>
/// <list type="bullet">
/// <item><b>B73's coalescing.</b> A burst of fifteen already-queued pointer
///   events becomes fifteen full composes instead of one. The dam bounds this in
///   the running application — the owner's capture shows 111 publishes from 221
///   events, not 221 — but a canvas that has never presented is deliberately
///   never paced, so there the burst is unbounded. That is invariant 6's shape,
///   and a route that only behaves once a canvas exists is not a route.</item>
/// <item><b>B69/B89's live-equals-commit bar.</b> <c>builtin-smudge</c> and
///   <c>builtin-blender</c> stop matching their own commit — 20 px and 13 px at
///   1/255. Both sample the layer beneath, so <b>the preview of a sampling brush
///   depends on when the publish happens.</b> That is a latent defect this route
///   exposed rather than caused, filed as its own entry: it would break any
///   change to the publish cadence, not only this one.</item>
/// </list>
/// <para>
/// <b>So the arm ships opt-in and the measurement is recorded.</b> The finding
/// is not in doubt — the counter named the gate and the owner confirmed the feel
/// — but a switch that trades a reported stutter for a silently wrong preview is
/// not a fix. Rate-limiting the inline route to the refresh interval would
/// satisfy B73 and does nothing for the second one, which is about <em>when</em>
/// a publish happens rather than how often.
/// </para>
/// <para>
/// <b>Why an environment variable rather than a Configure setting.</b> It is how
/// this was settled and how it would be re-settled, the same reason
/// <c>LIGHTBOX_INFLIGHT</c> kept one. An artist cannot judge a dispatcher
/// priority, and a preference nobody can evaluate is a worse answer than a
/// measurement.
/// </para>
/// <para>
/// Read once, for the reason <c>LiveTipScale</c> gives: a capture is a whole
/// session, and an arm that could change mid-run would describe neither.
/// </para>
/// </remarks>
internal static class PublishRoute
{
    /// <summary>The environment variable that switches the arms.</summary>
    internal const string Variable = "LIGHTBOX_PUBLISH";

    /// <summary>
    /// Whether a publish is made straight from the event that asked. Off unless
    /// asked for: see the two guarantees above that a wholesale switch breaks.
    /// </summary>
    internal static readonly bool Inline = Read();

    private static bool Read()
    {
        var value = Environment.GetEnvironmentVariable(Variable);
        return value is not null
            && (value.Equals("inline", StringComparison.OrdinalIgnoreCase) || value == "1");
    }

    /// <summary>What the report calls the arm that ran.</summary>
    internal static string Describe() =>
        Inline
            ? "straight from the pointer event (" + Variable + "=inline — the dam is the"
                + " only pacer; faster, and not yet safe: see B335)"
            : "posted at Input priority (the default; " + Variable + "=inline switches arms)";
}
