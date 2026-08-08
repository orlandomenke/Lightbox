using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// "Add to palette", from whichever wheel the artist happens to be looking at.
/// </summary>
/// <remarks>
/// The point of the button is that finding a colour and keeping it are one
/// gesture. Everything here is about the second half of that being complete:
/// the colour lands somewhere real, it lands in the palette the artist can see,
/// it is undoable in one step, and the stroke that follows references it rather
/// than copying it.
/// </remarks>
public class AddToPaletteTests
{
    /// <summary>A view model whose document has no palette at all.</summary>
    private static MainViewModel Empty()
    {
        var vm = new MainViewModel(null);
        vm.Doc.Palettes.Clear();
        vm.PaletteDocker.Load(vm.Doc);
        return vm;
    }

    [AvaloniaFact]
    public void TheColourOnTheWheelGoesIntoTheSelectedPalette()
    {
        var vm = new MainViewModel(null);
        var palette = vm.PaletteDocker.SelectedPalette!;
        var before = palette.Swatches.Count;

        vm.ColorPicker.SetHex("#3070b0");
        vm.ColorPicker.AddToPaletteCommand.Execute(null);

        Assert.Equal(before + 1, vm.Doc.Palettes.Single(p => p.Id == palette.Id).Swatches.Count);
        Assert.Equal("#3070b0", palette.Swatches[^1].Color);
    }

    [AvaloniaFact]
    public void WithNoPaletteYetOneIsMade()
    {
        // The case the button is most useful in, and the one it would be
        // easiest to refuse. Refusing means going to find the palette docker,
        // creating a palette and coming back — by which time the wheel has
        // moved on.
        var vm = Empty();
        Assert.Empty(vm.Doc.Palettes);

        vm.ColorPicker.SetHex("#c04a2f");
        vm.ColorPicker.AddToPaletteCommand.Execute(null);

        var palette = Assert.Single(vm.Doc.Palettes);
        Assert.Equal("#c04a2f", Assert.Single(palette.Swatches).Color);
        // And the docker is showing it, not an empty list next to a palette
        // that exists.
        Assert.Same(palette, vm.PaletteDocker.SelectedPalette);
        Assert.Single(vm.PaletteDocker.Swatches);
    }

    [AvaloniaFact]
    public void MakingThePaletteAndAddingTheColourIsOneUndoStep()
    {
        // Undoing halfway would leave an empty palette nobody asked for.
        var vm = Empty();
        vm.ColorPicker.SetHex("#c04a2f");
        vm.ColorPicker.AddToPaletteCommand.Execute(null);

        vm.UndoCommand.Execute(null);

        Assert.Empty(vm.Doc.Palettes);
    }

    [AvaloniaFact]
    public void TheStrokeThatFollowsReferencesTheNewSwatchRatherThanCopyingIt()
    {
        // The whole reason to keep a colour. If the stroke recorded the literal
        // instead, the one colour you deliberately wrote down would be the one
        // a later palette edit could not reach.
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.ColorHex = "#3070b0";
        Assert.Null(vm.ActiveSwatchId);

        vm.ColorPicker.AddToPaletteCommand.Execute(null);

        var swatch = vm.PaletteDocker.SelectedPalette!.Swatches[^1];
        Assert.Equal(swatch.Id, vm.ActiveSwatchId);

        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(60, 60, 1);
        vm.EndStroke();
        Assert.Equal(swatch.Id, ((Frame)vm.PaintLayer().Cels[0].Frame!).Strokes[^1].SwatchId);
    }

    [AvaloniaFact]
    public void AddingTheBackgroundColourDoesNotChangeWhatTheBrushIsLoadedWith()
    {
        // The palette docker's own selection means "paint with this", so the
        // naive wiring — select the new row — repaints the foreground whichever
        // half of the pair the colour came from.
        var vm = new MainViewModel(null);
        vm.BackgroundColorHex = "#c04a2f";

        vm.BackgroundPicker.AddToPaletteCommand.Execute(null);

        var swatch = vm.PaletteDocker.SelectedPalette!.Swatches[^1];
        Assert.Equal("#c04a2f", swatch.Color);
        Assert.Equal("#000000", vm.ColorHex);
        Assert.Equal(DocumentFactory.BlackSwatchId, vm.ActiveSwatchId);
    }

