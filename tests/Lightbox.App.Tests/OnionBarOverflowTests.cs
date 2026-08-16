using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.Views;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The onion controls are actually on the timeline bar at an ordinary window
/// width — labelled, and not folded into the ▾.
/// </summary>
/// <remarks>
/// The markup test in <c>OnionDepthTests</c> asserts the controls exist in both
/// bars and are bound. That is not the same question as whether an artist can
/// see them: <see cref="OverflowBar"/> walks its children in order and sends
/// everything from the first that does not fit into the ▾, and the onion
/// controls sit behind a 526px transport on a bar with about 900 to give. They
/// fit today with room to spare, and the margin is thin enough that a future
/// addition to this bar could take them without anything turning red — so the
/// guard measures the laid-out window rather than the markup.
/// <para>
/// Worth knowing when reading this: the group being one <see cref="OnionBar"/>
/// control rather than four children makes it atomic to the split — it fits
/// whole or goes to the menu whole. That is fine while it fits and is the thing
/// to reconsider first if this test ever fails.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class OnionBarOverflowTests(Xunit.ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>An ordinary laptop window — not a wide one, which is the case that broke.</summary>
    private static (MainWindow Window, OverflowBar Bar) TimelineBar(double width = 1280)
    {
        var window = new MainWindow { Width = width, Height = 800 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var docker = window.GetVisualDescendants().OfType<Docker>()
            .First(d => d.Name == "TimelineDocker");
        return (window, docker.GetVisualDescendants().OfType<OverflowBar>().First());
    }

    [AvaloniaFact]
    public void TheDepthSpinnersStayOnTheTimelineBar()
    {
        var (window, bar) = TimelineBar();
        try
        {
            foreach (var c in bar.Children)
            {
                output.WriteLine($"{c.GetType().Name} w={c.Bounds.Width:F0} visible={c.IsVisible}");
            }

            // Inside the OnionBar specifically — the transport carries fps and
            // speed fields of its own, and counting every descendant would find
            // those too and pass whatever happened to the onion ones.
            var onion = bar.GetSelfAndVisualDescendants().OfType<OnionBar>().Single();
            var spinners = onion.GetSelfAndVisualDescendants().OfType<NumericUpDown>()
                .Where(n => n.Bounds.Width > 0).ToList();
            Assert.Equal(2, spinners.Count);

            // And the switch that turns it on, which is the one flipped most.
            Assert.Contains(onion.GetSelfAndVisualDescendants().OfType<CheckBox>(),
                c => c.Content as string == "Onion skin" && c.Bounds.Width > 0);

            // The motion trail's switch shares the bar and the same hazard.
            // Its depth fields are deliberately NOT out here: the first
            // laid-out measurement showed the labelled pair pushing the bar's
            // next group into the ▾, so they live behind the ⚙ and the
            // spinner count above staying at two is part of what this guards.
            Assert.Contains(onion.GetSelfAndVisualDescendants().OfType<CheckBox>(),
                c => c.Content as string == "Motion trail" && c.Bounds.Width > 0);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// The boxes say what they count.
    /// </summary>
    /// <remarks>
    /// Two bare number boxes reading 1 and 1 were read as "onion skin can only
    /// show one drawing each way" rather than as a control. The tooltips said
    /// otherwise, and a tooltip is not read by somebody who does not already
    /// suspect there is something to read.
    /// </remarks>
    [AvaloniaFact]
    public void TheDepthSpinnersAreLabelled()
    {
        var (window, bar) = TimelineBar();
        try
        {
            var labels = bar.GetSelfAndVisualDescendants().OfType<OnionBar>()
                .SelectMany(o => o.GetSelfAndVisualDescendants().OfType<TextBlock>())
                .Select(t => t.Text)
                .ToList();
            output.WriteLine(string.Join(", ", labels));
            Assert.Contains("Ghosts", labels);
        }
        finally
        {
            window.Close();
        }
    }
}
