using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.Raster.Media;

/// <summary>What to turn into drawings, and how.</summary>
public sealed class FieldTraceRequest
{
    /// <summary>The scalar field, row-major. Density for smoke, temperature for fire.</summary>
    public required float[] Field { get; init; }

    /// <summary>Cell-centred velocity, for <see cref="WeightSource.FlowSpeed"/>. Absent leaves that driver at nothing.</summary>
    public float[]? VelocityX { get; init; }

    public float[]? VelocityY { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>Where cell (0,0)'s top-left corner sits in the document.</summary>
    public double OriginX { get; init; }

    public double OriginY { get; init; }

    /// <summary>Document pixels per cell. The grid is deliberately coarser than the document.</summary>
    public double Scale { get; init; } = 1;

    public required ResolvedTreatment Treatment { get; init; }

    /// <summary>One colour per band, outermost first. Short lists repeat their last entry.</summary>
    public required IReadOnlyList<string> Colors { get; init; }

    /// <summary>The field range the bands are spread through.</summary>
    public float Low { get; init; }

    public float High { get; init; } = 1f;

    /// <summary>What lies beyond the field — see <see cref="MarchingSquares.Trace"/>.</summary>
    public float Outside { get; init; }

    /// <summary>The brush that draws the outline; its size is also the stroke-width every treatment distance is measured in.</summary>
    public BrushSettings? OutlineBrush { get; init; }

    public string OutlineColor { get; init; } = "#1a1a1a";
}

/// <summary>
/// Turns one frame of a scalar field into the strokes that draw it: filled
/// bands, and outlines drawn the way a treatment asks for.
/// </summary>
/// <remarks>
/// <para>
/// <b>It takes a field, not a solver</b>, and that is the load-bearing choice.
/// `field → iso-contour → strokes` is the stage that makes a drawing, and it
/// does not care whether the numbers came from the grid solver, from particles
/// splatted into a metaball field, or one day from a free surface. Fixing the
/// interface around a single producer would make the other two sources rewrites
/// rather than additions.
/// </para>
/// <para>
/// <b>Fills first, back to front, then every outline.</b> That is the cel order —
/// paint, then ink over it — and it means a silhouette line is never buried
/// under the band inside it.
/// </para>
/// <para>
/// <b>One band may emit several strokes</b> (Q118). Coverage and breaks both mask
/// the contour into runs, and each surviving run is its own stroke, so nothing
/// here may assume one contour makes one line. A partial outline — anime fire
/// drawn only along its upper edge — and a broken line are the same mechanism
/// seen twice.
/// </para>
/// <para>
/// <b>Everything is measured in stroke-widths and computed in cell units.</b>
/// The treatment's distances are relative to the outline brush's size, so a
/// style reads the same at any element scale (invariant 7); the geometry stays
/// in cell units until emission so the field can be sampled without mapping
/// backwards.
/// </para>
/// </remarks>
public sealed class FieldTracer
{
    /// <summary>Pressure floor. A dab at zero is not a light mark, it is a gap.</summary>
    private const double MinWeight = 0.02;

    /// <summary>Fraction of a run's length the arc taper acts over at each end.</summary>
    private const double TaperFraction = 0.15;

    private readonly MarchingSquares _marching = new();

    public List<Stroke> Trace(FieldTraceRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (r.Field.Length < r.Width * r.Height)
        {
            throw new ArgumentException(
                $"Field must hold {r.Width}×{r.Height} samples, got {r.Field.Length}.", nameof(r));
        }

        var t = r.Treatment;
        var bands = Math.Max(1, t.Bands);
        var width = r.OutlineBrush?.Size ?? new BrushSettings().Size;
        var widthInCells = r.Scale > 0 ? width / r.Scale : width;

        var fills = new List<Stroke>();
        var outlines = new List<Stroke>();

        for (var band = 0; band < bands; band++)
        {
            var level = LevelOf(band, bands, r.Low, r.High, t.Spacing);
            var loops = _marching.Trace(r.Field, r.Width, r.Height, level, r.Outside);
            if (loops.Count == 0) continue;

            var shaped = new List<List<StrokePoint>>(loops.Count);
            foreach (var loop in loops)
            {
                var s = Shape(loop, t, widthInCells);
                if (s.Count >= 3) shaped.Add(s);
            }
            if (shaped.Count == 0) continue;

            foreach (var group in Group(shaped))
            {
                fills.Add(new Stroke
                {
                    Tool = ToolKind.Fill,
                    Color = ColorOf(r.Colors, band),
                    Points = group.Outer.Select(p => ToDocument(p, r)).ToList(),
                    Holes = group.Holes.Count == 0
                        ? null
                        : group.Holes.Select(h => h.Select(p => ToDocument(p, r)).ToList()).ToList(),
                    Brush = new BrushSettings(),
                });
            }

            if (!IsOutlined(t.Outlined, band)) continue;

            foreach (var contour in shaped)
            {
                outlines.AddRange(Outline(contour, r, t, band, bands, widthInCells));
            }
        }

        fills.AddRange(outlines);
        return fills;
    }

