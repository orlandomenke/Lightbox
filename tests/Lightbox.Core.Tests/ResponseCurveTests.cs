using Lightbox.Core.Documents;

namespace Lightbox.Core.Tests;

/// <summary>
/// Artist-drawn pressure curves. What the engine, the brush cursor and the
/// editor all read, so the arithmetic is asserted once here rather than three
/// times where it is used.
/// </summary>
public class ResponseCurveTests
{
    [Fact]
    public void ACurveNobodyHasTouchedIsAStraightLine()
    {
        var curve = ResponseCurve.Linear();

        for (var i = 0; i <= 10; i++)
        {
            var x = i / 10.0;
            Assert.Equal(x, curve.Evaluate(x), 1e-9);
        }
    }

    [Fact]
    public void ACurveNeverLeavesTheUnitSquare()
    {
        // The whole reason this is a monotone cubic and not a plain spline.
        // A Catmull-Rom through these handles overshoots past 1 between the
        // last two, which for size is a dab wider than the brush and for flow
        // is paint darker than the colour picked.
        var curve = new ResponseCurve
        {
            Points = [new(0, 0), new(0.2, 0.02), new(0.5, 0.95), new(0.8, 1), new(1, 1)],
        };

        for (var i = 0; i <= 1000; i++)
        {
            var y = curve.Evaluate(i / 1000.0);
            Assert.InRange(y, 0, 1);
        }
    }

    [Fact]
    public void ARisingCurveNeverDips()
    {
        // Monotone is not just "stays in range". A curve an artist drew as
        // always-increasing must not wobble downward between two handles,
        // because that reads as the pen losing pressure when it did not.
        var curve = new ResponseCurve
        {
            Points = [new(0, 0), new(0.1, 0.6), new(0.5, 0.62), new(0.9, 0.65), new(1, 1)],
        };

        var previous = -1.0;
        for (var i = 0; i <= 1000; i++)
        {
            var y = curve.Evaluate(i / 1000.0);
            Assert.True(y >= previous - 1e-9, $"the curve dipped at {i / 1000.0:F3}: {previous:F4} -> {y:F4}");
            previous = y;
        }
    }

    [Fact]
    public void TheHandlesThemselvesAreHitExactly()
    {
        // An interpolant that misses its own control points is not an editor,
        // it is a suggestion box.
        var curve = new ResponseCurve
        {
            Points = [new(0, 0.1), new(0.3, 0.8), new(0.7, 0.2), new(1, 0.9)],
        };

        foreach (var point in curve.Points)
        {
            Assert.Equal(point.Out, curve.Evaluate(point.In), 1e-9);
        }
    }

    [Fact]
    public void OutsideItsHandlesTheCurveIsFlat()
    {
        // Not extrapolated. A curve with no handle at zero says nothing about
        // what happens at a feather touch, and inventing a slope there is how
        // a brush ends up with a negative size.
        var curve = new ResponseCurve { Points = [new(0.3, 0.4), new(0.7, 0.9)] };

        Assert.Equal(0.4, curve.Evaluate(0), 1e-9);
        Assert.Equal(0.4, curve.Evaluate(0.1), 1e-9);
        Assert.Equal(0.9, curve.Evaluate(0.9), 1e-9);
        Assert.Equal(0.9, curve.Evaluate(1), 1e-9);
    }

    [Fact]
    public void AGammaBecomesTheCurveItDescribes()
    {
        // What lets the editor open a brush made before it existed. Three
        // percent, not three tenths of one: the monotone limiter caps each
        // tangent at three times the smaller neighbouring secant, which is
        // exactly what stops the overshoot and exactly what refuses to turn a
        // corner as sharply as √p does at the origin. No handle count removes
        // the rest — 16 instead of 13 moves it by 0.003. That is fine, because
        // this is a shape to start editing from and the render path still
        // evaluates the exponent itself.
        foreach (var gamma in new[] { 0.3, 0.5, 0.8, 1.0, 1.4, 2.0, 3.0, 4.0 })
        {
            var curve = ResponseCurve.FromGamma(gamma);
            Assert.True(curve.Points.Count <= ResponseCurve.MaxPoints,
                $"gamma {gamma} produced {curve.Points.Count} handles — more than the editor keeps");

            for (var i = 0; i <= 200; i++)
            {
                var x = i / 200.0;
                Assert.True(Math.Abs(curve.Evaluate(x) - Math.Pow(x, gamma)) < 0.03,
                    $"gamma {gamma} at {x:F3}: {curve.Evaluate(x):F4} vs {Math.Pow(x, gamma):F4}");
            }
        }

        // At 1 it is not an approximation at all — the identity has to be exact,
        // because that is the setting most brushes ship with.
        var linear = ResponseCurve.FromGamma(1);
        for (var i = 0; i <= 20; i++) Assert.Equal(i / 20.0, linear.Evaluate(i / 20.0), 1e-9);
    }

