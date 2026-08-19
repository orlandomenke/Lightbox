using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.Docking;

namespace Lightbox.App.Tests;

/// <summary>
/// Rearranging the tabs in one header, from the measurement to the model.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists beside <c>DockZoneTests</c> and <c>DockLayoutTests</c>.</b>
/// Those two cover the arithmetic and the bookkeeping with numbers a test wrote
/// down, and both would stay green if the header reported no tabs at all — the
/// drop resolution falls back to "join this group, last", which is what every
/// header drop meant before this. That is the same shape as B183: correct
/// logic, tested, and unreachable because nothing was feeding it.
/// </para>
/// <para>
/// So these drive a realised docker: the rectangles come off the strip the
/// window actually laid out, and go through the real resolution into a real
/// layout.
/// </para>
/// </remarks>
public class DockerTabRearrangeTests(ITestOutputHelper output)
{
    private static (Window Window, Docker Docker) Shown(params DockPanelId[] tabs)
    {
        var docker = new Docker { PanelId = tabs[0], Title = DockPanels.Of(tabs[0]).Title };
        var window = new Window { Content = docker, Width = 400, Height = 300 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        docker.ShowTabs(tabs.Select(DockPanels.Of).ToList(), tabs[0]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, docker);
    }

    /// <summary>The docker as the drag arithmetic sees it, in window coordinates.</summary>
    private static PanelSlot SlotOf(Docker docker, Window window, DockPanelId active)
    {
        Visual visual = docker;
        var origin = visual.TranslatePoint(default, window)!.Value;
        return new PanelSlot(
            active, DockSide.Right, 0,
            new DockRect(origin.X, origin.Y, docker.Bounds.Width, docker.Bounds.Height),
            docker.HeaderHeight,
            [.. docker.TabRects().Select(t => t with { Bounds = t.Bounds.Offset(origin.X, origin.Y) })]);
    }

    [AvaloniaFact]
    public void TheHeaderSaysWhereEachOfItsTabsIs()
    {
        var (window, docker) = Shown(DockPanelId.Layers, DockPanelId.Color, DockPanelId.Palette);

        var tabs = docker.TabRects();

        foreach (var tab in tabs) output.WriteLine($"{tab.Id}: {tab.Bounds}");
        Assert.Equal(
            [DockPanelId.Layers, DockPanelId.Color, DockPanelId.Palette],
            tabs.Select(t => t.Id));
        // Left to right, each one starting where the last ended and none of
        // them empty — a zero-width rectangle would resolve every position to
        // the same gap without failing anything.
        for (var i = 0; i < tabs.Count; i++)
        {
            Assert.True(tabs[i].Bounds.Width > 0, $"{tabs[i].Id} measured no width");
            Assert.True(tabs[i].Bounds.Height > 0, $"{tabs[i].Id} measured no height");
            if (i > 0) Assert.True(tabs[i].Bounds.X >= tabs[i - 1].Bounds.Right - 0.5,
                $"{tabs[i].Id} overlaps {tabs[i - 1].Id}");
        }
        // And on the band the drop arithmetic treats as the header, or the
        // pointer would be over a tab the resolution has already ruled out.
        // A tab sits ON the header's bottom rule and measured here overhangs it
        // by a pixel, which is why DockZones takes the tabs themselves as a
        // strip hit as well as the band.
        var header = new DockRect(0, 0, docker.Bounds.Width, docker.HeaderHeight);
        Assert.All(tabs, t => Assert.True(
            t.Bounds.Y >= header.Y && t.Bounds.Y < header.Bottom,
            $"{t.Id} at {t.Bounds} does not start in the header {header}"));
    }

    [AvaloniaFact]
    public void AnUntabbedDockerMeasuresNothingAndThatIsTheFallback()
    {
        // A docker whose header was never given tabs reports none, which the
        // drop arithmetic reads as "this header cannot say where in itself
        // anything goes" rather than as an empty strip.
        var docker = new Docker { PanelId = DockPanelId.Layers, Title = "Layers" };
        var window = new Window { Content = docker, Width = 400, Height = 300 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Empty(docker.TabRects());
    }

    [AvaloniaFact]
    public void PointingAtATabResolvesToThatTabsPosition()
    {
        var (window, docker) = Shown(DockPanelId.Layers, DockPanelId.Color, DockPanelId.Palette);
        var slot = SlotOf(docker, window, DockPanelId.Layers);

        // Just past the middle of the second tab, which is the gap before the
        // third — measured off the strip rather than assumed.
        var second = slot.Strip[1].Bounds;
        var drop = DockZones.Resolve(
            second.Right - 1, second.Y + second.Height / 2, new DockRect(0, 0, 400, 300),
            [slot], DockPanelId.Gradient, DockLayout.Default());

        output.WriteLine($"tabs: {string.Join(", ", slot.Strip.Select(t => $"{t.Id} {t.Bounds}"))}");
        Assert.Equal(DockPanelId.Layers, drop!.Value.IntoGroupOf);
        Assert.Equal(2, drop.Value.TabIndex);
    }

    [AvaloniaFact]
    public void ATabDraggedToTheEndOfItsOwnHeaderEndsUpThere()
    {
        // The whole feature in one test: what the header measured, through the
        // real drop resolution, into the real layout.
        var layout = new DockLayout();
        layout.Dock(DockPanelId.Layers, DockSide.Right, 0);
        layout.JoinGroup(DockPanelId.Color, DockPanelId.Layers);
        layout.JoinGroup(DockPanelId.Palette, DockPanelId.Layers);
        layout.Activate(DockPanelId.Layers);
        Assert.Equal(
            [DockPanelId.Layers, DockPanelId.Color, DockPanelId.Palette],
            layout.SlotOf(DockPanelId.Layers));

        var (window, docker) = Shown([.. layout.SlotOf(DockPanelId.Layers)]);
        var slot = SlotOf(docker, window, DockPanelId.Layers);

        // Let go past the last tab, in the empty part of the header.
        var last = slot.Strip[^1].Bounds;
        var drop = DockZones.Resolve(
            last.Right + 8, last.Y + last.Height / 2, new DockRect(0, 0, 400, 300),
            [slot], DockPanelId.Layers, DockLayout.Default());

        Assert.NotNull(drop);
        Assert.Equal(DockPanelId.Layers, drop!.Value.IntoGroupOf);
        // What MainWindow does on release, and the only call it makes.
        layout.JoinGroup(DockPanelId.Layers, drop.Value.IntoGroupOf!.Value, drop.Value.TabIndex);

        Assert.Equal(
            [DockPanelId.Color, DockPanelId.Palette, DockPanelId.Layers],
            layout.SlotOf(DockPanelId.Layers));
        // Rearranging a strip is not choosing what it shows.
        Assert.Equal(DockPanelId.Layers, layout.ActiveOf(layout.SlotOf(DockPanelId.Layers)));
    }

    [AvaloniaFact]
    public void ATabDraggedOntoItselfLeavesTheStripAlone()
    {
        var layout = new DockLayout();
        layout.Dock(DockPanelId.Layers, DockSide.Right, 0);
        layout.JoinGroup(DockPanelId.Color, DockPanelId.Layers);
        layout.JoinGroup(DockPanelId.Palette, DockPanelId.Layers);
        layout.Activate(DockPanelId.Color);

        var (window, docker) = Shown([.. layout.SlotOf(DockPanelId.Layers)]);
        var slot = SlotOf(docker, window, DockPanelId.Color);

        var own = slot.Strip[1].Bounds;   // Color's own tab
        var drop = DockZones.Resolve(
            own.X + 1, own.Y + own.Height / 2, new DockRect(0, 0, 400, 300),
            [slot], DockPanelId.Color, DockLayout.Default());

        // A target, not nothing: resolving to null here would float the panel
        // on a drag that let go over its own name.
        Assert.NotNull(drop);
        layout.JoinGroup(DockPanelId.Color, drop!.Value.IntoGroupOf!.Value, drop.Value.TabIndex);

        Assert.Equal(
            [DockPanelId.Layers, DockPanelId.Color, DockPanelId.Palette],
            layout.SlotOf(DockPanelId.Layers));
    }
}
