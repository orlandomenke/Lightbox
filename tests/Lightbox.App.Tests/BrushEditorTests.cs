using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// The brush editor (Q211): the brush's settings split the way Krita splits
/// them — the bar for what changes every few strokes, the Tool options panel
/// for how the tool behaves, and this popup for the preset.
/// </summary>
[Collection("BrushState")]
public sealed class BrushEditorTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static (MainWindow Window, MainViewModel Vm, BrushEditor Editor) Open()
    {
        var window = new MainWindow { Width = 2400, Height = 1000 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        vm.ActiveTool = ToolId.Brush;
        Pump();
        window.OpenBrushEditor(window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "ToolOptionsGear"));
        Pump();
        return (window, vm, window.BrushEditorForTests!);
    }

    private static void Pump()
    {
        for (var i = 0; i < 6; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static BrushEditor.Option Option(string name) => BrushEditor.Options.First(o => o.Name == name);

    private static IReadOnlyList<string> Listed(BrushEditor editor) =>
        editor.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "OptionList")
            .Items.OfType<ListBoxItem>().Select(i => ((BrushEditor.Option)i.Tag!).Name).ToList();

    [AvaloniaFact]
    public void TheOptionsAreOneListAndSmudgeOnlyForASmudgeBrush()
    {
        var (_, vm, editor) = Open();
        Assert.False(vm.IsSmudgeBrush);
        Assert.Equal(
            ["Tip", "Size", "Opacity", "Flow", "Spacing", "Shape dynamics", "Scatter", "Texture", "Colour dynamics", "Medium", "Blend"],
            Listed(editor));
    }

    [AvaloniaFact]
    public void TurningAnOptionOffLeavesTheMarkAloneAndOnPutsItBack()
    {
        // The check reads the record rather than a flag of its own: off sets the
        // option's values to the ones that leave the mark alone, on restores
        // what it had. Nothing is lost, and no new key is written to the brush.
        var (_, vm, editor) = Open();
        vm.BrushScatter = 0.4;
        vm.BrushHueJitter = 0.25;
        Pump();
        Assert.True(editor.InUse(Option("Scatter")));
        Assert.True(editor.InUse(Option("Colour dynamics")));

        editor.SetInUse(Option("Scatter"), false);
        editor.SetInUse(Option("Colour dynamics"), false);
        Pump();
        Assert.Equal(0, vm.BrushScatter);
        Assert.Equal(0, vm.BrushHueJitter);
        Assert.False(editor.InUse(Option("Scatter")));

        editor.SetInUse(Option("Scatter"), true);
        editor.SetInUse(Option("Colour dynamics"), true);
        Pump();
        Assert.Equal(0.4, vm.BrushScatter, 6);
        Assert.Equal(0.25, vm.BrushHueJitter, 6);
    }

    [AvaloniaFact]
    public void WhatAnUntickedOptionHeldSurvivesASwitchToTheEraserAndBack()
    {
        // Found by the adversary review: the first version dropped what it kept
        // on any change of brush. A nudged brush stays nudged across a switch,
        // and so must what its unticked options held.
        var (_, vm, editor) = Open();
        vm.BrushScatter = 0.6;
        Pump();
        editor.SetInUse(Option("Scatter"), false);
        Assert.Equal(0, vm.BrushScatter);

        vm.ActiveTool = ToolId.Eraser;
        Pump();
        vm.ActiveTool = ToolId.Brush;
        Pump();

        editor.SetInUse(Option("Scatter"), true);
        Assert.Equal(0.6, vm.BrushScatter, 6);
    }

    [AvaloniaFact]
    public void TurningShapeDynamicsOffAlsoTakesItsPressureCurveAndOnBringsTheCurveBack()
    {
        var (_, vm, editor) = Open();
        var curve = new ResponseCurve { Points = [new(0, 0.3), new(0.5, 0.8), new(1, 1)] };
        vm.SetBrushCurve(BrushDynamic.Roundness, curve);
        Pump();
        Assert.True(editor.InUse(Option("Shape dynamics")));

        editor.SetInUse(Option("Shape dynamics"), false);
        Assert.False(vm.BrushDrives(BrushDynamic.Roundness));

        editor.SetInUse(Option("Shape dynamics"), true);
        Assert.True(vm.BrushDrives(BrushDynamic.Roundness));
        Assert.Equal(curve.Points, vm.BrushCurve(BrushDynamic.Roundness).Points);
    }

    [AvaloniaFact]
    public void ALockedOptionCannotBeTurnedOff()
    {
        var (_, vm, editor) = Open();
        var size = vm.BrushSize;
        editor.SetInUse(Option("Size"), false);
        Assert.Equal(size, vm.BrushSize);
        Assert.True(editor.InUse(Option("Size")) || Option("Size").Locked);
    }

    [AvaloniaTheory]
    [InlineData("Size", "CurvesSize", 1)]
    [InlineData("Flow", "CurvesFlow", 1)]
    [InlineData("Tip", "CurvesTip", 1)]
    [InlineData("Scatter", "CurvesScatter", 1)]
    [InlineData("Opacity", null, 0)]
    public void APressureCurveSitsBesideTheValueItDrives(string option, string? host, int curves)
    {
        // The old Pen pressure page kept every curve together, away from what
        // each one moved. Each option now carries its own.
        var (_, _, editor) = Open();
        editor.SelectOption(Option(option));
        Pump();
        Assert.Equal(Option(option).Panel, editor.ShownPanel);
        var editors = host is null
            ? []
            : editor.GetVisualDescendants().OfType<StackPanel>().First(p => p.Name == host)
                .GetVisualDescendants().OfType<CurveEditor>().ToList();
        Assert.Equal(curves, editors.Count);
    }

    [AvaloniaFact]
    public void ReloadPutsTheSavedBrushBack()
    {
        var (_, vm, editor) = Open();
        vm.ApplyPreset(vm.BrushPresetChoices.First(p => p.Settings.Kind == BrushKind.Paint));
        vm.ActiveTool = ToolId.Brush;
        Pump();
        output.WriteLine($"on “{vm.SelectedBrushPreset?.Name}”, modified {vm.BrushIsModified}");
        var saved = vm.BrushSpacing;
        vm.BrushSpacing = saved + 0.3;
        Pump();
        Assert.True(vm.BrushIsModified);

        var reload = editor.GetVisualDescendants().OfType<Button>().First(b => b.Name == "ReloadButton");
        Assert.True(reload.IsEnabled);
        reload.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Pump();

        Assert.Equal(saved, vm.BrushSpacing, 6);
        Assert.False(vm.BrushIsModified);
    }

    /// <summary>
    /// The owner's instruction for this build: "be careful around spacing, as in
    /// the gallery certain fields and toggles are cut off." Every option, every
    /// control in it, must end inside the panel it scrolls in — measured, not
    /// eyeballed — and every checkbox must be as tall as its box.
    /// </summary>
    [AvaloniaFact]
    public void NothingInAnyOptionIsCutOff()
    {
        var (window, vm, editor) = Open();
        var viewer = editor.GetVisualDescendants().OfType<ScrollViewer>()
            .First(s => s.Content is Panel { Name: "OptionPanels" });
        var bad = new List<string>();
        foreach (var option in BrushEditor.Options.Where(o => o.Shown?.Invoke(vm) ?? true))
        {
            editor.SelectOption(option);
            Pump();
            var panel = editor.GetVisualDescendants().OfType<Control>().First(c => c.Name == option.Panel);
            foreach (var control in panel.GetVisualDescendants().OfType<Control>()
                         .Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0
                                     && c is NumericUpDown or Slider or ComboBox or CheckBox or Button or TextBox or CurveEditor or ColorField))
            {
                var right = control.TranslatePoint(new Point(control.Bounds.Width, 0), viewer)!.Value.X;
                if (right > viewer.Viewport.Width + 0.5)
                {
                    bad.Add($"{option.Name}: {control.GetType().Name} ends at {right:F1}, past the panel's {viewer.Viewport.Width:F1}");
                }
                if (control is CheckBox check && check.Bounds.Height < 20)
                {
                    bad.Add($"{option.Name}: a checkbox is {check.Bounds.Height:F1} tall, shorter than its box");
                }
            }
        }
        foreach (var line in bad) output.WriteLine(line);
        Assert.Empty(bad);
        window.Close();
    }
}

