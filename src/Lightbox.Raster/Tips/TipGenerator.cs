using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster.Tips;

/// <summary>
/// Bakes a procedural brush tip into the same raster form an imported one
/// arrives in.
/// </summary>
/// <remarks>
/// <para>
/// Run once, when a tip is made — never during a stroke. What the engine
/// resolves is a cached bitmap, and it has no idea whether a formula or a
/// flatbed scanner produced it.
/// </para>
/// <para>
/// <b>Every boundary is coverage, not a test.</b> The obvious way to write a
/// disc is <c>d &lt;= Radius ? 1 : 0</c>, and it stair-steps. On one drawing a
/// staircase is a curiosity; replayed across two hundred frames the steps
/// shift phase from mark to mark and the edge <em>boils</em>. So every shape
/// here returns how much of the pixel the shape covers, approximated as one
/// smooth step across a one-pixel band — which costs the same arithmetic and
/// is the difference between a tip that survives being animated and one that
/// does not.
/// </para>
/// <para>
/// Sampling is at pixel centres. A pixel's centre is at <c>x + 0.5</c>, and
/// the shape's centre is the middle of the matrix — getting that half-pixel
/// wrong biases every tip toward the top left by half a pixel, which is
/// invisible on one stamp and a drift on a scaled one.
/// </para>
/// </remarks>
public static class TipGenerator
{
    /// <summary>Smallest useful bake. Below this a tip is a few pixels of aliasing.</summary>
    public const int MinSize = 8;

    /// <summary>
    /// Largest bake. 2048² is the size scanned stamps arrive at and there is
    /// nothing above it that a brush can show — a tip is downscaled to the dab,
    /// never up.
    /// </summary>
    public const int MaxSize = 2048;

    /// <summary>Bake a recipe. The alpha channel carries the shape.</summary>
    public static SKBitmap Bake(TipRecipe recipe)
    {
        var size = Math.Clamp(recipe.Size, MinSize, MaxSize);
        var alpha = Coverage(recipe, size);
        return ToBitmap(alpha, size);
    }

    /// <summary>Bake and encode, ready for the library.</summary>
    public static BrushTip Create(TipRecipe recipe, string name)
    {
        using var bitmap = Bake(recipe);
        return new BrushTip { Name = name, Png = PngCodec.Encode(bitmap), Recipe = recipe.Clone() };
    }

    /// <summary>
    /// The coverage matrix, row-major, 0..1. Exposed because it is what the
    /// tests can assert against without going through a PNG.
    /// </summary>
    public static float[] Coverage(TipRecipe recipe, int size)
    {
        var a = new float[size * size];
        var centre = size * 0.5f;
        var radius = centre;
        var theta = (float)(recipe.Angle * Math.PI / 180.0);
        var cos = MathF.Cos(theta);
        var sin = MathF.Sin(theta);

        // The alpha gradient every shape can wear. Fade is normalised per
        // shape — each scales it by its own inradius, so 1 always means
        // "fades from the shape's core" rather than "fades by half the tip",
        // which on a thin chisel would be a band no interior point escapes.
        var fade = (float)Math.Clamp(recipe.Fade, 0, 1);
        var curve = recipe.FadeProfile;

        for (var y = 0; y < size; y++)
        {
            var py = y + 0.5f - centre;
            for (var x = 0; x < size; x++)
            {
                var px = x + 0.5f - centre;

                // Into the tip's own frame, so Angle is baked rather than
                // costing a rotation at every dab.
                var rx = px * cos + py * sin;
                var ry = -px * sin + py * cos;

                a[y * size + x] = recipe.Shape switch
                {
                    TipShape.HardCircle => Disc(MathF.Sqrt(rx * rx + ry * ry), radius),
                    TipShape.SoftCircle => Soft(MathF.Sqrt(rx * rx + ry * ry), radius, (float)recipe.Hardness, curve),
                    TipShape.Ring => Ring(MathF.Sqrt(rx * rx + ry * ry), radius, (float)recipe.InnerRadius, fade, curve),
                    TipShape.Chisel => Chisel(rx, ry, radius, (float)recipe.Roundness, fade, curve),
                    TipShape.Hatch => Hatch(rx, ry, radius, recipe, fade, curve),
                    TipShape.Bristle => Bristle(rx, ry, radius, recipe, curve),
                    TipShape.Superellipse => Superellipse(rx, ry, radius, recipe, fade, curve),
                    TipShape.Polygon => Polygon(rx, ry, radius, recipe, fade, curve),
                    TipShape.Spatter => Spatter(rx, ry, radius, recipe, curve),
                    TipShape.Halo => Halo(MathF.Sqrt(rx * rx + ry * ry), radius, recipe),
                    TipShape.Drop => Drop(rx, ry, radius, (float)recipe.Sharpness, fade, curve),
                    TipShape.Crescent => Crescent(rx, ry, radius, (float)recipe.Sharpness, fade, curve),
                    TipShape.Blot => Blot(rx, ry, radius, recipe, fade, curve),
                    _ => 0f,
                };
            }
        }

        return a;
    }

