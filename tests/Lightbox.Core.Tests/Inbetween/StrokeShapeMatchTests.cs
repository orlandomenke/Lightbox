using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests.Inbetween;

/// <summary>
/// Phase 1 of <c>docs/DESIGN-ai-correctness.md</c>: shape in the matching cost,
/// not only position and length.
/// </summary>
/// <remarks>
/// <para>
/// B113 fixed <em>which</em> pairing is chosen — minimum total cost rather than
/// cheapest-first — and left <em>what the cost knows</em> alone. Centroid
/// distance and length ratio cannot see shape at all, and the drawings that
/// exposes are not exotic. Two strokes crossing in an X have the <b>same
/// centroid and the same length</b>, so every entry in the cost matrix is
/// identical and the optimal assignment is a coin toss over a tie. A figure's
/// two arms are the same in the other direction: distinct centroids, until the
/// figure travels far enough that the near arm's new position sits on the far
/// arm's old one, and then the two pairings tie again.
/// </para>
/// <para>
/// A tie is the shape of failure worth naming here, because it is not a
/// near-miss. The matcher is not choosing badly between two costs — it has
/// <em>no information</em>, and which stroke pairs with which falls out of the
/// solver's internal ordering. It will be stable run to run, and stably wrong
/// on half the drawings of that kind.
/// </para>
/// <para>
/// Both cases are the B113 lesson repeated: the smallest drawing that exposes
/// the defect is an ordinary one, and the failure is silent art rather than an
/// exception.
/// </para>
/// <para>
/// <b>Which of these actually fail on the pre-fix build, stated plainly,
/// because a tie does not fail reliably and pretending otherwise would make
/// this file lie.</b> The X and the arms below <em>passed</em> before the shape
/// terms existed — the solver's tie-break happened to land on the right answer.
/// Only <see cref="DrawOrderDoesNotDecideWhichStrokePairsWithWhich"/> fails, and
/// it does so by asserting the same drawing twice with its strokes listed in
/// each order: either order alone passes, and it is the disagreement between
/// them that is the defect. The other three are guards on the new terms rather
/// than proof of the old fault — they would catch a shape cost that overshoots
/// and starts crossing pairs that used to be right.
/// </para>
/// </remarks>
public class StrokeShapeMatchTests(ITestOutputHelper output)
{
    private static Stroke Line(double x1, double y1, double x2, double y2) => new()
    {
        Points = [new(x1, y1, 0.6), new(x2, y2, 0.6)],
        Brush = new BrushSettings { Size = 4 },
    };

    /// <summary>An arc from start to end, bowing <paramref name="bow"/> px sideways at its middle.</summary>
    private static Stroke Arc(double x1, double y1, double x2, double y2, double bow)
    {
        var points = new List<StrokePoint>();
        double dx = x2 - x1, dy = y2 - y1;
        var len = Math.Sqrt(dx * dx + dy * dy);
        double nx = -dy / len, ny = dx / len;   // unit normal
        for (var i = 0; i < 9; i++)
        {
            var t = i / 8.0;
            var k = Math.Sin(t * Math.PI) * bow;  // 0 at the ends, `bow` in the middle
            points.Add(new StrokePoint(x1 + dx * t + nx * k, y1 + dy * t + ny * k, 0.6));
        }
        return new Stroke { Points = points, Brush = new BrushSettings { Size = 4 } };
    }

    private static Stroke Shift(Stroke s, double dx, double dy) => new()
    {
        Points = [.. s.Points.Select(p => new StrokePoint(p.X + dx, p.Y + dy, p.Pressure))],
        Brush = s.Brush,
    };

    private static StrokePoint Start(Stroke s) => s.Points[0];

    private void Report(string what, List<StrokePair> pairs)
    {
        output.WriteLine(what);
        foreach (var p in pairs)
        {
            var a = p.A is null ? "—" : $"({Start(p.A).X:0},{Start(p.A).Y:0})";
            var b = p.B is null ? "—" : $"({Start(p.B).X:0},{Start(p.B).Y:0})";
            output.WriteLine($"  A start {a} -> B start {b}");
        }
    }

    /// <summary>
    /// Two strokes crossing in an X, moved together. Same centroid, same
    /// length, so position and length say nothing whatsoever — only where the
    /// ends are tells the two diagonals apart.
    /// </summary>
    [Fact]
    public void CrossingDiagonalsKeepTheirOwnEnds()
    {
        var down = Line(0, 0, 100, 100);     // ╲
        var up = Line(0, 100, 100, 0);       // ╱
        List<Stroke> a = [down, up];
        List<Stroke> b = [Shift(down, 10, 10), Shift(up, 10, 10)];

        var pairs = StrokeMatcher.Match(a, b);
        Report("X, moved by (10,10):", pairs);

        // Every stroke travels by exactly the shift. Crossed pairing sends a
        // diagonal to the other diagonal's place, which is a 100 px journey
        // through the middle of the drawing.
        Assert.All(pairs, p =>
        {
            Assert.NotNull(p.A);
            Assert.NotNull(p.B);
            Assert.Equal(10, Start(p.B!).X - Start(p.A!).X, precision: 6);
            Assert.Equal(10, Start(p.B!).Y - Start(p.A!).Y, precision: 6);
        });
    }

