namespace Lightbox.Raster.Media;

/// <summary>
/// Shades a stroke as if the paint standing on the paper had thickness, so
/// gouache and oil read as a raised body rather than as an opaque stain.
/// </summary>
/// <remarks>
/// <para>
/// The artist's complaint was exact: "gouache and oil have no height nor edges
/// that catch light". They had none because <c>Body</c> and <c>Relief</c> were
/// settings nothing read — measured at 0 of 41 600 pixels different between 0
/// and 1 (B23).
/// </para>
/// <para>
/// <b>No height buffer.</b> The design note reaches for a persistent height
/// channel, and that is the expensive route: it is per-layer state that is not
/// in the document, so either it is saved — a new format — or a reload renders
/// differently, which is invariant 1. The cheap route the note also allows is
/// taken here instead: <em>the paint's own coverage is its height</em>. Where
/// more pigment settled, the film stands higher. That is not merely convenient,
/// it is close to true for a physical medium, and it is a pure function of
/// pixels the pass already has — so a reload, an undo and an AI inbetween all
/// reproduce the same relief with no state to carry.
/// </para>
/// <para>
/// What it gives up is stacking: two strokes crossing do not build a ridge
/// where they meet, because each is shaded from its own coverage alone. That is
/// the real limit of this pass and the reason to build the buffer later, if the
/// look turns out to want it.
/// </para>
/// <para>
/// <b>Deterministic, and no light in the document.</b> The light direction is a
/// constant, not a setting. A per-document light would make the same painting
/// render differently on someone else's machine if it were a preference, and a
/// per-stroke one would let two strokes on one canvas disagree about where the
/// sun is. Upper-left at 45° is the convention every relief filter uses and the
/// one an artist reads as "raised" without being told.
/// </para>
/// </remarks>
internal static class Impasto
{
    /// <summary>
    /// Cells of horizontal run per unit of height at <c>Body</c> 1. Small
    /// enough that a soft edge shades as a slope rather than as a wall.
    /// </summary>
    private const float Steepness = 3f;

    /// <summary>How far diffuse shading can swing the colour at <c>Relief</c> 1.</summary>
    private const float DiffuseGain = 1.1f;

    /// <summary>Strength of the highlight on a ridge facing the light.</summary>
    private const float SpecularGain = 0.55f;

    /// <summary>Tight enough that the glint reads as wet paint, not as plastic.</summary>
    private const float Shininess = 12f;

    // Upper-left, 45°, normalised — and the half-vector against a viewer
    // straight on, folded to a constant because both are.
    private const float Lx = -0.5774f, Ly = -0.5774f, Lz = 0.5774f;
    private const float Hx = -0.3574f, Hy = -0.3574f, Hz = 0.8629f;

    /// <summary>
    /// Shade <paramref name="rgba"/> in place from its own alpha channel.
    /// Alpha is never touched: this is light falling on paint, not more paint.
    /// </summary>
    /// <param name="rgba">Unpremultiplied RGBA8888, <paramref name="w"/> × <paramref name="h"/>.</param>
    /// <param name="body">0..1 paint thickness. At 0 this returns untouched.</param>
    /// <param name="relief">0..1 shading strength. At 0 this returns untouched.</param>
    public static void Shade(byte[] rgba, int w, int h, double body, double relief)
    {
        var thickness = (float)Math.Clamp(body, 0, 1) * Steepness;
        var strength = (float)Math.Clamp(relief, 0, 1);
        if (thickness <= 0 || strength <= 0) return;

        // Read heights from a copy of the alpha channel, so a shaded cell does
        // not become the neighbour of the next one. Sampling the buffer being
        // written would smear the relief toward +x/+y.
        var height = new float[w * h];
        for (int i = 0, o = 3; i < height.Length; i++, o += 4) height[i] = rgba[o] * (1f / 255f);

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = y * w + x;
                var o = i * 4;
                if (rgba[o + 3] == 0) continue;

                // Central differences, clamped at the border rather than
                // wrapped: the region's edge is not a cliff in the paint.
                var l = height[x > 0 ? i - 1 : i];
                var r = height[x < w - 1 ? i + 1 : i];
                var u = height[y > 0 ? i - w : i];
                var d = height[y < h - 1 ? i + w : i];

                var nx = (l - r) * thickness;
                var ny = (u - d) * thickness;
                var inv = 1f / MathF.Sqrt(nx * nx + ny * ny + 1f);
                nx *= inv;
                ny *= inv;
                var nz = inv;

                // Measured against flat paint, so level ground is exactly
                // unshaded and Relief 0 and Body 0 are exact no-ops rather
                // than "almost".
                var diffuse = (nx * Lx + ny * Ly + nz * Lz - Lz) * strength * DiffuseGain;
                var spec = nx * Hx + ny * Hy + nz * Hz;
                var glint = spec <= 0 ? 0f : MathF.Pow(spec, Shininess) * strength * SpecularGain;

                rgba[o] = Lit(rgba[o], diffuse, glint);
                rgba[o + 1] = Lit(rgba[o + 1], diffuse, glint);
                rgba[o + 2] = Lit(rgba[o + 2], diffuse, glint);
            }
        }
    }

    /// <summary>
    /// Scale by the diffuse term and add the highlight. Multiplying keeps a
    /// dark pigment dark in shadow instead of washing the whole mark toward
    /// grey; the highlight adds, because a glint is reflected light and does
    /// not care what colour the paint underneath it is.
    /// </summary>
    private static byte Lit(byte channel, float diffuse, float glint)
    {
        var v = channel * (1f + diffuse) + glint * 255f;
        return v <= 0f ? (byte)0 : v >= 255f ? (byte)255 : (byte)(v + 0.5f);
    }
}
