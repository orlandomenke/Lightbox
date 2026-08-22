namespace Lightbox.Core.Documents;

/// <summary>Procedural tip shapes the generator can bake.</summary>
public enum TipShape
{
    /// <summary>Full inside the radius, feathered over the last pixel.</summary>
    HardCircle,

    /// <summary>Full inside <c>Radius × Hardness</c>, then smoothly to nothing.</summary>
    SoftCircle,

    /// <summary>Full between an inner and an outer radius. A hollow stamp.</summary>
    Ring,

    /// <summary>
    /// A flattened ellipse. The shape that makes
    /// <see cref="BrushSettings.AngleFollowsDirection"/> worth having, and the
    /// cheapest honest answer to a bristle brush.
    /// </summary>
    Chisel,

    /// <summary>Parallel or crossed rules, in tip space so they follow the stroke.</summary>
    Hatch,

    /// <summary>
    /// A ring of bristle channels, from a raised-cosine comb around the
    /// azimuth. What a round hog brush leaves when the paint thins.
    /// </summary>
    Bristle,

    /// <summary>
    /// A superellipse — Lamé's curve, <c>|x/a|^n + |y/b|^n = 1</c>. At n = 2 a
    /// plain ellipse, above it a squarish marker nib, below it a pointed
    /// almond. One exponent walks the whole family.
    /// </summary>
    Superellipse,

    /// <summary>
    /// A rounded polygon in polar form. The shape a cut nib or a folded felt
    /// tip presses out, and the one procedural shape with corners.
    /// </summary>
    Polygon,

    /// <summary>
    /// Worley (cellular) noise thresholded into a spatter of grains. The
    /// classic answer for a sponge, a stipple or a rough charcoal edge, and
    /// unlike value noise it produces distinct blobs rather than fog.
    /// </summary>
    Spatter,

    /// <summary>
    /// A soft disc with a raised rim, from the same 1D profile a watercolour
    /// leaves when it dries: pale in the middle, dark at the contact line.
    /// A single stamp of a wet edge, for a brush that is not simulating one.
    /// </summary>
    Halo,

    /// <summary>
    /// A teardrop — a round belly tapering to a point. The footprint of a
    /// pointed round; the shape that makes
    /// <see cref="BrushSettings.AngleFollowsDirection"/> read as a loaded
    /// brush being pulled.
    /// </summary>
    Drop,

    /// <summary>
    /// One disc bitten out of another. A curved nib for arced dry-brush
    /// marks, or a smile of a stamp at full bite.
    /// </summary>
    Crescent,

    /// <summary>
    /// An organic, unevenly lobed disc — a hand-made footprint for washes
    /// and stamped textures, so a mark's boundary does not read as geometry.
    /// The wobble is hashed from the lobe count, never rolled, so the same
    /// recipe is the same blot on every machine.
    /// </summary>
    Blot,
}

/// <summary>
/// The curve an alpha gradient follows across a tip's fade band — how a soft
/// edge gets from solid to nothing.
/// </summary>
public enum TipFalloff
{
    /// <summary>Smoothstep. The default, and what every soft tip did before this existed.</summary>
    Smooth,

    /// <summary>A straight ramp. Reads slightly firmer than smooth at the same width.</summary>
    Linear,

    /// <summary>A Gaussian shoulder with a long faint tail — the airbrush profile.</summary>
    Airbrush,

    /// <summary>A quarter-circle shoulder: full almost to the edge, then quickly gone.</summary>
    Dome,
}

/// <summary>
/// How a procedural tip was made, kept so the artist can reopen and adjust it.
/// </summary>
/// <remarks>
/// <b>Provenance, not a render input.</b> Nothing reads this while drawing —
/// the baked PNG is what stamps. Re-deriving pixels from a recipe at load time
/// would mean that improving a falloff curve silently repaints every drawing
/// that ever used the tip, which is invariant 1 seen from the other side.
/// Editing a recipe therefore bakes a <em>new</em> tip rather than changing an
/// old one's pixels.
/// </remarks>
public sealed class TipRecipe
{
    public TipShape Shape { get; set; } = TipShape.SoftCircle;

    /// <summary>Baked size in pixels, square. Powers of two keep the mipmap chain clean.</summary>
    public int Size { get; set; } = 256;

    /// <summary>0..1, for <see cref="TipShape.SoftCircle"/>. 1 is a hard edge.</summary>
    public double Hardness { get; set; } = 0.5;

    /// <summary>0..1 of the outer radius, for <see cref="TipShape.Ring"/>.</summary>
    public double InnerRadius { get; set; } = 0.7;

