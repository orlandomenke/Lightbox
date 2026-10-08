using Lightbox.Core.Geometry;

namespace Lightbox.Core.Tests;

/// <summary>The arithmetic of a transform that ramps over a sequence (Q216).</summary>
public class TransformRampTests
{
    [Fact]
    public void TheFirstPositionGetsNothingAndTheLastGetsAll()
    {
        var shares = TransformRamp.Shares([0, 6, 12, 24], RampEase.Linear);
        Assert.Equal(0, shares[0]);
        Assert.Equal(0.25, shares[6], 9);
        Assert.Equal(0.5, shares[12], 9);
        Assert.Equal(1, shares[24]);
    }

    [Fact]
    public void ADrawingOn2sTakesTheShareOfTheFrameItStartsOn()
    {
        // Positions are where drawings start; a hold adds no position, so the
        // drawing keeps one share for as long as it shows — it moves on 2s.
        var shares = TransformRamp.Shares([0, 2, 4], RampEase.Linear);
        Assert.Equal(3, shares.Count);
        Assert.Equal(0.5, shares[2], 9);
    }

    [Fact]
    public void ADuplicatedPositionCountsOnce()
    {
        var shares = TransformRamp.Shares([4, 0, 4, 8], RampEase.Linear);
        Assert.Equal(0.5, shares[4], 9);
    }

    [Fact]
    public void ALoneDrawingGetsTheWholeTransform()
    {
        Assert.Equal(1, TransformRamp.Shares([7], RampEase.EaseIn)[7]);
    }

    [Theory]
    [InlineData(RampEase.Linear, 0.5)]
    [InlineData(RampEase.EaseIn, 0.25)]
    [InlineData(RampEase.EaseOut, 0.75)]
    [InlineData(RampEase.EaseInOut, 0.5)]
    public void EachEaseFixesTheEndsAndBendsTheMiddle(RampEase ease, double atHalf)
    {
        Assert.Equal(0, TransformRamp.Ease(ease, 0), 9);
        Assert.Equal(1, TransformRamp.Ease(ease, 1), 9);
        Assert.Equal(atHalf, TransformRamp.Ease(ease, 0.5), 9);
    }

    [Fact]
    public void EaseInOutIsSlowAtBothEnds()
    {
        // Less than linear near the start, more than linear near the end.
        Assert.True(TransformRamp.Ease(RampEase.EaseInOut, 0.1) < 0.1);
        Assert.True(TransformRamp.Ease(RampEase.EaseInOut, 0.9) > 0.9);
    }

    [Fact]
    public void AHalfRotationIsARotationNotASquash()
    {
        // The reason the parts are eased instead of the matrix: blending a
        // quarter turn entry by entry shrinks the drawing at half-way.
        var quarter = new AffineParts(0, 0, 1, 1, Math.PI / 2, 0, 0);
        var (x, y) = quarter.At(0.5).Map()(100, 0);
        Assert.Equal(100, Math.Sqrt((x * x) + (y * y)), 6);   // same distance from the pivot
        Assert.Equal(Math.PI / 4, Math.Atan2(y, x), 6);       // half the angle
    }

    [Fact]
    public void ThePivotStaysAndEverythingElseScalesWithTheShare()
    {
        var box = new AffineParts(50, 50, 3, 2, 0, 40, -20);
        var half = box.At(0.5);
        Assert.Equal((50, 50), (half.PivotX, half.PivotY));
        Assert.Equal(2, half.ScaleX, 9);
        Assert.Equal(1.5, half.ScaleY, 9);
        Assert.Equal((20, -10), (half.OffsetX, half.OffsetY));
        Assert.True(box.At(0).IsIdentity);
        Assert.Equal(box, box.At(1));
    }

    [Fact]
    public void AMirrorIsRecognisedSoItCanBeRefused()
    {
        Assert.True(new AffineParts(0, 0, -1, 1, 0, 0, 0).Mirrors);
        Assert.False(new AffineParts(0, 0, 0.5, 2, 0, 0, 0).Mirrors);
    }
}
