using Lightbox.Core.Cleanup;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.Core.Tests.Cleanup;

public class LineCleanerTests
{
    private static Stroke Drawn(IEnumerable<(double X, double Y)> points, double size = 3, double pressure = 0.8) => new()
    {
        Brush = new BrushSettings { Size = size },
        Points = [.. points.Select(p => new StrokePoint(p.X, p.Y, pressure))],
    };

    /// <summary>A line from one point to another, one point per pixel, nudged sideways by <paramref name="offset"/>.</summary>
    private static Stroke Along(double x0, double y0, double x1, double y1, Func<double, double>? offset = null, double size = 3)
    {
        var length = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        var n = (int)Math.Ceiling(length);
        var (nx, ny) = (-(y1 - y0) / length, (x1 - x0) / length);
        return Drawn(Enumerable.Range(0, n + 1).Select(i =>
        {
            var t = (double)i / n;
            var o = offset?.Invoke(t * length) ?? 0;
            return (x0 + (x1 - x0) * t + nx * o, y0 + (y1 - y0) * t + ny * o);
        }), size);
    }

    /// <summary>How far the middle of a line strays from y; the ends are held where they were drawn.</summary>
    private static double MaxOffLine(Stroke s, double y) => s.Points.Where(p => p.X is > 15 and < 185).Max(p => Math.Abs(p.Y - y));

    private static double MaxMove(Stroke before, Stroke after) =>
        before.Points.Zip(after.Points).Max(p => GeometryOps.Dist(p.First, p.Second));

    [Fact]
    public void AWobblyLineIsSteadiedAndNoPointMovesFurtherThanTheLineIsWide()
    {
        var wobbly = Along(0, 100, 200, 100, d => 1.5 * Math.Sin(d / 2.0), size: 3);

        var result = LineCleaner.Clean([wobbly], CleanupStrength.Medium);

        var cleaned = Assert.Single(result.Strokes);
        Assert.True(MaxOffLine(cleaned, 100) < 0.5 * MaxOffLine(wobbly, 100),
            $"wobble {MaxOffLine(wobbly, 100):0.00} -> {MaxOffLine(cleaned, 100):0.00}");
        Assert.True(MaxMove(wobbly, cleaned) <= wobbly.Brush.Size + 1e-9);
        Assert.Equal(CleanupKind.Smoothed, Assert.Single(result.Changes).Kind);
    }

    [Fact]
    public void TheDrawingHandedInIsNeverChanged()
    {
        var wobbly = Along(0, 100, 200, 100, d => 1.5 * Math.Sin(d / 2.0));
        var before = wobbly.Points.ToList();

        var result = LineCleaner.Clean([wobbly]);

        Assert.Equal(before, wobbly.Points);
        Assert.NotSame(wobbly, result.Strokes[0]);
        Assert.Equal(wobbly.Id, result.Strokes[0].Id);
    }

    [Fact]
    public void PressureBrushAndColourStayWithTheLine()
    {
        var wobbly = Along(0, 100, 200, 100, d => 1.5 * Math.Sin(d / 2.0));
        wobbly.Points = [.. wobbly.Points.Select((p, i) => p with { Pressure = 0.2 + 0.6 * i / wobbly.Points.Count, TiltX = 12 })];
        wobbly.Color = "#a03020";

        var cleaned = LineCleaner.Clean([wobbly]).Strokes[0];

        Assert.Equal(wobbly.Points.Select(p => p.Pressure), cleaned.Points.Select(p => p.Pressure));
        Assert.All(cleaned.Points, p => Assert.Equal(12, p.TiltX));
        Assert.Equal(wobbly.Color, cleaned.Color);
        Assert.Equal(wobbly.Brush.Size, cleaned.Brush.Size);
        Assert.Equal(wobbly.Points.Count, cleaned.Points.Count);
    }

