using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// Pulling the curve itself: finding it, and moving it to where it was pulled.
/// </summary>
/// <remarks>
/// <para>
/// <b>The property that matters is the one an artist can see: the curve ends up
/// under the pointer, and nothing else moves.</b> Both halves are asserted
/// rather than one — a solve that lands the curve correctly by dragging a node
/// with it would pass a test that only checked the first, and would be a
/// different tool.
/// </para>
/// <para>
/// Pure arithmetic, so it is tested here rather than through the session. The
/// interesting decision — that there is a whole family of handle adjustments
/// putting the curve through a point, and which one you pick is what makes the
/// gesture feel local or lurching — has nothing to do with a document.
/// </para>
/// </remarks>
public class SegmentDragTests(ITestOutputHelper output)
{
    /// <summary>An ordinary curved segment: two smooth nodes, handles out sideways.</summary>
    private static StrokePath Arc() => new()
    {
        Nodes =
        [
            new PathNode(100, 200, 1, 0, 0, 40, -40, Corner: false),
            new PathNode(300, 200, 1, -40, -40, 0, 0, Corner: false),
        ],
    };

    private static StrokePath Straight() => new()
    {
        Nodes = [PathNode.At(100, 200), PathNode.At(300, 200)],
    };

    // ---- finding it ------------------------------------------------------------

    [Fact]
    public void APointOnTheCurveLandsOnItsSegment()
    {
        var path = Arc();
        var (x, y) = PathFlattener.PointOn(path.Nodes[0], path.Nodes[1], 0.4);

        var landing = SegmentDrag.Nearest(path, x, y, tolerance: 6);
        output.WriteLine($"grabbed ({x:F2}, {y:F2}) -> segment {landing.Segment}, t {landing.T:F3}, {landing.Distance:F4} px away");

        Assert.True(landing.IsHit);
        Assert.Equal(0, landing.Segment);
        Assert.Equal(0.4, landing.T, 2);
        Assert.True(landing.Distance < 0.5, $"the search landed {landing.Distance} px off the curve");
    }

    [Fact]
    public void APointNowhereNearTheCurveIsAMiss()
    {
        var landing = SegmentDrag.Nearest(Arc(), 100, 20, tolerance: 6);
        Assert.False(landing.IsHit);
    }

    /// <summary>
    /// A closed path has one more segment than it has gaps between nodes, and
    /// the extra one is the join nobody thinks about until it cannot be grabbed.
    /// </summary>
    [Fact]
    public void TheClosingSegmentCanBeFoundToo()
    {
        var path = new StrokePath
        {
            Closed = true,
            Nodes = [PathNode.At(100, 100), PathNode.At(300, 100), PathNode.At(300, 300)],
        };

        var landing = SegmentDrag.Nearest(path, 200, 200, tolerance: 8);
        output.WriteLine($"segment {landing.Segment}, t {landing.T:F3}");

        Assert.True(landing.IsHit);
        Assert.Equal(2, landing.Segment);
    }

    // ---- moving it -------------------------------------------------------------

    /// <summary>
    /// <b>The whole promise of the gesture</b>: the line goes where you pulled
    /// it, and its ends stay where they were put.
    /// </summary>
    [Fact]
    public void TheCurveEndsUpWhereItWasPulledAndTheNodesDoNotMove()
    {
        var path = Arc();
        var (a0, b0) = (path.Nodes[0], path.Nodes[1]);
        const double t = 0.5;
        var (beforeX, beforeY) = PathFlattener.PointOn(a0, b0, t);

        var (a, b) = SegmentDrag.Pinch(a0, b0, t, dx: 0, dy: 60);
        var (afterX, afterY) = PathFlattener.PointOn(a, b, t);

        output.WriteLine($"curve at t={t}: ({beforeX:F2}, {beforeY:F2}) -> ({afterX:F2}, {afterY:F2})");

        Assert.Equal(beforeX, afterX, 6);
        Assert.Equal(beforeY + 60, afterY, 6);

        // The nodes themselves are untouched — this is not a move.
        Assert.Equal(a0.X, a.X, 9);
        Assert.Equal(a0.Y, a.Y, 9);
        Assert.Equal(b0.X, b.X, 9);
        Assert.Equal(b0.Y, b.Y, 9);
    }

    /// <summary>
    /// Grab near one end and mostly that end's handle moves. This is what makes
    /// the gesture feel like pulling a wire rather than bending a whole span,
    /// and it is the reason the least-squares member of the solution family was
    /// chosen over the cheaper "give it all to the nearer control point".
    /// </summary>
    [Fact]
    public void TheWorkIsSplitInProportionToWhereTheCurveWasGrabbed()
    {
        var path = Arc();
        var (a, b) = SegmentDrag.Pinch(path.Nodes[0], path.Nodes[1], t: 0.25, dx: 0, dy: 40);

        var movedA = Math.Abs(a.OutY - path.Nodes[0].OutY);
        var movedB = Math.Abs(b.InY - path.Nodes[1].InY);
        output.WriteLine($"grabbed at t=0.25: near handle moved {movedA:F2}, far handle moved {movedB:F2}");

        Assert.True(movedA > movedB, $"the near handle moved {movedA} and the far one {movedB}");
    }

