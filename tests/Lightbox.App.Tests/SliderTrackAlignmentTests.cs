using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The hairline track rides the vertical centre of its slider.
/// </summary>
/// <remarks>
/// <para>
/// <b>B137.</b> Reported as "the thin slider does not align to the center of
/// the text". Fluent's slider template reserves 15px above and 15px below the
/// track — fixed grid rows for tick bars nothing in this application shows —
/// so inside our 24px slider the whole track assembly sat pinned 15px from
/// the top and read low against any label beside it. Alignment setters on the
/// template's parts could not win against fixed row heights; the row heights
/// are dynamic resources (<c>SliderPreContentMargin</c>/<c>Post</c>), which
/// <c>Theme.axaml</c> now sets to <c>*</c> so the track row centres by
/// construction at any slider height.
/// </para>
/// </remarks>
public class SliderTrackAlignmentTests
{
    [AvaloniaFact]
    public void TheTrackRidesTheVerticalCentreOfTheSlider()
    {
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = 50, Width = 120 };
        var window = new Window { Content = new StackPanel { Children = { slider } } };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var track = slider.GetVisualDescendants().OfType<Border>()
            .First(b => b.Name == "TrackBackground");
        double top = 0;
        for (Visual? n = track; n is not null && !ReferenceEquals(n, slider); n = n.GetVisualParent())
        {
            top += n.Bounds.Y;
        }

        var trackCentre = top + track.Bounds.Height / 2;
        var sliderCentre = slider.Bounds.Height / 2;
        Assert.True(Math.Abs(trackCentre - sliderCentre) <= 1.0,
            $"the track's centre is {trackCentre:F1} in a {slider.Bounds.Height} slider — "
            + $"{Math.Abs(trackCentre - sliderCentre):F1}px off the centre {sliderCentre:F1}; "
            + "next to a centred label that reads as a misaligned row");
    }
}