    [Fact]
    public void AStraightLineStaysExactlyWhereItIs()
    {
        var straight = Along(0, 100, 200, 100);

        var result = LineCleaner.Clean([straight], CleanupStrength.Strong);

        Assert.False(result.Changed, string.Join("; ", result.Changes));
        Assert.Same(straight, result.Strokes[0]);
    }

    [Fact]
    public void ACornerTheArtistDrewStaysSharp()
    {
        var corner = Drawn(
            Enumerable.Range(0, 80).Select(i => (100.0, 20.0 + i))
                .Concat(Enumerable.Range(0, 80).Select(i => (100.0 + i, 100.0))));

        var cleaned = LineCleaner.Clean([corner], CleanupStrength.Strong).Strokes[0];

        var at = cleaned.Points.MinBy(p => GeometryOps.Dist(p, new StrokePoint(100, 100, 0)));
        Assert.True(GeometryOps.Dist(at, new StrokePoint(100, 100, 0)) < 0.5, $"corner at {at.X:0.0},{at.Y:0.0}");
    }

    /// <summary>Two posts that meet the bar cleanly: a drawing whose junctions are mostly clean.</summary>
    private static Stroke[] CleanJunctions() => [Along(30, 40, 30, 100), Along(170, 40, 170, 100)];

    [Fact]
    public void AnEndThatRunsPastTheLineItMeetsIsCutBackToIt()
    {
        var bar = Along(0, 100, 200, 100);
        var post = Along(100, 40, 100, 106); // 6px past the bar

        var result = LineCleaner.Clean([bar, post, .. CleanJunctions()]);

        var trimmed = result.Strokes.Single(s => s.Id == post.Id);
        Assert.InRange(trimmed.Points[^1].Y, 99.5, 100.5);
        Assert.Contains(result.Changes, c => c.StrokeId == post.Id && c.Kind == CleanupKind.Trimmed);
    }

    [Fact]
    public void AnEndThatStopsJustShortOfALineIsCarriedOntoIt()
    {
        var bar = Along(0, 100, 200, 100);
        var post = Along(100, 40, 100, 97.5); // stops 2.5px short, heading straight at it

        var result = LineCleaner.Clean([bar, post, .. CleanJunctions()]);

        var joined = result.Strokes.Single(s => s.Id == post.Id);
        Assert.InRange(joined.Points[^1].Y, 99.5, 100.5);
        Assert.Contains(result.Changes, c => c.StrokeId == post.Id && c.Kind == CleanupKind.Joined);
    }

    [Fact]
    public void ALineEndingBesideAnotherIsNotPulledSideways()
    {
        var bar = Along(0, 100, 200, 100);
        var parallel = Along(0, 104, 150, 104);

        var result = LineCleaner.Clean([bar, parallel]);

        Assert.DoesNotContain(result.Changes, c => c.Kind is CleanupKind.Joined or CleanupKind.Trimmed);
    }

    [Fact]
    public void ADoubledPassIsNeverFusedOrRemoved()
    {
        var pass1 = Along(0, 100, 200, 100, d => 0.6 * Math.Sin(d / 7.0));
        var pass2 = Along(5, 103, 195, 102);

        var result = LineCleaner.Clean([pass1, pass2]);

        Assert.Equal(2, result.Strokes.Count);
        Assert.DoesNotContain(result.Changes, c => c.Kind is CleanupKind.Removed or CleanupKind.Joined);
    }

    [Fact]
    public void ABlipOfInkOnALineIsRemovedButAnIsolatedDotIsKept()
    {
        var line = Along(0, 100, 200, 100);
        var blip = Drawn([(80, 100.5), (81, 101)]);   // on the line's ink
        var pupil = Drawn([(80, 30), (81, 31)]);

        var result = LineCleaner.Clean([line, blip, pupil]);

        Assert.DoesNotContain(result.Strokes, s => s.Id == blip.Id);
        Assert.Contains(result.Strokes, s => s.Id == pupil.Id);
        Assert.Equal(CleanupKind.Removed, result.Changes.Single(c => c.StrokeId == blip.Id).Kind);
    }

