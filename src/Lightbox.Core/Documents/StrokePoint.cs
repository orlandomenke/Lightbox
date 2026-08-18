using System.Text.Json.Serialization;

namespace Lightbox.Core.Documents;

/// <summary>
/// A single sample of a stroke: position in document pixels, stylus pressure
/// (0..1; mouse input feeds a constant 0.5), and — when a pen reported them —
/// tilt and speed.
/// </summary>
/// <remarks>
/// <para>
/// <b>The three optional axes are absent unless a pen actually reported
/// them</b>, which is what keeps this from taxing every document that will
/// never use it. A mouse stroke and every drawing made before they existed
/// serialize byte-identically — `AMouseStrokeWritesNoTiltOrSpeedKeys` and its
/// neighbours hold that in the same commit that added the fields
/// (<c>CLAUDE.md</c> → *"Optional" has two halves*).
/// </para>
/// <para>
/// <b>Tilt is stored as the device delivers it</b> — degrees per axis, −90..90,
/// rounded to one decimal so the file does not carry float noise. Altitude
/// ("how far over is the pen") and azimuth ("which way is it leaning") are
/// *derived* from the pair at render rather than stored beside it: two numbers
/// and one convention, so the stored pair and a derivation of it can never
/// disagree in the file.
/// </para>
/// <para>
/// <b>Speed is a normalized 0..1 value computed once, at capture, and then
/// true forever</b> (Q113). It cannot be recomputed at render — Avalonia
/// delivers no per-point timestamps, verified against the shipped assembly —
/// so a stroke that stored the hand's speed keeps drawing the same mark on
/// reload, under undo, and through the inbetweener. That is invariant 4's rule
/// applied to time: a better estimator later changes new strokes only, never
/// art already made.
/// </para>
/// <para>
/// <b>Why widening this record rather than parallel arrays on the stroke</b>
/// (Q112): arrays would put an alignment invariant on every operation that
/// edits <c>Points</c> — the <c>RestPoints</c> count-mismatch trap, multiplied
/// across transforms, path re-flattening, the cleaner and the inbetweener.
/// A wider point cannot fall out of step with itself.
/// </para>
/// </remarks>
public readonly record struct StrokePoint(
    double X,
    double Y,
    double Pressure,
    double? TiltX = null,
    double? TiltY = null,
    double? Speed = null)
{
    /// <summary>
    /// How far the pen is leaned over, 0 (upright) to 1 (flat), or null when
    /// no tilt was reported.
    /// </summary>
    /// <remarks>
    /// Derived rather than stored, for the reason in the type's remarks. The
    /// magnitude of the tilt pair against 90°, which is what a brush wants when
    /// it asks "is the nib on its side" — the direction of the lean is
    /// <see cref="TiltAzimuthDeg"/>.
    /// </remarks>
    [JsonIgnore]
    public double? TiltAltitude =>
        TiltX is { } tx && TiltY is { } ty
            ? Math.Clamp(Math.Sqrt(tx * tx + ty * ty) / 90.0, 0, 1)
            : null;

    /// <summary>
    /// Which way the pen is leaning, in degrees, or null when no tilt was
    /// reported.
    /// </summary>
    /// <remarks>
    /// Measured the way every other angle in the document is — atan2 of the
    /// pair — so a brush that steers its dab by the lean uses the same
    /// convention as one that steers by the stroke's heading.
    /// </remarks>
    [JsonIgnore]
    public double? TiltAzimuthDeg =>
        TiltX is { } tx && TiltY is { } ty
            ? Math.Atan2(ty, tx) * 180 / Math.PI
            : null;

    /// <summary>Whether this point carries anything a pen had to report.</summary>
    /// <remarks>
    /// <b>All three derived members carry <see cref="JsonIgnoreAttribute"/>,
    /// and that is load-bearing.</b> A public getter beside a nullable field is
    /// a property as far as the serializer is concerned, so without this each
    /// one would write a key on every point of every document — reintroducing
    /// under a second name exactly the weight that making the fields nullable
    /// existed to avoid. <c>CLAUDE.md</c> records the same mistake being made
    /// once already, by <c>BlendOrNormal</c>.
    /// </remarks>
    [JsonIgnore]
    public bool HasPenAxes => TiltX is not null || TiltY is not null || Speed is not null;
}
