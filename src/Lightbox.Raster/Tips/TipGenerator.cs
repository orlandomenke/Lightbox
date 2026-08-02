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
                    TipShape.SoftCircle => Soft(MathF.Sqrt(rx * rx + ry * ry), radius, (float)recipe.Hardness),
                    TipShape.Ring => Ring(MathF.Sqrt(rx * rx + ry * ry), radius, (float)recipe.InnerRadius),
                    TipShape.Chisel => Chisel(rx, ry, radius, (float)recipe.Roundness),
                    TipShape.Hatch => Hatch(rx, ry, radius, recipe),
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
    /// Full inside the hard core, then a smooth shoulder out to the radius.
    /// </summary>
    /// <remarks>
    /// Smoothstep rather than a straight ramp, because the linear one leaves a
    /// visible crease where the core meets the fade and an artist reads that
    /// crease as a second, smaller brush inside the first.
    /// </remarks>
    private static float Soft(float d, float radius, float hardness)
    {
        var core = radius * Math.Clamp(hardness, 0f, 1f);
        if (d <= core) return 1f;
        if (d >= radius) return 0f;
        // Degenerate only if hardness is 1, and Disc's feather is the right
        // answer there rather than a divide by zero.
        if (radius - core < 1e-4f) return Step(radius - d);
        var t = (d - core) / (radius - core);
        return 1f - Smooth(t);
    }

    private static float Ring(float d, float radius, float inner)
    {
        var r0 = radius * Math.Clamp(inner, 0f, 0.99f);
        return MathF.Min(Step(radius - d), Step(d - r0));
    }

    /// <summary>An ellipse squashed across its own axis.</summary>
    private static float Chisel(float x, float y, float radius, float roundness)
    {
        var minor = radius * Math.Clamp(roundness, 0.02f, 1f);
        // Distance to the ellipse boundary, scaled back into pixels so the
        // feather is one pixel wide on the page rather than one unit wide in
        // a normalised space where it would be far too soft on the short axis.
        var nx = x / radius;
        var ny = y / minor;
        var n = MathF.Sqrt(nx * nx + ny * ny);
        if (n <= 0) return 1f;
        var scale = MathF.Sqrt(x * x + y * y) / n;
        return Step((1f - n) * scale);
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
    private static float Hatch(float x, float y, float radius, TipRecipe recipe)
    {
        var spacing = MathF.Max(2f, (float)recipe.Spacing);
        var half = MathF.Max(0.5f, (float)recipe.LineWidth * 0.5f);

        var rule = Line(x, spacing, half);
        if (recipe.Crossed) rule = MathF.Max(rule, Line(y, spacing, half));

        // Clipped to the round footprint, or the tip stamps its square.
        return MathF.Min(rule, Step(radius - MathF.Sqrt(x * x + y * y)));
    }

    private static float Line(float v, float spacing, float half)
    {
        var phase = v - MathF.Floor(v / spacing + 0.5f) * spacing;
        return Step(half - MathF.Abs(phase));
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
