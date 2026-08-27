namespace Lightbox.App.Rendering;

/// <summary>
/// Whether the live tip is stamped at document resolution or at the resolution
/// it is displayed (B322).
/// </summary>
/// <remarks>
/// <para>
/// <b>The tip's cost is an area, and B322 died on it three times before anyone
/// measured the area.</b> A size-70 dab costs about <b>45-50 us</b> stamped
/// into a 3840x2160 buffer and about <b>11 us</b> stamped into the 1440x810
/// surface the artist is actually looking at — <b>4.2x</b>, measured in Release
/// on the owner's machine by <c>LiveTipDabCostTests</c>. Covering the median
/// outstanding run of a fast size-70 stroke is about <b>12.5 ms</b> at document
/// scale and <b>3.0</b> at preview scale, against a 3 ms budget: the cheaper arm
/// brings the typical fast-stroke publish inside the budget that already exists,
/// where the dearer one cannot be brought inside any budget at all.
/// </para>
/// <para>
/// <b>The absolute figures move about 15% run to run and the ratio does not</b>,
/// which is why the test asserts the ratio and prints the rest. Timings taken as
/// medians rather than minima read roughly 25% higher, and inside the full
/// four-assembly suite about fourfold higher — contention only ever adds.
/// </para>
/// <para>
/// <b>It is invariant 7's cheap side, not a breach of it.</b> The surface is
/// scaled by a canvas transform; dab coordinates are never multiplied, so
/// <c>Hash01</c> seeds every scatter, jitter and rotation from the same bits and
/// the tip is the same mark at a smaller size. <c>ComposeScale</c> on the view
/// model does exactly this for the composite already, and the frame cache keys
/// on it for the same reason.
/// </para>
/// <para>
/// <b>Why it is a flag rather than the default.</b> The tip's dabs land
/// rasterised at preview resolution beside a processed body rasterised at
/// document resolution, until the pass catches up and replaces them. Whether
/// that seam is acceptable is a question about how a mark looks, which is the
/// owner's to answer and not a thing to measure — asked 2026-08-27, answered
/// <em>show me both</em>. So both arms ship in one build and the report names
/// which one ran.
/// </para>
/// <para>
/// <b>The saving is view-dependent and the report says so.</b> At fit-to-window
/// the compose scale is 0.375 and the tip costs 2.5x less; zoomed to 100% the
/// compose scale is 1.0, there is no saving, and a fast stroke behaves exactly
/// as it does today. That is honest rather than unfortunate: the tip costs what
/// it takes to be visible at the size it is being shown.
/// </para>
/// </remarks>
internal static class LiveTipScale
{
    /// <summary>The environment variable that switches the arms.</summary>
    internal const string Variable = "LIGHTBOX_TIP_SCALE";

    /// <summary>Whether the preview-scale arm is switched on for this run.</summary>
    /// <remarks>
    /// Read once. A capture is a whole session and an arm that could change
    /// mid-run would produce a report describing neither.
    /// </remarks>
    internal static readonly bool PreviewScale = Read();

    private static bool Read()
    {
        var value = Environment.GetEnvironmentVariable(Variable);
        return value is not null
            && (value.Equals("preview", StringComparison.OrdinalIgnoreCase) || value == "1");
    }

    /// <summary>
    /// The scale to stamp the tip at, given the scale the frame is composed at.
    /// </summary>
    /// <param name="composeScale">
    /// <c>MainViewModel.ComposeScale</c> — already 1.0 whenever scaling has
    /// stopped paying, so this returns 1.0 there without a second rule.
    /// </param>
    /// <param name="previewScale">
    /// Overridden by tests; defaults to whatever the environment said.
    /// </param>
    internal static double For(double composeScale, bool? previewScale = null)
    {
        if (!(previewScale ?? PreviewScale)) return 1.0;
        // Below 1 only. A compose scale at or above 1 would make the tip buffer
        // bigger than the document, which buys nothing and costs memory.
        return composeScale > 0 && composeScale < 1.0 ? composeScale : 1.0;
    }

    /// <summary>How the render report names the arm that ran.</summary>
    internal static string Describe(double tipScale) =>
        tipScale >= 1.0
            ? $"document resolution (the default; set {Variable}=preview for the other arm)"
            : $"preview resolution, scale {tipScale:0.###} ({Variable}=preview)";
}