    // ---- shapes -------------------------------------------------------------

    /// <summary>Full to the radius, one pixel of feather at the boundary.</summary>
    private static float Disc(float d, float radius) => Step(radius - d);

    /// <summary>
    /// Full inside the hard core, then a shaped shoulder out to the radius.
    /// </summary>
    /// <remarks>
    /// Smoothstep by default rather than a straight ramp, because the linear
    /// one leaves a visible crease where the core meets the fade and an artist
    /// reads that crease as a second, smaller brush inside the first — which is
    /// also why Linear is offered as a choice rather than imposed: on a tip an
    /// artist picked it for, the crease is the look.
    /// </remarks>
    private static float Soft(float d, float radius, float hardness, TipFalloff curve)
    {
        var core = radius * Math.Clamp(hardness, 0f, 1f);
        if (d <= core) return 1f;
        if (d >= radius) return 0f;
        // Degenerate only if hardness is 1, and Disc's feather is the right
        // answer there rather than a divide by zero.
        if (radius - core < 1e-4f) return Step(radius - d);
        var t = (d - core) / (radius - core);
        // The Smooth arm keeps the exact expression this always was, so a
        // re-baked old recipe cannot move by even a rounding step. The other
        // curves take the same min against Step that Edge does, because the
        // airbrush's tail never quite reaches zero and would otherwise end in
        // a faint hard rim.
        return curve == TipFalloff.Smooth
            ? 1f - Smooth(t)
            : MathF.Min(Shaped(1f - t, curve), Step(radius - d));
    }

    private static float Ring(float d, float radius, float inner, float fade, TipFalloff curve)
    {
        var r0 = radius * Math.Clamp(inner, 0f, 0.99f);
        // Both edges fade, or a soft ring would read as a disc with a hard
        // hole punched in it. The band is against the ring's own half-width:
        // fade 1 leaves full alpha only along the ring's centreline.
        return Edge(MathF.Min(radius - d, d - r0), fade * (radius - r0) * 0.5f, curve);
    }

    /// <summary>An ellipse squashed across its own axis.</summary>
    private static float Chisel(float x, float y, float radius, float roundness, float fade, TipFalloff curve)
    {
        var minor = radius * Math.Clamp(roundness, 0.02f, 1f);
        var band = fade * minor;
        // Distance to the ellipse boundary, scaled back into pixels so the
        // feather is one pixel wide on the page rather than one unit wide in
        // a normalised space where it would be far too soft on the short axis.
        var nx = x / radius;
        var ny = y / minor;
        var n = MathF.Sqrt(nx * nx + ny * ny);
        // The exact centre is `minor` from the nearest boundary — returned
        // through the same fade as its neighbours, or a faded chisel wears a
        // single dark pixel like a knot.
        if (n <= 0) return Edge(minor, band, curve);

        // A crisp edge only needs the distance to be right *across* the
        // boundary, and the ray-scaled form is. A fade band needs it to be
        // even *around* the boundary too — the ray form over-reports on the
        // flanks and the solid core pinches into a bone — so the band uses
        // the first-order distance from the implicit's own gradient. The ray
        // form stays for fade 0, so old recipes re-bake to the same pixels.
        if (band > 1e-3f)
        {
            var g = MathF.Sqrt(
                x * x / (radius * radius * radius * radius)
                + y * y / (minor * minor * minor * minor));
            return Edge(g <= 1e-9f ? minor : n * (1f - n) / g, band, curve);
        }

        var scale = MathF.Sqrt(x * x + y * y) / n;
        return Edge((1f - n) * scale, band, curve);
    }

