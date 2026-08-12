using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// What a press on a docker's header is a grip for: the <b>tab</b>, and
/// nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>B155 inverted this, and the inversion deleted a special case.</b> The
/// header used to be the grip with the tab strip carved out of it, which is
/// backwards from every docking UI an artist has used — you pick a panel up by
/// its tab — and it made the header's empty space the largest interactive
/// target in the docker.
/// </para>
/// <para>
/// The rule it replaces needed an exception for a strip of ONE (B139: a lone
/// docker is nearly all tab, so excluding the strip left nothing to hold, and
/// the timeline moved to a side could not be dragged back). With the tab as the
/// grip, one tab and five behave identically and the exception has nowhere to
/// live — which is why both cases are still asserted here.
/// </para>
/// </remarks>
public class LoneDockerDragTests
{
    private static Docker Shown(params DockPanelId[] tabs)
    {
        var docker = new Docker { PanelId = tabs[0], Title = "x" };
        var window = new Window { Content = docker };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        docker.ShowTabs(tabs.Select(DockPanels.Of).ToList(), tabs[0]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return docker;
    }

    private static Avalonia.Visual InsideStrip(Docker docker)
    {
        var strip = docker.GetVisualDescendants().OfType<ListBox>().First();
        // A press lands on whatever is deepest — a presenter inside the item.
        return strip.GetVisualDescendants().LastOrDefault() ?? strip;
    }

    /// <summary>The header background — the part that used to be the grip.</summary>
    private static Avalonia.Visual BareHeader(Docker docker) =>
        docker.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Header");

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void APressOnATabIsAGrip_HoweverManyTabsThereAre(int count)
    {
        var docker = Shown(new[]
        {
            DockPanelId.Palette, DockPanelId.Color, DockPanelId.Reference, DockPanelId.Layers,
        }.Take(count).ToArray());

        Assert.True(docker.LandedOnTheGrip(InsideStrip(docker)),
            "a press on a tab must be able to move the panel — the tab is the grip");
    }

    /// <summary>
    /// The reported bug. The empty part of a header exists to give the title
    /// room, and it was the largest drag target in the application.
    /// </summary>
    [AvaloniaFact]
    public void APressOnTheBareHeaderIsNotAGrip()
    {
        var docker = Shown(DockPanelId.Palette, DockPanelId.Color);

        Assert.False(docker.LandedOnTheGrip(BareHeader(docker)),
            "the header's empty space tore the panel out; only the tab should do that");
    }

    /// <summary>
    /// A docker with no tab strip still has to be movable, and its title is the
    /// only thing in the header that names it.
    /// </summary>
    [AvaloniaFact]
    public void WithNoTabsAtAllTheTitleIsTheGrip()
    {
        var docker = new Docker { PanelId = DockPanelId.Palette, Title = "Palette" };
        var window = new Window { Content = docker };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var title = docker.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "PART_Title");
        Assert.True(docker.LandedOnTheGrip(title));
    }

    [AvaloniaFact]
    public void TheCloseButtonIsStillNotAGrip()
    {
        var docker = Shown(DockPanelId.Palette);
        var button = docker.GetVisualDescendants().OfType<Button>().FirstOrDefault();
        if (button is null) return; // no close command wired in this fixture

        Assert.False(docker.LandedOnTheGrip(button));
    }
}
