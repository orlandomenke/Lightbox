namespace Lightbox.Core.Documents;

/// <summary>
/// What a symbol is for. A filter in the browser, not a behaviour.
/// </summary>
/// <remarks>
/// The six libraries Pillar 3 asks for — pose, expression, hand, face, prop,
/// FX — are this enum plus a filter, rather than six features. They render
/// identically; the kind only decides which shelf the browser puts one on.
/// </remarks>
public enum SymbolKind
{
    Prop,
    Pose,
    Expression,
    Hand,
    Face,
    Fx,
    Background,
}

/// <summary>
/// A drawing stored once and placed many times.
/// </summary>
/// <remarks>
/// <para>
/// Pillar 3's promise is "edit the sword once, every animation holding it
/// updates", and that only works if a placement <i>refers</i> to the drawing
/// rather than copying it. So a symbol is the fifth thing in this application
/// resolved by id at render time, after swatches, gradients, brush tips and
/// clip regions — the pattern and its registry lifecycle already exist.
/// </para>
/// <para>
/// This is the one place the self-containment of a single document is given
/// up, and it is given up deliberately: the <i>project</i> is what re-renders,
/// and <c>ProjectIo.Flatten</c> inlines every referenced symbol when a
/// document is exported. Invariant 1 then holds exactly where it must — at
/// the boundary where a file leaves the application. See
/// <c>docs/DESIGN-symbols.md</c>.
/// </para>
/// </remarks>
public sealed class Symbol
{
    public string Id { get; set; } = Ids.NewId("sym");

    public string Name { get; set; } = "Symbol";

    public SymbolKind Kind { get; set; } = SymbolKind.Prop;

    /// <summary>
    /// Free text, one per line of thinking rather than a taxonomy.
    /// </summary>
    /// <remarks>
    /// A tag list beats a folder tree here for the reason folder trees always
    /// lose: a sword is a prop, and it is also "knight", and also "act two".
    /// Filing it once means the other two searches fail.
    /// </remarks>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// The drawing, or the drawings — one frame for a prop, several for a
    /// cycle.
    /// </summary>
    /// <remarks>
    /// Ordinary frames, so a symbol is edited with the ordinary tools and
    /// renders through the ordinary pixel path. Nothing here is a second kind
    /// of artwork.
    /// </remarks>
    public List<Frame> Frames { get; set; } = [];

    /// <summary>Frames per second, only meaningful with more than one frame.</summary>
    public int Fps { get; set; } = 12;

    /// <summary>
    /// The point a placement positions, in the symbol's own coordinates.
    /// </summary>
    /// <remarks>
    /// A hand symbol is placed by the wrist, not by the corner of its bounding
    /// box. Without this every placement is a nudge-into-position by eye, and
    /// re-editing the symbol moves every use of it.
    /// </remarks>
    public double PivotX { get; set; }

    public double PivotY { get; set; }

    /// <summary>
    /// Bumped on every edit.
    /// </summary>
    /// <remarks>
    /// What "this placement was made against an older sword" means. Reported
    /// rather than fixed automatically — a placement that silently changed
    /// under an animator is the thing they will not forgive.
    /// </remarks>
    public int Version { get; set; } = 1;

    /// <summary>How many frames a placement can offset into, at least one.</summary>
    public int FrameCount => Math.Max(1, Frames.Count);
}

/// <summary>
/// One use of a symbol, on one cel.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not editable stroke by stroke. A placement can be moved,
/// scaled, rotated, faded, recoloured and time-offset; it cannot have one of
/// its strokes nudged, because that is a different drawing and pretending
/// otherwise is how a symbol quietly becomes a copy. <b>Break link</b> turns
/// a placement into ordinary strokes on the cel, and that is the honest way
/// to get there.
/// </para>
/// <para>
/// Nesting — a symbol containing a placement of another symbol — is allowed
/// by the type and refused by the loader in this cut. It brings a cycle
/// check, a depth limit and a dependency graph that all have to be correct
/// before anything renders, and none of that earns its keep until somebody
/// asks for a hand inside an arm.
/// </para>
/// </remarks>
public sealed class SymbolPlacement
{
    public string Id { get; set; } = Ids.NewId("pl");

    /// <summary>The symbol this shows. Resolved at render time, never copied.</summary>
    public string SymbolId { get; set; } = "";

    /// <summary>Where the symbol's pivot lands, in document coordinates.</summary>
    public double X { get; set; }

    public double Y { get; set; }

    public double ScaleX { get; set; } = 1;

    public double ScaleY { get; set; } = 1;

    /// <summary>Rotation about the pivot, in degrees.</summary>
    public double Angle { get; set; }

    public double Opacity { get; set; } = 1;

    /// <summary>
    /// Which of the symbol's frames this placement shows, counted from the
    /// cel it sits on.
    /// </summary>
    /// <remarks>
    /// What makes two placements of one cycle able to run out of step — a
    /// second character walking half a stride behind the first, from one
    /// stored walk.
    /// </remarks>
    public int FrameOffset { get; set; }

    /// <summary>
    /// Recolour this use without forking the symbol.
    /// </summary>
    /// <remarks>
    /// The cheapest kind of variation and the one most often wanted: the same
    /// sword, in the enemy's colours. Null means the symbol's own colours.
    /// </remarks>
    public string? SwatchOverrideId { get; set; }

    /// <summary>
    /// The symbol's <see cref="Symbol.Version"/> when this placement was made.
    /// </summary>
    /// <remarks>
    /// Only ever compared, never acted on by itself. It is how the app can say
    /// "the sword changed since you placed this" without changing it.
    /// </remarks>
    public int SeenVersion { get; set; } = 1;

    /// <summary>Which of the symbol's frames shows on a given cel of the timeline.</summary>
    public int FrameIndexAt(int celIndex, int frameCount)
    {
        if (frameCount <= 1) return 0;
        // Positive modulo: a negative offset is a placement that starts part
        // way through the cycle, which is exactly what the offset is for.
        var i = (celIndex + FrameOffset) % frameCount;
        return i < 0 ? i + frameCount : i;
    }
}
