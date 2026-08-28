namespace Lightbox.App.Rendering;

/// <summary>
/// Whether the live footprint — the ceiling a soft brush is capped to — is
/// accumulated at document resolution or at the resolution it is displayed
/// (B189).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every dab in the live path is stamped twice, and the second stamp costs
/// as much as the first.</b> Once as colour into the scratch and once as a
/// footprint into the coverage buffer, both document-sized: <b>44 us</b> a dab
/// against <b>43-45</b>, or <b>49-51%</b> of an event's dab work, measured in
/// Release by <c>FootprintCostsAsMuchAsTheMarkTests</c>. At the owner's compose
/// scale of 0.375 the footprint walk costs <b>11 us</b> — <b>4.1x</b> cheaper,
/// worth <b>39%</b> of an event's stamping.
/// </para>
/// <para>
/// <b>Nothing cheaper was available.</b> A footprint-only stamp does strictly
/// less work than a colour stamp — no jitter, no colour, one gradient — and
/// costs the same, so the per-dab cost is setup and geometry rather than
/// anything a micro-optimisation reaches. The area is the only lever, and this
/// is it.
/// </para>
/// <para>
/// <b>Why this buffer and not the others.</b> The live path keeps three
/// document-sized buffers. The scratch holds ink the artist is looking at; the
/// post-scratch holds the processed body they are also looking at; the coverage
/// buffer holds a number, read once per pixel to decide how far the scratch's
/// alpha may go, and never displayed. It is the only one of the three with no
/// claim on document resolution.
/// </para>
/// <para>
/// <b>It changes the preview and not the art.</b> <c>EndStroke</c> commits
/// through <c>AppendToFrameRender</c> into <c>BrushEngine.StampStroke</c>, which
/// builds its own footprint at document resolution out of the stroke record;
/// this buffer is dead the moment the pen lifts. A mark drawn under a coarse
/// ceiling settles onto the exact one at stroke end, and every save, reload,
/// re-render and export is bit-identical to what it was. That is what makes
/// this a preview trade rather than a change to what a brush is.
/// </para>
/// <para>
/// <b>Default on, decided rather than measured.</b> The cost of the trade is
/// that a soft rim is a shade softer while the pen is down — the ceiling is
/// reconstructed bilinearly from samples 2.67 document pixels apart, which
/// errs a little under the true maximum inside the falloff. Whether that is
/// worth 39% of an event's stamping is a question about what the application is
/// for, and the owner answered it on 2026-08-28: <em>live strokes are more
/// important; drawing should show what we draw</em>. So this is on, and
/// <c>LIGHTBOX_FOOTPRINT_SCALE=full</c> is the way back for a capture that
/// wants the other arm.
/// </para>
/// <para>
/// <b>The saving is view-dependent, like the tip's.</b> At fit-to-window the
/// compose scale is 0.375; zoomed to 100% it is 1.0, there is no saving, and
/// the footprint is exactly what it always was. The ceiling costs what it takes
/// to be right at the size the mark is being shown.
/// </para>
/// </remarks>
internal static class LiveFootprintScale
{
    /// <summary>The environment variable that switches the arms.</summary>
    internal const string Variable = "LIGHTBOX_FOOTPRINT_SCALE";

    /// <summary>
    /// Whether the footprint follows the compose scale on this run.
    /// </summary>
    /// <remarks>
    /// Read once. A capture is a whole session, and an arm that could change
    /// mid-run would produce a report describing neither.
    /// </remarks>
    internal static readonly bool FollowsPreview = Read();

    private static bool Read()
    {
        var value = Environment.GetEnvironmentVariable(Variable);
        if (value is null) return true;
        return !(value.Equals("full", StringComparison.OrdinalIgnoreCase)
            || value.Equals("document", StringComparison.OrdinalIgnoreCase)
            || value == "0");
    }

    /// <summary>
    /// The scale to accumulate the footprint at, given the scale the frame is
    /// composed at.
    /// </summary>
    /// <param name="composeScale">
    /// <c>MainViewModel.ComposeScale</c> — already 1.0 wherever scaling has
    /// stopped paying, so this needs no second rule for the zoomed-in case.
    /// </param>
    /// <param name="followsPreview">Overridden by tests.</param>
    internal static double For(double composeScale, bool? followsPreview = null)
    {
        if (!(followsPreview ?? FollowsPreview)) return 1.0;

        // Below 1 only: a coverage buffer bigger than the document buys nothing
        // and costs memory. And not arbitrarily small — the ceiling has to
        // resolve a soft brush's falloff, which is about a tenth of the brush
        // size, so a scale that put the whole falloff inside one buffer pixel
        // would cap the rim to a single flat number and read as a hard edge.
        if (composeScale <= 0 || composeScale >= 1.0) return 1.0;
        return Math.Max(0.2, composeScale);
    }

    /// <summary>How the render report names the arm that ran.</summary>
    internal static string Describe(double scale) =>
        scale >= 1.0
            ? $"document resolution (set {Variable}=full to pin it there)"
            : $"preview resolution, scale {scale:0.###} ({Variable}=full for the other arm)";
}