/// <summary>The bar's half of Q211, and the shortcuts.</summary>
[Collection("BrushState")]
public sealed class BrushBarTests : BrushStateIsolated
{
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow { Width = 2400, Height = 900 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, (MainViewModel)window.DataContext!);
    }

    [AvaloniaFact]
    public void TheEraserToggleSwitchesBetweenTheBrushAndTheEraser()
    {
        var (_, vm) = Open();
        vm.ActiveTool = ToolId.Brush;
        Assert.False(vm.EraserMode);
        vm.EraserMode = true;
        Assert.Equal(ToolId.Eraser, vm.ActiveTool);
        vm.EraserMode = false;
        Assert.Equal(ToolId.Brush, vm.ActiveTool);
    }

    [AvaloniaFact]
    public void HardnessAndTheStabiliserLeftTheBarAndFlowArrived()
    {
        // Krita's bar holds what changes every few strokes. Hardness went to the
        // editor's Tip, the stabiliser to the Tool options panel.
        var (window, vm) = Open();
        vm.ActiveTool = ToolId.Brush;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var bar = window.GetVisualDescendants().OfType<OverflowBar>().First(b => b.Name == "QuickToolOptions");
        var words = bar.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Flow", words);
        Assert.DoesNotContain("Hardness", words);
        Assert.DoesNotContain("Stabilizer", words);
    }

    [Fact]
    public void TheEditorAndThePresetsCanBeGivenAKey()
    {
        // Registered, so Configure's editor can find them and a remap applies.
        // No default: Krita's F5 is this app's project refresh.
        var map = new Lightbox.App.Services.ShortcutMap();
        Assert.Contains(map.Definitions, d => d.Id == "brush.editor" && d.Default is null);
        Assert.Contains(map.Definitions, d => d.Id == "brush.presets" && d.Default is null);
    }
}
