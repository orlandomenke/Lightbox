using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Real pointer events reach the docker's drag, end to end (B183).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists beside <c>LoneDockerDragTests</c>.</b> Those tests call
/// <c>HeaderPressed</c>/<c>LandedOnTheGrip</c> directly, so they prove the
/// state machine and say nothing about whether events arrive at it. B183 lived
/// in exactly that gap: the tab strip's <c>ListBox</c> marks a press on a tab
/// as handled while selecting it, and the header's plain subscription never
/// hears handled events — so the grip logic was correct, tested, and
/// unreachable. Every docker wears its title as a tab now, which made every
/// docker undraggable, and undock-by-drag with it.
/// </para>
/// <para>
/// These drive the headless window's own mouse, so they fail if anything
/// between the window and <c>PanelDragStarted</c> swallows the events again.
/// </para>
/// </remarks>
public class DockerDragRoutingTests(ITestOutputHelper output)
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

    private static Point Centre(Visual item, Window window) =>
        item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;

    /// <summary>A pointer move whose properties say no button is down.</summary>
    private static Avalonia.Input.PointerEventArgs MovedWithoutButton(Docker docker, Window root, Point position) =>
        new(Avalonia.Input.InputElement.PointerMovedEvent, docker,
            new Avalonia.Input.Pointer(1, Avalonia.Input.PointerType.Mouse, true),
            root, position, 0,
            new Avalonia.Input.PointerPointProperties(
                Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PointerUpdateKind.Other),
            Avalonia.Input.KeyModifiers.None);

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(3)]
    public void APressAndPullOnATabStartsTheDrag_HoweverManyTabsThereAre(int count)
    {
        var (window, docker) = Shown(new[]
        {
            DockPanelId.Layers, DockPanelId.Color, DockPanelId.Palette,
        }.Take(count).ToArray());

        var dragStarted = false;
        docker.PanelDragStarted += (_, _) => dragStarted = true;

        var strip = docker.GetVisualDescendants().OfType<ListBox>().First();
        var tab = strip.GetVisualDescendants().OfType<ListBoxItem>().First();
        var origin = Centre(tab, window);

        window.MouseDown(origin, Avalonia.Input.MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        // The modifier is what a real platform sends with every move while the
        // button is held; the drag guard reads it, so the test must carry it.
        window.MouseMove(new Point(origin.X + 40, origin.Y + 40), RawInputModifiers.LeftMouseButton);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        output.WriteLine($"{count} tab(s): drag started {dragStarted}");
        Assert.True(dragStarted, "a real press+pull on a tab never armed the drag (B183)");
    }

    /// <summary>The selection the ListBox made still happens — the fix adds the drag, it does not steal the click.</summary>
    [AvaloniaFact]
    public void PickingUpATabStillSelectsIt()
    {
        var (window, docker) = Shown(DockPanelId.Layers, DockPanelId.Color);

        var strip = docker.GetVisualDescendants().OfType<ListBox>().First();
        var second = strip.GetVisualDescendants().OfType<ListBoxItem>().Skip(1).First();

        window.MouseDown(Centre(second, window), Avalonia.Input.MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        output.WriteLine($"selected: {(strip.SelectedItem as DockPanelInfo)?.Id}");
        Assert.Equal(DockPanelId.Color, (strip.SelectedItem as DockPanelInfo)?.Id);
    }

    /// <summary>A short press that never travels stays a click, not a tear-out.</summary>
    [AvaloniaFact]
    public void AClickWithoutAPullDoesNotDrag()
    {
        var (window, docker) = Shown(DockPanelId.Layers, DockPanelId.Color);

        var dragStarted = false;
        docker.PanelDragStarted += (_, _) => dragStarted = true;

        var strip = docker.GetVisualDescendants().OfType<ListBox>().First();
        var tab = strip.GetVisualDescendants().OfType<ListBoxItem>().First();
        var origin = Centre(tab, window);

        window.MouseDown(origin, Avalonia.Input.MouseButton.Left);
        window.MouseMove(new Point(origin.X + 2, origin.Y + 1));
        window.MouseUp(origin, Avalonia.Input.MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(dragStarted);
    }

    /// <summary>
    /// A lost release never leaves the drag armed. Clicking a tab can rebuild
    /// the strip, taking the pointer's capture target with it — the release
    /// then routes past the header, and the next innocent move (button long
    /// since up) tore the tab out and left it chasing the pointer with no
    /// release ever coming to end it. The guard: a drag only starts while the
    /// button is held.
    /// </summary>
    [AvaloniaFact]
    public void AMoveWithTheButtonUpNeverStartsADrag()
    {
        var (window, docker) = Shown(DockPanelId.Layers, DockPanelId.Color);

        var dragStarted = false;
        docker.PanelDragStarted += (_, _) => dragStarted = true;

        var strip = docker.GetVisualDescendants().OfType<ListBox>().First();
        var tab = strip.GetVisualDescendants().OfType<ListBoxItem>().First();
        var origin = Centre(tab, window);

        // A real press arms the grip; the release is then "lost" (never
        // delivered), and the next move arrives with no button down.
        window.MouseDown(origin, Avalonia.Input.MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        docker.HeaderMoved(MovedWithoutButton(docker, window, new Point(origin.X + 40, origin.Y + 40)));

        Assert.False(dragStarted, "a buttonless move started a drag (the lost-release bug)");
    }

    /// <summary>Buttons in the header still own their presses — the fix listens, it does not grab.</summary>
    [AvaloniaFact]
    public void TheFloatButtonIsStillNotAGrip()
    {
        var (window, docker) = Shown(DockPanelId.Layers);
        docker.CanFloat = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var dragStarted = false;
        docker.PanelDragStarted += (_, _) => dragStarted = true;

        var floater = docker.GetVisualDescendants().OfType<Button>().First(b => b.Name == "PART_Float");
        var origin = Centre(floater, window);

        window.MouseDown(origin, Avalonia.Input.MouseButton.Left);
        window.MouseMove(new Point(origin.X + 40, origin.Y + 40));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(dragStarted);
    }
}
