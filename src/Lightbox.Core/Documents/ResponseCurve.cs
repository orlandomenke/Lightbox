namespace Lightbox.Core.Documents;

/// <summary>
/// What a pressure curve is allowed to drive. Each one varies a value the
/// engine already reads, per dab.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is applied inside the dab walk, which is what keeps the list
/// honest. <c>Opacity</c> is deliberately absent: it is a <em>stroke-level</em>
/// cap that overlapping dabs never exceed, so "opacity follows pressure" would
/// have to mean something different at every dab and could not mean anything at
/// the stroke. Flow is the per-dab control and is what an artist actually wants
/// when they say transparency.
/// </para>
/// <para>
/// Rotation is absent for a different reason: nothing an artist does with a pen
/// makes "press harder, turn the nib" a gesture. Rotation follows the stroke's
/// direction, or it jitters; both already exist.
/// </para>
/// </remarks>
public enum BrushDynamic
{
    /// <summary>Dab diameter. The one everybody expects.</summary>
    Size,

    /// <summary>Per-dab paint amount — what the tool bar calls transparency.</summary>
    Flow,

    /// <summary>Dab edge falloff. Light pressure, softer edge.</summary>
    Hardness,

    /// <summary>How far dabs are thrown off the path.</summary>
    Scatter,

    /// <summary>How squashed the dab is. Press harder, fatter mark.</summary>
    Roundness,

    /// <summary>How much of the brush's own colour a smudge adds as it goes.</summary>
    ColorRate,

    /// <summary>How far a smudge drags what it picked up.</summary>
    SmudgeLength,
}

/// <summary>One handle on a response curve. Both coordinates are 0..1.</summary>
public readonly record struct CurvePoint(double In, double Out);

/// <summary>
/// An artist-editable response curve: pen pressure in, a multiplier out.
/// </summary>
/// <remarks>
/// <para>
/// <b>Monotone cubic, not a plain spline.</b> A Catmull-Rom or natural cubic
/// through the same handles overshoots between them, so a curve whose points all
/// sit inside 0..1 can still evaluate to 1.2 halfway along. For size that is a
/// dab wider than the brush; for flow it is paint darker than the colour. The
/// Fritsch-Carlson construction limits the tangents so the interpolant is
/// monotone on every segment it can be — which is exactly the guarantee this
/// needs, and it costs the same arithmetic as the version that lies.
/// </para>
/// <para>
/// <b>Deterministic, and part of the stroke record.</b> Evaluation is pure
/// arithmetic on the stored handles, so a re-render, an undo and an AI inbetween
/// all take the same path — invariant 2. The curve is cloned into the stroke
/// like every other setting that reaches pixels, so editing a brush never
/// repaints old art — invariant 4.
/// </para>
/// </remarks>
public sealed class ResponseCurve
{
    /// <summary>
    /// Handles, in pressure order. Two are enough and two is the default: the
    /// straight line, which is what a curve means before anybody has touched it.
    /// </summary>
    public List<CurvePoint> Points { get; set; } = [new(0, 0), new(1, 1)];

    /// <summary>The most handles the editor will keep. Beyond this it is a drawing, not a curve.</summary>
    public const int MaxPoints = 16;

    /// <summary>The straight line: pressure through, unchanged.</summary>
    public static ResponseCurve Linear() => new();

