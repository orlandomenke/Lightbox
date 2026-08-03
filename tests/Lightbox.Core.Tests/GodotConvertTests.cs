using Lightbox.Core.Export;

namespace Lightbox.Core.Tests;

/// <summary>
/// The two Godot conversions that fail silently.
/// </summary>
/// <remarks>
/// Neither is arithmetic anybody would call hard. Both are unit mistakes waiting to
/// happen, and a unit mistake in a duration reads as "the importer hung" rather than
/// as a wrong number — which is why they are pure functions with worked examples
/// rather than three lines inside a script we cannot run.
/// </remarks>
public class GodotConvertTests
{
    // ---- durations are multipliers, not times --------------------------------------

    [Fact]
    public void AnOrdinaryFrameIsOneTickOfTheAnimationsSpeed()
    {
        // At 12 fps a tick is 83.33 ms and the sidecar writes 83, so dividing straight
        // through gives 0.996 rather than 1. These two assertions failed on exactly that
        // before the conversion rounded — see the next test, which is the finding.
        Assert.Equal(1.0, GodotConvert.FrameDuration(83, 12), 6);
        Assert.Equal(1.0, GodotConvert.FrameDuration(42, 24), 6);
    }

    [Fact]
    public void RoundingRecoversTheValueTheMillisecondsWereRoundedFrom()
    {
        // A hold is an integer number of frames, so the multiplier is an integer. That
        // makes rounding exact rather than approximate: it undoes the sidecar's own
        // rounding instead of carrying a 0.4% error into every frame.
        Assert.Equal(2.0, GodotConvert.FrameDuration(166, 12), 6);
        Assert.Equal(3.0, GodotConvert.FrameDuration(250, 12), 6);

        // And the error it removes, stated so the size is on record: unrounded, a
        // one-tick frame at 12 fps comes out at 83/83.333.
        var unrounded = 83 / (1000.0 / 12);
        Assert.True(
            Math.Abs(1 - unrounded) > 0.003,
            $"unrounded gives {unrounded:F4}; if that were 1.0 this rounding would be pointless");
    }

    [Fact]
    public void PassingMillisecondsStraightThroughWouldBeWildlyWrong()
    {
        // The discriminating case, stated as the size of the mistake: 83 as a multiplier
        // at 12 fps is nearly seven seconds for one frame. Both numbers printed, because
        // this is the failure that gets reported as "the import hangs".
        var right = GodotConvert.FrameDuration(83, 12);
        const double wrongIfPassedRaw = 83;

        Assert.True(
            wrongIfPassedRaw / right > 50,
            $"correct multiplier {right:F3}, raw milliseconds {wrongIfPassedRaw} — "
            + "the units are not far enough apart for this test to be worth anything");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void AMissingDurationFallsBackToOneRatherThanZero(int ms)
    {
        // A zero-duration frame in Godot is a frame that never advances.
        Assert.Equal(1.0, GodotConvert.FrameDuration(ms, 12), 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AnImpossibleFrameRateDoesNotDivideByZero(int fps)
    {
        var d = GodotConvert.FrameDuration(100, fps);
        Assert.False(double.IsNaN(d) || double.IsInfinity(d));
    }

    // ---- the pivot, which has nothing to flip and still needs a test ---------------

    [Fact]
    public void APivotAtTheCentreNeedsNoOffset()
    {
        var (x, y) = GodotConvert.SpriteOffset(50, 50, 0, 0, 100, 100);

        Assert.Equal(0, x, 6);
        Assert.Equal(0, y, 6);
    }

    [Fact]
    public void AFeetPivotPushesTheDrawingUpward()
    {
        // The case that matters. A 100x100 cell with the pivot on its bottom edge: the
        // drawing has to move up by half its height for the node's origin to sit at the
        // feet, and Godot's 2D Y runs down, so "up" is negative.
        var (x, y) = GodotConvert.SpriteOffset(50, 100, 0, 0, 100, 100);

        Assert.Equal(0, x, 6);
        Assert.Equal(-50, y, 6);
    }

    [Fact]
    public void GodotsYRunsDownLikeOursSoThereIsNoFlipHere()
    {
        // Written as a discriminating test on purpose: this conversion has nothing to
        // flip, which makes it exactly the one somebody "simplifies" into a sign error
        // later. A flipped version would give +50 above and +50 here.
        var below = GodotConvert.SpriteOffset(50, 100, 0, 0, 100, 100);
        var above = GodotConvert.SpriteOffset(50, 0, 0, 0, 100, 100);

        Assert.True(below.Y < 0, $"a pivot at the bottom gave offset y {below.Y}");
        Assert.True(above.Y > 0, $"a pivot at the top gave offset y {above.Y}");
        Assert.Equal(-below.Y, above.Y, 6);
    }

    [Fact]
    public void ItMeasuresFromTheCellsCentreRatherThanTheCanvasCentre()
    {
        // The trimmed-cell subtlety again: the offset is relative to the region Godot
        // will draw, and where the canvas edges are does not enter it.
        var (x, y) = GodotConvert.SpriteOffset(100, 100, cellLeft: 80, cellTop: 60, cellWidth: 40, cellHeight: 80);

        // Cell centre is (100, 100) — the same point as the pivot.
        Assert.Equal(0, x, 6);
        Assert.Equal(0, y, 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void ADegenerateCellIsTreatedAsOnePixel(int size)
    {
        var (x, y) = GodotConvert.SpriteOffset(0, 0, 0, 0, size, size);
        Assert.False(double.IsNaN(x) || double.IsNaN(y));
    }

    // ---- names ---------------------------------------------------------------------

    [Fact]
    public void ABlankTagNameGetsAUsablePlaceholder()
    {
        // Godot animation names are dictionary keys: a blank one collides with the
        // default animation.
        Assert.Equal("animation", GodotConvert.AnimationName(null));
        Assert.Equal("animation", GodotConvert.AnimationName("   "));
        Assert.Equal("run", GodotConvert.AnimationName("  run  "));
    }
}
