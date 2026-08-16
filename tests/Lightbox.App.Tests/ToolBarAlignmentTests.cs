using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

    // ---- B18: the bar is one line, not several ---------------------------------

    [AvaloniaFact]
    public void EveryGroupInTheBarSharesOneVerticalCentre()
    {
        // The columns lined up and the rows did not. Fluent's slider asks for
        // 44px in a 30px bar, so the groups holding one overflowed it — and an
        // overflowing child cannot be centred, it is pinned to the top. Their
        // labels then sat 7px below every group without a slider.
        var window = Open();
        var bar = OptionsBar(window);

        var centres = VisibleGroups(bar)
            .Select(g => Math.Round(Centre(g, bar), 1))
            .Distinct()
            .ToList();

        Assert.NotEmpty(centres);
        Assert.True(
            centres.Max() - centres.Min() < 1.5,
            $"groups centre at {string.Join(", ", centres)}");
    }

    [AvaloniaFact]
    public void NothingInTheBarAsksForMoreHeightThanTheBarHas()
    {
        // The condition that produced the drift, named directly: once a child
        // wants more than the bar gives it, its alignment stops being honoured
        // and no amount of VerticalAlignment="Center" will save it.
        var window = Open();
        var bar = OptionsBar(window);

        var toobig = VisibleGroups(bar)
            .Where(g => g.DesiredSize.Height > bar.Bounds.Height + 0.5)
            .Select(g => $"{g.GetType().Name} wants {g.DesiredSize.Height}")
            .ToList();

        Assert.Empty(toobig);
    }

    // ---- B19: the overlay bars are a grid of equal tiles -----------------------

    [AvaloniaFact]
    public void EveryTileInAnOverlayBarIsTheSameSquare()
    {
        // They sized to their glyph, and glyphs are not the same width: ◉ and
        // ▶ came out 25, an emoji wider still, the bar's own ▾ and ✕ 24. A
        // tile grid only reads as a grid if the tiles match.
        //
        // The zoom readout is excluded, and it is the exception this rule needed
        // all along rather than a hole in it. It shows "100%", which does not fit
        // a 26 px square — being held to one is why most of the value was
        // unreadable. It is a readout, not a tile: it keeps the tile measurement
        // across the bar so the column still lines up, and grows along the bar,
        // which OverlayBarLayoutTests checks in both orientations.
        var window = Open();

        var sizes = window.GetVisualDescendants().OfType<CanvasOverlayBar>()
            .SelectMany(b => b.GetVisualDescendants().OfType<TemplatedControl>())
            .Where(c => c is Button or ToggleButton && c.IsVisible && c.Bounds.Width > 0)
            .Where(c => c.Name != "ZoomLabel")
            .Select(c => (c.Bounds.Width, c.Bounds.Height))
            .Distinct()
            .ToList();

        Assert.NotEmpty(sizes);
        Assert.Single(sizes);
    }

    private static IEnumerable<Control> VisibleGroups(OverflowBar bar) =>
        bar.GetVisualChildren().OfType<Control>()
            .Where(c => c.IsVisible && c.Bounds.Width > 0)
            .SelectMany(c => c.GetVisualChildren().OfType<Control>().Where(g => g.IsVisible))
            // A group that overflowed the bar is parked off-screen by the
            // OverflowBar; only what is actually on the row is being judged.
            .Where(g => g.Bounds.Width > 0);

    private static double Centre(Visual group, Visual bar) =>
        (group.TranslatePoint(new Point(0, 0), bar)?.Y ?? 0) + group.Bounds.Height / 2;

    // ---- B16: the brush flyout is as tall as its page --------------------------

    [AvaloniaFact]
    public void TheGearCarriesNoFlyoutAnyMore()
    {
        // The brush parameters became the Tool options DOCKER (the owner's
        // call), so the gear is a plain command button. A flyout reappearing
        // here would mean the two hosts have quietly forked. Searched from
        // the window: the gear lives in the bar's pinned section beside the
        // preset picker now, not inside the OverflowBar.
        var button = Open().GetVisualDescendants().OfType<Button>()
            .First(b => (b.GetValue(ToolTip.TipProperty) as string)?.StartsWith("All brush parameters") == true);

        Assert.Null(button.Flyout);
        Assert.NotNull(button.Command);
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
