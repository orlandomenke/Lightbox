using Lightbox.App.Rendering;

namespace Lightbox.App.Tests;

/// <summary>
/// The rig overlay's decisions: what a press hit, and what a drag produces.
/// </summary>
/// <remarks>
/// Plain unit tests with no window, which is the point of the arithmetic living
/// outside <c>CanvasControl</c>. Every case here would otherwise need synthetic
/// input through Xvfb, where a dropped press looks exactly like a bug.
/// </remarks>
public class RigOverlayTests
{
    private static RigMark Anchor(string id, double x, double y) =>
        new(id, RigMarkKind.Anchor, id, x, y, 0, 0, false);

    private static RigMark Shape(
        string id, double x, double y, double w, double h, bool selected = false) =>
        new(id, RigMarkKind.Shape, id, x, y, w, h, selected);

    // ---- hit order, which is the whole design --------------------------------------

    [Fact]
    public void ASelectedShapesCornerBeatsItsOwnBody()
    {
        // A corner sits on the shape's edge, so body-before-corner makes resizing
        // impossible — the gesture would always move instead.
        var marks = new[] { Shape("body", 10, 10, 40, 40, selected: true) };

        var hit = RigOverlay.Hit(marks, 10, 10, scale: 1);

        Assert.Equal("body", hit.Id);
        Assert.Equal(RigCorner.TopLeft, hit.Corner);
        Assert.True(hit.IsResize);
    }

    [Fact]
    public void AnUnselectedShapeHasNoCornersToGrab()
    {
        // Otherwise every shape under the pointer offers eight targets at once and
        // the one you meant is a coin flip.
        var marks = new[] { Shape("body", 10, 10, 40, 40) };

        var hit = RigOverlay.Hit(marks, 10, 10, scale: 1);

        Assert.Equal("body", hit.Id);
        Assert.Equal(RigCorner.None, hit.Corner);
        Assert.False(hit.IsResize);
    }

    [Fact]
    public void AnAnchorInsideAShapeIsStillReachable()
    {
        // The ordinary arrangement: sockets sit on the character the hurtbox wraps.
        // Shape-before-anchor would make every socket on a rigged character
        // unselectable.
        var marks = new RigMark[]
        {
            Shape("body", 0, 0, 100, 100),
            Anchor("hand", 50, 50),
        };

        Assert.Equal("hand", RigOverlay.Hit(marks, 50, 50, scale: 1).Id);
        // And a press away from the anchor still finds the shape.
        Assert.Equal("body", RigOverlay.Hit(marks, 20, 80, scale: 1).Id);
    }

    [Fact]
    public void TheSmallerShapeWinsWhenOneIsInsideAnother()
    {
        // A hitbox inside a hurtbox is how a character is built. Taking the larger
        // one would make the inner shape impossible to pick up.
        var marks = new[]
        {
            Shape("hurtbox", 0, 0, 100, 100),
            Shape("sword", 40, 40, 20, 10),
        };

        Assert.Equal("sword", RigOverlay.Hit(marks, 45, 45, scale: 1).Id);
        Assert.Equal("hurtbox", RigOverlay.Hit(marks, 5, 5, scale: 1).Id);
    }

    [Fact]
    public void APressOnNothingHitsNothing()
    {
        var marks = new[] { Shape("body", 10, 10, 20, 20) };
        var hit = RigOverlay.Hit(marks, 80, 80, scale: 1);

        Assert.False(hit.Any);
        Assert.Null(hit.Id);
    }

    // ---- targets are screen-sized, not document-sized ------------------------------

    [Fact]
    public void AHandleStaysTheSameSizeToTheHandAtEveryZoom()
    {
        // Document-space handles are unclickable at 25% and cover the drawing at 800%.
        // Measured as: the document distance that still counts scales inversely.
        var marks = new[] { Shape("body", 100, 100, 60, 60, selected: true) };

        // At 400%, five screen pixels is 1.25 document pixels — so a press two
        // document pixels away misses.
        Assert.Equal(RigCorner.None, RigOverlay.Hit(marks, 102, 102, scale: 4).Corner);
        // At 25%, five screen pixels is twenty document pixels — the same press hits.
        Assert.Equal(RigCorner.TopLeft, RigOverlay.Hit(marks, 102, 102, scale: 0.25).Corner);
    }