    /// <summary>
    /// Rules in tip space, so a hatch brush combs along the stroke when
    /// <see cref="BrushSettings.AngleFollowsDirection"/> is on.
    /// </summary>
    /// <remarks>
    /// Distance to the nearest rule, feathered — not <c>x % spacing == 0</c>,
    /// which is a one-pixel hard line that aliases at every scale and whose
    /// grid phase shifts between frames. A screentone wants the opposite of
    /// this: locked to the document rather than to the dab, so it does not swim
    /// as the stroke turns. That is a pattern fill, not a tip.
    /// </remarks>
    private static float Hatch(float x, float y, float radius, TipRecipe recipe, float fade, TipFalloff curve)
    {
        var band = fade * radius;
        var spacing = MathF.Max(2f, (float)recipe.Spacing);
        var half = MathF.Max(0.5f, (float)recipe.LineWidth * 0.5f);

        var rule = Line(x, spacing, half);
        if (recipe.Crossed) rule = MathF.Max(rule, Line(y, spacing, half));

        // Clipped to the round footprint, or the tip stamps its square. The
        // fade softens the footprint only — the rules stay crisp, because a
        // faded rule is just a thinner rule and the width slider owns that.
        return MathF.Min(rule, Edge(radius - MathF.Sqrt(x * x + y * y), band, curve));
    }

    private static float Line(float v, float spacing, float half)
    {
        var phase = v - MathF.Floor(v / spacing + 0.5f) * spacing;
        return Step(half - MathF.Abs(phase));
    }

    /// <summary>
    /// A round brush whose bristles have parted: a raised-cosine comb around
    /// the azimuth, inside a soft disc.
    /// </summary>
    /// <remarks>
    /// The comb is <c>½(1 + cos(N·θ))</c>, which is the standard way to get a
    /// periodic ridge with no discontinuity anywhere — a hard-edged wedge would
    /// alias as the tip rotates with the stroke.
    ///
    /// <b>The scratches are subtracted, and they are narrow.</b> Two ways to get
    /// this wrong, and both stamp a starburst rather than a brush: multiplying
    /// by the comb keeps a thin ridge at each hair and discards everything
    /// between them, and even inverting that leaves grooves a quarter of the
    /// hair spacing wide, which the eye reads as wedges. So the groove is
    /// <c>(1 − comb)^k</c> with a high exponent — a narrow spike at each parting
    /// and flat zero elsewhere — subtracted from a solid disc at a depth below
    /// one, so a scratch thins the paint instead of removing it.
    ///
    /// It fades toward the middle, because the centre of a round brush is where
    /// the hairs are packed tightest and the last place paint runs out. A comb
    /// that reached the centre would leave a star, not a brush.
    ///
    /// Where that fade happens is the difference between a bristle brush and a
    /// soft round with a texture on it. Solid to a third of the way out and
    /// fully combed by four fifths puts the streaks over most of the mark,
    /// which is what the eye actually reads as hair.
    /// </remarks>
    private static float Bristle(float x, float y, float radius, TipRecipe recipe, TipFalloff curve)
    {
        var d = MathF.Sqrt(x * x + y * y);
        // The body already fades — that is part of what reads as hair — so
        // Fade widens that falloff rather than adding a second one on top.
        var body = Soft(d, radius, 0.5f * (1f - (float)Math.Clamp(recipe.Fade, 0, 1)), curve);
        if (body <= 0) return 0f;

        var n = Math.Max(2, recipe.Count);
        var theta = MathF.Atan2(y, x);
        var comb = 0.5f * (1f + MathF.Cos(n * theta));
        var sharp = 4f + 16f * (float)Math.Clamp(recipe.Sharpness, 0, 1);
        var groove = MathF.Pow(1f - comb, sharp);

        var t = Math.Clamp(d / radius, 0f, 1f);
        var depth = 0.9f * Smooth(Math.Clamp((t - 0.3f) / 0.5f, 0f, 1f));
        return body * (1f - depth * groove);
    }

