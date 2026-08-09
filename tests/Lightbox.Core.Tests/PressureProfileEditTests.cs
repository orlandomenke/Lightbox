using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// Editing the weight along a line: the arithmetic under the width tool.
/// </summary>
/// <remarks>
/// <para>
/// <b>The property that carries the weight — literally — is that a width edit is
/// local.</b> An artist points at one place on a line and expects the rest of it
/// to stay as they drew it, so a change that leaked to the ends would be the
/// tool taking their taper away as the price of thickening a middle.
/// </para>
/// <para>
/// Pure, and in Core beside the profile it edits, because a pressure array has
/// nothing to do with a document or a window.
/// </para>
/// </remarks>
public class PressureProfileEditTests(ITestOutputHelper output)
{
    /// <summary>A tapered line: light at the ends, heavy in the middle.</summary>
    private static PressureProfile Tapered(int points = 60)
    {
        var pts = new List<StrokePoint>();
        for (var i = 0; i < points; i++)
        {
            var t = i / (double)(points - 1);
            pts.Add(new StrokePoint(t * 200, 100, 0.15 + 0.85 * Math.Sin(t * Math.PI)));
        }
        return PressureProfile.Of(pts)!;
    }

    [Fact]
    public void AWidthEditIsLocalAndLeavesTheEndsAlone()
    {
        var before = Tapered();
        // Thinning rather than fattening, because the middle of a taper is
        // already at full weight: pushing it up would clamp and the test would
        // be measuring the clamp.
        var after = before.Adjusted(at: 0.5, delta: -0.3, reach: 0.15);

        output.WriteLine(
            $"at 0.50: {before.At(0.5):F3} -> {after.At(0.5):F3};  "
            + $"at 0.05: {before.At(0.05):F3} -> {after.At(0.05):F3}");

        Assert.True(after.At(0.5) < before.At(0.5) - 0.2, "the middle did not get lighter");
        // Outside the reach, untouched — not merely "less changed".
        Assert.Equal(before.At(0.05), after.At(0.05), 9);
        Assert.Equal(before.At(0.95), after.At(0.95), 9);
    }

    /// <summary>
    /// A triangle window is continuous and its slope is not, and a brush whose
    /// taper changes abruptly reads as a nick in the line. The falloff has to be
    /// smooth at the edge of its reach as well as in the middle.
    /// </summary>
    [Fact]
    public void TheEdgeOfTheEditIsSmoothRatherThanAKink()
    {
        var after = Tapered().Adjusted(at: 0.5, delta: 0.3, reach: 0.2);

        // Second difference across the boundary of the reach: a kink shows up
        // here and a smooth join does not.
        double Curvature(double t)
        {
            const double h = 0.005;
            return after.At(t - h) - 2 * after.At(t) + after.At(t + h);
        }

        var atEdge = Math.Abs(Curvature(0.30));
        var inside = Math.Abs(Curvature(0.42));
        output.WriteLine($"curvature at the edge {atEdge:E2}, well inside {inside:E2}");

        Assert.True(atEdge < inside * 3 + 1e-6, $"the edge kinks: {atEdge:E2} against {inside:E2}");
    }

    [Fact]
    public void WeightIsClampedToWhatAStrokeCanCarry()
    {
        var heavy = Tapered().Adjusted(0.5, delta: 5, reach: 0.9);
        var light = Tapered().Adjusted(0.5, delta: -5, reach: 0.9);

        output.WriteLine($"pushed hard up {heavy.At(0.5):F3}, hard down {light.At(0.5):F3}");
        Assert.Equal(1.0, heavy.At(0.5), 9);
        Assert.Equal(0.0, light.At(0.5), 9);
    }

    /// <summary>
    /// <b>A pen line has no pressure to read</b>, so the width tool has to start
    /// one. Flat at full weight is what the line already renders as, which makes
    /// the first drag an edit rather than a jump.
    /// </summary>
    [Fact]
    public void AnAuthoredLineStartsFlatAtFullWeight()
    {
        var flat = PressureProfile.Uniform();
        Assert.Equal(1.0, flat.At(0.0), 9);
        Assert.Equal(1.0, flat.At(0.5), 9);
        Assert.Equal(1.0, flat.At(1.0), 9);
    }

    /// <summary>
    /// A straight authored segment flattens to two points, and a local change
    /// has nowhere to land between two samples. Resampling first is what gives
    /// the edit somewhere to go.
    /// </summary>
    [Fact]
    public void ATwoSampleProfileIsResampledSoAnEditHasSomewhereToLand()
    {
        var sparse = PressureProfile.Of([
            new StrokePoint(0, 0, 1),
            new StrokePoint(100, 0, 1),
        ])!;
        Assert.Equal(2, sparse.SampleCount);

        var edited = sparse.Resampled(PressureProfile.DefaultSamples).Adjusted(0.5, -0.6, 0.15);
        output.WriteLine($"two samples -> {edited.SampleCount};  middle {edited.At(0.5):F3}, end {edited.At(0.02):F3}");

        Assert.Equal(PressureProfile.DefaultSamples, edited.SampleCount);
        Assert.True(edited.At(0.5) < 0.5, "the middle did not thin");
        Assert.Equal(1.0, edited.At(0.02), 3);
    }

    /// <summary>
    /// Resampling reads the same curve rather than a different one, or the
    /// width tool would change the taper the moment it was picked up.
    /// </summary>
    [Fact]
    public void ResamplingKeepsTheCurveItRead()
    {
        var before = Tapered();
        var after = before.Resampled(PressureProfile.DefaultSamples);

        var worst = 0.0;
        for (var i = 0; i <= 100; i++)
        {
            var t = i / 100.0;
            worst = Math.Max(worst, Math.Abs(before.At(t) - after.At(t)));
        }
        output.WriteLine($"worst difference after resampling: {worst:F4}");
        Assert.True(worst < 0.02, $"resampling moved the taper by {worst}");
    }

    /// <summary>
    /// The old profile is what an undo restores, so an edit must not write
    /// through it.
    /// </summary>
    [Fact]
    public void AnEditLeavesTheProfileItCameFromAlone()
    {
        var before = Tapered();
        var was = before.At(0.5);

        before.Adjusted(0.5, 0.4, 0.2);

        Assert.Equal(was, before.At(0.5), 9);
    }

    [Fact]
    public void AnEditOfNothingIsTheSameProfile()
    {
        var before = Tapered();
        Assert.Same(before, before.Adjusted(0.5, delta: 0, reach: 0.2));
        Assert.Same(before, before.Adjusted(0.5, delta: 0.3, reach: 0));
    }
}
