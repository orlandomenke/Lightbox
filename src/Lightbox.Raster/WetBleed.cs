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
        double scale = 1.0, SKPointI origin = default, Spread? spread = null)
    {
        var wet = brush.Wet ?? new WetSettings();
        region = SKRectI.Intersect(region, new SKRectI(0, 0, width, height));
        int rw = region.Width, rh = region.Height;
        if (rw <= 0 || rh <= 0) return;

        // 1. Wetness: the most any one dab puts on each pixel. And where the
        // brush was at all, whatever the pressure: its edge is the edge of the
        // water. Wetness alone would not do - a lightly pressed stretch is half
        // wet all over, and is not an edge anywhere.
        var mask = ArrayPool<float>.Shared.Rent(rw * rh);
        Array.Clear(mask, 0, rw * rh);
        var cover = ArrayPool<float>.Shared.Rent(rw * rh);
        Array.Clear(cover, 0, rw * rh);
        var touched = Wetness(dabs, 0, dabs.Count, brush, region, mask, cover, scale, origin);
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
        (spread is { } kept && kept.Fits(width, height, k) ? kept : new Spread(width, height, k))
            .Read(pre, rowBytes, region, blurred);

        // 3. One expression per pixel.
        var look = new Look(brush);
        var clouds = look.Clouds;
        var settle = look.Settle;
        float[]? paper = null;
        if (settle > 0)
        {
            paper = ArrayPool<float>.Shared.Rent(rw * rh);
            // The sheet is fixed to the document, like every paper.
            PaperField.Fill(
                paper.AsSpan(0, rw * rh), rw, rh, region.Left + (int)Math.Round(origin.X * scale), region.Top + (int)Math.Round(origin.Y * scale),
                brush.TextureSurface!.Value, Math.Max(1.5, brush.TextureScale * scale));
        }
        var coarse = look.Coarse;
        var fine = look.Fine;
        var columns = ArrayPool<double>.Shared.Rent(rw);
        for (var x = 0; x < rw; x++) columns[x] = ((region.Left + x + 0.5) / scale) + origin.X;
        for (var y = 0; y < rh; y++)
        {
            var src = pre.Slice(((region.Top + y) * rowBytes) + (region.Left * 4), rw * 4);
            var docY = ((region.Top + y + 0.5) / scale) + origin.Y;
            coarse.Row(docY);
            fine.Row(docY);
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

                var docX = columns[x];
                var c = clouds > 0 ? Math.Clamp((coarse.At(docX) * 0.68f) + (fine.At(docX) * 0.32f), 0f, 1f) : 0.5f;
                Pixel(src.Slice(x * 4, 4), blurred.AsSpan(o, 4), m, cover[i], c, paper is null ? -1f : paper[i], look, dest.Slice(o, 4));
            }
        }

        ArrayPool<double>.Shared.Return(columns);
        if (paper is not null) ArrayPool<float>.Shared.Return(paper);
        ArrayPool<byte>.Shared.Return(blurred);
        ArrayPool<float>.Shared.Return(mask);
        ArrayPool<float>.Shared.Return(cover);
    }

    /// <summary>
    /// The most any one of dabs <paramref name="from"/> to <paramref name="to"/>
    /// puts on each pixel of <paramref name="region"/>, merged into
    /// <paramref name="mask"/> (wetness) and <paramref name="cover"/> (where the
    /// brush was at all) by keeping the larger. True if any dab reached it.
    /// A maximum, so the order the dabs come in, and how many calls they are
    /// split across, does not change a single value.
    /// </summary>
    internal static bool Wetness(
        IReadOnlyList<BrushEngine.Dab> dabs, int from, int to, BrushSettings brush, SKRectI region,
        Span<float> mask, Span<float> cover, double scale = 1.0, SKPointI origin = default)
    {
        var rw = region.Width;
        var flow = Math.Clamp(brush.Flow, 0, 1);
        var touched = false;
        // No dab is wider than the brush's size (a pressure response is at most
        // one), so a dab further than that from the rectangle is passed over
        // before its radius is worked out - which with a curve is not cheap.
        var reach = (Math.Max(1, brush.Size) * scale / 2) + 1;
        for (var di = from; di < to; di++)
        {
            var d = dabs[di];
            double px = (d.Pos.X - origin.X) * scale, py = (d.Pos.Y - origin.Y) * scale;
            if (px + reach < region.Left || px - reach > region.Right || py + reach < region.Top || py - reach > region.Bottom) continue;
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
            // Only the pixels inside the disc, and a square root only in its soft
            // band: the solid middle is the same value everywhere.
            var r2 = radius * radius;
            var solid = hardness * radius;
            var solid2 = solid * solid;
            for (var y = y0; y <= y1; y++)
            {
                var dy = (y + 0.5) - cy;
                var dy2 = dy * dy;
                if (dy2 >= r2) continue;
                var half = Math.Sqrt(r2 - dy2);
                int xa = Math.Max(x0, (int)Math.Floor(cx - half - 0.5)), xb = Math.Min(x1, (int)Math.Ceiling(cx + half - 0.5));
                var row = (y - region.Top) * rw;
                for (var x = xa; x <= xb; x++)
                {
                    var dx = (x + 0.5) - cx;
                    var d2 = (dx * dx) + dy2;
                    if (d2 >= r2) continue;
                    double f;
                    if (d2 <= solid2)
                    {
                        f = 1;
                    }
                    else
                    {
                        // Solid to the hardness, then a smooth fall to the rim.
                        var t = Math.Sqrt(d2) / radius;
                        f = 1 - ((t - hardness) / (1 - hardness));
                        f = f * f * (3 - (2 * f));
                    }
                    var v = (float)f * strength;
                    var i = row + (x - region.Left);
                    if (v > mask[i]) mask[i] = v;
                    if (f > cover[i]) cover[i] = (float)f;
                }
            }
        }

        return touched;
    }

    /// <summary>The settings one pixel of the result is made from, read off the brush once.</summary>
    internal readonly struct Look
    {
        public readonly float Thin;
        public readonly float Clouds;
        public readonly float Gather;
        public readonly float Settle;
        public readonly double Cell;

        public Look(BrushSettings brush)
        {
            var wet = brush.Wet ?? new WetSettings();
            Thin = (float)Math.Clamp(wet.Thin, 0, 1);
            Clouds = (float)Math.Clamp(wet.Cloud, 0, 1);
            Gather = (float)Math.Clamp(wet.Gather, 0, 1);
            Settle = brush.TextureSurface is null ? 0f : (float)Math.Clamp(brush.TextureSettle ?? 0, 0, 1);
            Cell = Math.Max(6.0, brush.Size * 0.45);
        }

        public Octave Coarse => new(Cell, 0x51ED27);

        public Octave Fine => new(Cell * 0.37, 0x2C1B3D);
    }

    /// <summary>
    /// One pixel of the result: the layer as it was (<paramref name="src"/>),
    /// the layer blurred, the wetness and where the brush was, how much water
    /// pooled there, and the paper (negative for none). The only place the
    /// expression is written, so a whole render and a pixel redone on its own
    /// are the same arithmetic.
    /// </summary>
    internal static void Pixel(
        ReadOnlySpan<byte> src, ReadOnlySpan<byte> blurred, float m, float covered, float c, float paper,
        in Look look, Span<byte> dest)
    {
        var clouds = look.Clouds;
        // Where water pooled the pigment is pushed out; beside a pool there is a little more.
        var pool = Math.Clamp((c - 0.45f) * 2.2f, 0f, 1f);
        var bank = Math.Clamp((0.45f - c) * 2.2f, 0f, 1f);
        var carried = (1f - (0.9f * clouds * pool)) * (1f + (0.5f * clouds * bank));
        var mix = m * (1f - clouds + (clouds * Math.Clamp(c * 1.6f, 0f, 1f)));
        // The edge of the water: where the brush's own falloff passes
        // through its middle, as strong as the brush was wet there.
        var edge = 4f * covered * (1f - covered) * Math.Min(1f, m / Math.Max(covered, 0.05f));
        var keep = 1f - (look.Thin * m);
        var line = look.Gather * edge * (0.35f + c);
        var sheet = paper < 0 ? 1f : 1f - (look.Settle * paper * m);

        float a = 0;
        for (var ch = 3; ch >= 0; ch--)
        {
            var p = src[ch];
            var b = blurred[ch] * carried;
            var v = (p + ((b - p) * mix)) * keep;
            v += blurred[ch] * line;
            v *= sheet;
            v = Math.Clamp(v, 0f, 255f);
            // Premultiplied: a colour channel can never exceed the alpha.
            if (ch == 3) a = v;
            else if (v > a) v = a;
            dest[ch] = (byte)(v + 0.5f);
        }
    }

    /// <summary>
    /// How many pixels a cell of the spread's working grid is across. A wide
    /// blur has no detail finer than a fraction of its radius, so it is
    /// computed on a coarser grid and read back smoothly: a sixteenth of the
    /// work at a radius of thirty, and nothing an eye can find.
    /// </summary>
    public static int Cell(int k) => k >= 32 ? 8 : k >= 16 ? 4 : k >= 8 ? 2 : 1;

    /// <summary>
    /// The layer blurred, computed as it is asked for and kept: the layer is
    /// averaged onto a grid of <see cref="Cell"/>-pixel cells fixed to its
    /// origin, blurred there with three box passes, and read back between cell
    /// centres. One of these lasts a stroke, since the layer it reads is the
    /// layer as it was before the stroke and does not change while it is drawn.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pen event rewets a rectangle about the size of the brush, and its
    /// spread reads three radii beyond that: at 200 px that was 375,000 pixels
    /// read per event to render 45,000, and 16 ms. Kept in tiles of cells, each
    /// pixel of the layer is read once in a stroke and each cell blurred once.
    /// </para>
    /// <para>
    /// Integer sums on the grid, and each cell a function of the layer and of
    /// where the cell is - never of which tile or which request it was computed
    /// for. That is what makes a rectangle rendered alone the whole render there,
    /// and a render through a kept one of these the same as a render without
    /// (<c>WaterRewetsTests</c>).
    /// </para>
    /// </remarks>
    public sealed class Spread
    {
        private const int Tile = 64;
        private readonly int _width;
        private readonly int _height;
        private readonly int _k;
        private readonly int _cell;
        private readonly int _ks;
        private readonly Dictionary<(int X, int Y), int[]> _coarse = [];
        private readonly Dictionary<(int X, int Y), int[]> _blurred = [];

        public Spread(int width, int height, int k)
        {
            _width = width;
            _height = height;
            _k = k;
            _cell = WetBleed.Cell(k);
            _ks = Math.Max(1, (int)Math.Round(k / (double)_cell));
        }

        /// <summary>Whether this was made for a layer of this size and this radius.</summary>
        public bool Fits(int width, int height, int k) => width == _width && height == _height && k == _k;

        /// <summary>How many cells have been blurred so far: the work done, for a test to bound.</summary>
        public int CellsBlurred => _blurred.Count * Tile * Tile;

        /// <summary>The blurred layer over <paramref name="region"/>, premultiplied RGBA, tightly packed.</summary>
        public void Read(ReadOnlySpan<byte> pre, int rowBytes, SKRectI region, Span<byte> dest) =>
            Read(pre, rowBytes, region, dest, default, 0, default);

        /// <summary>
        /// The same, for only the pixels <paramref name="wanted"/> marks - a
        /// rectangle of flags <paramref name="wantedStride"/> wide whose corner is
        /// <paramref name="wantedAt"/> - leaving the rest of <paramref name="dest"/>
        /// as it was. Empty flags mean every pixel. Each pixel read is the value a
        /// read of the whole rectangle gives it.
        /// </summary>
        public void Read(
            ReadOnlySpan<byte> pre, int rowBytes, SKRectI region, Span<byte> dest,
            ReadOnlySpan<bool> wanted, int wantedStride, SKPointI wantedAt)
        {
            int rw = region.Width, rh = region.Height;
            var all = wanted.IsEmpty;
            if (_k <= 0)
            {
                for (var y = 0; y < rh; y++)
                {
                    pre.Slice(((region.Top + y) * rowBytes) + (region.Left * 4), rw * 4).CopyTo(dest.Slice(y * rw * 4, rw * 4));
                }
                return;
            }

            // The cells the rectangle reads back from: one beyond it each way.
            var d = _cell;
            int gx = FloorDiv(region.Left, d) - 1, gy = FloorDiv(region.Top, d) - 1;
            int gw = FloorDiv(region.Right - 1, d) + 1 - gx + 1, gh = FloorDiv(region.Bottom - 1, d) + 1 - gy + 1;
            var cells = ArrayPool<int>.Shared.Rent(gw * gh * 4);
            Gather(pre, rowBytes, _blurred, blurredTile: true, gx, gy, gw, gh, cells);

            var colIx = ArrayPool<int>.Shared.Rent(rw);
            var colFx = ArrayPool<float>.Shared.Rent(rw);
            for (var x = 0; x < rw; x++)
            {
                var u = ((region.Left + x + 0.5) / d) - 0.5;
                var ix = (int)Math.Floor(u);
                colIx[x] = ix;
                colFx[x] = (float)(u - ix);
            }
            for (var y = 0; y < rh; y++)
            {
                var v = ((region.Top + y + 0.5) / d) - 0.5;
                var iy = (int)Math.Floor(v);
                var fy = (float)(v - iy);
                var rowA = (iy - gy) * gw;
                var rowB = rowA + gw;
                var flags = all ? 0 : ((region.Top + y - wantedAt.Y) * wantedStride) + (region.Left - wantedAt.X);
                for (var x = 0; x < rw; x++)
                {
                    if (!all && !wanted[flags + x]) continue;
                    var ix = colIx[x];
                    var fx = colFx[x];
                    var a = (rowA + (ix - gx)) * 4;
                    var b = (rowB + (ix - gx)) * 4;
                    var o = ((y * rw) + x) * 4;
                    for (var ch = 0; ch < 4; ch++)
                    {
                        var top = cells[a + ch] + ((cells[a + 4 + ch] - cells[a + ch]) * fx);
                        var bottom = cells[b + ch] + ((cells[b + 4 + ch] - cells[b + ch]) * fx);
                        dest[o + ch] = (byte)Math.Clamp(((top + ((bottom - top) * fy)) / 16f) + 0.5f, 0f, 255f);
                    }
                }
            }
            ArrayPool<int>.Shared.Return(colIx);
            ArrayPool<float>.Shared.Return(colFx);
            ArrayPool<int>.Shared.Return(cells);
        }

        /// <summary>Copy a rectangle of cells out of the tiles that hold them, making the tiles it lacks.</summary>
        private void Gather(
            ReadOnlySpan<byte> pre, int rowBytes, Dictionary<(int X, int Y), int[]> tiles, bool blurredTile,
            int gx, int gy, int gw, int gh, Span<int> into)
        {
            for (var ty = FloorDiv(gy, Tile); ty <= FloorDiv(gy + gh - 1, Tile); ty++)
            {
                for (var tx = FloorDiv(gx, Tile); tx <= FloorDiv(gx + gw - 1, Tile); tx++)
                {
                    if (!tiles.TryGetValue((tx, ty), out var tile))
                    {
                        tile = blurredTile ? BlurTile(pre, rowBytes, tx, ty) : AverageTile(pre, rowBytes, tx, ty);
                        tiles[(tx, ty)] = tile;
                    }
                    int x0 = Math.Max(gx, tx * Tile), x1 = Math.Min(gx + gw, (tx + 1) * Tile);
                    int y0 = Math.Max(gy, ty * Tile), y1 = Math.Min(gy + gh, (ty + 1) * Tile);
                    for (var y = y0; y < y1; y++)
                    {
                        tile.AsSpan((((y - (ty * Tile)) * Tile) + (x0 - (tx * Tile))) * 4, (x1 - x0) * 4)
                            .CopyTo(into.Slice((((y - gy) * gw) + (x0 - gx)) * 4, (x1 - x0) * 4));
                    }
                }
            }
        }

        /// <summary>
        /// A tile of cells, each the mean of the pixels under it - sixteen times
        /// over, so the rounded divisions that follow lose nothing an eye could
        /// find. Where the layer ends there is no paint, and that is read as none.
        /// </summary>
        private int[] AverageTile(ReadOnlySpan<byte> pre, int rowBytes, int tx, int ty)
        {
            var d = _cell;
            var area = d * d;
            var tile = new int[Tile * Tile * 4];
            Span<int> sum = stackalloc int[4];
            for (var cy = 0; cy < Tile; cy++)
            {
                var py0 = ((ty * Tile) + cy) * d;
                if (py0 + d <= 0 || py0 >= _height) continue;
                for (var cx = 0; cx < Tile; cx++)
                {
                    var px0 = ((tx * Tile) + cx) * d;
                    if (px0 + d <= 0 || px0 >= _width) continue;
                    sum.Clear();
                    for (var y = Math.Max(0, py0); y < Math.Min(_height, py0 + d); y++)
                    {
                        var line = pre.Slice(y * rowBytes, _width * 4);
                        for (var x = Math.Max(0, px0); x < Math.Min(_width, px0 + d); x++)
                        {
                            var o = x * 4;
                            sum[0] += line[o]; sum[1] += line[o + 1]; sum[2] += line[o + 2]; sum[3] += line[o + 3];
                        }
                    }
                    var at = ((cy * Tile) + cx) * 4;
                    for (var ch = 0; ch < 4; ch++) tile[at + ch] = ((sum[ch] << 4) + (area >> 1)) / area;
                }
            }
            return tile;
        }

        /// <summary>A tile of blurred cells: the averaged cells under it and three passes of reach around it, blurred, and the middle kept.</summary>
        private int[] BlurTile(ReadOnlySpan<byte> pre, int rowBytes, int tx, int ty)
        {
            var margin = _ks * 3;
            var side = Tile + (margin * 2);
            var gathered = ArrayPool<int>.Shared.Rent(side * side * 4);
            Gather(pre, rowBytes, _coarse, blurredTile: false, (tx * Tile) - margin, (ty * Tile) - margin, side, side, gathered);
            var a = ArrayPool<int>.Shared.Rent(side * side);
            var b = ArrayPool<int>.Shared.Rent(side * side);
            var window = (2 * _ks) + 1;
            var tile = new int[Tile * Tile * 4];
            for (var ch = 0; ch < 4; ch++)
            {
                for (var i = 0; i < side * side; i++) a[i] = gathered[(i * 4) + ch];
                for (var pass = 0; pass < 3; pass++)
                {
                    BoxRows(a, b, side, side, _ks, window);
                    BoxColumns(b, a, side, side, _ks, window);
                }
                for (var y = 0; y < Tile; y++)
                {
                    for (var x = 0; x < Tile; x++) tile[(((y * Tile) + x) * 4) + ch] = a[((y + margin) * side) + x + margin];
                }
            }
            ArrayPool<int>.Shared.Return(gathered);
            ArrayPool<int>.Shared.Return(a);
            ArrayPool<int>.Shared.Return(b);
            return tile;
        }
    }

    private static int FloorDiv(int value, int by) => (int)Math.Floor(value / (double)by);

    private static void BoxRows(int[] from, int[] to, int w, int h, int k, int window)
    {
        for (var y = 0; y < h; y++)
        {
            var row = y * w;
            // An int holds the sum exactly: a cell is at most 4080 and a window a few hundred cells.
            var sum = 0;
            var half = window >> 1;
            for (var x = 0; x <= Math.Min(k, w - 1); x++) sum += from[row + x];
            for (var x = 0; x < w; x++)
            {
                to[row + x] = (sum + half) / window;
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
            var sum = 0;
            var half = window >> 1;
            for (var y = 0; y <= Math.Min(k, h - 1); y++) sum += from[(y * w) + x];
            for (var y = 0; y < h; y++)
            {
                to[(y * w) + x] = (sum + half) / window;
                var leaving = y - k;
                var entering = y + k + 1;
                if (leaving >= 0) sum -= from[(leaving * w) + x];
                if (entering < h) sum += from[(entering * w) + x];
            }
        }
    }

    /// <summary>
    /// One octave of where water pools, 0..1, fixed to the document: value
    /// noise on a lattice of cells hashed from the lattice point. The same
    /// place is the same value in every stroke and on every machine. Walked a
    /// row at a time, so the four corners are hashed when the pixel crosses
    /// into a new cell and not at every pixel.
    /// </summary>
    internal struct Octave(double cell, int salt)
    {
        private int _y0;
        private float _fy;
        private int _x0 = int.MinValue;
        private float _top0, _top1, _bottom0, _bottom1;

        public void Row(double docY)
        {
            var v = docY / cell;
            _y0 = (int)Math.Floor(v);
            var fy = (float)(v - _y0);
            _fy = fy * fy * (3 - (2 * fy));
            _x0 = int.MinValue;
        }

        public float At(double docX)
        {
            var u = docX / cell;
            var x0 = (int)Math.Floor(u);
            if (x0 != _x0)
            {
                _x0 = x0;
                _top0 = Lattice(x0, _y0, salt);
                _top1 = Lattice(x0 + 1, _y0, salt);
                _bottom0 = Lattice(x0, _y0 + 1, salt);
                _bottom1 = Lattice(x0 + 1, _y0 + 1, salt);
            }
            var fx = (float)(u - x0);
            fx = fx * fx * (3 - (2 * fx));
            var top = _top0 + ((_top1 - _top0) * fx);
            var bottom = _bottom0 + ((_bottom1 - _bottom0) * fx);
            return top + ((bottom - top) * _fy);
        }
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
