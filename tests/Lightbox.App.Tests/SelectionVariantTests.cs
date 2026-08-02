using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;

namespace Lightbox.App.Tests;

/// <summary>
/// Choosing a selection variant from the hold-list, and having the tool
/// options bar agree.
/// </summary>
[Collection("BrushState")]
public sealed class SelectionVariantTests : BrushStateIsolated
{
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow { Width = 2400, Height = 900 };
        window.Show();
        Pump(window);
        return (window, (MainViewModel)window.DataContext!);
    }

    private static void Pump(Avalonia.Layout.Layoutable? settle = null)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        settle?.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static ToggleButton SelectButton(MainWindow w) =>
        w.GetVisualDescendants().OfType<ToggleButton>()
            .First(b => b.Name == "SelectToolButton");

    [AvaloniaFact]
    public void TheHoldListSwitchesTheVariant()
    {
        var (window, vm) = Open();
        vm.ActiveTool = ToolId.Select;
        Pump(window);

        var flyout = (Flyout)SelectButton(window).ContextFlyout!;
        flyout.ShowAt(SelectButton(window));
        Pump(window);

        var polygon = ((StackPanel)flyout.Content!).Children
            .OfType<Button>()
            .First(b => (b.Content as string)!.Contains("Polygon"));
        // The command is what the click runs; a null one is the whole bug and
        // is silent on screen.
        Assert.NotNull(polygon.Command);
        // The real click path, not Command.Execute: the Click handler that
        // closes the flyout runs first, and a button torn out of the tree
        // mid-click is exactly the kind of thing that swallows the command.
        polygon.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        OnClickOf(polygon);
        Pump(window);

        Assert.Equal(SelectVariant.Polygon, vm.ActiveSelectVariant);
        Assert.Equal(ToolId.Select, vm.ActiveTool);
    }

    [AvaloniaFact]
    public void TheToolOptionsBarFollowsTheVariant()
    {
        // The radio row is bound one-way off the view model, so it has to
        // re-read on every change. It reporting the previous variant is what
        // makes the list look broken even when the switch worked.
        var (window, vm) = Open();
        vm.ActiveTool = ToolId.Select;
        Pump(window);

        foreach (var variant in (SelectVariant[])
                 [SelectVariant.Polygon, SelectVariant.Wand, SelectVariant.Box, SelectVariant.Freehand])
        {
            vm.SelectVariantOfCommand.Execute(variant);
            Pump(window);

            var checkedGlyphs = window.GetVisualDescendants().OfType<RadioButton>()
                .Where(r => r.GroupName == "selvar" && r.IsChecked == true)
                .Select(r => r.Content as string)
                .ToList();

            Assert.Single(checkedGlyphs);
            Assert.Equal(GlyphOf(variant), checkedGlyphs[0]);
        }
    }

    [AvaloniaFact]
    public void ClickingARadioInTheBarSwitchesTheVariantToo()
    {
        var (window, vm) = Open();
        vm.ActiveTool = ToolId.Select;
        Pump(window);

        var wand = window.GetVisualDescendants().OfType<RadioButton>()
            .First(r => r.GroupName == "selvar" && (r.Content as string) == "🪄");
        wand.Command!.Execute(wand.CommandParameter);
        Pump(window);

        Assert.Equal(SelectVariant.Wand, vm.ActiveSelectVariant);
    }

    /// <summary>
    /// What <c>Button.OnClick</c> does after raising Click: run the command.
    /// </summary>
    /// <remarks>
    /// Reproduced here because the interesting question is the ordering — the
    /// Click handler closes the flyout, and whether the command still runs
    /// afterwards is the whole bug.
    /// </remarks>
    private static void OnClickOf(Button b)
    {
        if (b.Command?.CanExecute(b.CommandParameter) == true) b.Command.Execute(b.CommandParameter);
    }

    private static string GlyphOf(SelectVariant v) => v switch
    {
        SelectVariant.Polygon => "⬠",
        SelectVariant.Box => "▭",
        SelectVariant.Ellipse => "◯",
        SelectVariant.Wand => "🪄",
        _ => "◌",
    };
}
