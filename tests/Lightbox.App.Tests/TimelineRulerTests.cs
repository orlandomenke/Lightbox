using Lightbox.App.Controls;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The timeline ruler's numbers, at every zoom the frame-width slider allows.
/// </summary>
/// <remarks>
/// Reported as "the timeline's scrubbing bar only shows the frame number when
/// the scrubbar is on it". It numbered every twelfth frame at any width — which
/// reads well at the narrowest setting and puts one number on the whole ruler at
/// the widest, where twelve frames is over eight hundred pixels. The playhead
/// carries its own number, so what was left really was "the frame you are
/// standing on and nothing else".
/// <para>
/// Pure arithmetic, tested without a window, which is why <c>LabelStep</c> is a
/// static beside the other geometry — the file's own note says they are static
/// so the tests can hold them still.
/// </para>
/// </remarks>
public class TimelineRulerTests(ITestOutputHelper output)
{
    /// <summary>The ends of the slider's range, from TimelineFrameWidth's clamp.</summary>
    private const double Narrowest = 14;
    private const double Widest = 72;

    [Fact]
    public void NumbersStayAtLeastAsCloseAsTheOldFixedTwelve()
    {
        for (var width = Narrowest; width <= Widest; width += 1)
        {
            var gap = TrackView.LabelStep(width) * width;
            output.WriteLine($"width {width}: step {TrackView.LabelStep(width)} -> {gap:F0}px");
            // Legible, and never the 864px the fixed twelve gave at the top of
            // the range.
            Assert.InRange(gap, 40, 160);
        }
    }

    [Fact]
    public void TheWidestZoomNumbersEveryFrame()
    {
        // Where the old ruler was emptiest is where there is most room.
        Assert.Equal(1, TrackView.LabelStep(Widest));
    }

    [Fact]
    public void TheNarrowestZoomStillNumbersOftenEnoughToCountFrom()
    {
        var step = TrackView.LabelStep(Narrowest);
        output.WriteLine($"narrowest step {step}");
        Assert.True(step <= 3, $"step {step} at the narrowest width");
    }

    [Fact]
    public void EveryStepKeepsTheSecondBoundariesNumbered()
    {
        // Frame 1, 13, 25 — the cadence an animator counts against — must fall
        // on a numbered frame whatever the zoom, which is true exactly when the
        // step divides twelve or is a multiple of it.
        for (var width = Narrowest; width <= Widest; width += 0.5)
        {
            var step = TrackView.LabelStep(width);
            Assert.True(12 % step == 0 || step % 12 == 0, $"step {step} at width {width}");
        }
    }
}
