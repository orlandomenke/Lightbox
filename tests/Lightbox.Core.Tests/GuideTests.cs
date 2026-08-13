using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests;

/// <summary>
/// Guides: what they pull a point to, what they hold a stroke along, and the
/// difference between those two things.
/// </summary>
/// <remarks>
/// Snapping a point is what a grid does — independent per point, for placing
/// things. Constraining a stroke is what a ruler or a vanishing point does —
/// decided once from a direction the artist committed to, then held. Getting
/// those the wrong way round is what makes a perspective ruler feel like a
/// fight.
/// </remarks>
public class GuideTests
{
    private static Guide Grid(double spacing = 10, double angle = 0, double x = 0, double y = 0) =>
        new() { Kind = GuideKind.Grid, Spacing = spacing, Angle = angle, X = x, Y = y };

    private static Guide Line(double angle, double x = 0, double y = 0) =>
        new() { Kind = GuideKind.Line, Angle = angle, X = x, Y = y };

    private static Guide Vp(double x, double y) =>
        new() { Kind = GuideKind.VanishingPoint, X = x, Y = y };

    private static void Near(double expected, double actual) =>
        Assert.True(Math.Abs(expected - actual) < 0.001, $"expected {expected}, got {actual}");

    // ---- snapping a point ------------------------------------------------------

    [Fact]
    public void AGridPullsToItsIntersections()
    {
        // Intersections rather than lines: an intersection is a place, a line
        // is a direction, and "snap to grid" has always meant the first.
        var (x, y) = Snapper.Point([Grid(10)], 23, 47, tolerance: 8);

        Near(20, x);
        Near(50, y);
    }

    [Fact]
    public void APointOutsideToleranceIsLeftAlone()
    {
        // A guide that grabs from across the canvas is a guide you turn off.
        var (x, y) = Snapper.Point([Grid(100)], 47, 47, tolerance: 8);

        Near(47, x);
        Near(47, y);
    }

    [Fact]
    public void ATiltedGridStillSnaps()
    {
        // The point is rotated into the grid's frame rather than the lattice
        // being rebuilt, which is what lets a tilted grid work at all.
        var grid = Grid(10, angle: 45);

        var (x, y) = Snapper.Point([grid], 7.2, 7.0, tolerance: 6);

        // The nearest lattice point along the 45° axis is 10 units out.
        Near(10 / Math.Sqrt(2), x);
        Near(10 / Math.Sqrt(2), y);
    }

    [Fact]
    public void ALinePullsPerpendicularlyOntoItself()
    {
        var (x, y) = Snapper.Point([Line(0, 0, 100)], 40, 96, tolerance: 8);

        Near(40, x);
        Near(100, y);
    }

    [Fact]
    public void AVanishingPointPullsToItself()
    {
        // Which is what lets a line start exactly on the horizon.
        var (x, y) = Snapper.Point([Vp(300, 200)], 304, 197, tolerance: 8);

        Near(300, x);
        Near(200, y);
    }

    [Fact]
    public void AGuideThatDoesNotSnapIsIgnored()
    {
        var grid = Grid(10);
        grid.Snaps = false;

        var (x, _) = Snapper.Point([grid], 23, 47, tolerance: 8);

        Near(23, x);
    }

    [Fact]
    public void AHiddenGuideStillSnaps()
    {
        // Hiding a rig to look at the drawing under it is something an artist
        // does constantly. Having that silently stop constraining the next line
        // is a very confusing way to lose a stroke.
        var grid = Grid(10);
        grid.Visible = false;

        var (x, _) = Snapper.Point([grid], 23, 47, tolerance: 8);

        Near(20, x);
    }

    [Fact]
    public void TheNearestGuideWinsWhenTwoAreInRange()
    {
        var (x, y) = Snapper.Point([Vp(100, 100), Vp(30, 30)], 34, 33, tolerance: 20);

        Near(30, x);
        Near(30, y);
    }

    // ---- locking a stroke -------------------------------------------------------

    [Fact]
    public void AStrokeDoesNotLockUntilItHasCommittedToADirection()
    {
        // The first samples of any stroke point wherever the pen landed, so
        // locking on them locks on noise.
        var guides = (Guide[])[Line(0)];

        Assert.Null(Snapper.Lock(guides, 0, 0, 2, 0));
        Assert.NotNull(Snapper.Lock(guides, 0, 0, 40, 1));
    }

    [Fact]
    public void AStrokeLocksToTheGuideItIsHeadingAlong()
    {
        var horizontal = Line(0);
        var vertical = Line(90);

        Assert.Same(vertical, Snapper.Lock([horizontal, vertical], 0, 0, 2, 40));
        Assert.Same(horizontal, Snapper.Lock([horizontal, vertical], 0, 0, 40, 2));
    }

    [Fact]
    public void DrawingBackwardsAlongARulerIsStillDrawingAlongIt()
    {
        // A line is undirected. Measuring the full 360 would make half of every
        // stroke miss its own guide.
        var horizontal = Line(0);

        Assert.Same(horizontal, Snapper.Lock([horizontal], 0, 0, -40, 1));
    }

    [Fact]
    public void AStrokeAcrossEveryGuideLocksToNone()
    {
        var guides = (Guide[])[Line(0), Line(90)];

        Assert.Null(Snapper.Lock(guides, 0, 0, 40, 40));
    }

    [Fact]
    public void AnIsometricGuideOffersThreeAxes()
    {
        var iso = new Guide { Kind = GuideKind.Isometric };

        // Two receding axes and the vertical — the convention that makes a cube
        // read as a cube.
        Assert.Equal([30d, -30d, 90d], iso.Angles);
        Assert.Same(iso, Snapper.Lock([iso], 0, 0, 40, 23));
        Assert.Same(iso, Snapper.Lock([iso], 0, 0, 40, -23));
        Assert.Same(iso, Snapper.Lock([iso], 0, 0, 1, 40));
    }

