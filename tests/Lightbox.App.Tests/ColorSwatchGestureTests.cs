using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// The foreground/background pair in the tool options bar, as the window
/// actually wires it.
/// </summary>
/// <remarks>
/// <para>
/// These are window tests rather than view-model tests on purpose. Both of the
/// pair's handlers shipped dead — the foreground one hung its gesture on the
/// Color panel's swatch instead of the one you pressed, and the background one
/// asked for an attached flyout that had never been attached — and no view-model
/// assertion could have caught either, because the view models were fine. What
/// was wrong was the wiring, so the wiring is what is tested.
/// </para>
/// <para>
/// See B13 in <c>.claude/quality/BUGS.md</c>.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class ColorSwatchGestureTests : BrushStateIsolated
{
    /// <remarks>
    /// Wide on purpose. The tool options bar overflows into a dropdown at the
    /// headless default of 1280, which parks the colour pair off screen where
    /// no click can reach it — the bar working as designed, and nothing to do
    /// with the pair.
    /// </remarks>
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow { Width = 2400, Height = 900 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, (MainViewModel)window.DataContext!);
    }

    private static Border Swatch(MainWindow w, string name) =>
        w.GetVisualDescendants().OfType<Border>().First(b => b.Name == name);

    private static void Pump() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    /// <summary>Press and release on a control, without moving.</summary>
    private static void Click(MainWindow w, Visual target)
    {
        var at = target.TranslatePoint(new Point(4, 4), w)!.Value;
        w.MouseDown(at, Avalonia.Input.MouseButton.Left);
        w.MouseUp(at, Avalonia.Input.MouseButton.Left);
        Pump();
    }

    /// <summary>
    /// The picker a flyout is editing, once it is open.
    /// </summary>
    /// <remarks>
    /// Only once it is open: a flyout's content is not in the tree until then,
    /// so its bindings have nothing to resolve against and reading the content
    /// early reports null whatever the XAML says.
    /// </remarks>
    private static ColorPickerViewModel? PickerIn(FlyoutBase? flyout) =>
        (flyout as Flyout)?.Content is ContentControl host
            ? host.Content as ColorPickerViewModel
            : null;

    // ---- the flyouts exist at all ---------------------------------------------

    [AvaloniaFact]
    public void EachHalfOfThePairCarriesItsOwnPicker()
    {
        // The background swatch used to call ShowAttachedFlyout on a Border
        // with no attached flyout, which is a no-op that looks exactly like a
        // click that did nothing.
        var (w, vm) = Open();
        var foregroundSwatch = Swatch(w, "ForegroundSwatch");
        var backgroundSwatch = Swatch(w, "BackgroundSwatch");

        Assert.NotNull(FlyoutBase.GetAttachedFlyout(foregroundSwatch));
        Assert.NotNull(FlyoutBase.GetAttachedFlyout(backgroundSwatch));

        // And each one edits its own half. Both pointing at the same picker is
        // the failure that would still open a flyout and still look right.
        Click(w, foregroundSwatch);
        Assert.Same(vm.ColorPicker, PickerIn(FlyoutBase.GetAttachedFlyout(foregroundSwatch)));

        // Closed by hand: an open flyout light-dismisses on the next press, so
        // the second click would be spent shutting the first one.
        FlyoutBase.GetAttachedFlyout(foregroundSwatch)!.Hide();
        Pump();
        Click(w, backgroundSwatch);
        Assert.Same(vm.BackgroundPicker, PickerIn(FlyoutBase.GetAttachedFlyout(backgroundSwatch)));
    }

    [AvaloniaFact]
    public void ClickingASwatchOpensItsPicker()
    {
        var (w, _) = Open();
        var swatch = Swatch(w, "ForegroundSwatch");

        Click(w, swatch);

        Assert.True(FlyoutBase.GetAttachedFlyout(swatch)!.IsOpen);
    }

    [AvaloniaFact]
    public void ClickingTheBackgroundSwatchOpensTheBackgroundPickerAndNotTheForegroundOne()
    {
        var (w, _) = Open();
        var background = Swatch(w, "BackgroundSwatch");

        Click(w, background);

        Assert.True(FlyoutBase.GetAttachedFlyout(background)!.IsOpen);
        Assert.False(FlyoutBase.GetAttachedFlyout(Swatch(w, "ForegroundSwatch"))!.IsOpen);
    }

    [AvaloniaFact]
    public void DraggingOffASwatchDoesNotAlsoOpenItsPicker()
    {
        // The two gestures share a press. A drag that also left a flyout open
        // over the canvas would put a panel exactly where the artist was about
        // to drop the colour.
        var (w, _) = Open();
        var swatch = Swatch(w, "ForegroundSwatch");
        var from = swatch.TranslatePoint(new Point(4, 4), w)!.Value;

        w.MouseDown(from, Avalonia.Input.MouseButton.Left);
        w.MouseMove(from + new Point(40, 40));
        Pump();
        w.MouseUp(from + new Point(40, 40), Avalonia.Input.MouseButton.Left);
        Pump();

        Assert.False(FlyoutBase.GetAttachedFlyout(swatch)!.IsOpen);
    }

    [AvaloniaFact]
    public void TheDropdownBesideThePairOpensTheForegroundPicker()
    {
        // A committed click, unlike the swatch, which has to decide whether the
        // hand is about to move.
        var (w, vm) = Open();
        var dropdown = w.GetVisualDescendants().OfType<Button>()
            .First(b => b.Name == "ColorPickerDropdown");

        Assert.NotNull(dropdown.Flyout);
        Click(w, dropdown);

        Assert.True(dropdown.Flyout!.IsOpen);
        Assert.Same(vm.ColorPicker, PickerIn(dropdown.Flyout));
    }

    // ---- the background picker tracks the colour it edits ----------------------

    [AvaloniaFact]
    public void TheBackgroundPickerStartsOnTheBackgroundColour()
    {
        var vm = new MainViewModel(null);

        Assert.Equal("#ffffff", vm.BackgroundPicker.Hex);
    }

    [AvaloniaFact]
    public void SwappingAndResettingMoveTheBackgroundPickerToo()
    {
        // Otherwise the wheel is still pointing at the colour the background
        // used to be, and the next nudge of it silently reverts the swap.
        var vm = new MainViewModel(null);

        vm.SwapColorsCommand.Execute(null);
        Assert.Equal("#000000", vm.BackgroundPicker.Hex);
        Assert.Equal("#ffffff", vm.ColorPicker.Hex);

        vm.ResetColorsCommand.Execute(null);
        Assert.Equal("#ffffff", vm.BackgroundPicker.Hex);
        Assert.Equal("#000000", vm.ColorPicker.Hex);
    }

    [AvaloniaFact]
    public void EditingTheBackgroundPickerChangesTheBackgroundAndLeavesTheForegroundAlone()
    {
        var vm = new MainViewModel(null);

        vm.BackgroundPicker.SetHex("#3070b0");
        vm.BackgroundPicker.Commit();

        Assert.Equal("#3070b0", vm.BackgroundColorHex);
        Assert.Equal("#000000", vm.ColorHex);
        Assert.Equal(DocumentFactory.BlackSwatchId, vm.ActiveSwatchId);
    }

    [AvaloniaFact]
    public void PickingABackgroundFromThePaletteKeepsTheLinkThroughASwap()
    {
        // The background half records a swatch just as the foreground one does,
        // or swapping to it turns a live colour into a literal.
        var vm = new MainViewModel(null);
        vm.BackgroundPicker.SetHex("#112233");
        vm.BackgroundPicker.Commit();

        var black = vm.BackgroundPicker.PaletteSwatches.First(s => s.Id == DocumentFactory.BlackSwatchId);
        vm.BackgroundPicker.PickSwatchCommand.Execute(black);
        Assert.Equal("#000000", vm.BackgroundColorHex);

        vm.SwapColorsCommand.Execute(null);
        Assert.Equal(DocumentFactory.BlackSwatchId, vm.ActiveSwatchId);
    }

    [AvaloniaFact]
    public void TheBackgroundPickerSeesTheDocumentsPaletteAsWell()
    {
        var vm = new MainViewModel(null);

        Assert.True(vm.BackgroundPicker.HasPalette);
        Assert.Contains(vm.BackgroundPicker.PaletteSwatches, s => s.Id == DocumentFactory.WhiteSwatchId);
    }
}
