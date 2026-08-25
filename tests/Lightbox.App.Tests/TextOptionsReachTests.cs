using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.Docking;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;

namespace Lightbox.App.Tests;

/// <summary>
/// Whether an artist with the text tool in hand can actually reach its options.
/// </summary>
/// <remarks>
/// <para>
/// <b>The tool shipped with its options unreachable</b>, and the registry tests
/// that exist did not catch it: the id is in <c>QuickBarCatalog</c>, the gate is
/// on <c>WorkspaceViewModel</c>, and the control is hosted in the bar — every
/// individual registration was present, and the bar was still empty. What none
/// of them asked was the artist's question: I have the tool, where are its
/// options.
/// </para>
/// <para>
/// So these assert reachability from the workspace an artist is actually in,
/// including the five built-in workspaces that name their bar contents
/// explicitly and therefore do not inherit a new catalogue entry.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class TextOptionsReachTests(Xunit.ITestOutputHelper output) : BrushStateIsolated
{
    [Fact]
    public void EveryBuiltInWorkspaceOffersTheTextOptions()
    {
        // A built-in workspace that names its bar contents does not pick up a
        // catalogue entry added later — so every one of them has to name this
        // one, or the tool has no options in the workspace an artist chose.
        var store = WorkspaceStore.Default();

        var without = store.Workspaces
            .Where(w => w.Layout.QuickBar is not null)
            .Where(w => !w.Layout.QuickBarContents.Contains(QuickBarCatalog.TextOptions))
            .Select(w => w.Name)
            .ToList();

        output.WriteLine($"workspaces naming their bar: "
            + string.Join(", ", store.Workspaces.Where(w => w.Layout.QuickBar is not null).Select(w => w.Name)));
        Assert.True(without.Count == 0, $"no text options in: {string.Join(", ", without)}");
    }

    [Fact]
    public void TheDefaultBarCarriesTheTextOptions() =>
        Assert.Contains(QuickBarCatalog.TextOptions, QuickBarCatalog.ToolDefaults);

    [AvaloniaTheory]
    [InlineData(1280.0)]
    [InlineData(1920.0)]
    public void TheTextOptionsStayOnTheBarWithTheTextToolInHand(double width)
    {
        // B260's lesson, one tool along: the OverflowBar parks what does not
        // fit past its right edge, so "on the bar" is a position rather than a
        // visibility. A group the artist can only reach through the ▾ is a
        // group they will not find.
        var window = new MainWindow { Width = width, Height = 900 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        try
        {
            vm.ActiveTool = ToolId.Text;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.Measure(new Avalonia.Size(width, 900));
            window.Arrange(new Avalonia.Rect(0, 0, width, 900));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var bar = window.GetVisualDescendants()
                .OfType<Lightbox.App.Controls.OverflowBar>().First(b => b.Name == "QuickToolOptions");
            var text = window.GetVisualDescendants().OfType<TextOptionsBar>().Single();

            output.WriteLine(
                $"window {width}: bar {bar.Bounds.Width:F0}px, text options at x={text.Bounds.X:F0} "
                + $"w={text.Bounds.Width:F0} (wants {text.DesiredSize.Width:F0})");
            foreach (var c in bar.Children.Where(c => c.IsVisible))
            {
                output.WriteLine($"   in bar: {c.GetType().Name} wants {c.DesiredSize.Width:F0} at x={c.Bounds.X:F0}");
            }
            Assert.True(
                text.Bounds.X < bar.Bounds.Width,
                $"the text options were pushed into the ▾ at a {width}px window");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PickingTheTextToolShowsItsOptionsBar()
    {
        var window = new MainWindow { Width = 1400, Height = 900 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;

        vm.SelectToolCommand.Execute(ToolId.Text);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.Measure(new Avalonia.Size(1400, 900));
        window.Arrange(new Avalonia.Rect(0, 0, 1400, 900));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var bar = window.GetVisualDescendants().OfType<TextOptionsBar>().FirstOrDefault();
        output.WriteLine(
            $"IsTextTool={vm.IsTextTool} QuickTextOptions={vm.Workspace.QuickTextOptions} bar={bar is not null} visible={bar?.IsVisible}");

        Assert.True(vm.IsTextTool, "the tool is in hand");
        Assert.True(vm.Workspace.QuickTextOptions, "the workspace offers the text options");
        Assert.NotNull(bar);
        Assert.True(bar!.IsVisible, "the text options bar must be on screen with the text tool in hand");
    }
}
