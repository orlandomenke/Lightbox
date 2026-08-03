namespace Lightbox.Core.Export;

/// <summary>
/// Turning Lightbox coordinates into Unity's.
/// </summary>
/// <remarks>
/// <para>
/// P5g, and the reason it is a separate tested class rather than three lines in a
/// C# file we ship into somebody's Unity project: <b>a silently flipped pivot looks
/// like an animation problem and gets debugged as one.</b> The character sinks into
/// the floor, or the weapon hangs off the wrong hand, and nothing points at a
/// coordinate convention. So the conversion happens here, where it can be tested
/// with worked examples, and the *result* goes in the sidecar — the importer script
/// reads a number rather than computing one.
/// </para>
/// <para>
/// Three differences at once, which is why this is easy to get wrong:
/// </para>
/// <list type="number">
/// <item><b>Origin.</b> Lightbox measures from the top-left, Unity from the
/// bottom-left.</item>
/// <item><b>Direction.</b> Y grows downward here and upward there.</item>
/// <item><b>Scale.</b> Ours is in pixels; Unity's sprite pivot is normalised 0..1
/// — and normalised <em>within the sprite's own rect</em>, not within the
/// canvas.</item>
/// </list>
/// <para>
/// That last one is the subtle one. A pivot outside the trimmed rect normalises
/// outside 0..1, which Unity accepts and which is correct: a character whose
/// ground point sits below their lowest ink has a pivot at y &lt; 0, and clamping it
/// into the rect would move the character. So <b>nothing is clamped here</b>.
/// </para>
/// </remarks>
public static class UnityConvert
{
    /// <summary>
    /// A pixel position in document space, as Unity's normalised sprite pivot.
    /// </summary>
    /// <param name="x">Document X, in pixels, measured from the left.</param>
    /// <param name="y">Document Y, in pixels, measured from the <b>top</b>.</param>
    /// <param name="cellLeft">The sprite's left edge in document space.</param>
    /// <param name="cellTop">The sprite's top edge in document space.</param>
    /// <param name="cellWidth">The sprite's width. Below 1 is treated as 1.</param>
    /// <param name="cellHeight">The sprite's height. Below 1 is treated as 1.</param>
    /// <returns>
    /// Normalised, bottom-left origin, Y up. Not clamped to 0..1 — see the remarks
    /// on the class.
    /// </returns>
    public static (double X, double Y) Pivot(
        double x, double y, int cellLeft, int cellTop, int cellWidth, int cellHeight)
    {
        var w = Math.Max(1, cellWidth);
        var h = Math.Max(1, cellHeight);

        // Into the cell first, then normalise, then flip. Flipping before
        // normalising is the mistake that produces a pivot that looks right on a
        // square sprite and wrong on every other one.
        var withinX = x - cellLeft;
        var withinY = y - cellTop;

        return (withinX / w, 1.0 - withinY / h);
    }

    /// <summary>
    /// A frame's duration in seconds, from the scene's frame rate.
    /// </summary>
    /// <remarks>
    /// Unity's <c>AnimationClip</c> places keyframes in seconds, so this is what
    /// the importer needs — and getting it from fps rather than from a per-frame
    /// millisecond figure keeps a clip's timing exact instead of accumulating the
    /// rounding the sidecar's integer <c>duration</c> carries. At 12 fps that is
    /// 83 ms per frame written to the file and 1/12 used to build the clip; over a
    /// hundred frames the difference is a third of a frame.
    /// </remarks>
    public static double SecondsPerFrame(int fps) => 1.0 / Math.Max(1, fps);

    /// <summary>
    /// Unity's sprite pixels-per-unit for a given canvas height and world height.
    /// </summary>
    /// <remarks>
    /// Offered rather than assumed. A 2D game decides how many pixels make a world
    /// unit and it is a project-wide decision, so the exporter takes it as a
    /// number and does the arithmetic instead of picking Unity's default 100 and
    /// leaving somebody to wonder why their character is nine units tall.
    /// </remarks>
    public static double PixelsPerUnit(int canvasHeightPx, double worldHeightUnits) =>
        worldHeightUnits <= 0 ? 100 : Math.Max(1, canvasHeightPx) / worldHeightUnits;
}
