using Avalonia.Headless.XUnit;
using Avalonia.Media;
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

    /// <summary>
    /// Numbering more frames must not cost more to paint: a number is built
    /// once and kept.
    /// </summary>
    /// <remarks>
    /// The adaptive step multiplies how many numbers a long scene draws — up to
    /// twelvefold at the widest zoom — and the strip repaints every time the
    /// playhead moves. Measured before the cache went in: the
    /// <c>FormattedText</c> for 2000 labels took 78 ms against a 12 fps budget
    /// of 83, where 42 labels took 1.07 ms. The step is only affordable because
    /// the numbers are kept.
    /// <para>
    /// <b>Asserted as identity rather than as elapsed time</b>, and the first
    /// draft of this test is why: a ten-fold ratio passed at 11.6 ms cold and
    /// failed at 5.0 ms cold, because the cold pass carries JIT warm-up that
    /// varies per run. The claim is "the same object comes back", which is
    /// exactly what a cache means and does not depend on the machine.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void ARulerNumberIsBuiltOnceAndKept()
    {
        var view = new TrackView { FrameCount = 2000, FrameWidth = Widest };
        var face = new Typeface(FontFamily.Default);

        var first = view.LabelForTests(7, face, 10, Brushes.White);
        var again = view.LabelForTests(7, face, 10, Brushes.White);

        Assert.Same(first, again);
    }

    [AvaloniaFact]
    public void AChangeOfStyleThrowsTheKeptNumbersAway()
    {
        var view = new TrackView { FrameCount = 100, FrameWidth = Widest };
        var face = new Typeface(FontFamily.Default);
        var first = view.LabelForTests(7, face, 10, Brushes.White);

        var recoloured = view.LabelForTests(7, face, 10, Brushes.Red);

        // A FormattedText holds its brush, so a cache that survived a theme
        // change would paint the ruler in the old colour for as long as the
        // control lived.
        Assert.NotSame(first, recoloured);
    }
}
