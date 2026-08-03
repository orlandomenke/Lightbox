namespace Lightbox.Core.Export;

/// <summary>
/// Turning Lightbox coordinates and timings into Godot's.
/// </summary>
/// <remarks>
/// <para>
/// Two conversions, and both are the kind that fails silently.
/// </para>
/// <para>
/// <b>Godot's <c>SpriteFrames.add_frame</c> duration is a multiplier, not seconds.</b>
/// An animation has one <c>speed</c> in frames per second, and each frame's duration
/// scales it — so a frame with duration 2.0 lasts twice as long as the speed implies.
/// Passing milliseconds there would produce an animation running thousands of times
/// too slowly, which reads as "the importer hung" rather than as a unit mistake.
/// </para>
/// <para>
/// <b><c>Sprite2D.offset</c> is measured from the region's centre, and Godot's 2D Y
/// runs down like ours.</b> So this conversion is simpler than Unity's — no flip —
/// which is exactly why it deserves a test: a conversion with nothing to flip is one
/// somebody will "simplify" into the wrong sign later.
/// </para>
/// </remarks>
public static class GodotConvert
{
    /// <summary>
    /// A frame's duration as Godot's relative multiplier.
    /// </summary>
    /// <param name="frameMilliseconds">This frame's duration, from the sidecar.</param>
    /// <param name="fps">The animation's speed, which the multiplier is relative to.</param>
    /// <remarks>
    /// <para>
    /// 1.0 for a frame held for exactly one tick of the animation's speed, which is the
    /// ordinary case. A frame held on 2s at 12 fps comes out at 2.0.
    /// </para>
    /// <para>
    /// <b>Rounded to a whole number of ticks, and that is a correction rather than a
    /// convenience.</b> The sidecar stores durations as integer milliseconds, so at 12 fps
    /// a one-tick frame is written as 83 where the true figure is 83.33 — and dividing
    /// straight through gives 0.996, not 1. A hold is by definition an integer number of
    /// frames, so rounding recovers exactly the value the millisecond figure was rounded
    /// from instead of passing a 0.4% error into every frame of the animation. Two of this
    /// method's own tests failed on that difference before it was rounded, which is how
    /// it was found.
    /// </para>
    /// </remarks>
    public static double FrameDuration(int frameMilliseconds, int fps)
    {
        var rate = Math.Max(1, fps);
        var tick = 1000.0 / rate;
        if (frameMilliseconds <= 0) return 1.0;
        return Math.Max(1, Math.Round(frameMilliseconds / tick, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// A pivot as <c>Sprite2D.offset</c>: pixels from the region's centre, Y down.
    /// </summary>
    /// <param name="pivotX">Pivot in document pixels.</param>
    /// <param name="pivotY">Pivot in document pixels, from the top.</param>
    /// <param name="cellLeft">The sprite's left edge in document space.</param>
    /// <param name="cellTop">The sprite's top edge in document space.</param>
    /// <param name="cellWidth">The sprite's width.</param>
    /// <param name="cellHeight">The sprite's height.</param>
    /// <returns>
    /// The offset to give a centred <c>Sprite2D</c> so its origin sits on the pivot.
    /// </returns>
    /// <remarks>
    /// Godot draws a sprite centred on its node by default, so moving the *drawing*
    /// rather than the node is what puts the pivot on the origin — hence the sign: the
    /// offset points from the pivot back to the centre.
    /// </remarks>
    public static (double X, double Y) SpriteOffset(
        double pivotX, double pivotY, int cellLeft, int cellTop, int cellWidth, int cellHeight)
    {
        var centreX = cellLeft + Math.Max(1, cellWidth) / 2.0;
        var centreY = cellTop + Math.Max(1, cellHeight) / 2.0;
        return (centreX - pivotX, centreY - pivotY);
    }

    /// <summary>
    /// A tag name as a Godot animation name.
    /// </summary>
    /// <remarks>
    /// Godot animation names are <c>StringName</c>s and are used as dictionary keys, so
    /// a blank one collides with the default animation and a duplicate silently replaces
    /// the earlier one. Neither is worth failing an export over, so a blank becomes a
    /// stable placeholder and the caller de-duplicates.
    /// </remarks>
    public static string AnimationName(string? tagName) =>
        string.IsNullOrWhiteSpace(tagName) ? "animation" : tagName.Trim();
}