    /// <summary>
    /// The curve a gamma exponent describes, sampled at enough points to be
    /// indistinguishable from it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a <b>shape to start editing from</b>, not a replacement for the
    /// exponent. It is what lets the editor open a brush made before curves
    /// existed and show that brush's actual response instead of a straight line
    /// that would silently flatten it. Rendering never comes through here: a
    /// target with no curve of its own evaluates <c>p^γ</c> directly, so old art
    /// is untouched until somebody deliberately drags a handle.
    /// </para>
    /// <para>
    /// <b>Sampled along whichever axis the exponent is well-behaved in.</b>
    /// Above 1 the slope is bounded and even pressure steps are right; below it
    /// the slope at zero is infinite, and even steps leave the whole knee to the
    /// interpolator — <c>p^0.5</c> lands 0.09 low at a feather touch. Stepping
    /// evenly through the <em>output</em> instead, <c>x = (i/n)^(1/γ)</c>,
    /// crowds handles into the part that is moving.
    /// </para>
    /// <para>
    /// That gets the worst case to about 2%, and no number of handles removes
    /// the rest: the monotone limiter caps each tangent at three times the
    /// smaller neighbouring secant, which is exactly what stops the overshoot
    /// and exactly what refuses to turn a corner as sharply as <c>√p</c> does at
    /// the origin. Doubling the handles from 13 to 16 moves it by 0.003. Since
    /// what this feeds is a curve drawn a couple of hundred pixels tall and then
    /// edited by hand, that is the right trade rather than a shortfall.
    /// </para>
    /// </remarks>
    public static ResponseCurve FromGamma(double gamma)
    {
        const int Steps = 12;
        var g = Math.Max(gamma, 0);

        // Zero is the off switch everywhere else in the brush, so the curve it
        // means is the constant 1 — not x^0, which is 1 everywhere except at a
        // feather touch, where it would be a brush that paints nothing.
        if (g <= 0) return new ResponseCurve { Points = [new(0, 1), new(1, 1)] };

        var points = new List<CurvePoint>(Steps + 1);
        for (var i = 0; i <= Steps; i++)
        {
            var y = i / (double)Steps;
            points.Add(new CurvePoint(Math.Pow(y, 1 / g), y));
        }
        return new ResponseCurve { Points = points };
    }

    /// <summary>
    /// The multiplier at a given pressure, 0..1 in and 0..1 out.
    /// </summary>
    public double Evaluate(double x)
    {
        var p = Sorted();
        if (p.Count == 0) return Math.Clamp(x, 0, 1);
        if (p.Count == 1) return Math.Clamp(p[0].Out, 0, 1);

        x = Math.Clamp(x, 0, 1);

        // Flat outside the handles rather than extrapolated. A curve that has
        // not been given a handle at 0 is saying nothing about what happens
        // below its first one, and inventing a slope there is how a brush ends
        // up with a negative size at a light touch.
        if (x <= p[0].In) return Math.Clamp(p[0].Out, 0, 1);
        if (x >= p[^1].In) return Math.Clamp(p[^1].Out, 0, 1);

        var i = 0;
        while (i < p.Count - 2 && x > p[i + 1].In) i++;

        var h = p[i + 1].In - p[i].In;
        if (h <= 0) return Math.Clamp(p[i + 1].Out, 0, 1);

        var (m0, m1) = Tangents(p, i);
        var t = (x - p[i].In) / h;
        var t2 = t * t;
        var t3 = t2 * t;

        // Hermite basis.
        var y = (2 * t3 - 3 * t2 + 1) * p[i].Out
                + (t3 - 2 * t2 + t) * h * m0
                + (-2 * t3 + 3 * t2) * p[i + 1].Out
                + (t3 - t2) * h * m1;

        return Math.Clamp(y, 0, 1);
    }

    /// <summary>
    /// Fritsch-Carlson tangents for the segment starting at <paramref name="i"/>.
    /// </summary>
    /// <remarks>
    /// A one-sided difference at the ends, the average of the neighbouring
    /// secants in the middle, and then the limiter: where the two secants
    /// disagree in sign the point is a local extremum and the tangent is zero,
    /// and otherwise the tangent is capped at three times the smaller secant.
    /// Those two rules are the whole of what stops the curve overshooting.
    /// </remarks>
    private static (double M0, double M1) Tangents(List<CurvePoint> p, int i)
    {
        double Secant(int a) => (p[a + 1].Out - p[a].Out) / (p[a + 1].In - p[a].In);

        double Tangent(int k)
        {
            if (k == 0) return Secant(0);
            if (k == p.Count - 1) return Secant(p.Count - 2);

            double before = Secant(k - 1), after = Secant(k);
            if (before * after <= 0) return 0;

            var m = (before + after) / 2;
            var limit = 3 * Math.Min(Math.Abs(before), Math.Abs(after));
            return Math.Sign(m) * Math.Min(Math.Abs(m), limit);
        }

        return (Tangent(i), Tangent(i + 1));
    }