    [Fact]
    public void AnAnchorHasAMoreForgivingTargetThanAHandle()
    {
        // It is a point with no body, so distance is the only way to grab it.
        Assert.True(RigOverlay.AnchorScreenRadius > RigOverlay.HandleScreenRadius);

        var marks = new[] { Anchor("hand", 50, 50) };
        Assert.Equal("hand", RigOverlay.Hit(marks, 50 + 8, 50, scale: 1).Id);
        Assert.False(RigOverlay.Hit(marks, 50 + 12, 50, scale: 1).Any);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void AnImpossibleZoomDoesNotDivideByZero(double scale)
    {
        var marks = new[] { Shape("body", 10, 10, 20, 20, selected: true) };
        var hit = RigOverlay.Hit(marks, 10, 10, scale);
        Assert.True(hit.Any);
    }

    // ---- dragging -----------------------------------------------------------------

    [Fact]
    public void MovingAShapeNeverResizesIt()
    {
        var moved = RigOverlay.Drag(Shape("s", 10, 20, 30, 40), RigCorner.None, 5, -7);

        Assert.Equal(15, moved.X);
        Assert.Equal(13, moved.Y);
        Assert.Equal(30, moved.W);
        Assert.Equal(40, moved.H);
    }

    [Fact]
    public void ResizingLeavesTheOppositeCornerExactlyWhereItWas()
    {
        // Half the contract, and the half a naive implementation breaks by moving the
        // origin and the size together.
        var shape = Shape("s", 10, 10, 40, 40);
        var resized = RigOverlay.Drag(shape, RigCorner.TopLeft, 6, 8);

        Assert.Equal(16, resized.X);
        Assert.Equal(18, resized.Y);
        // Bottom-right unmoved: 50, 50.
        Assert.Equal(50, resized.Right);
        Assert.Equal(50, resized.Bottom);
    }

    [Fact]
    public void EachCornerMovesOnlyItsOwnTwoEdges()
    {
        var shape = Shape("s", 10, 10, 40, 40);

        var tr = RigOverlay.Drag(shape, RigCorner.TopRight, 5, 5);
        Assert.Equal(10, tr.X);          // left pinned
        Assert.Equal(55, tr.Right);
        Assert.Equal(15, tr.Y);
        Assert.Equal(50, tr.Bottom);     // bottom pinned

        var bl = RigOverlay.Drag(shape, RigCorner.BottomLeft, 5, 5);
        Assert.Equal(15, bl.X);
        Assert.Equal(50, bl.Right);      // right pinned
        Assert.Equal(10, bl.Y);          // top pinned
        Assert.Equal(55, bl.Bottom);
    }

    [Fact]
    public void DraggingACornerPastItsOppositeFlipsRatherThanGoingNegative()
    {
        // A negative width is not a shape an engine can read; refusing the drag at the
        // crossover makes the handle feel broken instead.
        var shape = Shape("s", 10, 10, 20, 20);
        var flipped = RigOverlay.Drag(shape, RigCorner.TopLeft, 50, 50);

        Assert.True(flipped.W > 0, $"width came out {flipped.W}");
        Assert.True(flipped.H > 0, $"height came out {flipped.H}");
        // The far corner is still where it was; the rectangle simply turned over.
        Assert.Equal(30, flipped.X);
        Assert.Equal(30, flipped.Y);
    }

    [Fact]
    public void AShapeCannotBeCollapsedToNothing()
    {
        // Invisible, unhittable, and still exported — the worst of the three.
        var shape = Shape("s", 10, 10, 20, 20);
        var squashed = RigOverlay.Drag(shape, RigCorner.BottomRight, -20, -20);

        Assert.True(squashed.W >= RigOverlay.MinimumShapeSize, $"width {squashed.W}");
        Assert.True(squashed.H >= RigOverlay.MinimumShapeSize, $"height {squashed.H}");
    }

    [Fact]
    public void AnAnchorMovesEvenWhenACornerIsSomehowNamed()
    {
        // An anchor has no corners. If a caller ever passes one, moving is the only
        // sensible reading — and silently resizing a zero-size mark would produce a
        // two-pixel box out of a point.
        var moved = RigOverlay.Drag(Anchor("hand", 40, 40), RigCorner.BottomRight, 5, 5);

        Assert.Equal(45, moved.X);
        Assert.Equal(45, moved.Y);
        Assert.Equal(0, moved.W);
        Assert.Equal(0, moved.H);
    }

    // ---- the cursor ---------------------------------------------------------------

    [Fact]
    public void TheCursorSaysWhatTheGestureWillDo()
    {
        // A resize cursor over something that moves is a small lie an artist notices
        // at once, so the answer comes from the same place the gesture does.
        Assert.Equal("default", RigOverlay.CursorFor(default));
        Assert.Equal("move", RigOverlay.CursorFor(new RigHit("a")));
        Assert.Equal("resize-nwse", RigOverlay.CursorFor(new RigHit("a", RigCorner.TopLeft)));
        Assert.Equal("resize-nwse", RigOverlay.CursorFor(new RigHit("a", RigCorner.BottomRight)));
        Assert.Equal("resize-nesw", RigOverlay.CursorFor(new RigHit("a", RigCorner.TopRight)));
        Assert.Equal("resize-nesw", RigOverlay.CursorFor(new RigHit("a", RigCorner.BottomLeft)));
    }
}
