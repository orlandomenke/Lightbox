using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Lightbox.App.Views;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The tool rail's adaptive columns (Q56): one when the window is tall enough
/// to hold every tool, two ordinarily, centred either way.
/// </summary>
[Collection("BrushState")]
public sealed class ToolRailReflowTests : BrushStateIsolated
{
    [AvaloniaFact]
    public void TheRailUsesOneColumnWhenTallAndTwoWhenShort()
    {
        var window = new MainWindow { Width = 1400, Height = 1400 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var rail = window.FindControl<WrapPanel>("ToolButtons")!;
        Assert.Equal(34.0, rail.Width, 1);   // tall: a single centred column

        window.Height = 380;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(68.0, rail.Width, 1);   // short: two columns
    }

    [AvaloniaFact]
    public void TheColumnsSitCentredInTheRail()
    {
        var window = new MainWindow { Width = 1400, Height = 900 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var rail = window.FindControl<WrapPanel>("ToolButtons")!;
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Center, rail.HorizontalAlignment);
    }
}