    /// <summary>
    /// The handles in pressure order, with duplicates at the same pressure
    /// dropped. The editor keeps them ordered, but a hand-edited file or an
    /// importer need not, and an out-of-order list would make the interpolation
    /// fold back on itself.
    /// </summary>
    private List<CurvePoint> Sorted()
    {
        var ordered = Points
            .Where(q => double.IsFinite(q.In) && double.IsFinite(q.Out))
            .Select(q => new CurvePoint(Math.Clamp(q.In, 0, 1), Math.Clamp(q.Out, 0, 1)))
            .OrderBy(q => q.In)
            .ToList();

        for (var i = ordered.Count - 1; i > 0; i--)
        {
            if (ordered[i].In - ordered[i - 1].In < 1e-9) ordered.RemoveAt(i);
        }
        return ordered;
    }

    public ResponseCurve Clone() => new() { Points = [.. Points] };
}

/// <summary>
/// The one place pressure becomes a multiplier, so the engine, the brush cursor
/// and the tests cannot disagree about the answer.
/// </summary>
public static class PressureResponse
{
    /// <summary>
    /// The multiplier for a dynamic at a given pressure. 1 when nothing is
    /// driving it, which is what makes every one of these safe to ignore.
    /// </summary>
    /// <remarks>
    /// <b>The gamma is the curve's default shape, not a rival mechanism.</b>
    /// Three of these targets shipped with a <c>p^γ</c> exponent and files,
    /// presets and imports are full of them. So a target with no curve falls
    /// back to its gamma and renders exactly as it always did; a curve, once
    /// there, wins. That keeps one evaluation path at render time and leaves
    /// nothing to migrate.
    /// </remarks>
    public static double Factor(BrushSettings brush, BrushDynamic target, double pressure)
    {
        if (!brush.PressureEnabled) return 1;

        if (brush.Curves is { } curves && curves.TryGetValue(target, out var curve) && curve is not null)
        {
            return curve.Evaluate(Math.Clamp(pressure, 0, 1));
        }

        var gamma = target switch
        {
            BrushDynamic.Size => brush.PressureSizeGamma,
            BrushDynamic.Flow => brush.PressureFlowGamma,
            BrushDynamic.Hardness => brush.PressureHardnessGamma,
            _ => 0,
        };

        return gamma <= 0 ? 1 : Math.Pow(Math.Clamp(pressure, 0, 1), gamma);
    }

    /// <summary>
    /// Whether anything at all drives this target — what the tool bar's
    /// checkbox reads, and what the engine uses to skip work.
    /// </summary>
    public static bool IsDriven(BrushSettings brush, BrushDynamic target) =>
        brush.Curves?.ContainsKey(target) == true
        || target switch
        {
            BrushDynamic.Size => brush.PressureSizeGamma > 0,
            BrushDynamic.Flow => brush.PressureFlowGamma > 0,
            BrushDynamic.Hardness => brush.PressureHardnessGamma > 0,
            _ => false,
        };

    /// <summary>
    /// The curve to <em>show</em> for a target: the stored one, or the shape its
    /// gamma describes. Never null, so the editor always has something to draw.
    /// </summary>
    public static ResponseCurve Shape(BrushSettings brush, BrushDynamic target)
    {
        if (brush.Curves?.TryGetValue(target, out var curve) == true && curve is not null) return curve.Clone();

        return target switch
        {
            BrushDynamic.Size => ResponseCurve.FromGamma(brush.PressureSizeGamma),
            BrushDynamic.Flow => ResponseCurve.FromGamma(brush.PressureFlowGamma),
            BrushDynamic.Hardness => ResponseCurve.FromGamma(brush.PressureHardnessGamma),
            _ => ResponseCurve.Linear(),
        };
    }
}
