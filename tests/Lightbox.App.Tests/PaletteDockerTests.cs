using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;

namespace Lightbox.App.Tests;

/// <summary>
/// The palette docker end to end: a swatch is a colour you can paint with and
/// a colour you can change afterwards, and changing it has to reach the art.
/// </summary>
/// <remarks>
/// In the <c>BrushState</c> collection because it sets brush parameters, and
/// those live in a process-wide store: running beside a test that assumes
/// defaults hands it this one’s brush. It opted out until a CI run caught
/// it — the live-preview pixel check went red on a loaded machine and was
/// green on every local run, which is what this collection exists to stop.
/// </remarks>
[Collection("BrushState")]
public class PaletteDockerTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>
    /// A view model whose document has no palette. Every document now starts
    /// with black and white; these tests are about a palette the test builds,
    /// and the default one would be two extra swatches in every assertion.
    /// </summary>
    private static MainViewModel Vm()
    {
        var vm = VmLayers.PaperVm();
        vm.Doc.Palettes.Clear();
        vm.PaletteDocker.Load(vm.Doc);
        return vm;
    }

    private static SwatchRow AddSwatch(MainViewModel vm, string hex)
    {
        if (!vm.PaletteDocker.HasPalette) vm.PaletteDocker.AddPaletteCommand.Execute(null);
        vm.ColorHex = hex;
        vm.PaletteDocker.AddSwatchCommand.Execute(null);
        return vm.PaletteDocker.Swatches[^1];
    }

    private static Stroke LastStroke(MainViewModel vm)
    {
        var layer = vm.Doc.Scene.Layers[vm.ActiveLayerIndex];
        return ((Frame)layer.Cels[0].Frame!).Strokes[^1];
    }

    [AvaloniaFact]
    public void ANewDocumentStartsWithBlackAndWhiteSelectedOnBlack()
    {
        // A swatch is the difference between a stroke that carries a colour
        // and one that carries a reference, and only the second can be
        // recoloured later. Starting empty means the first hour of work can
        // never follow a palette edit.
        var vm = VmLayers.PaperVm();

        var palette = Assert.Single(vm.Doc.Palettes);
        Assert.Equal(["Black", "White"], palette.Swatches.Select(s => s.Name));
        Assert.Equal("#000000", vm.ColorHex);
        Assert.Equal(DocumentFactory.BlackSwatchId, vm.ActiveSwatchId);
        // The panel is still closed by default — having a palette and showing
        // the editor for it are different questions.
        Assert.False(vm.PaletteDockerVisible);
    }

    [AvaloniaFact]
    public void AddingASwatchTakesTheCurrentColourAndIsUndoable()
    {
        var vm = Vm();
        var row = AddSwatch(vm, "#3070b0");

        Assert.Equal("#3070b0", row.Color);
        Assert.Equal(row.Id, Assert.Single(vm.Doc.Palettes[0].Swatches).Id);

        vm.UndoCommand.Execute(null);
        Assert.Empty(vm.Doc.Palettes[0].Swatches);
        Assert.Empty(vm.PaletteDocker.Swatches);
    }

    [AvaloniaFact]
    public void SelectingASwatchPaintsWithIt()
    {
        var vm = Vm();
        vm.SmoothStrokes = false;
        var row = AddSwatch(vm, "#3070b0");
        vm.ColorHex = "#ffffff"; // some other colour, breaking the link
        Assert.Null(vm.ActiveSwatchId);

        vm.PaletteDocker.SelectedSwatch = null;
        vm.PaletteDocker.SelectedSwatch = row;
        Assert.Equal("#3070b0", vm.ColorHex);
        Assert.Equal(row.Id, vm.ActiveSwatchId);

        vm.BeginStroke(10, 10, 1);
        vm.MoveStroke(40, 40, 1);
        vm.EndStroke();

        var stroke = LastStroke(vm);
        Assert.Equal(row.Id, stroke.SwatchId);
        // The literal is recorded too, so a deleted swatch leaves the art alone.
        Assert.Equal("#3070b0", stroke.Color);
    }

    [AvaloniaFact]
    public void ChoosingAColourAnyOtherWayBreaksTheSwatchLink()
    {
        // Otherwise you would paint in one colour and record a reference to
        // another, and the stroke would jump the next time anyone touched
        // the palette.
        var vm = Vm();
        vm.SmoothStrokes = false;
        var row = AddSwatch(vm, "#3070b0");
        vm.PaletteDocker.SelectedSwatch = row;
        Assert.Equal(row.Id, vm.ActiveSwatchId);

        vm.ColorHex = "#c02040";
        Assert.Null(vm.ActiveSwatchId);

        vm.BeginStroke(10, 10, 1);
        vm.MoveStroke(40, 40, 1);
        vm.EndStroke();
        Assert.Null(LastStroke(vm).SwatchId);
    }

    [AvaloniaFact]
    public void RecolouringASwatchRepaintsTheArtThatUsedIt()
    {
        var vm = Vm();
        vm.SmoothStrokes = false;
        vm.BrushSize = 24;
        var row = AddSwatch(vm, "#ff0000");
        vm.PaletteDocker.SelectedSwatch = row;
        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(60, 60, 1);
        vm.EndStroke();

        var stroke = LastStroke(vm);
        Assert.Equal(row.Id, stroke.SwatchId);
        Assert.Equal(new SkiaSharp.SKColor(0xff, 0x00, 0x00), BrushEngine.StrokeColor(stroke));

        row.Color = "#0000ff";

        // Same stroke object, same document — only the swatch moved.
        Assert.Equal(new SkiaSharp.SKColor(0x00, 0x00, 0xff), BrushEngine.StrokeColor(stroke));
        Assert.Equal("#ff0000", stroke.Color); // the record is untouched
    }

    [AvaloniaFact]
    public void EditModeRoutesThePickerIntoTheSelectedSwatch()
    {
        var vm = Vm();
        var row = AddSwatch(vm, "#ff0000");
        vm.PaletteDocker.SelectedSwatch = row;
        vm.PaletteDocker.EditSelectedSwatch = true;

        vm.ColorHex = "#00ff00";

        Assert.Equal("#00ff00", row.Color);
        // Still linked: the artist is editing the swatch, not leaving it.
        Assert.Equal(row.Id, vm.ActiveSwatchId);
    }

    [AvaloniaFact]
    public void ARunOfColourEditsIsOneUndoStep()
    {
        // Dragging the colour wheel fires an edit per pointer event. Sixty
        // undo entries for one decision would bury the drawing history.
        var vm = Vm();
        var row = AddSwatch(vm, "#000000");
        vm.PaletteDocker.SelectedSwatch = row;
        vm.PaletteDocker.EditSelectedSwatch = true;

        for (var i = 1; i <= 20; i++) vm.ColorHex = $"#{i:x2}0000";
        Assert.Equal("#140000", row.Color);

        vm.PaletteDocker.EditSelectedSwatch = false; // ends the run
        vm.UndoCommand.Execute(null);

        Assert.Equal("#000000", vm.Doc.Palettes[0].Swatches[0].Color);
        // One step back, not twenty: the swatch is still there.
        Assert.Single(vm.Doc.Palettes[0].Swatches);
    }

    [AvaloniaFact]
    public void UndoingAStructuralEditDoesNotSwallowAnUncommittedRecolour()
    {
        // A snapshot undo replaces Doc wholesale. An uncommitted recolour that
        // is not on the stack yet would silently vanish with it.
        var vm = Vm();
        var row = AddSwatch(vm, "#000000");
        vm.PaletteDocker.SelectedSwatch = row;
        vm.PaletteDocker.EditSelectedSwatch = true;
        vm.ColorHex = "#abcdef";

        vm.UndoCommand.Execute(null); // undoes the recolour, which committed first
        Assert.Equal("#000000", vm.Doc.Palettes[0].Swatches[0].Color);

        vm.UndoCommand.Execute(null); // now the swatch itself
        Assert.Empty(vm.Doc.Palettes[0].Swatches);
    }

    [AvaloniaFact]
    public void ASwatchSurvivesUndoWithItsIdentity()
    {
        // The registry holds Swatch objects; a snapshot undo builds new ones.
        // If the registry kept the old objects, recolouring would stop working
        // after any undo.
        var vm = Vm();
        vm.SmoothStrokes = false;
        var row = AddSwatch(vm, "#ff0000");
        vm.PaletteDocker.SelectedSwatch = row;
        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(60, 60, 1);
        vm.EndStroke();
        var stroke = LastStroke(vm);

        vm.AddPaintedLayerCommand.Execute(null);
        vm.UndoCommand.Execute(null);

        var fresh = vm.PaletteDocker.Swatches.Single(s => s.Id == stroke.SwatchId);
        Assert.NotSame(row.Model, fresh.Model);
        fresh.Color = "#00ff00";
        Assert.Equal(new SkiaSharp.SKColor(0x00, 0xff, 0x00), BrushEngine.StrokeColor(stroke));
    }

    [AvaloniaFact]
    public void RemovingASwatchLeavesTheArtInTheColourItWasDrawnIn()
    {
        var vm = Vm();
        vm.SmoothStrokes = false;
        var row = AddSwatch(vm, "#3070b0");
        vm.PaletteDocker.SelectedSwatch = row;
        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(60, 60, 1);
        vm.EndStroke();
        var stroke = LastStroke(vm);

        vm.PaletteDocker.RemoveSwatchCommand.Execute(null);

        Assert.Empty(vm.Doc.Palettes[0].Swatches);
        Assert.Equal(new SkiaSharp.SKColor(0x30, 0x70, 0xb0), BrushEngine.StrokeColor(stroke));
        // And the link is properly dead: the orphaned swatch object must not
        // keep steering the art from outside the document.
        Assert.Null(PaletteRegistry.ResolveSwatch(row.Id));
        row.Model.Color = "#00ff00";
        Assert.Equal(new SkiaSharp.SKColor(0x30, 0x70, 0xb0), BrushEngine.StrokeColor(stroke));
    }

    [AvaloniaFact]
    public void SwitchingDocumentsSwitchesPalettes()
    {
        var vm = Vm();
        var first = AddSwatch(vm, "#ff0000");
        var firstTab = vm.ActiveTab!;

        var other = DocumentFactory.CreateDoc(100, 100, 12);
        other.Palettes.Clear();
        vm.OpenDocumentTab(other, null);
        Assert.Empty(vm.PaletteDocker.Palettes);
        // The other document's swatch must not resolve here, or two documents
        // open at once would recolour each other.
        Assert.Null(PaletteRegistry.ResolveSwatch(first.Id));

        vm.ActiveTab = firstTab;
        Assert.Single(vm.PaletteDocker.Palettes);
        Assert.NotNull(PaletteRegistry.ResolveSwatch(first.Id));
    }

    [AvaloniaFact]
    public void PalettesRoundTripThroughTheDocumentWithTheirLinks()
    {
        var vm = Vm();
        vm.SmoothStrokes = false;
        var row = AddSwatch(vm, "#3070b0");
        vm.PaletteDocker.SelectedSwatch = row;
        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(60, 60, 1);
        vm.EndStroke();

        var reloaded = Lightbox.Core.Serialization.DocJson.Deserialize(vm.SerializeDocument());
        var swatch = Assert.Single(Assert.Single(reloaded.Palettes).Swatches);
        var strokes = ((Frame)reloaded.Scene.Layers[vm.ActiveLayerIndex].Cels[0].Frame!).Strokes;

        Assert.Equal(row.Id, swatch.Id);
        Assert.Equal(swatch.Id, strokes[^1].SwatchId);
    }

    [AvaloniaFact]
    public void ImportedGplBecomesAPaletteOnTheDocument()
    {
        var vm = Vm();
        var path = Path.Combine(Path.GetTempPath(), $"lightbox-{Guid.NewGuid():N}.gpl");
        File.WriteAllText(path, "GIMP Palette\nName: Knight\nColumns: 4\n#\n255   0   0\tCape\n32  64 128\tArmour\n");
        try
        {
            vm.PaletteDocker.ImportGpl(path);
        }
        finally
        {
            File.Delete(path);
        }

        var palette = Assert.Single(vm.Doc.Palettes);
        Assert.Equal("Knight", palette.Name);
        Assert.Equal(2, palette.Swatches.Count);
        Assert.Equal(2, vm.PaletteDocker.Swatches.Count);
        Assert.Equal("#ff0000", vm.PaletteDocker.Swatches[0].Color);
        // And the engine can resolve them straight away, without a reload.
        Assert.NotNull(PaletteRegistry.ResolveSwatch(palette.Swatches[1].Id));
    }

    [AvaloniaFact]
    public void ExportedGplReadsBackAsTheSamePalette()
    {
        var vm = Vm();
        AddSwatch(vm, "#ff0000");
        AddSwatch(vm, "#204080");
        vm.PaletteDocker.Swatches[0].Name = "Cape";

        var path = Path.Combine(Path.GetTempPath(), $"lightbox-{Guid.NewGuid():N}.gpl");
        try
        {
            vm.PaletteDocker.ExportGpl(path);
            var back = GimpPalette.Read(File.ReadAllText(path));
            Assert.Equal(2, back.Swatches.Count);
            Assert.Equal("#ff0000", back.Swatches[0].Color);
            Assert.Equal("Cape", back.Swatches[0].Name);
            Assert.Equal("#204080", back.Swatches[1].Color);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void AnUnparseableHexIsRejectedRatherThanPaintingBlack()
    {
        var vm = Vm();
        var row = AddSwatch(vm, "#3070b0");
        row.Color = "not a colour";
        Assert.Equal("#3070b0", row.Color);
        row.Color = "0f8";
        Assert.Equal("#00ff88", row.Color);
    }

    // ---- what a wheel drag costs (B151) ----------------------------------------

    /// <summary>A document with enough strokes that scanning it is worth not doing twice.</summary>
    private static (MainViewModel Vm, SwatchRow Row) Dense(int strokes)
    {
        var vm = Vm();
        vm.SmoothStrokes = false;
        var row = AddSwatch(vm, "#ff0000");
        vm.PaletteDocker.SelectedSwatch = row;

        for (var i = 0; i < strokes; i++)
        {
            var y = 8 + i % 90;
            vm.BeginStroke(6, y, 1);
            vm.MoveStroke(60, y + 4, 1);
            vm.EndStroke();
        }
        return (vm, row);
    }

    /// <summary>
    /// <b>B151, and the property is countable rather than merely faster.</b>
    /// Dragging the colour wheel fires an edit per pointer event, and each one
    /// used to walk every stroke of every open document to find out which frames
    /// the swatch reaches. That answer cannot change during a drag — recolouring
    /// a swatch does not move a stroke's reference to it, and nothing can draw
    /// while the wheel is down — so it belongs to the run.
    /// </summary>
    [AvaloniaFact]
    public void AWheelDragScansTheDocumentOnceRatherThanOncePerEvent()
    {
        var (vm, row) = Dense(80);
        var before = vm.SwatchScans;

        // One run: the same swatch, thirty pointer events' worth.
        for (var i = 0; i < 30; i++) row.Color = $"#00{i:x2}ff";

        var scans = vm.SwatchScans - before;
        output.WriteLine($"30 edits in one run -> {scans} scan(s) of the stroke record");
        Assert.Equal(1, scans);
    }

    /// <summary>
    /// The run ends when the artist does something else, and the next drag has
    /// to look again — a scan cached past its run would repaint the frames the
    /// <em>previous</em> swatch reached.
    /// </summary>
    [AvaloniaFact]
    public void TheNextRunLooksAgain()
    {
        var (vm, row) = Dense(20);
        var second = AddSwatch(vm, "#00ff00");
        var before = vm.SwatchScans;

        row.Color = "#0000ff";
        row.Color = "#0000ee";
        // Touching a different swatch closes the previous run off.
        second.Color = "#00ee00";
        second.Color = "#00dd00";

        var scans = vm.SwatchScans - before;
        output.WriteLine($"two runs of two edits -> {scans} scan(s)");
        Assert.Equal(2, scans);
    }

    /// <summary>
    /// A structural edit can add, remove or replace the frames the cached list
    /// names, so it has to be dropped — a stale list repaints the wrong frames,
    /// or none of them, which looks exactly like the palette having stopped
    /// working.
    /// </summary>
    [AvaloniaFact]
    public void AStructuralEditDropsTheCachedFrameList()
    {
        var (vm, row) = Dense(10);
        row.Color = "#0000ff";
        var before = vm.SwatchScans;

        vm.AddPaintedLayerCommand.Execute(null);
        row.Color = "#0000ee";

        output.WriteLine($"after a structural edit -> {vm.SwatchScans - before} fresh scan(s)");
        Assert.Equal(1, vm.SwatchScans - before);
    }

    /// <summary>
    /// The end-to-end cost, because a count cannot see a scan that got slower.
    /// Deliberately a ratio rather than a millisecond figure: the charter's
    /// budgets catch order-of-magnitude regressions, and a ratio is the same
    /// number on a fast machine and a slow one.
    /// </summary>
    [AvaloniaFact]
    [Trait("Category", "Performance")]
    public void AnEventMidDragCostsAFractionOfTheFirstOne()
    {
        var (vm, row) = Dense(120);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        row.Color = "#0000ff";
        var first = clock.Elapsed.TotalMilliseconds;

        var during = new List<double>();
        for (var i = 0; i < 20; i++)
        {
            clock.Restart();
            row.Color = $"#00{i:x2}ff";
            during.Add(clock.Elapsed.TotalMilliseconds);
        }
        during.Sort();
        var median = during[during.Count / 2];

        // Both numbers, always — an assertion that passes because the first one
        // was also tiny says nothing at all.
        output.WriteLine($"first event {first:F2} ms, median of 20 mid-drag {median:F3} ms");
        Assert.True(
            median < first / 4,
            $"a mid-drag event cost {median:F3} ms against the first one's {first:F2} ms");
    }
}
