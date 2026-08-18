using Lightbox.Core.Documents;
using Lightbox.Raster.Media;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

public class MarchingSquaresTests(ITestOutputHelper output)
{
    private static float[] Field(int w, int h, Func<int, int, float> f)
    {
        var v = new float[w * h];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++) v[y * w + x] = f(x, y);
        }
        return v;
    }

    /// <summary>A cone, so the level set is a circle of a known radius.</summary>
    private static float[] Cone(int w, int h, float cx, float cy, float radius) =>
        Field(w, h, (x, y) =>
        {
            var dx = x + 0.5f - cx;
            var dy = y + 0.5f - cy;
            return radius - MathF.Sqrt(dx * dx + dy * dy);
        });

    /// <summary>Twice the enclosed area, signed — positive is one winding, negative the other.</summary>
    private static double SignedArea(IReadOnlyList<StrokePoint> loop)
    {
        double a = 0;
        for (var i = 0; i < loop.Count; i++)
        {
            var p = loop[i];
            var q = loop[(i + 1) % loop.Count];
            a += p.X * q.Y - q.X * p.Y;
        }
        return a / 2;
    }

    [Fact]
    public void A_Disc_Traces_As_One_Closed_Loop()
    {
        var loops = new MarchingSquares().Trace(Cone(48, 48, 24f, 24f, 12f), 48, 48, 0f, outside: -1f);

        Assert.Single(loops);
        var loop = loops[0];
        Assert.True(loop.Count > 20, $"a circle of radius 12 came back as {loop.Count} points");

        // Closed: the walk returns to its start, so the last point joins the first.
        var span = Math.Sqrt(Math.Pow(loop[0].X - loop[^1].X, 2) + Math.Pow(loop[0].Y - loop[^1].Y, 2));
        Assert.True(span < 2, $"the loop does not close: {span:F3} cells between its ends");

        foreach (var p in loop)
        {
            var r = Math.Sqrt(Math.Pow(p.X - 24, 2) + Math.Pow(p.Y - 24, 2));
            Assert.True(Math.Abs(r - 12) < 0.1, $"a point sits at radius {r:F3}, not 12");
        }

        output.WriteLine($"{loop.Count} points, area {Math.Abs(SignedArea(loop)):F1} against πr² = {Math.PI * 144:F1}");
        Assert.True(Math.Abs(Math.Abs(SignedArea(loop)) - Math.PI * 144) < 6, "the enclosed area is wrong");
    }

    /// <summary>
    /// The whole reason this class exists rather than reusing <c>ContourTracer</c>.
    /// A linear ramp puts the level at an exactly known place, and a tracer that
    /// walked sample centres would be out by up to half a cell — which at a
    /// coarse simulation grid is ten document pixels of staircase.
    /// </summary>
    [Fact]
    public void The_Crossing_Lands_Where_The_Field_Actually_Crosses()
    {
        // Rises by 1 per cell in x, so level 10.3 sits at x = 10.3 in field
        // coordinates, which is 10.8 in cell units.
        var field = Field(32, 8, (x, _) => x);
        var loops = new MarchingSquares().Trace(field, 32, 8, 10.3f);

        Assert.NotEmpty(loops);
        var onTheRamp = loops.SelectMany(l => l)
            .Where(p => p.Y > 1 && p.Y < 7 && p.X is > 5 and < 20)   // the ramp crossing, not the field's right edge
            .ToList();

        Assert.True(onTheRamp.Count >= 4, $"only {onTheRamp.Count} points landed on the ramp");
        var worst = onTheRamp.Max(p => Math.Abs(p.X - 10.8));
        output.WriteLine($"{onTheRamp.Count} points on the ramp, worst error {worst:E2} cells");
        Assert.True(worst < 1e-4, $"the crossing is out by {worst:F4} cells");
    }

    [Fact]
    public void An_Annulus_Gives_An_Outer_Loop_And_A_Hole_Wound_The_Other_Way()
    {
        // A ring: high between radius 6 and 14, low inside and out.
        var field = Field(48, 48, (x, y) =>
        {
            var r = MathF.Sqrt(MathF.Pow(x + 0.5f - 24f, 2) + MathF.Pow(y + 0.5f - 24f, 2));
            return r is > 6f and < 14f ? 1f : 0f;
        });

        var loops = new MarchingSquares().Trace(field, 48, 48, 0.5f);

        Assert.Equal(2, loops.Count);
        var areas = loops.Select(SignedArea).OrderBy(a => a).ToList();
        output.WriteLine($"signed areas {areas[0]:F1} and {areas[1]:F1}");

        Assert.True(areas[0] < 0 && areas[1] > 0,
            "the hole must wind opposite to the outer contour, or even-odd filling cannot tell them apart");

        // The convention FieldTracer depends on to tell an outer contour from a
        // hole: walking keeps the inside on the left, so in the document's
        // y-down space an outer loop signs negative and a hole signs positive.
        Assert.True(Math.Abs(areas[0]) > Math.Abs(areas[1]),
            $"the outer contour should be the negative one: {areas[0]:F1} against {areas[1]:F1}");
    }

    [Fact]
    public void Two_Blobs_Trace_As_Two_Loops()
    {
        var field = Field(64, 32, (x, y) =>
        {
            var a = 6f - MathF.Sqrt(MathF.Pow(x + 0.5f - 16f, 2) + MathF.Pow(y + 0.5f - 16f, 2));
            var b = 6f - MathF.Sqrt(MathF.Pow(x + 0.5f - 48f, 2) + MathF.Pow(y + 0.5f - 16f, 2));
            return Math.Max(a, b);
        });

        var loops = new MarchingSquares().Trace(field, 64, 32, 0f, outside: -1f);
        Assert.Equal(2, loops.Count);
    }

    /// <summary>
    /// Samples sit at cell centres, so without padding the marching grid cannot
    /// reach the border and a plume touching the edge of its element would trace
    /// an open curve — which cannot be filled at all.
    /// </summary>
    [Fact]
    public void A_Field_Running_Off_The_Edge_Still_Closes()
    {
        var field = Field(32, 32, (x, y) => y > 20 ? 1f : 0f);   // a slab against the bottom edge

        var loops = new MarchingSquares().Trace(field, 32, 32, 0.5f);

        Assert.Single(loops);
        var loop = loops[0];
        var span = Math.Sqrt(Math.Pow(loop[0].X - loop[^1].X, 2) + Math.Pow(loop[0].Y - loop[^1].Y, 2));
        output.WriteLine($"{loop.Count} points, ends {span:F3} cells apart, area {Math.Abs(SignedArea(loop)):F1}");
        Assert.True(span < 2, $"the loop does not close: {span:F3} cells between its ends");
        Assert.True(Math.Abs(SignedArea(loop)) > 300, "the slab's area came out too small to be the slab");
    }

    /// <summary>
    /// A level at or below <c>outside</c> makes the padded plane itself inside,
    /// so the whole field comes back wrapped in one border loop. That is what it
    /// means rather than a defect — but it caught four fixtures in this file on
    /// the first run, because a signed-distance field traced at zero against a
    /// default <c>outside</c> of zero is the obvious thing to write.
    /// </summary>
    [Fact]
    public void A_Level_At_The_Outside_Value_Encloses_The_Whole_Field()
    {
        var disc = Cone(32, 32, 16f, 16f, 8f);

        var wrapped = new MarchingSquares().Trace(disc, 32, 32, 0f);
        var justTheDisc = new MarchingSquares().Trace(disc, 32, 32, 0f, outside: -1f);

        output.WriteLine($"outside 0: {wrapped.Count} loops; outside -1: {justTheDisc.Count}");
        Assert.Equal(2, wrapped.Count);
        Assert.Single(justTheDisc);
    }

    [Fact]
    public void Nothing_Crossing_Traces_Nothing()
    {
        var flat = Field(24, 24, (_, _) => 0.2f);
        Assert.Empty(new MarchingSquares().Trace(flat, 24, 24, 0.5f));
        Assert.Empty(new MarchingSquares().Trace(flat, 24, 24, 0.05f, outside: 0.2f));
    }

    [Fact]
    public void Two_Traces_Are_Identical_Down_To_The_Point_Order()
    {
        var field = Field(40, 40, (x, y) =>
            MathF.Sin(x * 0.31f) * MathF.Cos(y * 0.27f) + MathF.Sin((x + y) * 0.11f));

        var a = new MarchingSquares().Trace(field, 40, 40, 0.15f);
        var b = new MarchingSquares().Trace(field, 40, 40, 0.15f);

        Assert.Equal(a.Count, b.Count);
        Assert.True(a.Count > 1, $"test would be vacuous: only {a.Count} loop");
        for (var i = 0; i < a.Count; i++) Assert.Equal(a[i], b[i]);
    }

    /// <summary>
    /// A reused instance keeps its buffers, so a second trace has to clear what
    /// the first left behind — otherwise a stale link joins this contour to the
    /// last one, which looks like a wild stray line rather than like a bug.
    /// </summary>
    [Fact]
    public void A_Reused_Instance_Traces_What_A_Fresh_One_Would()
    {
        var first = Field(40, 40, (x, y) => 10f - Math.Abs(x - 20) - Math.Abs(y - 20));
        var second = Field(24, 32, (x, y) => 6f - Math.Abs(x - 12) - Math.Abs(y - 16));

        var reused = new MarchingSquares();
        reused.Trace(first, 40, 40, 3f);
        var afterUse = reused.Trace(second, 24, 32, 2f);

        var fresh = new MarchingSquares().Trace(second, 24, 32, 2f);

        Assert.Equal(fresh.Count, afterUse.Count);
        for (var i = 0; i < fresh.Count; i++) Assert.Equal(fresh[i], afterUse[i]);
    }

    /// <summary>
    /// A pinch — two blobs a hair apart — is the saddle case, and the cell's own
    /// average is what decides whether they read as joined. Fixing it by a
    /// constant rule instead would fuse them a frame early or late, which at 12
    /// fps is a visible pop.
    /// </summary>
    [Fact]
    public void A_Pinch_Joins_Or_Separates_By_The_Cells_Own_Average()
    {
        static int LoopsAtGap(float gap)
        {
            var field = Field(41, 21, (x, y) =>
            {
                var a = 8f - MathF.Sqrt(MathF.Pow(x + 0.5f - (20.5f - gap), 2) + MathF.Pow(y + 0.5f - 10.5f, 2));
                var b = 8f - MathF.Sqrt(MathF.Pow(x + 0.5f - (20.5f + gap), 2) + MathF.Pow(y + 0.5f - 10.5f, 2));
                return Math.Max(a, b);
            });
            return new MarchingSquares().Trace(field, 41, 21, 0f, outside: -1f).Count;
        }

        var joined = LoopsAtGap(6f);
        var apart = LoopsAtGap(11f);
        output.WriteLine($"overlapping discs: {joined} loop(s); separated discs: {apart} loop(s)");
        Assert.Equal(1, joined);
        Assert.Equal(2, apart);
    }
}
