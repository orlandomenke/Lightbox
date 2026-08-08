using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// A lone docker's tab is a grip; a real tab strip is a control.
/// </summary>
/// <remarks>
/// <b>B139.</b> Every slot wears a tab strip now — the owner's call — and the
/// strip is rightly on the header's do-not-drag list, because clicking a tab
/// must switch tabs rather than tear the panel out. But for a slot of ONE the
/// tab is most of the header, so that rule made a lone docker undraggable:
/// reported as the timeline moved to a side that could not be dragged back.
/// A single tab has nothing to switch to, so a press there is a grip.
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

    [AvaloniaFact]
    public void APressOnALoneTabIsAGrip()
    {
        var docker = Shown(DockPanelId.Palette);

        Assert.False(docker.LandedOnAControl(InsideStrip(docker)),
            "a lone docker's only tab refused the drag — the panel cannot be moved at all");
    }

    [AvaloniaFact]
    public void APressOnARealTabStripStaysAControl()
    {
        var docker = Shown(DockPanelId.Palette, DockPanelId.Color);

        Assert.True(docker.LandedOnAControl(InsideStrip(docker)),
            "a press on a two-tab strip must switch tabs, not tear the group out");
    }

    [AvaloniaFact]
    public void TheCloseButtonIsStillAControlEitherWay()
    {
        var docker = Shown(DockPanelId.Palette);
        var button = docker.GetVisualDescendants().OfType<Button>().FirstOrDefault();
        if (button is null) return; // no close command wired in this fixture

        Assert.True(docker.LandedOnAControl(button));
    }
}
