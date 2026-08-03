using Lightbox.Core.Export;

namespace Lightbox.Core.Tests;

/// <summary>
/// The conversion that will bite if it is not tested, with worked examples.
/// </summary>
/// <remarks>
/// A silently flipped pivot looks like an animation problem and gets debugged as
/// one: the character sinks into the floor, or the weapon hangs off the wrong
/// hand, and nothing points at a coordinate convention. Three differences apply at
/// once — origin, direction and scale — so every case here states the arithmetic
/// rather than only the answer.
/// </remarks>
public class UnityConvertTests
{
    [Fact]
    public void TheCentreOfACellIsTheCentreEitherWayUp()
    {
        // The one case a flipped conversion also passes, included so it is on
        // record that it proves nothing on its own.
        var (x, y) = UnityConvert.Pivot(50, 50, cellLeft: 0, cellTop: 0, cellWidth: 100, cellHeight: 100);

        Assert.Equal(0.5, x, 6);
        Assert.Equal(0.5, y, 6);
    }

    [Fact]
    public void TheTopOfTheCellBecomesOneAndTheBottomBecomesZero()
    {
        // Y grows downward in the document and upward in Unity, so the document's
        // top edge is Unity's 1.
        var top = UnityConvert.Pivot(0, 0, 0, 0, 100, 100);
        var bottom = UnityConvert.Pivot(0, 100, 0, 0, 100, 100);

        Assert.Equal(1.0, top.Y, 6);
        Assert.Equal(0.0, bottom.Y, 6);
    }

    [Fact]
    public void AFeetCentrePivotComesOutAtTheBottomMiddle()
    {
        // The case that actually matters: a standing character anchored at their
        // feet. Document (480, 540) on a 960x540 canvas is bottom-centre, and
        // Unity's bottom-centre is (0.5, 0).
        var (x, y) = UnityConvert.Pivot(480, 540, 0, 0, 960, 540);

        Assert.Equal(0.5, x, 6);
        Assert.Equal(0.0, y, 6);
    }

    [Fact]
    public void ItNormalisesWithinTheTrimmedCellRatherThanTheCanvas()
    {
        // The subtle one, and the reason a square test sprite hides bugs. A pivot
        // at document (100, 100) inside a cell that starts at (80, 60) and is
        // 40x80 is 20/40 across and 40/80 down, so (0.5, 0.5) — nothing to do with
        // where the canvas edges are.
        var (x, y) = UnityConvert.Pivot(100, 100, cellLeft: 80, cellTop: 60, cellWidth: 40, cellHeight: 80);

        Assert.Equal(0.5, x, 6);
        Assert.Equal(0.5, y, 6);
    }

    [Fact]
    public void FlippingBeforeNormalisingWouldGiveADifferentAnswerOnANonSquareCell()
    {
        // Written as a discriminating case on purpose: the two orderings agree on a
        // square cell and disagree here, which is exactly why the bug ships.
        var (_, y) = UnityConvert.Pivot(0, 30, cellLeft: 0, cellTop: 0, cellWidth: 100, cellHeight: 60);

        // Correct: 30/60 down the cell, so 0.5 up from the bottom.
        Assert.Equal(0.5, y, 6);
        // The wrong ordering — flip in pixels against the cell *width*, then
        // normalise by height — would give (100-30)/60 = 1.1667.
        Assert.NotEqual(1.1667, y, 3);
    }

