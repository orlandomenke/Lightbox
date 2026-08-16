namespace Lightbox.App.Services;

/// <summary>
/// The motion trail as the artist has set it up.
/// </summary>
/// <remarks>
/// Persisted with the application for <see cref="OnionSettings"/>'s reason:
/// the trail is a drawing aid on the view-only side of invariant 5, and how
/// far along an arc an animator looks is how they work, not a property of any
/// scene. Off by default, unlike onion skin — the trail is reached for while
/// judging spacing, not left on while drawing.
/// </remarks>
public sealed class MotionTrailSettings
{
    public bool Enabled { get; set; }

    /// <summary>
    /// Drawings back and forward to trail through. Wider than onion skin's
    /// defaults on purpose: a spacing chart over two drawings says almost
    /// nothing, and unlike ghosts the ticks do not pile drawing over drawing,
    /// so depth here costs legibility nothing.
    /// </summary>
    public int Before { get; set; } = 4;

    public int After { get; set; } = 4;
}