    /// <summary>
    /// Lamé's superellipse, <c>|x/a|^n + |y/b|^n = 1</c>.
    /// </summary>
    /// <remarks>
    /// One exponent walks a whole family of nibs: n = 2 is an ellipse, n → ∞ a
    /// rectangle, n = 1 a diamond and below that a four-pointed almond. It is
    /// the cheapest single control that reaches shapes a circle and a chisel
    /// cannot, which is why it is here rather than three separate shapes.
    ///
    /// The implicit value is converted back to a distance in pixels before
    /// feathering, or the soft edge would be a different width on the long
    /// axis than the short one — the same correction <see cref="Chisel"/>
    /// makes.
    /// </remarks>
    private static float Superellipse(float x, float y, float radius, TipRecipe recipe, float fade, TipFalloff curve)
    {
        var minor = radius * Math.Clamp((float)recipe.Roundness, 0.05f, 1f);
        var band = fade * minor;
        // 0.4 → a pointed almond, 0.5 → an ellipse, 1 → a squarish nib.
        var n = 0.5f + 7.5f * (float)Math.Clamp(recipe.Sharpness, 0, 1);

        var nx = MathF.Abs(x) / radius;
        var ny = MathF.Abs(y) / minor;
        var f = MathF.Pow(nx, n) + MathF.Pow(ny, n);
        // The centre through the same fade as its neighbours — see Chisel.
        if (f <= 0) return Edge(minor, band, curve);

        // f = 1 on the boundary; f^(1/n) is the radial fraction, which scales
        // back to pixels the same way the chisel's does.
        var frac = MathF.Pow(f, 1f / n);

        // The fade band wants the gradient-normalised distance for the same
        // reason the chisel's does. Only above n = 1 — below it the gradient's
        // |x|^(n-1) terms blow up on the axes — and that is the last 7% of the
        // sharpness slider, where the ray form's unevenness is mild anyway.
        if (band > 1e-3f && n >= 1f)
        {
            var gx = MathF.Pow(MathF.Abs(x), n - 1f) / MathF.Pow(radius, n);
            var gy = MathF.Pow(MathF.Abs(y), n - 1f) / MathF.Pow(minor, n);
            var g = MathF.Pow(frac, 1f - n) * MathF.Sqrt(gx * gx + gy * gy);
            return Edge(g <= 1e-9f ? minor : (1f - frac) / g, band, curve);
        }

        var scale = MathF.Sqrt(x * x + y * y) / MathF.Max(frac, 1e-4f);
        return Edge((1f - frac) * MathF.Max(scale, 1f), band, curve);
    }

    /// <summary>
    /// A rounded regular polygon, in polar form.
    /// </summary>
    /// <remarks>
    /// The boundary radius of a regular N-gon at angle θ is
    /// <c>cos(π/N) / cos((θ mod 2π/N) − π/N)</c> — the distance to the flat,
    /// which peaks at the corners and dips to <c>cos(π/N)</c> at the middle of
    /// a side.
    ///
    /// Rounding is a lerp toward that dip — the <em>inscribed</em> circle, the
    /// one the flats already touch. Rounding a corner has to take material
    /// away: lerping toward the circumscribed circle instead (the tempting
    /// <c>flat → 1</c>) grows the tip as the slider moves, and an artist reads
    /// a size that changes with a shape control as a bug in the size slider.
    /// </remarks>
    private static float Polygon(float x, float y, float radius, TipRecipe recipe, float fade, TipFalloff curve)
    {
        var n = Math.Clamp(recipe.Count, 3, 12);
        var inscribed = MathF.Cos(MathF.PI / n);
        var band = fade * radius * inscribed;

        var d = MathF.Sqrt(x * x + y * y);
        if (d <= 0) return Edge(radius * inscribed, band, curve);

        var seg = 2f * MathF.PI / n;
        var theta = MathF.Atan2(y, x);
        var phase = theta - MathF.Floor(theta / seg) * seg;
        var flat = inscribed / MathF.Cos(phase - MathF.PI / n);

        // Sharpness 1 is the polygon, 0 the circle its own flats sit on.
        var round = 1f - (float)Math.Clamp(recipe.Sharpness, 0, 1);
        var boundary = radius * (flat + (inscribed - flat) * round);
        return Edge(boundary - d, band, curve);
    }

