using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;


namespace Lightbox.App.Tests;

/// <summary>
/// Where the tool options bar puts things, and for which tools.
/// </summary>
/// <remarks>
/// <para>
/// <c>MainWindow.axaml</c> is the riskiest file in <c>HOTSPOTS.md</c> — most
/// commits, most lines, and for a long time <b>no test file at all</b>. Moving the
/// ⚙ beside the brush button is exactly the kind of edit that list exists to
/// warn about: it is a 492-line block move, and the thing it can silently break
/// is not the brush tool but the <i>eraser</i>, which shares the button and has no
/// brush picker of its own.
/// </para>
/// <para>
/// Asserted through the real visual tree rather than by reading the XAML, so a
/// binding that resolves to nothing or a panel whose visibility hides the button
/// is caught. Synthetic pointer input is not used — <c>CLAUDE.md</c> records that
/// a dropped click through Xvfb looks exactly like a bug.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class ToolOptionsBarTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <remarks>Wide, or the bar overflows and the ordering under test stops being visible.</remarks>
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow { Width = 2400, Height = 900 };
        window.Show();
        Pump();
        return (window, (MainViewModel)window.DataContext!);
    }

    private static void Pump() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    private static Button Gear(MainWindow w) =>
        w.GetVisualDescendants().OfType<Button>()
            .First(b => b.Content as string == "⚙"
                        && (b.GetValue(ToolTip.TipProperty) as string)?.StartsWith("All brush parameters") == true);

    private static Button Picker(MainWindow w) =>
        w.GetVisualDescendants().OfType<Button>().First(b => b.Name == "BrushPickerButton");

    /// <summary>Visible here means actually on screen, not merely present in the tree.</summary>
    private static bool OnScreen(Control c) => c.IsEffectivelyVisible;

    [AvaloniaFact]
    public void TheParametersButtonSitsRightOfTheBrushButton()
    {
        var (window, vm) = Open();
        vm.ActiveTool = ToolId.Brush;
        Pump();

        var picker = Picker(window);
        var gear = Gear(window);
        Assert.True(OnScreen(picker), "the brush button is not visible for the brush tool");
        Assert.True(OnScreen(gear), "the parameters button is not visible for the brush tool");

        var pickerRight = picker.TranslatePoint(new Point(picker.Bounds.Width, 0), window)!.Value.X;
        var gearLeft = gear.TranslatePoint(new Point(0, 0), window)!.Value.X;
        var gap = gearLeft - pickerRight;

        output.WriteLine($"brush button ends at x={pickerRight:F1}, ⚙ starts at x={gearLeft:F1}, gap {gap:F1}px");

        Assert.True(gearLeft > pickerRight, "the ⚙ is not to the right of the brush button");
        // Adjacent, not merely somewhere to the right — it used to sit past the
        // effect and shape groups at the far end of the bar.
        Assert.True(gap < 40, $"the ⚙ is {gap:F0}px from the brush button, which is not beside it");
    }

    /// <summary>
    /// The picker and its ⚙ are pinned beside the colour pair (owner's ask,
    /// 2026-08-14) — B77's rule applied one control over: which brush you are
    /// holding is shared state the way colour is, on screen whatever tool is
    /// held. This deliberately supersedes two earlier rules this file pinned:
    /// the picker used to hide for the eraser and the ⚙ used to leave with
    /// the non-paint tools. Neither is a dead control now — a preset carries
    /// its own tool and applying one puts it in your hand, and the ⚙ opens
    /// the Tool options docker, which is meaningful from anywhere.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(ToolId.Brush)]
    [InlineData(ToolId.Eraser)]
    [InlineData(ToolId.Fill)]
    [InlineData(ToolId.Select)]
    [InlineData(ToolId.Move)]
    public void TheBrushPickerAndItsGearArePinnedForEveryTool(ToolId tool)
    {
        var (window, vm) = Open();
        vm.ActiveTool = tool;
        Pump();

        Assert.True(OnScreen(Picker(window)), $"the brush preset button is off screen with {tool} held");
        Assert.True(OnScreen(Gear(window)), $"the ⚙ is off screen with {tool} held");
    }

    /// <summary>
    /// What makes the pinned picker more than a label: a preset carries its
    /// tool, so picking one from the selection tool is a way back to
    /// painting. This is <c>ApplyPreset</c>'s existing behaviour observed
    /// from a place it could not be observed before.
    /// </summary>
    [AvaloniaFact]
    public void PickingAPresetFromANonPaintToolPutsItsToolInHand()
    {
        var (_, vm) = Open();

        vm.ActiveTool = ToolId.Select;
        vm.ApplyPreset(vm.BrushPresetChoices.First(p => p.Id == "builtin-pencil"));
        Pump();

        Assert.Equal(ToolId.Brush, vm.ActiveTool);
    }

    /// <summary>
    /// B77's guard, at last: the colour block is docked left of the bar with
    /// nothing conditional on it, so it is on screen whatever tool is held —
    /// including the tools that do not use colour, which is the simpler rule
    /// the entry argues for (no per-tool table to drift).
    /// </summary>
    /// <remarks>
    /// The entry describes tests that never landed on any branch — the fix
    /// shipped, the guard did not, and <c>bugs.py check</c> has flagged the
    /// half ever since. IsEffectivelyVisible rather than IsVisible, because
    /// what broke originally was a parent's binding, not the element's own.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(ToolId.Brush)]
    [InlineData(ToolId.Eraser)]
    [InlineData(ToolId.Fill)]
    [InlineData(ToolId.Shape)]
    [InlineData(ToolId.Gradient)]
    [InlineData(ToolId.Select)]
    [InlineData(ToolId.Move)]
    [InlineData(ToolId.Picker)]
    public void TheColourSwitcherIsShownForEveryToolThatUsesColour(ToolId tool)
    {
        var (window, vm) = Open();
        vm.ActiveTool = tool;
        Pump();

        var pair = window.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Name is "ForegroundSwatch" or "BackgroundSwatch").ToList();
        output.WriteLine($"{tool}: {pair.Count} swatches, on screen {pair.Count(OnScreen)}");
        // Exactly one pair — a second one would mean the move out of the brush
        // section left the original behind.
        Assert.Equal(2, pair.Count);
        Assert.All(pair, s => Assert.True(OnScreen(s), $"{s.Name} is off screen with {tool} held"));
    }

    /// <summary>
    /// B90's control, checked where an artist meets it rather than on the view model.
    /// </summary>
    [AvaloniaFact]
    public void TheEffectStrengthControlIsBoundToFlow()
    {
        var (window, vm) = Open();
        vm.ActiveTool = ToolId.Brush;
        vm.ApplyPreset(vm.BrushPresetChoices.First(p => p.Id == "builtin-smudge"));
        Pump();

        var box = window.GetVisualDescendants().OfType<NumericUpDown>()
            .First(n => (n.GetValue(ToolTip.TipProperty) as string)?.StartsWith("How hard each dab pulls") == true);

        Assert.True(OnScreen(box), "the effect strength control is not on the bar for a smudge");
        // The step has to reach the values the shipped brushes use: Smudge 0.08,
        // Blender 0.06. At the old 0.05 neither was dialable.
        Assert.Equal(0.01m, box.Increment);

        box.Value = 0.30m;
        Pump();
        Assert.Equal(0.30, vm.BrushFlow, 1e-6);
        Assert.Equal(0.30, vm.CurrentToolSettingsForTest.Flow, 1e-6);
    }
}
