using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// The foreground/background pair, and the palette link that has to survive it.
///
/// One pair shared by the brush, the fill and the gradient: reaching for the
/// same colour in three tools and finding three answers is the thing this
/// arrangement exists to prevent.
/// </summary>
public class ActiveColorTests
{
    [AvaloniaFact]
    public void TheAppStartsOnBlackFromThePaletteOverWhite()
    {
        var vm = new MainViewModel(null);

        Assert.Equal("#000000", vm.ColorHex);
        Assert.Equal("#ffffff", vm.BackgroundColorHex);
        // Linked, not literal. The first stroke of a session is as worth being
        // recolourable as any other, and it is the one nobody goes back to
        // re-link.
        Assert.Equal(DocumentFactory.BlackSwatchId, vm.ActiveSwatchId);
    }

    [AvaloniaFact]
    public void SwappingTradesTheTwoColours()
    {
        var vm = new MainViewModel(null);

        vm.SwapColorsCommand.Execute(null);

        Assert.Equal("#ffffff", vm.ColorHex);
        Assert.Equal("#000000", vm.BackgroundColorHex);
    }

    [AvaloniaFact]
    public void SwappingKeepsThePaletteLink()
    {
        // Otherwise X quietly turns live palette colours into literals, which
        // you would not notice until a recolour did nothing.
        var vm = new MainViewModel(null);
        Assert.Equal(DocumentFactory.BlackSwatchId, vm.ActiveSwatchId);

        vm.SwapColorsCommand.Execute(null);
        Assert.Equal(DocumentFactory.WhiteSwatchId, vm.ActiveSwatchId);

        vm.SwapColorsCommand.Execute(null);
        Assert.Equal(DocumentFactory.BlackSwatchId, vm.ActiveSwatchId);
    }

    [AvaloniaFact]
    public void SwappingToAColourWithNoSwatchDropsTheLinkRatherThanFakingOne()
    {
        var vm = new MainViewModel(null);
        vm.ColorHex = "#3070b0";        // chosen by some other means; link gone
        Assert.Null(vm.ActiveSwatchId);

        vm.SwapColorsCommand.Execute(null);   // white comes forward, still linked
        Assert.Equal(DocumentFactory.WhiteSwatchId, vm.ActiveSwatchId);

        vm.SwapColorsCommand.Execute(null);   // the literal comes back, unlinked
        Assert.Equal("#3070b0", vm.ColorHex);
        Assert.Null(vm.ActiveSwatchId);
    }

    [AvaloniaFact]
    public void ResetGoesBackToBlackOverWhiteAndRelinks()
    {
        var vm = new MainViewModel(null);
        vm.ColorHex = "#3070b0";
        vm.BackgroundColorHex = "#112233";

        vm.ResetColorsCommand.Execute(null);

        Assert.Equal("#000000", vm.ColorHex);
        Assert.Equal("#ffffff", vm.BackgroundColorHex);
        Assert.Equal(DocumentFactory.BlackSwatchId, vm.ActiveSwatchId);
    }

    [AvaloniaFact]
    public void TheColourPaintedWithIsTheForegroundWhicheverToolIsActive()
    {
        // One pair, all tools. A brush stroke and a fill made after a swap
        // must agree about what colour is current.
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.SwapColorsCommand.Execute(null);

        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(60, 60, 1);
        vm.EndStroke();

        var stroke = ((PaintedFrame)vm.PaintLayer().Cels[0].Frame!).Strokes[^1];
        Assert.Equal(DocumentFactory.WhiteSwatchId, stroke.SwatchId);
    }

    // ---- the palette, inside every picker -------------------------------------

    [AvaloniaFact]
    public void EveryPickerSeesTheDocumentsPalette()
    {
        var vm = new MainViewModel(null);

        // The panel's own picker, and one built fresh for a flyout field.
        Assert.True(vm.ColorPicker.HasPalette);
        Assert.True(new ColorPickerViewModel().HasPalette);
        Assert.Contains(new ColorPickerViewModel().PaletteSwatches,
            s => s.Id == DocumentFactory.BlackSwatchId);
    }

    [AvaloniaFact]
    public void PickingFromThePaletteLinksTheSwatchRatherThanTakingItsValue()
    {
        // The distinction the whole live-palette feature rests on: a colour,
        // versus a reference to a colour. Only the second follows an edit.
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.ColorHex = "#3070b0";
        Assert.Null(vm.ActiveSwatchId);

        var white = vm.ColorPicker.PaletteSwatches.First(s => s.Id == DocumentFactory.WhiteSwatchId);
        vm.ColorPicker.PickSwatchCommand.Execute(white);

        Assert.Equal(DocumentFactory.WhiteSwatchId, vm.ActiveSwatchId);
        Assert.Equal("#ffffff", vm.ColorHex);
    }

    [AvaloniaFact]
    public void APickerBuiltWithNoDocumentSimplyShowsNoPalette()
    {
        // Which is what stops a picker in a test reaching for application
        // state that is not there.
        var saved = ColorPickerViewModel.PaletteSource;
        ColorPickerViewModel.PaletteSource = null;
        try
        {
            Assert.False(new ColorPickerViewModel().HasPalette);
            Assert.Empty(new ColorPickerViewModel().PaletteSwatches);
        }
        finally
        {
            ColorPickerViewModel.PaletteSource = saved;
        }
    }
}
