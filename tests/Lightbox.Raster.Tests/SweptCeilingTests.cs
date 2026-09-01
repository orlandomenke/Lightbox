using System.Diagnostics;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// B349. A soft brush swept back and forth shows ridges along the sweep and a
/// hard rim, because the footprint ceiling is a running <em>maximum</em> of dab
/// shapes and a maximum of overlapping bumps is bumpy. This measures a
/// different <em>definition</em> of the ceiling against today's, on the reported
/// gesture and on Q157's own constraints, before a line of it goes near the
/// engine.
/// </summary>
/// <remarks>
/// <para>
/// <b>The candidate: the dab's own shape, applied to the distance from the edge
/// of everything the stroke reached.</b> Today's ceiling at a pixel is the best
/// any single dab does there; the candidate is the dab's radial shape evaluated
/// at how far inside the <em>union</em> of dab supports the pixel sits. For a
/// lone dab the two are the same function. Across a straight stroke they are
/// the same function. Between two passes of a sweep they differ: the maximum
/// dips where no dab centre is, the distance to the outer edge does not.
/// </para>
/// <para>
/// <b>Provably never lower than today's</b>, which is why B349's impossibility
/// argument does not bite: that argument is about operators on the max buffer,
/// and lowering it clips a lone dab. This never lowers it — the disc around the
/// nearest dab centre lies inside the union, so the distance to the union's
/// edge is at least the distance to that disc's edge, and a falling shape of a
/// larger argument is a larger value. The monotone test below holds that pixel
/// by pixel, because a proof is not a measurement.
/// </para>
/// <para>
/// <b>The shape is read off the engine's own footprint of one dab</b>, so the
/// candidate uses exactly the curve today's ceiling uses and the only thing
/// under test is the geometry the curve is applied to. The one free parameter
/// is where the rasterised union's edge sits relative to the pixel grid, and
/// the lone-dab test measures it rather than assumes it.
/// </para>
/// </remarks>
public class SweptCeilingTests(ITestOutputHelper output)
{
    private const int W = 900, H = 460;

    private static BrushSettings SoftRound() => new()
    {
        Size = 70, Hardness = 0.35, Opacity = 1, Flow = 1, Spacing = 0.15, AntiAlias = true,
    };

    private static BrushSettings Airbrush() => new()
    {
        Size = 70, Hardness = 0.05, Opacity = 1, Flow = 1, Spacing = 0.08, AntiAlias = true,
    };

    private static Stroke Sweep(BrushSettings brush, double pitch, int passes = 8)
    {
        var pts = new List<StrokePoint>();
        for (var k = 0; k < passes; k++)
        {
            var y = 150 + k * pitch;
            var forward = k % 2 == 0;
            for (double t = 0; t <= 600; t += 4.2)
            {
                var x = forward ? 150 + t : 750 - t;
                pts.Add(new StrokePoint(x, y, 1));
            }
        }
        return new Stroke { Tool = ToolKind.Brush, Color = "#000000", Brush = brush, Points = pts };
    }

    private static Stroke Straight(BrushSettings brush)
    {
        var pts = new List<StrokePoint>();
        for (double x = 150; x <= 750; x += 4.2) pts.Add(new StrokePoint(x, H / 2.0, 1));
        return new Stroke { Tool = ToolKind.Brush, Color = "#000000", Brush = brush, Points = pts };
    }

    private static Stroke Lone(BrushSettings brush) => new()
    {
        Tool = ToolKind.Brush, Color = "#000000", Brush = brush,
        Points = [new StrokePoint(W / 2.0, H / 2.0, 1)],
    };

    private static SKBitmap Mark(Stroke stroke, IReadOnlyList<BrushEngine.Dab> dabs)
    {
        var bmp = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        BrushEngine.StampDabRange(canvas, stroke, dabs, 0, dabs.Count);
        canvas.Flush();
        return bmp;
    }

    /// <summary>Today's ceiling: the running maximum the engine accumulates.</summary>
    private static SKBitmap TodaysCeiling(Stroke stroke, IReadOnlyList<BrushEngine.Dab> dabs)
    {
        var bmp = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        BrushEngine.AccumulateFootprint(canvas, stroke, dabs, 0, dabs.Count);
        canvas.Flush();
        return bmp;
    }

