namespace Lightbox.Core.Export;

/// <summary>
/// Turning Lightbox coordinates and timings into Unreal's Paper2D equivalents.
/// </summary>
/// <remarks>
/// <para>
/// Three conversions, and the first is the one that ruins an afternoon.
/// </para>
/// <para>
/// <b>An Unreal unit is a centimetre, and Unity's is a metre.</b> So the same
/// "how tall is this character in the world" figure that gives a correct
/// <c>pixelsPerUnit</c> for Unity is a hundred times wrong for Unreal, and it is wrong
/// in the direction that produces a character the size of a coin rather than an error.
/// This is why <see cref="PixelsPerUnrealUnit"/> exists beside
/// <see cref="UnityConvert.PixelsPerUnit"/> instead of the exporter reusing it.
/// </para>
/// <para>
/// <b><c>PaperFlipbookKeyFrame.frame_run</c> is a count of frames, not a time.</b> It
/// says how many of the flipbook's own frames this drawing is held for, so it carries
/// the same unit trap as Godot's duration multiplier and one extra: it is an
/// <em>integer</em>, so a hold that is not a whole number of frames has to round — and
/// rounding to zero removes the drawing from the animation.
/// </para>
/// <para>
/// <b><c>PaperSprite.custom_pivot_point</c> is relative to the sprite's own rectangle,
/// in texture pixels, Y down.</b> That is neither Unity's normalised pivot nor Godot's
/// offset-from-the-centre, which makes it the one somebody converts twice.
/// </para>
/// </remarks>
public static class UnrealConvert
{
    /// <summary>Unreal units in a metre. Unreal's unit is a centimetre.</summary>
    /// <remarks>
    /// Named rather than written as 100 at the point of use, because the whole hazard
    /// here is a factor of a hundred that looks like a typo when you meet it inline.
    /// </remarks>
    public const double UnrealUnitsPerMetre = 100;

    /// <summary>
    /// Unreal's <c>pixels_per_unreal_unit</c> for a canvas height and a world height
    /// given in <em>metres</em>.
    /// </summary>
    /// <param name="canvasHeightPx">The document height in pixels.</param>
    /// <param name="worldHeightMetres">
    /// How tall the drawing should be in the world, in metres — the same figure the
    /// Unity export takes, so a preset can serve both.
    /// </param>
    /// <remarks>
    /// <para>
    /// Unreal states the setting as "0.64 would make a 64 pixel wide sprite take up
    /// 100 cm", so it is pixels divided by <em>Unreal units</em>, and an Unreal unit is
    /// a centimetre. A 128-pixel character meant to stand 1.8 m tall is therefore
    /// 128 / 180 ≈ 0.71, where Unity's figure for the same intent is 128 / 1.8 ≈ 71.
    /// </para>
    /// <para>
    /// Feeding Unity's number to Unreal makes the sprite a hundred times smaller than
    /// asked — 1.8 cm rather than 1.8 m — which reads as "Paper2D is broken" rather
    /// than as a unit mistake. <c>UnrealConvertTests</c> asserts the factor of a
    /// hundred against <see cref="UnityConvert.PixelsPerUnit"/> so the two cannot
    /// quietly converge.
    /// </para>
    /// </remarks>
    public static double PixelsPerUnrealUnit(int canvasHeightPx, double worldHeightMetres) =>
        worldHeightMetres <= 0
            ? 1
            : Math.Max(1, canvasHeightPx) / (worldHeightMetres * UnrealUnitsPerMetre);

    /// <summary>
    /// A frame's exposure as <c>PaperFlipbookKeyFrame.frame_run</c>: how many of the
    /// flipbook's frames this drawing is held for.
    /// </summary>
    /// <param name="frameMilliseconds">This frame's duration, from the sidecar.</param>
    /// <param name="fps">The flipbook's <c>frames_per_second</c>.</param>
    /// <remarks>
    /// <para>
    /// 1 for an ordinary frame, 2 for one held on 2s. Rounded to a whole number of
    /// ticks for the same reason Godot's multiplier is: the sidecar stores integer
    /// milliseconds, so at 12 fps a one-tick frame arrives as 83 where the truth is
    /// 83.33, and dividing straight through gives 0.996 — which truncates to <b>zero</b>.
    /// </para>
    /// <para>
    /// A <c>frame_run</c> of zero is a drawing that never shows. That is the failure
    /// this floor at 1 exists to prevent, and it is worse than a wrong duration because
    /// the frame is simply missing from the animation.
    /// </para>
    /// </remarks>
    public static int FrameRun(int frameMilliseconds, int fps)
    {
        if (frameMilliseconds <= 0) return 1;
        var tick = 1000.0 / Math.Max(1, fps);
        return Math.Max(1, (int)Math.Round(frameMilliseconds / tick, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// A pivot as <c>PaperSprite.custom_pivot_point</c>: texture pixels from the
    /// sprite rectangle's top-left, Y down.
    /// </summary>
    /// <param name="pivotX">Pivot in document pixels.</param>
    /// <param name="pivotY">Pivot in document pixels, from the top.</param>
    /// <param name="cellLeft">The sprite's left edge in document space.</param>
    /// <param name="cellTop">The sprite's top edge in document space.</param>
    /// <remarks>
    /// The plainest of the three engines' pivots and therefore the most dangerous: it
    /// is not normalised the way Unity's is, and not measured from the centre the way
    /// Godot's is, so either of those conversions applied here compiles, exports, and
    /// puts every character's feet somewhere else. Tested against both wrong answers.
    /// </remarks>
    public static (double X, double Y) PivotPoint(
        double pivotX, double pivotY, int cellLeft, int cellTop) =>
        (pivotX - cellLeft, pivotY - cellTop);

    /// <summary>
    /// A name as an Unreal asset name.
    /// </summary>
    /// <remarks>
    /// Unreal object names reject spaces and most punctuation, and an import that trips
    /// over one fails per-asset in the log rather than stopping — so a sheet named
    /// "Hero (final)" would half-import. Letters, digits and underscores are kept;
    /// everything else becomes an underscore, runs collapse, and an empty result falls
    /// back rather than producing an asset called nothing.
    /// </remarks>
    public static string AssetName(string? name, string fallback = "Sprite")
    {
        if (string.IsNullOrWhiteSpace(name)) return fallback;

        var built = new System.Text.StringBuilder(name.Length);
        foreach (var c in name.Trim())
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                built.Append(c);
            }
            else if (built.Length > 0 && built[^1] != '_')
            {
                built.Append('_');
            }
        }

        var result = built.ToString().Trim('_');
        return result.Length == 0 ? fallback : result;
    }
}
