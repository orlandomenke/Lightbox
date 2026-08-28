namespace Lightbox.App.Rendering;

/// <summary>
/// Whether a publish is posted to the dispatcher or made straight from the
/// pointer event that asked for it (B178).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two pacers are stacked on the live preview and only one of them was
/// chosen.</b> The dam is deliberate: it holds a publish until the canvas is
/// within <c>InFlightDepth</c> frames, and its depth was settled by measurement
/// (2026-08-26, one to two, cycle 35.44 → 17.16 ms). The post is not a pacer at
/// all by intent — it is B73's coalescing, meant to make one publish cover a
/// burst of events — but a job posted at <c>Input</c> priority behind a
/// continuous stream of pointer input is a rate limit whether or not anybody
/// meant it as one, and while it is pending every further event is refused.
/// </para>
/// <para>
/// <b>What the arms are for.</b> The owner's capture of 2026-08-28 has a cycle
/// of <b>29.26 ms</b> against a pen delivering every <b>5.68</b>, a
/// <c>publish -&gt; drawn</c> of <b>28.59</b> sitting at B321's floor, and a
/// stamp the same report calls cheap. Those three are consistent with the dam
/// pacing at a depth of one — which it is not set to — and equally consistent
/// with the post being the thing that only comes round once a round trip. The
/// counters beside this name the gate; the arms let one capture check the
/// answer rather than only state it.
/// </para>
/// <para>
/// <b>The inline arm is not obviously better and is not the default.</b> It
/// hands the pacing entirely to the dam, which is the one that was chosen on
/// evidence — but it also gives up the coalescing, so every pointer event that
/// the dam lets through builds a frame. B189 measured what that costs when it
/// goes wrong: 935 of 1921 publishes replaced before anything drew them, at
/// ~27 ms of UI thread each. A build is 1.25 ms now rather than 27, which is
/// why the arm is worth trying at all, and <c>replaced before drawing</c> in
/// the render report is the number that would refuse it.
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

    /// <summary>Whether a publish is made straight from the event that asked.</summary>
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
            ? "straight from the pointer event (LIGHTBOX_PUBLISH=inline — the dam is the only pacer)"
            : "posted at Input priority (the default; LIGHTBOX_PUBLISH=inline switches arms)";
}