    [Fact]
    public void AGammaOfZeroMeansNoResponseAtAll()
    {
        // Zero is the off switch everywhere else in the brush, so the curve it
        // describes has to be the constant 1 rather than the constant 0 — which
        // is what x^0 would give at every x but zero, and is a brush that
        // paints nothing at a feather touch.
        var curve = ResponseCurve.FromGamma(0);

        Assert.Equal(1, curve.Evaluate(0), 1e-9);
        Assert.Equal(1, curve.Evaluate(0.5), 1e-9);
        Assert.Equal(1, curve.Evaluate(1), 1e-9);
    }

    [Fact]
    public void HandlesOutOfOrderAreSortedRatherThanFollowed()
    {
        // The editor keeps them ordered; a hand-edited file or an importer need
        // not, and an unsorted list makes the interpolation fold back on itself.
        var tidy = new ResponseCurve { Points = [new(0, 0), new(0.5, 0.9), new(1, 1)] };
        var jumbled = new ResponseCurve { Points = [new(1, 1), new(0, 0), new(0.5, 0.9)] };

        for (var i = 0; i <= 10; i++)
        {
            Assert.Equal(tidy.Evaluate(i / 10.0), jumbled.Evaluate(i / 10.0), 1e-9);
        }
    }

    [Fact]
    public void TwoHandlesAtTheSamePressureDoNotDivideByZero()
    {
        var curve = new ResponseCurve { Points = [new(0, 0), new(0.5, 0.3), new(0.5, 0.8), new(1, 1)] };

        for (var i = 0; i <= 100; i++)
        {
            var y = curve.Evaluate(i / 100.0);
            Assert.True(double.IsFinite(y), $"not finite at {i / 100.0:F2}");
            Assert.InRange(y, 0, 1);
        }
    }

    [Fact]
    public void ADegenerateCurveStillAnswers()
    {
        // A malformed brush must not stop somebody drawing.
        Assert.Equal(0.5, new ResponseCurve { Points = [] }.Evaluate(0.5), 1e-9);
        Assert.Equal(0.7, new ResponseCurve { Points = [new(0.2, 0.7)] }.Evaluate(0.9), 1e-9);
        Assert.InRange(new ResponseCurve { Points = [new(double.NaN, 0.5), new(1, 1)] }.Evaluate(0.5), 0, 1);
    }

    [Fact]
    public void TheSameCurveAnswersTheSameWayEveryTime()
    {
        // Invariant 2. A curve is evaluated on load, on undo and by the
        // inbetweener, and all three have to agree bit for bit.
        var curve = new ResponseCurve { Points = [new(0, 0), new(0.37, 0.62), new(1, 0.91)] };

        for (var i = 0; i <= 100; i++)
        {
            var x = i / 100.0;
            Assert.Equal(
                BitConverter.DoubleToInt64Bits(curve.Evaluate(x)),
                BitConverter.DoubleToInt64Bits(curve.Evaluate(x)));
        }
    }
}

/// <summary>
/// How a curve, a gamma and the master switch decide one multiplier between
/// them. One place, because the engine and the brush cursor both read it and a
/// ring that disagrees with the stroke is worse than no ring.
/// </summary>
public class PressureResponseTests
{
    private static BrushSettings Brush(Action<BrushSettings>? tweak = null)
    {
        var brush = new BrushSettings();
        tweak?.Invoke(brush);
        return brush;
    }

