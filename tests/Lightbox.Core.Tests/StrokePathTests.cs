using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Serialization;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// The authored side of a stroke: nodes and handles, the points they flatten
/// to, and the rule that the two can never disagree.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1 of the vector design, and it deliberately has no UI.</b> What is
/// being established here is a record and two functions between it and the
/// points that render — flatten one way, fit the other. Everything the artist
/// eventually touches sits on top of these, so a defect here is a defect in every
/// tool that follows.
/// </para>
/// <para>
/// Three properties carry the weight, and each is asserted rather than argued:
/// <b>absence</b> (a drawn stroke writes no <c>path</c> key, both before and after
/// one is cleared), <b>determinism</b> (the same nodes flatten to the same points,
/// every time, because those points feed <c>Hash01</c> and therefore every dab
/// dynamic), and <b>agreement</b> (any operation that maps points maps the nodes
/// or removes them).
/// </para>
/// </remarks>
public class StrokePathTests(ITestOutputHelper output)
{
    /// <summary>
    /// Distance from a point to the polyline, measured against its
    /// <b>segments</b> rather than its vertices.
    /// </summary>
    /// <remarks>
    /// <b>Written out because the vertex version was tried first and lied.</b>
    /// Nearest-vertex distance measures how far apart the flattened points are,
    /// not how far the line is from where it should be: a perfect fit sampled
    /// every 11 px reports 5.5 px of "error" for a point sitting exactly on the
    /// line, halfway between two samples. The number was real and the attribution
    /// was not — <c>DESIGN-performance.md</c>'s lesson, arriving in a geometry
    /// test.
    /// </remarks>
    private static double DistToPolyline(StrokePoint p, IReadOnlyList<StrokePoint> line)
    {
        var best = double.MaxValue;
        for (var i = 1; i < line.Count; i++)
        {
            best = Math.Min(best, GeometryOps.DistToSegment(p, line[i - 1], line[i]));
        }
        return best;
    }

