using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Tabs that do not fit a header shrink, cut their titles with an ellipsis, and
/// stay clear of the header's buttons (Q281).
/// </summary>
/// <remarks>
/// What was there: the strip was as wide as its titles and nothing stopped it,
/// so at sidebar width the colour group's tabs ran on under the float, close and
/// collapse buttons — transparent until hover and still taking clicks — and the
/// end of a tab could be neither clicked nor dragged. Measured in the real
/// window: tabs ending at 234 px against buttons starting at 196.
/// </remarks>
[Collection("BrushState")]
public class DockerTabOverflowTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static readonly DockPanelId[] Colour =
        [DockPanelId.Color, DockPanelId.Palette, DockPanelId.Gradient, DockPanelId.Channels];

    private static (Window Window, Docker Docker) Shown(double width, params DockPanelId[] tabs)
    {
        var docker = new Docker { PanelId = tabs[0], Title = DockPanels.Of(tabs[0]).Title, CloseCommand = new NoOp() };
        var window = new Window { Content = docker, Width = width, Height = 300 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        docker.ShowTabs(tabs.Select(DockPanels.Of).ToList(), tabs[0]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, docker);
    }

    private sealed class NoOp : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { }
    }

    /// <summary>Where the header's own controls begin, in the docker's coordinates.</summary>
    private static double ButtonsStart(Docker docker)
    {
        var header = docker.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Header");
        var buttons = header.GetVisualDescendants().OfType<StackPanel>()
            .First(s => s.Orientation == Avalonia.Layout.Orientation.Horizontal && s.Children.OfType<Button>().Any());
        return buttons.TransformToVisual(docker)!.Value.Transform(default).X;
    }

    private void Print(Docker docker)
    {
        foreach (var t in docker.TabRects()) output.WriteLine($"{t.Id}: {t.Bounds.X:F0}..{t.Bounds.Right:F0} ({t.Bounds.Width:F0})");
        output.WriteLine($"buttons start at {ButtonsStart(docker):F0}");
    }

    [AvaloniaFact]
    public void TabsThatDoNotFitEndBeforeTheButtons()
    {
        var (_, docker) = Shown(300, Colour);
        Print(docker);

        var tabs = docker.TabRects();
        Assert.Equal(Colour, tabs.Select(t => t.Id));
        Assert.All(tabs, t => Assert.True(t.Bounds.Right <= ButtonsStart(docker) + 0.5,
            $"{t.Id} runs to {t.Bounds.Right:F0}, under the buttons at {ButtonsStart(docker):F0}"));
        for (var i = 1; i < tabs.Count; i++) Assert.True(tabs[i].Bounds.X >= tabs[i - 1].Bounds.Right - 0.5);
    }

    [Fact]
    public void TheWidestTabsGiveWayFirst()
    {
        // Like a browser: short titles keep their width while the long ones
        // shrink to one shared width, rather than every tab losing a share.
        Assert.Equal([50, 60, 75, 75], TabStripPanel.Fit([50, 60, 100, 120], 260));
        Assert.Equal([75, 50, 75, 60], TabStripPanel.Fit([120, 50, 100, 60], 260));
        // Room enough: nothing changes.
        Assert.Equal([50, 60, 100], TabStripPanel.Fit([50, 60, 100], 400));
        // Equal tabs share equally, and however crowded, they all stay in the
        // room. A 44 px floor that won here pushed the last tabs past the
        // strip's clip: unclickable, while the drop arithmetic still aimed at
        // them (the adversary's refutation).
        Assert.All(TabStripPanel.Fit([90, 90, 90, 90], 180), w => Assert.Equal(45, w));
        var crowded = TabStripPanel.Fit([90, 90, 90, 90, 90, 90], 150);
        Assert.True(crowded.Sum() <= 150, $"six tabs took {crowded.Sum()} of 150");
        Assert.All(crowded, w => Assert.Equal(25, w));
        // Whole pixels, so layout rounding cannot push the strip past its room.
        Assert.True(TabStripPanel.Fit([100, 100, 100], 250).Sum() <= 250);
    }

    [Fact]
    public void TheTabInFrontKeepsItsWholeTitleFirst()
    {
        // The owner's call (Q281): the panel being looked at is named in full,
        // and the tabs behind it are cut harder to pay for it.
        var widths = TabStripPanel.Fit([60, 100, 120, 120], 260, front: 3);
        Assert.Equal(120, widths[3]);
        Assert.True(widths.Sum() <= 260);
        Assert.All(widths.Take(3), w => Assert.True(w >= TabStripPanel.BehindFloor, $"a tab behind was cut to {w}"));

        // But not at any price: a front title too long for the rest to keep
        // their floor gives way, and nothing leaves the strip.
        var squeezed = TabStripPanel.Fit([200, 200, 200], 150, front: 0);
        Assert.True(squeezed.Sum() <= 150, $"took {squeezed.Sum()} of 150");
        Assert.True(squeezed[0] < 200);
        Assert.All(squeezed, w => Assert.True(w > 0));
    }

    [AvaloniaFact]
    public void OnScreenTheFrontTabIsWholeWhileTheOthersAreCut()
    {
        var (_, wide) = Shown(1200, Colour);
        var natural = wide.TabRects().ToDictionary(t => t.Id, t => t.Bounds.Width);
        // Room for Channels whole and three tabs behind it at more than the
        // floor; the others are cut, Channels is not.
        var (_, narrow) = Shown(340, Colour);
        narrow.ShowTabs(Colour.Select(DockPanels.Of).ToList(), DockPanelId.Channels);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Print(narrow);

        var channels = narrow.TabRects().Single(t => t.Id == DockPanelId.Channels);
        Assert.Equal(natural[DockPanelId.Channels], channels.Bounds.Width, 0.5);
        Assert.Contains(narrow.TabRects(), t => t.Bounds.Width < natural[t.Id] - 0.5);
        Assert.All(narrow.TabRects(), t => Assert.True(t.Bounds.Right <= ButtonsStart(narrow) + 0.5));

        // Narrower, keeping it whole would leave the others under their floor:
        // it gives way, and they keep exactly the floor.
        var (_, tight) = Shown(300, Colour);
        tight.ShowTabs(Colour.Select(DockPanels.Of).ToList(), DockPanelId.Channels);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Print(tight);
        Assert.All(tight.TabRects().Where(t => t.Id != DockPanelId.Channels),
            t => Assert.Equal(TabStripPanel.BehindFloor, t.Bounds.Width, 0.5));
        Assert.All(tight.TabRects(), t => Assert.True(t.Bounds.Right <= ButtonsStart(tight) + 0.5));
    }

    [AvaloniaFact]
    public void OnScreenEveryCutTabSharesOneWidthAndTheRestKeepTheirs()
    {
        var (_, wide) = Shown(1200, Colour);
        var natural = wide.TabRects().ToDictionary(t => t.Id, t => t.Bounds.Width);
        var (_, narrow) = Shown(300, Colour);
        Print(narrow);
        // The front tab (Color) is whole by its own rule; this is about the
        // tabs behind it.
        var behind = narrow.TabRects().Where(t => t.Id != DockPanelId.Color).ToList();
        var cut = behind.Where(t => t.Bounds.Width < natural[t.Id] - 0.5).ToList();
        var kept = behind.Except(cut).ToList();

        Assert.NotEmpty(cut);
        Assert.Single(cut.Select(t => Math.Round(t.Bounds.Width)).Distinct());
        Assert.All(kept, t => Assert.Equal(natural[t.Id], t.Bounds.Width, 0.5));
        Assert.All(cut, t => Assert.True(t.Bounds.Width >= kept.Select(k => k.Bounds.Width).DefaultIfEmpty(0).Max() - 0.5,
            $"{t.Id} was cut narrower than a tab that kept its width"));
    }

    [AvaloniaFact]
    public void TabsThatFitKeepTheirNaturalWidthAndNoTooltip()
    {
        var (_, docker) = Shown(1200, Colour);
        var items = docker.GetVisualDescendants().OfType<ListBoxItem>().ToList();

        Assert.All(items, i => Assert.Null(ToolTip.GetTip(i)));
    }

    [AvaloniaFact]
    public void ACutTitleIsWholeInItsTooltip()
    {
        var (_, docker) = Shown(300, Colour);
        var channels = docker.GetVisualDescendants().OfType<ListBoxItem>()
            .First(i => i.DataContext is DockPanelInfo { Id: DockPanelId.Channels });
        var text = channels.GetVisualDescendants().OfType<TextBlock>().First();

        Assert.Equal(Avalonia.Media.TextTrimming.CharacterEllipsis, text.TextTrimming);
        Assert.Equal("Channels", ToolTip.GetTip(channels));
        // And it sheds its side padding so more of the title shows.
        Assert.True(TabStripPanel.GetIsCut(channels));
        Assert.True(channels.Padding.Left < 10, $"a cut tab kept {channels.Padding.Left} px of side padding");
    }

    [AvaloniaFact]
    public void TabsThatDoNotFitShrinkAndStayClickable()
    {
        // The last tab is the one that used to sit under the buttons: a real
        // click on it, through the window's input, has to reach it.
        var (window, docker) = Shown(300, Colour);
        DockPanelId? picked = null;
        docker.TabPicked += (_, id) => picked = id;

        var last = docker.TabRects().Last().Bounds;
        var at = new Point(last.Right - 6, last.Y + last.Height / 2).Transform(docker.TransformToVisual(window)!.Value);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(DockPanelId.Channels, picked);
    }

    [AvaloniaFact]
    public void MoreTabsThanTheFloorAllowsStillAllFitAndTheLastStillTakesAClick()
    {
        DockPanelId[] six = [DockPanelId.Color, DockPanelId.Palette, DockPanelId.Gradient,
                             DockPanelId.Channels, DockPanelId.History, DockPanelId.Reference];
        var (window, docker) = Shown(260, six);
        Print(docker);
        Assert.All(docker.TabRects(), t => Assert.True(t.Bounds.Right <= ButtonsStart(docker) + 0.5,
            $"{t.Id} runs to {t.Bounds.Right:F0}, past the strip at {ButtonsStart(docker):F0}"));

        DockPanelId? picked = null;
        docker.TabPicked += (_, id) => picked = id;
        var last = docker.TabRects().Last().Bounds;
        var at = new Point(last.X + last.Width / 2, last.Y + last.Height / 2).Transform(docker.TransformToVisual(window)!.Value);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(DockPanelId.Reference, picked);
    }

    [AvaloniaFact]
    public void InTheRealSidebarEveryTabEndsBeforeTheButtons()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Tabs", 400, 300, 12, 72, "#ffffff", false));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        foreach (var docker in window.FindControl<DockStrip>("RightStrip")!.Children.OfType<Docker>())
        {
            Print(docker);
            Assert.All(docker.TabRects(), t => Assert.True(t.Bounds.Right <= ButtonsStart(docker) + 0.5,
                $"{docker.PanelId}: {t.Id} runs to {t.Bounds.Right:F0}, buttons at {ButtonsStart(docker):F0}"));
        }
    }
}