    [Fact]
    public void ABrushWithNoCurvesBehavesExactlyAsItAlwaysDid()
    {
        // The whole compatibility claim in one test: every file, preset and
        // imported .abr in existence is written in gammas.
        var brush = Brush(b =>
        {
            b.PressureSizeGamma = 1.4;
            b.PressureFlowGamma = 0.8;
            b.PressureHardnessGamma = 2;
        });

        for (var i = 0; i <= 10; i++)
        {
            var p = i / 10.0;
            Assert.Equal(Math.Pow(p, 1.4), PressureResponse.Factor(brush, BrushDynamic.Size, p), 1e-12);
            Assert.Equal(Math.Pow(p, 0.8), PressureResponse.Factor(brush, BrushDynamic.Flow, p), 1e-12);
            Assert.Equal(Math.Pow(p, 2), PressureResponse.Factor(brush, BrushDynamic.Hardness, p), 1e-12);
        }
    }

    [Fact]
    public void ATargetNothingDrivesReturnsOne()
    {
        // Which is what makes every one of these safe to ignore: an untouched
        // dynamic multiplies by one and the engine's arithmetic is unchanged.
        var brush = Brush();

        foreach (var target in Enum.GetValues<BrushDynamic>())
        {
            if (target == BrushDynamic.Size) continue; // ships at gamma 1
            Assert.Equal(1, PressureResponse.Factor(brush, target, 0.3), 1e-12);
            Assert.False(PressureResponse.IsDriven(brush, target));
        }
    }

    [Fact]
    public void ACurveWinsOverTheGammaForTheSameTarget()
    {
        var brush = Brush(b =>
        {
            b.PressureSizeGamma = 3;
            b.Curves = new() { [BrushDynamic.Size] = ResponseCurve.Linear() };
        });

        Assert.Equal(0.5, PressureResponse.Factor(brush, BrushDynamic.Size, 0.5), 1e-9);
        Assert.NotEqual(Math.Pow(0.5, 3), PressureResponse.Factor(brush, BrushDynamic.Size, 0.5), 3);
    }

    [Fact]
    public void TheMasterSwitchBeatsEverything()
    {
        // Off means the tablet is ignored entirely — a curve must not be a way
        // back in, or the checkbox lies.
        var brush = Brush(b =>
        {
            b.PressureEnabled = false;
            b.Curves = new() { [BrushDynamic.Flow] = new ResponseCurve { Points = [new(0, 0), new(1, 0)] } };
        });

        Assert.Equal(1, PressureResponse.Factor(brush, BrushDynamic.Flow, 0.1), 1e-12);
        Assert.Equal(1, PressureResponse.Factor(brush, BrushDynamic.Size, 0.1), 1e-12);
    }

    [Fact]
    public void TheShapeToShowIsTheCurveOrTheGammasOwn()
    {
        // So the editor always has something to draw, and what it draws for an
        // old brush is that brush's actual response rather than a straight line
        // that would silently flatten it the moment anybody touched it.
        var old = Brush(b => b.PressureSizeGamma = 2);
        var shape = PressureResponse.Shape(old, BrushDynamic.Size);
        Assert.True(Math.Abs(shape.Evaluate(0.5) - 0.25) < 0.005);

        var drawn = new ResponseCurve { Points = [new(0, 1), new(1, 0)] };
        var modern = Brush(b => b.Curves = new() { [BrushDynamic.Size] = drawn });
        Assert.Equal(0.5, PressureResponse.Shape(modern, BrushDynamic.Size).Evaluate(0.5), 1e-9);
    }

    [Fact]
    public void TheShownShapeIsACopyAndCannotBeEditedByAccident()
    {
        var brush = Brush(b => b.Curves = new() { [BrushDynamic.Size] = ResponseCurve.Linear() });

        PressureResponse.Shape(brush, BrushDynamic.Size).Points.Clear();

        Assert.Equal(2, brush.Curves![BrushDynamic.Size].Points.Count);
    }

    [Fact]
    public void CloningABrushClonesItsCurvesRatherThanSharingThem()
    {
        // Invariant 4 with an extra step: a stroke that shared its brush's
        // curve object would have its mark change the next time the brush was
        // edited.
        var brush = Brush(b => b.Curves = new() { [BrushDynamic.Flow] = ResponseCurve.Linear() });
        var copy = brush.Clone();

        copy.Curves![BrushDynamic.Flow].Points = [new(0, 0), new(1, 0)];

        Assert.Equal(0.5, brush.Curves![BrushDynamic.Flow].Evaluate(0.5), 1e-9);
        Assert.Equal(0, copy.Curves[BrushDynamic.Flow].Evaluate(0.5), 1e-9);
    }
}
