using System.Buffers;
using Lightbox.Core.Documents;
using Lightbox.Raster.Media;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// What a rewetting brush does to the paint it passes over
/// (<see cref="WetSettings"/>, Q255): the paint bleeds out into the wet area
/// and thins, unevenly, and what the water pushed gathers where the water
/// stopped.
/// </summary>
/// <remarks>
/// <para>
/// <b>A function of two things and nothing else</b>: the layer as it was
/// before the stroke, and where the brush has been. Each pixel of the result
/// is computed from those alone, so any rectangle of it can be rendered on
/// its own and comes out the same as in a render of the whole stroke - which
/// is what lets the live path redo only the band the pen has just reached and
/// still show exactly what a replay of the record draws
/// (<c>WaterRewetsTests.APieceIsTheWholeToTheByte</c>). Nothing here goes
/// through a canvas: integer sums for the spread, one expression per pixel for
/// the rest.
/// </para>
/// <para>
/// <b>Where the brush has been</b> is a wetness per pixel: the most any one
/// dab puts there, a soft disc scaled by flow and pressure. A maximum, so the
/// order of the dabs and how many overlap do not matter, and going over the
/// same place in one stroke does not make it wetter - a second stroke does,
/// because it starts from what the first left.
/// </para>
/// <para>
/// <b>The spread</b> is the layer blurred: three passes of a box, which is a
/// Gaussian to the eye, summed in integers so the answer does not depend on
/// where the summing started. <b>The unevenness</b> is a field fixed to the
/// paper - two octaves of value noise hashed from document coordinates, never
/// a random number (invariant 2) - which decides where water pooled: in a
/// pool the pigment is pushed out, and beside it there is a little more.
/// <b>The gathered line</b> sits where the brush's own falloff passes through
/// its middle - the edge of where it was, whatever the pressure.
/// </para>
/// </remarks>
public static class WetBleed
{
    /// <summary>How far past its dabs a stroke can change a pixel: nothing, the water is where the brush was.</summary>
    public static SKRectI Bounds(
        IReadOnlyList<BrushEngine.Dab> dabs, int from, int to, BrushSettings brush, int width, int height,
        double scale = 1.0, SKPointI origin = default)
    {
        float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
        for (var i = Math.Max(0, from); i < Math.Min(to, dabs.Count); i++)
        {
            var d = dabs[i];
            var r = (float)(BrushEngine.RadiusAt(brush, d.Pressure) * scale) + 1;
            float x = (float)((d.Pos.X - origin.X) * scale), y = (float)((d.Pos.Y - origin.Y) * scale);
            left = Math.Min(left, x - r); top = Math.Min(top, y - r);
            right = Math.Max(right, x + r); bottom = Math.Max(bottom, y + r);
        }
        if (left > right) return SKRectI.Empty;
        var box = new SKRectI((int)Math.Floor(left), (int)Math.Floor(top), (int)Math.Ceiling(right), (int)Math.Ceiling(bottom));
        return SKRectI.Intersect(box, new SKRectI(0, 0, width, height));
    }

    /// <summary>The radius of one box pass of the spread, in pixels of the surface.</summary>
    public static int SpreadRadius(BrushSettings brush, double scale = 1.0)
    {
        var wet = brush.Wet;
        if (wet is null) return 0;
        return (int)Math.Round(Math.Clamp(wet.Spread, 0, 1) * Math.Max(1, brush.Size) * scale * 0.5);
    }

    /// <summary>How far outside a rectangle the layer has to be read to render it: three box passes.</summary>
    public static int ReadMargin(BrushSettings brush, double scale = 1.0)
    {
        var k = SpreadRadius(brush, scale);
        if (k <= 0) return 0;
        var d = Cell(k);
        return ((Math.Max(1, (int)Math.Round(k / (double)d)) * 3) + 3) * d;
    }

