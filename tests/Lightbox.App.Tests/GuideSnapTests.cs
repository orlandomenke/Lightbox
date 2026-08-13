using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// A stroke picks its guide once and then holds it, whatever the hand does afterwards.
/// </summary>
/// <remarks>
/// <para>
/// <c>GuideSnap</c> was extracted from <c>MainViewModel</c> under Q78. The geometry it
/// calls — <see cref="Snapper"/> — is already tested; what was never testable on its
/// own is the part with memory, because reaching it meant driving a whole stroke
/// through the view model.
/// </para>
/// <para>
/// <b>The rule is "decide once, then hold", and it exists because a hand wobbles.</b>
/// A stroke that re-chooses its guide part-way kinks visibly at the moment it changes
/// its mind. Every test here is a way of trying to make it change its mind.
/// </para>
/// </remarks>
public class GuideSnapTests(Xunit.ITestOutputHelper output)
{
    private static Guide Line(double angle) =>
        new() { Kind = GuideKind.Line, X = 0, Y = 0, Angle = angle };

    /// <summary>A stroke running along a guide locks to it.</summary>
    [Fact]
    public void AStrokeTravellingAlongAGuideLocksToIt()
    {
        var snap = new GuideSnap();
        var guides = new[] { Line(0) };          // horizontal
        snap.Begin(0, 0);

        // Far enough to have committed to a heading, and roughly along the guide.
        var (x, y) = snap.Apply(guides, 40, 1, tolerance: 12);

        output.WriteLine($"locked to {snap.Locked?.Id ?? "nothing"}, point ({x:0.##}, {y:0.##})");
        Assert.NotNull(snap.Locked);
        Assert.Equal(0, y, 6);                    // pulled onto the horizontal
    }

    /// <summary>
    /// Once locked, a later point heading somewhere else entirely is still held on the
    /// original line — the wobble case this class exists for.
    /// </summary>
    [Fact]
    public void OnceLockedItDoesNotReconsider()
    {
        var snap = new GuideSnap();
        var guides = new[] { Line(0), Line(90) }; // horizontal and vertical
        snap.Begin(0, 0);
        snap.Apply(guides, 40, 1, tolerance: 12);
        var first = snap.Locked;
        Assert.NotNull(first);

        // Now sweep hard downward — if it reconsidered, it would take the vertical.
        var (x, y) = snap.Apply(guides, 5, 90, tolerance: 12);

        output.WriteLine($"still locked to the first guide: {ReferenceEquals(first, snap.Locked)}; point ({x:0.##}, {y:0.##})");
        Assert.Same(first, snap.Locked);
        Assert.Equal(0, y, 6);                    // still projected onto the horizontal
    }

    /// <summary>
    /// A stroke that travels far enough matching nothing gives up asking, so a late
    /// wobble cannot grab it.
    /// </summary>
    [Fact]
    public void AFreehandStrokeStopsLookingForAGuide()
    {
        var snap = new GuideSnap();
        var guides = new[] { Line(0) };
        snap.Begin(0, 0);

        // Straight down the middle of nothing: 45° off the only guide, well past the
        // lock distance, so the decision closes as "freehand".
        snap.Apply(guides, 60, 60, tolerance: 0);
        Assert.Null(snap.Locked);

        // Now run along the guide. A stroke still looking would lock here.
        snap.Apply(guides, 200, 60, tolerance: 0);

        output.WriteLine($"locked after the late run along the guide: {snap.Locked is not null}");
        Assert.Null(snap.Locked);
    }

    /// <summary>
    /// A stroke that arrives with a direction — a shift-click continuing the last one —
    /// is never re-aimed by a guide.
    /// </summary>
    [Fact]
    public void AStrokeThatAlreadyHasADirectionIsNeverReAimed()
    {
        var snap = new GuideSnap();
        var guides = new[] { Line(0) };
        snap.Begin(0, 0);
        snap.HoldDirection(0, 0);

        snap.Apply(guides, 40, 1, tolerance: 0);

        output.WriteLine($"locked: {snap.Locked is not null} (must be false)");
        Assert.Null(snap.Locked);
    }

    /// <summary>Beginning a new stroke forgets the previous stroke's lock.</summary>
    /// <remarks>
    /// The failure this prevents is a whole session of strokes silently held to the
    /// first guide the artist happened to touch.
    /// </remarks>
    [Fact]
    public void BeginningANewStrokeForgetsThePreviousLock()
    {
        var snap = new GuideSnap();
        var guides = new[] { Line(0) };
        snap.Begin(0, 0);
        snap.Apply(guides, 40, 1, tolerance: 12);
        Assert.NotNull(snap.Locked);

        snap.Begin(100, 100);

        Assert.Null(snap.Locked);
    }

    /// <summary>
    /// The anchor is what the heading is measured from, so re-anchoring changes which
    /// guide a stroke commits to.
    /// </summary>
    /// <remarks>
    /// This is the difference between <c>Begin</c> and <c>Anchor</c> being one method or
    /// two. The view model calls <c>Begin</c> with the raw point and then <c>Anchor</c>
    /// with the snapped one, because measuring the heading from a point the hand never
    /// visited aims the stroke wrongly.
    /// </remarks>
    [Fact]
    public void TheAnchorDecidesWhereTheHeadingIsMeasuredFrom()
    {
        var guides = new[] { Line(90) };          // vertical

        var fromOrigin = new GuideSnap();
        fromOrigin.Begin(0, 0);
        fromOrigin.Apply(guides, 0, 40, tolerance: 0);   // straight down from (0,0): locks

        var reAnchored = new GuideSnap();
        reAnchored.Begin(0, 0);
        reAnchored.Anchor(0, 39);                        // heading is now (0,39)->(0,40): 1px
        reAnchored.Apply(guides, 0, 40, tolerance: 0);   // under LockDistance, so undecided

        output.WriteLine($"from origin locked: {fromOrigin.Locked is not null}; re-anchored locked: {reAnchored.Locked is not null}");
        Assert.NotNull(fromOrigin.Locked);
        Assert.Null(reAnchored.Locked);
    }

    /// <summary>A guide with snapping turned off is not a candidate.</summary>
    [Fact]
    public void AGuideThatDoesNotSnapIsIgnored()
    {
        var snap = new GuideSnap();
        var off = Line(0);
        off.Snaps = false;

        snap.Apply([off], 40, 1, tolerance: 12);

        Assert.Null(snap.Locked);
    }
}