    [AvaloniaFact]
    public void ItGoesToTheSelectedPaletteAndNotSimplyTheFirstOne()
    {
        var vm = Empty();
        vm.PaletteDocker.AddPaletteCommand.Execute(null);
        var first = vm.PaletteDocker.SelectedPalette!;
        vm.PaletteDocker.AddPaletteCommand.Execute(null);
        var second = vm.PaletteDocker.SelectedPalette!;
        Assert.NotSame(first, second);

        vm.ColorPicker.SetHex("#3070b0");
        vm.ColorPicker.AddToPaletteCommand.Execute(null);

        Assert.Empty(vm.Doc.Palettes.Single(p => p.Id == first.Id).Swatches);
        Assert.Single(vm.Doc.Palettes.Single(p => p.Id == second.Id).Swatches);
    }

    [AvaloniaFact]
    public void EveryPickerCanKeepAColour_NotOnlyTheOneInThePanel()
    {
        // A picker built for a flyout has no owner, so nothing links to the new
        // swatch — but the palette still gains the colour, which is what the
        // artist asked for.
        var vm = new MainViewModel(null);
        var flyout = new ColorPickerViewModel();
        Assert.True(flyout.CanAddToPalette);
        flyout.SetHex("#118844");

        flyout.AddToPaletteCommand.Execute(null);

        Assert.Contains(vm.PaletteDocker.SelectedPalette!.Swatches, s => s.Color == "#118844");
        // Nobody was listening, so the brush is where it was.
        Assert.Equal(DocumentFactory.BlackSwatchId, vm.ActiveSwatchId);
    }

    // ---- choosing where it goes -----------------------------------------------

    [AvaloniaFact]
    public void EveryPaletteIsOfferedByItsPath()
    {
        // A flat list of bare names in a document that has been filed is a
        // list of guesses.
        var vm = Empty();
        vm.PaletteDocker.AddFolderCommand.Execute(null);
        vm.PaletteDocker.SelectedNode!.Name = "Characters";
        vm.PaletteDocker.AddPaletteCommand.Execute(null);
        vm.PaletteDocker.SelectedPalette!.Name = "Armour";
        vm.PaletteDocker.Load(vm.Doc);

        Assert.Contains(vm.ColorPicker.PaletteTargets, t => t.Label == "Characters / Armour");
    }

    [AvaloniaFact]
    public void ANamedTargetBeatsTheSelection()
    {
        var vm = Empty();
        vm.PaletteDocker.AddPaletteCommand.Execute(null);
        var first = vm.PaletteDocker.SelectedPalette!;
        vm.PaletteDocker.AddPaletteCommand.Execute(null);
        var second = vm.PaletteDocker.SelectedPalette!;

        vm.ColorPicker.SetHex("#3070b0");
        var target = vm.ColorPicker.PaletteTargets.First(t => t.Id == first.Id);
        vm.ColorPicker.AddToNamedPaletteCommand.Execute(target);

        Assert.Single(vm.Doc.Palettes.Single(p => p.Id == first.Id).Swatches);
        Assert.Empty(vm.Doc.Palettes.Single(p => p.Id == second.Id).Swatches);
    }

    // ---- duplicates ---------------------------------------------------------------

    [AvaloniaFact]
    public void AnAnonymousPickerRefusesAColourThePaletteAlreadyHas()
    {
        // A palette full of near-identical entries is a palette nobody can
        // use, and the same colour arriving twice is almost always a slip.
        var vm = Empty();
        var flyout = new ColorPickerViewModel();   // a gradient stop, say
        Assert.Equal(PaletteAddIntent.Deduplicate, flyout.AddIntent);
        flyout.SetHex("#3070b0");
        flyout.AddToPaletteCommand.Execute(null);
        var palette = vm.PaletteDocker.SelectedPalette!;
        Assert.Single(palette.Swatches);

        flyout.AddToPaletteCommand.Execute(null);

        Assert.Single(palette.Swatches);
    }