    /// <summary>
    /// The dab's radial shape as today's ceiling records it, read off the
    /// footprint of one dab: <c>table[r]</c> is the ceiling <c>r</c> pixels
    /// from the centre, in 0..1. <c>outer</c> is the engine's own dab radius.
    /// </summary>
    private static (float[] Table, float Outer) Profile(BrushSettings brush)
    {
        var lone = Lone(brush);
        var dabs = BrushEngine.WalkDabs(lone);
        using var footprint = TodaysCeiling(lone, dabs);
        var cx = (int)Math.Round(dabs[0].Pos.X);
        var cy = (int)Math.Round(dabs[0].Pos.Y);
        var outer = (float)BrushEngine.RadiusAt(brush, dabs[0].Pressure);
        var n = (int)Math.Ceiling(outer) + 3;
        // Skia evaluates the dab at pixel CENTRES, so the pixel r columns right
        // of the centre samples the radial shape at sqrt((r+0.5)^2 + 0.5^2), not
        // at r. The table is stored against that true radius and read back by
        // interpolating on it; reading it as if it were r was a half-pixel
        // shift, which in a soft falloff is several levels.
        var table = new float[n];
        for (var r = 0; r < n; r++) table[r] = footprint.GetPixel(cx + r, cy).Red / 255f;
        return (table, outer);
    }

    private static float RadiusOfSample(int r) => MathF.Sqrt((r + 0.5f) * (r + 0.5f) + 0.25f);

    private static float Sample(float[] table, float rho)
    {
        if (rho <= RadiusOfSample(0)) return table[0];
        for (var i = 0; i + 1 < table.Length; i++)
        {
            float a = RadiusOfSample(i), b = RadiusOfSample(i + 1);
            if (rho <= b)
            {
                var f = (rho - a) / (b - a);
                return table[i] * (1 - f) + table[i + 1] * f;
            }
        }
        return 0;
    }