    /// <summary>
    /// Worley (cellular) noise, thresholded into grains — a sponge, a stipple,
    /// a charcoal edge.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cellular rather than value noise, and the difference is the whole point:
    /// value noise is fog, and thresholding fog gives ragged blobs with no
    /// characteristic size. Worley measures the distance to the nearest of a
    /// set of scattered feature points, so the grains have a size, and that
    /// size is the cell spacing — which is a control an artist can reason
    /// about.
    /// </para>
    /// <para>
    /// Feature points come from <see cref="Hash01"/> on the cell index, not an
    /// RNG. Invariant 2 is about rendering, and a tip is baked rather than
    /// rendered — but a tip that came out differently on two machines would
    /// make the same document render differently on each, which is the same
    /// defect one level up.
    /// </para>
    /// </remarks>
    private static float Spatter(float x, float y, float radius, TipRecipe recipe, TipFalloff curve)
    {
        // Fade widens the built-in falloff, the same trade Bristle makes.
        var body = Soft(MathF.Sqrt(x * x + y * y), radius,
            0.6f * (1f - (float)Math.Clamp(recipe.Fade, 0, 1)), curve);
        if (body <= 0) return 0f;

        var cells = Math.Clamp(recipe.Count, 2, 64);
        var cell = 2f * radius / cells;
        var u = (x + radius) / cell;
        var v = (y + radius) / cell;
        int cx = (int)MathF.Floor(u), cy = (int)MathF.Floor(v);

        // Nearest feature point over the 3×3 neighbourhood: anything closer
        // than that cannot be in a cell further away.
        var nearest = float.MaxValue;
        for (var j = -1; j <= 1; j++)
        {
            for (var i = -1; i <= 1; i++)
            {
                int gx = cx + i, gy = cy + j;
                var fx = gx + Hash01(gx * 0.7391f, gy * 1.3319f, 3);
                var fy = gy + Hash01(gx * 1.9137f, gy * 0.5711f, 7);
                float dx = u - fx, dy = v - fy;
                nearest = MathF.Min(nearest, dx * dx + dy * dy);
            }
        }

        // Grain radius in cell units. Coverage is the artist's control; the
        // square root keeps the slider roughly linear in *area* covered, which
        // is what the eye reads.
        var coverage = (float)Math.Clamp(recipe.Sharpness, 0.02, 1);
        var grain = 0.62f * MathF.Sqrt(coverage);
        var edge = (grain - MathF.Sqrt(nearest)) * cell;
        return body * Step(edge);
    }

    /// <summary>
    /// A pale disc with a dark rim — one stamp of a dried wet edge.
    /// </summary>
    /// <remarks>
    /// The radial profile a drying puddle leaves: pigment carried to the
    /// contact line and stranded there, so the middle is thin and the border is
    /// dense. <c>FluidLattice</c> produces this properly from the flow, and
    /// this is the cheap stamped version for a brush that is not paying for a
    /// simulation — which is the same "every simulated medium ships a fast
    /// counterpart" rule the preset catalogue follows.
    /// </remarks>
    private static float Halo(float d, float radius, TipRecipe recipe)
    {
        var t = d / radius;
        if (t >= 1f) return Step(radius - d);

        var rim = (float)Math.Clamp(recipe.Sharpness, 0, 1);
        var floor = 0.15f + 0.45f * (1f - rim);

        // A Gaussian band sitting just inside the boundary, over a low plateau.
        const float Where = 0.86f, Width = 0.11f;
        var k = (t - Where) / Width;
        var band = MathF.Exp(-k * k);
        var value = floor + (1f - floor) * band * rim;
        return MathF.Min(value, Step(radius - d));
    }