    private static Stroke Drawn(params (double X, double Y)[] pts) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#000000",
        Points = [.. pts.Select(p => new StrokePoint(p.X, p.Y, 1))],
    };

    // ---- absence -------------------------------------------------------------

    /// <summary>
    /// The half of "optional" that is easy to miss: not inert at its default,
    /// <em>absent</em>. A non-nullable path would put an empty object on every
    /// stroke of every document for a feature nobody switched on.
    /// </summary>
    [Fact]
    public void AHandDrawnStrokeSerializesNoPathKey()
    {
        var doc = DocumentFactory.CreateDoc(100, 100);
        doc.Scene.Layers[^1].Cels[0].Frame!.Strokes.Add(Drawn((10, 10), (20, 20)));

        var json = DocJson.Serialize(doc);

        output.WriteLine($"{json.Length} bytes of document JSON");
        Assert.DoesNotContain("\"path\"", json);
    }

    /// <summary>
    /// And clearing one takes the key away again, so an artist who tries the
    /// tools and undoes gets their file back rather than a permanently fatter one.
    /// </summary>
    [Fact]
    public void ClearingAPathLeavesTheJsonAsItWas()
    {
        var doc = DocumentFactory.CreateDoc(100, 100);
        var stroke = Drawn((10, 10), (40, 30), (70, 10));
        doc.Scene.Layers[^1].Cels[0].Frame!.Strokes.Add(stroke);

        var before = DocJson.Serialize(doc);

        stroke.Path = CurveFitter.Fit(stroke.Points);
        Assert.NotNull(stroke.Path);
        Assert.Contains("\"path\"", DocJson.Serialize(doc));

        stroke.Path = null;
        Assert.Equal(before, DocJson.Serialize(doc));
    }

    [Fact]
    public void APathSurvivesASaveAndLoad()
    {
        var doc = DocumentFactory.CreateDoc(100, 100);
        var stroke = Drawn((10, 10), (40, 30), (70, 10));
        stroke.Path = new StrokePath
        {
            Nodes =
            [
                new PathNode(10, 10, 0.4, 0, 0, 12, 8, Corner: false),
                new PathNode(70, 10, 0.9, -12, 8, 0, 0, Corner: true),
            ],
        };
        doc.Scene.Layers[^1].Cels[0].Frame!.Strokes.Add(stroke);

        var round = DocJson.Deserialize(DocJson.Serialize(doc));
        var back = round.Scene.Layers[^1].Cels[0].Frame!.Strokes[0].Path;

        Assert.NotNull(back);
        Assert.Equal(stroke.Path.Nodes, back!.Nodes);
        Assert.False(back.Closed);
    }

    // ---- flattening ----------------------------------------------------------

    /// <summary>
    /// Nodes with no handles are a polyline, and must stay one: a pen used to
    /// place corners would otherwise write four sampled points per straight
    /// edge, for a shape whose corners are its entire content.
    /// </summary>
    [Fact]
    public void StraightSegmentsFlattenToTheirNodesAndNothingElse()
    {
        var path = new StrokePath
        {
            Nodes = [PathNode.At(0, 0), PathNode.At(50, 0), PathNode.At(50, 50)],
        };

        var points = PathFlattener.Flatten(path);

        Assert.Equal(3, points.Count);
        Assert.Equal(new StrokePoint(0, 0, 1), points[0]);
        Assert.Equal(new StrokePoint(50, 50, 1), points[^1]);
    }

    [Fact]
    public void ACurvedSegmentFlattensThroughItsHandles()
    {
        // A quarter-circle-ish arc from (0,0) to (100,100), bulging right.
        var path = new StrokePath
        {
            Nodes =
            [
                new PathNode(0, 0, 1, 0, 0, 55, 0, Corner: false),
                new PathNode(100, 100, 1, 0, -55, 0, 0, Corner: false),
            ],
        };

        var points = PathFlattener.Flatten(path);
        output.WriteLine($"{points.Count} points for one arc");

        Assert.True(points.Count > 4, "a curve flattened to a straight line");
        // The middle of the arc bulges away from the chord, which runs y = x.
        var mid = points[points.Count / 2];
        Assert.True(mid.X > mid.Y, $"the arc did not bulge: ({mid.X:0.0}, {mid.Y:0.0})");
    }

    /// <summary>
    /// The same nodes flatten to the same points, every time.
    /// </summary>
    /// <remarks>
    /// <b>Invariant 2 reaches this far.</b> Flattened points are what
    /// <c>BrushEngine</c> stamps, and every dab dynamic is seeded from a dab's
    /// position through <c>Hash01</c> — so a flatten that emitted a different
    /// count, or points a hair apart, would re-roll scatter, jitter and rotation
    /// and produce a visibly different mark from an identical record. That is why
    /// the sample count is computed from a curvature bound rather than adapted.
    /// </remarks>
    [Fact]
    public void FlatteningIsDeterministic()
    {
        var path = new StrokePath
        {
            Nodes =
            [
                new PathNode(3.7, 11.2, 0.3, 0, 0, 41.9, -18.4, Corner: false),
                new PathNode(88.1, 46.6, 0.8, -22.5, -30.1, 17.0, 22.7, Corner: false),
                new PathNode(120.4, 130.9, 1.0, -9.9, -44.2, 0, 0, Corner: true),
            ],
        };

        var first = PathFlattener.Flatten(path);
        for (var run = 0; run < 5; run++)
        {
            Assert.Equal(first, PathFlattener.Flatten(path));
        }
        output.WriteLine($"{first.Count} points, identical across 6 flattens");
    }

    [Fact]
    public void PressureIsCarriedAlongTheCurve()
    {
        var path = new StrokePath
        {
            Nodes =
            [
                new PathNode(0, 0, 0.0, 0, 0, 40, 0, Corner: false),
                new PathNode(100, 0, 1.0, -40, 0, 0, 0, Corner: false),
            ],
        };

        var points = PathFlattener.Flatten(path);

        Assert.Equal(0.0, points[0].Pressure, 6);
        Assert.Equal(1.0, points[^1].Pressure, 6);
        // Monotone in between: a stroke that thickened and thinned again on a
        // single segment would be the interpolation, not the artist.
        for (var i = 1; i < points.Count; i++)
        {
            Assert.True(points[i].Pressure >= points[i - 1].Pressure - 1e-9);
        }
    }

    /// <summary>
    /// Joins are emitted once. A doubled point is not cosmetic downstream — it is
    /// a zero-length step for the densifier and a repeated dab for the engine,
    /// which reads as a dark spot at every node.
    /// </summary>
    [Fact]
    public void NodesAreNotDoubledAtTheJoins()
    {
        var path = new StrokePath
        {
            Nodes = [PathNode.At(0, 0), PathNode.At(10, 0), PathNode.At(20, 0)],
        };

        var points = PathFlattener.Flatten(path);

        Assert.Equal(3, points.Count);
        Assert.Equal(3, points.Distinct().Count());
    }

    /// <summary>
    /// The renderer stamps dabs along the flattened polyline and knows nothing
    /// about <c>Closed</c> — so a closed path must end back on its first point,
    /// or the closing segment exists in the record and never on screen. A
    /// "closed" triangle used to render as an open corner for exactly this.
    /// </summary>
    [Fact]
    public void AClosedPathEndsWhereItBegan()
    {
        var path = new StrokePath
        {
            Closed = true,
            Nodes = [PathNode.At(0, 0), PathNode.At(30, 0), PathNode.At(30, 30)],
        };

        var points = PathFlattener.Flatten(path);

        Assert.Equal(4, points.Count);
        Assert.Equal(points[0], points[^1]);
        // The join is a return, not a doubled node: every interior point is
        // still distinct.
        Assert.Equal(4, points.Distinct().Count() + 1);
    }

    // ---- fitting -------------------------------------------------------------

    /// <summary>
    /// Q50: every line already drawn becomes editable, on demand. The fit has to
    /// produce something a hand can work with, which means far fewer nodes than
    /// there were points.
    /// </summary>
    [Fact]
    public void FittingADrawnArcProducesAFewNodesRatherThanMany()
    {
        var drawn = new List<StrokePoint>();
        for (var i = 0; i <= 120; i++)
        {
            var t = i / 120.0 * Math.PI;
            drawn.Add(new StrokePoint(100 + 80 * Math.Cos(t), 100 + 80 * Math.Sin(t), 1));
        }

        var path = CurveFitter.Fit(drawn);

        Assert.NotNull(path);
        output.WriteLine($"{drawn.Count} drawn points -> {path!.Nodes.Count} nodes");
        Assert.True(path.Nodes.Count >= 2);
        Assert.True(
            path.Nodes.Count < drawn.Count / 8,
            $"the fit kept {path.Nodes.Count} nodes for {drawn.Count} points, which is not editable");
    }

    /// <summary>
    /// And the line it describes is the line that was drawn — the whole point.
    /// </summary>
    [Fact]
    public void AFittedPathFlattensBackOntoTheDrawnLine()
    {
        var drawn = new List<StrokePoint>();
        for (var i = 0; i <= 120; i++)
        {
            var t = i / 120.0 * Math.PI;
            drawn.Add(new StrokePoint(100 + 80 * Math.Cos(t), 100 + 80 * Math.Sin(t), 1));
        }

        var path = CurveFitter.Fit(drawn)!;
        var back = PathFlattener.Flatten(path);

        var worst = drawn.Max(p => DistToPolyline(p, back));

        output.WriteLine(
            $"{path.Nodes.Count} nodes, {back.Count} flattened points, worst deviation "
            + $"{worst:0.000} px against a {CurveFitter.Tolerance} px tolerance");
        // Measured against the flatten rather than against the fit's own error,
        // because the flatten is what actually renders — a fit that was accurate
        // and flattened badly would look identical to a bad fit.
        Assert.True(worst < CurveFitter.Tolerance * 2, $"the fitted line drifted {worst:0.00} px");
    }

    [Fact]
    public void FittingIsDeterministic()
    {
        var drawn = new List<StrokePoint>();
        for (var i = 0; i <= 90; i++)
        {
            drawn.Add(new StrokePoint(i * 1.7, 40 * Math.Sin(i / 9.0) + i * 0.3, 0.5 + i / 200.0));
        }

        var first = CurveFitter.Fit(drawn)!;
        for (var run = 0; run < 5; run++)
        {
            var again = CurveFitter.Fit(drawn)!;
            Assert.Equal(first.Nodes, again.Nodes);
        }
        output.WriteLine($"{first.Nodes.Count} nodes, identical across 6 fits");
    }

    [Fact]
    public void AStraightLineFitsToTwoNodes()
    {
        var drawn = Enumerable.Range(0, 50).Select(i => new StrokePoint(i * 2, i * 2, 1)).ToList();

        var path = CurveFitter.Fit(drawn)!;

        Assert.Equal(2, path.Nodes.Count);
    }

    /// <summary>
    /// A pen resting produces repeated samples, and a chord-length
    /// parameterisation divides by the chord — so this is a divide by zero
    /// waiting in ordinary input rather than a hostile case.
    /// </summary>
    [Fact]
    public void RepeatedPointsDoNotBreakTheFit()
    {
        var drawn = new List<StrokePoint>
        {
            new(10, 10, 1), new(10, 10, 1), new(10, 10, 1),
            new(40, 30, 1), new(40, 30, 1),
            new(70, 10, 1),
        };

        var path = CurveFitter.Fit(drawn);

        Assert.NotNull(path);
        Assert.All(path!.Nodes, n => Assert.True(double.IsFinite(n.X) && double.IsFinite(n.Y)));
    }

    [Fact]
    public void ThereIsNothingToFitInFewerThanTwoPoints()
    {
        Assert.Null(CurveFitter.Fit([]));
        Assert.Null(CurveFitter.Fit([new StrokePoint(1, 1, 1)]));
        Assert.Null(CurveFitter.Fit([new StrokePoint(1, 1, 1), new StrokePoint(1, 1, 1)]));
    }

    // ---- agreement -----------------------------------------------------------

    /// <summary>
    /// <b>The invariant the record creates, asserted rather than commented.</b>
    /// A stroke's path and its points must never disagree — so transforming one
    /// transforms the other, and flattening the transformed path has to reproduce
    /// the transformed points.
    /// </summary>
    /// <remarks>
    /// The failure this catches is quiet, which is why it is the test the design
    /// names first. Nothing renders differently from a stale path: the points are
    /// what draw. It only surfaces later, when the artist drags a node and the
    /// line jumps back to where it used to be — at which point the cause is three
    /// operations ago.
    /// </remarks>
    [Fact]
    public void TransformingAStrokeTransformsItsPathToMatch()
    {
        var stroke = Drawn((0, 0), (50, 40), (100, 0));
        stroke.Path = CurveFitter.Fit(stroke.Points);
        Assert.NotNull(stroke.Path);

        TransformOps.TransformStroke(stroke, (x, y) => (x * 1.5 + 20, y * 1.5 - 10));

        var reflattened = PathFlattener.Flatten(stroke.Path!);
        var worst = stroke.Points.Max(p => DistToPolyline(p, reflattened));

        output.WriteLine($"path and points differ by at most {worst:0.000} px after a scale and translate");
        Assert.True(worst < CurveFitter.Tolerance * 2, $"path and points disagree by {worst:0.00} px");
    }

    /// <summary>
    /// Handles are offsets, so they take the transform's linear part and not its
    /// translation. Getting this wrong is invisible under a pure move and doubles
    /// every handle under a scale.
    /// </summary>
    [Fact]
    public void APureTranslationLeavesTheHandlesAlone()
    {
        var stroke = Drawn((0, 0), (50, 40), (100, 0));
        stroke.Path = new StrokePath
        {
            Nodes =
            [
                new PathNode(0, 0, 1, 0, 0, 30, 10, Corner: false),
                new PathNode(100, 0, 1, -30, 10, 0, 0, Corner: false),
            ],
        };

        TransformOps.TransformStroke(stroke, (x, y) => (x + 200, y + 300));

        var moved = stroke.Path!.Nodes;
        Assert.Equal(200, moved[0].X, 6);
        Assert.Equal(300, moved[0].Y, 6);
        Assert.Equal(30, moved[0].OutX, 6);
        Assert.Equal(10, moved[0].OutY, 6);
    }

    [Fact]
    public void ScalingScalesTheHandlesWithTheNodes()
    {
        var stroke = Drawn((0, 0), (100, 0));
        stroke.Path = new StrokePath
        {
            Nodes =
            [
                new PathNode(0, 0, 1, 0, 0, 30, 0, Corner: false),
                new PathNode(100, 0, 1, -30, 0, 0, 0, Corner: false),
            ],
        };

        TransformOps.TransformStroke(stroke, (x, y) => (x * 2, y * 2));

        Assert.Equal(60, stroke.Path!.Nodes[0].OutX, 6);
        Assert.Equal(200, stroke.Path.Nodes[1].X, 6);
    }

    /// <summary>
    /// Dropping is always allowed and is the safe answer: the points are the
    /// truth, so a stroke that loses its path still draws exactly the same line.
    /// What is forbidden is keeping one that has stopped describing it.
    /// </summary>
    [Fact]
    public void ADegenerateTransformDropsThePathRatherThanCorruptingIt()
    {
        var stroke = Drawn((0, 0), (50, 40), (100, 0));
        stroke.Path = CurveFitter.Fit(stroke.Points);
        Assert.NotNull(stroke.Path);

        TransformOps.TransformStroke(stroke, (_, _) => (double.NaN, double.NaN));

        Assert.Null(stroke.Path);
        Assert.NotEmpty(stroke.Points);
    }

    /// <summary>
    /// An inbetween carries no path, and that is a decision. It is resampled to a
    /// fixed count and lerped point by point, so there are no authored nodes to
    /// carry and no correspondence between the two ends' nodes that would say
    /// where they went.
    /// </summary>
    [Fact]
    public void AnInbetweenHasNoPathToDisagreeWith()
    {
        var a = Drawn((0, 0), (50, 40), (100, 0));
        var b = Drawn((0, 20), (50, 80), (100, 20));
        a.Path = CurveFitter.Fit(a.Points);
        b.Path = CurveFitter.Fit(b.Points);

        var tween = StrokeInterpolator.Interpolate(a, b, 0.5);

        Assert.Null(tween.Path);
        Assert.NotEmpty(tween.Points);
    }

    /// <summary>
    /// Cloning is deep. A shared path would let reshaping one copy of a
    /// duplicated cel silently reshape the other — and the other's points would
    /// not move with it, so both would end up disagreeing with themselves.
    /// </summary>
    [Fact]
    public void CloningAStrokeCopiesItsPathRatherThanSharingIt()
    {
        var stroke = Drawn((0, 0), (50, 40), (100, 0));
        stroke.Path = CurveFitter.Fit(stroke.Points);

        var copy = stroke.Clone();
        copy.Path!.Nodes[0] = copy.Path.Nodes[0] with { X = -999 };

        Assert.NotEqual(-999, stroke.Path!.Nodes[0].X);
    }

    // ---- shape ---------------------------------------------------------------

    [Fact]
    public void APathOfOneNodeCannotDescribeALine()
    {
        var path = new StrokePath { Nodes = [PathNode.At(5, 5)] };

        Assert.False(path.IsUsable);
        Assert.Single(PathFlattener.Flatten(path));
    }

    [Fact]
    public void APlainPenClickPlacesACornerWithNoHandles()
    {
        var node = PathNode.At(12, 34, 0.7);

        Assert.True(node.Corner);
        Assert.False(node.HasHandles);
        Assert.Equal(0.7, node.Pressure);
    }
}