    [Fact]
    public void OnTheArtistsOwnDrawingALightShortMarkIsLeftAlone()
    {
        // Hatching, a blush tick, a construction mark: light and short, and theirs.
        var line = Along(0, 100, 200, 100);
        var tick = Along(50, 40, 58, 40);
        tick.Brush.Opacity = 0.2;

        var result = LineCleaner.Clean([line, tick], CleanupStrength.Strong);

        Assert.Contains(result.Strokes, s => s.Id == tick.Id);
    }

    [Fact]
    public void AFaintShortMarkInAnInbetweenIsRemovedAndAFaintLongLineIsNot()
    {
        var keyA = new[] { Along(0, 95, 200, 95), Along(50, 35, 58, 35) };
        var keyB = new[] { Along(0, 105, 200, 105) };
        var line = Along(0, 100, 200, 100);
        var faintMark = Along(50, 40, 58, 40);      // fading out: only key A has it
        faintMark.Brush.Opacity = 0.2;
        var faintLine = Along(0, 60, 200, 60);
        faintLine.Brush.Opacity = 0.2;

        var result = LineCleaner.CleanGuided([line, faintMark, faintLine], [keyA, keyB]);

        Assert.DoesNotContain(result.Strokes, s => s.Id == faintMark.Id);
        Assert.Contains(result.Strokes, s => s.Id == faintLine.Id);
    }

