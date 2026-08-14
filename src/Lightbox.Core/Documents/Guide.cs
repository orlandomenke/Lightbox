namespace Lightbox.Core.Documents;

/// <summary>What a guide constrains a stroke to.</summary>
public enum GuideKind
{
    /// <summary>An infinite straight line through a point, at an angle.</summary>
    Line,

    /// <summary>A square lattice — the thing "grid and snapping" means.</summary>
    Grid,

    /// <summary>
    /// The three axes of an isometric drawing, through one origin.
    /// </summary>
    /// <remarks>
    /// A guide of its own rather than three <see cref="Line"/>s, because the
    /// artist reaches for "isometric", moves the origin once, and expects all
    /// three to follow. Three separate lines is the same picture and three
    /// times the housekeeping.
    /// </remarks>
    Isometric,

    /// <summary>
    /// A point everything recedes towards. One of these is one-point
    /// perspective, two is two-point, three is three.
    /// </summary>
    /// <remarks>
    /// The rig is not a type of its own: "two-point perspective" is just two
    /// vanishing points, and modelling it as a first-class object would mean
    /// an artist who wants an extra VP for a tilted roof has to leave the
    /// abstraction to get it.
    /// </remarks>
    VanishingPoint,

    /// <summary>
    /// A character height chart: a vertical scale divided into head units,
    /// standing on its anchor.
    /// </summary>
    /// <remarks>
    /// A kind of its own rather than a stack of <see cref="Line"/>s, for the
    /// isometric guide's reason: the artist reaches for "six heads", resizes
    /// the character once by pulling the top, and expects every division to
    /// follow. Seven hand-maintained lines is the same picture and seven
    /// times the housekeeping — and nothing would know to label it.
    /// (X, Y) is the <i>bottom</i> — the ground the character stands on —
    /// <see cref="Guide.Spacing"/> is one head, and
    /// <see cref="Guide.Divisions"/> is how many of them tall.
    /// </remarks>
    HeightScale,
}

/// <summary>
/// A drawing aid the artist places on the document: a ruler, a grid, a
/// vanishing point.
/// </summary>
/// <remarks>
/// <para>
/// Document data, like the camera and unlike the view transform. A perspective
/// rig set up for a background is part of that background's construction — it
/// has to be there tomorrow — so it is saved. Also like the camera, it never
/// reaches a pixel: guides are chrome, and what they change is where the
/// artist's <i>input</i> lands.
/// </para>
/// <para>
/// That is what keeps invariant 1 intact. A snapped stroke records the snapped
/// points, so a reload re-renders it exactly without consulting the guide at
/// all — deleting a guide later cannot move a line that was already drawn.
/// The alternative, storing the guide reference and re-projecting at render
/// time, would make every old drawing hostage to a rig somebody might move.
/// </para>
/// <para>
/// Absent by default. <see cref="Scene.Guides"/> is null until one is placed,
/// so a document that never uses them writes no guide key.
/// </para>
/// </remarks>
public sealed class Guide
{
    public string Id { get; set; } = Ids.NewId("gd");

    public GuideKind Kind { get; set; }

    public string? Name { get; set; }

    /// <summary>
    /// The guide's anchor in document pixels: a point on the line, the grid's
    /// origin, the isometric origin, or the vanishing point itself.
    /// </summary>
    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>Degrees clockwise from east. A line's direction; a grid's tilt.</summary>
    public double Angle { get; set; }

    /// <summary>
    /// Grid pitch — or, on a height scale, one head unit — in document pixels.
    /// Ignored by the other kinds.
    /// </summary>
    public double Spacing { get; set; } = 32;

    /// <summary>
    /// How many of the thing this kind counts, or null for the kind's default.
    /// </summary>
    /// <remarks>
    /// Two meanings, by kind, on one nullable key rather than two:
    /// on a <see cref="GuideKind.HeightScale"/> it is how many heads tall the
    /// chart is — "6 heads" is six of these — and on a
    /// <see cref="GuideKind.VanishingPoint"/> it is how many rays are drawn
    /// out of the point. Both answer "how many of them", both are absent until
    /// the artist says otherwise, and a second nullable key would widen the
    /// record for nothing. Null on every other kind, so a document with
    /// neither writes no divisions key at all.
    /// </remarks>
    public int? Divisions { get; set; }

    /// <summary>
    /// How many rays a vanishing point is drawn with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A drawing property, not a constraint: a vanishing point constrains
    /// <i>every</i> direction through it — <c>Snapper.DirectionAt</c> works
    /// from the point, not from a ray — so changing this changes what you see
    /// and never what you can snap to. Fewer rays to see the drawing through,
    /// more to read the perspective.
    /// </para>
    /// <para>
    /// Twenty-four when unset, which is the fan the renderer always drew.
    /// Clamped rather than validated: a fan of one is not a fan, and past a
    /// couple of hundred the rays close into a filled disc.
    /// </para>
    /// <para>Derived; never serialized — see <see cref="Angles"/>.</para>
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public int RayCount => Math.Clamp(Divisions ?? DefaultRays, MinRays, MaxRays);

    /// <summary>The fan a vanishing point is drawn with when nothing says otherwise.</summary>
    public const int DefaultRays = 24;

    public const int MinRays = 2;

    public const int MaxRays = 180;

    /// <summary>Drawn on the canvas. A hidden guide still snaps — see the remarks.</summary>
    /// <remarks>
    /// Deliberately separate from whether it snaps. Hiding a rig to look at the
    /// drawing underneath is something an artist does constantly mid-work, and
    /// having that silently stop constraining the next line would be a very
    /// confusing way to lose a stroke. <see cref="Snaps"/> is the other switch.
    /// </remarks>
    public bool Visible { get; set; } = true;

    /// <summary>Whether strokes are constrained by it.</summary>
    public bool Snaps { get; set; } = true;

    /// <summary>Pinned in place, so a drag on the canvas cannot move it.</summary>
    public bool Locked { get; set; }

    public Guide Clone() => (Guide)MemberwiseClone();

    /// <summary>The angles, in degrees, that this guide constrains along.</summary>
    /// <remarks>
    /// A vanishing point has no fixed angle — its direction depends on where
    /// you are standing — so it returns nothing here and is handled by
    /// <see cref="Snapper.DirectionAt"/> instead. A height scale also returns
    /// nothing: it pulls points onto its division lines but never grabs a
    /// stroke's direction, or every horizontal stroke on the canvas would
    /// belong to it.
    ///
    /// <para>Derived; never serialized — see <see cref="Scene.HasGhostFrames"/>.</para>
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<double> Angles => Kind switch
    {
        GuideKind.Line => [Angle],
        // Two axes plus the vertical: the isometric convention, and the one
        // that makes a cube read as a cube.
        GuideKind.Isometric => [Angle + 30, Angle - 30, Angle + 90],
        GuideKind.Grid => [Angle, Angle + 90],
        _ => [],
    };
}