    // ---- bands ---------------------------------------------------------------

    /// <summary>
    /// Where band <paramref name="band"/> sits in the field's range. Levels are
    /// interior to the range on purpose: a band at <c>High</c> encloses almost
    /// nothing and a band at <c>Low</c> encloses everything, so neither is worth
    /// spending one of a small number of bands on.
    /// </summary>
    internal static float LevelOf(int band, int bands, float low, float high, BandSpacing spacing)
    {
        var t = (band + 1) / (double)(bands + 1);
        if (spacing == BandSpacing.CoreBiased) t = Math.Sqrt(t);
        return (float)(low + (high - low) * t);
    }

    private static bool IsOutlined(OutlinedBands which, int band) => which switch
    {
        OutlinedBands.Every => true,
        OutlinedBands.Silhouette => band == 0,
        _ => false,
    };

    private static string ColorOf(IReadOnlyList<string> colors, int band) =>
        colors.Count == 0 ? "#808080" : colors[Math.Min(band, colors.Count - 1)];

    // ---- shaping -------------------------------------------------------------

    /// <summary>Simplify, then optionally refit — both in cell units, both measured in stroke-widths.</summary>
    private static List<StrokePoint> Shape(List<StrokePoint> loop, in ResolvedTreatment t, double widthInCells)
    {
        var simplified = t.Simplify > 0
            ? FloodFill.Simplify(loop, t.Simplify * widthInCells)
            : loop;

        return t.Smoothing > 0 ? Smooth(simplified, t, widthInCells) : simplified;
    }

    /// <summary>
    /// Replace the polyline with fitted cubics and re-flatten, breaking the fit
    /// at any turn sharper than <c>CornerAngleDeg</c> so a flame's tongue keeps
    /// its point instead of being rounded into a bubble.
    /// </summary>
    private static List<StrokePoint> Smooth(List<StrokePoint> loop, in ResolvedTreatment t, double widthInCells)
    {
        var tolerance = t.Smoothing * widthInCells * 0.5;
        if (tolerance <= 0 || loop.Count < 4) return loop;

        var cosLimit = Math.Cos(Math.PI - t.CornerAngleDeg * Math.PI / 180);
        var corners = new List<int> { 0 };
        for (var i = 1; i < loop.Count - 1; i++)
        {
            if (TurnCos(loop[i - 1], loop[i], loop[i + 1]) > cosLimit) corners.Add(i);
        }
        corners.Add(loop.Count - 1);

        var result = new List<StrokePoint>(loop.Count);
        for (var c = 0; c < corners.Count - 1; c++)
        {
            var from = corners[c];
            var to = corners[c + 1];
            var span = loop.GetRange(from, to - from + 1);

            var fitted = span.Count >= 4 ? CurveFitter.Fit(span, tolerance) : null;
            var flat = fitted is null ? span : PathFlattener.Flatten(fitted);

            // The join is shared with the previous span, so drop its first point.
            if (result.Count > 0 && flat.Count > 0) flat.RemoveAt(0);
            result.AddRange(flat);
        }

        return result.Count >= 3 ? result : loop;
    }

    /// <summary>Cosine of the angle at <paramref name="b"/>; near 1 is a hairpin, near −1 is straight.</summary>
    private static double TurnCos(StrokePoint a, StrokePoint b, StrokePoint c)
    {
        double ux = a.X - b.X, uy = a.Y - b.Y, vx = c.X - b.X, vy = c.Y - b.Y;
        var lu = Math.Sqrt(ux * ux + uy * uy);
        var lv = Math.Sqrt(vx * vx + vy * vy);
        return lu < 1e-12 || lv < 1e-12 ? -1 : (ux * vx + uy * vy) / (lu * lv);
    }

    // ---- outer contours and their holes ---------------------------------------

    private sealed record Region(List<StrokePoint> Outer, List<List<StrokePoint>> Holes);