    [AvaloniaFact]
    public void RefusingSaysSoRatherThanDoingNothing()
    {
        // A button that does nothing is indistinguishable from a button that
        // is broken, and this one is refusing on purpose.
        var vm = Empty();
        var flyout = new ColorPickerViewModel();
        flyout.SetHex("#3070b0");
        flyout.AddToPaletteCommand.Execute(null);

        flyout.AddToPaletteCommand.Execute(null);

        Assert.Contains("already", vm.PaletteDocker.Status);
        // And in the status bar too — the docker's own line is easy to miss
        // with the wheel open over the canvas.
        Assert.Contains("already", vm.AiStatus);
        Assert.Contains("Duplicate", vm.PaletteDocker.Status);
    }

    [AvaloniaFact]
    public void TheWheelInThePalettePanelIsTakenAtFaceValue()
    {
        // Somebody working in the palette who asks for a second copy wants
        // one, usually to file the two under different folders.
        var vm = Empty();
        var inPanel = new ColorPickerViewModel { AddIntent = PaletteAddIntent.Allow };
        inPanel.SetHex("#3070b0");
        inPanel.AddToPaletteCommand.Execute(null);

        inPanel.AddToPaletteCommand.Execute(null);

        Assert.Equal(2, vm.PaletteDocker.SelectedPalette!.Swatches.Count);
    }

    [AvaloniaFact]
    public void TheForegroundPickerAdoptsTheSwatchThatIsAlreadyThere()
    {
        // The point of adding is to start painting with a live colour, and the
        // swatch already in the palette does that. A second one would hand
        // back the literal you were trying to get away from.
        var vm = new MainViewModel(null);
        var black = vm.PaletteDocker.SelectedPalette!.Swatches
            .First(s => s.Id == DocumentFactory.BlackSwatchId);
        var before = vm.PaletteDocker.SelectedPalette.Swatches.Count;
        vm.ColorHex = "#3070b0";                 // link dropped
        Assert.Null(vm.ActiveSwatchId);
        vm.ColorPicker.SetHex(black.Color);

        vm.ColorPicker.AddToPaletteCommand.Execute(null);

        Assert.Equal(before, vm.PaletteDocker.SelectedPalette.Swatches.Count);
        Assert.Equal(black.Id, vm.ActiveSwatchId);
        Assert.Contains("selected it instead", vm.AiStatus);
    }

    [AvaloniaFact]
    public void TheAdoptedSwatchIsLiveSoRecolouringReachesTheArt()
    {
        // The whole reason adopting beats refusing: the stroke that follows
        // references the swatch, so a later palette edit repaints it.
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        var black = vm.PaletteDocker.SelectedPalette!.Swatches
            .First(s => s.Id == DocumentFactory.BlackSwatchId);
        vm.ColorHex = "#3070b0";
        vm.ColorPicker.SetHex(black.Color);
        vm.ColorPicker.AddToPaletteCommand.Execute(null);

        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(60, 60, 1);
        vm.EndStroke();

        Assert.Equal(black.Id, ((Frame)vm.PaintLayer().Cels[0].Frame!).Strokes[^1].SwatchId);
    }

    [AvaloniaFact]
    public void TheBackgroundPickerAdoptsToo()
    {
        var vm = new MainViewModel(null);
        var white = vm.PaletteDocker.SelectedPalette!.Swatches
            .First(s => s.Id == DocumentFactory.WhiteSwatchId);
        var before = vm.PaletteDocker.SelectedPalette.Swatches.Count;
        vm.BackgroundPicker.SetHex(white.Color);

        vm.BackgroundPicker.AddToPaletteCommand.Execute(null);

        Assert.Equal(before, vm.PaletteDocker.SelectedPalette.Swatches.Count);
        // And swapping brings the link forward, which is what makes it live.
        vm.SwapColorsCommand.Execute(null);
        Assert.Equal(white.Id, vm.ActiveSwatchId);
    }

    [AvaloniaFact]
    public void ADuplicateIsJudgedAgainstTheTargetPaletteOnly()
    {
        // Two palettes are allowed the same colour; it is two entries in one
        // palette that nobody can tell apart.
        var vm = Empty();
        vm.PaletteDocker.AddPaletteCommand.Execute(null);
        var first = vm.PaletteDocker.SelectedPalette!;
        vm.ColorHex = "#3070b0";
        vm.PaletteDocker.AddSwatchCommand.Execute(null);

        vm.PaletteDocker.AddPaletteCommand.Execute(null);
        var second = vm.PaletteDocker.SelectedPalette!;
        var flyout = new ColorPickerViewModel();
        flyout.SetHex("#3070b0");
        flyout.AddToPaletteCommand.Execute(null);

        Assert.Single(vm.Doc.Palettes.Single(p => p.Id == first.Id).Swatches);
        Assert.Single(vm.Doc.Palettes.Single(p => p.Id == second.Id).Swatches);
    }

