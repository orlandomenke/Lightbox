using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.Views;
using Lightbox.App.ViewModels;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The gear opens the tool options where the gear is, and always gives the
/// page back (B261).
/// </summary>
/// <remarks>
/// <b>The return is the part worth guarding, not the opening.</b> The flyout
/// borrows the Tool options panel's content rather than copying it — one
/// definition, two hosts — so a close path that forgets to hand it back leaves
/// the panel permanently empty, and an artist who then opens it from the View
/// menu finds nothing there with no error to explain it. That failure survives
/// until a restart and would not show up in any test that only checked the
/// flyout opened.
/// </remarks>
[Collection("BrushState")]
public sealed class ToolOptionsFlyoutTests(Xunit.ITestOutputHelper output) : BrushStateIsolated
{
    private static (MainWindow Window, MainViewModel Vm, Button Gear) Open()
    {
        var window = new MainWindow { Width = 1920, Height = 900 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (
            window,
            (MainViewModel)window.DataContext!,
            window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "ToolOptionsGear"));
    }

    private static Docker TheDocker(MainWindow window) =>
        window.GetVisualDescendants().OfType<Docker>()
            .Concat(window.GetSelfAndVisualDescendants().OfType<Docker>())
            .First(d => d.Name == "ToolOptionsDocker");

    [AvaloniaFact]
    public void TheGearIsWiredToTheFlyoutRatherThanTheDockerCommand()
    {
        var (window, _, gear) = Open();
        try
        {
            // The regression this replaces: the gear ran OpenToolOptionsCommand,
            // which opens a panel across the window. A bound command here means
            // the click handler was reverted.
            Assert.Null(gear.Command);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ClickingTheGearLendsThePageToAFlyoutAndClosingGivesItBack()
    {
        var (window, vm, gear) = Open();
        try
        {
            vm.Workspace.SetVisible(Lightbox.App.Docking.DockPanelId.ToolOptions, false);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var docker = TheDocker(window);
            var page = docker.Content;
            Assert.NotNull(page);

            gear.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            output.WriteLine($"while open, docker content is {(docker.Content is null ? "lent away" : "still here")}");

            // Lent, not copied: the panel is empty precisely because the flyout
            // is showing the same controls.
            Assert.Null(docker.Content);

            var flyout = window.GetVisualDescendants().OfType<ScrollViewer>()
                .FirstOrDefault(s => ReferenceEquals(s.Content, page));
            Assert.NotNull(flyout);

            // Closed the way anything closes it — light dismiss, Escape, the
            // gear again all come through here. The page must come home, and
            // this is the failure that would strand the panel empty.
            FlyoutBase.GetAttachedFlyout(gear)!.Hide();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            output.WriteLine($"after close, docker content is {(docker.Content is null ? "MISSING" : "restored")}");
            Assert.Same(page, docker.Content);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void WithThePanelAlreadyOnScreenTheGearBringsItForwardInstead()
    {
        var (window, vm, gear) = Open();
        try
        {
            vm.Workspace.SetVisible(Lightbox.App.Docking.DockPanelId.ToolOptions, true);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var docker = TheDocker(window);
            var page = docker.Content;

            gear.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            // Lending the content away from a panel the artist is looking at
            // would empty it in front of them, which is worse than the problem
            // the flyout solves.
            Assert.Same(page, docker.Content);
            Assert.True(vm.Workspace.ToolOptionsDockerVisible);
        }
        finally { window.Close(); }
    }
}