    /// <summary>
    /// Sort the loops into filled regions and the holes inside them.
    /// </summary>
    /// <remarks>
    /// Marching keeps the inside on the left, so in the document's y-down space
    /// an outer contour signs negative and a hole signs positive —
    /// <c>MarchingSquaresTests.An_Annulus_…</c> is what holds that convention
    /// still. A hole goes to the smallest outer contour that contains it, which
    /// is the only assignment that survives one blob sitting inside another
    /// blob's hole.
    /// </remarks>
    private static List<Region> Group(List<List<StrokePoint>> loops)
    {
        var outers = new List<(List<StrokePoint> Loop, double Area)>();
        var holes = new List<List<StrokePoint>>();

        foreach (var loop in loops)
        {
            var area = SignedArea(loop);
            if (area < 0) outers.Add((loop, -area));
            else holes.Add(loop);
        }

        // A field whose every loop signed positive would otherwise vanish; take
        // them as outers rather than dropping the frame.
        if (outers.Count == 0)
        {
            return [.. holes.Select(h => new Region(h, []))];
        }

        var regions = outers.Select(o => new Region(o.Loop, [])).ToList();
        foreach (var hole in holes)
        {
            var best = -1;
            var bestArea = double.MaxValue;
            for (var i = 0; i < outers.Count; i++)
            {
                if (outers[i].Area >= bestArea) continue;
                if (!GeometryOps.ContainsEvenOdd([outers[i].Loop], hole[0].X, hole[0].Y)) continue;
                best = i;
                bestArea = outers[i].Area;
            }
            if (best >= 0) regions[best].Holes.Add(hole);
        }

        return regions;
    }

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

    // ---- outlines --------------------------------------------------------------

    /// <summary>
    /// Draw one contour: offset it, weight it, mask it down to the parts this
    /// style draws, and emit a stroke per surviving run.
    /// </summary>
    private List<Stroke> Outline(
        List<StrokePoint> contour, FieldTraceRequest r, in ResolvedTreatment t,
        int band, int bands, double widthInCells)
    {
        var n = contour.Count;
        var placed = new List<StrokePoint>(n);
        var keep = new bool[n];

        var taper = 0.0;
        foreach (var w in t.Weights)
        {
            if (w.Source == WeightSource.ArcTaper) taper += w.Amount;
        }

        var coverDir = Direction(t.CoverageAngleDeg);
        var lightDir = Direction(t.LightAngleDeg);
        var coverCos = Math.Cos(Math.Clamp(t.CoverageSpreadDeg, 0, 180) * Math.PI / 180);

        for (var i = 0; i < n; i++)
        {
            var p = contour[i];
            var (nx, ny, gradient) = Outward(r, p);

            var x = p.X + nx * t.Offset * widthInCells;
            var y = p.Y + ny * t.Offset * widthInCells;

            var weight = t.BaseWeight;
            foreach (var driver in t.Weights)
            {
                weight += driver.Amount * driver.Source switch
                {
                    WeightSource.Curvature => CurvatureSignal(contour, i, widthInCells),
                    WeightSource.FlowSpeed => FlowSignal(r, p),
                    WeightSource.FieldGradient => Math.Clamp(gradient / Math.Max(r.High - r.Low, 1e-6f), 0, 1),
                    WeightSource.LightDirection => (1 - (nx * lightDir.X + ny * lightDir.Y)) * 0.5,
                    WeightSource.BandDepth => bands <= 1 ? 1 : 1 - band / (double)(bands - 1),
                    _ => 0,   // ArcTaper is per emitted run, applied below
                };
            }

            placed.Add(new StrokePoint(x, y, Math.Clamp(weight, MinWeight, 1)));
            keep[i] = t.Covers != LineCoverage.Facing
                || nx * coverDir.X + ny * coverDir.Y >= coverCos;
        }

        if (t.BreakLength > 0) ApplyBreaks(placed, keep, t, widthInCells);

        return Runs(placed, keep, r, taper);
    }

    /// <summary>
    /// Punch the line into dashes by arc length. Same mechanism as coverage —
    /// mask, then split — because a broken line and a partial line are the same
    /// thing seen twice.
    /// </summary>
    private static void ApplyBreaks(
        List<StrokePoint> points, bool[] keep, in ResolvedTreatment t, double widthInCells)
    {
        var on = t.BreakLength * widthInCells;
        var off = Math.Max(t.BreakGap, 0) * widthInCells;
        var period = on + off;
        if (period <= 1e-9) return;

        double travelled = 0;
        for (var i = 0; i < points.Count; i++)
        {
            if (i > 0) travelled += Distance(points[i - 1], points[i]);
            if (travelled % period >= on) keep[i] = false;
        }
    }

    /// <summary>Every run of kept points, as its own stroke.</summary>
    private List<Stroke> Runs(List<StrokePoint> points, bool[] keep, FieldTraceRequest r, double taper)
    {
        var strokes = new List<Stroke>();
        var whole = Array.TrueForAll(keep, k => k);

        var i = 0;
        while (i < points.Count)
        {
            if (!keep[i]) { i++; continue; }

            var start = i;
            while (i < points.Count && keep[i]) i++;

            var run = points.GetRange(start, i - start);

            // An untouched contour is a closed line, so it has to come back to
            // where it began — otherwise every element carries a hairline gap at
            // whichever arbitrary point the trace happened to start from.
            if (whole && run.Count >= 3) run.Add(run[0]);
            if (run.Count < 2) continue;

            if (Math.Abs(taper) > 1e-9) ApplyTaper(run, taper);

            strokes.Add(new Stroke
            {
                Tool = ToolKind.Brush,
                Color = r.OutlineColor,
                Points = run.Select(p => ToDocument(p, r)).ToList(),
                Brush = r.OutlineBrush?.Clone() ?? new BrushSettings(),
            });
        }

        return strokes;
    }