    [Fact]
    public void APivotOutsideTheCellIsNotClamped()
    {
        // A character whose ground point sits below their lowest ink has a pivot
        // under the trimmed rect, and Unity accepts that. Clamping it into 0..1
        // would move the character — quietly, and only on the frames where the
        // trim happened to be tight.
        var below = UnityConvert.Pivot(50, 120, cellLeft: 0, cellTop: 0, cellWidth: 100, cellHeight: 100);
        var left = UnityConvert.Pivot(-20, 50, cellLeft: 0, cellTop: 0, cellWidth: 100, cellHeight: 100);

        Assert.Equal(-0.2, below.Y, 6);
        Assert.Equal(-0.2, left.X, 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ADegenerateCellIsTreatedAsOnePixelRatherThanDividingByZero(int size)
    {
        var (x, y) = UnityConvert.Pivot(0, 0, 0, 0, size, size);
        Assert.False(double.IsNaN(x) || double.IsInfinity(x));
        Assert.False(double.IsNaN(y) || double.IsInfinity(y));
    }

    // ---- colliders -------------------------------------------------------------

    [Fact]
    public void ARectangleCentredOnThePivotHasNoOffset()
    {
        // The baseline, and the one case a sign error also passes.
        var (ox, oy, sx, sy) = UnityConvert.Collider(
            left: 40, top: 40, width: 20, height: 20, pivotX: 50, pivotY: 50, pixelsPerUnit: 100);

        Assert.Equal(0, ox, 6);
        Assert.Equal(0, oy, 6);
        Assert.Equal(0.2, sx, 6);
        Assert.Equal(0.2, sy, 6);
    }

    [Fact]
    public void ARectangleBelowThePivotHasANegativeYOffset()
    {
        // Y up, so a box under the character's feet pivot is below the origin. Get
        // this sign wrong and every hurtbox sits above the character's head — which
        // is the failure this method exists to prevent, so it is asserted with the
        // arithmetic written out.
        //
        // Feet pivot at document (100, 200). A box from y=210 to y=250 has its
        // centre at 230, which is 30 px below the pivot, which at 100 ppu is -0.3.
        var (_, oy, _, sy) = UnityConvert.Collider(
            left: 80, top: 210, width: 40, height: 40, pivotX: 100, pivotY: 200, pixelsPerUnit: 100);

        Assert.Equal(-0.3, oy, 6);
        Assert.Equal(0.4, sy, 6);
    }

    [Fact]
    public void XRunsTheSameWayInBothSystemsAndYDoesNot()
    {
        // The asymmetry stated as a test: a box to the *right* of the pivot is a
        // positive X offset, a box *below* it is a negative Y offset, and the two
        // subtractions therefore run opposite ways. A single-axis test would pass on
        // a version that flipped both.
        var (ox, oy, _, _) = UnityConvert.Collider(
            left: 100, top: 100, width: 20, height: 20, pivotX: 50, pivotY: 50, pixelsPerUnit: 100);

        Assert.True(ox > 0, $"right of the pivot should be +X, got {ox}");
        Assert.True(oy < 0, $"below the pivot should be -Y, got {oy}");
        Assert.Equal(0.6, ox, 6);
        Assert.Equal(-0.6, oy, 6);
    }

    [Fact]
    public void PixelsPerUnitScalesBothTheOffsetAndTheSize()
    {
        // At 270 ppu a 54 px box is a fifth of a unit. Scaling the size and
        // forgetting the offset is a plausible half-fix that would put a correctly
        // sized collider in the wrong place.
        var (ox, _, sx, _) = UnityConvert.Collider(
            left: 27, top: 0, width: 54, height: 54, pivotX: 0, pivotY: 0, pixelsPerUnit: 270);

        Assert.Equal(0.2, sx, 6);
        Assert.Equal(54.0 / 270, ox, 6);
    }

    [Fact]
    public void MovingTheRectAndThePivotTogetherChangesNothing()
    {
        // The load-bearing property, stated as the arithmetic that makes it true: the
        // offset is a difference of two document positions, so it is invariant under
        // translating both. That is *why* the method takes no cell rect, and why
        // trimming cannot move a collider by construction rather than by care.
        var here = UnityConvert.Collider(80, 210, 40, 40, 100, 200, 100);
        var moved = UnityConvert.Collider(80 + 137, 210 + 91, 40, 40, 100 + 137, 200 + 91, 100);

        Assert.Equal(here, moved);

        // And the discriminating half: moving only one of them does change it, so
        // the equality above is a real property rather than a method that ignores
        // its arguments.
        Assert.NotEqual(here, UnityConvert.Collider(80 + 137, 210, 40, 40, 100, 200, 100));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void AnImpossiblePixelsPerUnitFallsBackRatherThanDividingByZero(double ppu)
    {
        var (ox, oy, sx, sy) = UnityConvert.Collider(0, 0, 100, 100, 0, 0, ppu);
        Assert.False(double.IsNaN(ox) || double.IsInfinity(ox));
        Assert.False(double.IsNaN(oy) || double.IsInfinity(oy));
        Assert.Equal(1.0, sx, 6);
        Assert.Equal(1.0, sy, 6);
    }

    // ---- timing ----------------------------------------------------------------

    [Fact]
    public void FrameDurationComesFromFpsRatherThanFromRoundedMilliseconds()
    {
        // The sidecar's integer duration at 12 fps is 83 ms; the true figure is
        // 83.33. The claim is about *accumulation*, so that is what this measures —
        // comparing the two per-frame numbers directly is a trap, because at three
        // decimals they agree and the assertion passes while saying nothing.
        Assert.Equal(1.0 / 12, UnityConvert.SecondsPerFrame(12), 10);
        Assert.Equal(1.0 / 24, UnityConvert.SecondsPerFrame(24), 10);

        var exact = 100 * UnityConvert.SecondsPerFrame(12);
        var rounded = 100 * (Math.Round(1000.0 / 12) / 1000.0);
        // 8.3333 s against 8.3 s — a third of a frame adrift over a hundred, which
        // is a visible desync against audio and a compounding one in a long clip.
        Assert.True(
            exact - rounded > 0.03,
            $"exact {exact:F4}s vs rounded {rounded:F4}s — only {exact - rounded:F4}s apart");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AnImpossibleFrameRateDoesNotDivideByZero(int fps)
    {
        Assert.Equal(1.0, UnityConvert.SecondsPerFrame(fps), 10);
    }

    [Fact]
    public void PixelsPerUnitIsDerivedFromTheWorldSizeTheProjectChose()
    {
        // A 540 px character meant to stand two world units tall is 270 ppu. Taken
        // as a number rather than defaulting to Unity's 100, because how many
        // pixels make a unit is a project-wide decision and guessing it leaves
        // somebody wondering why their character is nine units tall.
        Assert.Equal(270, UnityConvert.PixelsPerUnit(540, 2), 6);
        Assert.Equal(100, UnityConvert.PixelsPerUnit(100, 1), 6);
    }

    [Fact]
    public void AnUnsetWorldHeightFallsBackToUnitysOwnDefault()
    {
        Assert.Equal(100, UnityConvert.PixelsPerUnit(540, 0), 6);
    }
}