    /// <summary>
    /// The candidate ceiling: the union of every dab's support, and the shape
    /// evaluated at each pixel's distance inside that union's edge.
    /// </summary>
    /// <param name="edgeOffset">
    /// Where the rasterised union's edge sits relative to the nearest outside
    /// sample, in supersampled pixels — the one calibration the lone-dab test
    /// measures.
    /// </param>
    /// <remarks>
    /// The union is taken at double resolution so its edge lands within a
    /// quarter pixel of the geometric one; the distance transform is exact
    /// Euclidean (Felzenszwalb and Huttenlocher's two-pass form), which is what
    /// makes the result deterministic — no random access, no clock, the same
    /// pixels for the same dabs every time (invariant 2).
    /// </remarks>
    private static (SKBitmap Ceiling, double Ms) CandidateCeiling(
        IReadOnlyList<BrushEngine.Dab> dabs, float[] table, float outer, float edgeOffset)
    {
        var sw = Stopwatch.StartNew();
        const int S = 5;
        int w = W * S, h = H * S;
        var inside = new bool[w * h];
        foreach (var d in dabs)
        {
            var cx = d.Pos.X * S;
            var cy = d.Pos.Y * S;
            var r = outer * S;
            int x0 = Math.Max(0, (int)Math.Floor(cx - r)), x1 = Math.Min(w - 1, (int)Math.Ceiling(cx + r));
            int y0 = Math.Max(0, (int)Math.Floor(cy - r)), y1 = Math.Min(h - 1, (int)Math.Ceiling(cy + r));
            var rr = r * r;
            for (var y = y0; y <= y1; y++)
            {
                var dy = y + 0.5f - cy;
                for (var x = x0; x <= x1; x++)
                {
                    var dx = x + 0.5f - cx;
                    if (dx * dx + dy * dy <= rr) inside[y * w + x] = true;
                }
            }
        }

        var dist = ExactDistanceTransform(inside, w, h);

        var bmp = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Opaque));
        var bytes = new byte[W * H * 4];
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                // The doc pixel's centre is exactly the centre supersample when
                // S is odd — no quarter-pixel bias in where the shape is read.
                var sx = x * S + S / 2;
                var sy = y * S + S / 2;
                var i = sy * w + sx;
                float c = 0;
                if (inside[i])
                {
                    var dEdge = (MathF.Sqrt(dist[i]) - edgeOffset) / S;
                    if (dEdge < 0) dEdge = 0;
                    c = Sample(table, outer - dEdge);
                }
                var v = (byte)Math.Clamp((int)MathF.Round(c * 255), 0, 255);
                var o = (y * W + x) * 4;
                bytes[o] = v; bytes[o + 1] = v; bytes[o + 2] = v; bytes[o + 3] = 255;
            }
        }
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            using var src = new SKBitmap();
            src.InstallPixels(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Opaque), handle.AddrOfPinnedObject(), W * 4);
            src.CopyTo(bmp);
        }
        finally
        {
            handle.Free();
        }
        sw.Stop();
        return (bmp, sw.Elapsed.TotalMilliseconds);
    }

    /// <summary>Squared Euclidean distance to the nearest outside cell, for every cell.</summary>
    private static float[] ExactDistanceTransform(bool[] inside, int w, int h)
    {
        const float Inf = 1e12f;
        var f = new float[w * h];
        for (var i = 0; i < f.Length; i++) f[i] = inside[i] ? Inf : 0;

        var buf = new float[Math.Max(w, h)];
        var outp = new float[Math.Max(w, h)];
        for (var x = 0; x < w; x++)
        {
            for (var y = 0; y < h; y++) buf[y] = f[y * w + x];
            OneDimensional(buf, h, outp);
            for (var y = 0; y < h; y++) f[y * w + x] = outp[y];
        }
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++) buf[x] = f[y * w + x];
            OneDimensional(buf, w, outp);
            for (var x = 0; x < w; x++) f[y * w + x] = outp[x];
        }
        return f;
    }

    /// <summary>Felzenszwalb and Huttenlocher's lower-envelope pass over one line.</summary>
    private static void OneDimensional(float[] f, int n, float[] d)
    {
        var v = new int[n];
        var z = new float[n + 1];
        var k = 0;
        v[0] = 0;
        z[0] = float.NegativeInfinity;
        z[1] = float.PositiveInfinity;
        for (var q = 1; q < n; q++)
        {
            float s;
            while (true)
            {
                var p = v[k];
                s = ((f[q] + q * (float)q) - (f[p] + p * (float)p)) / (2f * q - 2f * p);
                if (s <= z[k]) { k--; continue; }
                break;
            }
            k++;
            v[k] = q;
            z[k] = s;
            z[k + 1] = float.PositiveInfinity;
        }
        k = 0;
        for (var q = 0; q < n; q++)
        {
            while (z[k + 1] < q) k++;
            var p = v[k];
            d[q] = (q - p) * (float)(q - p) + f[p];
        }
    }

    private static SKBitmap Capped(Stroke stroke, IReadOnlyList<BrushEngine.Dab> dabs, SKBitmap ceiling)
    {
        var mark = Mark(stroke, dabs);
        BrushEngine.CapToFootprintBand(mark, ceiling, new SKRectI(0, 0, W, H));
        return mark;
    }

    /// <summary>B349's metric: the detrended peak-to-trough of a cut through the interior, out of 255.</summary>
    private static double Ripple(SKBitmap mark, int x, int y0, int y1)
    {
        var n = y1 - y0 + 1;
        var ys = new double[n];
        for (var i = 0; i < n; i++) ys[i] = mark.GetPixel(x, y0 + i).Alpha;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (var i = 0; i < n; i++) { sx += i; sy += ys[i]; sxx += i * (double)i; sxy += i * ys[i]; }
        var slope = (n * sxy - sx * sy) / (n * sxx - sx * sx);
        var intercept = (sy - slope * sx) / n;
        double lo = double.MaxValue, hi = double.MinValue;
        for (var i = 0; i < n; i++)
        {
            var r = ys[i] - (intercept + slope * i);
            lo = Math.Min(lo, r); hi = Math.Max(hi, r);
        }
        return hi - lo;
    }

    private static (int Worst, (int X, int Y) At) MaxAbsDiff(SKBitmap a, SKBitmap b, Func<SKColor, int> read)
    {
        var worst = 0; var at = (0, 0);
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                var d = Math.Abs(read(a.GetPixel(x, y)) - read(b.GetPixel(x, y)));
                if (d > worst) { worst = d; at = (x, y); }
            }
        }
        return (worst, at);
    }

    public static TheoryData<string> Brushes() => new() { "Soft round", "Airbrush" };

    private static BrushSettings Named(string name) => name == "Airbrush" ? Airbrush() : SoftRound();

    private static readonly float[] Offsets = [0f, 0.25f, 0.5f, 0.75f, 1.0f];

    /// <summary>
    /// The calibration, measured: for a lone dab the candidate and today's
    /// ceiling are the same function, so the edge offset that makes them agree
    /// is the one the rasterised union actually has.
    /// </summary>
    private float Calibrate(BrushSettings brush, string name, float[] table, float outer)
    {
        var lone = Lone(brush);
        var dabs = BrushEngine.WalkDabs(lone);
        using var todays = TodaysCeiling(lone, dabs);
        var best = 0f; var bestWorst = int.MaxValue;
        foreach (var off in Offsets)
        {
            var (candidate, _) = CandidateCeiling(dabs, table, outer, off);
            using (candidate)
            {
                var (worst, at) = MaxAbsDiff(todays, candidate, c => c.Red);
                output.WriteLine($"  {name}, lone dab, edge offset {off:0.00}: ceilings differ by at most {worst}/255 at {at}");
                if (worst < bestWorst) { bestWorst = worst; best = off; }
            }
        }
        output.WriteLine($"  {name}: calibrated edge offset {best:0.00} (worst {bestWorst}/255)");
        return best;
    }

    [Theory]
    [MemberData(nameof(Brushes))]
    public void TheSweptInteriorIsFlatUnderTheCandidateAndRidgedUnderTodays(string name)
    {
        var brush = Named(name);
        Assert.True(BrushEngine.NeedsFootprintCap(brush), "this brush is not capped, so the test measures nothing");
        var (table, outer) = Profile(brush);
        var offset = Calibrate(brush, name, table, outer);

        // The pitch that ridges worst under today's ceiling, found rather than
        // assumed: a soft dab's flat core covers a tight pitch, so the ridge only
        // appears once the passes are far enough apart for the maximum to dip.
        double worstPitch = 0, worstRipple = -1;
        foreach (var pitch in new[] { 18.4, 24, 28, 32, 36, 40 })
        {
            var s = Sweep(brush, pitch);
            var d = BrushEngine.WalkDabs(s);
            using var t = TodaysCeiling(s, d);
            using var m = Capped(s, d, t);
            int y0 = (int)(150 + outer) + 2, y1 = (int)(150 + 7 * pitch - outer) - 2;
            var r = y1 - y0 > 8 ? Ripple(m, 450, y0, y1) : -1;
            output.WriteLine($"  {name}, pitch {pitch:0.0}: today's ripple {r:0.0}/255");
            if (r > worstRipple) { worstRipple = r; worstPitch = pitch; }
        }

        var sweep = Sweep(brush, worstPitch);
        var dabs = BrushEngine.WalkDabs(sweep);
        using var todays = TodaysCeiling(sweep, dabs);
        var (candidate, ms) = CandidateCeiling(dabs, table, outer, offset);
        using (candidate)
        {
            using var today = Capped(sweep, dabs, todays);
            using var ours = Capped(sweep, dabs, candidate);

            // Interior rows: clear of the outermost passes' own falloff, so the
            // cut measures the ceiling's ripple and not the mark's edge.
            int y0 = (int)(150 + outer) + 2, y1 = (int)(150 + 7 * worstPitch - outer) - 2;
            using var uncapped = Mark(sweep, dabs);
            var rToday = Ripple(today, 450, y0, y1);
            var rOurs = Ripple(ours, 450, y0, y1);
            var rNone = Ripple(uncapped, 450, y0, y1);
            output.WriteLine($"{name} size 70, pitch {worstPitch:0.0}: ripple today {rToday:0.0}/255, candidate {rOurs:0.0}/255, uncapped {rNone:0.0}/255 — ceiling built in {ms:0.0} ms");

            var below = 0; var worstBelow = 0;
            for (var y = 0; y < H; y++)
            {
                for (var x = 0; x < W; x++)
                {
                    var t = todays.GetPixel(x, y).Red;
                    var c = candidate.GetPixel(x, y).Red;
                    // Three levels: the union is rasterised at a fifth of a pixel,
                    // which in the softest falloff is about that much.
                    if (c + 3 < t) { below++; worstBelow = Math.Max(worstBelow, t - c); }
                }
            }
            output.WriteLine($"  pixels where the candidate is below today's by more than 3: {below} (worst by {worstBelow})");
            Assert.True(below == 0, $"the candidate lowered the ceiling at {below} pixels, worst by {worstBelow} — that is the clipping B349 forbids");

            Assert.True(rToday >= 5, $"today's ceiling did not reproduce the ridge (ripple {rToday:0.0}) — the probe is not looking at the defect");
            // The floor is the mark itself: where the ceiling does not bind, the
            // capped mark is the uncapped one, ripple included. The candidate
            // may not ADD to that; the ridge today adds tens of levels.
            Assert.True(rOurs <= rNone + 0.5, $"the candidate ripples at {rOurs:0.0}/255 over an uncapped {rNone:0.0}/255");
        }
    }

    [Theory]
    [MemberData(nameof(Brushes))]
    public void ALoneDabAndAStraightStrokeAreUnchangedByTheCandidate(string name)
    {
        var brush = Named(name);
        var (table, outer) = Profile(brush);
        var offset = Calibrate(brush, name, table, outer);

        {
            var lone = Lone(brush);
            var dabs = BrushEngine.WalkDabs(lone);
            using var todays = TodaysCeiling(lone, dabs);
            var (candidate, _) = CandidateCeiling(dabs, table, outer, offset);
            using (candidate)
            {
                using var today = Capped(lone, dabs, todays);
                using var ours = Capped(lone, dabs, candidate);
                var (worst, at) = MaxAbsDiff(today, ours, c => c.Alpha);
                output.WriteLine($"{name}, lone dab: worst capped-pixel difference {worst}/255 at {at}");
                Assert.True(worst <= 2, $"lone dab: the candidate moved a pixel by {worst} at {at}");
            }
        }

        {
            // Q157 measures the cross-profile through a dab's centre, and that
            // is where the two definitions coincide across a straight stroke.
            // ALONG the stroke they do not: today's maximum reads F(0) on a dab
            // centre and F(half a pitch) between two, which is the dab-pitch
            // ripple B349 found finer sampling could not remove; the union's
            // edge is the same distance away all along, so the candidate is
            // flat there. That difference is the fix, so it is asserted as an
            // improvement rather than as identity.
            var straight = Straight(brush);
            var dabs = BrushEngine.WalkDabs(straight);
            using var todays = TodaysCeiling(straight, dabs);
            var (candidate, _) = CandidateCeiling(dabs, table, outer, offset);
            using (candidate)
            {
                using var today = Capped(straight, dabs, todays);
                using var ours = Capped(straight, dabs, candidate);

                var mid = dabs[dabs.Count / 2];
                var column = (int)Math.Round(mid.Pos.X);
                var worst = 0; var at = 0;
                for (var y = 0; y < H; y++)
                {
                    var d = Math.Abs(today.GetPixel(column, y).Alpha - ours.GetPixel(column, y).Alpha);
                    if (d > worst) { worst = d; at = y; }
                }
                output.WriteLine($"{name}, straight stroke, cross-profile through a dab centre: worst difference {worst}/255 at y={at}");
                Assert.True(worst <= 2, $"straight stroke: the cross-profile moved by {worst} at y={at}");

                // Along the centreline, between the first and last dab.
                var cy = (int)Math.Round(mid.Pos.Y);
                int x0 = (int)Math.Ceiling(dabs[0].Pos.X) + 1, x1 = (int)Math.Floor(dabs[^1].Pos.X) - 1;
                double AlongRipple(SKBitmap m)
                {
                    int lo = 255, hi = 0;
                    for (var x = x0; x <= x1; x++) { var a = m.GetPixel(x, cy).Alpha; lo = Math.Min(lo, a); hi = Math.Max(hi, a); }
                    return hi - lo;
                }
                double rToday = AlongRipple(today), rOurs = AlongRipple(ours);
                output.WriteLine($"{name}, straight stroke, along the centreline: ripple today {rToday:0}/255, candidate {rOurs:0}/255");
                Assert.True(rOurs <= rToday, "the candidate ripples more along the stroke than today's ceiling");
            }
        }
    }
}