    /// <summary>
    /// Render <paramref name="region"/> of the stroke's result into
    /// <paramref name="dest"/> (premultiplied RGBA, the region's size, tightly
    /// packed). <paramref name="pre"/> is the whole layer as it was before the
    /// stroke, premultiplied RGBA with <paramref name="rowBytes"/> to a row.
    /// </summary>
    public static void Render(
        ReadOnlySpan<byte> pre, int width, int height, int rowBytes,
        IReadOnlyList<BrushEngine.Dab> dabs, BrushSettings brush, SKRectI region, Span<byte> dest,
        double scale = 1.0, SKPointI origin = default)
    {
        var wet = brush.Wet ?? new WetSettings();
        region = SKRectI.Intersect(region, new SKRectI(0, 0, width, height));
        int rw = region.Width, rh = region.Height;
        if (rw <= 0 || rh <= 0) return;

        // 1. Wetness: the most any one dab puts on each pixel.
        var mask = ArrayPool<float>.Shared.Rent(rw * rh);
        Array.Clear(mask, 0, rw * rh);
        // And where the brush was at all, whatever the pressure: its edge is
        // the edge of the water. Wetness alone would not do - a lightly pressed
        // stretch is half wet all over, and is not an edge anywhere.
        var cover = ArrayPool<float>.Shared.Rent(rw * rh);
        Array.Clear(cover, 0, rw * rh);
        var flow = Math.Clamp(brush.Flow, 0, 1);
        var touched = false;
        foreach (var d in dabs)
        {
            var radius = BrushEngine.RadiusAt(brush, d.Pressure) * scale;
            if (radius <= 0) continue;
            double cx = (d.Pos.X - origin.X) * scale, cy = (d.Pos.Y - origin.Y) * scale;
            int x0 = Math.Max(region.Left, (int)Math.Floor(cx - radius)), x1 = Math.Min(region.Right - 1, (int)Math.Ceiling(cx + radius));
            int y0 = Math.Max(region.Top, (int)Math.Floor(cy - radius)), y1 = Math.Min(region.Bottom - 1, (int)Math.Ceiling(cy + radius));
            if (x0 > x1 || y0 > y1) continue;
            var hardness = Math.Clamp(BrushEngine.HardnessOf(brush, d.Pressure), 0, 0.98);
            var strength = (float)(flow * BrushEngine.WetnessOfPressure(brush, d.Pressure));
            if (strength <= 0) continue;
            touched = true;
            for (var y = y0; y <= y1; y++)
            {
                var dy = (y + 0.5) - cy;
                var row = (y - region.Top) * rw;
                for (var x = x0; x <= x1; x++)
                {
                    var dx = (x + 0.5) - cx;
                    var t = Math.Sqrt((dx * dx) + (dy * dy)) / radius;
                    if (t >= 1) continue;
                    // Solid to the hardness, then a smooth fall to the rim.
                    var f = t <= hardness ? 1.0 : 1 - ((t - hardness) / (1 - hardness));
                    f = f * f * (3 - (2 * f));
                    var v = (float)f * strength;
                    var i = row + (x - region.Left);
                    if (v > mask[i]) mask[i] = v;
                    if (f > cover[i]) cover[i] = (float)f;
                }
            }
        }

        // Nothing wet in this rectangle: it is the layer as it was.
        if (!touched)
        {
            for (var y = 0; y < rh; y++)
            {
                pre.Slice(((region.Top + y) * rowBytes) + (region.Left * 4), rw * 4).CopyTo(dest.Slice(y * rw * 4, rw * 4));
            }
            ArrayPool<float>.Shared.Return(mask);
            ArrayPool<float>.Shared.Return(cover);
            return;
        }

        // 2. The spread: the layer blurred, over the rectangle.
        var k = SpreadRadius(brush, scale);
        var blurred = ArrayPool<byte>.Shared.Rent(rw * rh * 4);
        Blur(pre, width, height, rowBytes, region, k, blurred);

        // 3. One expression per pixel.
        var thin = (float)Math.Clamp(wet.Thin, 0, 1);
        var clouds = (float)Math.Clamp(wet.Cloud, 0, 1);
        var gather = (float)Math.Clamp(wet.Gather, 0, 1);
        var settle = brush.TextureSurface is null ? 0f : (float)Math.Clamp(brush.TextureSettle ?? 0, 0, 1);
        float[]? paper = null;
        if (settle > 0)
        {
            paper = ArrayPool<float>.Shared.Rent(rw * rh);
            // The sheet is fixed to the document, like every paper.
            PaperField.Fill(
                paper.AsSpan(0, rw * rh), rw, rh, region.Left + (int)Math.Round(origin.X * scale), region.Top + (int)Math.Round(origin.Y * scale),
                brush.TextureSurface!.Value, Math.Max(1.5, brush.TextureScale * scale));
        }
        var cell = Math.Max(6.0, brush.Size * 0.45);
        for (var y = 0; y < rh; y++)
        {
            var src = pre.Slice(((region.Top + y) * rowBytes) + (region.Left * 4), rw * 4);
            var docY = ((region.Top + y + 0.5) / scale) + origin.Y;
            for (var x = 0; x < rw; x++)
            {
                var i = (y * rw) + x;
                var o = i * 4;
                var m = mask[i];
                if (m <= 0)
                {
                    src.Slice(x * 4, 4).CopyTo(dest.Slice(o, 4));
                    continue;
                }

                var docX = ((region.Left + x + 0.5) / scale) + origin.X;
                var c = clouds > 0 ? Cloud(docX, docY, cell) : 0.5f;
                // Where water pooled the pigment is pushed out; beside a pool there is a little more.
                var pool = Math.Clamp((c - 0.45f) * 2.2f, 0f, 1f);
                var bank = Math.Clamp((0.45f - c) * 2.2f, 0f, 1f);
                var carried = (1f - (0.9f * clouds * pool)) * (1f + (0.5f * clouds * bank));
                var mix = m * (1f - clouds + (clouds * Math.Clamp(c * 1.6f, 0f, 1f)));
                // The edge of the water: where the brush's own falloff passes
                // through its middle, as strong as the brush was wet there.
                var covered = cover[i];
                var edge = 4f * covered * (1f - covered) * Math.Min(1f, m / Math.Max(covered, 0.05f));
                var keep = 1f - (thin * m);
                var line = gather * edge * (0.35f + c);
                var sheet = paper is null ? 1f : 1f - (settle * paper[i] * m);

                float a = 0;
                for (var ch = 3; ch >= 0; ch--)
                {
                    var p = src[(x * 4) + ch];
                    var b = blurred[o + ch] * carried;
                    var v = (p + ((b - p) * mix)) * keep;
                    v += blurred[o + ch] * line;
                    v *= sheet;
                    v = Math.Clamp(v, 0f, 255f);
                    // Premultiplied: a colour channel can never exceed the alpha.
                    if (ch == 3) a = v;
                    else if (v > a) v = a;
                    dest[o + ch] = (byte)(v + 0.5f);
                }
            }
        }

        if (paper is not null) ArrayPool<float>.Shared.Return(paper);
        ArrayPool<byte>.Shared.Return(blurred);
        ArrayPool<float>.Shared.Return(mask);
        ArrayPool<float>.Shared.Return(cover);
    }

