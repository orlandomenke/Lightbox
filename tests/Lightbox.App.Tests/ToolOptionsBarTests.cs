using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;

namespace Lightbox.App.Tests;

/// <summary>
/// What the tool options bar shows, and what it must not hide.
/// </summary>
/// <remarks>
/// <para>
/// <b>B77.</b> The foreground/background pair lived inside the bar's brush
/// section, under <c>IsVisible="{Binding IsBrushTool}"</c>. Flood fill and the
/// shape tools paint with the foreground colour and did not show it, so changing
/// colour for a fill meant selecting the brush, changing it, and selecting the
/// fill again.
/// </para>
/// <para>
/// Tested through the real window rather than by reading the XAML, because what
/// broke was a <em>parent's</em> visibility and not the control's own — an
/// assertion about the element would have passed the whole time it was invisible.
/// The window is opened wide for the reason <c>ColorSwatchGestureTests</c> records:
/// at the headless default the bar overflows into its ▾ and parks controls off
/// screen, which is the bar working as designed and would read here as the bug.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class ToolOptionsBarTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow { Width = 2400, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, (MainViewModel)window.DataContext!);
    }

    /// <summary>
    /// Whether a named control is genuinely on screen, ancestors included.
    /// </summary>
    /// <remarks>
    /// <c>IsVisible</c> on the control itself is not the question — it was true
    /// throughout the bug. <c>IsEffectivelyVisible</c> walks the chain, which is
    /// where the <c>IsBrushTool</c> binding sat.
    /// </remarks>
    private static bool Showing(MainWindow w, string name) =>
        w.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == name)
            is { IsEffectivelyVisible: true };

    /// <summary>Every tool that paints with the foreground colour.</summary>
    /// <remarks>
    /// Named individually rather than derived from a flag, because a flag is the
    /// table this fix exists to avoid: if some future <c>UsesColour</c> property
    /// were wrong, a test reading it would agree with it. These are the tools the
    /// report names plus the gradient, which has always shared the pair.
    /// </remarks>
    public static TheoryData<ToolId> ColourTools =>
        [ToolId.Brush, ToolId.Fill, ToolId.Shape, ToolId.Gradient];

    [AvaloniaTheory]
    [MemberData(nameof(ColourTools))]
    public void TheColourSwitcherIsShownForEveryToolThatUsesColour(ToolId tool)
    {
        var (window, vm) = Open();
        vm.ActiveTool = tool;
        Dispatcher.UIThread.RunJobs();

        var foreground = Showing(window, "ForegroundSwatch");
        var background = Showing(window, "BackgroundSwatch");
        var picker = Showing(window, "ColorPickerDropdown");
        output.WriteLine($"{tool}: foreground {foreground}, background {background}, picker {picker}");

        Assert.True(
            foreground && background,
            $"the {tool} tool paints with the foreground colour and the swatches are not on screen — "
            + "changing colour for it means selecting the brush, changing it, and coming back (B77)");
        Assert.True(picker, $"the {tool} tool cannot reach the colour picker");
    }

    /// <summary>
    /// And it is shown for the tools that do not paint colour either, because
    /// persistent is the whole point.
    /// </summary>
    /// <remarks>
    /// The reporter asked for a block that is always there, and that is the simpler
    /// rule as well as the requested one: a control that is always present needs no
    /// table of which tools use colour, and a table like that is exactly what drifts
    /// — the reason <c>ShortcutMap</c> exists one level up. So this asserts the
    /// stronger property, and the theory above says why anybody should care.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(ToolId.Eraser)]
    [InlineData(ToolId.Select)]
    [InlineData(ToolId.Move)]
    [InlineData(ToolId.Picker)]
    public void TheColourSwitcherStaysPutForEveryOtherToolToo(ToolId tool)
    {
        var (window, vm) = Open();
        vm.ActiveTool = tool;
        Dispatcher.UIThread.RunJobs();

        Assert.True(
            Showing(window, "ForegroundSwatch"),
            $"the colour pair disappeared on the {tool} tool — it is conditional again, and a condition "
            + "is the thing that has to be kept in step with every tool ever added");
    }

    /// <summary>
    /// It does not come back twice, which is the obvious way to fix this badly.
    /// </summary>
    /// <remarks>
    /// Moving the block and leaving the original in the brush section would look
    /// correct on every tool except the brush, where there would be two pairs — and
    /// the second would still be the one the brush's own layout positioned. A count
    /// catches that; a "is it visible" check does not.
    /// </remarks>
    [AvaloniaFact]
    public void ThereIsExactlyOneColourPair()
    {
        var (window, vm) = Open();
        foreach (var tool in new[] { ToolId.Brush, ToolId.Fill, ToolId.Eraser })
        {
            vm.ActiveTool = tool;
            Dispatcher.UIThread.RunJobs();
            var pairs = window.GetVisualDescendants().OfType<Control>()
                .Count(c => c.Name == "ForegroundSwatch");
            output.WriteLine($"{tool}: {pairs} foreground swatch(es)");
            Assert.Equal(1, pairs);
        }
    }

    /// <summary>
    /// The tool's own options still appear, so this did not fix colour by removing
    /// everything else.
    /// </summary>
    /// <remarks>
    /// The colour block moved out of the brush section and out of the
    /// <c>OverflowBar</c>. Both are places a careless edit takes a sibling with it,
    /// and a bar that lost the brush picker would pass every assertion above.
    /// </remarks>
    [AvaloniaFact]
    public void TheToolsOwnOptionsAreStillThere()
    {
        var (window, vm) = Open();
        vm.ActiveTool = ToolId.Brush;
        Dispatcher.UIThread.RunJobs();
        Assert.True(Showing(window, "BrushPickerButton"), "the brush picker went missing from the bar");

        vm.ActiveTool = ToolId.Fill;
        Dispatcher.UIThread.RunJobs();
        Assert.False(
            Showing(window, "BrushPickerButton"),
            "the brush picker is showing on the fill tool, so the bar has stopped being per-tool at all");
    }
}
