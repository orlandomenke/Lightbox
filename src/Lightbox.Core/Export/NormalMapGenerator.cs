namespace Lightbox.Core.Export;

/// <summary>Which way green points in a tangent-space normal map.</summary>
/// <remarks>
/// <para>
/// <b>The same class of bug as the sprite pivot, and it will be debugged the same
/// wrong way.</b> Get this backwards and the lighting looks inverted — a character
/// lit from above reads as lit from below, every bevel becomes a groove — and
/// nothing about it points at a channel convention. So it is a named parameter with
/// a test on each value, not a constant somebody picked.
/// </para>
/// <para>
/// <b>OpenGL</b> (+Y up) is what Unity expects; <b>DirectX</b> (−Y up) is what
/// Unreal expects. The mnemonic that settles arguments: on an OpenGL-convention
/// map of a sphere, green is <em>bright at the top</em>.
/// </para>
/// </remarks>
public enum NormalGreen
{
    /// <summary>+Y up. Unity, Godot, most engines.</summary>
    OpenGl,

    /// <summary>−Y up. Unreal.</summary>
    DirectX,
}

/// <param name="BevelPixels">
/// How far in from the silhouette edge the surface finishes rising, in pixels. The
/// only control that changes the *shape* of the result rather than its strength.
/// </param>
/// <param name="Strength">
/// How steeply the bevel tilts. 1 is a natural rounded edge; higher exaggerates.
/// </param>
/// <param name="Green">Tangent-space convention. See <see cref="NormalGreen"/>.</param>
/// <param name="Rounded">
/// A domed edge rather than a flat chamfer. On by default because a chamfer leaves a
/// visible crease where it meets the flat interior — the derivative jumps, and a hard
/// line appears under a moving light that no artist put there.
/// </param>
public sealed record NormalMapOptions(
    double BevelPixels = 6,
    double Strength = 1.0,
    NormalGreen Green = NormalGreen.OpenGl,
    bool Rounded = true);

/// <summary>
/// A tangent-space normal map derived from a sprite's silhouette.
/// </summary>
/// <remarks>
/// <para>
/// Tier one of the three the roadmap names, and it is first because it needs
/// <b>no dependency and no model</b>: the alpha channel already says where the
/// drawing is, a distance transform turns that into a height, and a Sobel over the
/// height gives the normals. Laigter and an AI pass can do things this cannot —
/// knowing a cheek is round and a sleeve fold is a crease — but neither is needed
/// to make the panel, the export path and the preview light real.
/// </para>
/// <para>
/// <b>Deterministic by construction.</b> No RNG, no seed, no sampling: the output is
/// a pure function of the alpha channel and the options. Invariant 2 is not strained
/// here, it is trivially satisfied, which is the other reason this tier comes first.
/// </para>
/// <para>
/// <b>Pure, and in Core on purpose.</b> It takes an alpha array and returns RGBA
/// bytes, so every claim about it is testable from a hand-built silhouette without
/// composing a frame — the same reason <see cref="UnityConvert"/> lives here.
/// </para>
/// <para>
/// What it deliberately does <em>not</em> do is light anything. A preview light
/// belongs in the view, over the map; baking one in would produce an image that
/// looks right in the app and is wrong in every engine, and the light an artist
/// dragged around while judging it would ship inside the asset.
/// </para>
/// </remarks>
public static class NormalMapGenerator
{
    /// <summary>Below this a pixel is outside the silhouette. Matches the exporter's ink threshold.</summary>
    private const byte InkAlpha = 8;

    /// <summary>The flat normal, encoded. What every transparent pixel gets.</summary>
    private const byte FlatXy = 128;