    /// <summary>
    /// How many pixels a cell of the spread's working grid is across. A wide
    /// blur has no detail finer than a fraction of its radius, so it is
    /// computed on a coarser grid and read back smoothly: a sixteenth of the
    /// work at a radius of thirty, and nothing an eye can find.
    /// </summary>
    internal static int Cell(int k) => k >= 32 ? 8 : k >= 16 ? 4 : k >= 8 ? 2 : 1;

    /// <summary>
    /// The layer blurred, over <paramref name="region"/>: averaged onto a grid
    /// of <see cref="Cell"/>-pixel cells fixed to the layer's origin, three box
    /// passes there, and read back between cell centres. It reads as far
    /// outside the rectangle as that needs, and nothing where the layer ends.
    /// Integer sums on the grid and one expression per pixel off it, so a
    /// pixel's value does not depend on which rectangle it was computed as
    /// part of.
    /// </summary>
    private static void Blur(
        ReadOnlySpan<byte> pre, int width, int height, int rowBytes, SKRectI region, int k, Span<byte> dest)
    {
        int rw = region.Width, rh = region.Height;
        if (k <= 0)
        {
            for (var y = 0; y < rh; y++)
            {
                pre.Slice(((region.Top + y) * rowBytes) + (region.Left * 4), rw * 4).CopyTo(dest.Slice(y * rw * 4, rw * 4));
            }
            return;
        }

        var d = Cell(k);
        var ks = Math.Max(1, (int)Math.Round(k / (double)d));
        // The cells the rectangle is read from (one beyond it each way, for the
        // read-back between centres), and what three passes reach beyond those.
        var margin = (ks * 3) + 1;
        int gx = FloorDiv(region.Left, d) - 1 - margin, gy = FloorDiv(region.Top, d) - 1 - margin;
        int gw = FloorDiv(region.Right - 1, d) + 1 + margin - gx + 1, gh = FloorDiv(region.Bottom - 1, d) + 1 + margin - gy + 1;
        var a = ArrayPool<int>.Shared.Rent(gw * gh);
        var b = ArrayPool<int>.Shared.Rent(gw * gh);
        var window = (2 * ks) + 1;
        var area = d * d;
        for (var ch = 0; ch < 4; ch++)
        {
            // Each cell: the mean of the pixels under it, sixteen times over so
            // the rounded divisions that follow lose nothing an eye could find.
            for (var cy = 0; cy < gh; cy++)
            {
                var py0 = (gy + cy) * d;
                for (var cx = 0; cx < gw; cx++)
                {
                    var px0 = (gx + cx) * d;
                    var sum = 0;
                    for (var y = Math.Max(0, py0); y < Math.Min(height, py0 + d); y++)
                    {
                        var line = pre.Slice(y * rowBytes, width * 4);
                        for (var x = Math.Max(0, px0); x < Math.Min(width, px0 + d); x++) sum += line[(x * 4) + ch];
                    }
                    a[(cy * gw) + cx] = ((sum << 4) + (area >> 1)) / area;
                }
            }

            for (var pass = 0; pass < 3; pass++)
            {
                BoxRows(a, b, gw, gh, ks, window);
                BoxColumns(b, a, gw, gh, ks, window);
            }

            // Read back between cell centres.
            for (var y = 0; y < rh; y++)
            {
                var v = ((region.Top + y + 0.5) / d) - 0.5;
                var iy = (int)Math.Floor(v);
                var fy = (float)(v - iy);
                var rowA = (iy - gy) * gw;
                var rowB = rowA + gw;
                for (var x = 0; x < rw; x++)
                {
                    var u = ((region.Left + x + 0.5) / d) - 0.5;
                    var ix = (int)Math.Floor(u);
                    var fx = (float)(u - ix);
                    var c0 = ix - gx;
                    var top = a[rowA + c0] + ((a[rowA + c0 + 1] - a[rowA + c0]) * fx);
                    var bottom = a[rowB + c0] + ((a[rowB + c0 + 1] - a[rowB + c0]) * fx);
                    var value = (top + ((bottom - top) * fy)) / 16f;
                    dest[(((y * rw) + x) * 4) + ch] = (byte)Math.Clamp(value + 0.5f, 0f, 255f);
                }
            }
        }
        ArrayPool<int>.Shared.Return(a);
        ArrayPool<int>.Shared.Return(b);
    }

