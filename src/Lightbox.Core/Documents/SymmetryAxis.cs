using System.Text.Json.Serialization;

namespace Lightbox.Core.Documents;

/// <summary>
/// One placement of a mark under symmetry: turn by <paramref name="RotationDeg"/>
/// about the axis centre, and flip first if <paramref name="Mirrored"/>.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately <b>not</b> a matrix. <c>Lightbox.Core</c> has no SkiaSharp
/// reference and should not grow one for six numbers — so the geometry is
/// decided here, where it can be tested without a canvas, and turned into an
/// <c>SKMatrix</c> at the one place that stamps.
/// </para>
/// <para>
/// The order of operations is <c>translate(centre) · rotate · flip ·
/// translate(−centre)</c>, so a reflection across a line at angle <c>β</c>
/// through the centre is <c>Rotation = 2β</c> with <c>Mirrored = true</c> —
/// the standard identity that <c>reflect(β) = rotate(2β) ∘ flipY</c>.
/// </para>
/// </remarks>
/// <param name="RotationDeg">Degrees to turn about the axis centre.</param>
/// <param name="Mirrored">Whether this copy is a reflection rather than a turn.</param>
public readonly record struct SymmetryPlacement(double RotationDeg, bool Mirrored)
{
    /// <summary>The copy that is the mark exactly as the artist drew it.</summary>
    public static SymmetryPlacement Identity => new(0, false);

    /// <summary>True for the one placement that changes nothing.</summary>
    [JsonIgnore]
    public bool IsIdentity => !Mirrored && RotationDeg == 0;
}

/// <summary>
/// The symmetry a stroke was painted under: a centre, an axis angle, how many
/// rotational copies, and whether each is reflected as well.
/// </summary>
/// <remarks>
/// <para>
/// <b>One record for three behaviours</b>, which is what Q185 chose over
/// shipping mirror and radial as separate features:
/// </para>
/// <list type="table">
/// <item><term><c>Order 1</c>, <c>Mirror</c></term><description>the plain
/// left/right mirror — the one character design needs most, and the case Q15
/// was raised about.</description></item>
/// <item><term><c>Order 6</c>, no <c>Mirror</c></term><description>six turned
/// copies, cyclic.</description></item>
/// <item><term><c>Order 6</c> + <c>Mirror</c></term><description>twelve copies,
/// dihedral — a kaleidoscope.</description></item>
/// </list>
/// <para>
/// <b>It lives on the stroke, not on the scene.</b> That is Q15's answer and
/// invariant 4 agreeing: symmetry reaches pixels, so the mark carries the
/// symmetry it was drawn under and an artist who returns to a scene finds it as
/// they left it. Turning symmetry off afterwards is then meaningful — it removes
/// the reflection rather than orphaning a copy — and "break symmetry" is the
/// deliberate act that expands one stroke into several ordinary ones.
/// </para>
/// <para>
/// <b>Absent unless used.</b> <c>Stroke.Symmetry</c> is nullable and
/// <c>DocJson</c> ignores nulls, so a document painted without symmetry grows no
/// <c>symmetry</c> key — the <see cref="Stroke.Path"/> treatment, for the reason
/// <c>CLAUDE.md</c> gives under *"Optional has two halves"*.
/// <c>AStrokeDrawnWithoutSymmetrySerializesNoSymmetryKey</c> ships beside it,
/// and every derived accessor here carries <c>[JsonIgnore]</c> so none of them
/// reintroduces a key under a second name.
/// </para>
/// </remarks>
public sealed class SymmetryAxis
{
    /// <summary>Axis centre in document coordinates.</summary>
    public double CenterX { get; set; }

    /// <summary>Axis centre in document coordinates.</summary>
    public double CenterY { get; set; }

    /// <summary>
    /// The axis direction in degrees — 90 is the vertical mirror line an artist
    /// reaches for first, 0 is horizontal.
    /// </summary>
    /// <remarks>
    /// This is the "rotate the axis" half of the feature: it turns the whole
    /// arrangement rather than any one copy.
    /// </remarks>
    public double AngleDeg { get; set; } = 90;

    /// <summary>
    /// How many rotational copies, including the original. 1 means no turning.
    /// </summary>
    public int Order { get; set; } = 1;

    /// <summary>Whether each rotational copy is reflected as well.</summary>
    public bool Mirror { get; set; }

    /// <summary>
    /// How many marks this axis produces, the original included.
    /// </summary>
    /// <remarks>
    /// <c>[JsonIgnore]</c> because it is derived, and a public getter beside the
    /// fields is exactly how <c>BlendOrNormal</c> reintroduced the key that
    /// making its neighbour nullable had removed.
    /// </remarks>
    [JsonIgnore]
    public int CopyCount => Math.Max(1, Order) * (Mirror ? 2 : 1);

    /// <summary>
    /// True when this axis would change nothing, so a caller can skip it
    /// entirely rather than stamping one identity copy through a matrix.
    /// </summary>
    /// <remarks>
    /// The fast path's guard. An axis of order 1 with no mirror is a record that
    /// says "no symmetry", and the stamping path must cost exactly what it costs
    /// with no axis at all — see <c>DrawingCostBaselineTests</c>.
    /// </remarks>
    [JsonIgnore]
    public bool IsIdentity => Math.Max(1, Order) == 1 && !Mirror;

    /// <summary>
    /// Every placement this axis asks for, the identity first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The identity is always first and is exactly the identity</b>, so the
    /// mark the artist drew is stamped through no transform at all and renders
    /// byte-for-byte as it would with no symmetry. That is what lets the
    /// fingerprint in <c>DrawingCostBaselineTests</c> stay still.
    /// </para>
    /// <para>
    /// Rotations are <c>k · 360/N</c>. Reflections are across lines at
    /// <c>AngleDeg + k · 180/N</c>, which is the dihedral arrangement and the
    /// reason <c>Order 1</c> with <c>Mirror</c> gives a single mirror line at
    /// <c>AngleDeg</c> rather than two coincident ones.
    /// </para>
    /// <para>
    /// Allocates a small array per call, and is called once per stroke render
    /// rather than once per dab or per pointer event — the placements depend on
    /// the axis alone, so there is nothing per-dab to pay for here.
    /// </para>
    /// </remarks>
    public SymmetryPlacement[] Placements()
    {
        var n = Math.Max(1, Order);
        if (IsIdentity) return [SymmetryPlacement.Identity];

        var step = 360.0 / n;
        var copies = new SymmetryPlacement[Mirror ? n * 2 : n];
        for (var k = 0; k < n; k++) copies[k] = new SymmetryPlacement(k * step, false);

        if (Mirror)
        {
            // reflect(β) = rotate(2β) ∘ flipY, with β = AngleDeg + k·180/N.
            for (var k = 0; k < n; k++)
            {
                copies[n + k] = new SymmetryPlacement(2 * AngleDeg + k * step, true);
            }
        }

        return copies;
    }

    /// <summary>A copy, so editing an axis never reaches a stroke already painted.</summary>
    public SymmetryAxis Clone() => new()
    {
        CenterX = CenterX,
        CenterY = CenterY,
        AngleDeg = AngleDeg,
        Order = Order,
        Mirror = Mirror,
    };
}