    /// <summary>Lighten a run toward its own ends, so it reads as drawn rather than stamped.</summary>
    private static void ApplyTaper(List<StrokePoint> run, double amount)
    {
        var lengths = new double[run.Count];
        for (var i = 1; i < run.Count; i++) lengths[i] = lengths[i - 1] + Distance(run[i - 1], run[i]);

        var total = lengths[^1];
        if (total <= 1e-9) return;
        var over = total * TaperFraction;

        for (var i = 0; i < run.Count; i++)
        {
            var edge = Math.Min(lengths[i], total - lengths[i]);
            var s = Math.Clamp(edge / over, 0, 1);
            var eased = s * s * (3 - 2 * s);
            var p = run[i];
            run[i] = p with { Pressure = Math.Clamp(p.Pressure + amount * (eased - 1), MinWeight, 1) };
        }
    }

    // ---- signals ----------------------------------------------------------------

    /// <summary>
    /// The way out of the fluid, and how steeply the field falls off here.
    /// </summary>
    /// <remarks>
    /// Taken from the field's own gradient rather than from the contour's
    /// winding, which is what makes it right inside a hole without a special
    /// case: the way out of the smoke is downhill either way.
    /// </remarks>
    private static (double X, double Y, double Gradient) Outward(FieldTraceRequest r, StrokePoint p)
    {
        var gx = Sample(r.Field, r, p.X + 0.5, p.Y) - Sample(r.Field, r, p.X - 0.5, p.Y);
        var gy = Sample(r.Field, r, p.X, p.Y + 0.5) - Sample(r.Field, r, p.X, p.Y - 0.5);
        var len = Math.Sqrt(gx * gx + gy * gy);
        return len < 1e-9 ? (0, 0, 0) : (-gx / len, -gy / len, len);
    }

    private static double CurvatureSignal(List<StrokePoint> contour, int i, double widthInCells)
    {
        var n = contour.Count;
        var a = contour[(i - 1 + n) % n];
        var b = contour[i];
        var c = contour[(i + 1) % n];

        var span = (Distance(a, b) + Distance(b, c)) * 0.5;
        if (span < 1e-9) return 0;

        // Turn per unit length is 1/radius; measured against the line's own width
        // it is scale-free, which is what lets one treatment read the same on a
        // small element and a large one.
        var turn = Math.Acos(Math.Clamp(-TurnCos(a, b, c), -1, 1));
        return Math.Clamp(turn / span * widthInCells, 0, 1);
    }

    private static double FlowSignal(FieldTraceRequest r, StrokePoint p)
    {
        if (r.VelocityX is null || r.VelocityY is null) return 0;
        var vx = Sample(r.VelocityX, r, p.X, p.Y);
        var vy = Sample(r.VelocityY, r, p.X, p.Y);
        return Math.Clamp(Math.Sqrt(vx * vx + vy * vy), 0, 1);
    }

    /// <summary>Bilinear sample in cell units, clamped — sample (i,j) sits at (i+0.5, j+0.5).</summary>
    private static double Sample(float[] field, FieldTraceRequest r, double x, double y)
    {
        var gx = Math.Clamp(x - 0.5, 0, r.Width - 1.0);
        var gy = Math.Clamp(y - 0.5, 0, r.Height - 1.0);
        int x0 = (int)gx, y0 = (int)gy;
        int x1 = Math.Min(x0 + 1, r.Width - 1), y1 = Math.Min(y0 + 1, r.Height - 1);
        double tx = gx - x0, ty = gy - y0;

        var top = field[y0 * r.Width + x0] * (1 - tx) + field[y0 * r.Width + x1] * tx;
        var bottom = field[y1 * r.Width + x0] * (1 - tx) + field[y1 * r.Width + x1] * tx;
        return top * (1 - ty) + bottom * ty;
    }

    /// <summary>Degrees clockwise from up, in the document's y-down space.</summary>
    private static (double X, double Y) Direction(double degrees)
    {
        var rad = degrees * Math.PI / 180;
        return (Math.Sin(rad), -Math.Cos(rad));
    }

    private static double Distance(StrokePoint a, StrokePoint b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static StrokePoint ToDocument(StrokePoint p, FieldTraceRequest r) =>
        new(r.OriginX + p.X * r.Scale, r.OriginY + p.Y * r.Scale, p.Pressure);
}
