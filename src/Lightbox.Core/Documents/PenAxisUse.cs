namespace Lightbox.Core.Documents;

/// <summary>
/// Which of the pen's extra axes a brush actually needs — the one place that
/// question is answered, so capture and render cannot disagree about it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The brush decides what gets recorded, because response was always the
/// brush's business.</b> How hard a mark reacts to tilt lives on
/// <see cref="BrushSettings"/> and rides each stroke, exactly as pressure
/// response does; it follows that whether the numbers are worth storing is the
/// same question asked one step earlier. A brush that ignores speed writing a
/// speed into every point is the definition of a key that is present and
/// unused.
/// </para>
/// <para>
/// <b>It is measured rather than assumed to matter.</b> A 400-point stroke — a
/// few seconds of drawing — costs 113 bytes per point once the three axes are
/// written, taking the saved document to 1.70× its size, or 1.92× compact.
/// Recording axes nobody reads nearly doubles the record, and the unit of work
/// here is two hundred drawings rather than one.
/// </para>
/// <para>
/// <b>What this cannot decide is whether the artist wants them anyway.</b>
/// Brush settings stay editable forever — a stroke carries its own snapshot,
/// so it can be retuned long after it was drawn — while the hand's motion
/// happened once. Gating capture on the brush alone therefore lets a
/// reversible choice destroy irreversible data, and silently: null axes
/// contribute the neutral value by design, so a tilt curve added to a stroke
/// that never recorded tilt does nothing at all. That is what the
/// <c>AlwaysRecordPenAxes</c> preference overrides, and the whole of what it
/// means.
/// </para>
/// <para>
/// A static class rather than properties on <see cref="BrushSettings"/>: a
/// public getter beside serialized fields is a property, and would write a key
/// on every stroke of every document — the <c>BlendOrNormal</c> mistake.
/// Nothing here can be serialized by accident.
/// </para>
/// </remarks>
public static class PenAxisUse
{
    /// <summary>Whether anything on this brush reads the pen's tilt.</summary>
    public static bool NeedsTilt(BrushSettings brush) =>
        brush.AngleFollowsTilt == true || brush.TiltCurves is { Count: > 0 };

    /// <summary>Whether anything on this brush reads the hand's speed.</summary>
    public static bool NeedsSpeed(BrushSettings brush) =>
        brush.SpeedCurves is { Count: > 0 };
}