    /// <summary>
    /// A teardrop: a round belly joined to a smaller circle by its tangent
    /// lines — the classic uneven-capsule signed distance, which is exact, so
    /// the fade band is the same width along the belly and along the taper.
    /// </summary>
    /// <remarks>
    /// Sharpness 0 is a plain disc — the degenerate case where both circles
    /// coincide — and rising sharpness both narrows the point and stretches
    /// the join, so one slider walks from a round to a fine liner's footprint.
    /// The point is up in tip space; <see cref="TipRecipe.Angle"/> and
    /// <see cref="BrushSettings.AngleFollowsDirection"/> aim it.
    /// </remarks>
    private static float Drop(float x, float y, float radius, float sharpness, float fade, TipFalloff curve)
    {
        var s = (float)Math.Clamp(sharpness, 0f, 1f);
        var r1 = radius * (1f - 0.35f * s);
        var r2 = r1 * (1f - 0.9f * s);
        var band = fade * r1;
        var h = 2f * radius - r1 - r2;
        if (h <= 1e-3f) return Edge(radius - MathF.Sqrt(x * x + y * y), band, curve);

        // Belly centre sits so the belly touches the bottom of the tip, the
        // point circle so it touches the top; px/py are measured from the
        // belly with the point along +py.
        var px = MathF.Abs(x);
        var py = (radius - r1) - y;

        var b = (r1 - r2) / h;
        var a = MathF.Sqrt(MathF.Max(1f - b * b, 1e-6f));
        var k = a * py - b * px;

        var sd = k < 0f ? r1 - MathF.Sqrt(px * px + py * py)
            : k > a * h ? r2 - MathF.Sqrt(px * px + (py - h) * (py - h))
            : r1 - (a * px + b * py);
        return Edge(sd, band, curve);
    }

    /// <summary>One disc bitten out of another, the bite widening with sharpness.</summary>
    /// <remarks>
    /// The distance is the intersection of the outer disc with the bite's
    /// complement — a max of two exact distances, which under-reports only in
    /// the corner where the two arcs meet, where a one-pixel feather cannot
    /// show the difference. Sharpness 0 leaves a barely dented disc rather
    /// than a full one, so the slider does something everywhere on its travel.
    /// </remarks>
    private static float Crescent(float x, float y, float radius, float sharpness, float fade, TipFalloff curve)
    {
        var s = (float)Math.Clamp(sharpness, 0f, 1f);
        var bite = radius * 0.95f;
        var cx = radius * (1.9f - 1.55f * s);
        // The deepest interior point sits on the -x axis where the two arcs'
        // distances agree — the crescent's own half-thickness, which is what
        // the fade normalises against so a thin sliver can still fade fully.
        var band = fade * MathF.Max((radius + cx - bite) * 0.5f, radius * 0.02f);

        var d = MathF.Sqrt(x * x + y * y);
        var dx = x - cx;
        var din = MathF.Sqrt(dx * dx + y * y);
        return Edge(MathF.Min(radius - d, din - bite), band, curve);
    }