    [Fact]
    public void ADotBesideALineIsKept()
    {
        // A pupil against the iris: close to the ink, not on it.
        var iris = Drawn(Enumerable.Range(0, 126).Select(i => (100 + 20 * Math.Cos(i / 20.0), 100 + 20 * Math.Sin(i / 20.0))));
        var pupil = Drawn([(100, 116), (100.5, 116)]);

        var result = LineCleaner.Clean([iris, pupil], CleanupStrength.Strong);

        Assert.Contains(result.Strokes, s => s.Id == pupil.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADashedLineStaysDashed(bool guided)
    {
        // Ten dashes along one line, 4px apart: a hem, a fold line, a stitch.
        Stroke[] Dashes(double y) => [.. Enumerable.Range(0, 10).Select(k => Along(k * 14, y, k * 14 + 10, y))];
        var drawing = Dashes(100);

        var result = guided ? LineCleaner.CleanGuided(drawing, [Dashes(90), Dashes(110)]) : LineCleaner.Clean(drawing, CleanupStrength.Strong);

        Assert.Equal(10, result.Strokes.Count);
        Assert.DoesNotContain(result.Changes, c => c.Kind == CleanupKind.Joined);
        for (var k = 0; k < 10; k++)
        {
            Assert.InRange(result.Strokes[k].Points[0].X, k * 14 - 0.01, k * 14 + 0.01);
            Assert.InRange(result.Strokes[k].Points[^1].X, k * 14 + 10 - 0.01, k * 14 + 10 + 0.01);
        }
    }

    [Fact]
    public void ATrimmedOvershootKeepsItsTaper()
    {
        var bar = Along(0, 100, 200, 100);
        var post = Along(100, 40, 100, 106);
        post.Points = [.. post.Points.Select((p, i) => p with { Pressure = i < post.Points.Count - 12 ? 0.9 : 0.15 })];

        var result = LineCleaner.Clean([bar, post, .. CleanJunctions()]);

        var trimmed = result.Strokes.Single(s => s.Id == post.Id);
        Assert.InRange(trimmed.Points[^1].Y, 99.5, 100.5);
        Assert.Equal(0.15, trimmed.Points[^1].Pressure, 6);
    }

    [Fact]
    public void ALoneOvershootOnASketchThatOvershootsEverywhereIsKept()
    {
        // The drawing's own habit: every junction runs past, so that is the style.
        var bar = Along(0, 100, 200, 100);
        Stroke[] posts = [Along(30, 40, 30, 106), Along(100, 40, 100, 106), Along(170, 40, 170, 106)];

        var result = LineCleaner.Clean([bar, .. posts]);

        Assert.DoesNotContain(result.Changes, c => c.Kind == CleanupKind.Trimmed);
    }

    [Fact]
    public void ALineCarryingANonNumberIsLeftExactlyAsItIs()
    {
        var broken = Along(0, 100, 200, 100, d => 1.5 * Math.Sin(d / 2.0));
        broken.Points[50] = broken.Points[50] with { X = double.NaN };
        var bad = Along(0, 150, 200, 150, d => 1.5 * Math.Sin(d / 2.0));
        bad.Brush.Size = double.NaN;

        var result = LineCleaner.Clean([broken, bad], CleanupStrength.Strong);

        Assert.Same(broken, result.Strokes[0]);
        Assert.Same(bad, result.Strokes[1]);
        Assert.False(result.Changed);
    }

    [Fact]
    public void TwoStrokesSharingAnIdAreLeftAlone()
    {
        var one = Along(0, 100, 200, 100, d => 1.5 * Math.Sin(d / 2.0));
        var two = Along(0, 150, 200, 150, d => 1.5 * Math.Sin(d / 2.0));
        two.Id = one.Id;

        var result = LineCleaner.Clean([one, two], CleanupStrength.Strong);

        Assert.Same(one, result.Strokes[0]);
        Assert.Same(two, result.Strokes[1]);
    }

    [Fact]
    public void ADrawingTooBigToCleanPromptlyIsRefusedNotCleanedSlowly()
    {
        var many = Enumerable.Range(0, LineCleaner.MaxLines + 1).Select(i => Along(0, i, 10, i)).ToList();

        var result = LineCleaner.Clean(many);

        Assert.True(result.TooLarge);
        Assert.False(result.Changed);
    }

    [Fact]
    public void ErasersFillsAndAuthoredPathsAreLeftExactlyAsTheyAre()
    {
        var eraser = Along(0, 100, 200, 100, d => 1.5 * Math.Sin(d / 2.0));
        eraser.Tool = ToolKind.Eraser;
        var fill = Along(0, 50, 200, 50, d => 1.5 * Math.Sin(d / 2.0));
        fill.Tool = ToolKind.Fill;
        var pen = Along(0, 150, 200, 150, d => 1.5 * Math.Sin(d / 2.0));
        pen.Path = new StrokePath();

        var result = LineCleaner.Clean([eraser, fill, pen], CleanupStrength.Strong);

        Assert.False(result.Changed);
        Assert.Same(eraser, result.Strokes[0]);
        Assert.Same(fill, result.Strokes[1]);
        Assert.Same(pen, result.Strokes[2]);
    }

    [Fact]
    public void TheSameDrawingCleansToTheSameBits()
    {
        Stroke[] Make() =>
        [
            Along(0, 100, 200, 100, d => 1.5 * Math.Sin(d / 2.0)),
            Along(100, 40, 100, 106),
            Drawn([(80, 101), (81, 102)]),
        ];
        var one = LineCleaner.Clean(Make());
        var two = LineCleaner.Clean(Make());

        Assert.Equal(
            one.Strokes.SelectMany(s => s.Points).Select(p => (BitConverter.DoubleToInt64Bits(p.X), BitConverter.DoubleToInt64Bits(p.Y))),
            two.Strokes.SelectMany(s => s.Points).Select(p => (BitConverter.DoubleToInt64Bits(p.X), BitConverter.DoubleToInt64Bits(p.Y))));
        Assert.Equal(one.Changes.Select(c => (c.Kind, c.Moved)), two.Changes.Select(c => (c.Kind, c.Moved)));
    }

    // ------------------------------------------------------------------ guided

    /// <summary>
    /// One straight line drawn in two pieces whose ends meet a few pixels out
    /// of line — the seam an inbetween is rebuilt with.
    /// </summary>
    private static (Stroke Upper, Stroke Lower) Seam(double jog)
    {
        var upper = Along(0, 100, 100, 100);
        var lower = Along(102, 100 + jog, 200, 100);
        // Lower drifts back onto the line over its first 20px.
        lower.Points = [.. lower.Points.Select(p => p with { Y = 100 + jog * Math.Max(0, 1 - (p.X - 102) / 20) })];
        return (upper, lower);
    }

    [Fact]
    public void AJogWhereTwoPiecesOfAnInbetweenMeetIsSmoothedAway()
    {
        var (upper, lower) = Seam(jog: 3);
        var keyA = Along(0, 80, 200, 80);
        var keyB = Along(0, 120, 200, 120);

        var result = LineCleaner.CleanGuided([upper, lower], [[keyA], [keyB]]);

        var u = result.Strokes.Single(s => s.Id == upper.Id);
        var l = result.Strokes.Single(s => s.Id == lower.Id);
        var before = Math.Abs(lower.Points[0].Y - upper.Points[^1].Y);
        var after = Math.Abs(l.Points[0].Y - u.Points[^1].Y);
        Assert.True(after < 0.3 * before, $"seam step {before:0.00} -> {after:0.00}");
        Assert.True(MaxMove(lower, l) <= lower.Brush.Size + 1e-9);
    }

    [Fact]
    public void AStepSidewaysInTheMiddleOfALineIsEasedOutGuided()
    {
        // The knee of the owner's sketch, made small: one line that steps
        // 2.5px sideways over 3px and carries on, between keys that do not.
        var stepped = Along(0, 100, 200, 100, d => d < 100 ? 0 : d < 103 ? 2.5 * (d - 100) / 3 : 2.5);
        var keyA = Along(0, 80, 200, 80, d => 0.3 * Math.Sin(d / 5.0));
        var keyB = Along(0, 120, 200, 120, d => 0.3 * Math.Sin(d / 5.0));

        var cleaned = LineCleaner.CleanGuided([stepped], [[keyA], [keyB]]).Strokes[0];

        // Steepest rise between neighbouring points, before and after.
        static double Steepest(Stroke s) => s.Points.Zip(s.Points.Skip(1)).Max(p => Math.Abs(p.Second.Y - p.First.Y));
        Assert.True(Steepest(cleaned) < 0.5 * Steepest(stepped), $"step {Steepest(stepped):0.00} -> {Steepest(cleaned):0.00} " + string.Join(" ", cleaned.Points.Where(p => p.X is > 85 and < 118).Select(p => $"{p.X:0}:{p.Y - 100:0.00}")));
        Assert.True(MaxMove(stepped, cleaned) <= stepped.Brush.Size + 1e-9);
    }

    [Fact]
    public void AKneeTheKeysBendAtStaysBent()
    {
        StrokePoint P(double x, double y) => new(x, y, 1);
        Stroke Knee(double dx) => Drawn(
            Enumerable.Range(0, 80).Select(i => (100.0 + dx, 20.0 + i))
                .Concat(Enumerable.Range(0, 80).Select(i => (100.0 + dx + i, 100.0))));
        var inbetween = Knee(0);

        var result = LineCleaner.CleanGuided([inbetween], [[Knee(-6)], [Knee(6)]]);

        var cleaned = result.Strokes[0];
        var at = cleaned.Points.MinBy(p => GeometryOps.Dist(p, P(100, 100)));
        Assert.True(GeometryOps.Dist(at, P(100, 100)) < 0.5, $"knee at {at.X:0.0},{at.Y:0.0}");
    }

    [Fact]
    public void AnInbetweenIsNotMadeSmootherThanItsKeys()
    {
        // Keys and inbetween share the same hand wobble: nothing is out of
        // character, so nothing moves.
        Func<double, double> hand = d => 0.8 * Math.Sin(d / 3.0);
        var keyA = Along(0, 80, 200, 80, hand);
        var keyB = Along(0, 120, 200, 120, hand);
        var inbetween = Along(0, 100, 200, 100, hand);

        var result = LineCleaner.CleanGuided([inbetween], [[keyA], [keyB]]);

        Assert.True(MaxMove(inbetween, result.Strokes[0]) < 0.5, $"moved {MaxMove(inbetween, result.Strokes[0]):0.00}");
    }

    [Fact]
    public void AShortMarkTheKeysDoNotHaveIsAnOrphan_OneTheyDoHaveIsKept()
    {
        var keyA = new[] { Along(0, 100, 200, 100), Along(60, 60, 68, 60) };
        var keyB = new[] { Along(0, 110, 200, 110), Along(60, 70, 68, 70) };
        var line = Along(0, 105, 200, 105);
        var tick = Along(60, 65, 68, 65);       // the keys have one here
        var orphan = Along(150, 30, 158, 30);   // the keys have nothing like it

        var result = LineCleaner.CleanGuided([line, tick, orphan], [keyA, keyB]);

        Assert.Contains(result.Strokes, s => s.Id == tick.Id);
        Assert.DoesNotContain(result.Strokes, s => s.Id == orphan.Id);
    }

    [Fact]
    public void WhenTheKeysOvershootTheirJunctionsTheInbetweenKeepsItsOvershoots()
    {
        Stroke[] Key(double y) => [Along(0, y, 200, y), Along(100, y - 60, 100, y + 7), Along(50, y - 60, 50, y + 7)];
        var bar = Along(0, 100, 200, 100);
        var post = Along(100, 40, 100, 106);

        var result = LineCleaner.CleanGuided([bar, post], [Key(90), Key(110)]);

        Assert.DoesNotContain(result.Changes, c => c.Kind == CleanupKind.Trimmed);
    }

    [Fact]
    public void AGuidedCleanOfTheSameDrawingGivesTheSameBits()
    {
        var (u1, l1) = Seam(jog: 3);
        var (u2, l2) = Seam(jog: 3);
        Stroke[] keyA = [Along(0, 80, 200, 80, d => 0.3 * Math.Sin(d / 5.0))];
        Stroke[] keyB = [Along(0, 120, 200, 120, d => 0.3 * Math.Sin(d / 5.0))];

        var one = LineCleaner.CleanGuided([u1, l1], [keyA, keyB]);
        var two = LineCleaner.CleanGuided([u2, l2], [keyA, keyB]);

        Assert.Equal(
            one.Strokes.SelectMany(s => s.Points).Select(p => (BitConverter.DoubleToInt64Bits(p.X), BitConverter.DoubleToInt64Bits(p.Y))),
            two.Strokes.SelectMany(s => s.Points).Select(p => (BitConverter.DoubleToInt64Bits(p.X), BitConverter.DoubleToInt64Bits(p.Y))));
    }

    [Fact]
    public void AGuideMeasuredOnceCleansExactlyAsMeasuringEachTimeDoes()
    {
        var (upper, lower) = Seam(jog: 3);
        Stroke[] keyA = [Along(0, 80, 200, 80)];
        Stroke[] keyB = [Along(0, 120, 200, 120)];

        var guide = LineCleaner.Measure([keyA, keyB])!;
        var once = LineCleaner.CleanGuided([upper, lower], guide);
        var each = LineCleaner.CleanGuided([upper, lower], [keyA, keyB]);

        Assert.Equal(each.Strokes.SelectMany(s => s.Points), once.Strokes.SelectMany(s => s.Points));
    }

    [Fact]
    public void WithNoReferenceAGuidedCleanChangesNothing()
    {
        var wobbly = Along(0, 100, 200, 100, d => 1.5 * Math.Sin(d / 2.0));

        var result = LineCleaner.CleanGuided([wobbly], [[], []]);

        Assert.False(result.Changed);
        Assert.Same(wobbly, result.Strokes[0]);
    }
}
