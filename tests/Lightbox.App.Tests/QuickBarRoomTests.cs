using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The quick options bar has room to be a quick options bar (B260).
/// </summary>
/// <remarks>
/// <para>
/// <b>Measures the laid-out window, not the markup.</b> Every control in this
/// bar was present and correctly bound while the bug was live — what was wrong
/// was that the row had no width left to give them, and no markup test can see
/// that. The same distinction <c>OnionBarOverflowTests</c> draws for the
/// timeline bar.
/// </para>
/// <para>
/// 1280 is the case that matters: it is an ordinary laptop, and it is where a
/// bar starved by 872px of workspace tabs had literally zero width.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class QuickBarRoomTests(Xunit.ITestOutputHelper output) : BrushStateIsolated
{
    private static (MainWindow Window, MainViewModel Vm, OverflowBar Bar) Bar(double width)
    {
        var window = new MainWindow { Width = width, Height = 900 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (
            window,
            (MainViewModel)window.DataContext!,
            window.GetVisualDescendants().OfType<OverflowBar>().First(b => b.Name == "QuickToolOptions"));
    }

    private static void Relayout(MainWindow window, double width)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.Measure(new Avalonia.Size(width, 900));
        window.Arrange(new Avalonia.Rect(0, 0, width, 900));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData(1280.0)]
    [InlineData(1920.0)]
    public void SizeAndOpacityKeepTheirWidthAtEveryOrdinaryWindowSize(double width)
    {
        var (window, _, _) = Bar(width);
        try
        {
            Relayout(window, width);
            var pinned = window.GetVisualDescendants().OfType<StackPanel>()
                .First(p => p.Name == "QuickFixedOptions");

            output.WriteLine($"window {width}: pinned Size/Opacity laid out at {pinned.Bounds.Width:F0}px");
            // Q70's second rule, and the one layout was quietly overruling: the
            // two controls a hand reaches for mid-stroke are pinned and must
            // never fold away. They were arranged at 0px at 1280.
            Assert.True(
                pinned.Bounds.Width > 300,
                $"pinned Size/Opacity got {pinned.Bounds.Width:F0}px at a {width}px window");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheWorkspacePickerDoesNotEatTheRow()
    {
        var (window, _, _) = Bar(1280);
        try
        {
            Relayout(window, 1280);
            var picker = window.GetVisualDescendants().OfType<ComboBox>()
                .First(c => c.Name == "WorkspacePicker");

            output.WriteLine($"workspace picker {picker.Bounds.Width:F0}px (was 872 as a tab strip)");
            // A dropdown rather than six flush tabs. The number is loose on
            // purpose — this guards the order of magnitude, not the styling.
            Assert.True(
                picker.Bounds.Width < 300,
                $"the picker is back to {picker.Bounds.Width:F0}px and is starving the bar again");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1280.0)]
    [InlineData(1920.0)]
    public void ChoosingTheMarqueeStaysOnTheBarWithTheSelectToolInHand(double width)
    {
        var (window, vm, bar) = Bar(width);
        try
        {
            vm.ActiveTool = ToolId.Select;
            Relayout(window, width);
            var shapes = window.GetVisualDescendants().OfType<SelectShapeBar>().Single();

            output.WriteLine(
                $"window {width}: bar {bar.Bounds.Width:F0}px, marquee at x={shapes.Bounds.X:F0} w={shapes.Bounds.Width:F0}");
            // The OverflowBar parks what does not fit past its right edge, so
            // "on the bar" is a position rather than a visibility.
            Assert.True(
                shapes.Bounds.X < bar.Bounds.Width,
                $"the marquee shapes were pushed into the ▾ at a {width}px window");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheSelectGroupIsFourItemsSoItCanPartlyFit()
    {
        var (window, vm, bar) = Bar(1920);
        try
        {
            vm.ActiveTool = ToolId.Select;
            Relayout(window, 1920);
            var parts = bar.Children.Where(c => c.IsVisible && c.DesiredSize.Width > 0).ToList();
            foreach (var p in parts) output.WriteLine($"{p.GetType().Name} {p.DesiredSize.Width:F0}px");

            // The heart of B260's second cause: as one control the group was
            // atomic to the split and so went to the menu whole, every time.
            // More than one part is what makes partial fitting possible at all.
            Assert.True(parts.Count > 1, "the Select group is atomic again and can only fit whole");
        }
        finally { window.Close(); }
    }
}