    // ---- holding a stroke on the guide -------------------------------------------

    [Fact]
    public void AConstrainedStrokeKeepsItsLengthAndLosesItsWobble()
    {
        // Projected, not clamped: the ruler decides the direction, the hand
        // decides how far.
        var (x, y) = Snapper.Along(Line(0), 0, 0, 100, 17);

        Near(100, x);
        Near(0, y);
    }

    [Fact]
    public void AVanishingPointsDirectionDependsOnWhereYouAreStanding()
    {
        // Which is exactly what makes perspective work. A VP has no angle of
        // its own.
        var vp = Vp(0, 0);

        Near(0, Snapper.DirectionAt(vp, 100, 0));
        Near(90, Snapper.DirectionAt(vp, 0, 100));
        Near(45, Snapper.DirectionAt(vp, 100, 100));
    }

    [Fact]
    public void AStrokeIsHeldOnTheRayFromTheVanishingPoint()
    {
        // Anchor sits on the 45° ray from the VP at the origin; the stroke
        // wanders off it and is put back.
        var vp = Vp(0, 0);

        var (x, y) = Snapper.Along(vp, 100, 100, 210, 190);

        Near(x, y);                    // still on the 45° ray
        Assert.True(x > 190 && x < 210);
    }

    [Fact]
    public void AnIsometricStrokeIsHeldOnWhicheverAxisItMeant()
    {
        var iso = new Guide { Kind = GuideKind.Isometric };

        // Nearly straight up: held on the vertical axis, x driven to zero.
        var (across, up) = Snapper.Along(iso, 0, 0, 3, 100);
        Near(0, across);
        Assert.True(up > 99);

        // Up and to the right at about 30°: the receding axis, not the vertical.
        var (x, y) = Snapper.Along(iso, 0, 0, 100, 55);
        Assert.True(y > 0 && y < x);
    }

    // ---- the character height scale ------------------------------------------------

    private static Guide Scale(double x = 100, double y = 800, double unit = 90, int heads = 6) =>
        new() { Kind = GuideKind.HeightScale, X = x, Y = y, Spacing = unit, Divisions = heads };

    [Fact]
    public void AHeightScalePullsToItsDivisionLines()
    {
        // "Put the chin on the fifth head" — the pull works anywhere across
        // the canvas, the way a line's does, not only at the chart itself.
        var (x, y) = Snapper.Point([Scale(y: 800, unit: 90)], 400, 616, tolerance: 8);

        Near(400, x);          // x untouched: the division is a height, not a place
        Near(620, y);          // two heads up from the ground at 800
    }

    [Fact]
    public void AboveItsTopAHeightScalePullsNoFurtherThanTheTop()
    {
        // Six heads of 90 from 800 puts the top at 260. Far above it there is
        // no seventh division to mean.
        var (_, y) = Snapper.Point([Scale(y: 800, unit: 90, heads: 6)], 400, 255, tolerance: 8);

        Near(260, y);
    }

    [Fact]
    public void AHeightScaleNeverGrabsAStrokesDirection()
    {
        // It pulls points onto division lines but offers no angles — otherwise
        // every horizontal stroke on the canvas would belong to it.
        var scale = Scale();

        Assert.Empty(scale.Angles);
        Assert.Null(Snapper.Lock([scale], 100, 500, 160, 501));
    }

    [Fact]
    public void OnlyAHeightScaleWritesADivisionsKey()
    {
        // Optional means absent: divisions is the height scale's key and no
        // other guide's, so a document without one never carries it.
        var plain = new Doc();
        plain.Scene.Guides = [Line(0), Grid(24)];
        Assert.DoesNotContain("divisions", DocJson.Serialize(plain), StringComparison.OrdinalIgnoreCase);

        var charted = new Doc();
        charted.Scene.Guides = [Scale(unit: 90, heads: 6)];
        var back = DocJson.Deserialize(DocJson.Serialize(charted));
        Assert.Equal(GuideKind.HeightScale, back.Scene.Guides![0].Kind);
        Assert.Equal(6, back.Scene.Guides[0].Divisions);
        Assert.Equal(90, back.Scene.Guides[0].Spacing);
    }

    // ---- absence -----------------------------------------------------------------

    [Fact]
    public void ADocumentWithNoGuidesWritesNoGuideKey()
    {
        // Optional means absent. The camera's rule, applied again.
        var doc = new Doc();

        Assert.False(doc.Scene.HasGuides);
        Assert.DoesNotContain("guides", DocJson.Serialize(doc), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GuidesSurviveASaveAndReload()
    {
        // Authored, like the camera: a perspective rig set up for a background
        // is part of that background's construction and has to be there
        // tomorrow.
        var doc = new Doc();
        doc.Scene.Guides =
        [
            new Guide { Kind = GuideKind.VanishingPoint, X = 900, Y = 300, Name = "Right" },
            Grid(24, angle: 15),
        ];

        var back = DocJson.Deserialize(DocJson.Serialize(doc));

        Assert.True(back.Scene.HasGuides);
        Assert.Equal("Right", back.Scene.Guides![0].Name);
        Assert.Equal(900, back.Scene.Guides[0].X);
        Assert.Equal(24, back.Scene.Guides[1].Spacing);
        Assert.Equal(15, back.Scene.Guides[1].Angle);
    }

    [Fact]
    public void NoGuidesMeansNoSnapping()
    {
        var (x, y) = Snapper.Point([], 23, 47, tolerance: 8);

        Near(23, x);
        Near(47, y);
    }
}