    [AvaloniaFact]
    public void DuplicateSwatchMakesAnIndependentCopy()
    {
        // The deliberate path, and what lets the wheel refuse. A new id, so
        // recolouring the copy leaves art painted with the original alone —
        // which is the whole point of wanting two.
        var vm = Empty();
        vm.PaletteDocker.AddPaletteCommand.Execute(null);
        vm.ColorHex = "#3070b0";
        vm.PaletteDocker.AddSwatchCommand.Execute(null);
        var original = vm.PaletteDocker.Swatches[^1];
        original.Name = "Sky";

        vm.PaletteDocker.DuplicateSwatchCommand.Execute(null);

        var swatches = vm.PaletteDocker.SelectedPalette!.Swatches;
        Assert.Equal(2, swatches.Count);
        Assert.NotEqual(swatches[0].Id, swatches[1].Id);
        Assert.Equal(swatches[0].Color, swatches[1].Color);
        Assert.Equal("Sky copy", swatches[1].Name);
        // And the copy is what is selected, so it can be renamed straight away.
        Assert.Equal(swatches[1].Id, vm.PaletteDocker.SelectedSwatch!.Id);
    }

    // ---- moving a swatch between palettes ---------------------------------------

    [AvaloniaFact]
    public void ASwatchDraggedToAnotherPaletteKeepsItsIdAndTheArtFollowsIt()
    {
        // A move, not a copy. Minting a new id would cut every stroke painted
        // with the swatch loose from the colour, silently.
        var vm = Empty();
        vm.PaletteDocker.AddPaletteCommand.Execute(null);
        var from = vm.PaletteDocker.SelectedPalette!;
        vm.ColorHex = "#3070b0";
        vm.PaletteDocker.AddSwatchCommand.Execute(null);
        var row = vm.PaletteDocker.Swatches[^1];
        var id = row.Id;

        vm.PaletteDocker.AddPaletteCommand.Execute(null);
        var into = vm.PaletteDocker.SelectedPalette!;
        vm.PaletteDocker.SelectedPalette = vm.PaletteDocker.Palettes.First(p => p.Id == from.Id);
        row = vm.PaletteDocker.Swatches.First(s => s.Id == id);

        Assert.True(vm.PaletteDocker.MoveSwatch(row, into));

        Assert.Empty(vm.Doc.Palettes.Single(p => p.Id == from.Id).Swatches);
        var moved = Assert.Single(vm.Doc.Palettes.Single(p => p.Id == into.Id).Swatches);
        Assert.Equal(id, moved.Id);
        Assert.Equal("#3070b0", moved.Color);
    }

    [AvaloniaFact]
    public void ASwatchDroppedOnItsOwnPaletteChangesNothing()
    {
        var vm = Empty();
        vm.PaletteDocker.AddPaletteCommand.Execute(null);
        var palette = vm.PaletteDocker.SelectedPalette!;
        vm.ColorHex = "#3070b0";
        vm.PaletteDocker.AddSwatchCommand.Execute(null);

        Assert.False(vm.PaletteDocker.MoveSwatch(vm.PaletteDocker.Swatches[^1], palette));

        Assert.Single(vm.Doc.Palettes.Single(p => p.Id == palette.Id).Swatches);
    }

    [AvaloniaFact]
    public void APickerWithNoDocumentBehindItSimplyCannotAddOne()
    {
        // Which is what keeps the button off a picker in a test harness rather
        // than having it reach for application state that is not there.
        var savedSink = ColorPickerViewModel.PaletteSink;
        ColorPickerViewModel.PaletteSink = null;
        try
        {
            var picker = new ColorPickerViewModel();
            Assert.False(picker.CanAddToPalette);
            picker.AddToPaletteCommand.Execute(null); // no throw
        }
        finally
        {
            ColorPickerViewModel.PaletteSink = savedSink;
        }
    }
}