    /// <summary>0..1 minor over major axis, for <see cref="TipShape.Chisel"/>.</summary>
    public double Roundness { get; set; } = 0.25;

    /// <summary>Degrees, baked into the shape itself.</summary>
    public double Angle { get; set; }

    /// <summary>Rule spacing in tip pixels, for <see cref="TipShape.Hatch"/>.</summary>
    public double Spacing { get; set; } = 16;

    /// <summary>Rule width in tip pixels, for <see cref="TipShape.Hatch"/>.</summary>
    public double LineWidth { get; set; } = 3;

    /// <summary>Rule the other way too, for <see cref="TipShape.Hatch"/>.</summary>
    public bool Crossed { get; set; }

    /// <summary>
    /// How many of the repeating element there are: bristles round a
    /// <see cref="TipShape.Bristle"/> tip, sides on a
    /// <see cref="TipShape.Polygon"/>, cells across a
    /// <see cref="TipShape.Spatter"/>.
    /// </summary>
    public int Count { get; set; } = 12;

    /// <summary>
    /// The shape's own exponent or sharpness, 0..1 mapped per shape: Lamé's
    /// <c>n</c> for a <see cref="TipShape.Superellipse"/>, corner radius for a
    /// <see cref="TipShape.Polygon"/>, grain coverage for
    /// <see cref="TipShape.Spatter"/>, rim height for <see cref="TipShape.Halo"/>,
    /// how pointed a <see cref="TipShape.Drop"/> is, bite depth on a
    /// <see cref="TipShape.Crescent"/>, wobble depth on a <see cref="TipShape.Blot"/>.
    /// </summary>
    public double Sharpness { get; set; } = 0.5;

    /// <summary>
    /// 0..1: how far the alpha gradient reaches in from the boundary. 0 is the
    /// crisp one-pixel edge every shape bakes by default; 1 fades from the
    /// shape's own core — each shape normalises the band by its inradius, so a
    /// thin chisel fades fully across its own thickness rather than being
    /// swallowed by a band measured against the tip's radius. Read by every
    /// shape except the two circles (whose
    /// <see cref="Hardness"/> already is this dial) and the
    /// <see cref="TipShape.Halo"/>, which is its own gradient.
    /// </summary>
    public double Fade { get; set; }

    /// <summary>The curve the fade follows — and the soft circle's <see cref="Hardness"/> band follows it too.</summary>
    public TipFalloff FadeProfile { get; set; } = TipFalloff.Smooth;

    public TipRecipe Clone() => (TipRecipe)MemberwiseClone();
}

/// <summary>
/// One brush tip in the library: a baked grayscale raster, plus where the pen
/// touches it and where it came from.
/// </summary>
/// <remarks>
/// <para>
/// A tip is an asset. It is generated or imported once and after that only
/// looked up — nothing about one is computed while a stroke is being drawn.
/// <c>BrushTipRegistry</c> already worked this way for imported tips; the
/// generator writes into the same shape, and past the registry the engine
/// cannot tell a procedural tip from a scanned one.
/// </para>
/// <para>
/// <see cref="PivotX"/>/<see cref="PivotY"/> is the addition that is not
/// obvious. It is where the pen touches, which is <em>not</em> the centre of
/// the mark for anything captured at an angle, and it is what keeps a blend
/// between two captures from ghosting. Making it a field rather than a
/// cropping discipline means it can be corrected afterwards and cannot be
/// silently got wrong.
/// </para>
/// </remarks>
public sealed class BrushTip
{
    /// <summary>Globally unique, and already the registry's key.</summary>
    public string Id { get; set; } = Ids.NewId("tip");

    public string Name { get; set; } = "Tip";

    /// <summary>The baked raster: square, shape in the alpha channel, base64 PNG.</summary>
    public string Png { get; set; } = "";

    /// <summary>Where the pen touches, normalised. Centre unless a capture says otherwise.</summary>
    public double PivotX { get; set; } = 0.5;

    public double PivotY { get; set; } = 0.5;

    /// <summary>Set when the generator made it; null for a scan or an import.</summary>
    public TipRecipe? Recipe { get; set; }

    /// <summary>Where a scanned or imported tip came from, for the artist's memory only.</summary>
    public string? Source { get; set; }

    public BrushTip Clone() => new()
    {
        Id = Id,
        Name = Name,
        Png = Png,
        PivotX = PivotX,
        PivotY = PivotY,
        Recipe = Recipe?.Clone(),
        Source = Source,
    };
}