    private static int FloorDiv(int value, int by) => (int)Math.Floor(value / (double)by);

    private static void BoxRows(int[] from, int[] to, int w, int h, int k, int window)
    {
        for (var y = 0; y < h; y++)
        {
            var row = y * w;
            long sum = 0;
            for (var x = 0; x <= Math.Min(k, w - 1); x++) sum += from[row + x];
            for (var x = 0; x < w; x++)
            {
                to[row + x] = (int)((sum + (window >> 1)) / window);
                var leaving = x - k;
                var entering = x + k + 1;
                if (leaving >= 0) sum -= from[row + leaving];
                if (entering < w) sum += from[row + entering];
            }
        }
    }

    private static void BoxColumns(int[] from, int[] to, int w, int h, int k, int window)
    {
        for (var x = 0; x < w; x++)
        {
            long sum = 0;
            for (var y = 0; y <= Math.Min(k, h - 1); y++) sum += from[(y * w) + x];
            for (var y = 0; y < h; y++)
            {
                to[(y * w) + x] = (int)((sum + (window >> 1)) / window);
                var leaving = y - k;
                var entering = y + k + 1;
                if (leaving >= 0) sum -= from[(leaving * w) + x];
                if (entering < h) sum += from[(entering * w) + x];
            }
        }
    }

    /// <summary>
    /// Where water pools, 0..1, fixed to the document: two octaves of value
    /// noise on a lattice of <paramref name="cell"/> pixels, hashed from the
    /// lattice point. The same place is the same value in every stroke and on
    /// every machine.
    /// </summary>
    private static float Cloud(double x, double y, double cell)
    {
        var coarse = Noise(x / cell, y / cell, 0x51ED27);
        var fine = Noise(x / (cell * 0.37), y / (cell * 0.37), 0x2C1B3D);
        return Math.Clamp((coarse * 0.68f) + (fine * 0.32f), 0f, 1f);
    }

    private static float Noise(double u, double v, int salt)
    {
        var x0 = (int)Math.Floor(u);
        var y0 = (int)Math.Floor(v);
        var fx = (float)(u - x0);
        var fy = (float)(v - y0);
        fx = fx * fx * (3 - (2 * fx));
        fy = fy * fy * (3 - (2 * fy));
        var a = Lattice(x0, y0, salt);
        var b = Lattice(x0 + 1, y0, salt);
        var c = Lattice(x0, y0 + 1, salt);
        var d = Lattice(x0 + 1, y0 + 1, salt);
        var top = a + ((b - a) * fx);
        var bottom = c + ((d - c) * fx);
        return top + ((bottom - top) * fy);
    }

    private static float Lattice(int x, int y, int salt)
    {
        unchecked
        {
            var h = (uint)((x * 0x27D4EB2D) ^ (y * 0x165667B1) ^ salt);
            h ^= h >> 15;
            h *= 0x2C1B3C6D;
            h ^= h >> 12;
            h *= 0x297A2D39;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
