using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;

namespace Lightbox.App.Tests;

/// <summary>
/// Three things that only show up on screen: the tool options bar's columns,
/// the brush flyout's height, and what is under the paper when the paper goes.
/// </summary>
[Collection("BrushState")]
public sealed class ToolBarAlignmentTests : BrushStateIsolated
{
    private static MainWindow Open()
    {
        var window = new MainWindow { Width = 2400, Height = 900 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static OverflowBar OptionsBar(MainWindow w) =>
        w.GetVisualDescendants().OfType<OverflowBar>().First();

    // ---- B15: the bar is a column of columns -----------------------------------

    [AvaloniaFact]
    public void EveryValueFieldInTheBarIsTheSameWidth()
    {
        // They were 64, 68 and 72 for three numbers of the same shape, so the
        // row started and ended in a different place every time the tool
        // changed. A bar you read left to right has to line up.
        var bar = OptionsBar(Open());

        var widths = bar.GetVisualDescendants().OfType<NumericUpDown>()
            .Where(n => n.Classes.Contains("value"))
            .Select(n => n.Width)
            .Distinct()
            .ToList();

        Assert.NotEmpty(widths);
        Assert.Single(widths);
    }

    [AvaloniaFact]
    public void EverySliderInTheBarHasTheSameTrackLength()
    {
        var bar = OptionsBar(Open());

        var widths = bar.GetVisualDescendants().OfType<Slider>()
            .Where(s => s.Classes.Contains("param"))
            .Select(s => s.Width)
            .Distinct()
            .ToList();

        Assert.NotEmpty(widths);
        Assert.Single(widths);
    }

    [AvaloniaFact]
    public void NoValueFieldInTheBarSetsAWidthOfItsOwn()
    {
        // The class is the only place a width is decided. One control that
        // opted out is how the row drifted in the first place.
        var bar = OptionsBar(Open());

        var offenders = bar.GetVisualDescendants().OfType<NumericUpDown>()
            .Where(n => !n.Classes.Contains("value") && !double.IsNaN(n.Width))
            .ToList();

        Assert.Empty(offenders);
    }

    // ---- B16: the brush flyout is as tall as its page --------------------------

    [AvaloniaFact]
    public void TheBrushParameterFlyoutIsNotPinnedToOneHeight()
    {
        // A fixed height gave the short pages empty space and the long ones a
        // scrollbar. The cap stays, so a very long page cannot run off screen.
        var bar = OptionsBar(Open());
        var button = bar.GetVisualDescendants().OfType<Button>()
            .First(b => b.Content as string == "⚙");

        var grid = Assert.IsType<Grid>((button.Flyout as Flyout)!.Content);

        Assert.True(double.IsNaN(grid.Height), "the flyout still declares a fixed height");
        Assert.True(grid.MaxHeight > 0 && !double.IsPositiveInfinity(grid.MaxHeight));
    }

    // ---- B14: deleting the paper leaves no paper --------------------------------

    [AvaloniaFact]
    public void DeletingThePaperLeavesTransparencyRatherThanWhite()
    {
        // Without this the composite falls back to clearing to the scene's
        // colour, so the canvas goes opaque white and the deletion looks like
        // it did nothing — the one thing it must not look like.
        var vm = new MainViewModel(null);
        var paper = vm.Doc.Scene.Layers.First(l => l.IsBackground);
        Assert.False(vm.Doc.Scene.TransparentBackground);

        vm.SetLayerLocked(paper, false);
        vm.DeleteLayer(paper);

        Assert.DoesNotContain(vm.Doc.Scene.Layers, l => l.IsBackground);
        Assert.True(vm.Doc.Scene.TransparentBackground);
        Assert.Equal(SkiaSharp.SKColors.Transparent, SceneRenderer.BackgroundOf(vm.Doc.Scene));
    }

    [AvaloniaFact]
    public void PuttingThePaperBackIsUndoAndTheDocumentIsOpaqueAgain()
    {
        var vm = new MainViewModel(null);
        var paper = vm.Doc.Scene.Layers.First(l => l.IsBackground);
        vm.SetLayerLocked(paper, false);
        vm.DeleteLayer(paper);

        vm.UndoCommand.Execute(null);

        Assert.Contains(vm.Doc.Scene.Layers, l => l.IsBackground);
        Assert.False(vm.Doc.Scene.TransparentBackground);
    }

    [AvaloniaFact]
    public void DeletingAnOrdinaryLayerDoesNotTouchThePaper()
    {
        var vm = new MainViewModel(null);
        vm.AddPaintedLayerCommand.Execute(null);
        var extra = vm.ActiveLayerForIpc;

        vm.DeleteLayer(extra);

        Assert.False(vm.Doc.Scene.TransparentBackground);
        Assert.Contains(vm.Doc.Scene.Layers, l => l.IsBackground);
    }
}