    /// <summary>
    /// A figure's two arms, bowing opposite ways, as the figure travels far
    /// enough that the near arm lands on the far arm's old position. Centroid
    /// distance ties exactly; the turn is the only thing left that distinguishes
    /// a left arm from a right one.
    /// </summary>
    [Fact]
    public void MirroredArmsDoNotSwapWhenTheFigureTravels()
    {
        var left = Arc(40, 20, 40, 80, -12);    // bows away to the left
        var right = Arc(60, 20, 60, 80, +12);   // bows away to the right
        List<Stroke> a = [left, right];
        // 20 px puts the left arm exactly where the right arm was.
        List<Stroke> b = [Shift(left, 20, 0), Shift(right, 20, 0)];

        var pairs = StrokeMatcher.Match(a, b);
        Report("two arms, figure moved 20 right:", pairs);

        Assert.All(pairs, p =>
        {
            Assert.NotNull(p.A);
            Assert.NotNull(p.B);
            Assert.Equal(20, Start(p.B!).X - Start(p.A!).X, precision: 6);
        });
    }

    /// <summary>
    /// The interpolator already reverses B when its ends are crossed
    /// (<see cref="StrokeInterpolator.Interpolate"/>), so a stroke redrawn in
    /// the opposite direction is a perfectly good match and must not be
    /// penalised into a worse one. This is the constraint that stops the new
    /// endpoint term being read the naive way round: the matcher scores the
    /// better of the two orientations, because the interpolator will pick the
    /// better of the two anyway.
    /// </summary>
    [Fact]
    public void AStrokeRedrawnBackwardsStillMatchesItself()
    {
        var stroke = Arc(20, 20, 20, 90, 15);
        var backwards = new Stroke
        {
            Points = [.. stroke.Points.AsEnumerable().Reverse().Select(p => new StrokePoint(p.X + 5, p.Y, p.Pressure))],
            Brush = stroke.Brush,
        };
        var decoy = Arc(200, 20, 200, 90, 15);

        var pairs = StrokeMatcher.Match([stroke, decoy], [backwards, Shift(decoy, 5, 0)]);
        Report("one stroke redrawn end-to-start:", pairs);

        var matched = Assert.Single(pairs, p => p.A == stroke);
        Assert.Same(backwards, matched.B);
    }

    /// <summary>
    /// The property the other tests are instances of, and the one that makes
    /// this a defect rather than an improvement: <b>the pairing must not depend
    /// on the order the artist drew in.</b>
    /// </summary>
    /// <remarks>
    /// An X rotated about its own centre produced a cost matrix of four zeros —
    /// same centroid, same length, so position and length are silent — and the
    /// assignment then fell out of the solver's internal ordering. Listing B's
    /// two strokes the other way round swapped the pairing, on the same drawing.
    /// Both orders are asserted here because either one alone passes on the
    /// broken build; it is the disagreement between them that is the bug.
    /// </remarks>
    [Fact]
    public void DrawOrderDoesNotDecideWhichStrokePairsWithWhich()
    {
        static Stroke Rotate(Stroke s, double degrees, double cx, double cy)
        {
            var r = degrees * Math.PI / 180;
            double cos = Math.Cos(r), sin = Math.Sin(r);
            return new Stroke
            {
                Points = [.. s.Points.Select(p => new StrokePoint(
                    cx + (p.X - cx) * cos - (p.Y - cy) * sin,
                    cy + (p.X - cx) * sin + (p.Y - cy) * cos,
                    p.Pressure))],
                Brush = s.Brush,
            };
        }

        var down = Line(0, 0, 100, 100);
        var up = Line(0, 100, 100, 0);
        var rotatedDown = Rotate(down, 20, 50, 50);
        var rotatedUp = Rotate(up, 20, 50, 50);

        foreach (var order in new[] { new[] { rotatedDown, rotatedUp }, [rotatedUp, rotatedDown] })
        {
            var pairs = StrokeMatcher.Match([down, up], order);
            Report($"X rotated 20°, B listed {(ReferenceEquals(order[0], rotatedDown) ? "[╲,╱]" : "[╱,╲]")}:", pairs);

            Assert.Same(rotatedDown, Assert.Single(pairs, p => p.A == down).B);
            Assert.Same(rotatedUp, Assert.Single(pairs, p => p.A == up).B);
        }
    }

    /// <summary>
    /// The property B113 pinned, restated against shape: adding terms to the
    /// cost must not cost the drawing that made the cost matter. Kept here as
    /// well as in <see cref="BoxEdgeMatchTests"/> because it is the regression
    /// this change is most likely to cause.
    /// </summary>
    [Fact]
    public void TheMovedBoxStillPairsEdgeToEdge()
    {
        static List<Stroke> Box(double top) =>
        [
            Line(20, top, 80, top),
            Line(80, top, 80, top + 60),
            Line(20, top + 60, 80, top + 60),
            Line(20, top, 20, top + 60),
        ];

        var pairs = StrokeMatcher.Match(Box(20), Box(60));
        Report("box moved down 40:", pairs);

        Assert.All(pairs, p => Assert.Equal(
            40,
            GeometryOps.Centroid(p.B!.Points).Y - GeometryOps.Centroid(p.A!.Points).Y,
            precision: 6));
    }
}