    /// <summary>
    /// Distance from the outside of the silhouette, in pixels, per pixel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A two-pass chamfer transform with 3-4 weights, divided by three: an integer
    /// approximation of Euclidean distance that is within a few percent and costs two
    /// sweeps rather than a search. Exact Euclidean distance would change the bevel by
    /// less than the antialiasing on the edge it is measuring from, so the cheap one is
    /// correct here — the medium-simulation rule applied to a filter.
    /// </para>
    /// <para>
    /// Exposed because it is worth testing on its own: a wrong distance field produces
    /// a plausible-looking normal map that is subtly the wrong shape, and that is much
    /// harder to see than a wrong number.
    /// </para>
    /// </remarks>
    public static double[] DistanceInside(ReadOnlySpan<byte> alpha, int width, int height)
    {
        var d = new double[width * height];
        const double far = 1e9;

        for (var i = 0; i < d.Length; i++) d[i] = alpha[i] < InkAlpha ? 0 : far;

        // Forward: up-left neighbourhood.
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                if (d[i] == 0) continue;
                var best = d[i];
                if (x > 0) best = Math.Min(best, d[i - 1] + 3);
                if (y > 0) best = Math.Min(best, d[i - width] + 3);
                if (x > 0 && y > 0) best = Math.Min(best, d[i - width - 1] + 4);
                if (x < width - 1 && y > 0) best = Math.Min(best, d[i - width + 1] + 4);
                d[i] = best;
            }
        }

        // Backward: down-right neighbourhood. Both sweeps are required — one alone
        // leaves the distance wrong wherever the nearest edge is behind the scan.
        for (var y = height - 1; y >= 0; y--)
        {
            for (var x = width - 1; x >= 0; x--)
            {
                var i = y * width + x;
                if (d[i] == 0) continue;
                var best = d[i];
                if (x < width - 1) best = Math.Min(best, d[i + 1] + 3);
                if (y < height - 1) best = Math.Min(best, d[i + width] + 3);
                if (x < width - 1 && y < height - 1) best = Math.Min(best, d[i + width + 1] + 4);
                if (x > 0 && y < height - 1) best = Math.Min(best, d[i + width - 1] + 4);
                d[i] = best;
            }
        }

        for (var i = 0; i < d.Length; i++) d[i] = d[i] >= far ? 0 : d[i] / 3.0;
        return d;
    }

    /// <summary>
    /// The height field the normals are taken from: 0 at the edge, 1 in the interior.
    /// </summary>
    /// <remarks>
    /// <paramref name="options"/>'s <c>Rounded</c> shapes the rise with a quarter sine,
    /// whose derivative is zero at the top. That is the whole point of it: a linear ramp
    /// has a discontinuous derivative where it meets the flat interior, and a
    /// discontinuous derivative is a visible line under a moving light.
    /// </remarks>
    public static double[] Height(double[] distance, NormalMapOptions options)
    {
        var bevel = Math.Max(0.5, options.BevelPixels);
        var h = new double[distance.Length];
        for (var i = 0; i < h.Length; i++)
        {
            var t = Math.Clamp(distance[i] / bevel, 0, 1);
            h[i] = options.Rounded ? Math.Sin(t * Math.PI / 2) : t;
        }
        return h;
    }

    /// <summary>
    /// A normal map as RGBA bytes: RGB is the encoded normal, A is the sprite's own alpha.
    /// </summary>
    /// <remarks>
    /// Alpha is carried through rather than set opaque, so the map masks exactly as the
    /// sprite does and an engine sampling outside the silhouette gets nothing rather
    /// than a flat normal it might light.
    /// </remarks>
    public static byte[] Generate(
        ReadOnlySpan<byte> alpha, int width, int height, NormalMapOptions? options = null)
    {
        var opts = options ?? new NormalMapOptions();
        var rgba = new byte[width * height * 4];
        if (width <= 0 || height <= 0) return rgba;

        var h = Height(DistanceInside(alpha, width, height), opts);
        var strength = Math.Max(0, opts.Strength);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                var o = i * 4;
                rgba[o + 3] = alpha[i];

                if (alpha[i] < InkAlpha)
                {
                    // Flat outside the drawing. Not zero: (0,0,0) decodes to a normal
                    // pointing away from the surface, which lights as a black hole in
                    // any engine that samples the edge.
                    rgba[o] = FlatXy;
                    rgba[o + 1] = FlatXy;
                    rgba[o + 2] = 255;
                    continue;
                }

                // Sobel, clamped at the borders. Clamping rather than wrapping: a
                // sprite's edge is an edge, and wrapping would slope the left of the
                // cell according to what is on its right.
                var gx = Sobel(h, width, height, x, y, horizontal: true);
                var gy = Sobel(h, width, height, x, y, horizontal: false);

                // nx = -dh/dx, ny = +dh/dy for OpenGL. Image Y runs down and world Y
                // runs up, and those two sign choices are what the enum exists for.
                var nx = -gx * strength;
                var ny = gy * strength * (opts.Green == NormalGreen.DirectX ? -1 : 1);
                var nz = 1.0;

                var len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                nx /= len;
                ny /= len;
                nz /= len;

                rgba[o] = Encode(nx);
                rgba[o + 1] = Encode(ny);
                rgba[o + 2] = Encode(nz);
            }
        }
        return rgba;
    }

    /// <summary>A signed component in −1..1 as an unsigned byte.</summary>
    private static byte Encode(double v) =>
        (byte)Math.Clamp((int)Math.Round((v * 0.5 + 0.5) * 255), 0, 255);

    private static double Sobel(double[] h, int width, int height, int x, int y, bool horizontal)
    {
        double At(int px, int py) =>
            h[Math.Clamp(py, 0, height - 1) * width + Math.Clamp(px, 0, width - 1)];

        if (horizontal)
        {
            return (At(x + 1, y - 1) + 2 * At(x + 1, y) + At(x + 1, y + 1)
                    - At(x - 1, y - 1) - 2 * At(x - 1, y) - At(x - 1, y + 1)) / 8.0;
        }
        return (At(x - 1, y + 1) + 2 * At(x, y + 1) + At(x + 1, y + 1)
                - At(x - 1, y - 1) - 2 * At(x, y - 1) - At(x + 1, y - 1)) / 8.0;
    }
}
