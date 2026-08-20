using Lightbox.Core.Documents;
using Lightbox.Raster.Media;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Field to strokes, judged on a <em>static</em> field so the tracer is measured
/// without the solver in the reading.
/// </summary>
public class FieldTracerTests(ITestOutputHelper output)
{
    /// <summary>A cone falling from 1 at the centre to 0 at <paramref name="radius"/>.</summary>
    private static float[] Blob(int w, int h, float cx, float cy, float radius)
    {
        var f = new float[w * h];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var d = MathF.Sqrt(MathF.Pow(x + 0.5f - cx, 2) + MathF.Pow(y + 0.5f - cy, 2));
                f[y * w + x] = MathF.Max(0f, 1f - d / radius);
            }
        }
        return f;
    }

    private static FieldTraceRequest Request(
        float[] field, int w, int h, LineTreatment? treatment = null,
        double scale = 1, double originX = 0, double originY = 0, double brushSize = 1)
        => new()
        {
            Field = field,
            Width = w,
            Height = h,
            Scale = scale,
            OriginX = originX,
            OriginY = originY,
            Treatment = LineTreatment.Resolve(treatment),
            Colors = ["#331100", "#cc5500", "#ffdd66"],
            Low = 0f,
            High = 1f,
            OutlineBrush = new BrushSettings { Size = brushSize },
            OutlineColor = "#221100",
        };

    private static List<Stroke> Fills(IEnumerable<Stroke> s) => s.Where(x => x.Tool == ToolKind.Fill).ToList();

    private static List<Stroke> Outlines(IEnumerable<Stroke> s) => s.Where(x => x.Tool == ToolKind.Brush).ToList();

    private static double Extent(Stroke s)
    {
        var cx = s.Points.Average(p => p.X);
        var cy = s.Points.Average(p => p.Y);
        return s.Points.Max(p => Math.Sqrt(Math.Pow(p.X - cx, 2) + Math.Pow(p.Y - cy, 2)));
    }

    // ---- bands ---------------------------------------------------------------

    /// <summary>Where a fill's points sit, on average.</summary>
    private static (double X, double Y) Centre(Stroke s) =>
        (s.Points.Average(p => p.X), s.Points.Average(p => p.Y));

    /// <summary>
    /// Unshaded, the bands are concentric — which is the problem, not a feature.
    /// A cross-section of a volume rather than a lit one.
    /// </summary>
    [Fact]
    public void Without_Shading_Every_Band_Shares_One_Centre()
    {
        var strokes = new FieldTracer().Trace(Request(Blob(64, 64, 32f, 32f, 24f), 64, 64));

        var centres = Fills(strokes).Select(Centre).ToList();
        output.WriteLine(string.Join("  ", centres.Select(c => $"({c.X:F2},{c.Y:F2})")));

        // Half a cell, not exact: a simplified contour's mean point is not its
        // geometric centroid, and the claim is "these share a centre" against a
        // shift measured in whole cells rather than "these are at 32.00".
        Assert.All(centres, c =>
        {
            Assert.InRange(c.X, 31.5, 32.5);
            Assert.InRange(c.Y, 31.5, 32.5);
        });
    }

    /// <summary>
    /// Shaded, the inner bands move toward the light and the outer one does not.
    /// </summary>
    /// <remarks>
    /// Band 0 staying put is half the claim and the more important half: it is
    /// the silhouette, and a silhouette that slid off its own volume would not
    /// be one. Light at 0° is straight up, so "toward the light" is a smaller Y.
    /// </remarks>
    [Fact]
    public void Shading_Slides_The_Inner_Bands_Toward_The_Light_And_Leaves_The_Silhouette()
    {
        var lit = new LineTreatment { ShadeOffset = 2, LightAngleDeg = 0 };
        var plain = new FieldTracer().Trace(Request(Blob(64, 64, 32f, 32f, 24f), 64, 64));
        var shaded = new FieldTracer().Trace(Request(Blob(64, 64, 32f, 32f, 24f), 64, 64, lit));

        var before = Fills(plain).Select(Centre).ToList();
        var after = Fills(shaded).Select(Centre).ToList();
        Assert.Equal(3, after.Count);

        output.WriteLine($"silhouette {before[0].Y:F2} → {after[0].Y:F2}, " +
                         $"mid {before[1].Y:F2} → {after[1].Y:F2}, core {before[2].Y:F2} → {after[2].Y:F2}");

        Assert.Equal(before[0].Y, after[0].Y, 2);
        Assert.True(after[1].Y < before[1].Y - 0.2, "the middle band did not move toward the light");
        Assert.True(after[2].Y < after[1].Y, "the innermost band must move furthest");
        // Sideways is the light's other axis and it is zero here, so nothing
        // should have drifted in X — which is what says the direction is read
        // rather than a constant offset being applied.
        for (var i = 0; i < after.Count; i++) Assert.Equal(before[i].X, after[i].X, 6);
    }

    /// <summary>
    /// The light's angle is obeyed, not just its presence.
    /// </summary>
    [Fact]
    public void Shading_Follows_The_Light_Angle()
    {
        (double X, double Y) Core(double angle)
        {
            var t = new LineTreatment { ShadeOffset = 2, LightAngleDeg = angle };
            return Centre(Fills(new FieldTracer().Trace(Request(Blob(64, 64, 32f, 32f, 24f), 64, 64, t)))[^1]);
        }

        var up = Core(0);
        var right = Core(90);
        var down = Core(180);

        output.WriteLine($"up {up}, right {right}, down {down}");
        Assert.True(up.Y < 32, "light from above must lift the core");
        Assert.True(right.X > 32, "light from the right must push the core right");
        Assert.True(down.Y > 32, "light from below must drop the core");
    }

    /// <summary>
    /// A highlight can never leave the silhouette, however far the slider goes.
    /// </summary>
    /// <remarks>
    /// <b>The failure this prevents is visible and was seen before it was
    /// fixed</b>: past a certain offset the pale band pokes out beyond the dark
    /// outline and stops reading as a highlight, becoming a second paler shape
    /// sitting on top. An artist takes a slider to its end to find out what it
    /// does, so the end has to be somewhere sensible.
    /// </remarks>
    [Fact]
    public void A_Highlight_Cannot_Be_Lit_Out_Of_Its_Own_Volume()
    {
        var silhouette = Fills(new FieldTracer().Trace(Request(Blob(64, 64, 32f, 32f, 24f), 64, 64)))[0];
        var top = silhouette.Points.Min(p => p.Y);
        var bottom = silhouette.Points.Max(p => p.Y);

        foreach (var offset in new double[] { 4, 20, 200 })
        {
            var t = new LineTreatment { ShadeOffset = offset, LightAngleDeg = 0 };
            var fills = Fills(new FieldTracer().Trace(Request(Blob(64, 64, 32f, 32f, 24f), 64, 64, t)));

            var highest = fills.Skip(1).Min(f => f.Points.Min(p => p.Y));
            output.WriteLine($"offset {offset}: inner bands reach {highest:F2}, silhouette spans {top:F2}..{bottom:F2}");
            Assert.True(highest >= top - 1e-6, $"offset {offset} lit a band out of the silhouette");
        }
    }

    /// <summary>Nothing moves when nobody asked for it.</summary>
    [Fact]
    public void Shading_Is_Off_Unless_Asked_For()
    {
        Assert.Equal(0, LineTreatment.Defaults.ShadeOffset);

        var plain = Fills(new FieldTracer().Trace(Request(Blob(64, 64, 32f, 32f, 24f), 64, 64)));
        var zero = Fills(new FieldTracer().Trace(
            Request(Blob(64, 64, 32f, 32f, 24f), 64, 64, new LineTreatment { ShadeOffset = 0 })));

        Assert.Equal(plain.Select(Centre), zero.Select(Centre));
    }

    [Fact]
    public void Bands_Nest_And_Are_Painted_Back_To_Front()
    {
        var strokes = new FieldTracer().Trace(Request(Blob(64, 64, 32f, 32f, 24f), 64, 64));

        var fills = Fills(strokes);
        Assert.Equal(3, fills.Count);

        var extents = fills.Select(Extent).ToList();
        output.WriteLine($"band extents {string.Join(", ", extents.Select(e => e.ToString("F1")))}");
        Assert.True(extents[0] > extents[1] && extents[1] > extents[2],
            "each band must sit inside the one painted before it");

        Assert.Equal("#331100", fills[0].Color);
        Assert.Equal("#ffdd66", fills[2].Color);
    }

    /// <summary>
    /// Cel order: paint, then ink over it. Emitting an outline before the band
    /// inside it would bury the silhouette line.
    /// </summary>
    [Fact]
    public void Every_Fill_Comes_Before_Every_Outline()
    {
        var strokes = new FieldTracer().Trace(
            Request(Blob(64, 64, 32f, 32f, 24f), 64, 64, new LineTreatment { Outlined = OutlinedBands.Every }));

        var lastFill = strokes.FindLastIndex(s => s.Tool == ToolKind.Fill);
        var firstOutline = strokes.FindIndex(s => s.Tool == ToolKind.Brush);
        Assert.True(firstOutline > lastFill, $"an outline at {firstOutline} precedes a fill at {lastFill}");
    }

    [Theory]
    [InlineData(OutlinedBands.None, 0)]
    [InlineData(OutlinedBands.Silhouette, 1)]
    [InlineData(OutlinedBands.Every, 3)]
    public void Which_Bands_Are_Outlined_Is_The_Treatments_Call(OutlinedBands which, int expected)
    {
        var strokes = new FieldTracer().Trace(
            Request(Blob(64, 64, 32f, 32f, 24f), 64, 64, new LineTreatment { Outlined = which }));

        Assert.Equal(expected, Outlines(strokes).Count);
    }

    [Fact]
    public void Core_Biased_Spacing_Puts_The_Bands_Nearer_The_Hot_Middle()
    {
        var even = Fills(new FieldTracer().Trace(
            Request(Blob(64, 64, 32f, 32f, 24f), 64, 64, new LineTreatment { Spacing = BandSpacing.Even })));
        var biased = Fills(new FieldTracer().Trace(
            Request(Blob(64, 64, 32f, 32f, 24f), 64, 64, new LineTreatment { Spacing = BandSpacing.CoreBiased })));

        output.WriteLine($"outermost band: even {Extent(even[0]):F1}, core-biased {Extent(biased[0]):F1}");
        Assert.True(Extent(biased[0]) < Extent(even[0]),
            "core-biased levels sit higher in the range, so every band is smaller");
    }

    // ---- holes ---------------------------------------------------------------

    [Fact]
    public void A_Ring_Fills_As_One_Region_With_A_Hole_In_It()
    {
        var w = 64;
        var field = new float[w * w];
        for (var y = 0; y < w; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var d = MathF.Sqrt(MathF.Pow(x + 0.5f - 32f, 2) + MathF.Pow(y + 0.5f - 32f, 2));
                field[y * w + x] = d is > 8f and < 24f ? 1f : 0f;
            }
        }

        var strokes = new FieldTracer().Trace(
            Request(field, w, w, new LineTreatment { Bands = 1, Outlined = OutlinedBands.None }));

        var fills = Fills(strokes);
        Assert.Single(fills);
        Assert.NotNull(fills[0].Holes);
        Assert.Single(fills[0].Holes!);
        output.WriteLine($"outer {fills[0].Points.Count} points, hole {fills[0].Holes![0].Count} points");
    }

    // ---- placement -----------------------------------------------------------

    [Fact]
    public void Points_Land_In_Document_Coordinates()
    {
        // Simplification is measured in stroke-widths, so it is scale-dependent
        // by design; switch it off to compare placement alone.
        var raw = new LineTreatment { Simplify = 0 };
        var plain = Fills(new FieldTracer().Trace(Request(Blob(48, 48, 24f, 24f, 18f), 48, 48, raw)))[0];
        var placed = Fills(new FieldTracer().Trace(
            Request(Blob(48, 48, 24f, 24f, 18f), 48, 48, raw, scale: 10, originX: 100, originY: 40)))[0];

        Assert.Equal(plain.Points.Count, placed.Points.Count);
        for (var i = 0; i < plain.Points.Count; i++)
        {
            Assert.Equal(100 + plain.Points[i].X * 10, placed.Points[i].X, 6);
            Assert.Equal(40 + plain.Points[i].Y * 10, placed.Points[i].Y, 6);
        }
    }

    /// <summary>
    /// Treatment distances are in stroke-widths, so an element drawn twice the
    /// size with a brush twice as wide is the same drawing scaled — which is
    /// invariant 7 restated for style, and the reason the units are not pixels.
    /// </summary>
    [Fact]
    public void A_Style_Reads_The_Same_At_Twice_The_Size()
    {
        var style = new LineTreatment
        {
            Simplify = 0.4,
            Weights = [new WeightDriver(WeightSource.Curvature, 0.5)],
        };

        var small = Outlines(new FieldTracer().Trace(
            Request(Blob(48, 48, 24f, 24f, 18f), 48, 48, style, scale: 1, brushSize: 6)))[0];
        var big = Outlines(new FieldTracer().Trace(
            Request(Blob(48, 48, 24f, 24f, 18f), 48, 48, style, scale: 2, brushSize: 12)))[0];

        Assert.Equal(small.Points.Count, big.Points.Count);
        for (var i = 0; i < small.Points.Count; i++)
        {
            Assert.Equal(small.Points[i].X * 2, big.Points[i].X, 6);
            Assert.Equal(small.Points[i].Pressure, big.Points[i].Pressure, 6);
        }
        output.WriteLine($"{small.Points.Count} points, pressures identical across a 2× element");
    }

    // ---- the line may leave the contour (Q118) --------------------------------

    [Fact]
    public void A_Broken_Line_Becomes_Several_Strokes()
    {
        var whole = Outlines(new FieldTracer().Trace(Request(Blob(64, 64, 32f, 32f, 26f), 64, 64)));
        var dashed = Outlines(new FieldTracer().Trace(Request(Blob(64, 64, 32f, 32f, 26f), 64, 64,
            new LineTreatment { BreakLength = 1.5, BreakGap = 1.0 })));

        output.WriteLine($"unbroken {whole.Count} stroke(s), broken {dashed.Count}");
        Assert.Single(whole);
        Assert.True(dashed.Count >= 4, $"a dashed silhouette came back as {dashed.Count} stroke(s)");
        Assert.All(dashed, s => Assert.True(s.Points.Count >= 2));
    }

    [Fact]
    public void A_Facing_Outline_Draws_Only_One_Side()
    {
        var strokes = Outlines(new FieldTracer().Trace(Request(Blob(64, 64, 32f, 32f, 26f), 64, 64,
            new LineTreatment { Covers = LineCoverage.Facing, CoverageAngleDeg = 0, CoverageSpreadDeg = 60 })));

        Assert.NotEmpty(strokes);
        var points = strokes.SelectMany(s => s.Points).ToList();
        var full = Outlines(new FieldTracer().Trace(Request(Blob(64, 64, 32f, 32f, 26f), 64, 64)))
            .SelectMany(s => s.Points).Count();

        output.WriteLine($"facing outline keeps {points.Count} of {full} points, " +
                         $"mean Y {points.Average(p => p.Y):F1} against centre 32");
        Assert.True(points.Count < full * 0.6, "a 60° fan should be well under half the contour");
        Assert.True(points.Average(p => p.Y) < 28, "coverage at 0° means the top, and the document's Y grows downward");
    }

    [Fact]
    public void An_Unbroken_Outline_Returns_To_Where_It_Started()
    {
        var line = Outlines(new FieldTracer().Trace(Request(Blob(64, 64, 32f, 32f, 24f), 64, 64)))[0];

        var gap = Math.Sqrt(Math.Pow(line.Points[0].X - line.Points[^1].X, 2)
                          + Math.Pow(line.Points[0].Y - line.Points[^1].Y, 2));
        Assert.Equal(0, gap, 9);
    }

    /// <summary>
    /// Offset is measured in stroke-widths, so the same treatment moves the line
    /// further when a fatter brush draws it. That is the unit doing its job —
    /// asserting only the direction would pass on a build that used pixels.
    /// </summary>
    [Fact]
    public void Offset_Moves_The_Line_By_Stroke_Widths()
    {
        static double Radius(double offset, double brushSize)
        {
            var line = Outlines(new FieldTracer().Trace(Request(Blob(96, 96, 48f, 48f, 36f), 96, 96,
                new LineTreatment { Offset = offset, Simplify = 0 }, brushSize: brushSize)))[0];
            return line.Points.Average(p => Math.Sqrt(Math.Pow(p.X - 48, 2) + Math.Pow(p.Y - 48, 2)));
        }

        var on = Radius(0, 1);
        var thin = Radius(1, 1) - on;
        var fat = Radius(1, 4) - on;
        var inward = on - Radius(-1, 4);

        output.WriteLine($"contour at {on:F2}; +1 width moves it {thin:F2} with a 1 px brush " +
                         $"and {fat:F2} with a 4 px one; −1 moves it {inward:F2} inward");

        Assert.Equal(1, thin, 1);
        Assert.Equal(4, fat, 1);
        Assert.Equal(4, inward, 1);
    }

    // ---- weight drivers -------------------------------------------------------

    [Fact]
    public void With_No_Drivers_Every_Point_Carries_The_Base_Weight()
    {
        var line = Outlines(new FieldTracer().Trace(
            Request(Blob(64, 64, 32f, 32f, 24f), 64, 64, new LineTreatment { BaseWeight = 0.4 })))[0];

        Assert.All(line.Points, p => Assert.Equal(0.4, p.Pressure, 9));
    }

    [Fact]
    public void Curvature_Makes_A_Tighter_Shape_Heavier()
    {
        static double Weight(float radius)
        {
            var line = Outlines(new FieldTracer().Trace(Request(Blob(96, 96, 48f, 48f, radius), 96, 96,
                new LineTreatment
                {
                    BaseWeight = 0.2,
                    Simplify = 0,
                    Weights = [new WeightDriver(WeightSource.Curvature, 1.0)],
                })))[0];
            return line.Points.Average(p => p.Pressure);
        }

        var tight = Weight(10f);
        var loose = Weight(40f);
        output.WriteLine($"mean pressure — tight blob {tight:F3}, loose blob {loose:F3}");
        Assert.True(tight > loose * 1.3, $"curvature did little: {tight:F3} against {loose:F3}");
    }

    [Fact]
    public void Light_Direction_Weights_The_Shadow_Side()
    {
        var line = Outlines(new FieldTracer().Trace(Request(Blob(64, 64, 32f, 32f, 24f), 64, 64,
            new LineTreatment
            {
                BaseWeight = 0.4,
                LightAngleDeg = 0,   // straight down from above
                Weights = [new WeightDriver(WeightSource.LightDirection, 0.5)],
            })))[0];

        var lit = line.Points.Where(p => p.Y < 26).Average(p => p.Pressure);
        var shadow = line.Points.Where(p => p.Y > 38).Average(p => p.Pressure);
        output.WriteLine($"lit top {lit:F3}, shadowed bottom {shadow:F3}");
        Assert.True(shadow > lit + 0.2, "the side facing away from the light should carry the weight");
    }

    [Fact]
    public void Band_Depth_Lightens_The_Inner_Bands()
    {
        var lines = Outlines(new FieldTracer().Trace(Request(Blob(64, 64, 32f, 32f, 26f), 64, 64,
            new LineTreatment
            {
                Outlined = OutlinedBands.Every,
                BaseWeight = 0.2,
                Weights = [new WeightDriver(WeightSource.BandDepth, 0.7)],
            })));

        Assert.Equal(3, lines.Count);
        var weights = lines.Select(l => l.Points.Average(p => p.Pressure)).ToList();
        output.WriteLine($"band weights {string.Join(", ", weights.Select(x => x.ToString("F3")))}");
        Assert.True(weights[0] > weights[1] && weights[1] > weights[2]);
    }

    [Fact]
    public void Arc_Taper_Lightens_A_Runs_Own_Ends()
    {
        var run = Outlines(new FieldTracer().Trace(Request(Blob(64, 64, 32f, 32f, 26f), 64, 64,
            new LineTreatment
            {
                BaseWeight = 0.9,
                BreakLength = 4,
                BreakGap = 2,
                Weights = [new WeightDriver(WeightSource.ArcTaper, 0.8)],
            })))[0];

        var ends = (run.Points[0].Pressure + run.Points[^1].Pressure) * 0.5;
        var middle = run.Points[run.Points.Count / 2].Pressure;
        output.WriteLine($"{run.Points.Count} points — ends {ends:F3}, middle {middle:F3}");
        Assert.True(middle > ends + 0.2, "a tapered run should start and finish lighter than its middle");
    }

    [Fact]
    public void Flow_Speed_Does_Nothing_Without_A_Velocity_Field()
    {
        var style = new LineTreatment
        {
            BaseWeight = 0.5,
            Weights = [new WeightDriver(WeightSource.FlowSpeed, 0.5)],
        };
        var line = Outlines(new FieldTracer().Trace(Request(Blob(64, 64, 32f, 32f, 24f), 64, 64, style)))[0];
        Assert.All(line.Points, p => Assert.Equal(0.5, p.Pressure, 9));

        var field = Blob(64, 64, 32f, 32f, 24f);
        var vx = new float[64 * 64];
        var vy = new float[64 * 64];
        for (var i = 0; i < vx.Length; i++) vx[i] = i % 64 < 32 ? 0.9f : 0.05f;

        var moving = new FieldTracer().Trace(new FieldTraceRequest
        {
            Field = field, VelocityX = vx, VelocityY = vy,
            Width = 64, Height = 64,
            Treatment = LineTreatment.Resolve(style),
            Colors = ["#888888"], Low = 0f, High = 1f,
            OutlineBrush = new BrushSettings { Size = 6 },
        });

        var pts = Outlines(moving)[0].Points;
        var fast = pts.Where(p => p.X < 30).Average(p => p.Pressure);
        var slow = pts.Where(p => p.X > 34).Average(p => p.Pressure);
        output.WriteLine($"fast side {fast:F3}, slow side {slow:F3}");
        Assert.True(fast > slow + 0.2, "the moving half of the field should carry more weight");
    }

    // ---- shaping --------------------------------------------------------------

    [Fact]
    public void Simplifying_Drops_Points_Without_Moving_The_Shape()
    {
        var raw = Outlines(new FieldTracer().Trace(Request(Blob(96, 96, 48f, 48f, 36f), 96, 96,
            new LineTreatment { Simplify = 0 })))[0];
        var clean = Outlines(new FieldTracer().Trace(Request(Blob(96, 96, 48f, 48f, 36f), 96, 96,
            new LineTreatment { Simplify = 1.5 })))[0];

        output.WriteLine($"{raw.Points.Count} points → {clean.Points.Count}");
        Assert.True(clean.Points.Count < raw.Points.Count * 0.7, "simplifying dropped almost nothing");

        var radii = clean.Points.Select(p => Math.Sqrt(Math.Pow(p.X - 48, 2) + Math.Pow(p.Y - 48, 2))).ToList();
        Assert.True(radii.Max() - radii.Min() < 4, "the simplified outline wandered off the circle");
    }

    [Fact]
    public void Smoothing_Keeps_The_Line_On_The_Shape()
    {
        var smoothed = Outlines(new FieldTracer().Trace(Request(Blob(96, 96, 48f, 48f, 36f), 96, 96,
            new LineTreatment { Simplify = 0.8, Smoothing = 1 })))[0];

        var radii = smoothed.Points.Select(p => Math.Sqrt(Math.Pow(p.X - 48, 2) + Math.Pow(p.Y - 48, 2))).ToList();
        output.WriteLine($"{smoothed.Points.Count} points, radius {radii.Min():F2}–{radii.Max():F2} against 27");
        Assert.True(smoothed.Points.Count >= 8);
        Assert.True(radii.Max() - radii.Min() < 4, "smoothing pulled the line off the shape");
    }

    // ---- determinism ----------------------------------------------------------

    [Fact]
    public void Two_Traces_Produce_Identical_Strokes()
    {
        var style = new LineTreatment
        {
            Outlined = OutlinedBands.Every,
            BreakLength = 3,
            BreakGap = 1.5,
            Weights = [new WeightDriver(WeightSource.Curvature, 0.4), new WeightDriver(WeightSource.ArcTaper, 0.3)],
        };

        var a = new FieldTracer().Trace(Request(Blob(72, 72, 36f, 36f, 28f), 72, 72, style));
        var b = new FieldTracer().Trace(Request(Blob(72, 72, 36f, 36f, 28f), 72, 72, style));

        Assert.Equal(a.Count, b.Count);
        Assert.True(a.Count > 5, $"test would be vacuous: only {a.Count} strokes");
        for (var i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Tool, b[i].Tool);
            Assert.Equal(a[i].Points, b[i].Points);
        }
    }

    [Fact]
    public void A_Reused_Tracer_Produces_What_A_Fresh_One_Would()
    {
        var tracer = new FieldTracer();
        tracer.Trace(Request(Blob(96, 96, 40f, 60f, 30f), 96, 96));
        var afterUse = tracer.Trace(Request(Blob(64, 48, 32f, 24f, 16f), 64, 48));

        var fresh = new FieldTracer().Trace(Request(Blob(64, 48, 32f, 24f, 16f), 64, 48));

        Assert.Equal(fresh.Count, afterUse.Count);
        for (var i = 0; i < fresh.Count; i++) Assert.Equal(fresh[i].Points, afterUse[i].Points);
    }

    [Fact]
    public void An_Empty_Field_Draws_Nothing()
    {
        Assert.Empty(new FieldTracer().Trace(Request(new float[32 * 32], 32, 32)));
    }

    // ---- budget ---------------------------------------------------------------

    /// <summary>
    /// Restyling re-traces rather than re-solving, and that is only worth
    /// anything if a re-trace is quick. The design's claim is ~30 ms for a
    /// 48-frame element; this is the regression guard around it, set well above
    /// the observed cost in the charter's usual way.
    /// </summary>
    [Fact]
    [Trait("Category", "Performance")]
    public void Retracing_A_Whole_Element_Is_Quick_Enough_To_Feel_Live()
    {
        const int w = 192, h = 108, frames = 48;
        var fields = Enumerable.Range(0, frames)
            .Select(f => Blob(w, h, 96f + f * 0.4f, 80f - f * 1.2f, 20f + f * 0.3f))
            .ToList();

        var style = new LineTreatment
        {
            Outlined = OutlinedBands.Every,
            Weights = [new WeightDriver(WeightSource.Curvature, 0.4), new WeightDriver(WeightSource.LightDirection, 0.3)],
        };

        var tracer = new FieldTracer();
        var strokes = 0;
        var ms = Bench.FastestMs(3, () =>
        {
            strokes = 0;
            foreach (var field in fields) strokes += tracer.Trace(Request(field, w, h, style)).Count;
        }, log: output);

        output.WriteLine($"{frames} frames re-traced in {ms:F1} ms ({strokes} strokes, {ms / frames:F2} ms/frame)");
        Assert.True(strokes > frames, "the fixture drew almost nothing, so nothing was measured");
        // 1200, up from 400 (2026-08-20): the fastest-of-3 sits well under
        // 200 ms alone, but under a full four-assembly parallel run on a
        // loaded container this tripped twice in one day at ~2 s while
        // passing every isolated re-run. The charter's budgets catch
        // order-of-magnitude regressions, not contention — 1200 still fails
        // a 10x regression and stops crying wolf on a busy machine.
        Assert.True(ms < 1200, $"re-tracing 48 frames took {ms:F0} ms");
    }
}