    /// <summary>
    /// The displacement is linear in the handles, so dragging in steps has to
    /// land where dragging in one go does — which is what makes it safe for the
    /// view model to feed this one pointer delta at a time.
    /// </summary>
    [Fact]
    public void ManySmallPullsLandWhereOneBigOneWould()
    {
        var path = Arc();
        var (oneA, oneB) = SegmentDrag.Pinch(path.Nodes[0], path.Nodes[1], 0.5, 0, 60);

        var (a, b) = (path.Nodes[0], path.Nodes[1]);
        for (var i = 0; i < 60; i++) (a, b) = SegmentDrag.Pinch(a, b, 0.5, 0, 1);

        var (bigX, bigY) = PathFlattener.PointOn(oneA, oneB, 0.5);
        var (stepX, stepY) = PathFlattener.PointOn(a, b, 0.5);
        output.WriteLine($"one pull ({bigX:F4}, {bigY:F4}), sixty pulls ({stepX:F4}, {stepY:F4})");

        Assert.Equal(bigX, stepX, 6);
        Assert.Equal(bigY, stepY, 6);
    }

    /// <summary>
    /// A straight run between two corners has no handles at all, and pulling it
    /// has to produce a curve rather than doing nothing — turning a straight
    /// line into a bend by dragging it is most of what this is for.
    /// </summary>
    [Fact]
    public void AStraightSegmentBendsRatherThanRefusing()
    {
        var path = Straight();
        var (a, b) = SegmentDrag.Pinch(path.Nodes[0], path.Nodes[1], 0.5, 0, 50);

        Assert.True(a.HasHandles);
        Assert.True(b.HasHandles);

        var (x, y) = PathFlattener.PointOn(a, b, 0.5);
        output.WriteLine($"straight segment pulled to ({x:F2}, {y:F2})");
        Assert.Equal(250, y, 6);

        // Corners stay corners: pulling the line beside a sharp point must not
        // quietly round it off.
        Assert.True(a.Corner);
        Assert.True(b.Corner);
    }

    /// <summary>
    /// Smooth means the curve does not kink at that node, and it has to keep
    /// meaning that through a pinch — otherwise a node stays smooth only while
    /// you drag its own arms.
    /// </summary>
    [Fact]
    public void ASmoothEndKeepsItsFarHandleInLine()
    {
        var node = new PathNode(100, 200, 1, -30, 0, 30, 0, Corner: false);
        var far = new PathNode(300, 200, 1, -40, 0, 0, 0, Corner: true);
        var beforeLength = Math.Sqrt(node.InX * node.InX + node.InY * node.InY);

        var (a, _) = SegmentDrag.Pinch(node, far, 0.5, 0, 40);

        // Opposite directions: the dot product of the two unit handles is -1.
        var outLength = Math.Sqrt(a.OutX * a.OutX + a.OutY * a.OutY);
        var inLength = Math.Sqrt(a.InX * a.InX + a.InY * a.InY);
        var dot = (a.InX * a.OutX + a.InY * a.OutY) / (inLength * outLength);
        output.WriteLine($"in ({a.InX:F2}, {a.InY:F2}) out ({a.OutX:F2}, {a.OutY:F2}), dot {dot:F4}");

        Assert.Equal(-1, dot, 6);
        // And it keeps its own length: mirroring it would grow the neighbouring
        // segment to match the one being pulled.
        Assert.Equal(beforeLength, inLength, 6);
    }

    /// <summary>
    /// The solve divides by weights that both vanish at the ends, so an
    /// unguarded pinch at <c>t = 0</c> flings the control points off the canvas.
    /// Clamping rather than refusing is right because the correct gesture that
    /// near a node is to drag the node, which the hit test already gives.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-5.0)]
    [InlineData(9.0)]
    public void PullingAtOrPastAnEndStaysFinite(double t)
    {
        var path = Arc();
        var (a, b) = SegmentDrag.Pinch(path.Nodes[0], path.Nodes[1], t, 0, 40);

        foreach (var v in new[] { a.OutX, a.OutY, b.InX, b.InY })
        {
            Assert.True(double.IsFinite(v), $"a pinch at t={t} produced {v}");
        }
        // And it stays a sane shape rather than merely a finite one.
        var (x, y) = PathFlattener.PointOn(a, b, 0.5);
        output.WriteLine($"t={t} -> midpoint ({x:F2}, {y:F2})");
        Assert.InRange(y, 100, 400);
    }
}