    /// <summary>
    /// An organic blob: a disc whose boundary wobbles through a few adjacent
    /// harmonics, phases hashed from the harmonic index.
    /// </summary>
    /// <remarks>
    /// Adjacent harmonics rather than one, because a single sinusoid reads as
    /// a gear — perfectly periodic lobes are geometry again, which is the one
    /// thing this shape exists to avoid. Count sets the dominant lobe count,
    /// sharpness the depth. The wobble only ever moves the boundary inward
    /// from the tip's radius, so a size control never lies about the mark.
    /// </remarks>
    private static float Blot(float x, float y, float radius, TipRecipe recipe, float fade, TipFalloff curve)
    {
        var lobes = Math.Clamp(recipe.Count, 2, 12);
        var depth = 0.45f * (float)Math.Clamp(recipe.Sharpness, 0, 1);
        var band = fade * radius * (1f - depth);
        var theta = MathF.Atan2(y, x);

        float wobble = 0f, norm = 0f;
        for (var h = 0; h < 3; h++)
        {
            var n = lobes + h;
            var amp = 1f / (1f + h);
            var phase = Hash01(n * 12.9898f, 78.233f, 11 + h) * 2f * MathF.PI;
            wobble += amp * MathF.Sin(n * theta + phase);
            norm += amp;
        }
        wobble /= norm;

        var boundary = radius * (1f - depth * (0.5f + 0.5f * wobble));
        return Edge(boundary - MathF.Sqrt(x * x + y * y), band, curve);
    }

    /// <summary>
    /// The same position hash the brush engine seeds its dab dynamics from,
    /// so a generated tip and a painted mark agree about what "random-looking
    /// but fixed" means.
    /// </summary>
    private static float Hash01(float x, float y, int salt)
    {
        var h = BitConverter.SingleToInt32Bits(x) * 73856093
                ^ BitConverter.SingleToInt32Bits(y) * 19349663
                ^ salt * 83492791;
        h ^= h >> 13;
        h *= unchecked((int)0x5bd1e995);
        h ^= h >> 15;
        return ((uint)h % 100000u) / 100000f;
    }

    // ---- coverage -----------------------------------------------------------

    /// <summary>
    /// How much of a pixel lies on the inside of a boundary, given the signed
    /// distance to it. One pixel of transition, centred on the edge.
    /// </summary>
    private static float Step(float signedDistance) =>
        signedDistance <= -0.5f ? 0f
        : signedDistance >= 0.5f ? 1f
        : Smooth(signedDistance + 0.5f);

    /// <summary>
    /// <see cref="Step"/> with an alpha gradient behind it: full at
    /// <paramref name="band"/> pixels inside the boundary, shaped by
    /// <paramref name="curve"/> on the way there.
    /// </summary>
    /// <remarks>
    /// A zero band is <see cref="Step"/> exactly — not nearly — so every
    /// recipe baked before <see cref="TipRecipe.Fade"/> existed re-bakes to
    /// the same pixels. The min against Step keeps the one-pixel feather at
    /// the cut even under a curve that never quite reaches zero, such as the
    /// airbrush's tail.
    /// </remarks>
    private static float Edge(float signedDistance, float band, TipFalloff curve)
    {
        if (band <= 1e-3f) return Step(signedDistance);
        var t = Math.Clamp(signedDistance / band, 0f, 1f);
        return MathF.Min(Shaped(t, curve), Step(signedDistance));
    }

    /// <summary>The falloff curves, over t = 0 at the boundary to 1 at the core.</summary>
    private static float Shaped(float t, TipFalloff curve) => curve switch
    {
        TipFalloff.Linear => t,
        TipFalloff.Airbrush => MathF.Exp(-4.5f * (1f - t) * (1f - t)),
        TipFalloff.Dome => MathF.Sqrt(MathF.Max(0f, 1f - (1f - t) * (1f - t))),
        _ => Smooth(t),
    };

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    /// <summary>
    /// Coverage into a bitmap. White with the shape in alpha, matching what
    /// the importers produce, so <c>BrushTipRegistry</c> needs no special case.
    /// </summary>
    private static SKBitmap ToBitmap(float[] alpha, int size)
    {
        var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);
        var bytes = new byte[size * size * 4];
        for (int i = 0, o = 0; i < alpha.Length; i++, o += 4)
        {
            bytes[o] = 255;
            bytes[o + 1] = 255;
            bytes[o + 2] = 255;
            bytes[o + 3] = (byte)(Math.Clamp(alpha[i], 0f, 1f) * 255f + 0.5f);
        }
        System.Runtime.InteropServices.Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
        return bitmap;
    }
}
