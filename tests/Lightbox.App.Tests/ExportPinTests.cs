using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Export;

namespace Lightbox.App.Tests;

/// <summary>
/// Pinning a layer in or out of exports, from the Layers docker.
/// </summary>
/// <remarks>
/// The record and the exporter are covered elsewhere; what these guard is the part
/// that makes it a feature rather than a field — that the menu reaches the document,
/// that it is one undo step, and that the three states really are three.
/// </remarks>
public class ExportPinTests
{
    private static MainViewModel Fresh()
    {
        var vm = new MainViewModel(artist: null) { SmoothStrokes = false };
        vm.NewDocument(new NewDocumentSettings("Pin", 80, 60, 12, 72, "#ffffff", TransparentBackground: true));
        return vm;
    }

    [AvaloniaFact]
    public void TheThreeStatesAreThreeAndTheDefaultIsAbsent()
    {
        var vm = Fresh();
        var layer = vm.Doc.Scene.Layers.First(l => !l.IsBackground);

        // Absent, not false. A document that never touches this writes no key.
        Assert.Null(layer.OmitFromExport);
        Assert.False(layer.IsOmittedFromExport);
        Assert.False(layer.IsKeptInExport);

        vm.SetLayerExportPin(layer, true);
        Assert.True(vm.Doc.Scene.Layers.First(l => l.Id == layer.Id).IsOmittedFromExport);

        vm.SetLayerExportPin(layer, false);
        var kept = vm.Doc.Scene.Layers.First(l => l.Id == layer.Id);
        Assert.True(kept.IsKeptInExport);
        Assert.False(kept.IsOmittedFromExport);

        vm.SetLayerExportPin(layer, null);
        Assert.Null(vm.Doc.Scene.Layers.First(l => l.Id == layer.Id).OmitFromExport);
    }

    [AvaloniaFact]
    public void PinningIsOneUndoStepAndMarksTheDocumentDirty()
    {
        // It changes what leaves the app, so it is document state rather than a
        // preference — and an artist who pins the wrong layer wants Ctrl+Z, not a
        // second trip through the menu.
        var vm = Fresh();
        var layer = vm.Doc.Scene.Layers.First(l => !l.IsBackground);

        vm.SetLayerExportPin(layer, true);
        Assert.True(vm.ActiveTab!.IsDirty);

        vm.UndoCommand.Execute(null);
        Assert.Null(vm.Doc.Scene.Layers.First(l => l.Id == layer.Id).OmitFromExport);
    }

    [AvaloniaFact]
    public void SettingThePinItAlreadyHasDoesNothing()
    {
        // Otherwise every visit to the menu would add an undo step that undoes nothing,
        // which is how an undo stack stops being usable.
        var vm = Fresh();
        var layer = vm.Doc.Scene.Layers.First(l => !l.IsBackground);
        vm.SetLayerExportPin(layer, true);

        vm.SetLayerExportPin(layer, true);
        vm.UndoCommand.Execute(null);

        // One undo was enough to clear it, so the second call recorded nothing.
        Assert.Null(vm.Doc.Scene.Layers.First(l => l.Id == layer.Id).OmitFromExport);
    }

    [AvaloniaFact]
    public void APinnedLayerIsLeftOutOfTheSheetItWouldOtherwiseAppearIn()
    {
        // End to end from the view model, because the field being right and the export
        // reading it are two claims.
        var vm = Fresh();
        var layer = vm.Doc.Scene.Layers.First(l => !l.IsBackground);

        var dir = Path.Combine(Path.GetTempPath(), "lightbox-pin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            vm.SetLayerExportPin(layer, true);
            var result = SpriteSheetExporter.Export(vm.Doc, Path.Combine(dir, "pinned.png"));

            Assert.Contains(
                result.OmittedLayers, o => o.LayerId == layer.Id && o.Signal == BackgroundSignal.Pinned);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
