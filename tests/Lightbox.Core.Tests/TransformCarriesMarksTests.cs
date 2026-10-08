using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.Core.Tests;

/// <summary>
/// B406: a transform of whole drawings carries the drawing's anchors and
/// collision boxes with it — the sockets and hurtboxes a sprite export reads.
/// Written before the fix, as the plan for it.
/// </summary>
public class TransformCarriesMarksTests
{
    private static Frame Marked() => new()
    {
        Strokes = [new Stroke { Points = [new StrokePoint(100, 50, 1), new StrokePoint(140, 50, 1)] }],
        Anchors = new() { ["hand"] = new AnchorPoint(100, 50, 0) },
        Shapes = new() { ["hurt"] = new ShapeBox(10, 10, 20, 10) },
    };

    [Fact]
    public void AnAnchorGoesExactlyWhereTheDrawingGoes()
    {
        var frame = Marked();
        TransformOps.TransformFrame(frame, TransformOps.Affine(0, 0, 2, 2, 0, 5, -5));
        var hand = frame.Anchors!["hand"];
        Assert.Equal(205, hand.X, 9);
        Assert.Equal(95, hand.Y, 9);
    }

    [Fact]
    public void AnAnchorsDirectionTurnsWithTheDrawing()
    {
        // A weapon socket pointing right, a quarter turn: it points down
        // (screen y grows downward, so +90° is clockwise on screen).
        var frame = Marked();
        TransformOps.TransformFrame(frame, TransformOps.Affine(0, 0, 1, 1, Math.PI / 2, 0, 0));
        Assert.Equal(90, frame.Anchors!["hand"].AngleDeg!.Value, 6);
    }

    [Fact]
    public void AnAnchorWithNoDirectionStaysWithout()
    {
        var frame = Marked();
        frame.Anchors!["hand"] = new AnchorPoint(100, 50);
        TransformOps.TransformFrame(frame, TransformOps.Affine(0, 0, 1, 1, Math.PI / 2, 0, 0));
        Assert.Null(frame.Anchors["hand"].AngleDeg);
    }

    [Fact]
    public void ACollisionBoxScalesAndMovesWithTheDrawing()
    {
        var frame = Marked();
        TransformOps.TransformFrame(frame, TransformOps.Affine(0, 0, 2, 2, 0, 0, 0));
        Assert.Equal(new ShapeBox(20, 20, 40, 20), frame.Shapes!["hurt"]);
    }

    [Fact]
    public void UnderATurnABoxBecomesTheUprightBoxAroundItsCorners()
    {
        // A box cannot rotate, so it becomes the smallest upright box that
        // holds its turned corners: (10,10)-(30,20) a quarter turn round the
        // origin is (-20,10)-(-10,30).
        var frame = Marked();
        TransformOps.TransformFrame(frame, TransformOps.Affine(0, 0, 1, 1, Math.PI / 2, 0, 0));
        var hurt = frame.Shapes!["hurt"];
        Assert.Equal(-20, hurt.X, 6);
        Assert.Equal(10, hurt.Y, 6);
        Assert.Equal(10, hurt.W, 6);
        Assert.Equal(20, hurt.H, 6);
    }

    [Fact]
    public void AMirroredBoxKeepsAPositiveSize()
    {
        var frame = Marked();
        TransformOps.TransformFrame(frame, TransformOps.Affine(0, 0, -1, 1, 0, 0, 0));
        var hurt = frame.Shapes!["hurt"];
        Assert.Equal(new ShapeBox(-30, 10, 20, 10), hurt);
    }

    [Fact]
    public void ARegionLimitedTransformLeavesThemWhereTheyAre()
    {
        // They belong to the drawing, not to strokes: moving some lines does
        // not say where the drawing's socket went.
        var frame = Marked();
        TransformOps.TransformFrame(frame, TransformOps.Affine(0, 0, 2, 2, 0, 0, 0), filter: _ => true);
        Assert.Equal(new AnchorPoint(100, 50, 0), frame.Anchors!["hand"]);
        Assert.Equal(new ShapeBox(10, 10, 20, 10), frame.Shapes!["hurt"]);
    }

    [Fact]
    public void AMapThatBlowsUpLeavesThemRatherThanWritingNonsense()
    {
        // A perspective near its horizon sends points to infinity; a socket
        // at infinity is worse than one that did not move.
        var frame = Marked();
        TransformOps.TransformFrame(frame, (x, y) => (double.PositiveInfinity, y));
        Assert.Equal(new AnchorPoint(100, 50, 0), frame.Anchors!["hand"]);
        Assert.Equal(new ShapeBox(10, 10, 20, 10), frame.Shapes!["hurt"]);
    }

    [Fact]
    public void ADrawingWithNoneGainsNone()
    {
        // Absent unless used (the optional-settings rule): no empty dictionary
        // appears, so the file writes no key it did not write before.
        var frame = new Frame { Strokes = Marked().Strokes };
        TransformOps.TransformFrame(frame, TransformOps.Affine(0, 0, 2, 2, 0, 0, 0));
        Assert.Null(frame.Anchors);
        Assert.Null(frame.Shapes);
    }

    [Fact]
    public void TheCallerIsToldWhenATurnWidenedABox()
    {
        // So the status line can say so — the box is now an approximation.
        Assert.True(TransformOps.WidensBoxes(TransformOps.Affine(0, 0, 1, 1, 0.3, 0, 0), Marked()));
        Assert.False(TransformOps.WidensBoxes(TransformOps.Affine(0, 0, 2, 3, 0, 10, 0), Marked()));
        Assert.False(TransformOps.WidensBoxes(TransformOps.Affine(0, 0, 1, 1, Math.PI / 2, 0, 0), Marked())); // a quarter turn stays exact
    }
}
